# 跨平台胶水层 + Metal 后端(本机预置,待实机激活)设计

日期:2026-10-28 | 状态:已批准(用户指定方案:本机写好+Windows 编译与全量绿,实机验证待 CI/Mac) | 前置:D6 收官(8965e60,392/392)

## 目标

在 Windows 本机完成三件事的**代码与测试预置**,使接入 GitHub Actions 或 Mac 硬件后"开机即用":
1. **动态库名跨平台映射**:`SDL2.dll → libSDL2-2.0.so.0(Linux) / libSDL2-2.0.dylib(macOS)`、`freetype.dll → libfreetype.so.6 / libfreetype.6.dylib`、`harfbuzz.dll → libharfbuzz.so.0 / libharfbuzz.0.dylib`。
2. **Linux/macOS 字体发现器**:`IFontLocator` 的两实现,替代现"非 Windows 返回 null"占位。
3. **Metal 后端**:`MetalContext : IRenderContext`(Objective-C runtime `objc_msgSend` 直连,MSL 着色器运行时编译,零第三方包)。

**验收口径(本机可达)**:Windows 编译零错误、全量 392+ 测试全绿(新增 Linux/macOS 发现器纯逻辑单测 + Metal 声明层结构自检)、AOT 产物不变;实机验证标注"待 CI"不入本门槛。

## 已批准决策

| # | 决策 |
|---|---|
| X-1 | 动态库名:集中常量表 `NativeLib`(`OperatingSystem.IsLinux/IsOSPlatform("MACCATALYST")→.dylib`);DllImport 改引常量;DllImport 解析失败时回退 `NativeLibrary.TryLoad` 全名探测(SDL/FT/HB 三个互操作类,同 SdlNative 既有模式) |
| X-2 | 字体发现:Linux=fontconfig 约定目录扫描(`/usr/share/fonts`、`~/.local/share/fonts`,递归 ttf/otf,族名从 FreeType face 读取匹配);macOS=CoreText 经 objc_msgSend 直连(**零第三方包**;失败回退固定路径 `/System/Library/Fonts`、`/Library/Fonts`,同 Linux 的 face 匹配)。默认主族/回退族三平台映射:Windows Segoe UI / Linux "Noto Sans"/"Noto Sans CJK SC"(或 DejaVu Sans 回退)/ macOS "Helvetica Neue"/"PingFang SC"+ "Apple Color Emoji" |
| X-3 | Metal 后端:`MetalContext : IRenderContext`;`objc_msgSend` 委托 + `sel_registerName` 经 `libobjc.dylib`(Linux 上不加载,IsMacOS 守卫);`MTLCreateSystemDefaultDevice`(Metal.framework);`CAMetalLayer` 为 layer(NSView.layer 或离屏 `CALayer`——headless 用 `CAMetalLayer` 独立实例+离屏 texture 读回);MSL 着色器 `MTLLibrary newLibraryWithSource`(等价 GLSL/HLSL 语义:预乘白×顶点色、Y 同 D3D 上行 NDC);顶点缓冲 `MTLBuffer newBufferWithLength`(Shared);绘制 `MTLCommandBuffer`/`MTLRenderCommandEncoder`(setVertexBuffer/setFragmentTexture/setFragmentSampler/loadAction Clear/storeAction Store/drawPrimitives);scissor per ClipRun;Present=`CAMetalLayer.nextDrawable`+present;`ReadBackMainFramebuffer`=getBytes(离屏 texture) |
| X-4 | 实机验证门:所有 Linux/macOS/Metal 代码标注 `// 待 CI 实机验证`(类头注释+测试 Skip 门控 `SHARPUI_RUN_MAC=1`);Windows 本机门槛=编译+全量绿+纯逻辑单测(目录扫描/常量映射/选择器结构) |

## 架构

```
src/SharpUI/
 ├─ NativeLib.cs                    三平台库名常量表(IsWindows/IsLinux/IsMac)
 ├─ Text/FontLibrary.cs             Locate 按 OS 分派(替换非 Windows null 占位)
 ├─ Text/LinuxFontLocator.cs        fontconfig 约定目录递归扫描 + FT face 族名匹配(FT_New_Memory_Face 探测,不落盘)
 ├─ Text/MacFontLocator.cs          CoreText(objc)优先,失败回退固定目录扫描(同 Linux 匹配法)
 ├─ Backends/ObjectiveC.cs          objc_msgSend 委托族(各签名变体)/sel_registerName/objc_getClass(libobjc.dylib;IsMacOS 守卫)
 ├─ Backends/MetalNative.cs         Metal 框架函数(MTLCreateSystemDefaultDevice)+ 全部 Selector 常量(实测来源:Metal 框架头文件,注释出处)
 ├─ Backends/MetalContext.cs        IRenderContext 实现(headless=独立 CAMetalLayer 离屏;窗口=接 NSView layer——窗口创建侧接 SDL macOS 后端留 M3e 后半,本任务只做 headless 路径+窗口路径签名)
 └─ Shaders.cs                      MslVertexSource/MslFragmentSource(#include <metal_stdlib>,与 GLSL/HLSL 同语义)
```

## 测试策略(Windows 本机可跑)

1. `NativeLibTests`:三平台库名映射断言(纯逻辑,注入假 OS 判定或拆 `Resolve(OSPlatform)` 纯函数)。
2. `LinuxFontLocatorTests`:目录扫描/匹配逻辑以**可注入目录根**单测(临时目录伪造 fonts 树,族名匹配接口 mock FT face 读取)。
3. `MetalNativeTests`:Selector 常量非空/objc 委托签名结构(编译期);`MetalContextTests` 全部 `[Fact(Skip="待 CI 实机验证")]` 或 env 门控占位(结构与 GL headless 镜像,激活后即像素读回)。
4. Windows 既有 392 测试零回归 + AOT 发布不变(5.97MiB 级)。

## 范围外

CI workflow 编写(待 GitHub remote);Linux GL 实机验证;macOS 窗口路径(SDL NSView 接 CAMetalLayer);Metal 帧调优。

## 实施纪要(Task 1-3 交付,2026-10-28 收官;裁定#48)

- **T1(1faa630 + de26f3a)**:NativeLib 常量表+Resolve 纯函数;LinuxFontLocator(fontconfig 目录扫描,目录查询排除 bold 面勘误)/MacFontLocator(CoreText+回退);FontLibrary 平台分派+默认族三平台映射;NativeLibTests/FontLocatorTests/FontLibraryTests 纯逻辑全矩阵。
- **T2(aa901e0 + 38be1cc)**:ObjectiveC 19 个 MsgSend 定参包装(调用约定/所有权交接/error 槽有效性三论证入类头;objectAtIndexedSubscript 单下标形态勘正);MetalNative 37 Selector 契约集+MTLCreateSystemDefaultDevice(绝对路径 DllImport)+MTLScissorRect/MTLRegion 按值结构体;MSL 顶点/片段源;ObjectiveCTests 契约锁(ExpectedSelectors 精确集合+签名断言+MSL 结构标记)。
- **T3(本提交)**:
  - `MetalContext.cs`(新):headless 全路径实现,API 序逐出处注释(类头+方法头);**顶点表错槽裁定**——顶点描述符 attributes bufferIndex=1,与 MSL `uSize [[buffer(0)]]` 错开(stage_in 装配与显式 buffer 引用共顶点缓冲表,同槽冲突);**帧模型裁定**——编码-提交,headless Present 严格 no-op,FlushFrame(endEncoding+commit[+waitUntilCompleted])在下一 BeginFrame/ReadBack/Dispose 落地;**索引裁定**(简报留白"32 位或 16→32 展开实现者定")——取 drawIndexedPrimitives 原生 UInt16+字节偏移(对位 GL DrawElements,免逐帧展开);**Resize 裁定**——离屏目标重建而非 GL 式仅记录(texture 无法长出像素,属性语义镜像锁定);libc memcpy DllImport(/usr/lib/libSystem.dylib,Marshal.Copy 无 span→指针重载,.NET 10 实面核对)作 Shared 缓冲直写原语。
  - `MetalNative.cs`:+25 Selector(62 计)+全部消费枚举常量(RGBA8Unorm=70/Float2=29/UChar4Normalized=9/PerVertex=1/One=1/OneMinusSourceAlpha=5/Add=0/Triangle=3/UInt16=0/Nearest=0/ClampToEdge=0/Shared=0);**T2 枚举勘误**:MTLLoadAction/MTLStoreAction 实数 DontCare=0/Load=1/Clear=2、Store=1(T2 注释记忆笔误;Store 按 T2 原注 0 取值=DontCare 弃读回内容,实质正确性修复)。
  - `ObjectiveC.cs`:+2 包装(MsgSendVoidRegionNUIntPtrNUInt=replaceRegion、MsgSendVoidNUIntNUIntNUIntPtrNUInt=drawIndexedPrimitives 5 参,21 计)。
  - `App.cs`+`NativeLib.cs`:NormalizeBackendName "metal" 分支;CreateWindow 路由(macOS default→metal 预留/Windows+Linux 显式 metal 抛 PlatformNotSupportedException 前置于 SDL/IME);静态构造 SetDllImportResolver(仅非 Windows,BaseNameOf→Resolve→TryLoad,未知名回落)——X-4 门控接线落地。
  - 测试:MetalContextTests 8 项(`SHARPUI_RUN_MAC != "1"` 门控 0ms 空过,镜像 GLContextTests+残留 scissor 复位);BackendSelectionTests metal 归一 3 行+Windows 拒绝 1 项;ObjectiveCTests 契约同步(62/21)。
  - 门槛:全量 **451/451**(439+12);AOT 发布不变(Counter.exe 2,801,152 B vs 基线 2,800,640 B,+512 B,publish 零告警)。
  - 待 CI 实机验证清单八项见主 spec 裁定#48(MetalContextTests 像素/Shared×离散 GPU/MSL 编译/autorelease 累积/Shared 并发/字体发现/Resolver 生效/macOS 路由自证[含 CreateForWindow 抛 NIE 前已建窗的句柄不 Dispose——进程退出场景容忍记账,评审 Minor#6])。

## 评审修复轮(Needs fixes,1C+1I+4M)

- **Critical#1**:MetalContext.BeginFrame 常量缓冲绑定误用两参包装 `MsgSendVoidPtrNUInt`(SelSetVertexBuffer=setVertexBuffer:offset:atIndex: 三实参,x4=index 槽读到未定义残留值)——修为三参,并收拢常量(slot 0)/几何(slot 1)两落位至单一 `BindVertexBuffer` 三参 helper(selector↔包装配对仅此一处,错型结构性不可现;静态锚注门控下防回归)。
- **Important#2**:MetalContextTests 补 `Headless_FreshAtlas_IsAllTransparentZero`(GL 镜像;AtlasSize 契约的构造期全零上传自此有测试触碰)。
- **Minor#3**:`DMsgSendVoidGetBytes` 委托回退 void(getBytes 无返回值)。
- **Minor#4**:实施纪要枚举计数 15→17 更正(报告同步)。
- **Minor#5**:BOOL ABI 附注从 replaceRegion 包装移位至实际承载 BOOL 的 `MsgSendVoidNUInt`(MetalNative.SelSetBlendingEnabled 注释的"见 MsgSendVoidNUInt 注"指针自此有着落)。
- **Minor#6**:macOS default→metal 路径 CreateForWindow 抛 NIE 前已建窗、句柄不经 Dispose——记入 CI 待验清单⑧(进程退出场景,不修码)。
- 复验:全量 452/452(MetalContextTests 9 项门控)。
