namespace SharpUI;

public sealed class ButtonElement : PanelElement
{
    readonly Action _onClick;

    public ButtonElement(string label, Action onClick)
    {
        _onClick = onClick;
        Add(new TextElement(label).Size(16));
        Style.MainAlign = MainAxisAlignment.Center;
        Style.CrossAlign = CrossAxisAlignment.Center;
        Style.Padding = new Thickness(horizontal: 16f, vertical: 8f);
        CornerRadius = 6f;
        IsHitTarget = true;
        Focusable = true;
    }

    public void InvokeClick() => _onClick();

    public override void Draw(in PaintContext pc)
    {
        Background = pc.Input.Focused == this ? Color32.FromRgb(0x60A5FA)
            : pc.Input.Hovered == this
                ? (pc.Input.LeftDown ? Color32.FromRgb(0x1D4ED8) : Color32.FromRgb(0x3B82F6))
            : Color32.FromRgb(0x0C63E4);
        base.Draw(pc);
    }
}
