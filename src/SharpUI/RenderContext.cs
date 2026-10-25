using Veldrid;

namespace SharpUI;

/// <summary>
/// 渲染后端窄接口(D27/M3d-i):Renderer 的全部上下文依赖,M3d-ii 逐平台自研后端的替换点。
/// 前两项(MainFramebuffer/UpdateAtlasRegion)为既定核面;其余五项为 Renderer 实际所需(「薄到 Renderer
/// 实际所需」+「Renderer 仅依赖之」)。Veldrid 类型仍暴露在签名中 = 既定 D12 债,接口面后端中立化时清偿。
/// </summary>
public interface IRenderContext
{
    /// <summary>主帧缓冲(headless 离屏目标;窗口路径每帧现取 Device.MainSwapchain.Framebuffer,避免 resize 陈旧,见 App.Run)。</summary>
    Framebuffer MainFramebuffer { get; }

    /// <summary>图集区域上传(TextAtlas 白点/字形写入)。</summary>
    void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint width, uint height);

    /// <summary>设备(缓冲创建/命令提交);以下管线面为 Renderer 构造与 Submit 所需。</summary>
    GraphicsDevice Device { get; }

    CommandList Commands { get; }

    Pipeline Pipeline { get; }

    DeviceBuffer ScreenSizeBuffer { get; }

    ResourceSet AtlasSet { get; }
}
