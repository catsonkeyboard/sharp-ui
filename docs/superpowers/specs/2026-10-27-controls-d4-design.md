# D4 控件库扩展 + 性能债批次 设计

日期:2026-10-27 | 状态:已批准(控件范围四组全选分两期;状态绑定回调式;Slider 全功能版;性能债并入) | 前置:窗口层里程碑收官(d1ef96a,299/299)

## 目标

第一期(D4,本规格):Checkbox/Radio + Slider 全功能版 + 拖拽捕获原语 + 性能债批次(D3D11 部分上传/GL HiDPI drawable/多窗多线程评估)+ IME 候选序前置修复。
第二期(D5,后续):浮层机制(Dialog/Tooltip)→ Menu/Select(依赖 D4 的浮层/捕获底座,另立规格)。

## 已批准决策

| # | 决策 | 内容 |
|---|---|---|
| C-1 | 控件范围 | 四组全做;本规格=D4(Checkbox/Radio/Slider);Dialog/Tooltip/Menu/Select=D5 |
| C-2 | 状态绑定 | **回调式**(与 ButtonElement 同构):状态存应用侧对象,每帧重建元素树;不做 StateStore 内置绑定 |
| C-3 | Slider 规格 | 全功能版:横向单滑块+双 thumb 区间(range)+纵向;拖拽/轨道点击/键盘/Step/禁用 |
| C-4 | 性能债并入 | D3D11 部分上传、GL drawable、多窗/多线程评估落码,随 D4 交付并复测基线 |
| C-5 | 前置修复 | 窗口层终审 I-1:SdlTextInput.EnsureBound 候选序与 LiveModule 同构(动 IME 链须回归复验) |

## 架构

```
src/SharpUI/Elements/
 ├─ CheckboxElement.cs   方框+对勾(quad 线段);三态视觉(悬停/按下/禁用);空格/回车切换
 ├─ RadioElement.cs      圆圈+选中点;Focus 组导航(方向键组内移动;单选互斥在应用回调)
 ├─ SliderElement.cs     轨道+thumb(1~2 个);横向/纵向;连续/Step;捕获拖拽
 └─ Ui.cs                DSL 入口:Checkbox/Radio/Slider(+链式 .Step/.Vertical/.Range)
src/SharpUI/Interaction.cs   拖拽捕获原语:按下元素捕获后续 MouseMotion(出界仍收),松开释放
src/SharpUI/Backends/D3D11Context.cs  DYNAMIC 缓冲 + Map/MapWriteDiscard 部分上传
src/SharpUI/Backends/GLContext.cs     drawable 尺寸查询(HiDPI 正确性)
src/SharpUI/Text/SdlTextInput.cs      EnsureBound 候选序同构化(C-5)
```

## 控件语义(回调式,C-2)

- **CheckboxElement**:`Checkbox(string label, bool isChecked, Action<bool>? onChanged)`;`Enabled` 属性(bool);键盘:聚焦时空格/回车切换;视觉:方框 14×14 + 对勾两段 quad,状态色沿用 Button 色板(0x0C63E4/0x3B82F6/0x1D4ED8 + 禁用灰);IsHitTarget/Focusable。
- **RadioElement**:`Radio(string label, string groupName, bool isSelected, Action? onSelected)`;groupName 仅用于 Focus 导航分组(方向键组内循环)与未来样式钩子;单选互斥=应用回调职责(框架不藏状态,C-2);圆圈+内点。
- **SliderElement**:`Slider(float min, float max, float value, Action<float> onChanged)`;`.Step(float)`(默认 0=连续)、`.Vertical()`、`.Range()`(第二 thumb,minValue/maxValue 双回调);值钳制 [min,max],Step 吸附(Math.Round(v/step)*step);回调仅在实际变化时触发;键盘:左右(纵向则上下)±step 或 ±(max-min)/20(连续)、Home/End;拖拽:thumb 捕获(Interaction 原语),轨道点击跳转(含 thumb 就位)。
- **拖拽捕获原语**(Interaction):`CapturedId`(静态会话态)——按下命中可捕获元素时记录,后续 MouseMotion/Up 路由到捕获元素(Interaction.Update 现有命中路径加捕获分支);HoveredId 在捕获期间锁定捕获者。语义注释:一次仅一个捕获;新按下覆盖旧捕获。

## 性能债(C-4)

1. **D3D11 部分上传**:顶点/索引缓冲改 DYNAMIC(CpuAccessWrite)+ 帧首 Map(MapWriteDiscard)→ 拷当帧数据 → Unmap;消除按容量整块 UpdateSubresource。**约束**:保持 Draw 分段重定基/原生偏移语义与接口契约(两后端各自最优已许可);金样本必须复跑(渲染正确性护栏)。
2. **GL drawable**:GLContext 建 make current 后查 SDL_GL_GetDrawableSize,TargetWidth/Height 用 drawable(逻辑尺寸仍从窗口);视口/scissor/读回全部随 drawable。HiDPI 屏 GL 渲染分辨率正确性修复。
3. **多窗/多线程评估**:GL EnsureLoaded 加锁评估(单线程泵下结论入档)、OwnerModule 缓存失效策略(多窗关闭重开场景)、SdlPump 无归属鼠标精确归属——可行的落码,评估后不落码的写明结论入 spec。

## 前置修复(C-5)

`SdlTextInput.EnsureBound` 候选序改与 LiveModule 同构(基目录优先——建窗属主;或直接复用 SdlNative.Candidates),重写承载已证伪假设的注释;**IME 回归复验**(SHARPUI_TEST_IME_OPEN=1 + keybd_event "nihao" → 组合串+候选窗位置+空格提交读回,流程同窗口层 T4)。

## 测试策略

- 合成事件单测(InjectTest 模式):Checkbox 切换/禁用/键盘、Radio 焦点分组导航、Slider 拖拽捕获出界续拖/轨道跳转/Step 吸附边界/Vertical/Range 双 thumb/钳制/回调仅变化触发。
- 性能:PerfTests 复测(预期 ≤5490B 进一步下降或持平);金样本 6/6 复跑(D3D11 上传路径变更护栏);双后端 harness 保持绿。
- IME 回归(C-5 硬门槛,桌面解锁)。
- 验收:AOT ≤6MB 级实测记录;Counter demo 控件段(Checkbox×2/Radio 组/Slider 横+纵+Range);裁定#44(控件库一期)/#45(性能债批次收账)。

## 范围外(D5+)

Dialog/Tooltip 浮层机制、Menu/Select;触屏/手柄;RTL 布局;控件主题系统(色板沿用 Button 硬编码,主题化另议)。

## 实施纪要(Task 4,2026-10-27 落档)

### C-4.1 D3D11 部分上传(取舍)

vb/ib 改 `DYNAMIC+CpuAccessWrite`(Bind 不变;DYNAMIC 创建时 pInitialData=NULL,初始化语义=首次
Draw 的 Map 写入)。Draw 路径:**每绘制** `Map(WRITE_DISCARD)` → kernel32 RtlMoveMemory 只拷当帧
实际字节(顶点 [0..vertexCount) 前缀直拷;索引源分段连续、直写缓冲头部即完成重定基,单次块拷、
scratch 数组整体移除)→ `Unmap` → IA 绑定 → `DrawIndexed(indexCount, 0, 0)`。

- **Map 时机偏离计划稿**:计划设想"每帧每缓冲恰一次 Map、分段复用同 Map";但映射中的缓冲不得被
  管线消费(首次绘制后缓冲已绑定 IA 读入),跨绘制驻留映射再绘制属未定义 —— 故取 ImGui 批次同款
  "每绘制 Map→写→Unmap→绘制"。单区段帧(常态)即每帧恰一次;多区段帧每区段一次,仍只写实际字节。
  WRITE_DISCARD 改名由运行时管理(改名槽回收复用),映射+IA 在途绑定合法(D3D11 运行时专为此设
  改名机制;两区段读回测试 `Headless_DrawTwoSegments_StartIndexOffset` 实证)。未引入
  MapWriteNoOverwrite 环形进阶(非必需,留作未来多区段密集场景的演进项)。
- **分段重定基语义保持**(拷到缓冲头部 + DrawIndexed 偏移 0):重定基每次拷贝量 O(indexCount),
  直写分段+偏移为 O(startIndex+indexCount)(末段近乎整帧重传),维持旧取舍;接口契约不变,
  金样本 6/6 与 D3D11ContextTests 8/8(含两区段)锁定。
- 旧"scratch 字节容量恒等于 ByteWidth"不变量与 20 字节圆整随整块上传一并移除(容量仅约束
  ByteWidth,×2 增长保留);块拷原语=kernel32 RtlMoveMemory(注意导出名非 CopyMemory,后者是
  头文件宏,EntryPointNotFound 实证)。

### C-4.2 GL drawable

`SdlNative.GetDrawableSize`(SDL_GL_GetDrawableSize,属主模块);GLContext 窗口路径构造与 Resize:
TargetWidth/Height 实取 drawable 像素(逻辑窗尺寸仅用于 ScaleFactor 语义,App 侧);视口/scissor/
读回全部随 Target(即随 drawable)。headless 参数即目标(语义由 GLContextTests 锁定,不变)。
Windows 下 drawable==窗口尺寸,本机 DPI=96 断言不漂移;HiDPI(macOS Retina/Wayland)渲染分辨率
正确性由此修复。

### C-4.3 多窗/多线程评估(三子项结论)

1. **GL EnsureLoaded 加锁:落码**(评估结论=加)。单线程泵下并发首调竞态不可达,但加锁为零成本
   防御(快路径无锁读,锁仅首调进入,双检防重复解析);委托字段先写后置 `_loaded`,x86-TSO 下
   读方经 `_loaded` 观察到的字段恒就绪。已落 `GL.EnsureLoaded`(lock + 双检)。
2. **OwnerModule 缓存失效:维持,注释落档**(评估结论=不加失效钩子)。关窗残留键留存至进程结束
   (SdlPump.Unregister 只清泵窗表,本字典不清),单键成本一 IntPtr、量级=历史窗数,非增长型。
   失效风险链="HWND 复用 且 新旧窗属主模块不同"——应用建窗恒走同一 SdlWindow 层,属主翻转不可达,
   残留键复用即命中正确属主;缓存命中复验会重引入多候选探测、缓存归零收益。若未来出现不同层/不同
   模块建窗路径,须先给失效策略再接入(注释已记)。
3. **SdlPump 无归属鼠标精确归属:维持"最近聚焦窗"近似**(评估结论=不落码,精确化留 M3e 后半)。
   无归属 MOUSEMOTION 仅三态:关闭瞬间队列尾部事件/混宿进程他方窗口/焦点簿记空窗;"最近聚焦窗"
   传播在 (a)(c) 即正确归属,(b) 下目标窗未聚焦不消费点击(误漂移仅一处只读位置);键盘/文本/
   按钮/滚轮仍严格按 windowID 路由、不传播(归属精确性不受影响)。精确归属需 SDK 侧
   FOCUS/MOUSE_FOCUS flags 查询判真实光标窗,待 M3e 后半评估。

## 实施纪要(Task 5,2026-10-27 收官)

- **demo 控件段收窄(偏离上文"验收"行,收官指令裁定)**:验收行原列 "Checkbox×2/Radio 组/
  Slider 横+纵+Range 各一";实际交付紧凑一段 Row——`Checkbox("check") + Radio("a"/"b" 同组) +
  Slider(0,100,50).Step(10) + 值回显文本`,状态存 CounterState(回调式 C-2)。收窄理由:收官
  demo 以紧凑、初始不引入滚动为先;横/纵/Range/键盘等全功能面已由 ControlsTests/SliderTests
  逐项单测锁定,demo 无需重复铺陈。主 spec 裁定#44/#45 记收官;承证(headless 读回四帧像素断言
  + PNG 目视,桌面锁定降级)见 artifacts/t5-demo/ 与 task-5-report.md。
