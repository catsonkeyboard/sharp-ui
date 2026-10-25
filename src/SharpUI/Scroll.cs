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

/// <summary>滚轮分发:深度优先,最深可滚者赢(D19)。</summary>
public static class Scroll
{
    public static void Dispatch(Element root, in InputState input)
    {
        if (input.WheelDelta == 0f) return;
        // 防御(与 Interaction.Update 同款):未经 Assign 的树直连 Dispatch 时补算稳定 id,
        // 否则嵌套 Scroll 的 ResolvedId 同为 null 会写同一个 "scroll:" 键。
        // App.Run 恒以上帧树(构建时已 Assign)调 Dispatch,生产路径此行零命中。
        if (root.ResolvedId is null) IdResolver.Assign(root);
        DispatchDeep(root, input.Mouse, input.WheelDelta);
    }

    static bool DispatchDeep(Element e, PointF mouse, float delta)
    {
        // 子先父后(D19);滚动内容可越出父 Frame(内容堆叠在视口外),故下渗不作父级 Contains 门,
        // 仅自滚时校验自身 Frame(不含鼠标/不可滚 → 返回 false,事件向父级下渗)。
        if (e.Children is not null)
            for (int i = e.Children.Count - 1; i >= 0; i--)
                if (DispatchDeep(e.Children[i], mouse, delta)) return true;
        if (e.Frame.Contains(mouse) && e is ScrollElement { ContentMain: > 0 } s && s.ContentMain > s.ViewportMain)
        {
            s.ScrollBy(delta * 40f);
            return true;
        }
        return false;
    }
}
