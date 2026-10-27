using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>D5 T2(DialogElement 模态对话框):合成事件经 Interaction.Update + 键盘经 FocusManager.HandleKeys
/// (模式同 PopupHostTests —— AppWindow 直构 + Focus/Session 注入假句柄,CreateWindow 同款接线,免原生窗)。
/// 已知项 A(浮层内 TextField 字符流)经 App.FeedFocused 合成驱动;已知项 B(resize 居中重算)经元素级
/// 重复 Arrange 承证(App.Run 对 Dialog 类浮层根的每帧重排为其调用方);视觉经 headless D3D11 像素读回。</summary>
[Collection("interaction")]
public class DialogTests
{
    /// <summary>每测 +N 的假句柄(独立于 PopupHostTests 的 0xD501xx 段;IntPtr 值仅作字典键,不触原生)。</summary>
    static IntPtr Win(int n) => (IntPtr)(0xD50200 + n);

    /// <summary>合成窗:AppWindow 直构(Focus/Session 内联初始化)+ 假句柄接线。</summary>
    static (AppWindow W, IntPtr H) Window(int n)
    {
        IntPtr h = Win(n);
        PopupHost.Clear(h);
        var w = new AppWindow();
        w.Focus.SdlWindow = h;
        w.Session.SdlWindow = h;
        return (w, h);
    }

    /// <summary>声明树:全宽"打开"按钮;Arrange+Assign 就绪(200×150 逻辑屏)。</summary>
    static (PanelElement Root, ButtonElement Opener) Declared(Action onOpen)
    {
        ButtonElement opener = new("打开", onOpen);
        PanelElement root = Ui.Column(opener);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 150));
        IdResolver.Assign(root);
        return (root, opener);
    }

    /// <summary>打开者帧(App.Run 等价):声明树注册 + 焦点落打开者 —— ShowDialog 的 PushScope 记账基准。</summary>
    static void OpenerFrame(AppWindow w, PanelElement root, ButtonElement opener)
    {
        w.Focus.BeginFrame();
        TreeWalk.Register(root, w.Focus);
        w.Focus.SetFocus(opener);
    }

    /// <summary>帧组合(App.Run 注册序):BeginFrame + 声明树后逐层浮层树注册;围栏栈跨帧存活(T1)。</summary>
    static void ComposeFrame(AppWindow w, Element declaredRoot)
    {
        w.Focus.BeginFrame();
        TreeWalk.Register(declaredRoot, w.Focus);
        foreach (Popup p in PopupHost.StackOf(w.Focus.SdlWindow))
            TreeWalk.Register(p.Root, w.Focus);
    }

    /// <summary>Dialog 类浮层根 Arrange 到 200×150 逻辑屏(App.Run 对 Dialog 每帧重排的合成等价,
    /// 只排 Dialog 类 —— Menu/Tooltip 打开者定格语义)。</summary>
    static void ArrangeDialogs(IntPtr h)
    {
        foreach (Popup p in PopupHost.StackOf(h))
            if (p.Kind == PopupKind.Dialog)
                LayoutEngine.Arrange(p.Root, new RectF(0, 0, 200, 150));
    }

    /// <summary>深序收集 Dialog 树内全部 ButtonElement(按钮行序 = params 序)。</summary>
    static List<ButtonElement> ButtonsOf(Element e)
    {
        var list = new List<ButtonElement>();
        Collect(e, list);
        return list;
        static void Collect(Element x, List<ButtonElement> into)
        {
            if (x is ButtonElement b) into.Add(b);
            if (x.Children is not null)
                foreach (Element c in x.Children) Collect(c, into);
        }
    }

    static PointF Center(Element e) => new(e.Frame.X + e.Frame.Width / 2f, e.Frame.Y + e.Frame.Height / 2f);

    // ---- 打开 + 围栏(F-2)----

    [Fact]
    public void ShowDialog_OpenFenced_CountOne_TabCyclesInsideOnly()
    {
        (AppWindow w, IntPtr h) = Window(1);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        CheckboxElement cb = Ui.Checkbox("选", false, null);
        w.ShowDialog("设置", p => p.Add(cb),
            new DialogButton("取消", () => { }), new DialogButton("确定", () => { }));

        Assert.Equal(1, PopupHost.Count(h));
        Element dialogRoot = PopupHost.StackOf(h)[0].Root;
        Assert.StartsWith("/__popup", dialogRoot.ResolvedId);   // T1 评审携带①:自动根键 __popupN 防与用户键碰撞
        Assert.Equal(dialogRoot.ResolvedId, w.Focus.ActiveScopeId);   // 围栏=浮层根子树
        Assert.False(w.Focus.IsFocused(opener));                // 打开者焦点已记账,焦点移入圈内

        ComposeFrame(w, root);
        Assert.True(w.Focus.IsFocused(cb));                     // 初始焦点=Dialog 内首个可聚焦(DOM 序,内容优先)
        w.Focus.HandleKeys(new[] { new KeyEventData(SharpUI.Key.Tab, true, false) });
        Assert.NotEqual(cb.ResolvedId, w.Focus.FocusedId);      // 圈内多项(checkbox+两按钮),Tab 确实移动

        string scope = dialogRoot.ResolvedId!;
        for (int i = 0; i < 6; i++)
        {
            w.Focus.HandleKeys(new[] { new KeyEventData(SharpUI.Key.Tab, true, false) });
            Assert.StartsWith(scope, w.Focus.FocusedId, StringComparison.Ordinal);   // Tab 环回永不出圈
            Assert.False(w.Focus.IsFocused(opener));
        }
        PopupHost.Clear(h);
    }

    [Fact]
    public void DialogButton_Click_FiresCallback_PopsAndRestoresOpenerFocus()
    {
        (AppWindow w, IntPtr h) = Window(2);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        int okClicks = 0;
        w.ShowDialog("确认", p => { }, new DialogButton("确定", () => okClicks++));
        ArrangeDialogs(h);

        ButtonElement ok = ButtonsOf(PopupHost.StackOf(h)[0].Root)[0];
        Interaction.Update(w.Session, root, new RawInput(Center(ok), new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(Center(ok), new[] { false }), w.Focus);

        Assert.Equal(1, okClicks);                    // 按钮回调触发
        Assert.Equal(0, PopupHost.Count(h));          // 点击=关闭(Pop)
        Assert.True(w.Focus.IsFocused(opener));       // OnClosed → PopScope → 归还打开者焦点
    }

    [Fact]
    public void Escape_Closes_RestoresFocusToOpener_NoButtonFires()
    {
        (AppWindow w, IntPtr h) = Window(3);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        int okClicks = 0;
        w.ShowDialog("提示", p => { }, new DialogButton("确定", () => okClicks++));
        ComposeFrame(w, root);
        w.Focus.HandleKeys(new[] { new KeyEventData(SharpUI.Key.Escape, true, false) });

        Assert.Equal(0, PopupHost.Count(h));          // Esc → 关闭
        Assert.Equal(0, okClicks);                    // 无 IsCancel:Esc 只关不触发按钮
        Assert.True(w.Focus.IsFocused(opener));       // 焦点归还打开者
    }

    [Fact]
    public void IsCancel_EscEquivalent_FiresOnClickAndCloses()
    {
        (AppWindow w, IntPtr h) = Window(4);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        int okClicks = 0, cancelClicks = 0;
        w.ShowDialog("提示", p => { },
            new DialogButton("确定", () => okClicks++),
            new DialogButton("取消", () => cancelClicks++) { IsCancel = true });
        ComposeFrame(w, root);
        w.Focus.HandleKeys(new[] { new KeyEventData(SharpUI.Key.Escape, true, false) });

        Assert.Equal(1, cancelClicks);                // IsCancel=Esc 等效:合成点击
        Assert.Equal(0, okClicks);                    // 其他按钮不受扰
        Assert.Equal(0, PopupHost.Count(h));          // 且关闭
        Assert.True(w.Focus.IsFocused(opener));       // 焦点归还
    }

    [Fact]
    public void IsDefault_EnterOnNonActivatableFocus_Fires_FocusedButtonWinsOtherwise()
    {
        (AppWindow w, IntPtr h) = Window(5);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        TextFieldElement tf = Ui.TextField();
        int okClicks = 0;
        w.ShowDialog("重命名", p => p.Add(tf),
            new DialogButton("确定", () => okClicks++) { IsDefault = true },
            new DialogButton("取消", () => { }));
        ComposeFrame(w, root);
        Assert.True(w.Focus.IsFocused(tf));           // 初始焦点=内容 TextField(非按钮)
        w.Focus.HandleKeys(new[] { new KeyEventData(SharpUI.Key.Enter, true, false) });
        Assert.Equal(1, okClicks);                    // IsDefault=Enter(焦点在非按钮时)
        Assert.Equal(0, PopupHost.Count(h));          // 触发即关闭

        // 对照:焦点在按钮上 → Enter 激活按钮自身(既有激活语义),不落 IsDefault
        int ok2 = 0, cancel2 = 0;
        w.ShowDialog("重命名", p => { },
            new DialogButton("确定", () => ok2++) { IsDefault = true },
            new DialogButton("取消", () => cancel2++));
        ComposeFrame(w, root);
        w.Focus.SetFocus(ButtonsOf(PopupHost.StackOf(h)[0].Root)[1]);   // 焦点=取消按钮(非默认)
        w.Focus.HandleKeys(new[] { new KeyEventData(SharpUI.Key.Enter, true, false) });
        Assert.Equal(1, cancel2);                     // 聚焦按钮赢
        Assert.Equal(0, ok2);                         // IsDefault 不触发
        PopupHost.Clear(h);
    }

    [Fact]
    public void MaskClick_DismissesByDefault_SwallowedWhenConfiguredOff()
    {
        (AppWindow w, IntPtr h) = Window(6);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        PointF mask = new(2, 2);                      // 屏角落=遮罩(面板居中不可达)

        w.ShowDialog("提示", p => { }, new DialogButton("确定", () => { }));
        ArrangeDialogs(h);
        Interaction.Update(w.Session, root, new RawInput(mask, new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(mask, new[] { false }), w.Focus);
        Assert.Equal(0, PopupHost.Count(h));          // 默认:遮罩点击关闭
        Assert.True(w.Focus.IsFocused(opener));       // 关闭归还焦点

        w.ShowDialog("表单", p => { }, dismissOnMaskClick: false, new DialogButton("确定", () => { }));
        ArrangeDialogs(h);
        Interaction.Update(w.Session, root, new RawInput(mask, new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(mask, new[] { false }), w.Focus);
        Assert.Equal(1, PopupHost.Count(h));          // 配置关:遮罩点击吞按压,不关闭
        Assert.False(w.Focus.IsFocused(opener));      // 焦点留守围栏内(模态陷阱语义)
        PopupHost.Clear(h);
    }

    [Fact]
    public void Build_InjectsArbitraryElements_ContentInteractiveInPlace()
    {
        (AppWindow w, IntPtr h) = Window(7);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        int toggles = 0;
        CheckboxElement cb = Ui.Checkbox("选项", false, _ => toggles++);
        w.ShowDialog("设置", p => p.Add(cb), new DialogButton("确定", () => { }));
        ArrangeDialogs(h);

        Interaction.Update(w.Session, root, new RawInput(Center(cb), new[] { true }), w.Focus);
        Interaction.Update(w.Session, root, new RawInput(Center(cb), new[] { false }), w.Focus);
        Assert.Equal(1, toggles);                     // 内容控件在浮层内正常交互
        Assert.Equal(1, PopupHost.Count(h));          // 点内容不关 Dialog

        ComposeFrame(w, root);
        Assert.True(w.Focus.IsFocused(cb));           // 初始焦点=内容首项(入围栏,Tab 可达)
        PopupHost.Clear(h);
    }

    [Fact]
    public void ShowDialog_SecondWhileOpen_Throws_ClosedThenReopens()
    {
        (AppWindow w, IntPtr h) = Window(8);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        w.ShowDialog("一", p => { }, new DialogButton("确定", () => { }));
        Assert.Throws<InvalidOperationException>(() => w.ShowDialog("二", p => { }));
        Assert.Equal(1, PopupHost.Count(h));          // 首个 Dialog 完好(断言不扰动栈)

        w.Focus.HandleKeys(new[] { new KeyEventData(SharpUI.Key.Escape, true, false) });
        w.ShowDialog("二", p => { }, new DialogButton("确定", () => { }));
        Assert.Equal(1, PopupHost.Count(h));          // 关闭后可再开
        PopupHost.Clear(h);
    }

    // ---- 已知项 A:浮层内 TextField 字符流(T1 留置,T2 接线)----

    [Fact]
    public void TextFieldInDialog_ReceivesCharStream_FeedFocusedSearchesPopupStack()
    {
        (AppWindow w, IntPtr h) = Window(9);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);
        App.State = new StateStore();   // TextField.Feed 依赖状态存储(隔离惯例:finally 复位)
        try
        {
            TextFieldElement tf = Ui.TextField();
            w.ShowDialog("重命名", p => p.Add(tf), new DialogButton("确定", () => { }));
            ComposeFrame(w, root);
            Assert.True(w.Focus.IsFocused(tf));

            App.FeedFocused(root, w.Focus, h,
                new RawInput(new PointF(-1f, -1f), Array.Empty<bool>()) { Chars = new[] { 'a', 'b' } });
            Assert.Equal("ab", tf.CurrentText);       // 声明树未命中 → 浮层栈根兜底命中
        }
        finally
        {
            App.State = null;
            PopupHost.Clear(h);
        }
    }

    // ---- 已知项 B:resize 居中重算(T1 留置,T2 裁定每帧重排;元素级重复 Arrange 承证)----

    [Fact]
    public void Dialog_ReArrange_RecentersPanelForNewScreen()
    {
        (AppWindow w, IntPtr h) = Window(10);
        (PanelElement root, ButtonElement opener) = Declared(() => { });
        OpenerFrame(w, root, opener);

        w.ShowDialog("提示", p => p.Add(new Spacer(new SizeF(80, 20))), new DialogButton("确定", () => { }));
        DialogElement dlg = (DialogElement)PopupHost.StackOf(h)[0].Root;

        LayoutEngine.Arrange(dlg, new RectF(0, 0, 100, 100));
        Assert.Equal(100f, dlg.Frame.Width);          // 根=全屏遮罩
        Assert.InRange(dlg.Panel.Frame.X + dlg.Panel.Frame.Width / 2f, 48f, 52f);   // 面板水平居中(100 宽)

        LayoutEngine.Arrange(dlg, new RectF(0, 0, 300, 100));   // resize → Run 每帧重排(T2 接线)
        Assert.Equal(300f, dlg.Frame.Width);
        Assert.InRange(dlg.Panel.Frame.X + dlg.Panel.Frame.Width / 2f, 148f, 152f); // 居中随屏重算
        PopupHost.Clear(h);
    }

    // ---- 视觉(headless D3D11 像素读回;空 label 隔离字体面)----

    [Fact]
    [Trait("Category", "Gpu")]
    public void Dialog_Draw_MaskSemiTransparent_PanelOpaque()
    {
        using var ctx = D3D11Context.CreateHeadless(256, 256);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        var dialog = new DialogElement("", p => p.Add(new Spacer(new SizeF(120, 24))),
            new[] { new DialogButton("", () => { }), new DialogButton("", () => { }) });
        dialog.Key = "dlg";                            // 稳定 id 同 PopupHost.Push 指派式(免 hover:null==null 分支)
        dialog.ResolvedId = "/dlg";
        IdResolver.AssignChildren(dialog);
        LayoutEngine.Arrange(dialog, new RectF(0, 0, 256, 256));

        dialog.Draw(new PaintContext(mesh, atlas, InputState.Empty));
        using var renderer = new Renderer(ctx);
        renderer.Submit(mesh, new SizeF(256, 256));
        byte[] px = ctx.ReadBackMainFramebuffer();

        // 角落(2,2)=遮罩:半透明黑 0x80(预乘 RGB=0)叠清屏(18,18,20)→(9,9,10);B8G8R8A8 字节序 [B,G,R]
        int mask = (2 * 256 + 2) * 4;
        Assert.Equal(10, px[mask]);
        Assert.Equal(9, px[mask + 1]);
        Assert.Equal(9, px[mask + 2]);

        // 内容槽中心=面板底色 0x1E1E24 不透明(Spacer 无绘制)→ BGR [0x24,0x1E,0x1E]=[36,30,30]
        RectF c = dialog.Content.Frame;
        int panel = ((int)(c.Y + c.Height / 2) * 256 + (int)(c.X + c.Width / 2)) * 4;
        Assert.Equal(36, px[panel]);
        Assert.Equal(30, px[panel + 1]);
        Assert.Equal(30, px[panel + 2]);
        Assert.Equal(255, px[panel + 3]);
    }
}
