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

    readonly Sdl2Window _window;
    readonly GraphicsContext _ctx;
    readonly Renderer _renderer;
    readonly Func<Element>? _render;

    App(Sdl2Window window, GraphicsDevice device, Func<Element>? render)
    {
        _window = window;
        _ctx = GraphicsContext.CreateForWindow(window, device);
        _renderer = new Renderer(_ctx);
        _render = render;
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
        return new App(window, device, render);
    }

    public int Run(int maxFrames = 0)
    {
        Atlas = new TextAtlas(_ctx);
        State = new StateStore();
        var mesh = new MeshBuilder();
        var focus = new FocusManager();
        int frames = 0;
        while (_window.Exists)
        {
            InputSnapshot snapshot = _window.PumpEvents();
            Element root = _render is null ? new Spacer(SizeF.Zero) : _render();
            IdResolver.Assign(root);
            focus.BeginFrame();
            TreeWalk.Register(root, focus);
            SizeF screen = new(_window.Width, _window.Height);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            RawInput raw = new(
                new PointF(snapshot.MousePosition.X, snapshot.MousePosition.Y),
                LeftEvents(snapshot),
                MapKeys(snapshot));
            InputState input = Interaction.Update(root, in raw, focus);
            focus.HandleKeys(raw.KeyEvents ?? Array.Empty<KeyEventData>());
            input = input with { Focused = focus.FocusedThisFrame };
            mesh.Clear();
            root.Draw(new PaintContext(mesh, Atlas, input));
            _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer);
            _ctx.Device.SwapBuffers();
            State.Sweep();
            if (maxFrames > 0 && ++frames >= maxFrames)
                _window.Close();
        }
        return 0;
    }

    static KeyEventData[] MapKeys(InputSnapshot snapshot)
    {
        if (snapshot.KeyEvents.Count == 0) return Array.Empty<KeyEventData>();
        var keys = new KeyEventData[snapshot.KeyEvents.Count];
        for (int i = 0; i < snapshot.KeyEvents.Count; i++)
        {
            KeyEvent ke = snapshot.KeyEvents[i];
            keys[i] = new KeyEventData(ke.Key, ke.Down, ke.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        return keys;
    }

    static bool[] LeftEvents(InputSnapshot snapshot)
    {
        int n = 0;
        foreach (MouseEvent me in snapshot.MouseEvents)
            if (me.MouseButton == MouseButton.Left) n++;
        if (n == 0) return Array.Empty<bool>();
        var events = new bool[n];
        int i = 0;
        foreach (MouseEvent me in snapshot.MouseEvents)
            if (me.MouseButton == MouseButton.Left)
                events[i++] = me.Down;
        return events;
    }
}
