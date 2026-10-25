namespace SharpUI;

public enum Axis { Vertical, Horizontal }
public enum MainAxisAlignment { Start, Center, End }
public enum CrossAxisAlignment { Start, Center, End, Stretch }
public enum FlexWrap { NoWrap, Wrap }

public sealed class Style
{
    public Axis Axis = Axis.Vertical;
    public MainAxisAlignment MainAlign = MainAxisAlignment.Start;
    public CrossAxisAlignment CrossAlign = CrossAxisAlignment.Start;
    public FlexWrap Wrap = FlexWrap.NoWrap;
    public float Gap;
    public Thickness Padding;
    public float FlexGrow;          // 作为子项:行内剩余主轴加权分配
    public float FlexShrink;        // 作为子项:默认 0(M1"叶子不收缩"语义,规格 D16)
    public float? FlexBasis;        // null=auto(内容主尺寸)
}

public static class LayoutEngine
{
    public static SizeF Measure(Element e, SizeF available)
    {
        if (e.Children is not { Count: > 0 })
            return e.MeasureSelf(available);

        bool vertical = e.Style.Axis == Axis.Vertical;
        Thickness pad = e.Style.Padding;
        float availMain = MathF.Max(0f, (vertical ? available.Height : available.Width)
            - (vertical ? pad.Vertical : pad.Horizontal));
        float availCross = MathF.Max(0f, (vertical ? available.Width : available.Height)
            - (vertical ? pad.Horizontal : pad.Vertical));

        var childMain = new float[e.Children.Count];
        var childCross = new float[e.Children.Count];
        var lineOf = new int[e.Children.Count];
        var lineMain = new float[e.Children.Count];
        var lineCross = new float[e.Children.Count];
        int lines = ComputeLines(e, availMain, availCross, childMain, childCross, lineOf, lineMain, lineCross);

        float maxLineMain = 0f, totalCross = 0f;
        for (int l = 0; l < lines; l++)
        {
            maxLineMain = MathF.Max(maxLineMain, lineMain[l]);
            totalCross += lineCross[l];
        }
        totalCross += MathF.Max(0, lines - 1) * e.Style.Gap;
        SizeF content = vertical ? new SizeF(totalCross, maxLineMain) : new SizeF(maxLineMain, totalCross);
        return new SizeF(content.Width + pad.Horizontal, content.Height + pad.Vertical);
    }

    public static void Arrange(Element e, RectF frame)
    {
        e.Frame = frame;
        if (e.Children is not { Count: > 0 }) return;

        bool vertical = e.Style.Axis == Axis.Vertical;
        RectF inner = frame.Deflate(e.Style.Padding);
        float availMain = MathF.Max(0f, vertical ? inner.Height : inner.Width);
        float availCross = MathF.Max(0f, vertical ? inner.Width : inner.Height);

        var childMain = new float[e.Children.Count];
        var childCross = new float[e.Children.Count];
        var lineOf = new int[e.Children.Count];
        var lineMain = new float[e.Children.Count];
        var lineCross = new float[e.Children.Count];
        int lines = ComputeLines(e, availMain, availCross, childMain, childCross, lineOf, lineMain, lineCross);

        float crossPos = vertical ? inner.X : inner.Y;
        for (int l = 0; l < lines; l++)
        {
            float mainSize = vertical ? inner.Height : inner.Width;
            float pos = e.Style.MainAlign switch
            {
                MainAxisAlignment.Center => (vertical ? inner.Top : inner.Left) + MathF.Max(0f, (mainSize - lineMain[l]) / 2f),
                MainAxisAlignment.End => (vertical ? inner.Bottom : inner.Right) - lineMain[l],
                _ => vertical ? inner.Top : inner.Left,
            };
            float extent = lines == 1 ? availCross : lineCross[l];   // 单行对齐基准=容器内交叉宽(M1)
            bool firstInLine = true;
            for (int i = 0; i < e.Children.Count; i++)
            {
                if (lineOf[i] != l) continue;
                Element child = e.Children[i];
                float m = childMain[i];
                float c = childCross[i];
                if (!firstInLine) pos += e.Style.Gap;
                firstInLine = false;
                if (vertical)
                {
                    float w = e.Style.CrossAlign == CrossAxisAlignment.Stretch ? extent : c;
                    float x = e.Style.CrossAlign switch
                    {
                        CrossAxisAlignment.Center => crossPos + (extent - c) / 2f,
                        CrossAxisAlignment.End => crossPos + extent - c,
                        _ => crossPos,
                    };
                    Arrange(child, new RectF(x, pos, w, m));
                }
                else
                {
                    float h = e.Style.CrossAlign == CrossAxisAlignment.Stretch ? extent : c;
                    float y = e.Style.CrossAlign switch
                    {
                        CrossAxisAlignment.Center => crossPos + (extent - c) / 2f,
                        CrossAxisAlignment.End => crossPos + extent - c,
                        _ => crossPos,
                    };
                    Arrange(child, new RectF(pos, y, m, h));
                }
                pos += m;
            }
            crossPos += extent + e.Style.Gap;
        }
    }

    /// <summary>行打包 + 行内分配(D16)。输出数组由调用方分配;返回行数。</summary>
    static int ComputeLines(Element e, float availMain, float availCross,
        float[] childMain, float[] childCross, int[] lineOf, float[] lineMain, float[] lineCross)
    {
        bool vertical = e.Style.Axis == Axis.Vertical;
        bool wrap = e.Style.Wrap == FlexWrap.Wrap && availMain < float.MaxValue;
        float gap = e.Style.Gap;
        System.Collections.Generic.List<Element> children = e.Children!;
        int line = 0, curFrom = 0, curCount = 0;
        float curMain = 0f, curCross = 0f;

        void CloseLine()
        {
            lineMain[line] = curMain;
            lineCross[line] = curCross;
            Distribute(children, availMain, gap, childMain, lineMain, line, curFrom, curCount);
            line++;
            curMain = 0f; curCross = 0f; curCount = 0;
        }

        for (int i = 0; i < children.Count; i++)
        {
            Element c = children[i];
            SizeF m = Measure(c, vertical
                ? new SizeF(availCross, float.PositiveInfinity)
                : new SizeF(float.PositiveInfinity, availCross));
            float basis = c.Style.FlexBasis ?? (vertical ? m.Height : m.Width);
            childMain[i] = basis;
            childCross[i] = vertical ? m.Width : m.Height;

            float projected = curCount == 0 ? basis : curMain + gap + basis;
            if (wrap && curCount > 0 && projected > availMain)
            {
                CloseLine();
                curFrom = i;
                projected = basis;
            }
            if (curCount == 0) curFrom = i;
            if (curCount > 0) curMain += gap;
            curMain += basis;
            curCross = MathF.Max(curCross, childCross[i]);
            lineOf[i] = line;
            curCount++;
        }
        CloseLine();
        return line;
    }

    static void Distribute(System.Collections.Generic.List<Element> children, float availMain, float gap,
        float[] childMain, float[] lineMain, int line, int from, int count)
    {
        if (count <= 0 || availMain >= float.MaxValue) return;   // 无限主轴不分配(Measure 阶段)
        float basisSum = 0f, growSum = 0f, shrinkSum = 0f;
        for (int k = 0; k < count; k++)
        {
            int i = from + k;
            basisSum += childMain[i];
            growSum += children[i].Style.FlexGrow;
            shrinkSum += children[i].Style.FlexShrink * childMain[i];
        }
        float free = availMain - basisSum - MathF.Max(0, count - 1) * gap;
        if (free > 0 && growSum > 0)
            for (int k = 0; k < count; k++)
            {
                int i = from + k;
                childMain[i] += free * children[i].Style.FlexGrow / growSum;
            }
        else if (free < 0 && shrinkSum > 0)
            for (int k = 0; k < count; k++)
            {
                int i = from + k;
                childMain[i] = MathF.Max(0f, childMain[i]
                    + free * (children[i].Style.FlexShrink * childMain[i]) / shrinkSum);
            }
        float total = 0f;
        for (int k = 0; k < count; k++)
        {
            int i = from + k;
            total = k == 0 ? childMain[i] : total + gap + childMain[i];
        }
        lineMain[line] = total;
    }
}
