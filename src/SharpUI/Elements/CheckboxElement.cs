namespace SharpUI;

/// <summary>复选框(T2):14×14 方框(左)+ label TextElement 子元素(垂直居中);勾 = 两段白色 quad。
/// 点击切换语义,不捕获拖拽(CapturesDrag=false 默认):按下+松开同元素即切换;键盘聚焦+Space/Enter
/// 切换(经 FocusManager.HandleKeys 的 IActivatable 激活分支,同 ButtonElement 管道)。
/// 选中态由调用方持有(树每帧重建,isChecked 参数即当前值),激活回调 onChanged(!isChecked) 一次。
/// Enabled=false:灰视觉 + 不响应(指针/键盘统一 CanActivate 门,点击账本与回调一致不记)并退出 Tab
/// 焦点序(Focusable 随 Enabled 置位;程序性 SetFocus 亦被 CanActivate 拒激活)。
/// Draw 零新分配:纯结构体运算 + 复用静态色板;Draw 中改子元素样式同 ButtonElement.Background 先例。</summary>
public sealed class CheckboxElement : PanelElement, IActivatable
{
    /// <summary>方框边长(规格常量,布局与绘制共用)。</summary>
    public const float BoxSize = 14f;
    const float BoxInset = 4f;    // 方框左缘内缩
    const float LabelGap = 4f;    // 方框右缘到文本
    const float MarkHalf = 0.9f;  // 勾笔画半宽

    // 色板(硬编码,同 ButtonElement 纪律):常态/勾选框面 = Button 常态蓝 0x0C63E4,悬停 0x3B82F6 /
    // 聚焦 0x60A5FA / 禁用缘 0x3A3A42 同 Button 悬停/聚焦/禁用;未勾框面 0x1A1A22、未勾框缘 0x565662、
    // 禁用框面 0x24242A、禁用前景 0x6A6A74;勾与标签常白。
    static readonly Color32 Accent = Color32.FromRgb(0x0C63E4);
    static readonly Color32 AccentHover = Color32.FromRgb(0x3B82F6);
    static readonly Color32 AccentFocus = Color32.FromRgb(0x60A5FA);
    static readonly Color32 OffFill = Color32.FromRgb(0x1A1A22);
    static readonly Color32 OffBorder = Color32.FromRgb(0x565662);
    static readonly Color32 DisabledFill = Color32.FromRgb(0x24242A);
    static readonly Color32 DisabledBorder = Color32.FromRgb(0x3A3A42);
    static readonly Color32 DisabledFg = Color32.FromRgb(0x6A6A74);

    readonly TextElement _label;
    readonly bool _isChecked;
    readonly Action<bool>? _onChanged;
    bool _enabled = true;

    public CheckboxElement(string label, bool isChecked, Action<bool>? onChanged)
    {
        _isChecked = isChecked;
        _onChanged = onChanged;
        _label = new TextElement(label).Size(14f);
        Add(_label);
        Style.MainAlign = MainAxisAlignment.Center;   // 文本垂直居中(Axis 默认 Vertical,主轴 = 纵向)
        Style.Padding = new Thickness(BoxInset + BoxSize + LabelGap, 3f, 4f, 3f);   // 左侧让位方框
        IsHitTarget = true;
        Focusable = true;
    }

    /// <summary>false = 禁用:灰视觉 + 不响应,并同步退出焦点序。</summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Focusable = value; }
    }

    /// <summary>IActivatable:禁用拒激活(指针/键盘统一门,账本与回调一致)。</summary>
    public bool CanActivate => _enabled;

    /// <summary>切换语义:指针 = 按下+松开同元素;键盘 = 聚焦+Space/Enter。各触发一次 onChanged(!isChecked)。</summary>
    public void InvokeClick()
    {
        if (!_enabled) return;
        _onChanged?.Invoke(!_isChecked);
    }

    public override void Draw(in PaintContext pc)
    {
        string? id = ResolvedId;
        Color32 border, fill, mark;
        if (!_enabled)
        {
            border = DisabledBorder;
            fill = DisabledFill;
            mark = _isChecked ? DisabledFg : DisabledBorder;
        }
        else if (pc.Input.FocusedId == id)           // 聚焦分支优先级同 Button:聚焦压过悬停
        {
            border = AccentFocus;
            fill = _isChecked ? Accent : OffFill;
            mark = Color32.White;
        }
        else if (pc.Input.HoveredId == id)
        {
            border = AccentHover;
            fill = _isChecked ? Accent : OffFill;
            mark = Color32.White;
        }
        else
        {
            border = _isChecked ? Accent : OffBorder;
            fill = _isChecked ? Accent : OffFill;
            mark = Color32.White;
        }
        _label.Color = _enabled ? Color32.White : DisabledFg;

        PointF uv = new(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f);
        var box = new RectF(Frame.X + BoxInset, Frame.Y + (Frame.Height - BoxSize) / 2f, BoxSize, BoxSize);
        Tessellator.AddRoundedRect(pc.Mesh, box, 3f, border, uv);                                  // 外缘
        Tessellator.AddRoundedRect(pc.Mesh, box.Deflate(new Thickness(1f)), 2f, fill, uv);         // 内面(留 1px 缘)
        if (_isChecked)
        {
            // 勾 = 两段 quad:短划(左中→下中)+ 长划(下中→右上),沿法线展宽成 quad(各 2 三角,零分配)
            AddStroke(pc.Mesh, new PointF(box.X + 3f, box.Y + 7.5f), new PointF(box.X + 5.5f, box.Y + 10f), MarkHalf, mark, uv);
            AddStroke(pc.Mesh, new PointF(box.X + 5.5f, box.Y + 10f), new PointF(box.X + 11f, box.Y + 4f), MarkHalf, mark, uv);
        }
        base.Draw(pc);
    }

    /// <summary>线段展宽为 quad(4 顶点 2 三角,退化 UV = 纯色,同 Tessellator 纯色手法)。</summary>
    static void AddStroke(MeshBuilder mesh, PointF a, PointF b, float halfWidth, Color32 color, PointF uv)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float nx = -dy / len * halfWidth, ny = dx / len * halfWidth;
        var solidUv = new RectF(uv.X, uv.Y, 0f, 0f);
        mesh.AddTriangle(new PointF(a.X + nx, a.Y + ny), new PointF(b.X + nx, b.Y + ny), new PointF(b.X - nx, b.Y - ny), color, uv);
        mesh.AddTriangle(new PointF(a.X + nx, a.Y + ny), new PointF(b.X - nx, b.Y - ny), new PointF(a.X - nx, a.Y - ny), color, uv);
    }
}
