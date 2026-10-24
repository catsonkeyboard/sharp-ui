using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace SharpUI;

public sealed class App
{
    /// <summary>TextElement 度量用(M1 服务定位,M2 换度量上下文)。</summary>
    public static TextAtlas? Atlas { get; internal set; }

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
        window.Resized += () => device.ResizeMainWindow((uint)window.Width, (uint)window.Height);
        return new App(window, device, render);
    }

    public int Run(int maxFrames = 0)
    {
        Atlas = new TextAtlas(_ctx);
        var mesh = new MeshBuilder();
        int frames = 0;
        while (_window.Exists)
        {
            _window.PumpEvents();
            Element root = _render is null ? new Spacer(SizeF.Zero) : _render();
            SizeF screen = new(_window.Width, _window.Height);
            LayoutEngine.Measure(root, screen);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            mesh.Clear();
            root.Draw(new PaintContext(mesh, Atlas));   // 显式 in 不能接右值(CS8156),隐式传递等价
            _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer);
            _ctx.Device.SwapBuffers();
            if (maxFrames > 0 && ++frames >= maxFrames)
                _window.Close();
        }
        return 0;
    }
}
