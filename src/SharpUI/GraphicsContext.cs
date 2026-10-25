using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;

namespace SharpUI;

public sealed class GraphicsContext : IDisposable
{
    public const int AtlasSize = 1024;

    public GraphicsDevice Device { get; }
    public CommandList Commands { get; }
    public Pipeline Pipeline { get; }
    public DeviceBuffer ScreenSizeBuffer { get; }
    public Texture AtlasTexture { get; }
    public TextureView AtlasView { get; }
    public Sampler AtlasSampler { get; }
    public ResourceSet AtlasSet { get; }
    public Framebuffer MainFramebuffer { get; }

    Texture? _readbackStaging;

    GraphicsContext(GraphicsDevice device, Framebuffer mainFramebuffer)
    {
        Device = device;
        MainFramebuffer = mainFramebuffer;
        ResourceFactory factory = device.ResourceFactory;
        Commands = factory.CreateCommandList();

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm));

        ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ScreenSize", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("Atlas", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("AtlasSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        // GLSL → SPIR-V → 按后端交叉编译(HLSL/MSL/GLSL),三后端一份源码
        Shader[] shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(Shaders.VertexSource), "main"),
            new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(Shaders.FragmentSource), "main"));

        Pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: false, depthWriteEnabled: false, comparisonKind: ComparisonKind.Always),
            RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise, depthClipEnabled: false, scissorTestEnabled: true),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { layout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, shaders),
            Outputs = mainFramebuffer.OutputDescription,
        });

        ScreenSizeBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        AtlasTexture = factory.CreateTexture(TextureDescription.Texture2D(
            AtlasSize, AtlasSize, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        AtlasView = factory.CreateTextureView(AtlasTexture);
        AtlasSampler = factory.CreateSampler(SamplerDescription.Point);
        AtlasSet = factory.CreateResourceSet(new ResourceSetDescription(layout, ScreenSizeBuffer, AtlasView, AtlasSampler));
    }

    public static GraphicsContext CreateForWindow(Sdl2Window window, GraphicsDevice device) =>
        new(device, device.MainSwapchain.Framebuffer);

    public static GraphicsContext CreateHeadless(uint width, uint height)
    {
        GraphicsDevice device = GraphicsDevice.CreateD3D11(new GraphicsDeviceOptions { Debug = false });
        Texture colorTarget = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer framebuffer = device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, colorTarget));
        return new GraphicsContext(device, framebuffer);
    }

    public void UpdateAtlasRegion(byte[] rgbaPixels, uint x, uint y, uint width, uint height) =>
        Device.UpdateTexture(AtlasTexture, rgbaPixels, x, y, 0, width, height, 1, 0, 0);

    /// <summary>读回 MainFramebuffer 颜色目标(GPU 测试用)。M1 限制:仅支持行字节数 256 对齐的宽度(64/128/256…)。</summary>
    public byte[] ReadBackMainFramebuffer()
    {
        Texture source = MainFramebuffer.ColorTargets[0].Target;
        uint rowBytes = source.Width * 4;
        if (rowBytes % 256 != 0)
            throw new NotSupportedException($"M1 读回要求 width*4 对齐 256,当前 {rowBytes}");
        _readbackStaging ??= Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            source.Width, source.Height, 1, 1, source.Format, TextureUsage.Staging));
        CommandList cl = Commands;
        cl.Begin();
        cl.CopyTexture(source, _readbackStaging);
        cl.End();
        Device.SubmitCommands(cl);
        MappedResource mapped = Device.Map(_readbackStaging, MapMode.Read);
        byte[] pixels = new byte[mapped.SizeInBytes];
        Marshal.Copy(mapped.Data, pixels, 0, pixels.Length);
        Device.Unmap(_readbackStaging);
        return pixels;
    }

    public void Dispose()
    {
        _readbackStaging?.Dispose();
        AtlasSet.Dispose();
        AtlasSampler.Dispose();
        AtlasView.Dispose();
        AtlasTexture.Dispose();
        ScreenSizeBuffer.Dispose();
        Pipeline.Dispose();
        Commands.Dispose();
        Device.Dispose();
    }
}
