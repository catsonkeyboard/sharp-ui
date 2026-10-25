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
