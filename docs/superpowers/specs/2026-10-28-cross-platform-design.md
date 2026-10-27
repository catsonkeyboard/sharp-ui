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
