namespace SharpUI;

public readonly record struct SizeF(float Width, float Height)
{
    public static readonly SizeF Zero = new(0, 0);
    public static SizeF operator +(SizeF a, SizeF b) => new(a.Width + b.Width, a.Height + b.Height);
}

public readonly record struct PointF(float X, float Y)
{
    public static readonly PointF Zero = new(0, 0);
    public static PointF operator +(PointF a, PointF b) => new(a.X + b.X, a.Y + b.Y);
}

public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public PointF TopLeft => new(X, Y);
    public SizeF Size => new(Width, Height);
    public bool IsEmpty => Width <= 0f || Height <= 0f;

    public bool Contains(PointF p) =>
        p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public bool Intersects(RectF o) =>
        !IsEmpty && !o.IsEmpty && X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    public RectF Inflate(Thickness t) =>
        new(X - t.Left, Y - t.Top, Width + t.Horizontal, Height + t.Vertical);

    public RectF Deflate(Thickness t) =>
        new(X + t.Left, Y + t.Top, Width - t.Horizontal, Height - t.Vertical);
}

public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    public Thickness(float all) : this(all, all, all, all) { }
    public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}
