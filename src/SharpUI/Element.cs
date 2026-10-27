namespace SharpUI;

/// <summary>绘制上下文(M1 最终形态)。</summary>
public readonly record struct PaintContext(MeshBuilder Mesh, TextAtlas Atlas, InputState Input)
{
    public RectF WhiteUv => Atlas.WhiteUv;
}

public abstract class Element
{
    public Style Style { get; } = new();
    public List<Element>? Children { get; private set; }
    public RectF Frame;      // Arrange 后的绝对坐标
    public SizeF Measured;   // Arrange 期间的度量缓存
    public bool IsHitTarget; // 命中测试终点(Button = true)

    /// <summary>拖拽捕获声明(T2 捕获原语):LeftDown 命中 IsHitTarget 且本属性为 true 的元素时,
    /// Interaction 记其 ResolvedId 为捕获者 —— 捕获期间 MouseMotion/LeftUp 路由旁路 HitTest 直达捕获者,
    /// HoveredId 锁定捕获者;LeftUp 释放;新 LeftDown 覆盖旧捕获。仅 Slider 类拖拽控件覆写 true;
    /// Checkbox/Radio 等点击切换控件保持默认 false(按下+松开同元素即切换,现有点击语义)。</summary>
    internal virtual bool CapturesDrag => false;

    /// <summary>用户提供的稳定键;未提供时按 类型名+兄弟索引 回退(规格书 D10)。</summary>
    public string? Key { get; internal set; }
    public bool Focusable;
    internal string? ResolvedId { get; set; }

    /// <summary>悬停提示文本(D5 T3):ElementExtensions.Tooltip 链式附加;非 null 即为 Tooltip 载体,
    /// 由 TooltipService 每帧扫描驱动(悬停 ≥0.5s 弹出/移出或点击移除)。internal 字段免附加属性表。</summary>
    internal string? TooltipText;

    /// <summary>注册期回链(D5 T4 / D6 T2 定位收敛):最近一次 TreeWalk.Register 本元素入册的 FocusManager,
    /// 是声明式元素获取所属 FocusManager 及其窗口句柄(<see cref="FocusManager.SdlWindow"/>)的唯一入口
    /// (App.Run 每帧对声明树+浮层树逐元素接线;SelectElement 激活时据此定位所属窗的浮层栈/围栏打开下拉菜单,
    /// 不另存窗句柄也不查 FocusManager._byWindow)。未注册(树未经 Register 直用)时激活抛 InvalidOperationException。
    /// 生命周期随元素实例(树每帧重建,LastTree 保留一帧,回链常新)。</summary>
    internal FocusManager? OwnerFocus;

    public Element Add(Element child)
    {
        Children ??= new List<Element>(4);
        Children.Add(child);
        return this;
    }

    /// <summary>叶子自测尺寸;容器尺寸由 LayoutEngine 依据子元素推导。</summary>
    protected internal virtual SizeF MeasureSelf(SizeF available) => SizeF.Zero;

    /// <summary>子元素排布虚挂钩(D19):默认走 LayoutEngine.ArrangeLines 行布局;ScrollElement 等覆写替换。</summary>
    protected internal virtual void ArrangeChildren(RectF inner) => LayoutEngine.ArrangeLines(this, inner);

    public virtual void Draw(in PaintContext pc) { }

    public Element? HitTest(PointF p)
    {
        if (!Frame.Contains(p)) return null;
        if (Children is not null)
            for (int i = Children.Count - 1; i >= 0; i--)
                if (Children[i].HitTest(p) is { } hit)
                    return hit;
        return IsHitTarget ? this : null;
    }

    public static implicit operator Element(string text) => new TextElement(text);
}

public sealed class Spacer : Element
{
    readonly SizeF _size;
    public Spacer(SizeF size) => _size = size;
    protected internal override SizeF MeasureSelf(SizeF available) => _size;
}
