namespace SharpUI;

/// <summary>绘制上下文(Task 11 将再加 InputState)。</summary>
public readonly record struct PaintContext(MeshBuilder Mesh, TextAtlas Atlas)
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

    public Element Add(Element child)
    {
        Children ??= new List<Element>(4);
        Children.Add(child);
        return this;
    }

    /// <summary>叶子自测尺寸;容器尺寸由 LayoutEngine 依据子元素推导。</summary>
    protected internal virtual SizeF MeasureSelf(SizeF available) => SizeF.Zero;

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
