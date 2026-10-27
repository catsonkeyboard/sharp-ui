# 跨平台胶水层 + Metal 后端预置 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 本机预置 Linux/macOS 动态库名映射与字体发现器 + Metal 后端(objc_msgSend 直连+MSL),Windows 编译零错误、全量 392+ 绿、AOT 不变;实机验证标"待 CI"门控。

**Architecture:** `NativeLib` 三平台库名常量表;`LinuxFontLocator`(目录扫描+FT face 族名匹配)与 `MacFontLocator`(CoreText objc 优先+目录回退);`ObjectiveC.cs`(libobjc 委托族)+`MetalNative.cs`(框架函数+Selector 常量)+`MetalContext.cs`(headless 离屏 CAMetalLayer 路径)。

**Tech Stack:** .NET 10、Objective-C runtime(libobjc.dylib objc_msgSend)、Metal.framework、MSL、xUnit(纯逻辑单测+env 门控占位)。

**Spec:** `docs/superpowers/specs/2026-10-28-cross-platform-design.md`(X-1~X-4 已批准)

## Global Constraints

- 零 `unsafe`;NativeAOT 兼容(Windows 发布产物不变 ~5.97MiB)。
- **DllImport 常量化改造不得改变 Windows 行为**(392 既有测试零回归)。
- Linux/macOS/Metal 实机路径全部标注"待 CI 实机验证"(类头注释);单测 env 门控(`SHARPUI_RUN_MAC=1` / 目录注入纯逻辑单测除外)。
- Selector 常量与 objc 方法签名注释出处(Metal.h/MetalKit.h 官方头文件;实现者按公开文档核对并注释)。
- 全量测试门槛:基线 392/392;git 每任务至少一提交。

---

### Task 1: NativeLib 库名映射 + Linux/macOS 字体发现器

**Files:**
- Create: `src/SharpUI/NativeLib.cs`、`src/SharpUI/Text/LinuxFontLocator.cs`、`src/SharpUI/Text/MacFontLocator.cs`
- Modify: `src/SharpUI/Text/FreeTypeNative.cs`(:19 Lib 常量)、`src/SharpUI/Text/HarfBuzzNative.cs`(:17)、`src/SharpUI/Clipboard.cs`(:9)、`src/SharpUI/Backends/SdlNative.cs`(:24)、`src/SharpUI/Text/FontLibrary.cs`(Locate 分派+默认族三平台映射)
- Test: `tests/SharpUI.Tests/NativeLibTests.cs`、`tests/SharpUI.Tests/FontLocatorTests.cs`

**Interfaces(Produces,T2/T3 与运行时消费):**

```csharp
namespace SharpUI;

/// <summary>三平台原生库名解析(X-1):纯函数可单测(注入 OS 三元组)。</summary>
internal static class NativeLib
{
    internal enum Platform { Windows, Linux, Mac }
    internal static Platform Current { get; }   // OperatingSystem.IsLinux()/IsOSPlatform("MACCATALYST") 派生
    // 纯函数:Resolve(Platform, "SDL2") → "SDL2.dll" | "libSDL2-2.0.so.0" | "libSDL2-2.0.dylib"
    internal static string Resolve(Platform p, string baseName);
    internal static string Sdl2 { get; } internal static string FreeType { get; } internal static string HarfBuzz { get; }
    // FT/HB 的 SONAME:freetype → libfreetype.so.6 / libfreetype.6.dylib;harfbuzz → libharfbuzz.so.0 / libharfbuzz.0.dylib
}

// LinuxFontLocator(X-2):fontconfig 约定目录递归扫描(ttf/otf/ttc),FT face 族名匹配(FT_New_Memory_Face 探测,不落盘)
internal sealed class LinuxFontLocator : IFontLocator
{
    public LinuxFontLocator(IEnumerable<string>? roots = null);   // roots 注入单测;默认 /usr/share/fonts + ~/.local/share/fonts + ~/.fonts
    // Locate:遍历目录树字体文件 → FT_New_Memory_Face 读 family(支持 bold 样式判定:FT_Style_Name_Flag 或 face style flags)→ 匹配族名(大小写不敏感)
    // 粗体:"family Bold" 直名匹配优先,否则 face style flags 含 Bold 的同名 family
    public string? Locate(string familyName, bool bold);
}

// MacFontLocator(X-2):CoreText(objc)优先 → CTFontCollectionCreateFromAvailableFonts 匹配;失败(非 Mac/objc 不可用)回退目录扫描
internal sealed class MacFontLocator : IFontLocator
{
    public MacFontLocator(IEnumerable<string>? roots = null);   // 默认 /System/Library/Fonts + /Library/Fonts + ~/Library/Fonts
    public string? Locate(string familyName, bool bold);        // CoreText 路径待 CI 验证;回退路径同 Linux 匹配法
}

// FontLibrary.cs 改造:
//   _locator = IsWindows ? new WindowsFontLocator() : IsLinux ? new LinuxFontLocator() : new MacFontLocator();
//   PrimaryFamily 常量 → static 三平台映射:Windows "Segoe UI" / Linux "Noto Sans"(缺则回退 "DejaVu Sans")/ Mac "Helvetica Neue"
//   默认回退族链:Linux ["Noto Sans CJK SC","Noto Color Emoji"] / Mac ["PingFang SC","Apple Color Emoji"]
//   (FontLibrary ctor 的 fallbackFamilies null 默认按平台取,Windows 保持现值)
```

- [ ] **Step 1: 写失败测试**:
  - `NativeLibTests`:Resolve(Windows,"SDL2")=="SDL2.dll"、Resolve(Linux,"SDL2")=="libSDL2-2.0.so.0"、Resolve(Mac,"freetype")=="libfreetype.6.dylib"、Resolve(Linux,"harfbuzz")=="libharfbuzz.so.0" 等全矩阵 Theory;Current 在 Windows 测试宿主==Windows。
  - `FontLocatorTests`(纯逻辑,临时目录伪造字体树):扫描目录树收集 ttf/otf(扩展名过滤/递归/空目录);roots 注入生效;文件不存在根返回 null。族名匹配依赖 FT 的部分:接口抽象 `IFontFamilyProbe`(LinuxFontLocator 构造可选注入;默认 FT 实现)——单测注入假 Probe(family→"Noto Sans",bold→true)断言匹配/粗体优先级/大小写不敏感;FT 真实现小合同测试 Windows 上跑真文件(C:\Windows\Fonts\segoeui.ttf family=="Segoe UI")。
- [ ] **Step 2-5: 红→实现(Dllimport 常量化改造:5 文件 Lib 常量改引 NativeLib.XXX,Windows 值不变 392 零回归)→全量绿→提交**

```bash
git commit -m "feat(cross): native lib name resolution + linux/mac font locators"
```

---

### Task 2: ObjectiveC 声明层 + Metal 框架声明层 + MSL 着色器

**Files:**
- Create: `src/SharpUI/Backends/ObjectiveC.cs`、`src/SharpUI/Backends/MetalNative.cs`
- Modify: `src/SharpUI/Shaders.cs`(追加 MslVertexSource/MslFragmentSource)
- Test: `tests/SharpUI.Tests/ObjectiveCTests.cs`

**Interfaces(Produces,T3 消费):**

```csharp
namespace SharpUI.Backends;

/// <summary>X-3:libobjc 直连(IsMacOS 守卫;Windows 上 Ensure 不加载,测试仅编译结构)。</summary>
internal static class ObjectiveC
{
    // 经 NativeLibrary.Load("libobjc.dylib")/("libobjc.so.4"? Linux 无 objc——IsMacOS 才加载,失败抛带指引)
    // 委托族:MsgSend(IntPtr receiver, IntPtr sel) 到 8 参重载(float/int/IntPtr/nuint 混合签名各一,cdecl 不适用——objc_msgSend 是 C 调用约定,直接 CallingConvention.Winapi/Cdecl 按平台;
    //   实现者按公开 ABI 资料定并注释:arm64 上 objc_msgSend 与 C 调用约定兼容,Cdecl 可用)
    internal static IntPtr GetClass(string name);        // objc_getClass
    internal static IntPtr RegisterSel(string name);    // sel_registerName(缓存字典)
    internal static IntPtr MsgSend(IntPtr rcv, IntPtr sel);                          // id(id,SEL)
    internal static IntPtr MsgSendPtr(IntPtr rcv, IntPtr sel, IntPtr a0);            // 1 参
    internal static IntPtr MsgSendPtrPtr(IntPtr rcv, IntPtr sel, IntPtr a0, IntPtr a1);
    internal static void MsgSendVoid(IntPtr rcv, IntPtr sel);
    internal static void MsgSendVoidFloat4(IntPtr rcv, IntPtr sel, float r, float g, float b, float a);
    internal static IntPtr MsgSendNSError(IntPtr rcv, IntPtr sel, IntPtr a0, out IntPtr err);   // newLibraryWithSource:options:error:
    // static Ensure():加载 libobjc + 缓存 class/sel(失败抛 InvalidOperationException 带平台指引)
}

/// <summary>X-3:Metal 框架声明(Metal.framework 经 dlopen("-framework=Metal")?——实现路径:NativeLibrary.Load("/System/Library/Frameworks/Metal.framework/Metal"));全部 Selector 以 Metal.h 官方头为出处注释。</summary>
internal static class MetalNative
{
    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    internal static extern IntPtr MTLCreateSystemDefaultDevice();   // 返回 id<MTLDevice>
    // Selector 常量(全部 sel_registerName 缓存,注释 Metal.h 行号/方法全名):
    internal static readonly IntPtr SelNewCommandQueue;             // newCommandQueue
    internal static readonly IntPtr SelNewBufferWithLengthOptions;  // newBufferWithLength:options:
    internal static readonly IntPtr SelNewTextureWithDescriptor;    // newTextureWithDescriptor:
    internal static readonly IntPtr SelNewLibraryWithSource;        // newLibraryWithSource:options:error:
    internal static readonly IntPtr SelNewFunctionWithName;         // newFunctionWithName:
    internal static readonly IntPtr SelNewRenderPipelineState;      // newRenderPipelineStateWithDescriptor:error:
    internal static readonly IntPtr SelCommandBuffer;               // commandBuffer
    internal static readonly IntPtr SelRenderCommandEncoder;        // renderCommandEncoderWithDescriptor:
    internal static readonly IntPtr SelSetVertexBuffer;             // setVertexBuffer:offset:atIndex:
    internal static readonly IntPtr SelSetFragmentTexture;          // setFragmentTexture:atIndex:
    internal static readonly IntPtr SelSetFragmentSamplerState;     // setFragmentSamplerState:atIndex:
    internal static readonly IntPtr SelSetRenderPipelineState;      // setRenderPipelineState:
    internal static readonly IntPtr SelSetScissorRect;              // setScissorRect:
    internal static readonly IntPtr SelDrawPrimitives;              // drawPrimitives:vertexStart:vertexCount:instanceCount:
    internal static readonly IntPtr SelEndEncoding;                 // endEncoding
    internal static readonly IntPtr SelCommit;                      // commit
    internal static readonly IntPtr SelNextDrawable;                // nextDrawable (CAMetalLayer)
    internal static readonly IntPtr SelGetBytes;                    // getBytes:bytesPerRow:fromRegion:mipmapLevel:
    // 以及 MTLTextureDescriptor/MTLRenderPipelineDescriptor/MTLRenderPassDescriptor 的属性 selector(setPixelFormat:/setWidth: 等)与 MTLTextureDescriptor.new
    internal static void Ensure();  // IsMacOS 守卫+全 selector 注册
}
```

MSL 着色器(`Shaders.cs` 追加,语义与 HLSL/GLSL 同——预乘白×顶点色、Y 上行 NDC 同 D3D):

```metal
#include <metal_stdlib>
using namespace metal;
struct VsIn { float2 position [[attribute(0)]]; float2 texCoord [[attribute(1)]; float4 color [[attribute(2)]]; };
struct VsOut { float4 position [[position]]; float2 texCoord; float4 color; };
vertex VsOut vs_main(VsIn in [[stage_in]], constant float2& uSize [[buffer(0)]]) {
    VsOut o;
    o.position = float4(in.position.x / uSize.x * 2.0 - 1.0, 1.0 - in.position.y / uSize.y * 2.0, 0.0, 1.0);
    o.texCoord = in.texCoord; o.color = in.color; return o;
}
fragment float4 fs_main(VsOut in [[stage_in]], texture2d<float> atlas [[texture(0)]], sampler atlasSampler [[sampler(0)]]) {
    return in.color * atlas.sample(atlasSampler, in.texCoord);
}
```

- [ ] **Step 1: 写失败测试**(ObjectiveCTests,编译结构级):Selector 常量声明完备(MetalNative 全字段非默认值——Windows 上 Ensure 不调用,常量是 `static readonly IntPtr` 由 Lazy 注册,单测用反射断言字段集合与命名规约;MsgSend 委托签名存在性)。
- [ ] **Step 2-5: 红→实现→全量绿→提交**

```bash
git commit -m "feat(metal): objc msgsend layer + metal framework selectors + msl shaders"
```

---

### Task 3: MetalContext(headless 离屏路径)+ 门控测试 + 收官

**Files:**
- Create: `src/SharpUI/Backends/MetalContext.cs`
- Modify: `src/SharpUI/App.cs`(NormalizeBackendName 增 "metal" 分支:非 Windows 默认逻辑预留——IsMacOS 时 default→metal;Windows 上选 metal 抛 PlatformNotSupportedException 带指引)
- Test: `tests/SharpUI.Tests/MetalContextTests.cs`(env `SHARPUI_RUN_MAC=1` 门控,镜像 GLContextTests 七项结构)、`tests/SharpUI.Tests/BackendSelectionTests.cs`(metal 归一化 Theory 追加)
- Modify: `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(裁定#48:跨平台预置定稿)、`docs/superpowers/specs/2026-10-28-cross-platform-design.md`(实施纪要)

**Interfaces(Produces):**

```csharp
namespace SharpUI.Backends;

/// <summary>X-3/X-4:Metal 后端。headless=独立 CAMetalLayer(离屏 drawable texture 读回);
/// 窗口路径=CreateForWindow(sdlWindow) 预留(SDL macOS NSView.layer 接线归 M3e 后半 CI 落地,
/// 本任务签名在+IsMacOS 守卫+NotImplemented 例外注释)。全部 IRenderContext 成员语义与 D3D11/GL 同。</summary>
internal sealed class MetalContext : IRenderContext
{
    public static MetalContext CreateHeadless(uint width, uint height);
    public static MetalContext CreateForWindow(IntPtr sdlWindow, uint width, uint height);  // 抛 NotImplementedException("待 M3e 后半:SDL macOS NSView 接 CAMetalLayer")
    public uint AtlasSize => 1024;
    public uint TargetWidth { get; } public uint TargetHeight { get; }
    public void Resize(uint w, uint h);
    public void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint w, uint h);   // replaceRegion(MTLTexture)
    public void BeginFrame(float r, float g, float b, float a);                   // MTLRenderPassDescriptor loadAction=Clear/storeAction=Store+renderCommandEncoder
    public void SetScreenSize(float w, float h);                                  // MTLBuffer Shared 映射/contents 写 16B
    public void SetScissor(uint x, uint y, uint w, uint h);                       // MTLScissorRect(0 原点左上,Metal 同)
    public void Draw(UiVertex[] vertices, int vertexCount, ushort[] indices, uint indexCount, uint startIndex);  // setVertexBuffer+drawPrimitives triangle;索引缓冲 Metal 用 32 位或 16→32 展开(实现者定,注释)
    public void Present();                                                        // headless no-op;CAMetalLayer nextDrawable+present
    public byte[] ReadBackMainFramebuffer();                                      // getBytes:bytesPerRow:fromRegion:(离屏 texture,行序上翻同 GL)
    public void Dispose();
}
```

- [ ] **Step 1: 写门控测试**(MetalContextTests:七项镜像 GL——清屏/白四边形/红通道/scissor/两段/Resize 语义/AtlasSize;全部 `if (Environment.GetEnvironmentVariable("SHARPUI_RUN_MAC") != "1") return;` 门控,Windows 全量跑=0ms 空过;BackendSelectionTests 追加 "metal"→"metal" 归一化与 Windows 上 App 选 metal 抛 PlatformNotSupportedException 两条纯逻辑测试)。
- [ ] **Step 2: 实现 MetalContext**(实现者按 Metal.h 公开文档核对每步 API 序;全部标注待 CI 验证)。
- [ ] **Step 3: NormalizeBackendName 接线**(metal 归一;IsMacOS 默认 metal 预留——代码可写,Windows 不触)。
- [ ] **Step 4: 全量绿(392+新增) + AOT 发布对比不变 + 文档(裁定#48)→ 提交**

```bash
dotnet publish examples/Counter/Counter.csproj -c Release -r win-x64 -p:PublishAot=true
git commit -m "feat(metal): metalcontext headless path + gated tests, cross-platform closeout (ruling 48)"
```

---

## Self-Review 结论(已执行)

- **Spec 覆盖**:X-1 库名→T1(NativeLib+5 文件常量化);X-2 发现器→T1(Linux 目录扫描/Mac CoreText+回退/默认族三平台映射);X-3 objc+Metal+MSL→T2/T3;X-4 门控→T1(FT 假 Probe)/T3(env 门控+纯逻辑);验收(编译/392 零回归/AOT 不变)→T1/T3 Step4。
- **占位符扫描**:Metal 端 API 序细节(属性 selector 全集/索引位宽)标注"实现者按 Metal.h 核对并注释"——与 SDL/D3D11/FT 头文件纪律同源(记忆值不可信),非 TBD;Selector 常量清单已列全核心集。
- **类型一致性**:NativeLib.Resolve/IFontFamilyProbe/MsgSend 委托名/MetalContext 签名跨任务一致;App.NormalizeBackendName 既有签名扩展不破坏。
