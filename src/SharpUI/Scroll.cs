namespace SharpUI;

/// <summary>竖向滚动容器(D19):偏移存 StateStore["scroll:"+id];Arrange 时内容上移 offset;wheel 分发由 App 调 Scroll.Dispatch。</summary>
public class ScrollElement : PanelElement
{
    internal float ContentMain;
    internal float ViewportMain;

    internal float StateOffset() => App.State?.Get("scroll:" + ResolvedId, 0f) ?? 0f;

    internal void ScrollBy(float delta)
    {
        float max = MathF.Max(0f, ContentMain - ViewportMain);
        App.State?.Set("scroll:" + ResolvedId, Math.Clamp(StateOffset() + delta, 0f, max));
    }

    protected internal override void ArrangeChildren(RectF inner)
    {
        if (Children is not { Count: > 0 }) { ContentMain = ViewportMain = 0f; return; }
        int n = Children.Count;
        var childMain = new float[n];
        var childCross = new float[n];
        var lineOf = new int[n];
        var lineMain = new float[n];
        var lineCross = new float[n];
        int lines = LayoutEngine.ComputeLines(this, float.MaxValue, inner.Width,
            childMain, childCross, lineOf, lineMain, lineCross);   // 主轴无限 → 单行内容度量
        float content = lineMain[0];
        ContentMain = content;
        ViewportMain = inner.Height;
        float offset = Math.Clamp(StateOffset(), 0f, MathF.Max(0f, content - inner.Height));
        float y = inner.Top - offset;
        for (int i = 0; i < n; i++)
        {
            LayoutEngine.Arrange(Children[i], new RectF(inner.X, y, inner.Width, childMain[i]));
            y += childMain[i] + Style.Gap;
        }
    }
}

/// <summary>滚轮分发:深度优先,最深可滚者赢(D19);逐级 Frame.Contains 门 = 帧相交可见性(D17/裁定#22)。</summary>
public static class Scroll
{
    public static void Dispatch(Element root, in InputState input)
    {
        if (input.WheelDelta != 0f)
            DispatchDeep(root, input.Mouse, input.WheelDelta);
    }

    static bool DispatchDeep(Element e, PointF mouse, float delta)
    {
        if (!e.Frame.Contains(mouse)) return false;
        if (e.Children is not null)
            for (int i = e.Children.Count - 1; i >= 0; i--)
                if (DispatchDeep(e.Children[i], mouse, delta)) return true;
        if (e is ScrollElement { ContentMain: > 0 } s && s.ContentMain > s.ViewportMain)
        {
            s.ScrollBy(delta * 40f);
            return true;
        }
        return false;
    }
}

/// <summary>固定行高虚拟化列表(D20):Arrange 时按可见窗 ±1 重建行;行 Key=索引字符串。</summary>
public sealed class VirtualListElement : ScrollElement
{
    readonly int _count;
    readonly float _itemHeight;
    readonly Action<int, PanelElement> _render;

    public VirtualListElement(string key, int count, float itemHeight, Action<int, PanelElement> render)
    {
        Key = key;
        _count = count;
        _itemHeight = itemHeight;
        _render = render;
        Add(new Spacer(SizeF.Zero));   // 占位:LayoutEngine.Arrange 对无子容器短路,须非空才进 ArrangeChildren(首排即清空重建)
    }

    protected internal override void ArrangeChildren(RectF inner)
    {
        Children!.Clear();   // 构造已占位,此处必非空(简报的 Children??= 不可编译:Element.Children 私有 set)
        ContentMain = _count * _itemHeight;
        ViewportMain = inner.Height;
        float offset = Math.Clamp(StateOffset(), 0f, MathF.Max(0f, ContentMain - inner.Height));
        int first = Math.Max(0, (int)MathF.Floor(offset / _itemHeight) - 1);
        int lastExcl = Math.Min(_count, (int)MathF.Ceiling((offset + inner.Height) / _itemHeight) + 1);
        for (int i = first; i < lastExcl; i++)
        {
            var row = new PanelElement { Key = i.ToString() };
            row.Style.CrossAlign = CrossAxisAlignment.Stretch;
            _render(i, row);
            Children.Add(row);
        }
        for (int i = first; i < lastExcl; i++)
            LayoutEngine.Arrange(Children[i - first],
                new RectF(inner.X, inner.Top + i * _itemHeight - offset, inner.Width, _itemHeight));
        IdResolver.AssignChildren(this);   // 重建后重排子树 id(保自 id)
    }
}
