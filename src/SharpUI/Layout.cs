namespace SharpUI;

public enum Axis { Vertical, Horizontal }
public enum MainAxisAlignment { Start, Center, End }
public enum CrossAxisAlignment { Start, Center, End, Stretch }

public sealed class Style
{
    public Axis Axis = Axis.Vertical;
    public MainAxisAlignment MainAlign = MainAxisAlignment.Start;
    public CrossAxisAlignment CrossAlign = CrossAxisAlignment.Start;
    public float Gap;
    public Thickness Padding;
}

public static class LayoutEngine
{
    public static SizeF Measure(Element e, SizeF available)
    {
        if (e.Children is not { Count: > 0 })
            return e.MeasureSelf(available);

        bool vertical = e.Style.Axis == Axis.Vertical;
        Thickness pad = e.Style.Padding;
        SizeF inner = new(
            MathF.Max(0f, available.Width - pad.Horizontal),
            MathF.Max(0f, available.Height - pad.Vertical));
        float main = 0f, cross = 0f;
        int i = 0;
        foreach (Element child in e.Children)
        {
            if (i++ > 0) main += e.Style.Gap;
            SizeF avail = vertical
                ? new SizeF(inner.Width, MathF.Max(0f, inner.Height - main))
                : new SizeF(MathF.Max(0f, inner.Width - main), inner.Height);
            SizeF m = Measure(child, avail);
            main += vertical ? m.Height : m.Width;
            cross = MathF.Max(cross, vertical ? m.Width : m.Height);
        }
        SizeF content = vertical ? new SizeF(cross, main) : new SizeF(main, cross);
        return new SizeF(content.Width + pad.Horizontal, content.Height + pad.Vertical);
    }

    public static void Arrange(Element e, RectF frame)
    {
        e.Frame = frame;
        if (e.Children is not { Count: > 0 }) return;

        bool vertical = e.Style.Axis == Axis.Vertical;
        RectF inner = frame.Deflate(e.Style.Padding);
        SizeF innerAvail = vertical
            ? new SizeF(inner.Width, float.PositiveInfinity)
            : new SizeF(float.PositiveInfinity, inner.Height);

        float used = 0f;
        int i = 0;
        foreach (Element child in e.Children)
        {
            if (i++ > 0) used += e.Style.Gap;
            child.Measured = Measure(child, innerAvail);
            used += vertical ? child.Measured.Height : child.Measured.Width;
        }

        float mainSize = vertical ? inner.Height : inner.Width;
        float pos = e.Style.MainAlign switch
        {
            MainAxisAlignment.Center => (vertical ? inner.Top : inner.Left) + MathF.Max(0f, (mainSize - used) / 2f),
            MainAxisAlignment.End => (vertical ? inner.Bottom : inner.Right) - used,
            _ => vertical ? inner.Top : inner.Left,
        };

        i = 0;
        foreach (Element child in e.Children)
        {
            if (i++ > 0) pos += e.Style.Gap;
            SizeF m = child.Measured;
            if (vertical)
            {
                float w = e.Style.CrossAlign == CrossAxisAlignment.Stretch ? inner.Width : m.Width;
                float x = e.Style.CrossAlign switch
                {
                    CrossAxisAlignment.Center => inner.X + (inner.Width - m.Width) / 2f,
                    CrossAxisAlignment.End => inner.Right - m.Width,
                    _ => inner.X,
                };
                Arrange(child, new RectF(x, pos, w, m.Height));
                pos += m.Height;
            }
            else
            {
                float h = e.Style.CrossAlign == CrossAxisAlignment.Stretch ? inner.Height : m.Height;
                float y = e.Style.CrossAlign switch
                {
                    CrossAxisAlignment.Center => inner.Y + (inner.Height - m.Height) / 2f,
                    CrossAxisAlignment.End => inner.Bottom - m.Height,
                    _ => inner.Y,
                };
                Arrange(child, new RectF(pos, y, m.Width, h));
                pos += m.Width;
            }
        }
    }
}
