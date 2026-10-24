namespace SharpUI;

public readonly record struct Color32(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Color32 White = new(255, 255, 255);
    public static readonly Color32 Black = new(0, 0, 0);
    public static readonly Color32 Transparent = new(0, 0, 0, 0);

    public static Color32 FromRgb(uint rgb) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public Color32 WithAlpha(byte a) => new(R, G, B, a);

    /// 预乘 alpha:与 Byte4_Norm 顶点色、预乘图集、SingleOverrideBlend 配套。
    public (byte R, byte G, byte B) Premultiplied =>
        ((byte)(R * A / 255), (byte)(G * A / 255), (byte)(B * A / 255));
}
