# SharpUI 规格书

- 日期:2026-10-25
- 状态:已定稿(M1 范围已锁定,附实施计划 `docs/superpowers/plans/2026-10-25-sharp-ui-m1-foundation.md`)
- 对标:Rust [GPUI](https://github.com/zed-industries/zed)(Zed)、Go [MyGO](https://github.com/egoist/mygo)(egoist)

## 1. 愿景与定位

SharpUI 是一个 C# 原生 GPU 自绘的跨平台应用 UI 框架:

- **GPU 全自绘**:所有 UI(几何、文本、图标)光栅化为三角形网格 + 纹理图集,直接提交 GPU。Windows 用 Direct3D 11,macOS 用 Metal,Linux 用 OpenGL。
- **纯 C# 声明 UI**:无 XAML、无独立标记语言、无 MVVM 强制,控件全部用 C# 代码声明。
- **Native AOT**:发布产物为单文件原生可执行,无需安装运行时。
- **编程模型**:声明式元素树为核心(GPUI 同构),即时模式为逃生舱(egui/MyGO 风格,内嵌工具面板、调试 UI 场景)。

### 非目标(明确排除)

- 不是 WPF/Avalonia 的替代品:不做 XAML、不做样式表系统、不做控件主题包。
- M1 不做:HTML/Webview 混合、动画系统、无障碍、IME、多窗口、滚动列表、diff/patch 增量更新。
- 不支持 AOT 之外的部署形态作为一等公民(开发期 JIT 仅用于调试与热重载)。

## 2. 三项核心决策(用户已拍板)

| # | 决策 | 内容 |
|---|------|------|
| D1 | 编程模型 | **声明式元素树为核心 + 即时模式逃生舱**。即时模式不是独立系统,而是架设在同一套布局/绘制管线上的第二层 API |
| D2 | 图形层 | **M1 起用 Veldrid 4.9.0**(统一封装 D3D11/Metal/OpenGL)。自研薄后端(M2 后逐平台替换)是既定退出路径,渲染器只依赖一个窄接口 |
| D3 | 文本层 | **先借 SkiaSharp**(SKFont 度量 + 光栅化到自管图集),M2 换 HarfBuzzSharp shaping,远期自研字形光栅化(SDF)。度量/图集/绘制 API 从第一天就是自己的,Skia 只在图集生产侧,不进渲染热路径 |

### Veldrid 现状说明

Veldrid 作者自 2023-02 起停止公开发布更新,NuGet 上 4.9.0 为最后公开稳定版。对本项目影响可控:API 已冻结稳定、三后端齐备、本项目本就计划自研后端替换。所有 M1 用到的 API 已于 2026-10-25 在本机(Veldrid 4.9.0 + SkiaSharp 4.153.1,.NET SDK 10.0.401,win-x64/D3D11)真实编译与运行验证,包括离屏渲染像素读回。

## 3. 派生决策(ADR)

- **D4 运行时**:net10.0(SDK 10.0.401)。开发期默认 JIT(支持热重载),发布用 Native AOT。M1 验收平台 Windows(本机);macOS/Linux 构建通道在 M2 通过 CI 建立。
- **D5 窗口层**:Veldrid.StartupUtilities(SDL2):`CreateWindowAndGraphicsDevice` 一站式创建窗口 + 设备。手写平台层(win32/AppKit/X11/Wayland)推迟到 M3,触发条件是 IME/系统集成需要。
- **D6 测试**:xUnit(`dotnet new xunit` 模板默认版本)。纯逻辑(几何/网格/细分/布局/图集分包/DSL)为普通单测;需要 GPU 的用 headless D3D11 设备 + 离屏渲染 + staging 读回断言像素,打 `[Trait("Category","Gpu")]`,CI 可 `--filter` 跳过,本机必须全绿。
- **D7 依赖纪律**:全链路 trimming/AOT 友好(`IsAotCompatible=true` 开分析器),禁止反射序列化、动态代码、XAML。依赖版本锁定:Veldrid 4.9.0、Veldrid.StartupUtilities 4.9.0、Veldrid.SPIRV 1.0.15、SkiaSharp 4.153.1。
- **D8 许可**:MIT。
- **D9 命名**:框架名 SharpUI,仓库名 sharp-ui。

## 4. 架构

### 4.1 分层

```
┌─────────────────────────────────────────────────┐
│ 应用层    App / 事件循环 / Counter 等 examples   │
├─────────────────────────────────────────────────┤
│ 编程模型  声明式元素树(核心) │ 即时模式 Ui(逃生舱,M2)│
├─────────────────────────────────────────────────┤
│ 交互      输入状态 / 命中测试 / 悬停 / 点击       │
├─────────────────────────────────────────────────┤
│ 布局      LayoutEngine(单向 flex 子集 → M2 全量)│
├─────────────────────────────────────────────────┤
│ 光栅化    MeshBuilder / Tessellator(圆角矩形)   │
│ 文本      TextAtlas(SkiaSharp 借用) / ShelfPacker│
├─────────────────────────────────────────────────┤
│ 渲染      Renderer(单 pipeline、单 draw call)   │
├─────────────────────────────────────────────────┤
│ 设备抽象  GraphicsContext(薄封装 Veldrid)       │
├─────────────────────────────────────────────────┤
│ 平台      Veldrid 4.9(D3D11/Metal/OpenGL)+ SDL2 │
└─────────────────────────────────────────────────┘
```

### 4.2 每帧流水线(M1,M2 增量优化)

1. `window.PumpEvents()` 取输入快照
2. 调用用户 render 闭包**重建整棵元素树**(egui 式;M2 加 keyed diff 复用)
3. `LayoutEngine.Measure` + `Arrange`(绝对坐标直存 `Element.Frame`)
4. `Interaction.Update`:命中测试 → 悬停/按压状态 → 派发点击
5. 元素树 `Draw` → 光栅化为网格(`MeshBuilder`)+ 图集 UV
6. 上传 VBO/IBO/Uniform(动态缓冲,容量翻倍增长)
7. 提交命令:Clear → SetPipeline → **一次** `DrawIndexed`
8. `SwapBuffers`(VSync)

### 4.3 渲染设计(egui/GPUI 同款哲学)

- **单 pipeline、单图集、单 draw call**:所有元素(含矩形)都是采样图集的四边形/三角形;纯色矩形采样图集里的 1×1 白像素。
- 顶点格式(20 字节):`Position float2 @0, TexCoord float2 @8, Color byte4_norm @16`(预乘 alpha)。
- 着色器用 GLSL 450 编写,经 Veldrid.SPIRV 编译为 SPIR-V 并交叉编译到 HLSL(D3D11)/ MSL(Metal)/ GLSL(OpenGL),一份源码三后端。
- 屏幕坐标(左上原点)→ 裁剪坐标由顶点着色器用 `u_screenSize` 统一变换。
- 混合:预乘 alpha,`BlendStateDescription.SingleOverrideBlend`。

### 4.4 M1 简化决策(均为 M2 优化项)

- 整树每帧重建,无 diff/patch、无 key、无状态缓存。
- 布局只有单向 flex(Column/Row):主轴 Start/Center/End、交叉轴 Start/Center/End/Stretch、Gap、Padding。
- 文本逐字符 advance 排版(无 kerning/shaping),Latin 承诺,CJK"尽力显示"。
- 逻辑像素 = 物理像素(无 DPI 缩放)。
- 单窗口,无剪裁区(元素不裁剪超出父级)。

## 5. 编程模型规格

### 5.1 声明式核心(M1 公开面)

```csharp
using SharpUI;
using static SharpUI.Ui;

var state = new CounterState();
App.Create("Counter", 480, 320, () => Column(
        Text("Counter").Size(28).Bold().Color(0xE8E8EC),
        Text(state.Count.ToString()).Size(48).Color(0x7AA2F7),
        Row(
            Button("−", () => state.Count--),
            Button("+", () => state.Count++)
        )
    ).Center().Gap(4).Padding(new Thickness(24)))
  .Run();
```

- `Element` 抽象基类:内联 `Style`(布局)、`Children`、`Frame`(绝对布局框)、`Measured`;叶子实现 `MeasureSelf(SizeF)`,全部实现 `Draw(in PaintContext)`。
- 工厂方法 `Ui.Column/Row/Text/Panel/Spacer/Button`;链式修饰符(`.Size()/.Bold()/.Color()/.Fill()/.Radius()/.Center()/.Gap()/.Padding()`,MyGO 风格);`string` 隐式转换为 `TextElement`。
- 状态归用户(M1 外部普通对象,每帧重建读取);失效 = 连续重绘(VSync 节流)。

### 5.2 即时模式逃生舱(M2 实现,接口预留)

```csharp
ui.Row(static ui =>
{
    ui.Text("FPS: ").Draw();
    if (ui.Button("Reset").Clicked()) Reset();
});
```

`ref struct Ui` 记录为元素树后走同一条布局/绘制管线;不引入第二套渲染路径。widget 状态用 id-keyed 存储(`State<T>`)。

### 5.3 后续(M3,可选):隐式上下文语法糖

`Div(() => { Text("hi"); Button("ok", Ok); })` 风格,`UiContext.Current` 压栈实现,挂架在声明式核心上。M1/M2 不做。

## 6. M1 模块与文件结构

```
sharp-ui/
├── src/SharpUI/SharpUI.csproj
│   ├── SharpUIInfo.cs        版本常量
│   ├── Geometry.cs           SizeF/PointF/RectF/Thickness
│   ├── Color32.cs
│   ├── Mesh.cs               UiVertex/MeshBuilder
│   ├── Tessellation.cs       圆角矩形细分
│   ├── Shaders.cs            GLSL 源(内嵌字符串)
│   ├── GraphicsContext.cs    Veldrid 设备/管线/图集纹理/读回
│   ├── Renderer.cs           帧上传与提交
│   ├── Layout.cs             Style/Axis/Align/LayoutEngine
│   ├── Element.cs            Element 基类/Spacer
│   ├── Elements/TextAtlas.cs SkiaSharp 度量+光栅化+图集
│   ├── Elements/ShelfPacker.cs
│   ├── Elements/PanelElement.cs
│   ├── Elements/TextElement.cs
│   ├── Elements/ButtonElement.cs
│   ├── Elements/Ui.cs        DSL 工厂/隐式转换
│   ├── Interaction.cs        InputState/命中测试
│   └── App.cs                窗口/事件循环
├── examples/Counter/Program.cs
├── tests/SharpUI.Tests/      各模块 *_tests.cs
└── docs/superpowers/         本规格书与计划
```

## 7. 里程碑与验收标准

### M1 基础(本计划,目标 6~8 周)

范围:架构 4.1 全层打通最小闭环 + Counter demo。**验收标准(全部满足)**:

1. Windows 本机 `dotnet run -c Release --project examples/Counter`:480×320 窗口,60fps VSync,显示标题/计数文本/两个按钮,点击增减计数,按钮有悬停/按压视觉反馈。
2. `dotnet test` 全绿;其中 Gpu 类测试(headless D3D11 离屏渲染像素断言)在本机全绿。
3. `dotnet publish examples/Counter -c Release -r win-x64 -p:PublishAot=true` 成功;单文件产物记录大小(预期 <40MB);双击冷启动可交互 <1s。
4. 性能基线测试(Release):Counter 场景(约 30 元素)稳态每帧托管分配 ≤64KB、单帧 CPU 耗时 <8ms(预期 <2ms)。
5. 全程无 `unsafe` 代码、`IsAotCompatible` 分析器零警告。

### M2 应用(目标 +3~4 个月;2026-10-25 起分五阶段执行)

完整 flexbox(wrap/basis/grow/shrink)、keyed diff 与状态(Entity/`State<T>`)、焦点与键盘、滚动容器与虚拟化列表、即时模式逃生舱、HarfBuzz shaping + 中文/emoji 承诺、DPI 缩放、macOS/Linux CI 三平台 AOT 发布、热重载工作流文档、文本 SDF 评估。

**阶段划分(依赖排序,每阶段独立可测):**

| 阶段 | 内容 | 前置 |
|---|---|---|
| M2a 内核 | ushort 守卫、Ui.Button、稳定 id + State\<T\>、点击身份 id 化、焦点与键盘(Tab/Enter/Space)、性能再基线 | M1 ✅ |
| M2b-i 交互与逃生舱 | 管线重排(上帧几何交互 + 点击账本,D13)、InputState id 化、InputMapper、StateStore.Set、state↔id 桥(D14)、UiScope 即时模式逃生舱、焦点硬化(Escape/空白失焦/失焦取消按压/激活负例)、同键碰撞调试检测 | M2a ✅ |
| M2b-ii 布局与呈现 | 全量 flexbox(wrap/basis/grow/shrink)、剪裁(scissor,多批次渲染)、DPI 缩放(Sdl2Window.ScaleFactor,已验证存在) | M2b-i |
| M2c 滚动列表 | 滚轮事件、滚动容器(id 状态)、虚拟化列表 | M2b-ii |
| M2d 文本升级 | HarfBuzzSharp shaping、字体回退链、中文/emoji 承诺测试、SDF 评估(文档) | M2a(可与 M2b 并行) |
| M2e 交付管道 | 三平台 CI AOT、trimmer 抑制、热重载工作流文档 | 需 GitHub remote |

**M2 架构裁定(2026-10-25):**
- D10 "keyed diff"在每帧重建架构下实现为**键解析 + 状态对账**:每帧树走查计算元素稳定 id(Key 优先、`类型名+兄弟索引` 回退),状态按键存取、帧末清扫失联键。不引入结构 diff——状态正确性不依赖实例复用(egui 同款模型,实测预算内)。
- D11(C# 语言约束,M2b 生效):`ref struct` 不能作泛型参数,规格 §5.2 逃生舱的闭包式 API `ui.Row(ui => {...})` 无法用 ref struct 实现;逃生舱用 sealed class + 闭包,接受小量每帧分配(预算余量 40 倍)。零分配目标由池化兜底,不阻塞 API 形态。
- D12 `Veldrid.Key` 出现在公开 API(KeyEventData)为接受的技术债;M3 自研后端/输入层时一并抽象。
- D13(M2b-i)交互移至上帧保留树("点击账本"):每帧先对上帧已排列树做命中/点击/焦点按压,再构建本帧树;`InputState` 携带 id(HoveredId/FocusedId/ClickedIds),元素视觉门按 ResolvedId 比较。新出现元素的交互有一帧延迟(egui 同款,16ms 不可感知);换来即时模式在构建期即可回答 `.Clicked()`。Interaction 的 `_leftHeld` 静态退役,改用 `InputSnapshot.IsMouseDown(MouseButton)`(M1 时误记为无此 API,2026-10-25 反射复核存在)。
- D14(M2b-i)逃生舱 `Ui.Immediate(rootKey, build)`:rootKey 即元素 Key,用户须保证唯一(同 Key 纪律);UiScope 为其创建的每个元素烤入与 IdResolver 回退公式一致的段 Key,使局部路径与全局 ResolvedId 天然对应;`.Clicked()/.Hovered()` 按 `/rootKey/局部路径` 后缀匹配账本;`ui.State<T>(key)` 走 `StateStore`,键名空间 `imm:rootKey:key`。UiScope 为 sealed class + 闭包(D11)。
- D15(M2b-i 附录 A 增补,均已反射验证):`Sdl2Window` 有 `FocusLost/FocusGained` 事件、`ScaleFactor : Vector2` 属性(M2b-ii DPI 直用,无需原生调用)、`Focused`;`InputSnapshot` 有 `IsMouseDown(MouseButton)`、`KeyCharPresses`(M3 文本输入);`new MouseEvent(MouseButton, bool)`、`new KeyEvent(Key, bool, ModifierKeys)` 位置构造可用(可测);`InputSnapshot` 无公共构造。

### M3 生产(目标 +6~12 个月)

IME 深水区(必要时替换窗口层)、无障碍(UIA/AX/AT-SPI)、动画、多窗口、系统对话框/托盘/菜单、自研薄后端逐平台替换 Veldrid、性能收紧(每帧分配 ≤8KB)。

## 8. 测试策略

- **纯逻辑单测(无 GPU)**:几何、颜色预乘、网格构建(顶点/索引计数与内容)、细分(精确顶点数、半径钳制)、布局(Frame 断言)、ShelfPacker(顺序分配/换行/满重置)、DSL(隐式转换、修饰符链)。
- **GPU 测试(trait `Gpu`)**:headless D3D11 设备创建、着色器 SPIR-V 交叉编译、pipeline 创建、离屏渲染 + staging 读回断言像素颜色(白四边形中心 = 白,角落 = 清屏色)、图集区域更新。
- **性能测试(trait `Gpu`)**:N 帧 build→layout→tessellate 的分配字节与耗时断言(§9 预算)。
- **手测清单**:每任务附;窗口行为(resize/vsync/关闭)人工确认。

## 9. 性能预算

| 指标 | M1 预算 | M2 目标 | 测量方法 |
|------|--------|--------|---------|
| 稳态每帧托管分配 | ≤64KB | ≤8KB(元素池化) | `GC.GetAllocatedBytesForCurrentThread` 前后差 ÷ 帧数 |
| 单帧 CPU(1080p,Release) | <8ms(预期 <2ms) | <2ms | `Stopwatch` 每帧均值 |
| 帧率 | 60fps(VSync) | 120fps 可选 | 手测 |

## 10. 风险登记

| # | 风险 | 等级 | 缓解 |
|---|------|------|------|
| R1 | Veldrid 冻结无更新 | 低 | API 已锁版本且全部本机验证;渲染器只碰窄接口,M3 自研后端替换 |
| R2 | Veldrid.SPIRV 传递依赖 Newtonsoft.Json 9.0.1(NU1903 高危通告,本机已复现) | 中 | 在 csproj 显式直接引用 Newtonsoft.Json 13.0.3 覆盖(计划 Task 5 内置) |
| R3 | SDL2 的 IME/文本输入能力弱 | 中 | M2 调研:SDL3(Silk.NET)/GLFW/原生平台层三选一;M3 手写平台层兜底 |
| R4 | SkiaSharp 原生库体积影响 AOT 产物与启动 | 低 | 记录基线;远期自研光栅化后消除 |
| R5 | GC 停顿破坏帧预算 | 中 | M1 预算纪律 + 每帧分配测量入 CI;M2 元素/List 池化 |
| R6 | 无 DPI 缩放在高 DPI 屏上界面过小 | 中 | M1 明确不支持并记录;M2 实现 `ScaleFactor` 管线 |

## 11. 术语表

- **元素树(Element tree)**:用户 render 闭包每帧构建的轻量控件描述树。
- **图集(Atlas)**:单张 1024×1024 RGBA 纹理,含白像素与已光栅化字形,所有绘制共享。
- **网格(Mesh)**:一帧全部三角形的顶点/索引集合,一次 draw call 提交。
- **逃生舱(Escape hatch)**:即时模式 API,供工具/调试场景绕过声明式样板。
- **Headless**:无窗口 D3D11 设备,用于 GPU 自动化测试与离屏像素读回。

## 附录 A:已验证的 Veldrid 4.9 / SkiaSharp 4 关键 API(本机编译+运行确认)

```
VeldridStartup.CreateWindowAndGraphicsDevice(WindowCreateInfo, GraphicsDeviceOptions, GraphicsBackend, out Sdl2Window, out GraphicsDevice)
VeldridStartup.GetPlatformDefaultBackend()
new GraphicsDeviceOptions { Debug, PreferStandardClipSpaceYDirection }      // 无 SyncToVBlank 成员;vsync 用下一行的 MainSwapchain.SyncToVerticalBlank(App.Create 已设 true)
gd.MainSwapchain.SyncToVerticalBlank = true
window.Resized += () => gd.ResizeMainWindow((uint)window.Width, (uint)window.Height)
window.PumpEvents() → InputSnapshot { KeyEvents, MouseEvents, MousePosition, WheelDelta }   // 无 CharEvents
MouseEvent { MouseButton, Down }; KeyEvent { Key, Down, ModifierKeys }
factory.CreateBuffer(new BufferDescription(size, BufferUsage.VertexBuffer|Dynamic))
cl.UpdateBuffer(vb, 0, byte[])  /  cl.UpdateBuffer(vb, 0, ReadOnlySpan<T>)   // 两种重载均可用
TextureDescription.Texture2D(w, h, mipLevels, arrayLayers, format, usage)
gd.UpdateTexture(tex, byte[], x,y,z, w,h,d, mip, layer)
factory.CreateFromSpirv(new ShaderDescription(stage, bytes, "main"), ...) → Shader[]
new VertexElementDescription(name, VertexElementSemantic, VertexElementFormat)
factory.CreateGraphicsPipeline(new GraphicsPipelineDescription { ... })      // 4.9 是 Graphics 前缀,无 PipelineDescription
factory.CreateFramebuffer(new FramebufferDescription(null, colorTex))
cl.CopyTexture(src, dst); gd.Map(tex, MapMode.Read|Write) → MappedResource; gd.Unmap(tex)
GraphicsDevice.CreateD3D11(options)                                          // headless
SkiaSharp:SKTypeface.FromFamilyName / SKFont / SKPaint / SKBitmap(SKImageInfo) / SKCanvas(SKBitmap)
  font.MeasureText(s, out SKRect, paint); canvas.DrawText(s, x, y, font, paint);
  bmp.GetPixelSpan(); bmp.PeekPixels()
```

> 与新版 Veldrid 文档记忆的差异(易踩坑):4.9 无 `GraphicsDeviceBuilder`、无 `PipelineDescription`(是 `GraphicsPipelineDescription`)、`Map` 无 out 参数、`CreateFromSpirv` 返回数组、顶点元素构造为 `(name, semantic, format)`、vsync 挂在 Swapchain 上(已在 App.Create 设置)。
