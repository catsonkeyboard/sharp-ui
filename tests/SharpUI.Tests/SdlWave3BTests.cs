using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>波 3B:消费面全量接线(消费波 3A 铺好的 SDL 绑定与事件通道)。审计 §B
/// B2(右键/全按钮消费)/ B10(双击)/ B11(Alt/Gui 到达消费层)/ B12+A7(repeat 策略)/
/// B3+B4(窗口管理 API 对 AppWindow 开放)/ B13(指针形状帧尾消费)/ B21(VSync 三后端确认)。
/// 分层同 SdlWave3ATests:①纯逻辑(无 Gpu,不触原生模块)②真模块/真窗(Gpu trait,冒烟)。
/// 文本三升级(undo-redo/词导航/鼠标拖选)在 TextEditingTests/TextFieldTests 域内交付。
/// 测试主机禁并行见 AssemblyInfo.cs;触碰 IdResolver/App 静态的类挂 "interaction" 集合。</summary>
[Collection("interaction")]
public class SdlWave3BTests
{
    // ---- ① 纯逻辑:InputState 新账本面 + Interaction 右键/双击消费(B2/B10)----

    static ButtonElement ButtonWithId(string key, Action onClick)
    {
        var b = new ButtonElement("B", onClick);
        b.Key = key;
        return b;
    }

    static Element Tree(ButtonElement button)
    {
        // 按钮直排为固定帧(InteractionTests.ButtonAt 同款惯例):无图集下 TextElement 度量 0,
        // 经 Column 布局的按钮仅 ~16px 高,测试鼠标 y=20 命不中。
        LayoutEngine.Arrange(button, new RectF(0, 0, 100, 40));
        IdResolver.Assign(button);
        return button;
    }

    [Fact]
    public void InputState_RightAndDoubleClickIds_DefaultNull_ZeroShapeChange()
    {
        // 既有位置参数调用零形变:新账本走默认参数(波 3B/B2/B10)
        var s = new InputState(new PointF(-1, -1), false, false, false, null);
        Assert.Null(s.RightClickedIds);
        Assert.Null(s.DoubleClickedIds);
        Assert.False(s.ContextRequested("/any"));   // 空/无账本恒 false
        Assert.False(InputState.Empty.ContextRequested("/any"));
    }

    [Fact]
    public void Interaction_RightClick_LedgersRightClickedIds_NoLeftSemantics()
    {
        // B2:右键按下+松开同元素 → RightClickedIds 入账 + ContextRequested 语义;
        // 独立通道 —— 不触发左键按压语义:onClick 不调、ClickedIds 空、Focusable 不被聚焦、无捕获。
        int clicks = 0;
        ButtonElement b = ButtonWithId("rc", () => clicks++);
        Element root = Tree(b);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        var s = new InputSession();

        InputState down = Interaction.Update(s, root, new RawInput(new PointF(50, 20), Array.Empty<bool>(),
            ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Right, true, 1) }), focus);
        InputState up = Interaction.Update(s, root, new RawInput(new PointF(50, 20), Array.Empty<bool>(),
            ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Right, false, 1) }), focus);

        Assert.Equal(0, clicks);                            // 不触发激活回调
        Assert.Null(up.ClickedIds);                         // 左键账本不受污染
        Assert.Null(focus.FocusedId);                       // 右键不触发焦点(非左键按压语义)
        Assert.Null(down.CapturedId);                       // 右键不触发捕获
        Assert.Equal(b.ResolvedId, up.RightClickedIds!.Single());
        Assert.True(up.ContextRequested(b.ResolvedId!));    // ContextRequested 语义
        Assert.False(up.ContextRequested("/other"));
    }

    [Fact]
    public void Interaction_RightClick_IndependentOfLeftPressSession()
    {
        // 右键按下+松开与左键按压/释放完全分离:右键全程后,一次左键按下-松开仍完成一次左键点击
        // (右键事件不曾置 PressedOn/CapturedId —— 若污染,左键点击语义会被右键"提前按下"破坏)。
        int clicks = 0;
        ButtonElement b = ButtonWithId("mix", () => clicks++);
        Element root = Tree(b);
        var s = new InputSession();
        Interaction.Update(s, root, new RawInput(new PointF(50, 20), Array.Empty<bool>(),
            ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Right, true, 1) }));
        Interaction.Update(s, root, new RawInput(new PointF(50, 20), Array.Empty<bool>(),
            ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Right, false, 1) }));
        InputState leftUp = Interaction.Update(s, root, new RawInput(new PointF(50, 20), new[] { true }));
        leftUp = Interaction.Update(s, root, new RawInput(new PointF(50, 20), new[] { false }));
        Assert.Equal(1, clicks);
        Assert.Equal(b.ResolvedId, leftUp.ClickedIds!.Single());
    }

    [Fact]
    public void Interaction_MiddleAndXButtons_BookkeptNotConsumed()
    {
        // B2 最小形态:中键/X1 记账(全按钮通道透传至 RawInput.ButtonEvents)不消费 —— 无账本入项、
        // 无激活、无崩溃。
        int clicks = 0;
        ButtonElement b = ButtonWithId("mid", () => clicks++);
        Element root = Tree(b);
        var s = new InputSession();
        InputState up = Interaction.Update(s, root, new RawInput(new PointF(50, 20), Array.Empty<bool>(),
            ButtonEvents: new[]
            {
                new MouseButtonEventData(MouseButton.Middle, true, 1),
                new MouseButtonEventData(MouseButton.Middle, false, 1),
                new MouseButtonEventData(MouseButton.Button1, true, 1),
                new MouseButtonEventData(MouseButton.Button1, false, 1),
            }));
        Assert.Equal(0, clicks);
        Assert.Null(up.ClickedIds);
        Assert.Null(up.RightClickedIds);
        Assert.Null(up.DoubleClickedIds);
    }

    [Fact]
    public void Interaction_DoubleClick_IdsInDoubleClickedIds_SingleClickNotMarked()
    {
        // B10:clicks>1 的松开完成点击 → DoubleClickedIds 入账;单击(clicks=1)不入。
        int clicks = 0;
        ButtonElement b = ButtonWithId("dbl", () => clicks++);
        Element root = Tree(b);
        var s = new InputSession();

        Interaction.Update(s, root, new RawInput(new PointF(50, 20), new[] { true },
            ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Left, true, 1) }));
        InputState singleUp = Interaction.Update(s, root, new RawInput(new PointF(50, 20), new[] { false },
            ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Left, false, 1) }));
        Assert.Equal(1, clicks);
        Assert.Null(singleUp.DoubleClickedIds);             // 单击无双击标记
        Assert.Equal(b.ResolvedId, singleUp.ClickedIds!.Single());

        Interaction.Update(s, root, new RawInput(new PointF(50, 20), new[] { true },
            ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Left, true, 2) }));
        InputState doubleUp = Interaction.Update(s, root, new RawInput(new PointF(50, 20), new[] { false },
            ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Left, false, 2) }));
        Assert.Equal(2, clicks);                            // 第二次释放仍走激活管道(两次完整点击)
        Assert.Equal(b.ResolvedId, doubleUp.DoubleClickedIds!.Single());
        Assert.Equal(b.ResolvedId, doubleUp.ClickedIds!.Single());
    }

    [Fact]
    public void Interaction_DoubleClick_MarksWithoutActivatable_TextFieldTarget()
    {
        // 双击标记不设 IActivatable 门(词选消费面:TextField 非 IActivatable 亦入账)。
        // 直排固定帧(同 Tree 注:无图集下 Column 布局的字段仅 ~12px 高,y=15 命不中)。
        TextFieldElement tf = Ui.TextField("ph");
        tf.Key = "tfdbl";
        tf.Focusable();
        App.State = new StateStore();
        LayoutEngine.Arrange(tf, new RectF(0, 0, 200, 30));
        IdResolver.Assign(tf);
        var s = new InputSession();
        try
        {
            Interaction.Update(s, tf, new RawInput(new PointF(60, 15), new[] { true },
                ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Left, true, 2) }));
            InputState up = Interaction.Update(s, tf, new RawInput(new PointF(60, 15), new[] { false },
                ButtonEvents: new[] { new MouseButtonEventData(MouseButton.Left, false, 2) }));
            Assert.Equal(tf.ResolvedId, up.DoubleClickedIds!.Single());
            Assert.Null(up.ClickedIds);                     // 非 IActivatable:点击账本不入(与回调一致)
        }
        finally { App.State = null; }
    }

    // ---- ① 纯逻辑:B11 Alt/Gui 到达消费层 + B12/A7 repeat 策略 ----

    [Fact]
    public void HandleKeys_AltGui_ReachConsumptionLayer_NoNewShortcuts()
    {
        // B11 最小形态:KeyEventData.Alt/Gui 经既有管道到达消费层(FocusManager.HandleKeys)。
        // 不新增快捷键:Alt/Gui+Enter 仍走既有激活(数据可达的实证);Alt+字母无任何新语义。
        int clicks = 0;
        ButtonElement a = ButtonWithId("alt", () => clicks++);
        ButtonElement b = ButtonWithId("alt2", () => { });
        PanelElement root = Ui.Column(a, b);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a));

        focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false, Alt: true, Gui: true) });
        Assert.Equal(1, clicks);                            // Alt/Gui 位随事件到达,激活语义不变
        focus.HandleKeys(new[] { new KeyEventData(Key.A, true, false, Alt: true) });
        Assert.Equal(1, clicks);                            // 不新增 Alt 快捷键
        Assert.True(focus.IsFocused(a));                    // Alt+字母不移焦/不清焦
    }

    [Fact]
    public void HandleKeys_RepeatTab_DoesNotMoveFocus()
    {
        (PanelElement root, ButtonElement a, ButtonElement b) = TwoButtons3B();
        FocusManager focus = Frame3B(root);
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a));
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false, IsRepeat: true) });
        Assert.True(focus.IsFocused(a));                    // 长按 Tab 不再连续移焦(A7/B12)
    }

    [Fact]
    public void HandleKeys_RepeatEnter_DoesNotActivate()
    {
        int clicks = 0;
        ButtonElement a = ButtonWithId("rep", () => clicks++);
        ButtonElement b = ButtonWithId("rep2", () => { });
        PanelElement root = Ui.Column(a, b);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        FocusManager focus = Frame3B(root);
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        Assert.Equal(1, clicks);
        focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false, IsRepeat: true) });
        Assert.Equal(1, clicks);                            // 长按 Enter 不重复激活
        focus.HandleKeys(new[] { new KeyEventData(Key.Space, true, false, IsRepeat: true) });
        Assert.Equal(1, clicks);                            // 长按 Space 同策略
    }

    [Fact]
    public void HandleKeys_RepeatNavigation_DoesNotCycleRadioOrSteer()
    {
        // 导航分支(方向键/Home/End)repeat 跳过:Radio 组内不移焦、Slider 不重复微调。
        RadioElement r1 = Ui.Radio("a", "g3b", true, () => { });
        RadioElement r2 = Ui.Radio("b", "g3b", false, () => { });
        PanelElement root = Ui.Column(r1, r2);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        FocusManager focus = Frame3B(root);
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });   // Tab 入序:r1(无焦 Tab 落首项)
        focus.HandleKeys(new[] { new KeyEventData(Key.Right, true, false) });
        Assert.True(focus.IsFocused(r2));
        focus.HandleKeys(new[] { new KeyEventData(Key.Right, true, false, IsRepeat: true) });
        Assert.True(focus.IsFocused(r2));                   // repeat Right 不环移
    }

    [Fact]
    public void TextEditing_RepeatBackSpace_StillDeletes()
    {
        // 文本编辑路径保留 repeat 语义(BackSpace/方向键在 TextEditing.Apply,不经 HandleKeys 门):
        // IsRepeat=true 的 BackSpace 照常删字符。
        var s = TextEditing.Apply(new TextState("ab", 1),
            new[] { new KeyEventData(Key.BackSpace, true, false, IsRepeat: true) }, null);
        Assert.Equal(new TextState("b", 0), s);
        var right = TextEditing.Apply(new TextState("ab", 0),
            new[] { new KeyEventData(Key.Right, true, false, IsRepeat: true) }, null);
        Assert.Equal(new TextState("ab", 1), right);
    }

    // ---- ① 纯逻辑:B13 指针形状声明与帧尾解析 ----

    [Fact]
    public void Element_CursorShape_Declarations_TextFieldIbeam_ButtonsHand()
    {
        Assert.Equal(CursorShape.Arrow, new Element3BProbe().CursorShape);      // 基类默认箭头
        Assert.Equal(CursorShape.IBeam, Ui.TextField("ph").CursorShape);        // 文本输入 I 梁
        Assert.Equal(CursorShape.Hand, Ui.Button("b", () => { }).CursorShape);  // 按钮/可点击 Hand
        Assert.Equal(CursorShape.Hand, Ui.Checkbox("c", false, _ => { }).CursorShape);
        Assert.Equal(CursorShape.Hand, Ui.Radio("r", "g", true, () => { }).CursorShape);
        Assert.Equal(CursorShape.Hand, Ui.Select(new[] { "x" }, 0, _ => { }).CursorShape);
    }

    [Fact]
    public void UiCursor_Resolve_HoverWins_FocusedFallback_DefaultArrow()
    {
        var tf = Ui.TextField("ph");
        var btn = Ui.Button("b", () => { });
        var spacer = Ui.Spacer(new SizeF(4, 4));
        // 悬停元素声明形优先(指针跟手):悬停按钮(Hand)压过聚焦文本框(IBeam)
        Assert.Equal(CursorShape.Hand, UiCursor.Resolve(hovered: btn, focused: tf));
        // 悬停无声明形(箭头/未命中)→ 聚焦元素声明形兜底:聚焦文本框即 IBeam
        Assert.Equal(CursorShape.IBeam, UiCursor.Resolve(hovered: null, focused: tf));
        Assert.Equal(CursorShape.IBeam, UiCursor.Resolve(hovered: spacer, focused: tf));
        // 悬停箭头声明 + 无聚焦 → 箭头
        Assert.Null(UiCursor.Resolve(hovered: spacer, focused: null));
        Assert.Null(UiCursor.Resolve(hovered: null, focused: null));
    }

    // ---- ② 真模块/真窗(Gpu):B13 帧尾消费、B3/B4 窗口 API、B21 VSync 三后端 ----

    [Fact]
    [Trait("Category", "Gpu")]
    public void UiCursor_ApplyFrameTail_ConsumesDeclarations_NoThrow()
    {
        var w = new SdlWindow("sharpui-3b-cursor", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            var tf = Ui.TextField("ph");
            var btn = Ui.Button("b", () => { });
            UiCursor.ApplyFrameTail(hovered: btn, focused: tf);
            Assert.Equal(CursorShape.Hand, UiCursor.LastAppliedForTest);
            UiCursor.ApplyFrameTail(hovered: null, focused: tf);
            Assert.Equal(CursorShape.IBeam, UiCursor.LastAppliedForTest);
            UiCursor.ApplyFrameTail(hovered: null, focused: null);
            Assert.Null(UiCursor.LastAppliedForTest);       // 箭头 = 复原默认(SDL_GetDefaultCursor)
            // 同形去重:重复帧尾不再翻转( LastApplied 稳定)
            UiCursor.ApplyFrameTail(hovered: btn, focused: tf);
            UiCursor.ApplyFrameTail(hovered: btn, focused: tf);
            Assert.Equal(CursorShape.Hand, UiCursor.LastAppliedForTest);
            UiCursor.UseDefault();
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void AppWindow_WindowManagementApi_NoThrow_Readback()
    {
        // B3/B4:窗口管理 API 对 AppWindow 完整开放(透传 SdlWindow,应用侧唯一入口)。
        if (!OperatingSystem.IsWindows() && !GlSupport.Available) return;
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            if (!OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("SHARPUI_BACKEND", "gl");
            AppWindow app = App.CreateWindow("sharpui-3b-api", 200, 150, null);
            try
            {
                app.SetTitle("sharpui-3b-titled");
                Assert.Equal("sharpui-3b-titled", app.Window.Title);   // 读回经 internal 字段(应用面冒烟)
                app.SetSize(180, 140);
                Assert.Equal((180, 140), (app.Window.Width, app.Window.Height));
                app.SetPosition(120, 80);
                app.Minimize();
                app.Restore();
                app.Maximize();
                app.Restore();
                app.SetFullscreen(true);
                app.SetFullscreen(false);
                app.FocusInput();                           // 任务稿 Focus() 与既有 internal 字段 AppWindow.Focus(FocusManager)同名冲突(CS0102),按实码取 SdlWindow 同名方法 FocusInput 透传
                app.Raise();
                Assert.True(app.Window.Exists);
            }
            finally { app.Window.Dispose(); }
        }
        finally { Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void App_VSync_D3D11Backend_SetVSyncPath_NoThrow()
    {
        // B21:D3D11(Windows 默认)窗口路径 SetVSync 运行时开关(显式指定后端,防环境变量串场)。
        if (!OperatingSystem.IsWindows()) return;
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            Environment.SetEnvironmentVariable("SHARPUI_BACKEND", "d3d11");
            App app = App.Create("sharpui-3b-vsync-d3d11", 160, 120, () => Ui.Text(""));
            try
            {
                App.VSync(false);
                App.VSync(true);
                Assert.Equal(7, app.Run(1, 7));
            }
            finally { }
        }
        finally { Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void App_VSync_GlBackend_SetVSyncPath_NoThrow()
    {
        // B21:GL 窗口路径(SDL_GL_SetSwapInterval 运行时开关)。
        if (!OperatingSystem.IsWindows() && !GlSupport.Available) return;
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            Environment.SetEnvironmentVariable("SHARPUI_BACKEND", "gl");
            App app = App.Create("sharpui-3b-vsync-gl", 160, 120, () => Ui.Text(""));
            try
            {
                App.VSync(false);
                App.VSync(true);
                Assert.Equal(9, app.Run(1, 9));
            }
            finally { }
        }
        finally { Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void MetalContext_Headless_SetVSync_NoThrow()
    {
        // B21 第三后端:Metal headless SetVSync no-throw(macOS 实机门,Windows 空过 —— 窗口路径 M3e 后半)。
        if (!MetalGate) return;
        using var ctx = MetalContext.CreateHeadless(64, 64);
        ctx.SetVSync(false);
        ctx.SetVSync(true);
    }

    static bool MetalGate => Environment.GetEnvironmentVariable("SHARPUI_RUN_MAC") == "1";

    [Fact]
    [Trait("Category", "Gpu")]
    public void AppWindow_FileDropped_WiredThroughAppSurface()
    {
        // B5 接线确认:AppWindow.FileDropped(3A 已透传)—— 应用面(公共 API)订阅 + 泵路由到该窗全链。
        if (!OperatingSystem.IsWindows() && !GlSupport.Available) return;
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            if (!OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("SHARPUI_BACKEND", "gl");
            AppWindow app = App.CreateWindow("sharpui-3b-drop", 160, 120, null);
            try
            {
                string? dropped = null;
                app.FileDropped += s => dropped = s;
                System.Text.Encoding utf8 = System.Text.Encoding.UTF8;
                byte[] bytes = utf8.GetBytes("demo-file.txt");
                IntPtr p = SdlEvents.SdlMalloc((nuint)(bytes.Length + 1));
                System.Runtime.InteropServices.Marshal.Copy(bytes, 0, p, bytes.Length);
                System.Runtime.InteropServices.Marshal.WriteByte(p, bytes.Length, 0);
                app.Window.InjectTest(new SdlEvents.SdlEvent
                    { Type = SdlEvents.EventTypeDropFile, DropFile = p, DropWindowId = app.Window.WindowId });
                Assert.Equal("demo-file.txt", dropped);   // SDL_free 由泵执行(3A 契约)
            }
            finally { app.Window.Dispose(); }
        }
        finally { Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old); }
    }

    // ---- helpers ----

    static (PanelElement Root, ButtonElement A, ButtonElement B) TwoButtons3B()
    {
        ButtonElement a = ButtonWithId("a3b", () => { });
        ButtonElement b = ButtonWithId("b3b", () => { });
        PanelElement root = Ui.Column(a, b);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        return (root, a, b);
    }

    static FocusManager Frame3B(PanelElement root)
    {
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        return focus;
    }

    /// <summary>基类默认形探针(匿名子类;避免为断言引入公共类型)。</summary>
    sealed class Element3BProbe : Element { }
}
