namespace SharpUI;

public readonly record struct Color32(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Color32 White = new(255, 255, 255);
    public static readonly Color32 Black = new(0, 0, 0);
    public static readonly Color32 Transparent = new(0, 0, 0, 0);

    public static Color32 FromRgb(uint rgb) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public Color32 WithAlpha(byte a) => new(R, G, B, a);

    /// <summary>RGBA 字节线性插值(D23 悬停过渡);t 夹紧 [0,1],四舍五入到最近字节。</summary>
    public static Color32 Lerp(Color32 a, Color32 b, float t)
    {
        float k = Math.Clamp(t, 0f, 1f);
        return new Color32(
            (byte)MathF.Round(a.R + (b.R - a.R) * k),
            (byte)MathF.Round(a.G + (b.G - a.G) * k),
            (byte)MathF.Round(a.B + (b.B - a.B) * k),
            (byte)MathF.Round(a.A + (b.A - a.A) * k));
    }

    /// 预乘 alpha:与 Byte4_Norm 顶点色、预乘图集、SingleOverrideBlend 配套。
    public (byte R, byte G, byte B) Premultiplied =>
        ((byte)(R * A / 255), (byte)(G * A / 255), (byte)(B * A / 255));
}
