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
| D3 | 文本层 | **先借 SkiaSharp**(SKFont 度量 + 光栅化到自管图集),M2 换 HarfBuzzSharp shaping,远期自研字形光栅化(SDF)。度量/图集/绘制 API 从第一天就是自己的,Skia 只在图集生产侧,不进渲染热路径。**(2026-10-26 收官 ✅:FreeType/HarfBuzz 直连全自研替换完成,SkiaSharp/SkiaSharp.HarfBuzz 移除,AOT 部署面 19.9→6.95MB,裁定#42;SDF 为远期项不随收官)** |

### Veldrid 现状说明

Veldrid 作者自 2023-02 起停止公开发布更新,NuGet 上 4.9.0 为最后公开稳定版。对本项目影响可控:API 已冻结稳定、三后端齐备、本项目本就计划自研后端替换。所有 M1 用到的 API 已于 2026-10-25 在本机(Veldrid 4.9.0 + SkiaSharp 4.153.1,.NET SDK 10.0.401,win-x64/D3D11)真实编译与运行验证,包括离屏渲染像素读回。

## 3. 派生决策(ADR)

- **D4 运行时**:net10.0(SDK 10.0.401)。开发期默认 JIT(支持热重载),发布用 Native AOT。M1 验收平台 Windows(本机);macOS/Linux 构建通道在 M2 通过 CI 建立。
- **D5 窗口层**:Veldrid.StartupUtilities(SDL2):`CreateWindowAndGraphicsDevice` 一站式创建窗口 + 设备。手写平台层(win32/AppKit/X11/Wayland)推迟到 M3,触发条件是 IME/系统集成需要。**(2026-10-26 收官 ✅:窗口/输入层全自研替换完成——自有 SDL 声明层[SdlEvents/Key/MouseButton/ModifierKeys/InputSnapshot/SdlWindow/SdlPump 全局泵],Veldrid.SDL2 + Newtonsoft.Json 移除,SDL2.dll 转 vendored 原生库,裁定#43;macOS/Linux 窗口路径仍待环境另立计划)**
- **D6 测试**:xUnit(`dotnet new xunit` 模板默认版本)。纯逻辑(几何/网格/细分/布局/图集分包/DSL)为普通单测;需要 GPU 的用 headless D3D11 设备 + 离屏渲染 + staging 读回断言像素,打 `[Trait("Category","Gpu")]`,CI 可 `--filter` 跳过,本机必须全绿。
- **D7 依赖纪律**:全链路 trimming/AOT 友好(`IsAotCompatible=true` 开分析器),禁止反射序列化、动态代码、XAML。依赖版本锁定(2026-10-26 窗口层收官后现状):**NuGet 包依赖清零**(Veldrid 图形系 M3d-ii 移除/裁定#40、SkiaSharp 系 D3 收官移除/裁定#42、Veldrid.SDL2 + Newtonsoft.Json 钉版窗口层收官移除/裁定#43),原生 vendor 仅 SDL2 2.30.8 + FreeType 2.13.3 + HarfBuzz 三 C 库(provenance: native/VERSIONS.md 与 native/SDL2/VERSIONS.md)。
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
│ 文本      TextAtlas(FT/HB 直连) / ShelfPacker    │
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
- 撞名注意:`SharpUI.Text` 命名空间(D3 文本层)与 `Ui.Text()` DSL 入口撞名——`using SharpUI;` + `using static SharpUI.Ui;` 组合下 `Text(` 裸调用 CS0118,消费者须写 `Ui.Text(...)` 限定(2026-10-26 裁定#42 附注)。
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
- D14(M2b-i)逃生舱 `Ui.Immediate(rootKey, build)`:rootKey 即元素 Key,用户须保证唯一(同 Key 纪律);UiScope 为其创建的每个元素烤入与 IdResolver 回退公式一致的段 Key,使局部路径与全局 ResolvedId 天然对应;`.Clicked()/.Hovered()` 按 `/rootKey/局部路径` 后缀匹配账本;`ui.State<T>(key)` 读 / `ui.SetState(key, value)` 写(裁定#19:C# 不允许仅返回类型差异的重载)走 `StateStore`,键名空间 `imm:rootKey:key`。UiScope 为 sealed class + 闭包(D11)。
- D15(M2b-i 附录 A 增补,均已反射验证):`Sdl2Window` 有 `FocusLost/FocusGained` 事件、`ScaleFactor : Vector2` 属性(M2b-ii DPI 直用,无需原生调用)、`Focused`;`InputSnapshot` 有 `IsMouseDown(MouseButton)`、`KeyCharPresses`(M3 文本输入);`new MouseEvent(MouseButton, bool)`、`new KeyEvent(Key, bool, ModifierKeys)` 位置构造可用(可测);`InputSnapshot` 无公共构造。
- D16(M2b-ii flexbox):`Style` 增 `Wrap(NoWrap/Wrap)/FlexGrow/FlexShrink/FlexBasis(float?);FlexShrink 默认 0(M1"叶子不收缩"语义保持,现有测试锁定);grow/shrink 为子项属性由父行内加权分配(basis=FlexBasis??内容主尺寸);wrap 行沿交叉轴堆叠,行间距复用 Gap;Measure 用 available 主轴,Arrange 用实际帧内主轴(CSS 同款 measure/arrange 分歧可接受)。已验证 API:`CommandList.SetScissorRect(uint bufferIndex,uint x,uint y,uint w,uint h)`、`DrawIndexed(uint indexCount,uint instanceCount,uint indexStart,int vertexOffset,uint instanceStart)`。
- D17(M2b-ii 剪裁):模型 = **帧相交剪裁**(子剪裁 = 自身 Frame ∩ 父剪裁),`MeshBuilder.SetClip(RectF?)` 记录 ClipRun 区段(null=全屏哨兵 MaxValue),Renderer 每区段一次 SetScissorRect+DrawIndexed(单 draw call 退化为少批次,UI 帧内区段数少)。**HitTest 天然与该模型一致**(逐层 Frame.Contains 已等价于帧相交可见性),无需改动;PanelElement 进入时压剪裁、退出时还原(Mesh.CurrentClip 携带,PaintContext 不变形)。
- D18(M2b-ii DPI):`App.ScaleFactor`(读 `Sdl2Window.ScaleFactor.X`,钳制 0.5~4);几何**零着色器改动**(uniform 携带逻辑尺寸时 NDC 分数与物理/逻辑无关);scissor 物理像素 = 剪裁×scale;鼠标 ÷scale 入 RawInput;TextAtlas 以物理字号(⌈字号×scale⌉)光栅、四边形按逻辑尺寸回除(scale≥1 整数倍 Point 采样精确;小数倍轻微锯齿由 M2d SDF/Linear 重估解决);`Renderer.Submit(mesh, 逻辑尺寸, fb, scale=1)`/`InputMapper.Map(snapshot, scale=1)` 默认参保既有测试零改动。
- D19(M2c 滚动):滚轮通道 `RawInput.WheelDelta`/`InputState.WheelDelta`(InputSnapshot.WheelDelta 已验证存在);**滚轮分发在构建前、上帧保留树上**(App.Run 内 Interaction.Update 之后):深度优先递归找最深包含鼠标的 ScrollElement(子先父后,最深者赢,天然支持嵌套);偏移存 `StateStore["scroll:"+ResolvedId]`,钳 [0, content−viewport];步长 WheelDelta×40 逻辑像素;滚动条可视化与按压捕获为 M3。ScrollElement 测量按内容(Column 同款),父容器须给定高度(Basis/Stretch)。
- D20(M2c 虚拟化):`VirtualListElement(key, count, itemHeight, render)`:固定行高,可见窗 ±1 过扫;行元素 Key=索引字符串(跨窗移稳定);Arrange 时按窗重建子项并对其子树重跑 IdResolver.Assign;ContentMain=count×itemHeight 供滚轮分发;继承 ScrollElement。
- D21(M2d 文本):SkiaSharp 4 画布已无 glyph 级绘制(探针证实,DrawGlyphs/DrawTextBlob 均移除;首次探针"编译通过"系 tail 截断误读,已纠正)。设计:字形位图仍走既有 per-char 图集缓存;**位置来自 shaping**——按字体回退链(默认 ["Microsoft YaHei","Segoe UI Emoji"],ContainsGlyph 判定,已验证)分 run,每 run `SKShaper.Shape(text,x,y,font)`(四参重载已验证)得 Points/Width,kerning/合成位置即正确;Measure=Σrun.Width;光栅化用非废弃重载 `DrawText(string,float,float,SKTextAlign.Left,font,paint)`(M1 的 CS0618 债就此清偿)。彩色 emoji 由 Skia 位图化自然支持。SDF 仅评估出文档。
- D22(M3a 文本输入):字符通道 `RawInput.Chars`(char[]? 默认 null,源自 KeyCharPresses);`KeyEventData` 增尾参 `bool Ctrl = false`(ModifierKeys.Control 已验证)。**键路由**:App 在构建前用上帧保留树按 `focus.FocusedId` 定位聚焦元素(`TreeWalk.Find(root,id)` 新 helper),若为 TextFieldElement 则 `Feed(keys, chars)`——编辑语义实现为**纯函数静态类 `TextEditing`**(TextState{Text,Cursor,Anchor} → 新 TextState,完全可测),TextField 只是状态绑定壳(状态键 "tf:"+id)。语义:字符追加/替换选区、Backspace/Delete、Left/Right/Home/End(±Shift 选区)、Ctrl+A 全选;复制/粘贴/剪切为 M3c(剪贴板依赖);组合窗口定位 M3c 评估。视觉:光标 1px 竖线、选区半透明蓝、聚焦描边 0x60A5FA;闪烁属 M3b 动画。shaping LRU:`(text,size,bold)→(逻辑宽,分 run 位置快照)` 缓存,Measure/DrawRun 共用,超 512 条全清(粗逐出,注释在案)。附注(#24 更正):HarfBuzz Clusters 为 UTF-8 字节偏移而非字符索引,DrawRun 需 ClusterToCharIndex 换算(ASCII 巧合掩盖,中文路径暴露);SKFont.ContainsGlyph 形参为 int。

### M3 生产(目标 +6~12 个月;2026-10-25 起分五阶段)

| 阶段 | 内容 | 前置 |
|---|---|---|
| M3a 文本输入 | 字符通道、TextField(光标/选区/编辑语义)、shaping LRU 缓存 | M2 ✅ |
| M3b 动画与性能 | 动画系统、池化与 LRU 收紧至 ≤8KB/帧(规格 §9 M2 目标) | M3a |
| M3c 多窗口与集成 | 多窗口、剪贴板(原生 P/Invoke,已探明 Sdl2Window 无封装)、对话框/托盘/菜单 | M3a |
| M3d 自研薄后端 ✅(D3D11 2026-10-26;GL 2026-10-26 见 M3e 前半;Metal 待环境) | D3D11 → Metal → OpenGL 逐平台替换 Veldrid(渲染器窄接口既备) | M2 ✅ 可并行 |
| M3e 无障碍 + 后端收官 | 前半=GL 后端 ✅(Windows 实证,2026-10-26,`SHARPUI_BACKEND`,裁定#41;Metal/CI 待环境另立计划);后半=UIA/AX/AT-SPI 逐步接入 | M3b |

IME 定位:SDL2 `KeyCharPresses`(已验证 `IReadOnlyList<char>`)承载 IME 提交文本,Windows 下中文输入可过;组合窗口定位属"深水区"(需原生平台层),M3c 评估。
- D23(M3b 动画):时间源 `Anim.TimeSource : Func<double>`(可注入,默认 Stopwatch 秒);`Anim.Get(id, target, duration=0.2s, easing) → float`(目标变化时从当前值重启补间;内部字典无逐出,上限 4096 全清——与 shaped 缓存同款粗策略);`Anim.Blink(period=1.06s, duty=0.5) → bool` 无状态(墙钟取模)。**动画态不入 StateStore**(Sweep 单帧未触即清,冲突);TextField 光标闪烁与按钮悬停过渡为首批消费者。性能:布局数组按**深度 ThreadStatic 池化**(ComputeLines 进出深度计数,数组按深度增长复用——嵌套递归安全);TextField 静态 Empty/缓存键;若仍超 8KB 则元素自由列表(条件步骤)。
- D24(M3c 多窗口):Interaction 静态(`_pressedOn/_clicks/_queued`)会话化为 `InputSession` 实例(每窗口一个;`Interaction.Update(session, root, raw, focus)`);App 重构为 `App`(设备与窗口表)+ `AppWindow`(window/context/atlas/focus/lastTree/session);**服务定位静态改为"当前窗口"语义**(每窗口渲染前置 App.Atlas/UiContext.CurrentInput;StateStore 全局共享);`App.OpenWindow(title,w,h,render) → AppWindow`;主窗关闭即退出,副窗关闭仅移除。InputSnapshot 本就按窗口泵送,天然隔离。
- D25(M3c 剪贴板与保留):剪贴板走 SDL2 原生导出 `SDL_SetClipboardText/SDL_GetClipboardText`(P/Invoke 已探针,Windows 本机;跨平台库名 M3e CI 处理,同 D18 先例);Ctrl+C/X/V 在 `TextFieldElement.Feed` 于 TextEditing.Apply 前处理(纯函数 `TextEditing.Paste/CutResult` 可测);**StateStore 宽限清扫**:条目记 lastTouched 帧,App 每帧 Tick,未触碰超 300 帧(≈5s)才清——虚拟列表行滚出后文本保留(修终审 I-1)。
- D26(M3c 范围调整):原生对话框/托盘/菜单移 M3e——每项均为逐平台数周级原生工程(comdlg32/IFileDialog/NotifyIcon/Win32 菜单),与"多窗口+剪贴板"无共享基建;本轮以 IME 组合窗定位评估文档收口。
- D27(M3d 分期):M3d-i = 渲染后端窄接口抽取(现 GraphicsContext 公开面收为 `IRenderContext`,Renderer 仅依赖之)+ **D3D11 直连探针**(P/Invoke d3d11/dxgi/d3dcompiler_47,均已探针存在;交付"自建设备+交换链+清屏呈现"最小闭环证明互操作模式);M3d-ii = 完整替换 Veldrid(管线/缓冲/图集/剪裁批次),依赖逐平台硬件,M3e CI 后执行;Metal/OpenGL 仅在接口证明后排期。**(2026-10-26 收官:M3d-i/ii ✅ —— GraphicsContext 退役为 D3D11Context + IRenderContext v2,Veldrid/Veldrid.SPIRV/Veldrid.StartupUtilities 移除(Veldrid.SDL2 保留),"M3e CI 后执行"提前为本机真实设备全量 GPU 测试;裁定#40。)**
- 裁定#40(M3d-ii 定稿,2026-10-26):D3D11 直连后端定稿——COM 槽位纪律内化于 `D3D11Native` 声明层(逐槽标注本机 SDK d3d11.h/dxgi.h 声明序推导,锚点校验和:设备 CreateRTV=9、上下文 DrawIndexed=12/OM=33/ClearRTV=50、SwapChain GetBuffer=9,spike 60 帧闭环实证);SdlNative 双 SDL2 模块教训二次实证(裁定#38 复燃):模块落点不可假设,活模块 WMInfo 多候选探测是正确性依赖、不可"简化"掉;Veldrid.SDL2 保留为窗口/输入层(范围界定见 D27)。附带收账:AOT 裁定#35 关闭(Vortice 随图形包移除,见 docs/notes/m1-aot.md),性能基线持平(5948B/0.05ms,docs/notes/perf-baselines.md)。
- 接口耦合债(M3d-ii 终审背记,2026-10-26):TextAtlas 与 AppWindow 仍以具体类型 D3D11Context 声明(消费面实为 IRenderContext 成员 UpdateAtlasRegion/Present/Resize/Dispose),M3e 多后端落地前须接口化;决策点:AtlasSize 常量归属(接口成员 vs 常量类)。**(已清偿:M3e T1,2026-10-26 —— TextAtlas/AppWindow 均改持 IRenderContext,AtlasSize 落接口实例属性并携"构造期显式全零上传"契约;Renderer 自 M3d-ii 起仅依赖接口。)**
- 裁定#41(M3e 前半定稿,2026-10-26):GL 后端路线定稿(OpenGL 3.3 core,SDL 原生 GL 路线)—— **SDL GL 属主模块**纪律(裁定#38 延伸三次实证:窗口参 GL 调用严格 OwnerModule;建窗前 EnsureVideoInit/SetGlAttribute 全候选,属主副本未收属性即静默回落兼容上下文且仍过版本下限断言,T3 实证);**Y 差异三处收敛**(顶点着色器不翻 Y,与 HLSL 同式 `1.0 - y/h*2`;glScissor Y 翻转 `fbH-y-h`;glReadPixels 读回行序翻转——两段方向锁定像素测试防双翻转);**DrawElements 原生索引字节偏移直绘**(glBufferSubData 显式长度上传,无 D3D11 整块 scratch 重定基之需,IRenderContext.Draw 契约注两可语义);**BeginFrame scissor 复位对齐 D3D11 清屏语义**(glClear 受 scissor test 作用,帧末残留剪裁会把清屏裁成子矩形拖影)。后端选择:`SHARPUI_BACKEND`(`d3d11`/`gl`;Windows 默认 d3d11,非 Windows 默认 gl——后者仅编译保证,本机无环境)。冒烟:Counter 双后端各 7s 存活 + GL 进程 opengl32.dll 加载/默认进程不加载(路由实证)+ 可见窗 90 帧 harness 读回像素断言。附带收账:测试主机禁并行(assembly `CollectionBehavior`,GPU 类并行 0xC0000005 崩主机 T4 实证,IdResolver 静态同因);GL perf 可比测持平(5944B/0.04ms,docs/notes/perf-baselines.md)。Metal/CI 待 Mac 环境/GitHub remote 另立计划(preflight 裁定)。
- 裁定#42(D3 文本层定稿,2026-10-26):文本层 FreeType/HarfBuzz 直连全自研定稿(设计 spec docs/superpowers/specs/2026-10-26-text-layer-design.md;消费面 100% 收敛于 TextAtlas,公共 API 不变;vendor freetype.dll/harfbuzz.dll 来源/sha256 见 native/VERSIONS.md,FTL/BSD+MIT LICENSE 随附)。**三项规格级裁定(T4 评审核验成立,设计 spec 勘误节同步)**:①金样本阈值原定 均值<2%/max≤96 经**不可达证明**调整——旧栈 Windows 字体宿主=DirectWrite(非 FreeType),两光栅器曲线 AA 逐像素不同,位级平价物理不可达;度量等值的真机制=两引擎对同一 TrueType hinting 字节码执行出相同网格拟合整值;新阈值=均值≤6%×255 + max≤240(仅值域护栏)+ 墨迹包围盒 ±1px 闸 + 精确数值平价单测(advance/行高逐值)。②旧栈 bold-CJK 推进 bug(雅黑 bold ≈13/17px/字,应 20)**隔离不复刻**,bold×1 语料特例容差 12px。③T-3 行度量修订:FT_SizeMetrics(hhea)整体下移基线 ~4px@20px 致金样本溢阈,改 FT hinted Á/g 墨迹并集(InkLineMetrics),12/18/20/24px+bold 五尺寸点与旧栈逐值相等零漂移(原 ≤0.5px 重定基预案未启用)。**T5 收官收账**:SkiaSharp/SkiaSharp.HarfBuzz 包引用移除(`grep -rni skia src/` 零命中);金样本 6/6 绿(永久回归护栏,不依赖 Skia 存续);全量 226/226;AOT 产物实测 19,931,800→**6,945,792 B ≈ 6.95MB**(≤8MB 验收达成:Counter.exe 3.48 + SDL2 1.70 + freetype 0.69 + harfbuzz 1.08 MB,Skia 系原生库自产物消失,明细 docs/notes/m1-aot.md);AOT 冒烟进程存活 10s 无未处理异常(桌面锁定降级标准,渲染由金样本承证);性能基线 5552B/0.05ms(文本栈替换后复测,收官日两次全量 5576/0.04 与 5552/0.05 抖动内,docs/notes/perf-baselines.md)。Newtonsoft.Json 钉版保留(R2:Veldrid.SDL2 链仍在,M3e 后半退出);风险 R4 关闭。
- 裁定#43(窗口层自研定稿收官,2026-10-26):窗口/输入层全自研定稿(设计 spec docs/superpowers/specs/2026-10-26-window-layer-design.md,实施计划 docs/superpowers/plans/2026-10-26-window-layer-sdl.md 四任务交付)——**①全局泵分发**(SdlPump 进程单泵,App.Run 帧首 PumpAll 一次,事件按 WindowId 路由至注册窗;无归属 MOUSEMOTION 传播最近聚焦窗[悬浮/拖拽断流防护],按键/文本/按钮/滚轮严格归属不传播;每窗 GetSnapshotAndReset 双缓冲轮换稳态零分配);**②枚举自有化**(Key[147 成员]/MouseButton/ModifierKeys 镜像生成,T1 镜像守卫测试随使命完成退役;KeyEventData 签名零形变,全 switch case 无漂移);**③SDL2 vendor 接管**(Veldrid.SDL2 4.9.0 与 Newtonsoft.Json 13.0.3 钉版同批移除,R2 链条根除;SDL2.dll 2.30.8 转 vendored 原生库 native/SDL2/,provenance/sha256 见 native/SDL2/VERSIONS.md)。**D1-D3 三借全还收官**:D2 图形层(裁定#40)/D3 文本层(裁定#42)/D5 窗口层(本裁定),NuGet 运行时包依赖清零,原生依赖仅 SDL2/FreeType/HarfBuzz 三 C 库。**行为改进注记**:ScaleFactor 从 Veldrid 硬编码 One 改为真实 DPI/96 夹紧[0.5,4](D18;鼠标/文本/剪裁换算基准随之真实化);窗口 Close 语义分化(仅置标记,销毁归 App 移除路径 Dispose——防原生窗与泵注册表泄漏)。**T4 收官验证**:双后端窗口 harness(App.CreateWindow 全路径[EnableImeUi→EnsureVideoInit→SHARPUI_BACKEND 路由→flags→上下文→Renderer/图集→接线],90 帧清屏+四边形,末帧不 Present 像素读回,字节序双后端断言[D3D11 BGRA/GL RGBA])+ 进程内 App.Create+Run(maxFrames:5) 全 App 路径冒烟;全量 299/299;IME 实测回归(keybd_event 真键盘管线:组合串+候选窗紧贴输入框下方偏右+空格提交「你好」上屏读回,Enter=raw 拼音为系统 IME 语义非框架链路,证据 artifacts/ime-test/t4-regression-v3);Counter 真实注入交互清单(点击计数/键盘输入+Backspace/滚轮滚动/拖窗 resize)全绿;滚轮符号 y 原样透传与 Veldrid 4.9.0 逐字平价(`WheelDelta += y` 无方向分支,实测上+1/下−1);AOT 部署面 6,157,824 B ≈ 5.87 MiB(Counter.exe 2,693,120 B ≈ 2.57 MiB),AOT 进程存活+目视渲染实证(docs/notes/m1-aot.md);性能基线 5490B/0.05ms(docs/notes/perf-baselines.md)。
- 裁定#44(控件库一期定稿收官,2026-10-27):Checkbox/Radio/Slider 三控件 + 拖拽捕获原语定稿(设计 spec docs/superpowers/specs/2026-10-27-controls-d4-design.md,实施计划 docs/superpowers/plans/2026-10-27-controls-d4.md 五任务交付)——**回调式状态绑定(C-2,与 ButtonElement 同构)**:`Ui.Checkbox(label, isChecked, onChanged)` / `Ui.Radio(label, groupName, isSelected, onSelected)` / `Ui.Slider(min, max, value, onChanged)`,状态存应用侧、控件不藏状态,回调仅值实际变化触发(Radio 互斥=应用回调职责,groupName 仅焦点导航分组);**Slider 全功能版(C-3)**:`.Step(float)` 吸附 / `.Vertical()`(上=大)/ `.Range(maxValue, onMaxChanged)` 双 thumb(活动 thumb StateStore 锁存),拖拽捕获出界续拖 + 轨道点击就位续拖,键盘 IKeySteerable(方向 ±step 或量程/20、Home/End);**拖拽捕获原语**:InputSession.CapturedId(按下命中 CapturesDrag 元素即捕获、悬停锁定、Up 释放、新 Down 覆盖;CancelPress 同步清捕获=失焦自愈;Checkbox/Radio 不捕获、Slider 捕获);键盘激活 IActivatable(Checkbox/Radio 聚焦+Space/Enter 切换/选中;Radio 方向键组内移焦、显式激活);三控件 Enabled 属性一致(灰视觉+拒激活+不入捕获),色板沿用 Button 硬编码。**前置修复(C-5)随 T1 落码**:`EnsureBound` 候选序与建窗属主同构(同源 `SdlNative.Candidates()`,裁定#38 终态;src/SharpUI/TextInput.cs,句柄制绑定不复述已证伪的 runtimes 优先序);IME 视觉端到端复验挂起至桌面解锁(trace 级实证过验收实质,复跑脚本 artifacts/ime-test/ime-regression-v3.ps1 在案)。**T5 收官收账**:Counter demo 控件段(Checkbox + Radio "a"/"b" 同组 + Slider(0,100,50).Step(10) + 值回显,紧凑一行 Row,状态存 CounterState);全量 325/325;AOT 部署面 **6,191,616 B ≈ 5.90 MiB**(Counter.exe 2,726,912,+33,792 B 控件面,≤8MB 持续达成,docs/notes/m1-aot.md);AOT 冒烟存活 10s Responding=True、CloseMainWindow 干净退出 ExitCode=0(桌面锁定降级标准);demo 控件段交互承证=headless D3D11 读回四帧像素断言(checkbox 勾选 accent / radio 互斥选中点迁移 / slider 轨道 70% 点击就位且 Step 吸附至 70)+ PNG 目视(artifacts/t5-demo/;锁屏截屏不可用,降级承证同裁定#42 先例)。
- 裁定#45(性能债批次收账,2026-10-27):C-4 三子项定稿(Task 4 交付,取舍全文 docs/superpowers/specs/2026-10-27-controls-d4-design.md "实施纪要(Task 4)")——**①D3D11 部分上传**:vb/ib `DYNAMIC(CpuAccessWrite)`,每绘制 `Map(WRITE_DISCARD)` → RtlMoveMemory 只拷当帧实际字节 → `Unmap` → DrawIndexed(ImGui 批次同款;计划稿"每帧每缓冲恰一次 Map"证伪偏离——IA 在途绑定的缓冲不得驻留映射被管线消费,WRITE_DISCARD 运行时改名使其合法,两区段读回测试实证);scratch 整块上传与 20 字节圆整移除,分段重定基语义保持(拷至缓冲头部+偏移 0);上传微基线(Renderer.Submit→Draw 真实路径,2000 帧 Release 两组复跑):8 区段 7.4-8.6ms→**~150us(≈49-55×)**、1 区段 352-970us→~20-22us、32 区段 14-17ms→~1.45ms,与 GL 同场量级拉齐(8 区段 156 vs 210us);块拷原语 kernel32 RtlMoveMemory(导出名非 CopyMemory——后者是头文件宏,EntryPointNotFound 实证)。**②GL drawable**:`SdlNative.GetDrawableSize`(SDL_GL_GetDrawableSize,属主模块),GLContext 窗口路径构造/Resize 实取 drawable 为 TargetWidth/Height(HiDPI 渲染分辨率修复;headless 参数即目标语义不变;本机 DPI=96 drawable==窗口尺寸,断言不漂移)。**③多窗/多线程评估**(可行落码/不可行结论入档双通道):GL `EnsureLoaded` 加锁落码(lock+双检,快路径无锁零成本;委托字段先写后置 `_loaded`);OwnerModule 维持+注释落档(关窗残留键非增长型,"HWND 复用且属主翻转"不可达);SdlPump 无归属鼠标维持"最近聚焦窗"近似,精确化留 M3e 后半(SDK FOCUS flags)。护栏复跑:金样本 6/6 持平(639 vs 630ms)、D3D11ContextTests 8/8(含两区段 startIndex>0)、双后端 WindowSmokeTests 4/4、PerfTests 5488→5490 B / 0.04 ms 持平(docs/notes/perf-baselines.md)。
- 裁定#46(浮层机制定稿 + 控件库二期收官,2026-10-27):PopupHost 浮层栈全链定稿(设计 spec docs/superpowers/specs/2026-10-27-controls-d5-design.md,实施计划四任务交付)——**①每窗浮层栈**:静态按窗服务定位,Push(一次性指派浮层树稳定根 id "/__popupN",双下划线前缀防与用户键碰撞)/Pop(盲弹,Esc 与外点专用——调用方保证目标即栈顶)/Remove(按 Popup 实例身份移除,叠栈下自关闭专用,幂等)/StackOf/TopOf;绘制序=声明树后栈底→栈顶逐层 Draw;命中=栈顶浮层独占(模态语义,帧首快照);外点=按压消费+可-dismiss Pop;Esc=PopTopNonTooltip(Tooltip 对 Esc 透明穿越)。**②焦点围栏栈**:FocusManager.PushScope(scopeId)/PopScopeOf(scopeId)(按身份出栈,下层浮层先亡不误弹上层围栏),Dialog/Menu 打开入圈(Tab/方向键/激活圈定)、关闭归还打开者焦点。**③自关闭按身份**(T2→T3 承重改造):Dialog.Close/Menu.Close/Tooltip 移除均走 Remove+PopScopeOf,三层叠栈(Dialog←Menu←Tooltip)互不误弹。**④四控件**:Dialog(ShowDialog App/AppWindow 双载扩展[浮层打开=命令式动作不入声明式工厂]/单实例重复抛 InvalidOperationException/IsDefault=Enter 缺省/IsCancel=Esc 等效/遮罩点击可配/Dialog 类浮层根每帧重排居中)、Tooltip(element.Tooltip(text) 链式载体标记 + TooltipService 每帧服务:悬停墙钟表 0.5s 门限[非 Anim.Get——首调即返回目标值,0.5s 门会瞬开]/开启后几何存活判定[浮层命中独占下 HoveredId 不可用]/孤儿愈合与计时剪枝)、Menu(Ui.ShowMenu(IntPtr)/window.ShowMenu 扩展经 FocusManager 窗注册表定位围栏;MenuItem/MenuSeparator 混排/禁用项跳导航跳激活;方向键环移+Enter 激活+Esc/外点关闭)、Select(Ui.Select(options, selectedIndex, onChanged) 按钮+Menu 复合:点击/聚焦+Enter/Space 开菜单[锚=自身 Frame 底缘,初始焦点=当前项即高亮],菜单未开时方向键直接换选 ±1 钳制不环移仅变化回调,菜单内选中回调+关菜单,Esc 不开菜单[开着只关];打开接线=Element.OwnerFocus 注册期回链[FocusManager.Register 每帧接线,App.Run 帧序下指针路径读上帧回链、键盘路径读当帧回链,同一 FocusManager 实例语义等价],未注册元素激活抛 InvalidOperationException;Enabled=灰视觉+拒激活/拒换选+退出焦点序,与 D4 三控件一致)。**T4 顺带两裁定**(T3 报告自审移交):多窗 DPI 度量偏差以 App.Run 内 `Atlas = w.Atlas` 前移一行收账(点击回调内开浮层即以本窗图集度量;单窗赋值相同仅时点提前零行为差;双 DPI 窗专用断言留环境——本机 DPI 恒 96 不可构造);菜单无翻转/钳制入档缓议(Push 时无屏幕矩形知识,锚贴屏底时菜单溢出;修法需按窗屏幕矩形注册表 + 翻转/夹紧策略,当前无近屏底菜单场景,Select 下拉同题同裁定,MenuExtensions.Show 注释已记)。**T4 收官收账**:Counter demo 浮层段(About 按钮→ShowDialog[OK=IsDefault+IsCancel] + Checkbox.Tooltip("提示文本") + Select 三项选项切换回显);全量 **377/377**(365+12 SelectTests:开/锚定/当前项高亮/指针与键盘选择/Esc 语义/直接换选钳制[帧循环镜像回调式重建]/禁用全路径/构造防御/接线诚实性/demo 段状态承证 + headless D3D11 像素读回[按钮形态/箭头/禁用色板]);AOT 部署面 **6,246,912 B ≈ 5.96 MiB**(Counter.exe 2,782,208,+55,296 B 浮层面,≤8MB 持续达成,docs/notes/m1-aot.md);AOT/JIT 冒烟存活 10s Responding=True CloseMainWindow 干净退出 ExitCode=0(桌面锁定降级标准);**IME 视觉端到端补验仍未跑(桌面锁定持续:LogonUI 驻留会话 1)——D5 合并条件:桌面解锁后补跑 artifacts/ime-test/ime-regression-v3.ps1 并回填账本,前于分支合并**(D4 裁定#44 同款挂起,流程约束非代码债)。

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
| R2 | 传递依赖 Newtonsoft.Json 9.0.1(NU1903 高危通告,本机已复现;原链 Veldrid.SPIRV 已随 M3d-ii 移除,现存活链=Veldrid.SDL2→NativeLibraryLoader,2026-10-26 移除-复发-恢复实证) | 中 | csproj 显式直接引用 Newtonsoft.Json 13.0.3 钉版浮动压制;退出条件=M3e 替换窗口层移除 Veldrid.SDL2 后删除 |
| R3 | SDL2 的 IME/文本输入能力弱 | 中 | M2 调研:SDL3(Silk.NET)/GLFW/原生平台层三选一;M3 手写平台层兜底 |
| R4 | SkiaSharp 原生库体积影响 AOT 产物与启动 | **已消除**(2026-10-26) | D3 收官:FT/HB 直连替换,Skia 系移除,AOT 部署面 19.9→6.95MB(docs/notes/m1-aot.md) |
| R5 | GC 停顿破坏帧预算 | 中 | M1 预算纪律 + 每帧分配测量入 CI;M2 元素/List 池化 |
| R6 | 无 DPI 缩放在高 DPI 屏上界面过小 | 中 | M1 明确不支持并记录;M2 实现 `ScaleFactor` 管线 |

## 11. 术语表

- **元素树(Element tree)**:用户 render 闭包每帧构建的轻量控件描述树。
- **图集(Atlas)**:单张 1024×1024 RGBA 纹理,含白像素与已光栅化字形,所有绘制共享。
- **网格(Mesh)**:一帧全部三角形的顶点/索引集合,一次 draw call 提交。
- **逃生舱(Escape hatch)**:即时模式 API,供工具/调试场景绕过声明式样板。
- **Headless**:无窗口 D3D11 设备,用于 GPU 自动化测试与离屏像素读回。

## 附录 A:已验证的 Veldrid 4.9 / SkiaSharp 4 关键 API(本机编译+运行确认;Veldrid 图形系/SkiaSharp 系分别于 2026-10-26 M3d-ii/D3 收官移除,本附录留档 M1 期验证史)

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
