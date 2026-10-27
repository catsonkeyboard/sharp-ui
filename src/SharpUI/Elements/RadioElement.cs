namespace SharpUI;

/// <summary>单选钮(T2):14×14 圆环(左,双扇形叠出 1.5px 环)+ label TextElement 子元素(垂直居中);
/// 选中 = 环内填充同色成实心盘 + 白色内点。点击切换语义,不捕获拖拽(CapturesDrag=false 默认):
/// 按下+松开同元素即激活;键盘聚焦+Space/Enter 激活(IActivatable 管道,同 ButtonElement)。
/// 同 groupName 焦点邻居查询落 FocusManager 每帧 Register 分组(方向键组内循环,方向键只移焦 ——
/// 选中仍由 Space/Enter 承担;每窗一份 FocusManager,分组天然按窗隔离,不落静态注册表)。
/// 已选中再激活不回调(回调仅值变化触发)。Enabled=false 同 CheckboxElement:灰视觉 + 不响应 + 退出焦点序。</summary>
public sealed class RadioElement : PanelElement, IActivatable
{
    /// <summary>圆环直径(规格常量,布局与绘制共用)。</summary>
    public const float Diameter = 14f;
    const float Inset = 4f;          // 圆环左缘内缩
    const float LabelGap = 4f;       // 圆环右缘到文本
    const float RingThickness = 1.5f;
    const float DotRadius = 2.5f;
    const int RingSegments = 24;     // 7px 半径整圆细分(视觉平滑 × 三角数平衡)
    const int DotSegments = 12;

    // 色板与 CheckboxElement 同源:常态/选中环面 = Button 常态蓝 0x0C63E4,悬停 0x3B82F6 / 聚焦
    // 0x60A5FA / 禁用缘 0x3A3A42;未选环内面 0x1A1A22、未选环 0x565662、禁用面 0x24242A、
    // 禁用前景 0x6A6A74;内点与标签常白。
    static readonly Color32 Accent = Color32.FromRgb(0x0C63E4);
    static readonly Color32 AccentHover = Color32.FromRgb(0x3B82F6);
    static readonly Color32 AccentFocus = Color32.FromRgb(0x60A5FA);
    static readonly Color32 OffFill = Color32.FromRgb(0x1A1A22);
    static readonly Color32 OffBorder = Color32.FromRgb(0x565662);
    static readonly Color32 DisabledFill = Color32.FromRgb(0x24242A);
    static readonly Color32 DisabledBorder = Color32.FromRgb(0x3A3A42);
    static readonly Color32 DisabledFg = Color32.FromRgb(0x6A6A74);

    readonly TextElement _label;
    readonly string _groupName;
    readonly bool _isSelected;
    readonly Action? _onSelected;
    bool _enabled = true;

    public RadioElement(string label, string groupName, bool isSelected, Action? onSelected)
    {
        _groupName = groupName;
        _isSelected = isSelected;
        _onSelected = onSelected;
        _label = new TextElement(label).Size(14f);
        Add(_label);
        Style.MainAlign = MainAxisAlignment.Center;   // 文本垂直居中(Axis 默认 Vertical,主轴 = 纵向)
        Style.Padding = new Thickness(Inset + Diameter + LabelGap, 3f, 4f, 3f);   // 左侧让位圆环
        IsHitTarget = true;
        Focusable = true;
    }

    /// <summary>同组标识(FocusManager 每帧按此分组,方向键组内循环)。</summary>
    public string GroupName => _groupName;

    /// <summary>false = 禁用:灰视觉 + 不响应,并同步退出焦点序。</summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Focusable = value; }
    }

    /// <summary>IActivatable:禁用拒激活(指针/键盘统一门,账本与回调一致)。</summary>
    public bool CanActivate => _enabled;

    /// <summary>激活语义:未选中时触发 onSelected 一次;已选中再激活 = 值未变化,不回调。</summary>
    public void InvokeClick()
    {
        if (!_enabled || _isSelected) return;
        _onSelected?.Invoke();
    }

    public override void Draw(in PaintContext pc)
    {
        string? id = ResolvedId;
        Color32 ring, fill, dot;
        if (!_enabled)
        {
            ring = DisabledBorder;
            fill = DisabledFill;
            dot = DisabledFg;
        }
        else if (pc.Input.FocusedId == id)           // 聚焦分支优先级同 Button:聚焦压过悬停
        {
            ring = AccentFocus;
            fill = _isSelected ? Accent : OffFill;
            dot = Color32.White;
        }
        else if (pc.Input.HoveredId == id)
        {
            ring = AccentHover;
            fill = _isSelected ? Accent : OffFill;
            dot = Color32.White;
        }
        else
        {
            ring = _isSelected ? Accent : OffBorder;
            fill = _isSelected ? Accent : OffFill;
            dot = Color32.White;
        }
        _label.Color = _enabled ? Color32.White : DisabledFg;

        PointF uv = new(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f);
        var center = new PointF(
            Frame.X + Inset + Diameter / 2f,
            Frame.Y + (Frame.Height - Diameter) / 2f + Diameter / 2f);
        AddCircle(pc.Mesh, center, Diameter / 2f, RingSegments, ring, uv);                     // 环外圈
        AddCircle(pc.Mesh, center, Diameter / 2f - RingThickness, RingSegments, fill, uv);     // 环内面(叠出环)
        if (_isSelected)
            AddCircle(pc.Mesh, center, DotRadius, DotSegments, dot, uv);                       // 选中内点
        base.Draw(pc);
    }

    /// <summary>实心圆 = 三角扇(零分配:结构体运算,纯色退化 UV 同 Tessellator 手法)。</summary>
    static void AddCircle(MeshBuilder mesh, PointF center, float radius, int segments, Color32 color, PointF uv)
    {
        PointF prev = new(center.X + radius, center.Y);
        for (int i = 1; i <= segments; i++)
        {
            float angle = MathF.PI * 2f * i / segments;
            var next = new PointF(center.X + radius * MathF.Cos(angle), center.Y + radius * MathF.Sin(angle));
            mesh.AddTriangle(center, prev, next, color, uv);
            prev = next;
        }
    }
}
