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
    public static TextFieldElement TextField(string placeholder = "") => new(placeholder);

    /// <summary>复选框(T2):点击切换/聚焦+Space 切换;选中态由调用方持有,回调报新值。</summary>
    public static CheckboxElement Checkbox(string label, bool isChecked, Action<bool>? onChanged) =>
        new(label, isChecked, onChanged);

    /// <summary>单选钮(T2):同 groupName 方向键组内移焦;已选中再点不回调(仅值变化触发)。</summary>
    public static RadioElement Radio(string label, string groupName, bool isSelected, Action? onSelected) =>
        new(label, groupName, isSelected, onSelected);

    /// <summary>滑块(T3):拖拽捕获/轨道点击就位续拖/键盘微调(±step、Home/End);.Range 链式启用双 thumb
    /// 区间,.Vertical 纵向(上=大);值态由调用方持有,回调报钳制+吸附后的新值(仅变化触发)。</summary>
    public static SliderElement Slider(float min, float max, float value, Action<float> onChanged) =>
        new(min, max, value, onChanged);

    /// <summary>下拉选择器(T4):按钮形态(当前项文本+下拉箭头);点击/聚焦+Enter/Space 打开锚定菜单
    /// (当前项高亮,选中回调+关菜单);菜单未开时方向键直接换选(±1 钳制,仅变化回调);
    /// Esc 不开菜单(开菜单时只关菜单)。选中态由调用方持有,回调报新下标。</summary>
    public static SelectElement Select(string[] options, int selectedIndex, Action<int> onChanged) =>
        new(options, selectedIndex, onChanged);

    /// <summary>竖向滚动容器(D19);与 Container 同构,但 new ScrollElement(默认 Vertical)。</summary>
    public static ScrollElement Scroll(params Element?[] children)
    {
        var scroll = new ScrollElement();
        foreach (Element? child in children)
            if (child is not null)
                scroll.Add(child);
        return scroll;
    }

    public static VirtualListElement VirtualList(string key, int count, float itemHeight, Action<int, PanelElement> render) => new(key, count, itemHeight, render);

    /// <summary>在锚定矩形处弹出菜单(命令式:按钮点击回调内调用;锚=触发元素 Frame,菜单左上对齐锚底缘)。
    /// items 为 MenuItem/MenuSeparator 混排(params object?[]);方向键上下移动高亮(禁用项跳过)、
    /// Enter 激活+关闭、Esc/外点关闭、焦点围栏(打开 Push/关闭归还)。sdlWindow 须为 App 建窗接线的句柄
    /// (FocusManager 窗注册表);AppWindow 扩展 <see cref="MenuExtensions.ShowMenu"/> 免句柄直调。</summary>
    public static void ShowMenu(IntPtr sdlWindow, RectF anchorRect, params object?[] items)
    {
        FocusManager? focus = FocusManager.ForWindow(sdlWindow);
        if (focus is null)
            throw new InvalidOperationException(
                "ShowMenu: 该句柄未绑定 FocusManager(App.CreateWindow 接线;合成测试经 FocusManager.BindWindow)。");
        MenuExtensions.Show(sdlWindow, focus, anchorRect, items);
    }

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

    /// <summary>给任意元素附悬停提示(F-1 Tooltip 经 PopupHost,D5 T3):悬停 ≥0.5s 于元素左下弹出
    /// 提示浮层,移出/点击移除;无焦点语义(Esc 穿越)。链式。</summary>
    public static T Tooltip<T>(this T e, string text) where T : Element { e.TooltipText = text; return e; }
}
