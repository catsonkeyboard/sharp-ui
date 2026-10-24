namespace SharpUI;

public static class Tessellator
{
    public const int CornerSegments = 8;

    public static void AddRoundedRect(MeshBuilder mesh, RectF rect, float radius, Color32 color, PointF whiteUv)
    {
        radius = MathF.Min(radius, MathF.Min(rect.Width, rect.Height) / 2f);
        if (radius < 0.5f)
        {
            mesh.AddQuad(rect, new RectF(whiteUv.X, whiteUv.Y, 0f, 0f), color);
            return;
        }

        var solidUv = new RectF(whiteUv.X, whiteUv.Y, 0f, 0f);   // 退化 UV = 纯色
        mesh.AddQuad(new RectF(rect.X + radius, rect.Y, rect.Width - 2f * radius, rect.Height), solidUv, color);
        mesh.AddQuad(new RectF(rect.X, rect.Y + radius, radius, rect.Height - 2f * radius), solidUv, color);
        mesh.AddQuad(new RectF(rect.Right - radius, rect.Y + radius, radius, rect.Height - 2f * radius), solidUv, color);

        AddCorner(mesh, new PointF(rect.X + radius, rect.Y + radius), radius, MathF.PI, MathF.PI * 1.5f, color, whiteUv);
        AddCorner(mesh, new PointF(rect.Right - radius, rect.Y + radius), radius, MathF.PI * 1.5f, MathF.PI * 2f, color, whiteUv);
        AddCorner(mesh, new PointF(rect.Right - radius, rect.Bottom - radius), radius, 0f, MathF.PI * 0.5f, color, whiteUv);
        AddCorner(mesh, new PointF(rect.X + radius, rect.Bottom - radius), radius, MathF.PI * 0.5f, MathF.PI, color, whiteUv);
    }

    static void AddCorner(MeshBuilder mesh, PointF center, float radius, float a0, float a1, Color32 color, PointF uv)
    {
        PointF prev = PointOnArc(center, radius, a0);
        for (int i = 1; i <= CornerSegments; i++)
        {
            PointF next = PointOnArc(center, radius, a0 + (a1 - a0) * i / CornerSegments);
            mesh.AddTriangle(center, prev, next, color, uv);
            prev = next;
        }
    }

    static PointF PointOnArc(PointF center, float radius, float angle) =>
        new(center.X + radius * MathF.Cos(angle), center.Y + radius * MathF.Sin(angle));
}
