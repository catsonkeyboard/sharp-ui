namespace SharpUI;

public sealed class PanelElement : Element
{
    public Color32 Background = Color32.Transparent;
    public float CornerRadius;

    public PanelElement Fill(Color32 color) { Background = color; return this; }
    public PanelElement Fill(uint rgb) { Background = Color32.FromRgb(rgb); return this; }
    public PanelElement Radius(float radius) { CornerRadius = radius; return this; }

    public override void Draw(in PaintContext pc)
    {
        if (!Frame.IsEmpty && Background.A != 0)
            Tessellator.AddRoundedRect(pc.Mesh, Frame, CornerRadius, Background,
                new PointF(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f));
        if (Children is null) return;
        foreach (Element child in Children)
            child.Draw(in pc);
    }
}
