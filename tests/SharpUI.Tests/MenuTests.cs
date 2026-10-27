using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>D5 T3(MenuElement 锚定菜单):合成事件经 Interaction.Update + 键盘经 FocusManager.HandleKeys
/// (模式同 DialogTests —— AppWindow 直构 + 假句柄接线,FocusManager.BindWindow 注册句柄供 Ui.ShowMenu(IntPtr)
/// 服务定位)。假句柄段 0xD50380(独立于 TooltipTests 0xD50300)。视觉经 headless D3D11 像素读回。</summary>
[Collection("interaction")]
public class MenuTests
{
    static IntPtr Win(int n) => (IntPtr)(0xD50380 + n);

    /// <summary>合成窗:AppWindow 直构 + 假句柄接线 + 句柄注册表绑定(Ui.ShowMenu(IntPtr) 的围栏服务定位)。</summary>
    static (AppWindow W, IntPtr H) Window(int n)
    {
        IntPtr h = Win(n);
        PopupHost.Clear(h);
        FocusManager.UnbindWindow(h);
        var w = new AppWindow();
        w.Focus.SdlWindow = h;
        w.Session.SdlWindow = h;
        FocusManager.BindWindow(h, w.Focus);
        return (w, h);
    }

    static void Cleanup(IntPtr h)
    {
        PopupHost.Clear(h);
        FocusManager.UnbindWindow(h);
    }

    /// <summary>声明树:全宽"打开"按钮(200×150 逻辑屏)。</summary>
    static (PanelElement Root, ButtonElement Opener) Declared(Action onOpen)
    {
        ButtonElement opener = new("打开", onOpen);
        PanelElement root = Ui.Column(opener);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 150));
        IdResolver.Assign(root);
        return (root, opener);
    }

    /// <summary>打开者帧:声明树注册 + 焦点落打开者(ShowMenu 的 PushScope 记账基准)。</summary>
    static void OpenerFrame(AppWindow w, PanelElement root, ButtonElement opener)
    {
        w.Focus.BeginFrame();
        TreeWalk.Register(root, w.Focus);
        w.Focus.SetFocus(opener);
    }

    /// <summary>帧组合(App.Run 注册序):声明树后逐层浮层树。</summary>
    static void ComposeFrame(AppWindow w, Element declaredRoot)
    {
        w.Focus.BeginFrame();
        TreeWalk.Register(declaredRoot, w.Focus);
        foreach (Popup p in PopupHost.StackOf(w.Focus.SdlWindow))
            TreeWalk.Register(p.Root, w.Focus);
    }

    static PointF Center(Element e) => new(e.Frame.X + e.Frame.Width / 2f, e.Frame.Y + e.Frame.Height / 2f);

    // ---- 打开 + 围栏 ----

    [Fact]
    public void ShowMenu_PushesAnchoredMenu_Fenced_InitialFocusFirstEnabled()
    {
        (AppWindow w, IntPtr h) = Window(1);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        Ui.ShowMenu(h, opener.Frame,
            new MenuItem("剪切", () => { }),
            new MenuItem("粘贴", () => { }) { Enabled = false },
            new MenuSeparator(),
            new MenuItem("删除", () => { }));

        Assert.Equal(1, PopupHost.Count(h));
        Popup p = PopupHost.StackOf(h)[0];
        Assert.Equal(PopupKind.Menu, p.Kind);
        Assert.Equal(opener.Frame, p.AnchorRect);              // 锚=触发元素 Frame
        Assert.True(p.DismissOnOutsideClick);                  // 外点关闭
        Assert.StartsWith("/__popup", p.Root.ResolvedId);      // 自动根键(T1 携带①)
        Assert.Equal(p.Root.ResolvedId, w.Focus.ActiveScopeId);// 围栏=菜单根子树
        Assert.False(w.Focus.IsFocused(opener));               // 打开者焦点已记账

        ComposeFrame(w, root);
        MenuElement menu = (MenuElement)p.Root;
        Assert.Equal(3, menu.Items.Count);                     // 分隔线不入导航序
        Assert.True(w.Focus.IsFocused(menu.Items[0]));         // 初始焦点=首个可用项(禁用次项跳过)
        Cleanup(h);
    }

    // ---- 键盘导航(方向键跳禁用 + 环移)----

    [Fact]
    public void ArrowKeys_MoveHighlight_SkipDisabled_WrapAround()
    {
        (AppWindow w, IntPtr h) = Window(2);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        Ui.ShowMenu(h, opener.Frame,
            new MenuItem("A", () => { }),
            new MenuItem("B", () => { }) { Enabled = false },
            new MenuItem("C", () => { }));
        ComposeFrame(w, root);
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        Assert.True(w.Focus.IsFocused(menu.Items[0]));         // A

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Down, true, false) });
        Assert.True(w.Focus.IsFocused(menu.Items[2]));         // Down:B 禁用跳过 → C
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Down, true, false) });
        Assert.True(w.Focus.IsFocused(menu.Items[0]));         // Down:环回 → A
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Up, true, false) });
        Assert.True(w.Focus.IsFocused(menu.Items[2]));         // Up:环回 → C(跳过 B)
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Up, true, false) });
        Assert.True(w.Focus.IsFocused(menu.Items[0]));         // Up → A
        Cleanup(h);
    }

    // ---- 激活/关闭(键盘)----

    [Fact]
    public void Enter_ActivatesCallback_ClosesMenu_RestoresOpenerFocus()
    {
        (AppWindow w, IntPtr h) = Window(3);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        int cutClicks = 0, pasteClicks = 0;
        Ui.ShowMenu(h, opener.Frame,
            new MenuItem("剪切", () => cutClicks++),
            new MenuItem("粘贴", () => pasteClicks++));
        ComposeFrame(w, root);
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        w.Focus.SetFocus(menu.Items[1]);                       // 聚焦"粘贴"

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });

        Assert.Equal(0, cutClicks);
        Assert.Equal(1, pasteClicks);                          // Enter 激活聚焦项回调
        Assert.Equal(0, PopupHost.Count(h));                   // 激活即关闭
        Assert.True(w.Focus.IsFocused(opener));                // 围栏出栈归还打开者焦点
    }

    [Fact]
    public void Escape_ClosesMenu_RestoresFocus_NoCallbackFires()
    {
        (AppWindow w, IntPtr h) = Window(4);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        int clicks = 0;
        Ui.ShowMenu(h, opener.Frame, new MenuItem("剪切", () => clicks++));
        ComposeFrame(w, root);

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });

        Assert.Equal(0, PopupHost.Count(h));                   // Esc → PopTopNonTooltip
        Assert.Equal(0, clicks);                               // 只关不触发
        Assert.True(w.Focus.IsFocused(opener));
    }

    // ---- 指针路径 ----

    [Fact]
    public void ItemClick_MousePath_ActivatesAndCloses()
    {
        (AppWindow w, IntPtr h) = Window(5);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        int cutClicks = 0;
        Ui.ShowMenu(h, opener.Frame,
            new MenuItem("剪切", () => cutClicks++),
            new MenuItem("粘贴", () => { }));
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        PointF cut = Center(menu.Items[0]);                    // Push 前 Arrange 已定格 Frame

        Interaction.Update(w.Session, root, new RawInput(cut, new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(cut, new[] { false }), w.Focus);

        Assert.Equal(1, cutClicks);                            // 按下+松开同项 → 激活
        Assert.Equal(0, PopupHost.Count(h));                   // 且关闭
        Assert.True(w.Focus.IsFocused(opener));                // 焦点归还
    }

    [Fact]
    public void OutsideClick_ClosesMenu_ConsumesPress()
    {
        (AppWindow w, IntPtr h) = Window(6);
        int openerClicks = 0;
        (PanelElement root, ButtonElement opener) = Declared(() => openerClicks++);
        OpenerFrame(w, root, opener);
        Ui.ShowMenu(h, opener.Frame, new MenuItem("剪切", () => { }));
        PointF outside = new(5, 148);                          // 菜单下方远处(锚在顶部,菜单高有限)

        Interaction.Update(w.Session, root, new RawInput(outside, new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(outside, new[] { false }), w.Focus);

        Assert.Equal(0, PopupHost.Count(h));                   // 外点 → Pop
        Assert.Equal(0, openerClicks);                         // 按压被消费(模态语义)
        Assert.True(w.Focus.IsFocused(opener));                // OnClosed → PopScopeOf 归还
    }

    [Fact]
    public void DisabledItem_ClickKeepsMenu_NavigationNeverLandsOnIt()
    {
        (AppWindow w, IntPtr h) = Window(7);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        int enabledClicks = 0;
        Ui.ShowMenu(h, opener.Frame,
            new MenuItem("禁用", () => { }) { Enabled = false },
            new MenuItem("可用", () => enabledClicks++));
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        PointF disabled = Center(menu.Items[0]);

        Interaction.Update(w.Session, root, new RawInput(disabled, new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(disabled, new[] { false }), w.Focus);
        Assert.Equal(1, PopupHost.Count(h));                   // 点击禁用项:不激活、不关闭
        Assert.Equal(0, enabledClicks);

        ComposeFrame(w, root);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Up, true, false) });   // 末项起反向
        Assert.True(w.Focus.IsFocused(menu.Items[1]));         // 导航永不停在禁用项
        Cleanup(h);
    }

    // ---- 叠栈(Dialog 上开菜单)----

    [Fact]
    public void MenuOverDialog_EscClosesMenuOnly_DialogIntactThenEscClosesDialog()
    {
        (AppWindow w, IntPtr h) = Window(8);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        w.ShowDialog("设置", p => { }, new DialogButton("确定", () => { }));
        ComposeFrame(w, root);
        Element dialogRoot = PopupHost.StackOf(h)[0].Root;
        Assert.Equal(dialogRoot.ResolvedId, w.Focus.ActiveScopeId);

        Ui.ShowMenu(h, new RectF(10, 60, 80, 20), new MenuItem("重做", () => { }));
        Assert.Equal(2, PopupHost.Count(h));                   // Menu 叠于 Dialog
        ComposeFrame(w, root);

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(1, PopupHost.Count(h));                   // Esc 只关栈顶 Menu
        Assert.Equal(PopupKind.Dialog, PopupHost.StackOf(h)[0].Kind);
        Assert.Equal(dialogRoot.ResolvedId, w.Focus.ActiveScopeId);   // Dialog 围栏复位

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(0, PopupHost.Count(h));                   // 再 Esc 关 Dialog
        Assert.True(w.Focus.IsFocused(opener));                // 双层收尾归还打开者
    }

    // ---- 入口形态 ----

    [Fact]
    public void ShowMenu_AppWindowExtension_UnboundHandleThrows_UnknownItemTypeThrows()
    {
        (AppWindow w, IntPtr h) = Window(9);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        w.ShowMenu(opener.Frame, new MenuItem("项", () => { }));   // AppWindow 扩展:免句柄
        Assert.Equal(1, PopupHost.Count(h));
        Cleanup(h);

        Assert.Throws<InvalidOperationException>(() =>            // 句柄未注册:围栏不可定位,拒绝静默降级
            Ui.ShowMenu(h, opener.Frame, new MenuItem("项", () => { })));
        Assert.Throws<ArgumentException>(() =>                    // 未知项类型:显式拒绝(AppWindow 扩展路径)
            w.ShowMenu(opener.Frame, "裸字符串"));
    }

    // ---- 视觉(headless D3D11 像素读回;真实图集度量)----

    [Fact]
    [Trait("Category", "Gpu")]
    public void Menu_Draw_SeparatorLine_Highlight_MenuBg()
    {
        using var ctx = D3D11Context.CreateHeadless(128, 160);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;                                     // TextElement 度量需图集(UiDslTests 惯例)
        try
        {
            var menu = new MenuElement(new object?[]
            {
                new MenuItem("Edit", () => { }),
                new MenuSeparator(),
                new MenuItem("Cut", () => { }) { Enabled = false },
            });
            menu.Key = "m";                                    // 稳定 id 同 PopupHost.Push 指派式
            menu.ResolvedId = "/m";
            IdResolver.AssignChildren(menu);
            SizeF m = LayoutEngine.Measure(menu, new SizeF(float.PositiveInfinity, float.PositiveInfinity));
            LayoutEngine.Arrange(menu, new RectF(8, 8, m.Width, m.Height));

            var input = new InputState(new PointF(-1, -1), false, false, false, null)
            { FocusedId = menu.Items[0].ResolvedId };          // 高亮=聚焦项
            var mesh = new MeshBuilder();
            menu.Draw(new PaintContext(mesh, atlas, input));
            using var renderer = new Renderer(ctx);
            renderer.Submit(mesh, new SizeF(128, 160));
            byte[] px = ctx.ReadBackMainFramebuffer();

            // 菜单底色 0x1E1E24(根 padding 区,避让项/线):BGR [0x24,0x1E,0x1E]=[36,30,30]
            Element sep = FindSeparator(menu);
            int bg = ((int)(menu.Frame.Y + 2f) * 128 + (int)(menu.Frame.X + 2f)) * 4;
            Assert.Equal(36, px[bg]);
            Assert.Equal(30, px[bg + 1]);
            Assert.Equal(30, px[bg + 2]);
            Assert.Equal(255, px[bg + 3]);

            // 分隔线 1px 0x2A2A30(线行中点,横向中部):BGR [0x30,0x2A,0x2A]=[48,42,42]
            float lineY = sep.Frame.Y + 4f;                    // 上下 4px 让位后 1px 线
            int line = ((int)lineY * 128 + (int)(menu.Frame.X + menu.Frame.Width / 2f)) * 4;
            Assert.Equal(48, px[line]);
            Assert.Equal(42, px[line + 1]);
            Assert.Equal(42, px[line + 2]);

            // 聚焦项高亮 0x3B82F6(项左缘内 padding 区,避让字形):BGR [0xF6,0x82,0x3B]=[246,130,59]
            Element item = menu.Items[0];
            int hl = ((int)(item.Frame.Y + item.Frame.Height / 2f) * 128 + (int)(item.Frame.X + 4f)) * 4;
            Assert.Equal(246, px[hl]);
            Assert.Equal(130, px[hl + 1]);
            Assert.Equal(59, px[hl + 2]);

            // 禁用项无高亮(同位置左缘=菜单底色,非蓝)
            Element disabled = menu.Items[1];
            int dis = ((int)(disabled.Frame.Y + disabled.Frame.Height / 2f) * 128 + (int)(disabled.Frame.X + 4f)) * 4;
            Assert.Equal(36, px[dis]);
            Assert.Equal(30, px[dis + 1]);
            Assert.Equal(30, px[dis + 2]);
        }
        finally
        {
            App.Atlas = null;                                  // 用后复位(静态悬垂防护)
        }
    }

    static Element FindSeparator(Element e)
    {
        if (e is MenuSeparatorElement sep) return sep;
        if (e.Children is not null)
            foreach (Element c in e.Children)
                if (FindSeparator(c) is { } hit) return hit;
        return null!;
    }
}
