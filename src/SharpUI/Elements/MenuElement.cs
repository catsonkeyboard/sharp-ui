namespace SharpUI;

/// <summary>菜单项描述(D5 T3):Label/OnClick 必备;Enabled=false 灰标签 0x3A3A42 + 退出焦点序 +
/// 方向键导航跳过 + 拒激活(指针/键盘统一 CanActivate 门)。</summary>
public sealed class MenuItem(string label, Action onClick)
{
    public string Label { get; } = label;
    public Action OnClick { get; } = onClick;
    public bool Enabled { get; init; } = true;
}

/// <summary>菜单分隔线标记类型:Ui.ShowMenu(params object?[] items) 中与 MenuItem 混排(渲染为 1px 0x2A2A30)。</summary>
public sealed class MenuSeparator;

/// <summary>菜单分隔线元素:上下 4px 让位 + 1px 横线 0x2A2A30;非命中目标(命中落回菜单根,点击不误判外点)。</summary>
internal sealed class MenuSeparatorElement : Element
{
    const float VerticalMargin = 4f;
    static readonly Color32 LineColor = Color32.FromRgb(0x2A2A30);

    protected internal override SizeF MeasureSelf(SizeF available) => new(0f, 1f + VerticalMargin * 2f);

    public override void Draw(in PaintContext pc)
    {
        // 1px 线横向拉满(列 Stretch 撑宽);退化 UV 纯色(Tessellator 手法),结构体运算零分配
        PointF uv = new(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f);
        Tessellator.AddRoundedRect(pc.Mesh,
            new RectF(Frame.X, Frame.Y + VerticalMargin, Frame.Width, 1f), 0f, LineColor, uv);
    }
}

/// <summary>菜单项元素(叶子,自绘标签文本):高亮=聚焦或悬停 0x3B82F6(整行,菜单 padding 内满宽);
/// 禁用=标签 0x3A3A42、无高亮。IActivatable=onClick+关菜单(三渠道同漏斗);IKeySteerable=Up/Down
/// 委托所属菜单导航(FocusManager.SteerFocused 聚焦项路由,禁用项不聚焦天然跳过)。
/// 叶子自绘而非 TextElement 子元素:合成测试(App.Atlas null)需确定性度量回退,容器路径的
/// MeasureSelf 不经子元素度量,故自持标签度量/绘制(缓存于 <see cref="_measured"/>)。</summary>
internal sealed class MenuItemElement : Element, IActivatable, IKeySteerable
{
    const float FontSize = 14f;
    const float PadH = 12f, PadV = 6f;
    static readonly Color32 Highlight = Color32.FromRgb(0x3B82F6);   // 悬停蓝(Button 悬停同源)
    static readonly Color32 DisabledFg = Color32.FromRgb(0x3A3A42);

    readonly MenuElement _owner;
    readonly MenuItem _item;
    SizeF _measured;   // 最近 MeasureSelf 缓存(Draw 垂直居中用;Measure 先于 Draw 的 Arrange 序保证)

    public MenuItemElement(MenuElement owner, MenuItem item)
    {
        _owner = owner;
        _item = item;
        IsHitTarget = true;
        Focusable = item.Enabled;
    }

    public bool Enabled => _item.Enabled;

    /// <summary>IActivatable:禁用拒激活(点击账本与回调一致)。</summary>
    public bool CanActivate => _item.Enabled;

    /// <summary>激活漏斗:用户回调先行(回调内可再开浮层),随后关本菜单(按身份,回调新开的浮层不被误弹)。</summary>
    public void InvokeClick()
    {
        if (!_item.Enabled) return;
        _item.OnClick();
        _owner.Close();
    }

    /// <summary>IKeySteerable:Up/Down 委托菜单导航;横向/Home/End 无菜单语义(返回 false 回落既有路由)。
    /// global:: 前缀:类内 Key 绑定继承的 Element.Key(string 属性),须全局限定到枚举类型。</summary>
    public bool HandleKey(global::SharpUI.Key key)
    {
        switch (key)
        {
            case global::SharpUI.Key.Up: _owner.Navigate(-1); return true;
            case global::SharpUI.Key.Down: _owner.Navigate(1); return true;
            default: return false;
        }
    }

    protected internal override SizeF MeasureSelf(SizeF available)
    {
        SizeF text = App.Atlas is null
            ? new SizeF(_item.Label.Length * FontSize * 0.6f, FontSize * 1.3f)   // 无图集回退(合成测试;生产 Run 恒有图集)
            : App.Atlas.Measure(_item.Label, FontSize);
        _measured = text;
        return new SizeF(text.Width + PadH * 2f, text.Height + PadV * 2f);
    }

    public override void Draw(in PaintContext pc)
    {
        string? id = ResolvedId;
        bool highlighted = _item.Enabled && (pc.Input.FocusedId == id || pc.Input.HoveredId == id);
        if (highlighted)
        {
            PointF uv = new(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f);
            Tessellator.AddRoundedRect(pc.Mesh, Frame, 0f, Highlight, uv);       // 整行高亮(结构体运算零分配)
        }
        Color32 fg = _item.Enabled ? Color32.White : DisabledFg;
        pc.Atlas.DrawRun(pc.Mesh, _item.Label,
            new PointF(Frame.X + PadH, Frame.Y + (Frame.Height - _measured.Height) / 2f),
            FontSize, false, fg);
    }
}

/// <summary>锚定菜单浮层根(D5 T3):浮层底色 0x1E1E24/圆角 6/padding 4 的竖列;项 Stretch 满宽。
/// 打开经 Ui.ShowMenu / AppWindow.ShowMenu(Menu 类 Push,锚=触发元素 Frame,菜单左上对齐锚底缘,
/// Push 前 Arrange 定格);焦点围栏入栈(PushScope),关闭(OnClosed → PopScopeOf 按身份)归还打开者。
/// 根为命中目标(T1 携带② —— 分隔线/项间距空白区点击不误判外点)。方向键导航见
/// <see cref="Navigate"/>;Enter/点击激活走 IActivatable;Esc/外点经 PopupHost 既有路由。</summary>
public sealed class MenuElement : PanelElement
{
    static readonly Color32 PanelBg = Color32.FromRgb(0x1E1E24);   // 浮层底色(Dialog 面板同源)

    readonly List<MenuItemElement> _items = new();
    IntPtr _owner = IntPtr.Zero;   // Show 接线(PopupHost 按窗服务定位键)
    Popup? _popup;                 // Close 按身份移除的实例凭证(Tooltip 可叠于其上,盲弹会误弹)
    FocusManager? _focus;
    bool _closed;

    internal MenuElement(object?[] items)
    {
        Background = PanelBg;
        CornerRadius = 6f;
        Style.Padding = new Thickness(4f);
        Style.CrossAlign = CrossAxisAlignment.Stretch;   // 项/分隔线满宽(高亮整行)
        IsHitTarget = true;                              // T1 携带②
        foreach (object? raw in items)
        {
            switch (raw)
            {
                case MenuItem mi:
                    var el = new MenuItemElement(this, mi);
                    Add(el);
                    _items.Add(el);
                    break;
                case MenuSeparator:
                    Add(new MenuSeparatorElement());    // 不入导航序
                    break;
                case null:
                    break;                              // params object?[] 空位跳过
                default:
                    throw new ArgumentException(
                        $"ShowMenu: 不支持的菜单项类型 {raw.GetType().Name}(MenuItem/MenuSeparator/null)", nameof(items));
            }
        }
    }

    /// <summary>可导航项序(分隔线除外;测试/调试观测用)。</summary>
    internal IReadOnlyList<MenuItemElement> Items => _items;

    /// <summary>绑定窗句柄/浮层实例/焦点管理(Show 在 Push 前接线)。</summary>
    internal void Bind(IntPtr owner, Popup popup, FocusManager focus)
    {
        _owner = owner;
        _popup = popup;
        _focus = focus;
    }

    /// <summary>OnClosed 收尾置位(外部 Pop/Remove 路径的幂等记账)。</summary>
    internal void NotifyClosed() => _closed = true;

    /// <summary>关闭:按身份 Remove(Tooltip 可叠于其上);幂等。</summary>
    internal void Close()
    {
        if (_closed || _owner == IntPtr.Zero || _popup is null) return;
        _closed = true;
        PopupHost.Remove(_owner, _popup);
    }

    /// <summary>首个可用项(Show 的初始焦点;全禁用返回 null)。</summary>
    internal MenuItemElement? FirstEnabled()
    {
        foreach (MenuItemElement item in _items)
            if (item.Enabled) return item;
        return null;
    }

    /// <summary>方向键导航(Up=-1/Down=+1):自当前聚焦项沿方向环移跳过禁用项;无焦时正向入首项/反向入末项;
    /// 全禁用为无操作。焦点即高亮(FocusedId 驱动绘制),禁用项不 Focusable 天然不停留。</summary>
    internal void Navigate(int direction)
    {
        if (_focus is null) return;
        int n = _items.Count;
        if (n == 0) return;
        int cur = -1;
        for (int i = 0; i < n; i++)
            if (_items[i].ResolvedId is not null && _items[i].ResolvedId == _focus.FocusedId) { cur = i; break; }
        int start = cur < 0 ? (direction > 0 ? 0 : n - 1) : cur;   // 无焦:正向=首项,反向=末项
        for (int step = cur < 0 ? 0 : 1; step <= n; step++)        // 自 start+direction 起环扫,跳禁用
        {
            int idx = ((start + direction * step) % n + n) % n;
            if (_items[idx].Enabled) { _focus.SetFocus(_items[idx]); return; }
        }
    }
}

/// <summary>Menu 打开 API(D5 T3)。DSL 裁定同 T2 Dialog:浮层打开为命令式动作(按钮点击回调内调用),
/// 非元素工厂 —— 自持扩展类。AppWindow 扩展免句柄(单窗/多窗皆直用 window.Focus);
/// <see cref="Ui.ShowMenu(IntPtr, RectF, object?[])"/> 句柄形态经 FocusManager 窗注册表定位围栏
/// (App.CreateWindow 接线;未注册句柄抛 InvalidOperationException —— 围栏不可定位时拒绝静默降级)。</summary>
public static class MenuExtensions
{
    public static void ShowMenu(this AppWindow window, RectF anchorRect, params object?[] items) =>
        Show(window.Focus.SdlWindow, window.Focus, anchorRect, items);

    /// <summary>打开锚定菜单:度量后 Arrange 于锚底缘左对齐(Push 前定格);PushScope 围栏 + 初始焦点
    /// =initialFocusIndex 项(D5 T4 Select 传当前项下标即"当前项高亮"),越界/禁用回落首个可用项;
    /// Esc/外点经 PopupHost 既有路由关闭(OnClosed → PopScopeOf 归还打开者焦点)。
    /// 已知边界(D5 T4 裁定入档,spec 裁定#46):无翻转/钳制 —— 锚贴屏底时菜单溢出屏幕
    /// (Push 时无屏幕矩形知识;修法需按窗屏幕矩形注册表,当前无近屏底菜单场景,缓议)。</summary>
    internal static void Show(IntPtr window, FocusManager focus, RectF anchorRect, object?[] items,
        int initialFocusIndex = -1)
    {
        var menu = new MenuElement(items);
        SizeF m = LayoutEngine.Measure(menu, new SizeF(float.PositiveInfinity, float.PositiveInfinity));
        LayoutEngine.Arrange(menu, new RectF(anchorRect.X, anchorRect.Bottom, m.Width, m.Height));
        var popup = new Popup(menu, anchorRect, PopupKind.Menu)
        {
            DismissOnOutsideClick = true,   // 外点关闭(Esc 走 PopTopNonTooltip,Tooltip 穿越)
            OnClosed = () => { menu.NotifyClosed(); focus.PopScopeOf(menu.ResolvedId!); },
        };
        menu.Bind(window, popup, focus);
        PopupHost.Push(window, popup);     // 根 id 指派("/__popupN")后围栏以前缀圈定子树
        focus.PushScope(menu.ResolvedId!);
        MenuItemElement? initial =                          // 初始焦点(Select 当前项高亮;缺省首可用项)
            initialFocusIndex >= 0 && initialFocusIndex < menu.Items.Count && menu.Items[initialFocusIndex].Enabled
                ? menu.Items[initialFocusIndex]
                : menu.FirstEnabled();
        if (initial is { } first)
            focus.SetFocus(first);         // 初始焦点入圈(打开者焦点已由 PushScope 记账)
    }
}
