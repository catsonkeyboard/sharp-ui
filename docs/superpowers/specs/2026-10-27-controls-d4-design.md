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
