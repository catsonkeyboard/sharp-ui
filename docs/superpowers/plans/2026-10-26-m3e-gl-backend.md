# M3e 前半:OpenGL 后端 + 接口收尾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 IRenderContext v2 上交付第二后端 OpenGL 3.3 core(Windows 本机可开发验证),完成 TextAtlas/AppWindow 接口化(spec 背记债),确立后端选择机制;Metal 与三平台 CI 待 Mac 环境与 GitHub remote(本计划不含)。

**Architecture:** GL 走 SDL2 原生 GL 路线:GL 上下文必须建在**窗口属主 SDL 模块**上(裁定#38 二次实证,WMInfo 探测定位属主),GL 函数经 `SDL_GL_GetProcAddress` 委托加载;GLSL 330 core 新源(现有 GLSL 源是 Vulkan 风格不可用,保留不动);零分配绘制直钉数组(GL `glBufferSubData` 显式长度语义,无 D3D11 整块 scratch 之需)。D3D11 保持 Windows 默认,`SHARPUI_BACKEND=gl` 环境变量覆盖。

**Tech Stack:** .NET 10、SDL2 GL 原生路径(活模块函数指针)、OpenGL 3.3 core、xUnit GPU 测试(隐藏窗 + glReadPixels)。

**Spec:** `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(D27/M3e 行 + M3d-ii 终审背记的接口债与 M3e 准备清单)

## Global Constraints

- 零 `unsafe`(NativeLibrary/GetDelegateForFunctionPointer/GCHandle 模式,同 M3d-ii)。
- NativeAOT 兼容。
- 每帧稳态分配 ≤8192B:GL 绘制路径零新分配(直钉数组 + glBufferSubData 显式长度)。
- SDL 模块纪律(裁定#38 修订):任何 SDL_* 窗口相关调用必须落在**窗口属主模块**;属主经多候选 WMInfo 探测定位,禁止假设落点,禁止裸 DllImport。
- GL 语义差异必须显式处理并注释:默认帧缓冲 Y 轴下原点(顶点投影翻转、glScissor Y 翻转、glReadPixels 行序翻转)。
- 图集初始内容契约(M3d-ii 终审遗留):构造期显式上传全零(两后端一致;字形 padding 依赖透明零,不得依赖驱动隐式清零)。
- 全量测试门槛:基线 167/167;桌面锁定下窗口 Present 类验证用降级标准(进程存活+无异常+隐藏窗读回断言),headless GPU 测试正常跑。
- git 每任务至少一提交。

---

### Task 1: TextAtlas/AppWindow 接口化 + AtlasSize 归接口 + 图集零初始化契约

**Files:**
- Modify: `src/SharpUI/RenderContext.cs`(接口加 `uint AtlasSize { get; }` 成员与图集零初始化契约注释)
- Modify: `src/SharpUI/Backends/D3D11Context.cs`(实现 AtlasSize;构造期上传 4MB 全零图集)
- Modify: `src/SharpUI/Elements/TextAtlas.cs`(D3D11Context → IRenderContext,字段/ctor 参/7 处 AtlasSize 引用)
- Modify: `src/SharpUI/AppWindow.cs`(Ctx 字段 D3D11Context → IRenderContext)
- Test: 全量回归(既有测试即覆盖面;新增一条图集零初始测试)

**Interfaces:**
- Consumes: 现有 `IRenderContext`(9 成员)、`D3D11Context`。
- Produces: `IRenderContext.AtlasSize { get; }(=1024)`;`TextAtlas(IRenderContext ctx, float scale = 1f, string[]? fallbackFamilies = null)`;`AppWindow.Ctx` 类型 `IRenderContext`。后续 T3/T4 的 GLContext 依此契约实现。

- [ ] **Step 1: 写失败测试(图集零初始)**

`tests/SharpUI.Tests/D3D11ContextTests.cs` 追加:

```csharp
    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_FreshAtlas_IsAllTransparentZero()
    {
        // M3e 契约:图集构造期显式全零(字形 padding 依赖透明零,不得依赖驱动隐式清零)
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.BeginFrame(0, 0, 0, 1f);
        // 用白象素铺满采样区+全图集 UV,任何未写区域透明即图集为零
        // 更直接:直接上传 2x2 白块到 (0,0),画满屏 quad UV=全图集,断言除白块外全透明
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 2, 2);
        float inv = 1f / D3D11Context.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 0f : 64f, i < 2 ? 0f : 64f),
            new PointF(i % 2 == 0 ? 0f : 1f, i < 2 ? 0f : 1f),
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.SetScissor(0, 0, 64, 64);
        ctx.Draw(verts, 4, idx, 6, 0);
        byte[] px = ctx.ReadBackMainFramebuffer();
        // 白块 (0,0)-(2,2)/1024 → 屏幕上 0..2px 白;其余像素 = 图集零 × 白 = 透明黑 [0,0,0,0] 在 BGRA 清屏之上混合 → 清屏色
        // 清屏 (0,0,0,1):透明源 × 任何目标 → 目标(清屏)。断言 (32,32) 为清屏色、(0,0) 为白。
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 0, 0));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(px, 32, 32));
    }
```

(注:该测试在零初始化缺失且驱动残留旧数据时才可能失败——多数驱动恰会清零,属契约防御;若实现前即绿,在报告中注明并以代码审查保证构造期显式上传存在。)

- [ ] **Step 2: 跑测试(允许直接绿,契约防御性),随后全量基线确认 167/167**

Run: `dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q`

- [ ] **Step 3: 实现**

1. `RenderContext.cs` 接口追加(放在 TargetWidth 之前,文档注明"图集边长契约,后端必须构造期显式上传全零"):

```csharp
    /// <summary>图集边长(像素,方形)。后端契约:构造期显式上传全零初始内容
    /// (字形 padding 依赖透明零;D3D11/GL 的"初始内容未定义"语义不得依赖驱动隐式清零)。</summary>
    uint AtlasSize { get; }
```

2. `D3D11Context`:实现 `public uint AtlasSize => 1024;`(删除原 `public const uint AtlasSize = 1024;` 前先全仓 grep 引用——TextAtlas/测试均改走接口或实例属性;`const` 改属性是破坏性变更,所有 `D3D11Context.AtlasSize` 静态引用处必须改为实例/接口访问,逐一列出改动);构造管线里图集纹理创建后立即 `UpdateAtlasRegion(new byte[1024*1024*4], 0, 0, 1024, 1024)`(4MB 构造期一次性,注释注明契约)。
3. `TextAtlas.cs`:字段/ctor 参改 `IRenderContext`;`D3D11Context.AtlasSize` 7 处 → `_ctx.AtlasSize`(实例属性)。
4. `AppWindow.cs`:`internal IRenderContext Ctx = null!;`(注释同步:消费面 UpdateAtlasRegion/Present/Resize/Dispose,spec 背记债清偿)。
5. 检查 `App.cs` Run 循环对 `w.Ctx` 的使用(Present)——接口已有,无需改;`D3D11ContextHeadlessTests` 等测试中 `GraphicsContext.AtlasSize`/`D3D11Context.AtlasSize` 静态引用改实例访问。

- [ ] **Step 4: 全量测试绿 + 提交**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
git add -A
git commit -m "refactor(m3e): textatlas/appwindow consume irendercontext, atlas-size contract, d3d11 atlas zero-init"
```

---

### Task 2: SDL GL 基座——属主模块 GL 绑定 + 上下文创建 + GL 函数加载器

**Files:**
- Modify: `src/SharpUI/Backends/SdlNative.cs`(加 OwnerModule 探测 + SDL_GL_* 绑定)
- Create: `src/SharpUI/Backends/GL.cs`(GL 函数委托加载器 + 常量)
- Test: `tests/SharpUI.Tests/GLBaseTests.cs`

**Interfaces(Produces,T3 消费):**
- `SdlNative.OwnerModule(IntPtr sdlWindow)` → 窗口属主 SDL 模块句柄(WMInfo 多候选探测复用,窗口级缓存)。
- `SdlNative` GL 面(internal):`SetGlAttribute(int attr, int value)`、`CreateGlContext(IntPtr sdlWindow)` → IntPtr、`MakeCurrent(IntPtr sdlWindow, IntPtr ctx)`、`SwapWindow(IntPtr sdlWindow)`、`SetSwapInterval(int)`、`GetGlProcAddress(string)` → IntPtr。
- `GL` internal static class:`EnsureLoaded()`(惰性,首次调用经 GetGlProcAddress 解析全部函数);约 45 个函数委托(见 Step 3 清单);GL 常量(GL_* 枚举值)。
- Consumes: 现有 SdlNative 候选机制。

- [ ] **Step 1: 写失败测试**

`tests/SharpUI.Tests/GLBaseTests.cs`:

```csharp
using SharpUI.Backends;

namespace SharpUI.Tests;

public class GLBaseTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void GlContext_33Core_CreatesAndClearsOnHiddenWindow()
    {
        SdlTextInput.EnableImeUi();
        SdlNative.EnsureVideoInit();
        SdlNative.SetGlAttribute(17 /*SDL_GL_CONTEXT_MAJOR_VERSION*/, 3);
        SdlNative.SetGlAttribute(18 /*SDL_GL_CONTEXT_MINOR_VERSION*/, 3);
        SdlNative.SetGlAttribute(21 /*SDL_GL_CONTEXT_PROFILE_MASK*/, 0x0002 /*CORE*/);
        SdlNative.SetGlAttribute(14 /*SDL_GL_DOUBLEBUFFER*/, 1);
        var win = new Veldrid.Sdl2.Sdl2Window("gl-base-test", 50, 50, 64, 64,
            Veldrid.Sdl2.SDL_WindowFlags.OpenGL | Veldrid.Sdl2.SDL_WindowFlags.Hidden, false);
        IntPtr ctx = SdlNative.CreateGlContext(win.SdlWindowHandle);
        Assert.NotEqual(IntPtr.Zero, ctx);
        try
        {
            Assert.Equal(0, SdlNative.MakeCurrent(win.SdlWindowHandle, ctx));
            GL.EnsureLoaded();
            string ver = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(GL.GetString(0x1F00 /*GL_VERSION*/))!;
            Assert.Contains("3.3", ver);   // 至少 3.3(驱动可给更高)
            GL.ClearColor(0.07f, 0.07f, 0.08f, 1f);
            GL.Clear(0x4000 /*GL_COLOR_BUFFER_BIT*/);
            byte[] px = new byte[64 * 64 * 4];
            GL.ReadPixels(0, 0, 64, 64, 0x1908 /*GL_RGBA*/, 0x1401 /*GL_UNSIGNED_BYTE*/, px);
            // GL 行序自下而上:(0,0)=屏幕左下;清屏色一致无方向性,任取两点
            Assert.Equal(18, px[0]);   // B? no — GL RGBA 字节序:R=0.07*255≈18
            Assert.Equal(255, px[3]);
        }
        finally { win.Close(); }
    }
}
```

(注:`Assert.Contains("3.3", ver)` 对 4.x 驱动会失败——改为解析主版本 ≥3 且次版本场景断言:提取前两段数字,`major>3 || (major==3 && minor>=3)`。测试文件里直接写解析逻辑。)

- [ ] **Step 2: 跑测试确认失败(SdlNative 无 GL 面/GL 类不存在,编译错)**

- [ ] **Step 3: 实现**

1. `SdlNative` 追加:
   - `OwnerModule(IntPtr sdlWindow)`:复用 Candidates() 逐个 WMInfo 探测(与 GetWin32Hwnd 同判定),返回应答模块句柄;`ConditionalWeakTable`-式窗口级缓存(Dictionary<IntPtr,IntPtr> 即可,窗口关闭不强求清理)。
   - GL 六函数:在属主模块上 `Fn<T>(h, "SDL_GL_SetAttribute")` 等。**SDL_GL_SetAttribute 必须在建窗前调用**——本任务测试中演示序;App 接线(T4)注意时序。
   - `GetGlProcAddress(string)`:SDL_GL_GetProcAddress(属主或 primary 模块)→ IntPtr。
2. `GL.cs`:惰性单例加载;函数清单(全部 `[UnmanagedFunctionPointer(CallingConvention.Winapi)]` — GL 入口约定 WINAPI/stdcall 于 x64 统一):
   - 信息:GetString(uint)→IntPtr、GetError()→uint
   - 着色器:CreateShader/ShaderSource(uint, int, IntPtr[], int[])经 GCHandle 或 AllocHGlobal 传源码指针/CompileShader/GetShaderiv/GetShaderInfoLog/DeleteShader/CreateProgram/AttachShader/LinkProgram/GetProgramiv/GetProgramInfoLog/UseProgram
   - VAO:GenVertexArrays(int, uint[])/BindVertexArray/EnableVertexAttribArray/VertexAttribPointer(uint idx, int size, uint type, bool normalized, int stride, int offset)
   - 缓冲:GenBuffers/BindBuffer(uint target, uint id)/BufferData(uint, IntPtr size, IntPtr data, uint usage)/BufferSubData(uint, IntPtr offset, IntPtr size, IntPtr data)/DeleteBuffers/BindBufferBase(uint target, uint index, uint id)
   - 纹理:GenTextures/BindTexture/TexImage2D(完整 12 参)/TexSubImage2D/DeleteTextures/TexParameteri/ActiveTexture
   - 状态:Enable/Disable(uint)/BlendFunc/Viewport/Scissor/ClearColor/Clear
   - 绘制:DrawElements(uint mode, int count, uint type, IntPtr indices)
   - 采样器:GetUniformLocation(uint, string)→int/Uniform1i(int, int)
   - 读回:ReadPixels(完整 7 参,数据经预分配 byte[] + GCHandle 钉)
   - 常量:COMPILE_STATUS 0x8B81/LINK_STATUS 0x8B82/VERTEX_SHADER 0x8B31/FRAGMENT_SHADER 0x8B30/ARRAY_BUFFER 0x8892/ELEMENT_ARRAY_BUFFER 0x8893/UNIFORM_BUFFER 0x8A11/STATIC_DRAW 0x88E4/DYNAMIC_DRAW 0x88E8/TEXTURE_2D 0x0DE1/TEXTURE0 0x84C0/RGB/RGBA 0x1908/UNSIGNED_BYTE 0x1401/UNSIGNED_SHORT 0x1403/FLOAT 0x1406/UNSIGNED_INT_8_8_8_8_REV 0x8367/FONT/scissor 0x0C11/blend 0x0BE2/COLOR_BUFFER_BIT 0x4000/TRIANGLES 0x0004/TEXTURE_MIN_FILTER 0x2801/TEXTURE_MAG_FILTER 0x2800/NEAREST 0x2600/TEXTURE_WRAP_S 0x2802/TEXTURE_WRAP_T 0x2803/CLAMP_TO_EDGE 0x812F(值以 glew.h/gl3w 头或 OpenGL wiki 为准,实现时逐一注释出处)
   - 加载器:每个函数 `GetProcAddress(name)` 非零校验,失败抛 `InvalidOperationException("GL 函数缺失:"+name)`;一次 EnsureLoaded 全解析,字段缓存。
3. 修正 Step 1 测试的版本断言为解析式。

- [ ] **Step 4: 跑测试通过(隐藏窗 GL 上下文不依赖桌面交互状态,锁定环境可用)+ 全量绿 + 提交**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
git add -A
git commit -m "feat(m3e): sdl gl bindings on owner module + gl 3.3 core context + gl function loader"
```

---

### Task 3: GLContext : IRenderContext 全功能(headless 像素验证)

**Files:**
- Modify: `src/SharpUI/Shaders.cs`(追加 `Glsl330VertexSource`/`Glsl330FragmentSource`)
- Create: `src/SharpUI/Backends/GLContext.cs`
- Test: `tests/SharpUI.Tests/GLContextTests.cs`

**Interfaces:**
- Consumes: T1 的 IRenderContext(含 AtlasSize)、T2 的 SdlNative GL 面 + GL 加载器。
- Produces: `public sealed class GLContext : IRenderContext`(T4 接线消费):
  - `static GLContext CreateForWindow(IntPtr sdlWindow, uint width, uint height)`(窗口路径:上下文已由调用方创建并 current,或内部创建——设计取舍:内部经 SdlNative.CreateGlContext 创建并持有,T4 传 sdlWindow 即可)
  - `static GLContext CreateHeadless(uint width, uint height)`(64×64 隐藏窗 + GL flag + 自建上下文;窗口句柄内部持有,Closing 释放)
  - `ReadBackMainFramebuffer()`(glReadPixels 全帧 + **行序翻转到左上原点**,与 D3D11 读回字节序 RGBA 一致化——GL 读回即 RGBA,无需换序,仅行序)
  - IRenderContext 全成员;`Resize` = 更新尺寸(GL 窗口由 SDL 管理,仅记录 + viewport 随 BeginFrame 刷新;不接受 0——同 D3D11 语义)

**实现要点(实现者按此落码):**
- GLSL 330 core 新源(Vulkan 风格旧源保留不动):

```glsl
// VS(#version 330 core)
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;   // UNSIGNED_INT_8_8_8_8_REV normalized → 归一化 rgba
layout(std140) uniform ScreenSize { vec2 uSize; };
out vec2 vTexCoord; out vec4 vColor;
void main() {
    // GL 默认帧缓冲原点左下:顶点系左上原点 → 相对 D3D 版翻转 Y
    gl_Position = vec4(aPosition.x / uSize.x * 2.0 - 1.0, aPosition.y / uSize.y * 2.0 - 1.0, 0.0, 1.0);
    vTexCoord = aTexCoord; vColor = aColor;
}
// FS(#version 330 core)
in vec2 vTexCoord; in vec4 vColor;
uniform sampler2D uAtlas;   // 纹理单元 0
out vec4 outColor;
void main() { outColor = vColor * texture(uAtlas, vTexCoord); }
```

- 顶点色字节序:UiVertex 内存 R,G,B,A;D3D11 R8G8B8A8_UNORM 直读 rgba。GL `glVertexAttribPointer(2, 4, GL_UNSIGNED_BYTE, true, 20, 16)` 以 RGBA 顺序解释 4 字节 → 与内存 R,G,B,A 一致 ✓(与 D3D11 相同结果;红通道测试锁定)。
- std140 UBO:vec2 单成员占 16 字节槽(std140 对齐),上传 16 字节(2 float + pad);SetScreenSize 写 8 字节 BufferSubData。
- Draw:BindVertexArray + 各缓冲 Bind + **直钉数组**(GCHandle.Pin vertices/indices → glBufferSubData(0, count*size, pinnedPtr))——GL 显式长度语义,无 scratch;容量增长 ×2(BufferData 重建);分段重定基:**不需要**(GL 直接 DrawElements(count, type, indices=startIndex*sizeof(ushort) 偏移指针)——索引偏移参数原生支持,无需拷贝!绘制调用 `DrawElements(TRIANGLES, (int)indexCount, UNSIGNED_SHORT, (IntPtr)(startIndex*2))`,要素缓冲常驻一整段上传 [0..totalNeeded))。注意与 D3D11 的契约差异:接口注释(T1 修订版)允许"拷入自有 scratch 或原生偏移",此处选原生偏移,注释说明。
- BeginFrame:MakeCurrent(窗口路径多窗切换)+ Viewport(0,0,w,h)+ ClearColor+Clear+Enable(BLEND)+BlendFunc(ONE, INV_SRC_ALPHA)(预乘)。
- SetScissor(x,y,w,h):GL 坐标 Y 下原点 → `glScissor(x, fbH - y - h, w, h)` + Enable(SCISSOR);**与目标全等时 Disable(SCISSOR)**(Renderer 对 FullClip 传全屏矩形,禁用即全屏,等价且省状态)。
- Present:SwapWindow(隐藏窗 no-op 语义安全);VSync 经 SetSwapInterval(1) 建上下文后设一次。
- Atlas:1024² RGBA,TexImage2D 全零初始(4MB 构造期)+ TexParameteri(NEAREST/CLAMP_TO_EDGE);UpdateAtlasRegion = 直钉 rgba + TexSubImage2D。
- Dispose:DeleteTextures/Buffers/VAO/Program + 上下文销毁(SDL_GL_DeleteContext 需在 SdlNative 补一个绑定)+ 隐藏窗 Close。
- **测试镜像 D3D11ContextTests 七项**(清屏/白四边形/红通道 BGRA→**GL 为 RGBA,期望 [255,0,0,255] 表红**/剪裁(注意 GL scissor Y 翻转路径)/两段 startIndex(GlDrawElements 偏移路径)/Resize 抛 0/AtlasSize==1024),像素断言值按 GL RGBA 字节序 + 行序已由读回翻转对齐;读回 helper 同款 Pixel(px,x,y)。

- [ ] **Step 1: 写失败测试(GLContextTests 七项,镜像 D3D11ContextTests 结构,期望值按 GL 语义)**
- [ ] **Step 2: 跑确认编译失败**
- [ ] **Step 3: 实现 GLContext + GLSL 源**
- [ ] **Step 4: 跑过滤测试全过 + 全量绿 + 提交**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
git add -A
git commit -m "feat(m3e): opengl 3.3 context implementing irendercontext, headless pixel-verified"
```

---

### Task 4: App 后端选择接线 + GL 窗口冒烟 + 收官文档

**Files:**
- Modify: `src/SharpUI/App.cs`(CreateWindow 后端选择;GL 路径建窗加 OpenGL flag + GL 上下文时序)
- Modify: `src/SharpUI/Backends/SdlNative.cs`(若 T2/T3 未补 DeleteContext 则补)
- Test: `tests/SharpUI.Tests/BackendSelectionTests.cs` + 冒烟
- Modify: `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(M3e 前半完成标注:GL 后端 ✅ Windows 实证;Metal/CI 待环境;裁定#41:GL 后端路线定稿——SDL GL 属主模块/Y 翻转三点/索引偏移直绘;TextAtlas/AppWindow 接口债已清偿)、`docs/notes/perf-baselines.md`(GL 后端实测,如测)

**Interfaces:**
- Consumes: T1-T3 全部。
- Produces: `SHARPUI_BACKEND` 环境变量(`d3d11`(默认 Windows)/`gl`;非 Windows 默认 `gl`);`Renderer`/`TextAtlas`/`AppWindow` 对两后端透明。

- [ ] **Step 1: 写失败测试(后端选择逻辑纯函数 + GL 窗口 harness 冒烟)**

`tests/SharpUI.Tests/BackendSelectionTests.cs`:

```csharp
using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

public class BackendSelectionTests
{
    [Theory]
    [InlineData("gl", "gl")]
    [InlineData("d3d11", "d3d11")]
    [InlineData("GL", "gl")]
    [InlineData("", "default")]
    [InlineData("bogus", "default")]
    public void BackendName_Normalizes(string raw, string expected) =>
        Assert.Equal(expected, App.NormalizeBackendName(raw));

    [Fact]
    [Trait("Category", "Gpu")]
    public void GlWindowHarness_90FramesClearAndQuad_PixelsStable()
    {
        // 与 M3d-ii T4 harness 同法:真窗 + GL 上下文 + 90 帧(清屏+白四边形)读回断言
        // 桌面锁定降级标准:进程内无异常 + 读回像素命中即过(不依赖 vblank/交互)
        SdlTextInput.EnableImeUi();
        SdlNative.EnsureVideoInit();
        SdlNative.SetGlAttribute(17, 3); SdlNative.SetGlAttribute(18, 3);
        SdlNative.SetGlAttribute(21, 0x0002); SdlNative.SetGlAttribute(14, 1);
        var win = new Veldrid.Sdl2.Sdl2Window("gl-harness", 60, 60, 64, 64,
            Veldrid.Sdl2.SDL_WindowFlags.OpenGL | Veldrid.Sdl2.SDL_WindowFlags.Shown, false);
        try
        {
            using var ctx = GLContext.CreateForWindow(win.SdlWindowHandle, 64, 64);
            ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
            float inv = 1f / ctx.AtlasSize;
            var verts = new UiVertex[4];
            for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
                new PointF(i % 2 == 0 ? 16f : 48f, i < 2 ? 16f : 48f),
                new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv), Color32.White);
            ushort[] idx = { 0, 1, 2, 2, 1, 3 };
            for (int f = 0; f < 90; f++)
            {
                ctx.SetScreenSize(64, 64);
                ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
                ctx.SetScissor(0, 0, 64, 64);
                ctx.Draw(verts, 4, idx, 6, 0);
                ctx.Present();
            }
            byte[] px = ctx.ReadBackMainFramebuffer();   // 窗口帧缓冲读回(窗口路径)
            // GL RGBA + 行序翻转:白 (255,255,255,255) @中心;清屏 (18,18,20,255) @角落
            int i1 = (32 * 64 + 32) * 4, i2 = (4 * 64 + 4) * 4;
            Assert.Equal(255, px[i1]); Assert.Equal(255, px[i1 + 3]);
            Assert.Equal(18, px[i2]); Assert.Equal(20, px[i2 + 2]); Assert.Equal(255, px[i2 + 3]);
        }
        finally { win.Close(); }
    }
}
```

- [ ] **Step 2: 确认失败(NormalizeBackendName 不存在)**

- [ ] **Step 3: 实现**

`App.cs`:

```csharp
    /// <summary>后端名归一(SHARPUI_BACKEND):空/未识别 → "default"(平台默认)。</summary>
    internal static string NormalizeBackendName(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "gl" or "opengl" => "gl",
        "d3d11" or "d3d" => "d3d11",
        _ => "default",
    };

    static AppWindow CreateWindow(string title, int width, int height, Func<Element>? render)
    {
        SdlTextInput.EnableImeUi();
        SdlNative.EnsureVideoInit();
        string backend = NormalizeBackendName(Environment.GetEnvironmentVariable("SHARPUI_BACKEND"));
        bool useGl = backend == "gl" || (backend == "default" && !OperatingSystem.IsWindows());
        if (useGl)
        {
            // GL 属性须在建窗前(SDL_GL_SetAttribute 时序);3.3 core + 双缓冲
            SdlNative.SetGlAttribute(17, 3); SdlNative.SetGlAttribute(18, 3);
            SdlNative.SetGlAttribute(21, 0x0002); SdlNative.SetGlAttribute(14, 1);
        }
        var flags = SDL_WindowFlags.Resizable | SDL_WindowFlags.Shown | (useGl ? SDL_WindowFlags.OpenGL : 0);
        var window = new Sdl2Window(title, 100, 100, width, height, flags, false);
        IRenderContext ctx = useGl
            ? GLContext.CreateForWindow(window.SdlWindowHandle, (uint)window.Width, (uint)window.Height)
            : D3D11Context.CreateForWindow(SdlNative.GetWin32Hwnd(window.SdlWindowHandle),
                (uint)window.Width, (uint)window.Height);
        window.Resized += () => { if (window.Width > 0 && window.Height > 0) ctx.Resize((uint)window.Width, (uint)window.Height); };
        // ...(Scale/AppWindow 组装与 FocusLost 与现状同,ctx 变接口类型)
    }
```

(若 `SDL_WindowFlags` 枚举位组合需 cast,按实际调整;`|` 于枚举可用即用。)

- [ ] **Step 4: 测试过滤 + 全量绿 + demo 双后端冒烟**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
dotnet build examples/Counter/Counter.csproj -v q
```
Counter 默认(D3D11)与 `SHARPUI_BACKEND=gl ./Counter.exe` 各启动一次:进程存活 ≥5s、无未处理异常(桌面锁定降级标准);解锁桌面时目视对比两后端渲染一致性。

- [ ] **Step 5: 文档收官 + 提交**

spec M3e 前半标注 + 裁定#41;perf-baselines 如 GL 有可比数据则记录。

```bash
git add -A
git commit -m "feat(m3e): backend selection wiring, gl window harness verified, docs"
```

---

## Self-Review 结论(已执行)

- **Spec 覆盖**:接口债背记(spec M3d-ii 终审)→ T1;GL 后端(D27 后半)→ T2-T4;图集零初始契约 → T1;Metal/CI 明示不在本计划(spec 标注待环境)。
- **占位符**:GL 函数清单以显式枚举+签名规格给出(机械展开由实现者按模式完成,非 TBD);GLSL/Y 翻转/测试期望值均给出确定内容。
- **类型一致性**:IRenderContext.AtlasSize 在 T1 定义、T3 GLContext/T4 消费一致;GL 像素期望(RGBA/行序翻转)在 T3/T4 测试中一致区分于 D3D11 的 BGRA。

> **勘误(T2 实现裁定,后续任务以实码为准):** GL_VERSION=0x1F02(0x1F00=VENDOR);SDL_GLattr:DOUBLEBUFFER=5(14=MULTISAMPLESAMPLES)、CONTEXT_PROFILE_MASK CORE=0x0001(0x0002=COMPATIBILITY)、MAJOR=17/MINOR=18/MASK=21 不变;RGB=0x1907/RGBA=0x1908;TexImage2D 九参。VertexAttribPointer 末参 IntPtr(第 6 参指针);建窗前 SetGlAttribute 全候选设置(属主不可知),窗口参 GL 函数严格 OwnerModule。

> **勘误(T3 实现裁定,后续任务以实码为准):** ① GLSL 330 VS 的 Y 映射与 HLSL 版相同(1.0 - y/h*2),简报片段(y/h*2-1)系方向性笔误 —— 两 API 的 NDC 同向(+1=窗口上沿),着色器翻 Y 会使帧缓冲内容垂直镜像(可见窗 UI 上下颠倒)且与读回行序翻转叠加双翻转;GL 自底向上差异吸收于读回行序翻转 + glScissor Y 翻转两处(像素测试两段 startIndex 锁定方向)。② T2 加载器清单外增 4 函数(glGetUniformBlockIndex/glUniformBlockBinding/glDeleteProgram/glDeleteVertexArrays)与 3 常量(GL_ONE=1/GL_ONE_MINUS_SRC_ALPHA=0x0303/GL_RGBA8=0x8058,SDK GL.h + gl3.h 实数):UBO 绑定点 330 core 无 layout(binding) 不得假设默认 0;Dispose 对象卫生。③ 评审 Important#1 ①②(SetGlAttribute 去 early-return 全候选收 + EnsureVideoInit 全候选 SDL_Init)提前于 T3 落地 —— CreateHeadless 自建窗口即需之;实证:修复后 GL_VERSION="3.3.0 Core Profile Context"(修复前属主副本未收属性,建的是兼容上下文,亦过版本下限断言);T4 仅余交接提示修正。④ IRenderContext.Draw 契约注释落 M3e 修订版(上传语义两可:D3D11 scratch 整块 / GL 直钉显式长度 + DrawElements 原生偏移)。
