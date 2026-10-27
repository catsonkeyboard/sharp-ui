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
                new MenuSub("More", new MenuItem("Sub", () => { })),
                new MenuSub("DisSub", new MenuItem("Sub", () => { })) { Enabled = false },
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

            // MenuSub 尾绘 ▶ 右向箭头:启用态白色(255,255,255),禁用态 0x3A3A42(BGR [0x42,0x3A,0x3A]=[66,58,58])
            Element subEnabled = menu.Items[2];
            int subArrow = ((int)(subEnabled.Frame.Y + subEnabled.Frame.Height / 2f) * 128 + (int)(subEnabled.Frame.Right - 12f)) * 4;
            Assert.Equal(255, px[subArrow]);
            Assert.Equal(255, px[subArrow + 1]);
            Assert.Equal(255, px[subArrow + 2]);

            Element subDisabled = menu.Items[3];
            int subDisArrow = ((int)(subDisabled.Frame.Y + subDisabled.Frame.Height / 2f) * 128 + (int)(subDisabled.Frame.Right - 12f)) * 4;
            Assert.Equal(66, px[subDisArrow]);
            Assert.Equal(58, px[subDisArrow + 1]);
            Assert.Equal(58, px[subDisArrow + 2]);
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

    // ---- D6 跟进 Task 1: 翻转钳制 + SubMenu 子菜单 ----

    [Fact]
    public void FlipClamp_BottomOverflowFlipsUp_RightOverflowClampsLeft_DefaultUnclamped()
    {
        (AppWindow w, IntPtr h) = Window(10);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        // 1) 传入 screenSize=(200,100), anchor 在底缘 (10,80,40,15) → 底部溢出向上翻(y < 80)
        var bottomAnchor = new RectF(10, 80, 40, 15);
        var screen = new SizeF(200, 100);
        Ui.ShowMenu(h, bottomAnchor, screen,
            new MenuItem("剪切", () => { }),
            new MenuItem("复制", () => { }),
            new MenuItem("粘贴", () => { }));
        MenuElement flipped = (MenuElement)PopupHost.StackOf(h)[0].Root;
        Assert.True(flipped.Frame.Y < 80f);
        Assert.Equal(Math.Max(0f, bottomAnchor.Y - flipped.Frame.Height), flipped.Frame.Y);
        PopupHost.Clear(h);

        // 2) 右侧溢出: anchor 在右缘 (185,10,10,15) → x 钳制到 screen.Width - menuW
        var rightAnchor = new RectF(185, 10, 10, 15);
        w.ShowMenu(rightAnchor, screen,
            new MenuItem("较长菜单项文本", () => { }));
        MenuElement clamped = (MenuElement)PopupHost.StackOf(h)[0].Root;
        Assert.Equal(Math.Max(0f, screen.Width - clamped.Frame.Width), clamped.Frame.X);
        Assert.True(clamped.Frame.Right <= screen.Width);
        PopupHost.Clear(h);

        // 3) 无 screenSize(假句柄默认): 不翻转, 保持既有 anchor.Bottom
        Ui.ShowMenu(h, bottomAnchor,
            new MenuItem("剪切", () => { }),
            new MenuItem("复制", () => { }),
            new MenuItem("粘贴", () => { }));
        MenuElement unclamped = (MenuElement)PopupHost.StackOf(h)[0].Root;
        Assert.Equal(bottomAnchor.Bottom, unclamped.Frame.Y);
        Assert.Equal(bottomAnchor.X, unclamped.Frame.X);
        Cleanup(h);
    }

    [Fact]
    public void SubMenu_RightAndEnterExpand_LeftAndEscCloseSubOnly_RestoresParentItemFocus()
    {
        (AppWindow w, IntPtr h) = Window(11);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        Ui.ShowMenu(h, opener.Frame,
            new MenuItem("新建", () => { }),
            new MenuSub("导出",
                new MenuItem("PDF", () => { }),
                new MenuItem("PNG", () => { })));
        ComposeFrame(w, root);
        MenuElement mainMenu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        w.Focus.SetFocus(mainMenu.Items[1]);                   // 聚焦父项"导出"

        // 按 Right 展开子菜单: Count==2, 焦点入子首项
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Right, true, false) });
        Assert.Equal(2, PopupHost.Count(h));
        MenuElement subMenu = (MenuElement)PopupHost.StackOf(h)[1].Root;
        Assert.Equal(mainMenu.Items[1].Frame.Right, subMenu.Frame.X);
        Assert.Equal(mainMenu.Items[1].Frame.Y, subMenu.Frame.Y);
        Assert.True(w.Focus.IsFocused(subMenu.Items[0]));

        // 子菜单内按 Left: 仅关子菜单(Count==1), 焦点归还父菜单的 MenuSub 项
        ComposeFrame(w, root);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Left, true, false) });
        Assert.Equal(1, PopupHost.Count(h));
        Assert.True(w.Focus.IsFocused(mainMenu.Items[1]));

        // 父项聚焦按 Enter: 再次展开子菜单
        ComposeFrame(w, root);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        Assert.Equal(2, PopupHost.Count(h));
        subMenu = (MenuElement)PopupHost.StackOf(h)[1].Root;
        Assert.True(w.Focus.IsFocused(subMenu.Items[0]));

        // 子菜单内按 Esc: 仅关子菜单(Count==1), 焦点归还父项
        ComposeFrame(w, root);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(1, PopupHost.Count(h));
        Assert.True(w.Focus.IsFocused(mainMenu.Items[1]));
        Cleanup(h);
    }

    [Fact]
    public void SubMenu_ChildItemActivate_FiresCallback_ClosesEntireTree_RestoresOpenerFocus()
    {
        (AppWindow w, IntPtr h) = Window(12);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        int pdfClicks = 0, pngClicks = 0;

        Ui.ShowMenu(h, opener.Frame,
            new MenuSub("导出",
                new MenuItem("PDF", () => pdfClicks++),
                new MenuItem("PNG", () => pngClicks++)));
        ComposeFrame(w, root);

        // 1) 键盘路径: Enter 展开 → Down 到 PNG → Enter 激活 → 整棵收起(Count==0)
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        Assert.Equal(2, PopupHost.Count(h));
        ComposeFrame(w, root);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Down, true, false) });
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        Assert.Equal(0, pdfClicks);
        Assert.Equal(1, pngClicks);
        Assert.Equal(0, PopupHost.Count(h));
        Assert.True(w.Focus.IsFocused(opener));

        // 2) 鼠标路径: 点击父项展开 → 点击子项 PDF → 回调+整棵收起(Count==0)
        OpenerFrame(w, root, opener);
        Ui.ShowMenu(h, opener.Frame,
            new MenuSub("导出",
                new MenuItem("PDF", () => pdfClicks++),
                new MenuItem("PNG", () => pngClicks++)));
        MenuElement mainMenu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        PointF parentPt = Center(mainMenu.Items[0]);
        Interaction.Update(w.Session, root, new RawInput(parentPt, new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(parentPt, new[] { false }), w.Focus);
        Assert.Equal(2, PopupHost.Count(h));

        MenuElement subMenu = (MenuElement)PopupHost.StackOf(h)[1].Root;
        PointF childPt = Center(subMenu.Items[0]);
        Interaction.Update(w.Session, root, new RawInput(childPt, new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(childPt, new[] { false }), w.Focus);
        Assert.Equal(1, pdfClicks);
        Assert.Equal(0, PopupHost.Count(h));
        Assert.True(w.Focus.IsFocused(opener));
        Cleanup(h);
    }

    [Fact]
    public void SubMenu_Hover02s_Expands_SwitchingSiblingParent_ReplacesOldSubMenu()
    {
        double t = 0;
        Anim.TimeSource = () => t;
        (AppWindow w, IntPtr h) = Window(13);
        try
        {
            (PanelElement root, ButtonElement opener) = Declared(() => { });
            OpenerFrame(w, root, opener);

            Ui.ShowMenu(h, opener.Frame,
                new MenuSub("最近", new MenuItem("a.txt", () => { })),
                new MenuSub("导出", new MenuItem("PDF", () => { })));
            MenuElement mainMenu = (MenuElement)PopupHost.StackOf(h)[0].Root;
            PointF hoverA = Center(mainMenu.Items[0]);
            PointF hoverB = Center(mainMenu.Items[1]);

            // t=0: 起表, <0.2s 不展开
            Interaction.Update(w.Session, root, new RawInput(hoverA, Array.Empty<bool>()), w.Focus);
            t = 0.15;
            Interaction.Update(w.Session, root, new RawInput(hoverA, Array.Empty<bool>()), w.Focus);
            Assert.Equal(1, PopupHost.Count(h));

            // t=0.20: 达 0.2s 展开"最近"子菜单
            t = 0.20;
            Interaction.Update(w.Session, root, new RawInput(hoverA, Array.Empty<bool>()), w.Focus);
            Assert.Equal(2, PopupHost.Count(h));
            Popup subPopupA = PopupHost.StackOf(h)[1];

            // 移到兄弟父项"导出": 先起表, 满 0.2s 后替换旧子菜单
            t = 0.30;
            Interaction.Update(w.Session, root, new RawInput(hoverB, Array.Empty<bool>()), w.Focus);
            t = 0.40;
            Interaction.Update(w.Session, root, new RawInput(hoverB, Array.Empty<bool>()), w.Focus);
            Assert.Equal(2, PopupHost.Count(h));
            Assert.Same(subPopupA, PopupHost.StackOf(h)[1]);   // 未满 0.2s 仍为旧子菜单

            t = 0.51;
            Interaction.Update(w.Session, root, new RawInput(hoverB, Array.Empty<bool>()), w.Focus);
            Assert.Equal(2, PopupHost.Count(h));               // 旧子菜单已按身份 Remove, 新子菜单在栈
            Popup subPopupB = PopupHost.StackOf(h)[1];
            Assert.NotSame(subPopupA, subPopupB);
            Assert.Equal(mainMenu.Items[1].Frame.Y, subPopupB.Root.Frame.Y);
        }
        finally
        {
            Anim.TimeSource = null;
            Cleanup(h);
        }
    }

    [Fact]
    public void SubMenu_ThreeLevels_ThrowsInvalidOperationException()
    {
        (AppWindow w, IntPtr h) = Window(14);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Ui.ShowMenu(h, opener.Frame,
                new MenuSub("一级",
                    new MenuSub("二级",
                        new MenuItem("三级", () => { })))));
        Assert.Contains("SubMenu 最多 2 层", ex.Message);
        Cleanup(h);
    }

    [Fact]
    public void SubMenu_Disabled_SkippedInNavigation_IgnoresExpand()
    {
        double t = 0;
        Anim.TimeSource = () => t;
        (AppWindow w, IntPtr h) = Window(15);
        try
        {
            (PanelElement root, ButtonElement opener) = Declared(() => { });
            OpenerFrame(w, root, opener);

            Ui.ShowMenu(h, opener.Frame,
                new MenuItem("首项", () => { }),
                new MenuSub("禁用子菜单", new MenuItem("子项", () => { })) { Enabled = false },
                new MenuItem("末项", () => { }));
            ComposeFrame(w, root);
            MenuElement mainMenu = (MenuElement)PopupHost.StackOf(h)[0].Root;
            Assert.True(w.Focus.IsFocused(mainMenu.Items[0]));

            // Down 跳过禁用 MenuSub 直达"末项"
            w.Focus.HandleKeys(new[] { new KeyEventData(Key.Down, true, false) });
            Assert.True(w.Focus.IsFocused(mainMenu.Items[2]));

            // 点击与悬停禁用 MenuSub 均不展开
            PointF disPt = Center(mainMenu.Items[1]);
            Interaction.Update(w.Session, root, new RawInput(disPt, new[] { true }), w.Focus);
            Interaction.Update(w.Session, root, new RawInput(disPt, new[] { false }), w.Focus);
            t = 0.5;
            Interaction.Update(w.Session, root, new RawInput(disPt, Array.Empty<bool>()), w.Focus);
            Assert.Equal(1, PopupHost.Count(h));
        }
        finally
        {
            Anim.TimeSource = null;
            Cleanup(h);
        }
    }

    [Fact]
    public void SubMenu_RightOverflow_FlipsToLeftOfParentItem()
    {
        (AppWindow w, IntPtr h) = Window(16);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        var screen = new SizeF(140, 120);
        Ui.ShowMenu(h, new RectF(80, 10, 50, 20), screen,
            new MenuSub("导出格式",
                new MenuItem("导出为 PDF 文档", () => { })));
        ComposeFrame(w, root);
        MenuElement mainMenu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Right, true, false) });

        Assert.Equal(2, PopupHost.Count(h));
        MenuElement subMenu = (MenuElement)PopupHost.StackOf(h)[1].Root;
        Assert.Equal(Math.Max(0f, mainMenu.Items[0].Frame.X - subMenu.Frame.Width), subMenu.Frame.X);
        Cleanup(h);
    }
}
