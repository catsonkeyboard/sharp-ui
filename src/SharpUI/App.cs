using System.Diagnostics;
using SharpUI.Backends;

namespace SharpUI;

public sealed class App
{
    /// <summary>TextElement 度量用(M1 服务定位,M2 换度量上下文)。多窗:Run 每窗构建前置为该窗图集。</summary>
    public static TextAtlas? Atlas { get; internal set; }

    /// <summary>跨帧状态存储(M1 服务定位模式,同 Atlas;全局单份,所有窗口共享,Sweep 每帧一次)。</summary>
    public static StateStore? State { get; internal set; }

    /// <summary>墙钟秒,Run 循环每帧首行更新;Anim 默认时间源(D23)。</summary>
    public static float Time { get; private set; }

    /// <summary>窗口表(主窗恒为 [0]);internal 供簿记检查(OpenWindow 入队即可查 Count,无需启动 Run)。</summary>
    internal readonly List<AppWindow> Windows = new();

    readonly Stopwatch _stopwatch = new();

    AppWindow Main => Windows[0];

    /// <summary>主窗 DPI 缩放比(0.5–4 夹紧);鼠标/文本/剪裁统一换算基准(D18)。单窗 API 保持。</summary>
    public float ScaleFactor => Main.Scale;

    App(AppWindow main) => Windows.Add(main);

    public static App Create(string title, int width, int height, Func<Element>? render = null) =>
        new(CreateWindow(title, width, height, render));

    /// <summary>开新窗并入队(帧中调用则下一帧纳入 Run 循环);每窗独立设备/上下文/图集/焦点/输入会话。</summary>
    public AppWindow OpenWindow(string title, int w, int h, Func<Element> render)
    {
        AppWindow window = CreateWindow(title, w, h, render);
        Windows.Add(window);
        return window;
    }

    /// <summary>建窗全路径(EnableImeUi→EnsureVideoInit→SHARPUI_BACKEND 路由→flags→上下文→Renderer/图集→
    /// 失焦/resize 接线)。internal 供 WindowSmokeTests 以 App 同款组装驱动双后端窗口 harness
    /// (T4:像素读回须控末帧不 Present,Run 循环不可注入,故暴露组装点而非绕开重抄时序)。</summary>
    internal static AppWindow CreateWindow(string title, int width, int height, Func<Element>? render)
    {
        SdlTextInput.EnableImeUi();                       // 裁定#34:窗口创建前
        SdlNative.EnsureVideoInit();                      // 裁定#38:Init 落点与建窗模块可分裂(历史实证),不可假设;见 SdlNative 类注
        // 后端选择(裁定#41):SHARPUI_BACKEND 显式指定优先;"default" 平台默认 —— Windows=D3D11、
        // 非 Windows=GL(后者本轮仅编译保证,本机无环境验证;D3D11 亦仅 Windows 可用)。
        string backend = NormalizeBackendName(Environment.GetEnvironmentVariable("SHARPUI_BACKEND"));
        bool useGl = backend == "gl" || (backend == "default" && !OperatingSystem.IsWindows());
        if (useGl)
            GLContext.SetCoreProfileAttributes();   // 时序:EnsureVideoInit 后、建窗前(3.3 core + 双缓冲;见其注释)
        var window = new SdlWindow(title, 100, 100, width, height,
            SdlWindowFlags.Resizable | SdlWindowFlags.Shown | (useGl ? SdlWindowFlags.OpenGL : SdlWindowFlags.None));
        IRenderContext ctx = useGl
            ? GLContext.CreateForWindow(window.SdlWindowHandle, (uint)window.Width, (uint)window.Height)   // SwapWindow 节奏=构造期 SetSwapInterval(1)
            : D3D11Context.CreateForWindow(SdlNative.GetWin32Hwnd(window.SdlWindowHandle),
                (uint)window.Width, (uint)window.Height);   // 交换链 Present(1,0) 即 VSync(spec §7,每窗)
        // 0 尺寸 = 最小化边缘,两后端 ValidateSize 对 0 抛 ArgumentException,异常从 SDL 事件处理器
        // 冒出会击穿 Run 循环整窗崩溃(旧栈 ResizeMainWindow(0,0) 容忍 0)——忽略本帧 resize。
        window.Resized += () => { if (window.Width > 0 && window.Height > 0) ctx.Resize((uint)window.Width, (uint)window.Height); };
        float scale = Math.Clamp(window.ScaleFactor, 0.5f, 4f);   // SdlWindow 已夹紧,此处幂等(旧窗口层硬编码 1,现为真实 DPI,T2 行为改进)
        AppWindow w = new()
        {
            Window = window,
            Ctx = ctx,
            Renderer = new Renderer(ctx),
            Atlas = new TextAtlas(ctx, scale),
            Scale = scale,
            Render = render ?? (static () => new Spacer(SizeF.Zero)),   // null render:Spacer 兜底(单窗语义保持)
        };
        window.FocusLost += () => Interaction.CancelPress(w.Session);   // D15:失焦取消该窗按压
        w.Session.SdlWindow = window.SdlWindowHandle;   // D5:浮层命中优先/外点消费按窗接线(PopupHost 服务定位)
        w.Focus.SdlWindow = window.SdlWindowHandle;     // D5:Esc 浮层优先路由(栈空回归清焦点语义)
        return w;
    }

    /// <summary>后端名归一(SHARPUI_BACKEND):trim + 小写;"gl"/"opengl"→gl、"d3d11"/"d3d"→d3d11;
    /// 空/未识别 → "default"(平台默认:Windows=d3d11,其余=gl)。</summary>
    internal static string NormalizeBackendName(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "gl" or "opengl" => "gl",
        "d3d11" or "d3d" => "d3d11",
        _ => "default",
    };

    public int Run(int maxFrames = 0)
    {
        State = new StateStore();
        var mesh = new MeshBuilder();
        AppWindow main = Main;
        int frames = 0;
        while (main.Window.Exists)
        {
            if (!_stopwatch.IsRunning) _stopwatch.Start();   // Run 首次进入启动墙钟(D23)
            Time = (float)_stopwatch.Elapsed.TotalSeconds;
            SdlPump.PumpAll();   // T3:帧首一次全局泵,事件按 WindowId 路由到各注册窗(旧窗口层为每窗自泵,T2 单泵裁定)
            int liveCount = Windows.Count;   // 帧首快照:帧中 OpenWindow 的新窗下一帧纳入
            for (int i = 0; i < liveCount; i++)
            {
                AppWindow w = Windows[i];
                RawInput raw = InputMapper.Map(w.Window.GetSnapshotAndReset(), w.Scale);   // 各窗取当帧快照(T3:自有 InputSnapshot)
                InputState input = w.LastTree is not null
                    ? Interaction.Update(w.Session, w.LastTree, in raw, w.Focus)
                    : InputState.Empty;
                if (input.WheelDelta != 0f && w.LastTree is not null) Scroll.Dispatch(w.LastTree, in input);
                FeedFocused(w.LastTree, w.Focus, w.Focus.SdlWindow, in raw);   // T2 已知项 A:浮层栈根兜底(Dialog 内 TextField)
                Atlas = w.Atlas;   // 每窗切换服务定位:TextElement 度量读当前窗图集
                UiContext.CurrentInput = input;

                Element root = w.Render();
                IdResolver.Assign(root);
                // D5 浮层栈帧首快照(Update 外点 Pop 已生效):注册/绘制都以本帧此点为准;
                // 帧中回调 Push 的新浮层下一帧纳入(同 liveCount 惯例),帧中 Esc-Pop 以 min 钳制不越界。
                IReadOnlyList<Popup> popups = PopupHost.StackOf(w.Window.SdlWindowHandle);
                int popupCount = popups.Count;
                w.Focus.BeginFrame();
                TreeWalk.Register(root, w.Focus);
                for (int p = 0; p < popupCount && p < popups.Count; p++)   // D5:浮层树入焦点序(栈底→栈顶)
                    TreeWalk.Register(popups[p].Root, w.Focus);
                if (popupCount == 0 && w.Focus.FocusedId is null && FindAutoFocus(root) is { } af)   // IME 热修自测:声明式启动聚焦(浮层打开时不抢焦点入声明树)
                    w.Focus.SetFocus(af);
                w.Focus.HandleKeys(raw.KeyEvents ?? Array.Empty<KeyEventData>(), w.Session);
                input = input with { Focused = w.Focus.FocusedThisFrame, FocusedId = w.Focus.FocusedId };

                SizeF screen = new(w.Window.Width / w.Scale, w.Window.Height / w.Scale);
                RectF screenRect = new(0, 0, screen.Width, screen.Height);
                LayoutEngine.Arrange(root, screenRect);
                // D5 T2(已知项 B 裁定):Dialog 类浮层根随帧重排 —— 居中随 resize 重算(App.Run 已有
                // Arrange 调用点,浮层根追加一次成本一行;Menu/Tooltip 维持打开者 Arrange 定格,锚定语义)。
                for (int p = 0; p < popupCount && p < popups.Count; p++)
                    if (popups[p].Kind == PopupKind.Dialog)
                        LayoutEngine.Arrange(popups[p].Root, screenRect);
                TextFieldElement? tf = w.Focus.FocusedThisFrame as TextFieldElement;   // IME 热修:Arrange 后帧坐标已就绪
                w.TextInput.Update(w.Window.SdlWindowHandle, tf?.ResolvedId,
                    tf is null || tf.CursorRectLogical() is not { } cr ? null
                    : ((int)(cr.X * w.Scale), (int)(cr.Y * w.Scale),
                       Math.Max(1, (int)(cr.Width * w.Scale)), (int)(cr.Height * w.Scale)));
                mesh.Clear();
                root.Draw(new PaintContext(mesh, w.Atlas, input));
                // D5 绘制序:声明树之后栈底→栈顶逐层 Draw(后进在上)。浮层根不重排(打开者 Arrange 于 Push 前,
                // resize 期间帧定格);PaintContext 为 struct,逐层构造零堆分配。
                for (int p = 0; p < popupCount && p < popups.Count; p++)
                    popups[p].Root.Draw(new PaintContext(mesh, w.Atlas, input));
                w.Renderer.Submit(mesh, screen, w.Scale);
                w.Ctx.Present();
                w.LastTree = root;
            }
            State.Sweep();   // 全局一次/帧:所有窗口本帧触碰后才扫描失联键(D25 宽限:>300 帧未触碰才清)
            State.Tick();    // 帧计数 +1(M3c):宽限期以帧为刻度,先 Sweep 后 Tick
            for (int i = Windows.Count - 1; i >= 1; i--)   // 主窗恒留 [0](关闭即退出循环,不参与移除)
                if (!Windows[i].Window.Exists)
                {
                    IntPtr deadHandle = Windows[i].Window.SdlWindowHandle;   // D5:Dispose 前取句柄,清该窗浮层栈
                    PopupHost.Clear(deadHandle);
                    Windows[i].TextInput.Update(IntPtr.Zero, null, null);   // IME 热修:移除前确保 Stop(去激活该窗文本输入)
                    Windows[i].Renderer.Dispose();
                    Windows[i].Ctx.Dispose();
                    Windows[i].Atlas.Dispose();   // M1 复审补漏:文本图集原生资源随窗释放,防反复开关泄漏(D3 T4 起 FT/HB)
                    Windows[i].Window.Dispose();  // T2 强制交接:Close 事件仅 MarkClosed,Dispose 负责 Unregister+SDL_DestroyWindow(缺此即次窗原生窗泄漏)
                    Windows.RemoveAt(i);
                }
            if (maxFrames > 0 && ++frames >= maxFrames)
                main.Window.Close();
        }
        foreach (AppWindow w in Windows)   // IME 热修:退出兜底,残留窗(含主窗)各确保 Stop
        {
            PopupHost.Clear(w.Window.SdlWindowHandle);   // D5:退出清栈(静态字典不随窗释放)
            w.TextInput.Update(IntPtr.Zero, null, null);
            w.Window.Dispose();   // 生命周期归 App:退出销毁残留原生窗(主窗 Close 仅置标记;对齐旧窗口层 Close 即销毁的零泄漏语义)
        }
        return 0;
    }

    /// <summary>D22 键路由:构建前对上帧保留树按 FocusedId 定位聚焦元素,实现 ITextFeedable(T3 TextFieldElement)
    /// 则喂入当帧键与字符。D5 T2(已知项 A 接线):声明树未命中时逐层搜浮层栈根兜底 —— Dialog 内
    /// TextField 的字符流(同 Interaction.FindIn 的两树路径);internal 供 DialogTests 合成驱动。</summary>
    internal static void FeedFocused(Element? lastTree, FocusManager focus, IntPtr sdlWindow, in RawInput raw)
    {
        if (focus.FocusedId is not string id) return;
        ITextFeedable? feedable = lastTree is not null ? TreeWalk.Find(lastTree, id) as ITextFeedable : null;
        if (feedable is null && sdlWindow != IntPtr.Zero)
            foreach (Popup p in PopupHost.StackOf(sdlWindow))
                if (TreeWalk.Find(p.Root, id) is ITextFeedable f) { feedable = f; break; }
        feedable?.Feed(raw.KeyEvents, raw.Chars);
    }

    /// <summary>深度优先找首个 AutoFocus TextFieldElement(IME 热修自测:启动聚焦)。</summary>
    internal static TextFieldElement? FindAutoFocus(Element e)
    {
        if (e is TextFieldElement { AutoFocus: true } tf) return tf;
        if (e.Children is not null)
            foreach (Element c in e.Children)
                if (FindAutoFocus(c) is { } hit) return hit;
        return null;
    }
}
