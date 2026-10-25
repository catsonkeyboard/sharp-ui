# M3d-ii 自研 D3D11 后端(替换 Veldrid 图形栈)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用 D3D11 直连(零 unsafe COM vtable 互操作)完整替换 Veldrid 图形栈(管线/缓冲/图集/剪裁批次,spec D27),移除 Veldrid/Veldrid.SPIRV/Veldrid.StartupUtilities 包,顺带解决 AOT 回归(裁定#35)。

**Architecture:** 保留 Veldrid.SDL2(窗口+输入事件+`Veldrid.Key`,程序集自足零依赖,实证不引用 Veldrid.dll);图形栈换成 `Backends/D3D11Native.cs`(声明层)+ `Backends/D3D11Context.cs`(设备/管线/缓冲/图集/呈现,实现后端中立 `IRenderContext` v2)。Renderer 保留剪裁区段→scissor 的共享批处理逻辑,经窄接口驱动后端。

**Tech Stack:** .NET 10、D3D11/DXGI/d3dcompiler_47 P/Invoke(安全模式 vtable 委托,同 D3D11Spike 先例)、HLSL(vs_5_0/ps_5_0 运行时编译)、SkiaSharp(不动)。

**Spec:** `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(D27/M3d-ii 行 + §7 性能预算)

## Global Constraints

- 零 `unsafe`(spike 纪律:ReadIntPtr vtable + GetDelegateForFunctionPointer)。
- NativeAOT 兼容(`IsAotCompatible=true` 保持;委托封送 AOT 安全)。
- 每帧稳态分配预算 ≤8192B(现基线 5948B,`docs/notes/perf-baselines.md`):**绘制路径零新分配**(GCHandle 钉托管数组 + UpdateSubresource,禁用 ToArray 复制)。
- COM 槽位纪律(裁定#32/#33):槽位以本机 Windows SDK `d3d11.h`/`dxgi.h` 声明顺序推导,以 D3D11Spike 四个实证锚点校验(设备 CreateRenderTargetView=9;上下文 DrawIndexed=12、OMSetRenderTargets=33、ClearRenderTargetView=50);实现期每个新槽位须在代码注释标注出处与推导。
- 所有 DXGI 格式常量以本机 SDK `dxgiformat.h` 实数为准——**注意 D3D11Spike.cs:32 的 `DxgiFormatBgra8Unorm=28` 注释标 B8G8R8A8 实为 R8G8B8A8_TYPELESS,勿沿用**(R8G8B8A8_UNORM=29、B8G8R8A8_UNORM=87、R16_UINT=33、R32G32_FLOAT=16)。
- 测试全绿门槛:全量套件(158+)通过;GPU 测试(Category=Gpu)在本机真实设备上运行。
- git 提交身份沿用仓库现有配置;每任务至少一提交。

> **勘误(T1 实现裁定,后续任务一律以 `D3D11Native.cs` 实码为准,勿用本计划正文内联数值):**
> DXGI 格式实数(本机 dxgiformat.h):R8G8B8A8_UNORM=**28**(29=_SRGB)、R16_UINT=**57**、B8G8R8A8_UNORM=87、R32G32_FLOAT=16。
> 设备槽实数(d3d11.h,锚点 CreateRTV=9):CreateInputLayout=**11**、CreateVertexShader=**12**、CreatePixelShader=**15**、CreateBlendState=**20**、CreateRasterizerState=**22**、CreateSamplerState=**23**。
> 上下文槽实数(锚点 12/33/50):RSSetViewports=**44**、RSSetScissorRects=**45**、CopyResource=**47**、UpdateSubresource=**48**(计划原稿漏数 OMSetRenderTargetsAndUAV=34、RSSetState=43、CreateDepthStencilView=10)。
> BlendOne=**2**(D3D11_BLEND 自 1 起,1=ZERO)。结构尺寸实数:SubresourceData=16、Texture2DDesc=44、ShaderResourceViewDesc=24、SwapChainDesc=72(x64 对齐)。

---

### Task 1: D3D11/DXGI 原生声明层

**Files:**
- Create: `src/SharpUI/Backends/D3D11Native.cs`
- Test: `tests/SharpUI.Tests/D3D11NativeTests.cs`

**Interfaces(Produces,后续任务消费):**
- `internal static class D3D11Native` — 全部 P/Invoke、结构体、常量、槽位:
  - P/Invoke:`CreateDXGIFactory1(ref Guid, out IntPtr)`、`D3D11CreateDevice(...)`(签名照抄 D3D11Spike.cs:366-370)、`D3DCompile(IntPtr pSrcData, uint srcDataSize, [LPStr] string pSourceName, IntPtr pDefines, IntPtr pInclude, [LPStr] string pEntrypoint, [LPStr] string pTarget, uint Flags1, uint Flags2, out IntPtr ppShader, out IntPtr ppErrorMsgs)`(d3dcompiler_47.dll, StdCall)
  - 结构体(全部 `[StructLayout(LayoutKind.Sequential)]`,`internal`,字段名照下):见 Steps 完整代码
  - 槽位 `internal const int Slot…`(见 Steps 表)
- Consumes: 无(纯声明层)

- [ ] **Step 1: 写失败测试(结构尺寸自检)**

`tests/SharpUI.Tests/D3D11NativeTests.cs`:

```csharp
using System.Runtime.InteropServices;
using SharpUI.Backends;

namespace SharpUI.Tests;

public class D3D11NativeTests
{
    [Fact]
    public void NativeStructSizes_MatchD3D11HeaderLayouts()
    {
        Assert.Equal(24, Marshal.SizeOf<D3D11Native.BufferDesc>());            // 6×uint
        Assert.Equal(24, Marshal.SizeOf<D3D11Native.SubresourceData>());       // ptr+2×uint
        Assert.Equal(48, Marshal.SizeOf<D3D11Native.Texture2DDesc>());         // 5×uint+SampleDesc(8)+5×uint
        Assert.Equal(16, Marshal.SizeOf<D3D11Native.ShaderResourceViewDesc>());// Format+Dimension+union(8)
        Assert.Equal(52, Marshal.SizeOf<D3D11Native.SamplerDesc>());           // 7×uint+float[4]+2×uint... 实为 7×4+16+8=52
        Assert.Equal(264, Marshal.SizeOf<D3D11Native.BlendDesc>());            // 2×uint+8×32
        Assert.Equal(40, Marshal.SizeOf<D3D11Native.RasterizerDesc>());        // 10×4
        Assert.Equal(32, Marshal.SizeOf<D3D11Native.InputElementDesc>());      // ptr+6×uint
        Assert.Equal(16, Marshal.SizeOf<D3D11Native.MappedSubresource>());     // ptr+2×uint
        Assert.Equal(24, Marshal.SizeOf<D3D11Native.Box>());                   // 6×uint
        Assert.Equal(24, Marshal.SizeOf<D3D11Native.Viewport>());              // 4×float+2×float
        Assert.Equal(64, Marshal.SizeOf<D3D11Native.SwapChainDesc>());         // spike 同型
    }

    [Fact]
    public void VertexLayoutConstants_MatchUiVertex20Bytes()
    {
        // UiVertex: X,Y(0..8) U,V(8..16) RGBA(16..20)
        Assert.Equal(16u, D3D11Native.InputElementDesc.ColorOffset);   // 常量由声明层提供
        Assert.Equal(20u, D3D11Native.D3D11_AppendAlignedElement);     // 0xFFFFFFFF 之外的另一常量? 不——见实现:仅断言 stride 用
    }
}
```

注:`InputElementDesc.ColorOffset` 不是 D3D11 常量,删除该断言,仅保留结构尺寸断言(实现 Step 3 中不提供该常量)。

- [ ] **Step 2: 跑测试确认失败(类型不存在)**

Run: `dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj --filter D3D11NativeTests -v q`
Expected: 编译错误 CS0246(D3D11Native 不存在)

- [ ] **Step 3: 实现声明层**

`src/SharpUI/Backends/D3D11Native.cs` 完整内容:

```csharp
using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>M3d-ii:D3D11/DXGI/d3dcompiler 原生声明层(零 unsafe,槽位纪律见 Global Constraints;
/// 结构布局经 Marshal.SizeOf 自检测试锁定)。槽位出处:本机 Windows SDK d3d11.h/dxgi.h 声明顺序,
/// 锚点实证:D3D11Spike(设备 CreateRTV=9;上下文 DrawIndexed=12/OMSetRenderTargets=33/ClearRTV=50)。</summary>
internal static class D3D11Native
{
    // ---- COM 槽位(0-based) ----
    // IUnknown
    internal const int SlotAddRef = 1, SlotRelease = 2;
    // ID3D11Device : IUnknown(直连,无 DeviceChild;d3d11.h 序,锚点 CreateRenderTargetView=9 实证)
    internal const int SlotDevCreateBuffer = 3;              // CreateBuffer
    internal const int SlotDevCreateTexture2D = 5;           // CreateTexture2D
    internal const int SlotDevCreateShaderResourceView = 7;  // CreateShaderResourceView
    internal const int SlotDevCreateRenderTargetView = 9;    // ★锚点(spike 实证)
    internal const int SlotDevCreateInputLayout = 10;
    internal const int SlotDevCreateVertexShader = 11;
    internal const int SlotDevCreatePixelShader = 12;
    internal const int SlotDevCreateBlendState = 19;
    internal const int SlotDevCreateRasterizerState = 21;
    internal const int SlotDevCreateSamplerState = 22;
    // ID3D11DeviceContext : ID3D11DeviceChild(3-6);自有方法自 7 起(锚点 DrawIndexed=12/OM=33/ClearRTV=50 实证)
    internal const int SlotCtxVssSetConstantBuffers = 7;
    internal const int SlotCtxPssSetShaderResources = 8;
    internal const int SlotCtxPssSetShader = 9;
    internal const int SlotCtxPssSetSamplers = 10;
    internal const int SlotCtxVssSetShader = 11;
    internal const int SlotCtxDrawIndexed = 12;              // ★锚点
    internal const int SlotCtxMap = 14;
    internal const int SlotCtxUnmap = 15;
    internal const int SlotCtxIaSetInputLayout = 17;
    internal const int SlotCtxIaSetVertexBuffers = 18;
    internal const int SlotCtxIaSetIndexBuffer = 19;
    internal const int SlotCtxIaSetPrimitiveTopology = 24;
    internal const int SlotCtxOmSetRenderTargets = 33;       // ★锚点
    // RSSetViewports/RSSetScissorRects/CopyResource/UpdateSubresource 槽位:实现时从 d3d11.h 推导,
    // 以 ClearRenderTargetView=50 为校验和(见 D3D11Context 任务),此处先声明常量名,值以头文件核对后填入。
    internal const int SlotCtxRsSetViewports = 0;            // TODO(实现时以头文件实数替换)
    internal const int SlotCtxRsSetScissorRects = 0;         // TODO(实现时以头文件实数替换)
    internal const int SlotCtxUpdateSubresource = 0;         // TODO(实现时以头文件实数替换)
    internal const int SlotCtxCopyResource = 0;              // TODO(实现时以头文件实数替换)
    internal const int SlotCtxClearRenderTargetView = 50;    // ★锚点
    // IDXGISwapChain(IDXGIObject 0-6 + GetDevice 7)
    internal const int SlotSwapPresent = 8;
    internal const int SlotSwapGetBuffer = 9;                // ★锚点(GetDevice 漏数教训 #33)
    internal const int SlotSwapResizeBuffers = 13;
    // ID3D10Blob(d3dcompiler 产物)
    internal const int SlotBlobGetBufferPointer = 3;
    internal const int SlotBlobGetBufferSize = 4;

    // ---- 常量(dxgiformat.h / d3d11.h 实数;spike:32 的 28=B8G8R8A8 注释系误标) ----
    internal const uint DxgiFormatR32G32Float = 16;          // DXGI_FORMAT_R32G32_FLOAT
    internal const uint DxgiFormatR16Uint = 33;              // DXGI_FORMAT_R16_UINT
    internal const uint DxgiFormatR8G8B8A8Unorm = 29;        // DXGI_FORMAT_R8G8B8A8_UNORM(28 是 TYPELESS!)
    internal const uint DxgiFormatB8G8R8A8Unorm = 87;        // DXGI_FORMAT_B8G8R8A8_UNORM
    internal const uint DxgiUsageRenderTargetOutput = 32;

    internal const uint D3D11UsageDefault = 0, D3D11UsageDynamic = 2, D3D11UsageStaging = 3;
    internal const uint BindVertex = 0x1, BindIndex = 0x2, BindConstant = 0x4,
        BindShaderResource = 0x8, BindRenderTarget = 0x20;
    internal const uint CpuAccessWrite = 0x10000, CpuAccessRead = 0x20000;
    internal const uint MapRead = 1, MapWriteDiscard = 4;
    internal const uint CpuEOk = 0;

    internal const uint FilterMinMagMipPoint = 0;            // D3D11_FILTER_MIN_MAG_MIP_POINT
    internal const uint AddressClamp = 3;                    // D3D11_TEXTURE_ADDRESS_CLAMP
    internal const uint BlendOne = 1, BlendInvSrcAlpha = 6;  // D3D11_BLEND_*
    internal const uint BlendOpAdd = 1;                      // D3D11_BLEND_OP_ADD
    internal const uint FillSolid = 3, CullNone = 1;         // D3D11_FILL_SOLID / D3D11_CULL_NONE
    internal const uint TopologyTriangleList = 4;            // D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST
    internal const uint InputPerVertexData = 0;              // D3D11_INPUT_PER_VERTEX_DATA
    internal const uint DeviceFlagBgraSupport = 0x20;
    internal const uint SdkVersion = 7;                      // D3D11_SDK_VERSION

    internal static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    internal static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    // ---- 结构体(d3d11.h / dxgi.h 原序) ----
    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferDesc
    {
        public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SubresourceData
    {
        public IntPtr SysMem;
        public uint SysMemPitch, SysMemSlicePitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SampleDesc { public uint Count, Quality; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format;
        public SampleDesc Sample;
        public uint Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderResourceViewDesc
    {
        public uint Format;            // DXGI_FORMAT
        public uint ViewDimension;     // D3D11_SRV_DIMENSION_TEXTURE2D = 4
        public uint MostDetailedMip, MipLevels;   // union Tex2D(8 字节,取最大分支)
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SamplerDesc
    {
        public uint Filter, AddressU, AddressV, AddressW;
        public float MipLodBias;
        public uint MaxAnisotropy, ComparisonFunc;
        public float BorderR, BorderG, BorderB, BorderA;
        public float MinLod, MaxLod;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderTargetBlendDesc
    {
        public uint BlendEnable, SrcBlend, DestBlend, BlendOp, SrcBlendAlpha, DestBlendAlpha, BlendOpAlpha;
        public byte RenderTargetWriteMask;
        private readonly uint _pad;   // 头文件 UINT+8 结构对齐:末成员 1 字节 + 3 填充 → 每成员槽 4 字节
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlendDesc
    {
        public uint AlphaToCoverageEnable, IndependentBlendEnable;
        public RenderTargetBlendDesc RenderTarget0;   // 仅槽 0(IndependentBlendEnable=0)
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RasterizerDesc
    {
        public uint FillMode, CullMode, FrontCounterClockwise, DepthBias;
        public float DepthBiasClamp, SlopeScaledDepthBias;
        public uint DepthClipEnable, ScissorEnable, MultisampleEnable, AntialiasedLineEnable;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputElementDesc
    {
        public IntPtr SemanticName;   // 指向 ASCII 字面量(经 AllocHGlobal 常驻,上下文存续期有效)
        public uint SemanticIndex, Format, InputSlot, AlignedByteOffset, InputSlotClass, InstanceDataStepRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MappedSubresource { public IntPtr Data; public uint RowPitch, DepthPitch; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Box { public uint Left, Top, Front, Right, Bottom, Back; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Viewport
    {
        public float TopLeftX, TopLeftY, Width, Height, MinDepth, MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rational { public uint Numerator, Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ModeDesc
    {
        public uint Width, Height;
        public Rational RefreshRate;
        public uint Format, ScanlineOrdering, Scaling;
    }

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

    // ---- P/Invoke ----
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
```

**注意(实现者必做):**
1. `RenderTargetBlendDesc` 的 `RenderTargetWriteMask` 在 d3d11.h 中是 `UINT8`+3 填充 —— 上面用 `byte` + `uint _pad` 使该结构 32 字节;`BlendDesc` 只含槽 0 一个 RT(共 16 字节头+32=48? **不对**——D3D11_BLEND_DESC 含 8 个 RT 槽,尺寸 264。**必须保留 8 元素数组**:`public RenderTargetBlendDesc RenderTarget0; public fixed...` 不行(unsafe)。改用:
   ```csharp
   [StructLayout(LayoutKind.Sequential)]
   internal struct BlendDesc
   {
       public uint AlphaToCoverageEnable, IndependentBlendEnable;
       public RenderTargetBlendDesc R0, R1, R2, R3, R4, R5, R6, R7;   // 头文件 [8],零 unsafe 展开
   }
   ```
   尺寸 8+8×32=264 ✓ 测试断言 264。
2. `ShaderResourceViewDesc` 头文件 union 最大分支是 buffer{FirstElement+NumElements(8 字节)}或 Tex2D{Mip,MipLevels(8)}—— 16 字节总尺寸,如上两 uint 成员即可。
3. `TODO` 四个槽位(RSSetViewports/RSSetScissorRects/UpdateSubresource/CopyResource):**从本机 `%Windows Kits%\10\Include\…\um\d3d11.h` 中 ID3D11DeviceContext 声明顺序实数推导**,以 ClearRenderTargetView=50 校验(数到 ClearRenderTargetView 恰为 50 即整表正确);把推导(逐方法计数)写进注释。
4. 测试文件里第二个 Fact 删除(如 Step 1 注所述),只留尺寸自检。

- [ ] **Step 4: 跑测试通过**

Run: `dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj --filter D3D11NativeTests -v q`
Expected: PASS 1(尺寸自检)。若某尺寸断言失败 → 头文件核对修正结构(很可能是填充/成员序问题)。

- [ ] **Step 5: 全量回归 + 提交**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
git add src/SharpUI/Backends/D3D11Native.cs tests/SharpUI.Tests/D3D11NativeTests.cs
git commit -m "feat(m3dii): d3d11/dxgi native declaration layer with struct-size self-checks"
```

---

### Task 2: HLSL 着色器源与运行时编译器

**Files:**
- Modify: `src/SharpUI/Shaders.cs`(追加 HLSL 源,保留 GLSL 源注释"M3e GL 后端复用")
- Create: `src/SharpUI/Backends/ShaderCompiler.cs`
- Test: `tests/SharpUI.Tests/ShaderCompilerTests.cs`

**Interfaces:**
- Produces: `Shaders.HlslVertexSource` / `Shaders.HlslFragmentSource`(const string);
  `internal static class ShaderCompiler { internal static (byte[] Bytecode, string? Error) Compile(string source, string entryPoint, string target); }`
- Consumes: Task 1 的 `D3D11Native.D3DCompile` + blob 槽位常量。

- [ ] **Step 1: 写失败测试**

`tests/SharpUI.Tests/ShaderCompilerTests.cs`:

```csharp
using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

public class ShaderCompilerTests
{
    [Fact]  // 纯 CPU(d3dcompiler_47),无 GPU 依赖
    public void Compile_HlslVertexAndFragment_SucceedWithNonEmptyBytecode()
    {
        (byte[] vs, string? vsErr) = ShaderCompiler.Compile(Shaders.HlslVertexSource, "VSMain", "vs_5_0");
        Assert.Null(vsErr);
        Assert.True(vs.Length > 0);

        (byte[] ps, string? psErr) = ShaderCompiler.Compile(Shaders.HlslFragmentSource, "PSMain", "ps_5_0");
        Assert.Null(psErr);
        Assert.True(ps.Length > 0);
    }

    [Fact]
    public void Compile_SyntaxError_ReturnsErrorMessage()
    {
        (byte[] code, string? err) = ShaderCompiler.Compile("float4 garbage(", "VSMain", "vs_5_0");
        Assert.NotNull(err);
        Assert.Contains("error", err, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj --filter ShaderCompilerTests -v q`
Expected: CS0117(Shaders 无 HlslVertexSource)/ CS0103(ShaderCompiler 不存在)编译错。

- [ ] **Step 3: 实现**

`src/SharpUI/Shaders.cs` 追加(注意:与 GLSL 等价的投影数学——**D3D NDC 与 GL 同为 Y 上行**,GLSL 源里 `1.0 - y/h*2` 使 y=0(顶行)→ +1(屏幕上方)在 D3D 同样成立,无需翻转):

```csharp
    /// <summary>M3d-ii:D3D11 HLSL(vs_5_0/ps_5_0 运行时编译)。与上方 GLSL 语义等价;
    /// GLSL 源保留供 M3e OpenGL 后端复用(D3 决策)。</summary>
    public const string HlslVertexSource = """
        cbuffer ScreenSize : register(b0)
        {
            float2 u_screenSize;
        };

        struct VSIn
        {
            float2 position : POSITION;
            float2 texCoord : TEXCOORD0;
            float4 color : COLOR0;
        };

        struct VSOut
        {
            float4 position : SV_Position;
            float2 texCoord : TEXCOORD0;
            float4 color : COLOR0;
        };

        VSOut VSMain(VSIn i)
        {
            VSOut o;
            o.position = float4(i.position.x / u_screenSize.x * 2.0 - 1.0,
                                1.0 - i.position.y / u_screenSize.y * 2.0, 0.0, 1.0);
            o.texCoord = i.texCoord;
            o.color = i.color;
            return o;
        }
        """;

    public const string HlslFragmentSource = """
        Texture2D atlas : register(t0);
        SamplerState atlasSampler : register(s0);

        struct PSIn
        {
            float2 texCoord : TEXCOORD0;
            float4 color : COLOR0;
        };

        float4 PSMain(PSIn i) : SV_Target
        {
            return i.color * atlas.Sample(atlasSampler, i.texCoord);
        }
        """;
```

`src/SharpUI/Backends/ShaderCompiler.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace SharpUI.Backends;

/// <summary>HLSL 运行时编译(d3dcompiler_47;M3d-ii)。blob 经 ID3D10Blob vtable(槽 3/4)取出字节后即释放。</summary>
internal static class ShaderCompiler
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetBufferPointerFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate UIntPtr GetBufferSizeFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseFn(IntPtr self);

    public static (byte[] Bytecode, string? Error) Compile(string source, string entryPoint, string target)
    {
        byte[] src = Encoding.UTF8.GetBytes(source);
        IntPtr pSrc = Marshal.AllocHGlobal(src.Length);
        IntPtr code = IntPtr.Zero, errors = IntPtr.Zero;
        try
        {
            Marshal.Copy(src, 0, pSrc, src.Length);
            int hr = D3D11Native.D3DCompile(pSrc, (uint)src.Length, null!, IntPtr.Zero, IntPtr.Zero,
                entryPoint, target, 0, 0, out code, out errors);
            if (hr != 0)
            {
                string msg = ReadBlobString(errors);
                return (Array.Empty<byte>(), msg.Length > 0 ? msg : $"D3DCompile hr=0x{hr:X8}({entryPoint})");
            }
            return (ReadBlobBytes(code), null);
        }
        finally
        {
            ReleaseBlob(errors);
            ReleaseBlob(code);
            Marshal.FreeHGlobal(pSrc);
        }
    }

    static byte[] ReadBlobBytes(IntPtr blob)
    {
        if (blob == IntPtr.Zero) return Array.Empty<byte>();
        var getPtr = Vfn<GetBufferPointerFn>(blob, D3D11Native.SlotBlobGetBufferPointer);
        var getSize = Vfn<GetBufferSizeFn>(blob, D3D11Native.SlotBlobGetBufferSize);
        IntPtr data = getPtr(blob);
        int size = checked((int)getSize(blob).ToUInt64());
        var bytes = new byte[size];
        Marshal.Copy(data, bytes, 0, size);
        return bytes;
    }

    static string ReadBlobString(IntPtr blob)
    {
        byte[] bytes = ReadBlobBytes(blob);
        return bytes.Length == 0 ? "" : Encoding.UTF8.GetString(bytes);
    }

    static void ReleaseBlob(IntPtr blob)
    {
        if (blob == IntPtr.Zero) return;
        Vfn<ReleaseFn>(blob, D3D11Native.SlotRelease)(blob);
    }

    static T Vfn<T>(IntPtr comObj, int slot) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObj);
        IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        if (fn == IntPtr.Zero) throw new InvalidOperationException($"ID3D10Blob vtable 槽 {slot} 为空");
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }
}
```

- [ ] **Step 4: 跑测试通过**

Run: `dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj --filter ShaderCompilerTests -v q`
Expected: PASS 2。若 HLSL 编译报错,按错误信息修 HLSL 源(常见:cbbuffer 填充、语义名)。

- [ ] **Step 5: 提交**

```bash
git add src/SharpUI/Shaders.cs src/SharpUI/Backends/ShaderCompiler.cs tests/SharpUI.Tests/ShaderCompilerTests.cs
git commit -m "feat(m3dii): hlsl shader sources + runtime d3dcompile wrapper"
```

---

### Task 3: IRenderContext v2 + D3D11Context(headless 全功能)

**Files:**
- Rewrite: `src/SharpUI/RenderContext.cs`(接口 v2,清偿 D12 的 Veldrid 类型签名债)
- Create: `src/SharpUI/Backends/D3D11Context.cs`
- Create: `src/SharpUI/Backends/SdlNative.cs`(SDL_Init/GetWin32Hwnd,裁定#38/#39 教训内建)
- Test: `tests/SharpUI.Tests/D3D11ContextTests.cs`

**Interfaces(Produces,Task 4 消费):**

```csharp
// RenderContext.cs — 全文替换
namespace SharpUI;

/// <summary>渲染后端窄接口 v2(M3d-ii,D12 清偿:不再暴露 Veldrid 类型)。Renderer 经此驱动任意后端;
/// 剪裁区段→scissor 的换算留在 Renderer(平台共享逻辑),后端只做命令落地。</summary>
public interface IRenderContext : IDisposable
{
    /// <summary>当前主目标像素尺寸(scissor 全开与清屏基准;窗口路径随 Resize 更新)。</summary>
    uint TargetWidth { get; }
    uint TargetHeight { get; }

    /// <summary>窗口交换链 resize;headless 抛 NotSupportedException。</summary>
    void Resize(uint width, uint height);

    /// <summary>图集区域上传(RGBA 字节,左上原点)。</summary>
    void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint width, uint height);

    /// <summary>帧起始:绑主目标 + 设视口 + 清屏(r,g,b,a 线性)。</summary>
    void BeginFrame(float r, float g, float b, float a);

    /// <summary>顶点着色器屏幕尺寸常量(逻辑像素)。</summary>
    void SetScreenSize(float logicalWidth, float logicalHeight);

    /// <summary>剪裁矩形(目标像素坐标;全开=目标尺寸)。</summary>
    void SetScissor(uint x, uint y, uint w, uint h);

    /// <summary>绘制一段索引几何(零分配路径:钉数组 + UpdateSubresource,内存布局与 UiVertex 20 字节一致)。</summary>
    void Draw(UiVertex[] vertices, int vertexCount, ushort[] indices, uint indexCount, uint startIndex);

    /// <summary>呈现(窗口路径 Present(1,0) VSync;headless no-op)。</summary>
    void Present();
}
```

`D3D11Context`(公开类,替代 GraphicsContext):
- `public const uint AtlasSize = 1024;`
- `public static D3D11Context CreateHeadless(uint width, uint height)` — 离屏 RT(B8G8R8A8,保持读回字节序与旧断言兼容)
- `public static D3D11Context CreateForWindow(IntPtr hwnd, uint width, uint height)`(Task 4 才接线,本任务实现)
- `public byte[] ReadBackMainFramebuffer()` — staging 拷贝 + Map READ,**按 RowPitch 逐行拷**(解除 M1 的 width*4%256 限制)
- 实现全部 IRenderContext 成员;headless 的 `Present()` no-op、`Resize` 抛 NotSupportedException。

`MeshBuilder` 追加(Consumes 于 Task 4;本任务先加好,Mesh.cs):

```csharp
    /// <summary>原始顶点数组(M3d-ii 零分配上传:后端 GCHandle 钉此数组直传 GPU)。</summary>
    internal UiVertex[] VerticesBuffer => _vertices;

    /// <summary>原始索引数组(同上)。</summary>
    internal ushort[] IndicesBuffer => _indices;
```
(MeshBuilder 的 `_indices` 字段当前类型核实行为 `ushort[]`;若为 `List<ushort>` 或别的,以现字段类型为准调整暴露面,**不得复制**。)

**SdlNative.cs** — 裁定#38/#39 教训内建:

```csharp
using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>SDL 原生最小面(M3d-ii):视频子系统初始化 + 窗口 HWND。裁定#38:不做 NativeLibrary
/// 预载(避免 runtimes 副本成第二模块),DllImport 解析与 Veldrid.Sdl2 同路径(应用基目录)。
/// 裁定#39:WMInfo 结构给足 SDL 2.24+ Windows 联合体尺寸(4 指针)+ 余量,防原生越界写。</summary>
internal static class SdlNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion { public byte Major, Minor, Patch; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlSysWmInfo
    {
        public byte Maj, Min, Pat;
        public int Subsystem;
        public IntPtr Hwnd, Hdc, Hglrc, HelperWindow;   // 2.24+ win 分支 4 指针
        public IntPtr Pad1, Pad2, Pad3, Pad4;           // 其余平台分支余量
    }

    internal static void EnsureVideoInit()
    {
        _ = SDL_Init(0x00000020);   // SDL_INIT_VIDEO;幂等引用计数,失败值忽略(与 Veldrid.StartupUtilities 先例一致)
    }

    internal static IntPtr GetWin32Hwnd(IntPtr sdlWindow)
    {
        var v = new SdlVersion();
        SDL_GetVersion(ref v);
        var info = new SdlSysWmInfo { Maj = v.Major, Min = v.Minor, Pat = v.Patch };
        if (SDL_GetWindowWMInfo(sdlWindow, ref info) == 0 || info.Subsystem != 1 /*SYSWM_WINDOWS*/)
            throw new InvalidOperationException(
                $"SDL_GetWindowWMInfo 失败(SDL {v.Major}.{v.Minor}.{v.Patch}, sub={info.Subsystem})");
        return info.Hwnd;
    }

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_Init(uint flags);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GetVersion(ref SdlVersion v);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetWindowWMInfo(IntPtr window, ref SdlSysWmInfo info);
}
```

**D3D11Context 实现要点(实现者按此骨架):**
- 设备链:照抄 D3D11Spike.CreateState 的 1–3 步(factory1 → adapter0 → D3D11CreateDevice(BGRA)),AddRef/Release 往返实证照抄(`ValidateIUnknown` 模式);建链后 factory/adapter Release(设备内部持有)。
- Headless:无交换链;`CreateTexture2D`(B8G8R8A8、BindRenderTarget|BindShaderResource、Default)+ RTV。
- 窗口:SwapChainDesc.Create(hwnd,w,h) → swapchain → GetBuffer → RTV(ResizeBuffers 路径本任务实现:`Resize` = Release 旧 RTV → ResizeBuffers(槽 13)→ GetBuffer → 新 RTV → 更新 TargetWidth/Height)。
- 管线(一次性,构造内):
  - ShaderCompiler 编 HLSL → CreateVertexShader/CreatePixelShader(设备槽 11/12)
  - SemanticName 三个 ASCII 字面量(`"POSITION"/"TEXCOORD"/"COLOR"`)AllocHGlobal 常驻,Dispose 释放
  - InputLayout:R32G32_FLOAT@0、R32G32_FLOAT@8、R8G8B8A8_UNORM@16(UNORM 顶点色在着色器得到归一化 RGBA)
  - BlendDesc:AlphaToCoverage=0,Independent=0,R0{BlendEnable=1, Src=One, Dst=InvSrcAlpha, Op=Add, 同 alpha, WriteMask=0xF}
  - Rasterizer:Solid/CullNone/DepthClip=0/Scissor=1
  - Sampler:Point/Clamp×3
  - Atlas:1024² R8G8B8A8 Default+SRV(SRV_DIMENSION_TEXTURE2D=4, MipLevels=0xFFFFFFFF 即全部)
  - 常量缓冲:16 字节 Default+BindConstant;`readonly float[] _cb = new float[4]`(SetScreenSize 写 [0]/[1])
- 零分配 Draw:GCHandle.Alloc(vertices, Pinned) + SubresourceData{SysMem=AddrOfPinnedObject} → UpdateSubresource(槽位 Task 1 已定)到 vb(容量不足时重建 ×2,首容量 256KB/512KB 同旧 Renderer);ib 同型;随后设置全部状态(IASetInputLayout/IASetVertexBuffers(stride=20)/IASetIndexBuffer(R16_UINT)/IASetPrimitiveTopology/VSSetShader/PSSetShader/VSSetConstantBuffers/PSSetShaderResources/PSSetSamplers)→ DrawIndexed(indexCount, startIndex, 0)。
- BeginFrame:OMSetRenderTargets(1, rtv, null)+ RSSetViewports(1, {0,0,w,h,0,1})+ ClearRenderTargetView(rtv, {r,g,b,a})。
- Present(窗口):Present(1,0),hr≠0 抛(设备丢失显式失败)。
- 引用计数公平:Dispose 逆序 Release 全部 COM 对象(模式照抄 SpikeState.Dispose + ReleaseTrace 可观测);staging 读回纹理惰性创建。
- UpdateAtlasRegion:pin rgba + UpdateSubresource(Box{0..w, 0..h, 0..1},RowPitch=w*4)。

- [ ] **Step 1: 写失败测试**

`tests/SharpUI.Tests/D3D11ContextTests.cs`:

```csharp
using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

public class D3D11ContextTests
{
    static byte[] Pixel(byte[] px, uint x, uint y, uint w = 64) =>
        new[] { px[(y * w + x) * 4], px[(y * w + x) * 4 + 1], px[(y * w + x) * 4 + 2], px[(y * w + x) * 4 + 3] };

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_BeginFrameClearsToColor()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.Present();   // headless no-op
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 4, 4));   // BGRA 读回,同旧断言
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 60, 60));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawWhiteQuad_AttributelessAtlasWhitePixel()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);   // 图集 (0,0) 白
        float inv = 1f / D3D11Context.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 16f : 48f, i < 2 ? 16f : 48f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.SetScissor(0, 0, 64, 64);
        ctx.Draw(verts, 4, idx, 6, 0);
        ctx.Present();
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 32, 32));
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 4, 4));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawRedQuad_LandsInRedChannel()   // 通道映射锁定(Color32.R→红)
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / D3D11Context.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 16f : 48f, i < 2 ? 16f : 48f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            new Color32(255, 0, 0));
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.BeginFrame(0, 0, 0, 1f);
        ctx.SetScissor(0, 0, 64, 64);
        ctx.Draw(verts, 4, idx, 6, 0);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(px, 32, 32));   // BGRA 存储:纯红 → [B=0,G=0,R=255]
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_ScissorClipsDraw()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / D3D11Context.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 0f : 64f, i < 2 ? 0f : 64f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.SetScissor(0, 0, 32, 64);   // 左半
        ctx.Draw(verts, 4, idx, 6, 0);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 4, 32));
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 40, 32));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_Resize_Throws()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        Assert.Throws<NotSupportedException>(() => ctx.Resize(32, 32));
    }

    [Fact]
    public void AtlasSize_Is1024()
    {
        Assert.Equal(1024u, D3D11Context.AtlasSize);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj --filter D3D11ContextTests -v q`
Expected: CS0103/CS0246(D3D11Context 不存在)编译错。

- [ ] **Step 3: 实现 D3D11Context + SdlNative + 接口 v2 + MeshBuilder 缓冲暴露**

按上面 Interfaces 的完整签名与实现要点写码。设备创建后立即对 device/context/swapchain/rtv 做 `ValidateIUnknown` 往返(照抄 spike 模式,含 ReleaseTrace)。顶点/索引缓冲容量增长逻辑照抄旧 Renderer.EnsureCapacity(256KB/512KB 起,×2 增长,超容重建)。

- [ ] **Step 4: 跑测试通过**

Run: `dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj --filter D3D11ContextTests -v q`
Expected: PASS 7(6 GPU + AtlasSize)。通道/几何错位时优先核对:输入布局偏移、cbuffer、视口、Y 投影、BGRA 读回。

- [ ] **Step 5: 全量回归(GraphicsContext 仍在,互不干扰)+ 提交**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
git add src/SharpUI/RenderContext.cs src/SharpUI/Backends/D3D11Context.cs src/SharpUI/Backends/SdlNative.cs src/SharpUI/Mesh.cs tests/SharpUI.Tests/D3D11ContextTests.cs
git commit -m "feat(m3dii): backend-neutral irendercontext v2 + d3d11 context (headless verified)"
```

---

### Task 4: Renderer/App 接线 + 移除 Veldrid 图形包 + 测试迁移

**Files:**
- Modify: `src/SharpUI/Renderer.cs`(全文重写为 v2 驱动)
- Modify: `src/SharpUI/App.cs:42-66`(窗口创建)+ `App.cs` Run 循环两处(SwapBuffers→Present;Submit 签名)
- Modify: `src/SharpUI/AppWindow.cs`(Ctx 类型 GraphicsContext→D3D11Context)
- Delete: `src/SharpUI/GraphicsContext.cs`
- Modify: `src/SharpUI/SharpUI.csproj`(移除 Veldrid/Veldrid.SPIRV/Veldrid.StartupUtilities 三行)
- Modify: tests(`RendererTests.cs`、`TextAtlasTests.cs`、`PerfTests.cs`、`TextFieldTests.cs`、`UiScopeTests.cs` 中 `GraphicsContext` → `D3D11Context`,其余断言不动)
- Modify: `tests/SharpUI.Tests/D3D11SpikeTests.cs` 中如有 `GraphicsContext` 引用一并改

**Interfaces:**
- Consumes: Task 3 全部(D3D11Context、IRenderContext v2、SdlNative、MeshBuilder 缓冲暴露)。
- Produces: `Renderer.Submit(MeshBuilder mesh, SizeF logicalScreen, float scale = 1f)`(Framebuffer 参数删除);`AppWindow.Ctx` 类型 `D3D11Context`。

- [ ] **Step 1: 新 Renderer 测试先就位(现有 RendererTests 改调用面)**

`RendererTests.cs` 机械替换:`GraphicsContext.CreateHeadless` → `D3D11Context.CreateHeadless`;`GraphicsContext.AtlasSize` → `D3D11Context.AtlasSize`;`renderer.Submit(mesh, screen, framebuffer, scale)` → `renderer.Submit(mesh, screen, scale)`(framebuffer 变量删除)。**像素断言全部保留**(它们现在验证的是新后端)。

- [ ] **Step 2: 跑测试确认失败(编译错:Renderer.Submit 旧签名/无 Framebuffer)**

Run: `dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj --filter RendererTests -v q`
Expected: 编译错。

- [ ] **Step 3: 重写 Renderer.cs**

```csharp
namespace SharpUI;

/// <summary>M3d-ii:纯后端中立驱动——剪裁区段→scissor 换算留在此处(平台共享),命令经 IRenderContext v2 落地。</summary>
public sealed class Renderer : IDisposable
{
    readonly IRenderContext _ctx;

    public Renderer(IRenderContext ctx) => _ctx = ctx;

    public void Submit(MeshBuilder mesh, SizeF logicalScreen, float scale = 1f)
    {
        _ctx.SetScreenSize(logicalScreen.Width, logicalScreen.Height);
        _ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        if (mesh.IndexCount == 0) return;   // 空帧:仅清屏
        uint fbW = _ctx.TargetWidth, fbH = _ctx.TargetHeight;
        ReadOnlySpan<MeshBuilder.ClipRun> runs = mesh.ClipRuns;
        if (runs.Length == 0)
        {
            _ctx.SetScissor(0, 0, fbW, fbH);
            _ctx.Draw(mesh.VerticesBuffer, mesh.VertexCount, mesh.IndicesBuffer, (uint)mesh.IndexCount, 0);
            return;
        }
        foreach (ref readonly MeshBuilder.ClipRun run in runs)
        {
            if (run.Count == 0) continue;
            if (run.Clip == MeshBuilder.FullClip)
            {
                _ctx.SetScissor(0, 0, fbW, fbH);
            }
            else
            {
                // 精确包含矩形:floor(左上)、ceil(右下),避免缩放下 1px 小数欠覆盖(T5 续订)
                float left = run.Clip.X * scale, top = run.Clip.Y * scale;
                float right = run.Clip.Right * scale, bottom = run.Clip.Bottom * scale;
                uint x = (uint)Math.Clamp(MathF.Floor(left), 0f, fbW);
                uint y = (uint)Math.Clamp(MathF.Floor(top), 0f, fbH);
                uint w = (uint)Math.Clamp(MathF.Ceiling(right) - x, 0f, fbW - x);
                uint h = (uint)Math.Clamp(MathF.Ceiling(bottom) - y, 0f, fbH - y);
                if (w == 0 || h == 0) continue;
                _ctx.SetScissor(x, y, w, h);
            }
            _ctx.Draw(mesh.VerticesBuffer, mesh.VertexCount, mesh.IndicesBuffer, (uint)run.Count, (uint)run.Start);
        }
    }

    public void Dispose() { }   // 缓冲归 D3D11Context(Draw 内部持有);上下文由 AppWindow 释放
}
```

(顶点缓冲容量增长已移入 D3D11Context.Draw——实现者确认 Task 3 已含;Renderer 不再持有 GPU 资源。)

- [ ] **Step 4: App/AppWindow 接线**

`AppWindow.cs`:`internal GraphicsContext Ctx = null!;` → `internal D3D11Context Ctx = null!;`(注释同步)。加 `using SharpUI.Backends;`。

`App.cs` CreateWindow 全新实现:

```csharp
    static AppWindow CreateWindow(string title, int width, int height, Func<Element>? render)
    {
        SdlTextInput.EnableImeUi();                       // 裁定#34:窗口创建前
        SdlNative.EnsureVideoInit();                      // 裁定#38:与 Veldrid.Sdl2 同模块(SDL2.dll 基目录副本)
        var window = new Sdl2Window(title, 100, 100, width, height, Sdl2WindowFlags.Resizable, false);
        var ctx = D3D11Context.CreateForWindow(SdlNative.GetWin32Hwnd(window.SdlWindowHandle),
            (uint)window.Width, (uint)window.Height);
        window.Resized += () => ctx.Resize((uint)window.Width, (uint)window.Height);
        float scale = Math.Clamp(window.ScaleFactor.X, 0.5f, 4f);
        AppWindow w = new()
        {
            Window = window,
            Ctx = ctx,
            Renderer = new Renderer(ctx),
            Atlas = new TextAtlas(ctx, scale),
            Scale = scale,
            Render = render ?? (static () => new Spacer(SizeF.Zero)),
        };
        window.FocusLost += () => Interaction.CancelPress(w.Session);   // D15:失焦取消该窗按压
        return w;
    }
```

Run 循环两处:
- `w.Renderer.Submit(mesh, screen, w.Ctx.Device.MainSwapchain.Framebuffer, w.Scale);` → `w.Renderer.Submit(mesh, screen, w.Scale);`
- `w.Ctx.Device.SwapBuffers();` → `w.Ctx.Present();`
- 删除 `using Veldrid;`/`using Veldrid.StartupUtilities;`(保留 `using Veldrid.Sdl2;`)。

`SharpUI.csproj`:删除 `Veldrid`、`Veldrid.SPIRV`、`Veldrid.StartupUtilities` 三行 PackageReference(保留 `Veldrid.SDL2`)。删除 `src/SharpUI/GraphicsContext.cs`。

- [ ] **Step 5: 全量测试 + demo 冒烟**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
dotnet build examples/Counter/Counter.csproj -v q
```
全量须 165+(158 旧 + Task1/2/3 新增;GraphicsContext 相关若有独立测试文件随迁移)。随后启动 `examples/Counter/bin/Debug/net10.0/Counter.exe`,窗口出现、计数/输入框/列表可用(实现者可用 computer-use 或人工冒烟;至少确认进程存活 ≥5s 且无异常退出、窗口有内容渲染——用 GDI 抓屏核对非纯黑)。

- [ ] **Step 6: 提交**

```bash
git add -A
git commit -m "feat(m3dii): renderer/app on d3d11 backend, drop veldrid graphics packages"
```

---

### Task 5: spike 退役 + AOT 验证 + 性能复测 + 文档收官

**Files:**
- Delete: `src/SharpUI/Backends/D3D11Spike.cs`、`tests/SharpUI.Tests/D3D11SpikeTests.cs`
- Modify: `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(M3d-ii 行标 ✅ + 裁定#40:D3D11 直连后端定稿/槽位纪律内化于 D3D11Native)
- Modify: `docs/notes/m1-aot.md`(裁定#35 关闭:Vortice 随 Veldrid 图形包移除;AOT 发布验证记录)
- Modify: `docs/notes/perf-baselines.md`(新后端实测数,若与 5948B/0.05ms 偏差 >10% 需注明原因)
- Modify: `docs/superpowers/plans/` 账本惯例的 progress 记录(若仓库有 docs/superpowers/PROGRESS.md 或同位文件,按仓库现状)

**Interfaces:** Consumes: Task 4 完成态。

- [ ] **Step 1: 删除 spike + 其测试,全量回归**

```bash
git rm src/SharpUI/Backends/D3D11Spike.cs tests/SharpUI.Tests/D3D11SpikeTests.cs
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
```
Expected: 全绿(spike 测试的职责已被 D3D11ContextTests 承接:refcount 往返在 D3D11Context 构造/Dispose 内建,清屏呈现由 headless 读回验证)。

- [ ] **Step 2: AOT 发布验证(裁定#35 收账)**

```bash
dotnet publish examples/Counter/Counter.csproj -c Release -r win-x64 /p:PublishAot=true -v q
```
启动产物 exe,存活 ≥5s、窗口渲染(同 Task 4 Step 5 冒烟标准)。若仍现 Vortice/SharpGen 反射异常:核对 `dotnet publish` 依赖树(`--no-build` 前先 `dotnet list package --include-transitive | grep -i vortice` 应为空——Veldrid.SDL2 无此依赖,实证于计划期),把发现写进 m1-aot.md。

- [ ] **Step 3: 性能复测**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj --filter "Category=Gpu&PerfTests" -v q
```
对照 `docs/notes/perf-baselines.md`(5948B/帧、0.05ms)。更新基线文档;预算 ≤8192B/帧不得破(绘制路径若出现分配,GCHandle 钉数组方案核对——Draw 内禁止 new)。

- [ ] **Step 4: 文档更新 + 终提交**

spec M3d-ii 标 ✅;m1-aot.md 记录 AOT 结果;perf-baselines.md 更新;提交:

```bash
git add -A
git commit -m "chore(m3dii): retire d3d11 spike, verify aot publish, refresh perf baselines"
```

---

## Self-Review 结论(已执行)

- **Spec 覆盖**:D27 M3d-ii"管线/缓冲/图集/剪裁批次"→ T3/T4;窗口/输入(Veldrid.SDL2)按范围界定保留;AOT(#35)→ T5 Step 2;性能预算 → T5 Step 3 + Global Constraint。
- **占位符**:T1 的 4 个 `TODO` 槽位为**有意的实现期核对项**(从头文件实数推导,锚点校验法已给出),非未定设计;其余步骤均含完整代码。
- **类型一致性**:`IRenderContext` v2 成员在 T3 定义、T4 Renderer/App 消费签名一致;`D3D11Context.AtlasSize` 迁移自 GraphicsContext,测试引用同步。
