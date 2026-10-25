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

/// <summary>布局临时数组的按调用深度池(M3b 性能波):ComputeLines 会经 Measure(child) 递归重入,
/// 各入口(Measure/ArrangeLines/ScrollElement.ArrangeChildren)在递增深度后租用本层组,
/// 深层只能拿到更深下标 → 同栈互不覆盖即安全;容量按需倍增,预热后零分配。</summary>
internal static class FlexArrays
{
    internal sealed class Set
    {
        public float[] ChildMain = Array.Empty<float>(), ChildCross = Array.Empty<float>(),
            LineMain = Array.Empty<float>(), LineCross = Array.Empty<float>();
        public int[] LineOf = Array.Empty<int>();
    }

    /// <summary>当前布局调用深度;入口 ++ / finally --(try/finally 保异常路径平衡)。</summary>
    [ThreadStatic] internal static int Depth;

    [ThreadStatic] static List<Set>? _byDepth;

    /// <summary>取 <see cref="Depth"/> 层的数组组并保证容量 ≥ minCount。</summary>
    internal static Set Rent(int minCount)
    {
        List<Set> byDepth = _byDepth ??= new();
        while (byDepth.Count <= Depth) byDepth.Add(new Set());
        Set s = byDepth[Depth];
        if (s.ChildMain.Length < minCount)
        {
            int cap = Math.Max(Math.Max(4, minCount), s.ChildMain.Length * 2);
            s.ChildMain = new float[cap];
            s.ChildCross = new float[cap];
            s.LineOf = new int[cap];
            s.LineMain = new float[cap];
            s.LineCross = new float[cap];
        }
        return s;
    }
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

        FlexArrays.Depth++;   // 入口租用而非 ComputeLines 内:递归 Measure(child) 在更深层重租,本层数组安全
        try
        {
            FlexArrays.Set a = FlexArrays.Rent(e.Children.Count);
            int lines = ComputeLines(e, availMain, availCross, a.ChildMain, a.ChildCross, a.LineOf, a.LineMain, a.LineCross);

            float maxLineMain = 0f, totalCross = 0f;
            for (int l = 0; l < lines; l++)
            {
                maxLineMain = MathF.Max(maxLineMain, a.LineMain[l]);
                totalCross += a.LineCross[l];
            }
            totalCross += MathF.Max(0, lines - 1) * e.Style.Gap;
            SizeF content = vertical ? new SizeF(totalCross, maxLineMain) : new SizeF(maxLineMain, totalCross);
            return new SizeF(content.Width + pad.Horizontal, content.Height + pad.Vertical);
        }
        finally { FlexArrays.Depth--; }
    }

    public static void Arrange(Element e, RectF frame)
    {
        e.Frame = frame;
        if (e.Children is not { Count: > 0 }) return;
        RectF inner = frame.Deflate(e.Style.Padding);
        e.ArrangeChildren(inner);
    }

    /// <summary>默认行布局(D19:原 Arrange 的行布局循环原样搬入);经 Element.ArrangeChildren 虚挂钩调用,ScrollElement 等覆写替换。</summary>
    internal static void ArrangeLines(Element e, RectF inner)
    {
        bool vertical = e.Style.Axis == Axis.Vertical;
        float availMain = MathF.Max(0f, vertical ? inner.Height : inner.Width);
        float availCross = MathF.Max(0f, vertical ? inner.Width : inner.Height);

        FlexArrays.Depth++;   // 行内 Arrange(child) 递归更深层次租用,本层 childMain/lineOf 等在循环中保持有效
        try
        {
            FlexArrays.Set a = FlexArrays.Rent(e.Children!.Count);
            int lines = ComputeLines(e, availMain, availCross, a.ChildMain, a.ChildCross, a.LineOf, a.LineMain, a.LineCross);

            float crossPos = vertical ? inner.X : inner.Y;
            for (int l = 0; l < lines; l++)
            {
                float mainSize = vertical ? inner.Height : inner.Width;
                float pos = e.Style.MainAlign switch
                {
                    MainAxisAlignment.Center => (vertical ? inner.Top : inner.Left) + MathF.Max(0f, (mainSize - a.LineMain[l]) / 2f),
                    MainAxisAlignment.End => (vertical ? inner.Bottom : inner.Right) - a.LineMain[l],
                    _ => vertical ? inner.Top : inner.Left,
                };
                float extent = lines == 1 ? availCross : a.LineCross[l];   // 单行对齐基准=容器内交叉宽(M1)
                bool firstInLine = true;
                for (int i = 0; i < e.Children!.Count; i++)
                {
                    if (a.LineOf[i] != l) continue;
                    Element child = e.Children[i];
                    float m = a.ChildMain[i];
                    float c = a.ChildCross[i];
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
        finally { FlexArrays.Depth--; }
    }

    /// <summary>行打包 + 行内分配(D16)。输出数组由调用方分配;返回行数。</summary>
    internal static int ComputeLines(Element e, float availMain, float availCross,
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
