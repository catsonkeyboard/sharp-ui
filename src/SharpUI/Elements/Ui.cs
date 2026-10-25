namespace SharpUI;

public static class Ui
{
    public static PanelElement Column(params Element?[] children) => Container(Axis.Vertical, children);
    public static PanelElement Row(params Element?[] children) => Container(Axis.Horizontal, children);
    public static TextElement Text(string text) => new(text);

    /// <summary>即时模式逃生舱(D14):rootKey 须全树唯一(同 Key 纪律)。
    /// 不可作为渲染根(App.Create 的 render 闭包直接返回 Immediate 面板时根 id 为空串,后缀匹配永不成立);
    /// </summary>
    public static PanelElement Immediate(string rootKey, Action<UiScope> build)
    {
        var root = new PanelElement { Key = rootKey };
        build(new UiScope(root, rootKey));
        return root;
    }
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
    public static T Key<T>(this T e, string key) where T : Element { e.Key = key; return e; }
    public static T Focusable<T>(this T e) where T : Element { e.Focusable = true; return e; }
    public static T Grow<T>(this T e, float grow) where T : Element { e.Style.FlexGrow = grow; return e; }
    public static T Shrink<T>(this T e, float shrink) where T : Element { e.Style.FlexShrink = shrink; return e; }
    public static T Basis<T>(this T e, float basis) where T : Element { e.Style.FlexBasis = basis; return e; }
    public static T Wrap<T>(this T e) where T : Element { e.Style.Wrap = FlexWrap.Wrap; return e; }
}
