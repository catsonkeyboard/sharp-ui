using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace SharpUI;

public sealed class App
{
    readonly Sdl2Window _window;
    readonly GraphicsContext _ctx;
    readonly Renderer _renderer;

    App(Sdl2Window window, GraphicsDevice device)
    {
        _window = window;
        _ctx = GraphicsContext.CreateForWindow(window, device);
        _renderer = new Renderer(_ctx);
    }

    public static App Create(string title, int width, int height)
    {
        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo { WindowTitle = title, WindowWidth = width, WindowHeight = height },
            new GraphicsDeviceOptions { Debug = false, PreferStandardClipSpaceYDirection = true },
            VeldridStartup.GetPlatformDefaultBackend(),
            out Sdl2Window window,
            out GraphicsDevice device);
        window.Resized += () => device.ResizeMainWindow((uint)window.Width, (uint)window.Height);
        return new App(window, device);
    }

    public int Run(int maxFrames = 0)
    {
        var mesh = new MeshBuilder();
        int frames = 0;
        while (_window.Exists)
        {
            _window.PumpEvents();
            SizeF screen = new(_window.Width, _window.Height);
            _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer);
            _ctx.Device.SwapBuffers();
            if (maxFrames > 0 && ++frames >= maxFrames)
                _window.Close();
        }
        return 0;
    }
}
