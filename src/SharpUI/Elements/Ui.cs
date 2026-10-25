namespace SharpUI;

public static class Ui
{
    public static PanelElement Column(params Element?[] children) => Container(Axis.Vertical, children);
    public static PanelElement Row(params Element?[] children) => Container(Axis.Horizontal, children);
    public static TextElement Text(string text) => new(text);
    public static Spacer Spacer(SizeF size) => new(size);
    public static PanelElement Panel() => new();
    public static ButtonElement Button(string label, Action onClick) => new(label, onClick);

    static PanelElement Container(Axis axis, Element?[] children)
    {
        var panel = new PanelElement();
        panel.Style.Axis = axis;
        foreach (Element? child in children)
            if (child is not null)
                panel.Add(child);
        return panel;
    }
}

public static class ElementExtensions
{
    public static T Gap<T>(this T e, float gap) where T : Element { e.Style.Gap = gap; return e; }
    public static T Padding<T>(this T e, Thickness padding) where T : Element { e.Style.Padding = padding; return e; }
    public static T Padding<T>(this T e, float all) where T : Element { e.Style.Padding = new Thickness(all); return e; }
    public static T Center<T>(this T e) where T : Element { e.Style.MainAlign = MainAxisAlignment.Center; return e; }
    public static T CrossCenter<T>(this T e) where T : Element { e.Style.CrossAlign = CrossAxisAlignment.Center; return e; }
}
