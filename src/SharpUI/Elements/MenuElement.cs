namespace SharpUI;

/// <summary>菜单项描述(D5 T3):Label/OnClick 必备;Enabled=false 灰标签 0x3A3A42 + 退出焦点序 +
/// 方向键导航跳过 + 拒激活(指针/键盘统一 CanActivate 门)。</summary>
public sealed class MenuItem(string label, Action onClick)
{
    public string Label { get; } = label;
    public Action OnClick { get; } = onClick;
    public bool Enabled { get; init; } = true;
}

/// <summary>子菜单父项描述(D6 T1):与 MenuItem/MenuSeparator 同混排;上限 2 层(主菜单+一级子菜单)。</summary>
public sealed class MenuSub(string label, params object?[] children)
{
    public string Label { get; } = label;
    public object?[] Children { get; } = children;
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

/// <summary>菜单项元素(叶子/子菜单父项,自绘标签文本与 ▶ 子菜单箭头):高亮=聚焦或悬停 0x3B82F6(整行,菜单 padding 内满宽);
/// 禁用=标签/箭头 0x3A3A42、无高亮。IActivatable=叶子(onClick+整棵关菜单)/父项(展开子菜单);IKeySteerable=Up/Down
/// 委托所属菜单导航、Right 展开子菜单、Left 子菜单收起归还父项。
/// 叶子自绘而非 TextElement 子元素:合成测试(App.Atlas null)需确定性度量回退,容器路径的
/// MeasureSelf 不经子元素度量,故自持标签度量/绘制(缓存于 <see cref="_measured"/>)。</summary>
internal sealed class MenuItemElement : Element, IActivatable, IKeySteerable
{
    const float FontSize = 14f;
    const float PadH = 12f, PadV = 6f;
    const float ArrowSpace = 16f;
    const float ArrowSpan = 4f;
    const float ArrowHalf = 4f;
    const float StrokeHalf = 1.2f;
    static readonly Color32 Highlight = Color32.FromRgb(0x3B82F6);   // 悬停蓝(Button 悬停同源)
    static readonly Color32 DisabledFg = Color32.FromRgb(0x3A3A42);

    readonly MenuElement _owner;
    readonly string _label;
    readonly bool _enabled;
    readonly Action? _onClick;
    readonly MenuSub? _sub;
    SizeF _measured;   // 最近 MeasureSelf 缓存(Draw 垂直居中用;Measure 先于 Draw 的 Arrange 序保证)

    public MenuItemElement(MenuElement owner, MenuItem item)
    {
        _owner = owner;
        _label = item.Label;
        _enabled = item.Enabled;
        _onClick = item.OnClick;
        IsHitTarget = true;
        Focusable = item.Enabled;
    }

    public MenuItemElement(MenuElement owner, MenuSub sub)
    {
        _owner = owner;
        _label = sub.Label;
        _enabled = sub.Enabled;
        _sub = sub;
        IsHitTarget = true;
        Focusable = sub.Enabled;
    }

    public bool Enabled => _enabled;

    public bool IsSubMenu => _sub is not null;

    internal MenuSub? Sub => _sub;

    /// <summary>IActivatable:禁用拒激活(点击账本与回调一致)。</summary>
    public bool CanActivate => _enabled;

    /// <summary>激活漏斗:MenuSub 父项展开子菜单;叶子 MenuItem 用户回调先行(回调内可再开浮层),随后收起整棵菜单树。</summary>
    public void InvokeClick()
    {
        if (!_enabled) return;
        if (_sub is not null)
        {
            _owner.ExpandSubMenu(this);
            return;
        }
        _onClick?.Invoke();
        _owner.CloseTree();
    }

    /// <summary>IKeySteerable:Up/Down 委托菜单导航;Right 展开子菜单;Left 在子菜单内仅关闭子菜单归还父项。
    /// global:: 前缀:类内 Key 绑定继承的 Element.Key(string 属性),须全局限定到枚举类型。</summary>
    public bool HandleKey(global::SharpUI.Key key)
    {
        switch (key)
        {
            case global::SharpUI.Key.Up: _owner.Navigate(-1); return true;
            case global::SharpUI.Key.Down: _owner.Navigate(1); return true;
            case global::SharpUI.Key.Right:
                if (_sub is not null && _enabled)
                {
                    _owner.ExpandSubMenu(this);
                    return true;
                }
                return false;
            case global::SharpUI.Key.Left:
                if (_owner.ParentMenu is not null)
                {
                    _owner.Close();
                    return true;
                }
                return false;
            default: return false;
        }
    }

    protected internal override SizeF MeasureSelf(SizeF available)
    {
        SizeF text = App.Atlas is null
            ? new SizeF(_label.Length * FontSize * 0.6f, FontSize * 1.3f)   // 无图集回退(合成测试;生产 Run 恒有图集)
            : App.Atlas.Measure(_label, FontSize);
        _measured = text;
        float extraRight = _sub is not null ? ArrowSpace : 0f;
        return new SizeF(text.Width + PadH * 2f + extraRight, text.Height + PadV * 2f);
    }

    public override void Draw(in PaintContext pc)
    {
        string? id = ResolvedId;
        bool highlighted = _enabled && (pc.Input.FocusedId == id || pc.Input.HoveredId == id);
        PointF uv = new(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f);
        if (highlighted)
        {
            Tessellator.AddRoundedRect(pc.Mesh, Frame, 0f, Highlight, uv);       // 整行高亮(结构体运算零分配)
        }
        Color32 fg = _enabled ? Color32.White : DisabledFg;
        pc.Atlas.DrawRun(pc.Mesh, _label,
            new PointF(Frame.X + PadH, Frame.Y + (Frame.Height - _measured.Height) / 2f),
            FontSize, false, fg);
        if (_sub is not null)
        {
            float cx = Frame.Right - PadH, cy = Frame.Y + Frame.Height / 2f;
            AddStroke(pc.Mesh, new PointF(cx - ArrowSpan, cy - ArrowHalf), new PointF(cx, cy), StrokeHalf, fg, uv);
            AddStroke(pc.Mesh, new PointF(cx, cy), new PointF(cx - ArrowSpan, cy + ArrowHalf), StrokeHalf, fg, uv);
        }
    }

    /// <summary>线段展宽为 quad(4 顶点 2 三角,退化 UV = 纯色,复用 CheckboxElement/SelectElement 手法)。</summary>
    static void AddStroke(MeshBuilder mesh, PointF a, PointF b, float halfWidth, Color32 color, PointF uv)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float nx = -dy / len * halfWidth, ny = dx / len * halfWidth;
        mesh.AddTriangle(new PointF(a.X + nx, a.Y + ny), new PointF(b.X + nx, b.Y + ny), new PointF(b.X - nx, b.Y - ny), color, uv);
        mesh.AddTriangle(new PointF(a.X + nx, a.Y + ny), new PointF(b.X - nx, b.Y - ny), new PointF(a.X - nx, a.Y - ny), color, uv);
    }
}

/// <summary>锚定菜单浮层根(D5 T3 / D6 T1):浮层底色 0x1E1E24/圆角 6/padding 4 的竖列;项 Stretch 满宽。
/// 打开经 Ui.ShowMenu / AppWindow.ShowMenu(Menu 类 Push,锚=触发元素 Frame,支持屏幕翻转钳制与一级 SubMenu);
/// 焦点围栏入栈(PushScope),关闭(OnClosed → PopScopeOf 按身份)归还打开者。
/// 根为命中目标(T1 携带② —— 分隔线/项间距空白区点击不误判外点)。方向键导航见
/// <see cref="Navigate"/>;Enter/点击激活走 IActivatable;Esc/外点经 PopupHost 既有路由。</summary>
public sealed class MenuElement : PanelElement
{
    internal const double SubMenuHoverDelaySeconds = 0.2;
    static readonly Color32 PanelBg = Color32.FromRgb(0x1E1E24);   // 浮层底色(Dialog 面板同源)

    readonly List<MenuItemElement> _items = new();
    Popup? _popup;                 // Close 按身份移除的实例凭证(Tooltip/SubMenu 可叠于其上,盲弹会误弹)
    FocusManager? _focus;          // D6 T2 定位收敛:所属窗句柄唯一真相为 _focus.SdlWindow,不另存冗余句柄字段
    SizeF _screenSize = new(float.PositiveInfinity, float.PositiveInfinity);
    MenuElement? _activeSubMenu;
    MenuItemElement? _activeSubOwner;
    MenuItemElement? _hoverSubItem;
    double _hoverStart;
    bool _closed;

    internal MenuElement(object?[] items, MenuElement? parent = null)
    {
        ParentMenu = parent;
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
                case MenuSub sub:
                    if (parent is not null)
                        throw new InvalidOperationException("SubMenu 最多 2 层(主菜单+一级子菜单)");
                    ValidateSubChildren(sub.Children);
                    var subEl = new MenuItemElement(this, sub);
                    Add(subEl);
                    _items.Add(subEl);
                    break;
                case MenuSeparator:
                    Add(new MenuSeparatorElement());    // 不入导航序
                    break;
                case null:
                    break;                              // params object?[] 空位跳过
                default:
                    throw new ArgumentException(
                        $"ShowMenu: 不支持的菜单项类型 {raw.GetType().Name}(MenuItem/MenuSeparator/MenuSub/null)", nameof(items));
            }
        }
    }

    static void ValidateSubChildren(object?[] children)
    {
        if (children is null) return;
        foreach (object? c in children)
        {
            switch (c)
            {
                case MenuSub:
                    throw new InvalidOperationException("SubMenu 最多 2 层(主菜单+一级子菜单)");
                case MenuItem or MenuSeparator or null:
                    break;
                default:
                    throw new ArgumentException(
                        $"ShowMenu: 不支持的菜单项类型 {c.GetType().Name}(MenuItem/MenuSeparator/MenuSub/null)", nameof(children));
            }
        }
    }

    /// <summary>父菜单引用(null=主菜单;非 null=一级子菜单)。</summary>
    internal MenuElement? ParentMenu { get; }

    /// <summary>可导航项序(分隔线除外;测试/调试观测用)。</summary>
    internal IReadOnlyList<MenuItemElement> Items => _items;

    /// <summary>绑定浮层实例/焦点管理(含 SdlWindow 唯一真相)/屏幕尺寸(Show 在 Push 前接线)。</summary>
    internal void Bind(Popup popup, FocusManager focus, SizeF screenSize)
    {
        _popup = popup;
        _focus = focus;
        _screenSize = screenSize;
    }

    /// <summary>OnClosed 收尾置位(外部 Pop/Remove 路径的幂等记账)。</summary>
    internal void NotifyClosed() => _closed = true;

    /// <summary>关闭本层菜单:按身份 Remove(Tooltip/SubMenu 可叠于其上);若为父菜单则先按身份关闭已展开子菜单;幂等。</summary>
    internal void Close()
    {
        if (_closed || _focus is not { SdlWindow: var owner } || owner == IntPtr.Zero || _popup is null) return;
        CloseActiveSubMenu();
        _closed = true;
        PopupHost.Remove(owner, _popup);
    }

    /// <summary>收起整棵菜单树(子菜单叶子激活或菜单树外点消费):自根主菜单起连带关闭子菜单与主菜单。</summary>
    internal void CloseTree()
    {
        if (ParentMenu is { } parent)
            parent.CloseTree();
        else
            Close();
    }

    /// <summary>按身份移除当前已展开的子菜单(兄弟切换或主菜单关闭前收尾)。</summary>
    internal void CloseActiveSubMenu()
    {
        if (_activeSubMenu is not { } sub) return;
        _activeSubMenu = null;
        _activeSubOwner = null;
        sub.Close();
    }

    /// <summary>在给定 MenuSub 父项处展开一级子菜单:先按身份 Remove 兄弟旧子菜单;
    /// 锚=父项右缘,水平溢出左翻到 parentItem.X - subW,垂直溢出钳制;围栏入栈并聚焦子首项。</summary>
    internal void ExpandSubMenu(MenuItemElement parentItem)
    {
        if (_closed || _focus is not { SdlWindow: var owner } || owner == IntPtr.Zero
            || parentItem.Sub is not { } sub || !parentItem.Enabled)
            return;
        if (_activeSubMenu is { _closed: false } existing)
        {
            if (ReferenceEquals(_activeSubOwner, parentItem))
            {
                if (existing.FirstEnabled() is { } f) _focus.SetFocus(f);
                return;
            }
            CloseActiveSubMenu();
        }

        var subMenu = new MenuElement(sub.Children, parent: this);
        SizeF m = LayoutEngine.Measure(subMenu, new SizeF(float.PositiveInfinity, float.PositiveInfinity));
        var anchor = new RectF(parentItem.Frame.Right, parentItem.Frame.Y, 0f, parentItem.Frame.Height);
        float x = parentItem.Frame.Right;
        if (x + m.Width > _screenSize.Width)
            x = Math.Max(0f, parentItem.Frame.X - m.Width);
        float y = parentItem.Frame.Y;
        if (y + m.Height > _screenSize.Height)
            y = Math.Max(0f, _screenSize.Height - m.Height);
        LayoutEngine.Arrange(subMenu, new RectF(x, y, m.Width, m.Height));

        FocusManager focus = _focus;
        var popup = new Popup(subMenu, anchor, PopupKind.Menu)
        {
            DismissOnOutsideClick = true,
            OnClosed = () =>
            {
                subMenu.NotifyClosed();
                if (ReferenceEquals(_activeSubMenu, subMenu))
                {
                    _activeSubMenu = null;
                    _activeSubOwner = null;
                }
                focus.PopScopeOf(subMenu.ResolvedId!);
            },
        };
        subMenu.Bind(popup, focus, _screenSize);
        _activeSubMenu = subMenu;
        _activeSubOwner = parentItem;
        focus.SetFocus(parentItem);   // 确保子围栏 SavedFocusId 恒为该 MenuSub 父项(Left/Esc 归还焦点)
        PopupHost.Push(owner, popup);
        focus.PushScope(subMenu.ResolvedId!);
        TreeWalk.Register(subMenu, focus);
        if (subMenu.FirstEnabled() is { } first)
            focus.SetFocus(first);
    }

    /// <summary>每窗菜单悬停驱动(Interaction.Update 与 TooltipService.Update 共用,基于 Anim.Now 墙钟幂等):
    /// 悬停在主菜单启用的 MenuSub 父项持续 ≥0.2s 展开对应子菜单(切兄弟父项满 0.2s 替换旧子菜单)。</summary>
    internal static void UpdateWindowHover(IntPtr window, string? hoveredId, PointF mouse, bool leftPressed)
    {
        IReadOnlyList<Popup> stack = PopupHost.StackOf(window);
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i].Root is MenuElement { ParentMenu: null } mainMenu)
            {
                mainMenu.UpdateHover(hoveredId, mouse, leftPressed);
                return;
            }
        }
    }

    void UpdateHover(string? hoveredId, PointF mouse, bool leftPressed)
    {
        if (_closed || leftPressed)
        {
            _hoverSubItem = null;
            return;
        }
        MenuItemElement? hitSub = null;
        for (int i = 0; i < _items.Count; i++)
        {
            MenuItemElement item = _items[i];
            if (item.IsSubMenu && item.Enabled && item.ResolvedId is not null
                && item.ResolvedId == hoveredId && item.Frame.Contains(mouse))
            {
                hitSub = item;
                break;
            }
        }
        if (hitSub is null)
        {
            _hoverSubItem = null;
            return;
        }
        double now = Anim.Now;
        if (!ReferenceEquals(_hoverSubItem, hitSub))
        {
            _hoverSubItem = hitSub;
            _hoverStart = now;
        }
        if (now - _hoverStart + 1e-6 >= SubMenuHoverDelaySeconds)
            ExpandSubMenu(hitSub);
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

/// <summary>Menu 打开 API(D5 T3 / D6 T1)。DSL 裁定同 T2 Dialog:浮层打开为命令式动作(按钮点击回调内调用),
/// 非元素工厂 —— 自持扩展类。AppWindow 扩展免句柄(单窗/多窗皆直用 window.Focus);
/// <see cref="Ui.ShowMenu(IntPtr, RectF, object?[])"/> 句柄形态经 FocusManager 窗注册表定位围栏
/// (App.CreateWindow 接线;未注册句柄抛 InvalidOperationException —— 围栏不可定位时拒绝静默降级)。</summary>
public static class MenuExtensions
{
    public static void ShowMenu(this AppWindow window, RectF anchorRect, params object?[] items) =>
        Show(window.Focus, anchorRect, items, screenSize: window.Focus.ScreenSize);

    public static void ShowMenu(this AppWindow window, RectF anchorRect, SizeF? screenSize, params object?[] items) =>
        Show(window.Focus, anchorRect, items, screenSize: screenSize ?? window.Focus.ScreenSize);

    /// <summary>打开锚定菜单(D6 T2 定位收敛:窗口句柄唯一真相取自 focus.SdlWindow):度量后按屏幕尺寸做底部向上翻转
    /// (anchor.Bottom + menuH &gt; screen.Height → y = Math.Max(0, anchor.Y - menuH))与右侧钳制
    /// (anchor.X + menuW &gt; screen.Width → x = Math.Max(0, screen.Width - menuW)),未传 screenSize 则取 focus.ScreenSize 或无穷大不钳制;
    /// PushScope 围栏 + 初始焦点=initialFocusIndex 项(越界/禁用回落首个可用项);
    /// Esc/外点经 PopupHost 既有路由关闭(OnClosed → 连带关子菜单 + PopScopeOf 归还打开者焦点)。</summary>
    internal static void Show(FocusManager focus, RectF anchorRect, object?[] items,
        int initialFocusIndex = -1, SizeF? screenSize = null)
    {
        IntPtr window = focus.SdlWindow;
        var menu = new MenuElement(items);
        SizeF screen = ResolveScreenSize(screenSize ?? focus.ScreenSize);
        SizeF m = LayoutEngine.Measure(menu, new SizeF(float.PositiveInfinity, float.PositiveInfinity));
        float x = anchorRect.X;
        if (x + m.Width > screen.Width)
            x = Math.Max(0f, screen.Width - m.Width);
        float y = anchorRect.Bottom;
        if (y + m.Height > screen.Height)
            y = Math.Max(0f, anchorRect.Y - m.Height);
        LayoutEngine.Arrange(menu, new RectF(x, y, m.Width, m.Height));
        var popup = new Popup(menu, anchorRect, PopupKind.Menu)
        {
            DismissOnOutsideClick = true,   // 外点关闭(Esc 走 PopTopNonTooltip,Tooltip 穿越)
            OnClosed = () =>
            {
                menu.CloseActiveSubMenu();
                menu.NotifyClosed();
                focus.PopScopeOf(menu.ResolvedId!);
            },
        };
        menu.Bind(popup, focus, screen);
        PopupHost.Push(window, popup);     // 根 id 指派("/__popupN")后围栏以前缀圈定子树
        focus.PushScope(menu.ResolvedId!);
        MenuItemElement? initial =                          // 初始焦点(Select 当前项高亮;缺省首可用项)
            initialFocusIndex >= 0 && initialFocusIndex < menu.Items.Count && menu.Items[initialFocusIndex].Enabled
                ? menu.Items[initialFocusIndex]
                : menu.FirstEnabled();
        if (initial is { } first)
            focus.SetFocus(first);         // 初始焦点入圈(打开者焦点已由 PushScope 记账)
    }

    static SizeF ResolveScreenSize(SizeF? candidate) =>
        candidate is { Width: > 0f, Height: > 0f } s
            ? s
            : new SizeF(float.PositiveInfinity, float.PositiveInfinity);
}
