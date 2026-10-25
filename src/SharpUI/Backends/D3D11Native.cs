using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>
/// M3d-ii:D3D11/DXGI/d3dcompiler 原生声明层(零 unsafe、NativeAOT 兼容)。
/// 槽位出处:本机 Windows SDK 10.0.26100.0 的 um\d3d11.h / shared\dxgi.h / um\d3dcommon.h 接口方法声明顺序
/// (逐方法计数,锚点校验见各表尾注;锚点实证:D3D11Spike 60 帧闭环 — 设备 CreateRenderTargetView=9、
/// 上下文 DrawIndexed=12 / OMSetRenderTargets=33 / ClearRenderTargetView=50、交换链 GetBuffer=9)。
/// 常量出处:dxgiformat.h / d3d11.h / d3dcommon.h 枚举实数(含对简报原稿三处笔误的修正,见行尾注释)。
/// 结构布局经 D3D11NativeTests.Marshal.SizeOf 自检锁定(x64 原生 sizeof)。
/// </summary>
internal static class D3D11Native
{
    // ---- COM 槽位(0-based) ----
    // IUnknown(unknwn.h,三接口共用):QueryInterface(0)/AddRef(1)/Release(2)
    internal const int SlotAddRef = 1, SlotRelease = 2;

    // ID3D11Device : IUnknown 直连(无 DeviceChild 中间层;d3d11.h L14143 起 C++ 声明序逐方法计数,
    // 与同文件 C 接口 vtable 结构体序互证):
    //   自有方法序:CreateBuffer(3) CreateTexture1D(4) CreateTexture2D(5) CreateTexture3D(6)
    //   CreateShaderResourceView(7) CreateUnorderedAccessView(8) CreateRenderTargetView(9)
    // ★校验和:数到 CreateRenderTargetView 恰为槽 9,与 D3D11Spike 60 帧实证锚点一致 → 整表对齐。
    //   其后:CreateDepthStencilView(10) CreateInputLayout(11) CreateVertexShader(12)
    //   CreateGeometryShader(13) CreateGeometryShaderWithStreamOutput(14) CreatePixelShader(15)
    //   CreateHullShader(16) CreateDomainShader(17) CreateComputeShader(18) CreateClassLinkage(19)
    //   CreateBlendState(20) CreateDepthStencilState(21) CreateRasterizerState(22) CreateSamplerState(23)…
    // 注:简报原稿 InputLayout=10/VS=11/PS=12/Blend=19/Rasterizer=21/Sampler=22 系漏数
    //   (漏 CreateDepthStencilView 与 Geometry/Hull/Domain/Compute/ClassLinkage 五类),此处按头文件实数修正。
    internal const int SlotDevCreateBuffer = 3;              // CreateBuffer
    internal const int SlotDevCreateTexture2D = 5;           // CreateTexture2D
    internal const int SlotDevCreateShaderResourceView = 7;  // CreateShaderResourceView
    internal const int SlotDevCreateRenderTargetView = 9;    // ★锚点(D3D11Spike 实证)
    internal const int SlotDevCreateInputLayout = 11;        // CreateDepthStencilView(10) 之后
    internal const int SlotDevCreateVertexShader = 12;
    internal const int SlotDevCreatePixelShader = 15;        // Geometry/GeometrySO(13/14) 之后
    internal const int SlotDevCreateBlendState = 20;         // Hull/Domain/Compute/ClassLinkage(16-19) 之后
    internal const int SlotDevCreateRasterizerState = 22;    // CreateDepthStencilState(21) 之后
    internal const int SlotDevCreateSamplerState = 23;

    // ID3D11DeviceContext : ID3D11DeviceChild(IUnknown 0-2 + GetDevice(3)/GetPrivateData(4)/SetName(5)
    // /SetPrivateDataInterface(6)),自有方法自槽 7 起(d3d11.h L14143 区块后 L7764 起 C++ 声明序逐方法计数):
    //   VSSetConstantBuffers(7) PSSetShaderResources(8) PSSetShader(9) PSSetSamplers(10) VSSetShader(11)
    //   DrawIndexed(12)★ Draw(13) Map(14) Unmap(15) PSSetConstantBuffers(16) IASetInputLayout(17)
    //   IASetVertexBuffers(18) IASetIndexBuffer(19) DrawIndexedInstanced(20) DrawInstanced(21)
    //   GSSetConstantBuffers(22) GSSetShader(23) IASetPrimitiveTopology(24) VSSetShaderResources(25)
    //   VSSetSamplers(26) Begin(27) End(28) GetData(29) SetPredication(30) GSSetShaderResources(31)
    //   GSSetSamplers(32) OMSetRenderTargets(33)★ OMSetRenderTargetsAndUAV(34) OMSetBlendState(35)
    //   OMSetDepthStencilState(36) SOSetTargets(37) DrawAuto(38) DrawIndexedInstancedIndirect(39)
    //   DrawInstancedIndirect(40) Dispatch(41) DispatchIndirect(42) RSSetState(43) RSSetViewports(44)
    //   RSSetScissorRects(45) CopySubresourceRegion(46) CopyResource(47) UpdateSubresource(48)
    //   CopyStructureCount(49) ClearRenderTargetView(50)
    // ★校验和:数到 ClearRenderTargetView 恰为槽 50(第 44 个自有方法,7+43=50),与 D3D11Spike 60 帧
    //   实证锚点一致 → 整表对齐,四个新推导槽位(44/45/47/48)由此背书。
    internal const int SlotCtxVssSetConstantBuffers = 7;
    internal const int SlotCtxPssSetShaderResources = 8;
    internal const int SlotCtxPssSetShader = 9;
    internal const int SlotCtxPssSetSamplers = 10;
    internal const int SlotCtxVssSetShader = 11;
    internal const int SlotCtxDrawIndexed = 12;              // ★锚点(D3D11Spike 实证)
    internal const int SlotCtxMap = 14;                      // Draw(13) 之后
    internal const int SlotCtxUnmap = 15;
    internal const int SlotCtxIaSetInputLayout = 17;         // PSSetConstantBuffers(16) 之后
    internal const int SlotCtxIaSetVertexBuffers = 18;
    internal const int SlotCtxIaSetIndexBuffer = 19;
    internal const int SlotCtxIaSetPrimitiveTopology = 24;   // GSSetConstantBuffers/GSSetShader(22/23) 之后
    internal const int SlotCtxOmSetRenderTargets = 33;       // ★锚点(D3D11Spike 实证)
    internal const int SlotCtxRsSetViewports = 44;           // d3d11.h 推导:RSSetState(43) 之后(校验和 50 对齐)
    internal const int SlotCtxRsSetScissorRects = 45;        // d3d11.h 推导:RSSetViewports(44) 之后
    internal const int SlotCtxCopyResource = 47;             // d3d11.h 推导:CopySubresourceRegion(46) 之后
    internal const int SlotCtxUpdateSubresource = 48;        // d3d11.h 推导:CopyResource(47) 之后(CopyStructureCount(49) 之前)
    internal const int SlotCtxClearRenderTargetView = 50;    // ★锚点(D3D11Spike 实证,兼作上下文表校验和)

    // IDXGISwapChain : IDXGIObject(IUnknown 0-2 + SetPrivateData(3)/SetPrivateDataInterface(4)/GetPrivateData(5)
    // /GetParent(6)) + IDXGIDeviceSubObject::GetDevice(7),自有方法自槽 8 起(shared\dxgi.h C++ 声明序与
    // C 接口 vtable 结构体序双列互证):Present(8) GetBuffer(9) SetFullscreenState(10) GetFullscreenState(11)
    // GetDesc(12) ResizeBuffers(13) ResizeTarget(14) GetContainingOutput(15) GetFrameStatistics(16) GetLastPresentCount(17)
    // ★校验和:GetBuffer=9 与 D3D11Spike 实证一致(GetDevice 漏数教训 #33 已规避)→ 整表对齐。
    internal const int SlotSwapPresent = 8;
    internal const int SlotSwapGetBuffer = 9;                // ★锚点(D3D11Spike 实证)
    internal const int SlotSwapResizeBuffers = 13;

    // ID3D10Blob(d3dcompiler 产物;d3dcommon.h):IUnknown 0-2 之后 GetBufferPointer(3) GetBufferSize(4)
    internal const int SlotBlobGetBufferPointer = 3;
    internal const int SlotBlobGetBufferSize = 4;

    // ---- 常量(本机 Windows SDK dxgiformat.h / d3d11.h / d3dcommon.h 枚举实数) ----
    // DXGI_FORMAT(dxgiformat.h 逐项核对):27=R8G8B8A8_TYPELESS、28=R8G8B8A8_UNORM、29=R8G8B8A8_UNORM_SRGB、
    // 33=R16G16_TYPELESS、57=R16_UINT、87=B8G8R8A8_UNORM。
    // 注 1:D3D11Spike.cs:32 的 `DxgiFormatBgra8Unorm=28` 注释系误标 — 28 实为 R8G8B8A8_UNORM(RGBA 序),
    //   spike 交换链仍成功是因为 R8G8B8A8_UNORM 同为合法后备缓冲格式(typeless 才会被拒)。
    // 注 2:简报/任务附注所给"28=TYPELESS、R8G8B8A8_UNORM=29、R16_UINT=33"与本机 dxgiformat.h 矛盾
    //   (29 是 _SRGB 变体、33 是 R16G16_TYPELESS),此处按头文件实数修正。
    internal const uint DxgiFormatR32G32Float = 16;          // DXGI_FORMAT_R32G32_FLOAT(位置/UV 双 float)
    internal const uint DxgiFormatR16Uint = 57;              // DXGI_FORMAT_R16_UINT(索引缓冲;33 是 R16G16_TYPELESS)
    internal const uint DxgiFormatR8G8B8A8Unorm = 28;        // DXGI_FORMAT_R8G8B8A8_UNORM(27 才是 TYPELESS,29 是 _SRGB)
    internal const uint DxgiFormatB8G8R8A8Unorm = 87;        // DXGI_FORMAT_B8G8R8A8_UNORM(后备缓冲)
    internal const uint DxgiUsageRenderTargetOutput = 32;    // DXGI_USAGE_RENDER_TARGET_OUTPUT

    internal const uint D3D11UsageDefault = 0, D3D11UsageDynamic = 2, D3D11UsageStaging = 3;  // d3d11.h D3D11_USAGE
    internal const uint BindVertex = 0x1, BindIndex = 0x2, BindConstant = 0x4,
        BindShaderResource = 0x8, BindRenderTarget = 0x20;                                   // D3D11_BIND_FLAG
    internal const uint CpuAccessWrite = 0x10000, CpuAccessRead = 0x20000;                  // D3D11_CPU_ACCESS_FLAG
    internal const uint MapRead = 1, MapWriteDiscard = 4;                                   // D3D11_MAP
    internal const uint CpuEOk = 0;                                                          // S_OK

    internal const uint FilterMinMagMipPoint = 0;            // D3D11_FILTER_MIN_MAG_MIP_POINT
    internal const uint AddressClamp = 3;                    // D3D11_TEXTURE_ADDRESS_CLAMP(WRAP=1/MIRROR=2/CLAMP=3)
    // D3D11_BLEND(d3d11.h + MSDN 互证):ZERO=1、ONE=2、SRC_ALPHA=5、INV_SRC_ALPHA=6(枚举自 1 起,0 未定义!)
    // 注:简报原稿 BlendOne=1 系笔误(1 是 ZERO),此处按头文件实数修正为 2。
    internal const uint BlendOne = 2, BlendInvSrcAlpha = 6;
    internal const uint BlendOpAdd = 1;                      // D3D11_BLEND_OP_ADD
    internal const uint FillSolid = 3, CullNone = 1;         // D3D11_FILL_SOLID / D3D11_CULL_NONE
    internal const uint TopologyTriangleList = 4;            // D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST(d3dcommon.h)
    internal const uint InputPerVertexData = 0;              // D3D11_INPUT_PER_VERTEX_DATA
    internal const uint SrvDimensionTexture2D = 4;           // D3D_SRV_DIMENSION_TEXTURE2D(d3dcommon.h)
    internal const uint DeviceFlagBgraSupport = 0x20;        // D3D11_CREATE_DEVICE_BGRA_SUPPORT
    internal const uint SdkVersion = 7;                      // D3D11_SDK_VERSION

    // GUID(dxgi.h L3119 / d3d11.h L15171 DEFINE_GUID 实字)
    internal static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");   // IID_IDXGIFactory1
    internal static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");       // IID_ID3D11Texture2D

    // ---- 结构体(d3d11.h / dxgi.h 原序;x64 原生 sizeof 经 D3D11NativeTests 锁定) ----

    /// <summary>D3D11_BUFFER_DESC:6×UINT = 24。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferDesc
    {
        public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride;
    }

    /// <summary>D3D11_SUBRESOURCE_DATA:ptr + 2×UINT = 16(x64)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SubresourceData
    {
        public IntPtr SysMem;
        public uint SysMemPitch, SysMemSlicePitch;
    }

    /// <summary>DXGI_SAMPLE_DESC:2×UINT = 8。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SampleDesc { public uint Count, Quality; }

    /// <summary>D3D11_TEXTURE2D_DESC:5×UINT + SampleDesc(8) + 4×UINT = 44。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format;
        public SampleDesc Sample;
        public uint Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }

    /// <summary>
    /// D3D11_SHADER_RESOURCE_VIEW_DESC:Format(4) + ViewDimension(4) + union(16) = 24(x64 原生 sizeof)。
    /// union 最大分支是 Buffer{FirstElement:UINT64 + NumElements:UINT(+填充)}与 BufferEX(16 字节),
    /// Tex2D 分支(MostDetailedMip/MipLevels)仅 8 字节 — 故除 Tex2D 两成员外再铺 2 个 uint 填满 union 槽
    /// (简报原稿按"最大分支 8 字节"裁成 16 是误算,此处按头文件补齐至原生 24;Buffer 分支本工程不用)。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderResourceViewDesc
    {
        public uint Format;            // DXGI_FORMAT
        public uint ViewDimension;     // D3D11_SRV_DIMENSION_TEXTURE2D = 4(SrvDimensionTexture2D)
        public uint MostDetailedMip, MipLevels;   // union Tex2D 分支
        public uint _unionPad0, _unionPad1;       // union 其余 8 字节(Buffer/BufferEX 分支位),仅占位保尺寸
    }

    /// <summary>D3D11_SAMPLER_DESC:13×4 字节成员 = 52。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SamplerDesc
    {
        public uint Filter, AddressU, AddressV, AddressW;
        public float MipLodBias;
        public uint MaxAnisotropy, ComparisonFunc;
        public float BorderR, BorderG, BorderB, BorderA;
        public float MinLod, MaxLod;
    }

    /// <summary>
    /// D3D11_RENDER_TARGET_BLEND_DESC:7×UINT(BOOL/枚举) + UINT8 RenderTargetWriteMask = 29,
    /// 尾部按最大对齐 4 自动补 3 字节 → 32(x64 原生 sizeof)。
    /// 注意:显式追加 uint 填充字段反而会把尺寸顶到 36(实证),故此处不声明填充字段。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderTargetBlendDesc
    {
        public uint BlendEnable, SrcBlend, DestBlend, BlendOp, SrcBlendAlpha, DestBlendAlpha, BlendOpAlpha;
        public byte RenderTargetWriteMask;   // 头文件 UINT8 + 3 填充(封送器自动补齐至 32)
    }

    /// <summary>D3D11_BLEND_DESC:2×UINT + 8×RenderTargetBlendDesc(32) = 264(头文件 RenderTarget[8],零 unsafe 展开)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BlendDesc
    {
        public uint AlphaToCoverageEnable, IndependentBlendEnable;
        public RenderTargetBlendDesc R0, R1, R2, R3, R4, R5, R6, R7;   // 头文件 RenderTarget[8](IndependentBlendEnable=0,仅槽 0 生效)
    }

    /// <summary>D3D11_RASTERIZER_DESC:10×4 = 40。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RasterizerDesc
    {
        public uint FillMode, CullMode, FrontCounterClockwise, DepthBias;
        public float DepthBiasClamp, SlopeScaledDepthBias;
        public uint DepthClipEnable, ScissorEnable, MultisampleEnable, AntialiasedLineEnable;
    }

    /// <summary>D3D11_INPUT_ELEMENT_DESC:ptr + 6×UINT = 32。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputElementDesc
    {
        public IntPtr SemanticName;   // 指向 ASCII 字面量(经 AllocHGlobal 常驻,上下文存续期有效)
        public uint SemanticIndex, Format, InputSlot, AlignedByteOffset, InputSlotClass, InstanceDataStepRate;
    }

    /// <summary>D3D11_MAPPED_SUBRESOURCE:ptr + 2×UINT = 16。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MappedSubresource { public IntPtr Data; public uint RowPitch, DepthPitch; }

    /// <summary>D3D11_BOX:6×UINT = 24。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Box { public uint Left, Top, Front, Right, Bottom, Back; }

    /// <summary>D3D11_VIEWPORT:6×FLOAT = 24。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Viewport
    {
        public float TopLeftX, TopLeftY, Width, Height, MinDepth, MaxDepth;
    }

    /// <summary>DXGI_RATIONAL:2×UINT = 8。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rational { public uint Numerator, Denominator; }

    /// <summary>DXGI_MODE_DESC:2×UINT + Rational(8) + 3×UINT = 28。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ModeDesc
    {
        public uint Width, Height;
        public Rational RefreshRate;   // 0/0 = 未指定
        public uint Format, ScanlineOrdering, Scaling;
    }

    /// <summary>
    /// DXGI_SWAP_CHAIN_DESC:ModeDesc(28) + SampleDesc(8) + 2×UINT + ptr(8,需 8 对齐,前补 4 填充) + 3×UINT = 72(x64 原生 sizeof)。
    /// 与 D3D11Spike 同型(spike 60 帧实证可用)。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SwapChainDesc
    {
        public ModeDesc BufferDesc;
        public SampleDesc Sample;
        public uint BufferUsage, BufferCount;
        public IntPtr OutputWindow;
        public uint Windowed, SwapEffect, Flags;

        public static SwapChainDesc Create(IntPtr hwnd, uint width, uint height) => new()
        {
            BufferDesc = new ModeDesc { Width = width, Height = height, Format = DxgiFormatB8G8R8A8Unorm },
            Sample = new SampleDesc { Count = 1, Quality = 0 },
            BufferUsage = DxgiUsageRenderTargetOutput,
            BufferCount = 2,
            OutputWindow = hwnd,
            Windowed = 1,
            SwapEffect = 0,   // DXGI_SWAP_EFFECT_DISCARD
        };
    }

    // ---- P/Invoke(签名与 D3D11Spike.cs:363-370 一致;Winapi/StdCall 为 Windows 默认) ----

    [DllImport("dxgi.dll")]
    internal static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [DllImport("d3d11.dll")]
    internal static extern int D3D11CreateDevice(
        IntPtr adapter, uint driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out uint featureLevel, out IntPtr immediateContext);

    [DllImport("d3dcompiler_47.dll")]
    internal static extern int D3DCompile(
        IntPtr srcData, uint srcDataSize,
        [MarshalAs(UnmanagedType.LPStr)] string sourceName,
        IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entryPoint,
        [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags1, uint flags2,
        out IntPtr codeBlob, out IntPtr errorBlob);
}
