namespace SharpUI;

/// <summary>下拉选择器(D5 T4):按钮形态(当前项文本 + 右侧 ▼ 箭头图形)。点击/聚焦+Enter/Space
/// 打开锚定菜单(复用 <see cref="MenuExtensions.Show"/>,锚=自身 Frame,初始焦点=当前项即高亮);
/// 菜单项激活 → onChanged(新下标) + 关菜单(Esc/外点/点选当前项只关不回调)。
/// 键盘两路径:菜单未开时方向键直接换选(IKeySteerable,±1 钳制不环移,仅变化回调);开菜单时方向键
/// 在菜单内导航(既有 MenuElement.Navigate)、Enter 确认聚焦项、Esc 关闭不开菜单。
/// 选中态由调用方持有(树每帧重建,selectedIndex 参数即当前值,Button/Checkbox 回调式同构)。
/// 打开接线的最小侵入(T4 裁定):激活回调经 <see cref="Element.OwnerFocus"/> 注册期回链定位所属窗
/// (FocusManager.Register 每帧接线,App.Run 帧序下指针路径读上帧回链、键盘路径读当帧回链,同一
/// FocusManager 实例语义等价);未注册元素激活抛 InvalidOperationException(拒绝静默丢浮层)。
/// Enabled=false:灰视觉 + 拒激活/拒换选(CanActivate/HandleKey 统一门)+ 退出焦点序。
/// Draw 零新分配:静态色板 + 结构体运算,箭头两段 quad 同 CheckboxElement.AddStroke 手法。</summary>
public sealed class SelectElement : PanelElement, IActivatable, IKeySteerable
{
    const float FontSize = 14f;
    const float PadH = 12f, PadV = 6f;    // 按钮形态 padding(右侧加箭头让位区)
    const float ArrowInset = 20f;         // 箭头中心距右缘
    const float ArrowHalf = 4f;           // 箭头半宽(▼ 上缘两翼)
    const float ArrowDrop = 2f;           // 箭头垂深(上缘→下顶点)
    const float StrokeHalf = 1.2f;        // 笔画半宽

    // 色板(硬编码,同 Checkbox 纪律):常态/聚焦/悬停 = Button 蓝系 0x0C63E4/0x60A5FA/0x3B82F6
    // (聚焦压过悬停,优先级同 Button/Checkbox);禁用底 0x24242A、禁用前景 0x6A6A74(Checkbox 同源)。
    static readonly Color32 Accent = Color32.FromRgb(0x0C63E4);
    static readonly Color32 AccentHover = Color32.FromRgb(0x3B82F6);
    static readonly Color32 AccentFocus = Color32.FromRgb(0x60A5FA);
    static readonly Color32 DisabledFill = Color32.FromRgb(0x24242A);
    static readonly Color32 DisabledFg = Color32.FromRgb(0x6A6A74);

    readonly string[] _options;
    readonly int _selectedIndex;
    readonly Action<int> _onChanged;
    readonly TextElement _label;
    bool _enabled = true;

    public SelectElement(string[] options, int selectedIndex, Action<int> onChanged)
    {
        if (options is null || options.Length == 0)
            throw new ArgumentException("Select: options 须至少一项。", nameof(options));
        if ((uint)selectedIndex >= (uint)options.Length)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex),
                $"Select: selectedIndex={selectedIndex} 越界 [0,{options.Length})。");
        _options = options;
        _selectedIndex = selectedIndex;
        _onChanged = onChanged;
        _label = new TextElement(options[selectedIndex]).Size(FontSize);   // 当前项显示
        Add(_label);
        Style.MainAlign = MainAxisAlignment.Center;          // 文本垂直居中(Axis 默认 Vertical)
        Style.Padding = new Thickness(PadH, PadV, PadH + ArrowInset, PadV);   // 右侧箭头让位
        CornerRadius = 6f;                                   // 按钮圆角同 ButtonElement
        IsHitTarget = true;
        Focusable = true;
    }

    /// <summary>false = 禁用:灰视觉 + 拒激活/拒换选,并同步退出焦点序。</summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Focusable = value; }
    }

    /// <summary>IActivatable:禁用拒激活(指针/键盘统一门,账本与回调一致);激活语义 = 打开下拉菜单。</summary>
    public bool CanActivate => _enabled;

    /// <summary>激活漏斗(指针按下+松开同元素 / 聚焦+Enter/Space):打开锚定菜单,当前项为初始焦点(高亮)。</summary>
    public void InvokeClick()
    {
        if (!_enabled) return;
        Open();
    }

    /// <summary>IKeySteerable:菜单未开时方向键直接换选(Up=-1/Down=+1,钳制不环移);
    /// 开菜单时本元素不在围栏内,方向键由菜单项消费(既有路由),此路径不可达。禁用拒消费。</summary>
    public bool HandleKey(SharpUI.Key key)
    {
        if (!_enabled) return false;
        switch (key)
        {
            case SharpUI.Key.Up:
                Change(-1);
                return true;
            case SharpUI.Key.Down:
                Change(1);
                return true;
            default:
                return false;                                // 横向/Home/End 无纵向下拉语义(回落既有路由)
        }
    }

    /// <summary>直接换选:±1 钳制于 [0, n-1],仅下标实际变化触发回调(D4 回调纪律)。</summary>
    void Change(int delta)
    {
        int next = Math.Clamp(_selectedIndex + delta, 0, _options.Length - 1);
        if (next == _selectedIndex) return;
        _onChanged(next);
    }

    /// <summary>打开下拉菜单:options 逐项映射 MenuItem(OnClick=onChanged(下标);当前项门 —— 点选
    /// 未变化的当前项不回调,仅关菜单,D4 回调纪律),锚=自身 Frame
    /// (菜单左上对齐 Frame 底缘),初始焦点=当前项(Select 特有,非首项)。命令式动作,分配自由(T1 惯例)。</summary>
    void Open()
    {
        if (OwnerFocus is not { } focus || focus.SdlWindow == IntPtr.Zero)
            throw new InvalidOperationException(
                "Select: 元素未注册到窗口 FocusManager(App.Run 每帧 TreeWalk.Register 接线;合成测试同款注册)。");
        var items = new object?[_options.Length];
        for (int i = 0; i < _options.Length; i++)
        {
            int index = i;                                   // 闭包捕获循环下标
            items[i] = new MenuItem(_options[index], () => { if (index != _selectedIndex) _onChanged(index); });
        }
        MenuExtensions.Show(focus.SdlWindow, focus, Frame, items, _selectedIndex, focus.ScreenSize);
    }

    public override void Draw(in PaintContext pc)
    {
        string? id = ResolvedId;
        Color32 bg, fg;
        if (!_enabled)
        {
            bg = DisabledFill;
            fg = DisabledFg;
        }
        else if (pc.Input.FocusedId == id)                   // 聚焦压过悬停(同 Button/Checkbox)
        {
            bg = AccentFocus;
            fg = Color32.White;
        }
        else if (pc.Input.HoveredId == id)
        {
            bg = AccentHover;
            fg = Color32.White;
        }
        else
        {
            bg = Accent;
            fg = Color32.White;
        }
        Background = bg;                                     // PanelElement 基类画底(Draw 中改样式同 Button 先例)
        _label.Color = fg;
        base.Draw(pc);                                       // 底色+标签先行
        PointF uv = new(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f);
        float cx = Frame.Right - ArrowInset, cy = Frame.Y + Frame.Height / 2f;
        AddStroke(pc.Mesh, new PointF(cx - ArrowHalf, cy - ArrowDrop), new PointF(cx, cy + ArrowDrop), StrokeHalf, fg, uv);
        AddStroke(pc.Mesh, new PointF(cx, cy + ArrowDrop), new PointF(cx + ArrowHalf, cy - ArrowDrop), StrokeHalf, fg, uv);
    }

    /// <summary>线段展宽为 quad(4 顶点 2 三角,退化 UV = 纯色,同 CheckboxElement.AddStroke 手法)。</summary>
    static void AddStroke(MeshBuilder mesh, PointF a, PointF b, float halfWidth, Color32 color, PointF uv)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float nx = -dy / len * halfWidth, ny = dx / len * halfWidth;
        mesh.AddTriangle(new PointF(a.X + nx, a.Y + ny), new PointF(b.X + nx, b.Y + ny), new PointF(b.X - nx, b.Y - ny), color, uv);
        mesh.AddTriangle(new PointF(a.X + nx, a.Y + ny), new PointF(b.X - nx, b.Y - ny), new PointF(a.X - nx, a.Y - ny), color, uv);
    }
}
