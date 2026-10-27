using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>D5 T4(SelectElement 下拉选择器):合成事件经 Interaction.Update + 键盘经
/// FocusManager.HandleKeys(模式同 MenuTests —— AppWindow 直构 + 假句柄接线,
/// FocusManager.BindWindow 注册句柄)。假句柄段 0xD504C0(独立于 TooltipTests 0xD50300 /
/// MenuTests 0xD50380)。视觉经 headless D3D11 像素读回;demo 段(About 弹窗/Tooltip 载体/
/// Select 三项回显)以状态断言承证(桌面锁定降级,WindowSmokeTests 式)。</summary>
[Collection("interaction")]
public class SelectTests
{
    static IntPtr Win(int n) => (IntPtr)(0xD504C0 + n);

    /// <summary>合成窗:AppWindow 直构 + 假句柄接线 + 句柄注册表绑定。</summary>
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

    /// <summary>声明树:全宽三选项 Select(selectedIndex=1),200×260 逻辑屏。
    /// 注册 + 聚焦 Select(打开菜单的 PushScope 记账基准)。</summary>
    static (PanelElement Root, SelectElement Sel) Declared(AppWindow w, Action<int>? onChanged = null, int selected = 1)
    {
        Action<int> callback = onChanged ?? (static _ => { });
        SelectElement sel = Ui.Select(new[] { "small", "medium", "large" }, selected, callback);
        PanelElement root = Ui.Column(sel);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 260));
        IdResolver.Assign(root);
        w.Focus.BeginFrame();
        TreeWalk.Register(root, w.Focus);
        w.Focus.SetFocus(sel);
        return (root, sel);
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

    static void Click(AppWindow w, Element root, PointF at)
    {
        Interaction.Update(w.Session, root, new RawInput(at, new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(at, new[] { false }), w.Focus);
    }

    // ---- 构造防御 + DSL ----

    [Fact]
    public void Ctor_EmptyOptions_Throws_OutOfRangeIndex_Throws()
    {
        Assert.Throws<ArgumentException>(() => Ui.Select(Array.Empty<string>(), 0, static _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ui.Select(new[] { "a" }, 1, static _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ui.Select(new[] { "a" }, -1, static _ => { }));
    }

    [Fact]
    public void UiSelect_Factory_EnabledGateSyncsFocusable_CurrentItemDisplayed()
    {
        SelectElement sel = Ui.Select(new[] { "small", "medium", "large" }, 2, static _ => { });
        Assert.True(sel.Enabled);
        Assert.True(sel.Focusable);
        sel.Enabled = false;                       // 禁用同步退出焦点序(D4 三控件同款)
        Assert.False(sel.Focusable);
        Assert.Equal("large", ((TextElement)sel.Children![0]).Text);   // 按钮形态显示当前项
    }

    // ---- 打开(指针/键盘)----

    [Fact]
    public void Click_OpensMenu_AnchoredAtFrame_CurrentItemHighlighted()
    {
        (AppWindow w, IntPtr h) = Window(1);
        (PanelElement root, SelectElement sel) = Declared(w);

        Click(w, root, Center(sel));

        Assert.Equal(1, PopupHost.Count(h));
        Popup p = PopupHost.StackOf(h)[0];
        Assert.Equal(PopupKind.Menu, p.Kind);
        Assert.Equal(sel.Frame, p.AnchorRect);                     // 锚=自身 Frame(菜单贴底缘弹出)
        Assert.Equal(p.Root.ResolvedId, w.Focus.ActiveScopeId);    // 焦点围栏=菜单子树
        MenuElement menu = (MenuElement)p.Root;
        Assert.True(w.Focus.IsFocused(menu.Items[1]));             // 当前项高亮(初始焦点=selectedIndex,非首项)
        Assert.False(w.Focus.IsFocused(menu.Items[0]));
        Cleanup(h);
    }

    [Fact]
    public void Keyboard_EnterAndSpace_OpenMenu_FocusOnCurrentItem()
    {
        (AppWindow w, IntPtr h) = Window(2);
        (PanelElement root, SelectElement sel) = Declared(w);

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) }, w.Session);
        Assert.Equal(1, PopupHost.Count(h));                       // Enter 打开
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        Assert.True(w.Focus.IsFocused(menu.Items[1]));

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(0, PopupHost.Count(h));                       // Esc 关闭
        Assert.True(w.Focus.IsFocused(sel));                       // 焦点归还 Select

        ComposeFrame(w, root);                                     // 关闭后重注册(围栏已出栈)
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Space, true, false) }, w.Session);
        Assert.Equal(1, PopupHost.Count(h));                       // Space 同为打开语义
        Cleanup(h);
    }

    /// <summary>I-1 终审回归:镜像 App.Run 真实帧序(C-2 每帧重建树)的键盘开菜单 —— 锚必须取 Select
    /// 排布后的 Frame 下缘,而非未排布默认 (0,0,0,0)(激活回调消费自身 Frame,Menu 类浮层 Push 后定格
    /// 不重排)。帧序镜像 App.Run 逐行(修复后序,须与 App.Run 同步改):构建→Assign→BeginFrame→
    /// Register→Arrange→HandleKeys;两帧驱动 —— 帧 N-1 经 Tab 聚焦(稳定 Key 跨帧保持 FocusedId),
    /// 帧 N Enter 激活开菜单。</summary>
    [Fact]
    public void KeyboardOpen_RunFrameOrder_MenuAnchoredAtArrangedFrameBottom()
    {
        (AppWindow w, IntPtr h) = Window(10);
        PanelElement Frame(out SelectElement sel)
        {
            sel = Ui.Select(new[] { "small", "medium", "large" }, 1, static _ => { });
            sel.Key = "size";                                    // 稳定键:FocusedId 跨帧(C-2 重建)
            PanelElement root = Ui.Column(new Spacer(new SizeF(0, 40)), sel);   // 前置让位:锚 Y>0 缺陷可判
            root.Style.CrossAlign = CrossAxisAlignment.Stretch;
            IdResolver.Assign(root);
            return root;
        }

        // 帧 N-1(App.Run 序):Arrange 后 Tab 聚焦 Select
        PanelElement r0 = Frame(out SelectElement s0);
        w.Focus.BeginFrame();
        TreeWalk.Register(r0, w.Focus);
        LayoutEngine.Arrange(r0, new RectF(0, 0, 200, 260));
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        w.LastTree = r0;
        Assert.True(w.Focus.IsFocused(s0));                      // 前置:焦点已落 Select

        // 帧 N(App.Run 序):Arrange 后 Enter 键盘开菜单
        PanelElement root = Frame(out SelectElement sel);
        w.Focus.BeginFrame();
        TreeWalk.Register(root, w.Focus);
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 260));
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) }, w.Session);

        Assert.Equal(1, PopupHost.Count(h));
        Popup p = PopupHost.StackOf(h)[0];
        Assert.Equal(PopupKind.Menu, p.Kind);
        Assert.Equal(sel.Frame, p.AnchorRect);                   // 锚=排布后 Frame(激活回调当帧几何)
        Assert.True(p.AnchorRect.Y > 0 && p.AnchorRect.Height > 0);   // 非未排布默认 (0,0,0,0)
        Assert.Equal(p.AnchorRect.Bottom, p.Root.Frame.Y);       // 菜单定格于锚下缘(Show 时 Arrange)
        Cleanup(h);
    }

    // ---- 选择(菜单内指针/键盘)----

    [Fact]
    public void MenuItem_Click_FiresChanged_ClosesMenu_RestoresFocus()
    {
        (AppWindow w, IntPtr h) = Window(3);
        int changed = -1, calls = 0;
        (PanelElement root, SelectElement sel) = Declared(w, i => { changed = i; calls++; });
        Click(w, root, Center(sel));
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;

        Click(w, root, Center(menu.Items[2]));                     // 指针点选第 3 项

        Assert.Equal(2, changed);                                  // 回调报新下标
        Assert.Equal(1, calls);
        Assert.Equal(0, PopupHost.Count(h));                       // 选中即关菜单
        Assert.True(w.Focus.IsFocused(sel));                       // 围栏归还
    }

    /// <summary>I-2 终审:点选"当前项"(selectedIndex=1 点第 1 项)不触发 onChanged —— 无变化即无回调
    /// (D4 回调纪律);菜单照关、焦点照还。</summary>
    [Fact]
    public void MenuItem_ClickCurrentItem_NoCallback_MenuCloses()
    {
        (AppWindow w, IntPtr h) = Window(11);
        int calls = 0;
        (PanelElement root, SelectElement sel) = Declared(w, _ => calls++);
        Click(w, root, Center(sel));
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;

        Click(w, root, Center(menu.Items[1]));                     // 点选当前项(无变化)

        Assert.Equal(0, calls);                                    // 不回调
        Assert.Equal(0, PopupHost.Count(h));                       // 菜单照关
        Assert.True(w.Focus.IsFocused(sel));                       // 焦点照还
        Cleanup(h);
    }

    [Fact]
    public void MenuOpen_ArrowsNavigate_EnterConfirms_SelectionChanged()
    {
        (AppWindow w, IntPtr h) = Window(4);
        int changed = -1, calls = 0;
        (PanelElement root, SelectElement sel) = Declared(w, i => { changed = i; calls++; }, selected: 0);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) }, w.Session);
        ComposeFrame(w, root);
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        Assert.True(w.Focus.IsFocused(menu.Items[0]));

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Down, true, false) });   // 开菜单时:菜单内导航
        Assert.True(w.Focus.IsFocused(menu.Items[1]));
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Down, true, false) });
        Assert.True(w.Focus.IsFocused(menu.Items[2]));
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Up, true, false) });
        Assert.True(w.Focus.IsFocused(menu.Items[1]));

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) }, w.Session);
        Assert.Equal(1, changed);                                  // Enter 确认聚焦项
        Assert.Equal(1, calls);
        Assert.Equal(0, PopupHost.Count(h));                       // 确认即关
        Assert.True(w.Focus.IsFocused(sel));
    }

    // ---- 直接换选(菜单未开)----

    [Fact]
    public void MenuClosed_Arrows_ChangeSelection_Directly_ClampedNoWrap()
    {
        (AppWindow w, IntPtr h) = Window(5);
        List<int> changes = new();
        int current = 1;
        // 回调式 C-2:选中态存应用侧,App.Run 帧序 = 每键一帧、按新值重建树(同实例重按与
        // Checkbox/Slider 同为陈旧值语义,框架契约外)——帧循环镜像生产路径。
        SelectElement Frame()
        {
            SelectElement sel = Ui.Select(new[] { "small", "medium", "large" }, current,
                i => { current = i; changes.Add(i); });
            PanelElement root = Ui.Column(sel);
            root.Style.CrossAlign = CrossAxisAlignment.Stretch;
            LayoutEngine.Arrange(root, new RectF(0, 0, 200, 260));
            IdResolver.Assign(root);
            w.Focus.BeginFrame();
            TreeWalk.Register(root, w.Focus);
            w.Focus.SetFocus(sel);
            return sel;
        }

        foreach (Key k in new[] { Key.Down, Key.Down, Key.Down,   // 1→2;2 钳制;再钳制
                                  Key.Up, Key.Up, Key.Up, Key.Up })   // 2→1→0;0 钳制;再钳制
        {
            Frame();
            w.Focus.HandleKeys(new[] { new KeyEventData(k, true, false) });
        }

        Assert.Equal(new[] { 2, 1, 0 }, changes);                  // ±1 换选,钳制不环移,仅变化回调
        Assert.Equal(0, PopupHost.Count(h));                       // 方向键直接换选不开菜单
    }

    // ---- Esc 语义 ----

    [Fact]
    public void Escape_NeverOpensMenu_ClosesOpenMenuWithoutCallback()
    {
        (AppWindow w, IntPtr h) = Window(6);
        int calls = 0;
        (PanelElement root, SelectElement sel) = Declared(w, _ => calls++);

        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(0, PopupHost.Count(h));                       // 菜单未开:Esc 不开菜单(清焦点既有语义)
        Assert.Null(w.Focus.FocusedId);

        ComposeFrame(w, root);
        w.Focus.SetFocus(sel);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) }, w.Session);
        Assert.Equal(1, PopupHost.Count(h));
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(0, PopupHost.Count(h));                       // 菜单开着:Esc 只关菜单
        Assert.Equal(0, calls);                                    // 不触发选中回调
        Assert.True(w.Focus.IsFocused(sel));                       // 焦点归还
    }

    // ---- 禁用 ----

    [Fact]
    public void Disabled_RejectsPointer_Click_Enter_Arrows()
    {
        (AppWindow w, IntPtr h) = Window(7);
        int calls = 0;
        (PanelElement root, SelectElement sel) = Declared(w, _ => calls++);
        sel.Enabled = false;

        Click(w, root, Center(sel));                               // 指针:不激活不打开
        Assert.Equal(0, PopupHost.Count(h));

        w.Focus.SetFocus(sel);                                     // 程序性聚焦下键盘同样拒激活
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) }, w.Session);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Space, true, false) }, w.Session);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Down, true, false) });
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Up, true, false) });
        Assert.Equal(0, PopupHost.Count(h));
        Assert.Equal(0, calls);                                    // 回调账本一致不记
    }

    // ---- 接线诚实性 ----

    [Fact]
    public void UnregisteredElement_ClickOpen_Throws()
    {
        (AppWindow w, IntPtr h) = Window(8);
        SelectElement sel = Ui.Select(new[] { "a", "b" }, 0, static _ => { });
        PanelElement root = Ui.Column(sel);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 260));
        IdResolver.Assign(root);                                   // 有 id、未注册(无 OwnerFocus 接线)

        Assert.Throws<InvalidOperationException>(() => Click(w, root, Center(sel)));   // 拒绝静默降级
        Cleanup(h);
    }

    // ---- demo 段承证(About 弹窗 + Tooltip 载体 + Select 三项回显;状态断言,桌面锁定降级)----

    [Fact]
    public void DemoSegment_AboutDialog_TooltipCarrier_SelectRoundtrip()
    {
        (AppWindow w, IntPtr h) = Window(9);
        int sizeIndex = 1, dialogOpens = 0;
        string[] sizes = { "small", "medium", "large" };
        ButtonElement about = new("About", () =>
        {
            dialogOpens++;
            w.ShowDialog("About", p => p.Add(Ui.Text("SharpUI D5: Dialog/Tooltip/Menu/Select").Size(14f)),
                new DialogButton("OK", () => { }) { IsDefault = true, IsCancel = true });
        });
        CheckboxElement cb = Ui.Checkbox("check", false, null).Tooltip("提示文本");
        SelectElement Select() => Ui.Select(sizes, sizeIndex, i => sizeIndex = i);
        SelectElement sel = Select();
        PanelElement root = Ui.Column(about, cb, sel);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 260));
        IdResolver.Assign(root);

        ComposeFrame(w, root);
        Click(w, root, Center(about));                             // About → ShowDialog
        Assert.Equal(1, dialogOpens);
        Assert.Equal(1, PopupHost.Count(h));
        Assert.Equal(PopupKind.Dialog, PopupHost.StackOf(h)[0].Kind);
        w.Focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });   // IsCancel/IsDefault OK 按钮可 Esc
        Assert.Equal(0, PopupHost.Count(h));

        ComposeFrame(w, root);
        Click(w, root, Center(sel));                               // Select 点击 → 菜单
        Assert.Equal(1, PopupHost.Count(h));
        MenuElement menu = (MenuElement)PopupHost.StackOf(h)[0].Root;
        Click(w, root, Center(menu.Items[2]));                     // 选 "large"
        Assert.Equal(2, sizeIndex);

        SelectElement rebuilt = Select();                          // 树每帧重建:回显新选中项
        Assert.Equal("large", ((TextElement)rebuilt.Children![0]).Text);
        Assert.Equal("提示文本", cb.TooltipText);                  // Tooltip 载体在树内(悬停 0.5s 弹提示)
        Cleanup(h);
    }

    // ---- 视觉(headless D3D11 像素读回;真实图集度量)----

    [Fact]
    [Trait("Category", "Gpu")]
    public void Select_Draw_ButtonForm_Arrow_DisabledPalette()
    {
        using var ctx = D3D11Context.CreateHeadless(220, 120);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        try
        {
            SelectElement normal = Ui.Select(new[] { "small", "medium", "large" }, 1, static _ => { });
            SelectElement disabled = Ui.Select(new[] { "small", "medium", "large" }, 1, static _ => { });
            disabled.Enabled = false;
            foreach ((SelectElement sel, float y) in new[] { (normal, 8f), (disabled, 60f) })
            {
                sel.Key = "s" + y;
                sel.ResolvedId = "/" + sel.Key;                    // 稳定 id 同 PopupHost.Push 指派式
                IdResolver.AssignChildren(sel);
                SizeF m = LayoutEngine.Measure(sel, new SizeF(float.PositiveInfinity, float.PositiveInfinity));
                LayoutEngine.Arrange(sel, new RectF(8, y, m.Width, m.Height));
            }

            var input = new InputState(new PointF(-1, -1), false, false, false, null);
            var mesh = new MeshBuilder();
            normal.Draw(new PaintContext(mesh, atlas, input));
            disabled.Draw(new PaintContext(mesh, atlas, input));
            using var renderer = new Renderer(ctx);
            renderer.Submit(mesh, new SizeF(220, 120));
            byte[] px = ctx.ReadBackMainFramebuffer();

            // 常态底 = Button 蓝系 0x0C63E4(左侧 padding 区避让文本):BGR [0xE4,0x63,0x0C]=[228,99,12]
            int bg = ((int)(normal.Frame.Y + normal.Frame.Height / 2f) * 220 + (int)(normal.Frame.X + 4f)) * 4;
            Assert.Equal(228, px[bg]);
            Assert.Equal(99, px[bg + 1]);
            Assert.Equal(12, px[bg + 2]);

            // 下拉箭头 ▼ 白色(左斜笔画中点,StrokeHalf 1.2 带内)
            float cx = normal.Frame.Right - 20f, cy = normal.Frame.Y + normal.Frame.Height / 2f;
            int arrow = ((int)cy * 220 + (int)(cx - 2f)) * 4;
            Assert.Equal(255, px[arrow]);
            Assert.Equal(255, px[arrow + 1]);
            Assert.Equal(255, px[arrow + 2]);

            // 禁用底 0x24242A + 箭头 0x6A6A74(Checkbox 禁用色板同源)
            int dbg = ((int)(disabled.Frame.Y + disabled.Frame.Height / 2f) * 220 + (int)(disabled.Frame.X + 4f)) * 4;
            Assert.Equal(42, px[dbg]);
            Assert.Equal(36, px[dbg + 1]);
            Assert.Equal(36, px[dbg + 2]);
            float dcx = disabled.Frame.Right - 20f, dcy = disabled.Frame.Y + disabled.Frame.Height / 2f;
            int dar = ((int)dcy * 220 + (int)(dcx - 2f)) * 4;
            Assert.Equal(116, px[dar]);                            // 0x74
            Assert.Equal(106, px[dar + 1]);                        // 0x6A
            Assert.Equal(106, px[dar + 2]);
        }
        finally
        {
            App.Atlas = null;                                      // 用后复位(静态悬垂防护)
        }
    }
}
