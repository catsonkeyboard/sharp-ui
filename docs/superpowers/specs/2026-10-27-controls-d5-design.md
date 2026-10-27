# D5 控件库二期设计:浮层机制(Dialog/Tooltip)+ Menu/Select

日期:2026-10-27 | 状态:已批准(询问未应答,按推荐项默认:PopupHost 浮层栈 + 焦点围栏栈;用户可随时推翻) | 前置:D4 收官(d64edfc,325/325)

## 前置硬门

**IME 视觉端到端补验**(D4 挂起项):桌面解锁后跑 `artifacts/ime-test/ime-regression-v3.ps1`,结果回填 T1 账本——**完成前本里程碑不得触碰 IME 链路**(本里程碑正常不涉及 IME,此门约束的是分支合并顺序)。

## 目标

PopupHost 浮层栈统一底座 + 四控件:Dialog(模态)/Tooltip(悬停延时)/Menu(下拉菜单)/Select(下拉选择器)。D1-D3 三借全还后,控件面补齐到常用集合。

## 已批准决策(询问默认,可推翻)

| # | 决策 | 内容 |
|---|---|---|
| F-1 | 浮层机制 | **PopupHost 全局单例浮层栈**(每窗一份):Dialog/Menu/Select/Tooltip 统一从它弹出;浮层内元素优先命中;Esc 关闭;外部点击关闭(可配);锚定定位(菜单贴锚点,Dialog 居中,Tooltip 贴悬停元素) |
| F-2 | 焦点语义 | **焦点围栏+栈**:Dialog 打开时焦点被困 Dialog 内(Tab 循环不出圈,关闭归还焦点到打开者);Menu 同理(方向键在菜单项间移动);FocusManager 加焦点栈(push/pop);Tooltip 无焦点语义(纯悬停延时) |

## 架构

```
src/SharpUI/Elements/
 ├─ PopupHost.cs       每窗浮层栈(静态按窗服务定位,同 App.Atlas 模式):Push(popup)/Pop/Top;
 │    绘制序:声明树之后追加浮层(后进先绘在上);命中测试:浮层栈顶优先于声明树;
 │    Esc 键(浮层栈非空时)→ Pop;外部点击(栈顶浮层 dismissOnClick)→ Pop;
 │    焦点围栏:FocusManager.PushScope/PopScope(Tab 循环与方向键导航限制在栈顶 scope 内)
 ├─ DialogElement.cs   标题栏+内容槽+按钮槽;遮罩(全屏半透明,点击=可选关闭);居中;打开时 Push 焦点围栏
 ├─ TooltipElement.cs  非 Element——DSL:Ui.Tooltip(target, text) 包装任意元素,悬停 Anim 延时(~0.5s)
 │    后经 PopupHost 显示纯文本浮层(无焦点);移出即消失
 ├─ MenuElement.cs     菜单项列表(点击回调/分隔线/禁用项);锚定弹出(贴触发按钮下方);方向键导航/
 │    Enter 激活/Esc 关闭;子菜单(SubMenu)一期可选(标注:若实现超预算则 D5 只做单层,SubMenu 顺延)
 └─ SelectElement.cs   下拉选择器:按钮形态(显示当前项)+ 点击弹 Menu(复用 PopupHost);选中回调
Ui.cs                  DSL:Ui.Dialog(...)/Ui.Tooltip(...)/Ui.Menu(...)/Ui.Select(...)
```

## 语义

- **Dialog**:`Ui.Dialog(title, Action<PanelElement> build, params DialogButton[] buttons)`;`ShowDialog` 经 PopupHost(非声明树内长驻——打开是命令式动作,内容可声明式构建);遮罩点击行为可配(默认关闭);Esc=关闭;焦点围栏:进入时 Push(打开者当前焦点入栈归还),Tab 在 Dialog 内循环,关闭 Pop 归还。
- **Tooltip**:`element.Tooltip("text")` 链式扩展;悬停 0.5s(Anim 计时)后 PopupHost 显示;移出/点击即消失;无焦点。
- **Menu**:`Ui.Menu(items)` 命令式 Show(锚点=触发元素底部);方向键上下移动高亮、Enter 激活、Esc/外部点击关闭;焦点围栏同 Dialog;禁用项跳过导航;分隔线(`MenuSeparator`)。
- **Select**:`Ui.Select(options, selectedIndex, onChanged)` 按钮+Menu 复合(选中后回调并关闭);键盘:聚焦后 Enter/Space 打开、方向键选择、Enter 确认、Esc 关闭。
- **键盘事件优先级**:浮层栈非空时,FocusManager.HandleKeys 的 Esc/导航/激活先服务栈顶浮层;声明树焦点交互被围栏屏蔽。

## 测试策略

- 合成事件/键盘单测:Dialog 开关/围栏 Tab 循环/焦点归还/遮罩关闭;Tooltip 延时显示与消失;Menu 导航/激活/禁用跳过/Esc;Select 开合/选择回调;PopupHost 栈(后进先出/嵌套 Menu in Dialog)。
- Gpu 冒烟:Dialog+Menu 视觉读回各一。
- 全量绿 + Counter demo 控件段扩展(Dialog 按钮/Tooltip 示例/Select)+ AOT 复测 + 裁定#46。
- IME 前置门:解锁补验先过(不涉码,流程项)。

## 范围外

SubMenu 超预算顺延;多显示器锚定;浮层动画(仅淡入可选);模态对话框队列(同时仅一个 Dialog;Menu 可与 Dialog 并存于栈)。

## 实施纪要(Task 4 收官,2026-10-27)

- **Select 打开接线的最小侵入(简报"接线点实现者裁量"落法)**:声明式元素激活(点击/Enter/Space)需在回调内
  定位所属窗浮层栈,落法 = `Element.OwnerFocus` 注册期回链(FocusManager.Register 每帧接线,一行)——
  App.Run 帧序下指针路径读上帧回链(LastTree)、键盘路径读当帧回链,同一 FocusManager 实例语义等价;
  未注册元素(树未经 Register 直用)激活抛 InvalidOperationException(拒绝静默丢浮层)。备选 UiContext
  环境句柄方案弃:新增全局静态 + 生命周期不随元素,回链自然消亡更干净。
- **Select 直接换选的钳制语义与回调式 C-2 的耦合**:元素不藏状态(树每帧按新 selectedIndex 重建),
  同一实例连续按方向键为陈旧值语义(与 Checkbox/Slider 同实例重按一致,框架契约外);测试以帧循环
  镜像生产路径(每键一帧、按回调新值重建)锁定"±1 钳制不环移、仅变化回调"。
- **T3 顺带项裁定**:①多窗 DPI 度量偏差——App.Run 内 `Atlas = w.Atlas` 前移一行收账(点击回调内开浮层
  即本窗图集度量;单窗零行为差;双 DPI 窗专用断言留环境,本机 DPI 恒 96 不可构造);②菜单无翻转/钳制
  (锚贴屏底溢出)——入档缓议:Push 时无屏幕矩形知识,修法需按窗屏幕矩形注册表 + 翻转/夹紧策略,
  当前无近屏底菜单场景,Select 同题同裁定(MenuExtensions.Show 注释已记)。
- **demo 浮层段**:About 按钮→ShowDialog(OK=IsDefault+IsCancel)、Checkbox.Tooltip("提示文本")、
  Select 三项 + 选中回显文本,紧凑一行 Row(承 D4 收窄先例);交互承证 = SelectTests 状态断言
  (DemoSegment 条)+ headless 像素读回(Select 按钮/箭头/禁用色板),桌面锁定降级标准。
- **收官账**:全量 377/377(365+12);AOT 6,246,912 B ≈ 5.96 MiB(+55,296 B 浮层面,≤8MB 达成,
  docs/notes/m1-aot.md);AOT/JIT 冒烟 10s 存活 + 干净退出;主 spec 裁定#46 定稿。IME 视觉补验仍
  桌面锁定挂起(LogonUI 驻留会话 1)——**D5 合并条件:解锁后补跑 artifacts/ime-test/ime-regression-v3.ps1
  并回填账本,前于分支合并**。
