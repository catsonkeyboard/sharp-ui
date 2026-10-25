using System.Diagnostics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace SharpUI;

public sealed class App
{
    /// <summary>TextElement 度量用(M1 服务定位,M2 换度量上下文)。</summary>
    public static TextAtlas? Atlas { get; internal set; }

    /// <summary>跨帧状态存储(M1 服务定位模式,同 Atlas;M2b 度量上下文重构时收编)。</summary>
    public static StateStore? State { get; internal set; }

    /// <summary>墙钟秒,Run 循环每帧首行更新;Anim 默认时间源(D23)。</summary>
    public static float Time { get; private set; }

    readonly Sdl2Window _window;
    readonly GraphicsContext _ctx;
    readonly Renderer _renderer;
    readonly Func<Element>? _render;
    readonly InputSession _session = new();   // T5:输入会话(静态账本→按窗隔离;T6 并入 AppWindow)
    readonly Stopwatch _stopwatch = new();

    /// <summary>DPI 缩放比(0.5–4 夹紧);鼠标/文本/剪裁统一换算基准(D18)。</summary>
    public float ScaleFactor { get; }

    App(Sdl2Window window, GraphicsDevice device, Func<Element>? render, float scale)
    {
        _window = window;
        _ctx = GraphicsContext.CreateForWindow(window, device);
        _renderer = new Renderer(_ctx);
        _render = render;
        ScaleFactor = scale;
    }

    public static App Create(string title, int width, int height, Func<Element>? render = null)
    {
        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo { WindowTitle = title, WindowWidth = width, WindowHeight = height },
            new GraphicsDeviceOptions { Debug = false, PreferStandardClipSpaceYDirection = true },
            VeldridStartup.GetPlatformDefaultBackend(),
            out Sdl2Window window,
            out GraphicsDevice device);
        device.MainSwapchain.SyncToVerticalBlank = true;   // 60fps VSync(spec §7)
        window.Resized += () => device.ResizeMainWindow((uint)window.Width, (uint)window.Height);
        App app = new(window, device, render, Math.Clamp(window.ScaleFactor.X, 0.5f, 4f));
        window.FocusLost += () => Interaction.CancelPress(app._session);   // D15:Sdl2Window.FocusLost 存在
        return app;
    }

    public int Run(int maxFrames = 0)
    {
        Atlas = new TextAtlas(_ctx, ScaleFactor);
        State = new StateStore();
        var mesh = new MeshBuilder();
        var focus = new FocusManager();
        Element? lastTree = null;
        int frames = 0;
        while (_window.Exists)
        {
            if (!_stopwatch.IsRunning) _stopwatch.Start();   // Run 首次进入启动墙钟(D23)
            Time = (float)_stopwatch.Elapsed.TotalSeconds;
            RawInput raw = InputMapper.Map(_window.PumpEvents(), ScaleFactor);
            InputState input = lastTree is not null
                ? Interaction.Update(_session, lastTree, in raw, focus)
                : InputState.Empty;
            if (input.WheelDelta != 0f && lastTree is not null) Scroll.Dispatch(lastTree, in input);
            FeedFocused(lastTree, focus, in raw);
            UiContext.CurrentInput = input;

            Element root = _render is null ? new Spacer(SizeF.Zero) : _render();
            IdResolver.Assign(root);
            focus.BeginFrame();
            TreeWalk.Register(root, focus);
            focus.HandleKeys(raw.KeyEvents ?? Array.Empty<KeyEventData>(), _session);
            input = input with { Focused = focus.FocusedThisFrame, FocusedId = focus.FocusedId };

            SizeF screen = new(_window.Width / ScaleFactor, _window.Height / ScaleFactor);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            mesh.Clear();
            root.Draw(new PaintContext(mesh, Atlas, input));
            _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer, ScaleFactor);
            _ctx.Device.SwapBuffers();
            State.Sweep();
            lastTree = root;
            if (maxFrames > 0 && ++frames >= maxFrames)
                _window.Close();
        }
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
