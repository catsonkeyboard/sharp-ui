namespace SharpUI;

public sealed class TextElement : Element
{
    public string Text = "";
    public float FontSize = 16f;
    public bool IsBold;
    public Color32 Color = Color32.White;

    public TextElement(string text) => Text = text;

    public TextElement Size(float size) { FontSize = size; return this; }
    public TextElement Bold() { IsBold = true; return this; }

    protected internal override SizeF MeasureSelf(SizeF available) =>
        App.Atlas is null ? SizeF.Zero : App.Atlas.Measure(Text, FontSize, IsBold);

    public override void Draw(in PaintContext pc)
    {
        if (Frame.IsEmpty || Text.Length == 0) return;
        pc.Atlas.DrawRun(pc.Mesh, Text, new PointF(Frame.X, Frame.Y), FontSize, IsBold, Color);
    }
}

/// <summary>
/// Color 链式方法以扩展形式存在:C# 不允许同类型内字段与方法同名(CS0102),
/// 而测试与示例同时依赖 <c>t.Color.R</c>(字段)与 <c>.Color(0xE8E8EC)</c>(链式调用)。
/// </summary>
public static class TextElementExtensions
{
    public static TextElement Color(this TextElement e, Color32 c) { e.Color = c; return e; }
    public static TextElement Color(this TextElement e, uint rgb) { e.Color = Color32.FromRgb(rgb); return e; }
}
