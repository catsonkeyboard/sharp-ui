using System.Diagnostics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

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

    static AppWindow CreateWindow(string title, int width, int height, Func<Element>? render)
    {
        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo { WindowTitle = title, WindowWidth = width, WindowHeight = height },
            new GraphicsDeviceOptions { Debug = false, PreferStandardClipSpaceYDirection = true },
            VeldridStartup.GetPlatformDefaultBackend(),
            out Sdl2Window window,
            out GraphicsDevice device);
        device.MainSwapchain.SyncToVerticalBlank = true;   // 60fps VSync(spec §7,每窗)
        window.Resized += () => device.ResizeMainWindow((uint)window.Width, (uint)window.Height);
        GraphicsContext ctx = GraphicsContext.CreateForWindow(window, device);
        float scale = Math.Clamp(window.ScaleFactor.X, 0.5f, 4f);
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
        return w;
    }

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
            int liveCount = Windows.Count;   // 帧首快照:帧中 OpenWindow 的新窗下一帧纳入
            for (int i = 0; i < liveCount; i++)
            {
                AppWindow w = Windows[i];
                RawInput raw = InputMapper.Map(w.Window.PumpEvents(), w.Scale);
                InputState input = w.LastTree is not null
                    ? Interaction.Update(w.Session, w.LastTree, in raw, w.Focus)
                    : InputState.Empty;
                if (input.WheelDelta != 0f && w.LastTree is not null) Scroll.Dispatch(w.LastTree, in input);
                FeedFocused(w.LastTree, w.Focus, in raw);
                Atlas = w.Atlas;   // 每窗切换服务定位:TextElement 度量读当前窗图集
                UiContext.CurrentInput = input;

                Element root = w.Render();
                IdResolver.Assign(root);
                w.Focus.BeginFrame();
                TreeWalk.Register(root, w.Focus);
                w.Focus.HandleKeys(raw.KeyEvents ?? Array.Empty<KeyEventData>(), w.Session);
                input = input with { Focused = w.Focus.FocusedThisFrame, FocusedId = w.Focus.FocusedId };

                SizeF screen = new(w.Window.Width / w.Scale, w.Window.Height / w.Scale);
                LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
                TextFieldElement? tf = w.Focus.FocusedThisFrame as TextFieldElement;   // IME 热修:Arrange 后帧坐标已就绪
                w.TextInput.Update(w.Window.SdlWindowHandle, tf?.ResolvedId,
                    tf is null || tf.CursorRectLogical() is not { } cr ? null
                    : ((int)(cr.X * w.Scale), (int)(cr.Y * w.Scale),
                       Math.Max(1, (int)(cr.Width * w.Scale)), (int)(cr.Height * w.Scale)));
                mesh.Clear();
                root.Draw(new PaintContext(mesh, w.Atlas, input));
                w.Renderer.Submit(mesh, screen, w.Ctx.Device.MainSwapchain.Framebuffer, w.Scale);
                w.Ctx.Device.SwapBuffers();
                w.LastTree = root;
            }
            State.Sweep();   // 全局一次/帧:所有窗口本帧触碰后才扫描失联键(D25 宽限:>300 帧未触碰才清)
            State.Tick();    // 帧计数 +1(M3c):宽限期以帧为刻度,先 Sweep 后 Tick
            for (int i = Windows.Count - 1; i >= 1; i--)   // 主窗恒留 [0](关闭即退出循环,不参与移除)
                if (!Windows[i].Window.Exists)
                {
                    Windows[i].TextInput.Update(IntPtr.Zero, null, null);   // IME 热修:移除前确保 Stop(去激活该窗文本输入)
                    Windows[i].Renderer.Dispose();
                    Windows[i].Ctx.Dispose();
                    Windows[i].Atlas.Dispose();   // M1 复审补漏:Skia 原生图集随窗释放,防反复开关泄漏
                    Windows.RemoveAt(i);
                }
            if (maxFrames > 0 && ++frames >= maxFrames)
                main.Window.Close();
        }
        foreach (AppWindow w in Windows)   // IME 热修:退出兜底,残留窗(含主窗)各确保 Stop
            w.TextInput.Update(IntPtr.Zero, null, null);
        return 0;
    }

    /// <summary>D22 键路由:构建前对上帧保留树按 FocusedId 定位聚焦元素,实现 ITextFeedable(T3 TextFieldElement)则喂入当帧键与字符。</summary>
    static void FeedFocused(Element? lastTree, FocusManager focus, in RawInput raw)
    {
        if (lastTree is null || focus.FocusedId is not string id) return;
        if (TreeWalk.Find(lastTree, id) is ITextFeedable feedable)
            feedable.Feed(raw.KeyEvents, raw.Chars);
    }
}
