namespace SharpUI;

/// <summary>滑块(T3):轨道 4px(交叉居中胶囊)+ thumb 8×16(横)/16×8(纵);CapturesDrag=true 拖拽捕获控件。
/// 指针:Down 命中(轨道或 thumb)即捕获且"thumb 就位"(值 = f(位置)),Motion 值跟随(出界钳制),Up 释放 ——
/// 捕获原语(Interaction,见 InputState.CapturedId)承担 Down 捕获/Motion+Up 旁路 HitTest/悬停锁定/释放;
/// 本元素在 Draw 首行经 ConsumeInput 消费当帧 InputState(App 时序:Update 上帧树 → 构建新树 → Arrange →
/// Draw;捕获以稳定 id 跨树重建持续)。Down 落轨道即就位并续拖:元素级捕获(捕获原语以元素为单位),
/// thumb 归属经 Down 帧锁存分流,双 thumb 行为等效各自捕获。
/// Range(链式 .Range):双 thumb 各自回调;活动 thumb 于 Down 帧按"映射值距两 thumb 孰近"锁存
/// (StateStore,跨树重建持久 + 键盘续接"最后操作";线性映射下几何距 ∝ 值距,平局取 min)—— 无锁存时
/// 单帧大步拖越两 thumb 中点会按最近翻面劫持另一 thumb(调用方值滞后一帧)。交叉钳制
/// min ≤ max−gap / max ≥ min+gap(gap = step>0 ? step : 0),越界不产生回调(不变量优先)。
/// 键盘:聚焦 + Left/Up 减 / Right/Down 加(两对键横纵通收)、Home=min / End=max(Range 作用于活动
/// thumb,交叉钳制兜底);Range 模式下 Space 翻转活动 thumb 锁存 0↔1(单 thumb 模式不消费);
/// ±step 或 ±量程/20(连续)—— 经 IKeySteerable 在 FocusManager.HandleKeys 消费,
/// 不经 IActivatable(值微调/切换非激活语义)。
/// 值态由调用方持有(树每帧重建,ctor 参数即当前值);回调仅值实际变化(钳制+吸附后)触发,双 thumb 各自回调。
/// Enabled=false:灰视觉 + 不响应(CapturesDrag/Focusable 随 Enabled 置位,指针捕获/键盘消费统一门)。
/// Draw 零新分配:几何纯结构体运算 + 静态色板 + ActiveKey 按 ResolvedId 实例级缓存(Range 活动 thumb 外圈
/// 以比 thumb 大 2px 的底层圆角矩形绘 1px 0x93C5FD 亮色描边)。</summary>
public sealed class SliderElement : PanelElement, IKeySteerable
{
    /// <summary>thumb 主轴长(规格常量,布局/绘制/测试坐标换算共用):横 = 宽 8 / 纵 = 高 8。</summary>
    public const float ThumbMain = 8f;

    /// <summary>thumb 交叉轴长:横 = 高 16 / 纵 = 宽 16。</summary>
    public const float ThumbCross = 16f;

    const float TrackSize = 4f;          // 轨道厚(交叉向),主向居中
    const float TrackRadius = 2f;        // 轨道圆角(半厚,胶囊)
    const float ThumbRadius = 3f;
    const float ActiveThumbOutline = 1f; // Range 活动 thumb 外圈 1px 描边(比 thumb 大 2px 的底层圆角矩形)
    const float KeyboardFraction = 20f;  // 连续模式键盘步长 = 量程 / 20
    const float DefaultMain = 140f;      // 自测默认主轴长(交叉向留 ThumbCross 余量)
    const float DefaultCross = 20f;

    // 色板(硬编码,同 Checkbox/Radio 纪律):已选段常态 0x0C63E4 / 悬停 0x3B82F6 / 聚焦 0x60A5FA
    // (同 Button/Checkbox 蓝系);Range 活动 thumb 外圈 1px 亮色描边 0x93C5FD;轨道余段 0x1A1A22;
    // 禁用轨道 0x24242A、禁用 thumb 0x6A6A74;thumb 常白。
    static readonly Color32 Accent = Color32.FromRgb(0x0C63E4);
    static readonly Color32 AccentHover = Color32.FromRgb(0x3B82F6);
    static readonly Color32 AccentFocus = Color32.FromRgb(0x60A5FA);
    static readonly Color32 ActiveThumbBorder = Color32.FromRgb(0x93C5FD);
    static readonly Color32 TrackRest = Color32.FromRgb(0x1A1A22);
    static readonly Color32 DisabledFill = Color32.FromRgb(0x24242A);
    static readonly Color32 DisabledThumb = Color32.FromRgb(0x6A6A74);

    readonly float _min;
    readonly float _max;
    readonly Action<float>? _onMinChanged;
    Action<float>? _onMaxChanged;
    float _minValue;
    float _maxValue;
    float _step;
    bool _vertical;
    bool _isRange;
    bool _enabled = true;
    string? _activeKeyId;
    string? _activeKey;

    public SliderElement(float min, float max, float value, Action<float>? onChanged)
    {
        _min = min;
        _max = max;
        _minValue = value;
        _onMinChanged = onChanged;
        IsHitTarget = true;
        Focusable = true;
    }

    /// <summary>吸附步长;0 = 连续(默认),负值归零。</summary>
    public SliderElement Step(float step)
    {
        _step = MathF.Max(0f, step);
        return this;
    }

    /// <summary>纵向(默认横向);上 = 大。</summary>
    public SliderElement Vertical()
    {
        _vertical = true;
        return this;
    }

    /// <summary>双 thumb 区间:ctor 值/回调为 min 侧,maxValue/onMaxChanged 为 max 侧。</summary>
    public SliderElement Range(float maxValue, Action<float>? onMaxChanged)
    {
        _isRange = true;
        _maxValue = maxValue;
        _onMaxChanged = onMaxChanged;
        return this;
    }

    /// <summary>false = 禁用:灰视觉 + 不响应,并同步退出焦点序与捕获声明。</summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Focusable = value; }
    }

    /// <summary>拖拽捕获声明:仅启用时捕获(禁用不入捕获,Interaction 不为其锁定悬停)。</summary>
    internal override bool CapturesDrag => _enabled;

    protected internal override SizeF MeasureSelf(SizeF available) =>
        _vertical ? new SizeF(DefaultCross, DefaultMain) : new SizeF(DefaultMain, DefaultCross);

    // ---- 键盘(IKeySteerable,经 FocusManager.HandleKeys)----

    /// <summary>方向键/Home/End 微调;禁用拒消费(返回 false,回落既有路由)。消费即记"最后操作"
    /// (Range:StateStore 活动.thumb),值管线后仅变化回调。</summary>
    public bool HandleKey(Key key)
    {
        if (!_enabled) return false;
        switch (key)
        {
            // 方向:Right/Up 增、Left/Down 减(两对键横纵通收,HTML range 同款)——
            // 纵向"上=大"下 Up 即增;Element.Key(string 属性)遮蔽枚举名,须 SharpUI.Key 限定。
            case SharpUI.Key.Left:
            case SharpUI.Key.Down:
                Operate(-KeyboardStep());
                return true;
            case SharpUI.Key.Right:
            case SharpUI.Key.Up:
                Operate(KeyboardStep());
                return true;
            case SharpUI.Key.Home:
                Operate(_min, absolute: true);
                return true;
            case SharpUI.Key.End:
                Operate(_max, absolute: true);
                return true;
            case SharpUI.Key.Space:
                if (!_isRange) return false;
                WriteThumb(1 - LastThumb());
                return true;
            default:
                return false;
        }
    }

    float KeyboardStep()
    {
        float span = MathF.Max(0f, _max - _min);
        return _step > 0f ? _step : span / KeyboardFraction;
    }

    /// <summary>键盘操作活动 thumb(单 thumb 恒 0):absolute = 直取 Home/End 端点,否则相对当前值加减。</summary>
    void Operate(float operand, bool absolute = false)
    {
        int thumb = _isRange ? LastThumb() : 0;
        if (_isRange) WriteThumb(thumb);
        float current = thumb == 0 ? _minValue : _maxValue;
        float final = Apply(thumb, absolute ? operand : current + operand);
        if (final == current) return;
        if (thumb == 0) _onMinChanged?.Invoke(final);
        else _onMaxChanged?.Invoke(final);
    }

    // ---- 指针消费(Draw 首行;捕获面 = InputState.CapturedId)----

    /// <summary>捕获中消费:Down 帧锁存活动 thumb(Range)→ 位置映射值 → 值管线(钳制+吸附+交叉)→
    /// 仅变化回调。Down 落轨道即就位并续拖(值 = f(位置),thumb/轨道同式)。测试经 InternalsVisibleTo
    /// 直调以镜像 App 的 Update → Draw 时序,免 GPU。</summary>
    internal void ConsumeInput(in InputState input)
    {
        if (!_enabled) return;
        string? id = ResolvedId;
        if (id is null || input.CapturedId != id || !input.LeftDown) return;
        int thumb = _isRange
            ? (input.LeftPressed ? LatchThumb(MapRaw(input.Mouse)) : LastThumb())
            : 0;
        float final = Apply(thumb, MapRaw(input.Mouse));
        float current = thumb == 0 ? _minValue : _maxValue;
        if (final == current) return;
        if (thumb == 0) _onMinChanged?.Invoke(final);
        else _onMaxChanged?.Invoke(final);
    }

    /// <summary>Down 帧锁存活动 thumb:映射值距两 thumb 当前值孰近(平局取 min)。</summary>
    int LatchThumb(float mapped)
    {
        int nearest = MathF.Abs(mapped - _minValue) <= MathF.Abs(mapped - _maxValue) ? 0 : 1;
        WriteThumb(nearest);
        return nearest;
    }

    int LastThumb() => _isRange ? App.State?.Get(ActiveKey(), 0) ?? 0 : 0;

    void WriteThumb(int thumb)
    {
        if (_isRange && ResolvedId is not null)
            App.State?.Set(ActiveKey(), thumb);
    }

    string ActiveKey()
    {
        if (!ReferenceEquals(_activeKeyId, ResolvedId))
        {
            _activeKeyId = ResolvedId;
            _activeKey = "slider:active:" + ResolvedId;
        }
        return _activeKey!;
    }

    // ---- 值管线 ----

    /// <summary>位置→值(方向感知):横向 t=(x−track 左)/行程,纵向 t=(y−track 顶)/行程 后取
    /// max − t·量程(上 = 大);t 钳 [0,1] → 出界值自然钳到 min/max。量程 ≤ 0(退化)取 0。</summary>
    float MapRaw(PointF mouse)
    {
        RectF track = TrackRect();
        if (_vertical)
        {
            float t = track.Height > 0f ? Math.Clamp((mouse.Y - track.Y) / track.Height, 0f, 1f) : 0f;
            return _max - t * (_max - _min);
        }
        float th = track.Width > 0f ? Math.Clamp((mouse.X - track.X) / track.Width, 0f, 1f) : 0f;
        return _min + th * (_max - _min);
    }

    /// <summary>值管线:边界钳制 → Step 吸附(AwayFromZero:45→50 常规手感)→ 复钳边界(端点恒可达,
    /// 吸附越界以边界为准)→ Range 交叉钳制(不变量最优先;界点落在格点上时保持格点)。</summary>
    float Apply(int thumb, float candidate)
    {
        candidate = Math.Clamp(candidate, _min, _max);
        if (_step > 0f)
            candidate = _min + MathF.Round((candidate - _min) / _step, MidpointRounding.AwayFromZero) * _step;
        candidate = Math.Clamp(candidate, _min, _max);
        if (_isRange)
        {
            float gap = _step > 0f ? _step : 0f;
            candidate = thumb == 0
                ? MathF.Max(_min, MathF.Min(candidate, _maxValue - gap))
                : MathF.Min(_max, MathF.Max(candidate, _minValue + gap));
        }
        return candidate;
    }

    // ---- 绘制 ----

    public override void Draw(in PaintContext pc)
    {
        ConsumeInput(pc.Input);
        string? id = ResolvedId;
        Color32 active, thumb;
        if (!_enabled)
        {
            active = DisabledFill;
            thumb = DisabledThumb;
        }
        else if (pc.Input.FocusedId == id)           // 聚焦压过悬停(同 Button/Checkbox 优先级)
        {
            active = AccentFocus;
            thumb = Color32.White;
        }
        else if (pc.Input.HoveredId == id)           // 捕获期间悬停锁定捕获者 → 拖拽中恒为悬停色
        {
            active = AccentHover;
            thumb = Color32.White;
        }
        else
        {
            active = Accent;
            thumb = Color32.White;
        }
        Color32 rest = _enabled ? TrackRest : DisabledFill;

        PointF uv = new(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f);
        RectF track = TrackRect();
        Tessellator.AddRoundedRect(pc.Mesh, track, TrackRadius, rest, uv);
        // 已选段:单 thumb = min 端锚点 → thumb 中心;Range = 两 thumb 之间(FilledRect 归一方向)
        PointF cMin = ThumbCenter(_minValue);
        PointF cMax = _isRange ? ThumbCenter(_maxValue) : cMin;
        PointF anchor = _vertical ? new PointF(track.X, track.Bottom) : new PointF(track.X, track.Y);
        RectF filled = FilledRect(_isRange ? cMin : anchor, cMax, track);
        if (!filled.IsEmpty)
            Tessellator.AddRoundedRect(pc.Mesh, filled, TrackRadius, active, uv);
        if (_isRange)
        {
            RectF rMin = ThumbRect(cMin);
            RectF rMax = ThumbRect(cMax);
            if (_enabled && id is not null && (pc.Input.FocusedId == id || pc.Input.CapturedId == id))
            {
                RectF activeRect = LastThumb() == 0 ? rMin : rMax;
                Tessellator.AddRoundedRect(pc.Mesh,
                    new RectF(activeRect.X - ActiveThumbOutline, activeRect.Y - ActiveThumbOutline,
                        activeRect.Width + ActiveThumbOutline * 2f, activeRect.Height + ActiveThumbOutline * 2f),
                    ThumbRadius + ActiveThumbOutline, ActiveThumbBorder, uv);
            }
            Tessellator.AddRoundedRect(pc.Mesh, rMin, ThumbRadius, thumb, uv);
            Tessellator.AddRoundedRect(pc.Mesh, rMax, ThumbRadius, thumb, uv);
        }
        else
            Tessellator.AddRoundedRect(pc.Mesh, ThumbRect(cMax), ThumbRadius, thumb, uv);
        base.Draw(pc);
    }

    RectF TrackRect() => _vertical
        ? new RectF(Frame.X + (Frame.Width - TrackSize) / 2f, Frame.Y + ThumbMain / 2f,
            TrackSize, MathF.Max(0f, Frame.Height - ThumbMain))
        : new RectF(Frame.X + ThumbMain / 2f, Frame.Y + (Frame.Height - TrackSize) / 2f,
            MathF.Max(0f, Frame.Width - ThumbMain), TrackSize);

    /// <summary>归一行程 t ∈ [0,1];量程 ≤ 0(退化)取 0。</summary>
    float ThumbT(float value)
    {
        float span = _max - _min;
        return span > 0f ? Math.Clamp((value - _min) / span, 0f, 1f) : 0f;
    }

    PointF ThumbCenter(float value)
    {
        RectF track = TrackRect();
        float t = ThumbT(value);
        return _vertical
            ? new PointF(track.X + track.Width / 2f, track.Y + (1f - t) * track.Height)   // 上 = 大
            : new PointF(track.X + t * track.Width, track.Y + track.Height / 2f);
    }

    RectF ThumbRect(PointF center) => _vertical
        ? new RectF(center.X - ThumbCross / 2f, center.Y - ThumbMain / 2f, ThumbCross, ThumbMain)
        : new RectF(center.X - ThumbMain / 2f, center.Y - ThumbCross / 2f, ThumbMain, ThumbCross);

    RectF FilledRect(PointF a, PointF b, RectF track) => _vertical
        ? new RectF(track.X, MathF.Min(a.Y, b.Y), TrackSize, MathF.Abs(b.Y - a.Y))
        : new RectF(MathF.Min(a.X, b.X), track.Y, MathF.Abs(b.X - a.X), TrackSize);
}
