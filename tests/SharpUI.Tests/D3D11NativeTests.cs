using System.Runtime.InteropServices;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>
/// M3d-ii Task 1:原生声明层结构布局自检(锁定 Marshal.SizeOf 与 d3d11.h/dxgi.h 原生布局一致)。
/// 预期值 = 本机 Windows SDK 10.0.26100.0 头文件布局在 x64 下的原生 sizeof
/// (D3D11_BUFFER_DESC=24、D3D11_SUBRESOURCE_DATA=16、D3D11_TEXTURE2D_DESC=44、
/// D3D11_SHADER_RESOURCE_VIEW_DESC=24、D3D11_SAMPLER_DESC=52、D3D11_BLEND_DESC=264、
/// D3D11_RASTERIZER_DESC=40、D3D11_INPUT_ELEMENT_DESC=32、D3D11_MAPPED_SUBRESOURCE=16、
/// D3D11_BOX=24、D3D11_VIEWPORT=24、DXGI_SWAP_CHAIN_DESC=72)。
/// 简报原稿中 SubresourceData=24/Texture2DDesc=48/SwapChainDesc=64 为算术笔误
/// (与其自身注释"ptr+2×uint"、"5×uint+…+5×uint"矛盾,后者实为 4 个尾 uint),此处按头文件实数断言。
/// </summary>
public class D3D11NativeTests
{
    [Fact]
    public void NativeStructSizes_MatchD3D11HeaderLayouts()
    {
        Assert.Equal(24, Marshal.SizeOf<D3D11Native.BufferDesc>());             // 6×uint
        Assert.Equal(16, Marshal.SizeOf<D3D11Native.SubresourceData>());        // ptr+2×uint(x64:8+8)
        Assert.Equal(44, Marshal.SizeOf<D3D11Native.Texture2DDesc>());          // 5×uint+SampleDesc(8)+4×uint
        Assert.Equal(24, Marshal.SizeOf<D3D11Native.ShaderResourceViewDesc>()); // Format(4)+ViewDimension(4)+union 16(Buffer 分支 UINT64+2×uint 最大)
        Assert.Equal(52, Marshal.SizeOf<D3D11Native.SamplerDesc>());            // 7×uint+float+2×uint+4×float+2×float=13×4
        Assert.Equal(264, Marshal.SizeOf<D3D11Native.BlendDesc>());             // 2×uint + 8×RenderTargetBlendDesc(32)
        Assert.Equal(40, Marshal.SizeOf<D3D11Native.RasterizerDesc>());         // 10×4
        Assert.Equal(32, Marshal.SizeOf<D3D11Native.InputElementDesc>());       // ptr+6×uint
        Assert.Equal(16, Marshal.SizeOf<D3D11Native.MappedSubresource>());      // ptr+2×uint
        Assert.Equal(24, Marshal.SizeOf<D3D11Native.Box>());                    // 6×uint
        Assert.Equal(24, Marshal.SizeOf<D3D11Native.Viewport>());               // 6×float
        Assert.Equal(72, Marshal.SizeOf<D3D11Native.SwapChainDesc>());          // ModeDesc(28)+Sample(8)+2×uint+对齐填充(4)+ptr(8)+3×uint=72,对齐 8
    }
}
