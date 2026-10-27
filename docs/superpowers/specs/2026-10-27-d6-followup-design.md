# D6 跟进收尾设计:子菜单/菜单翻转钳制/定位收敛/滚轮栅门/Range 键盘+打磨批

日期:2026-10-27 | 状态:已批准(SubMenu 悬停+方向键双触发,2 层上限) | 前置:D5 收官(17f08f6,379/379)

## 前置硬门(延续 D4/D5)

IME 视觉补验(`artifacts/ime-test/ime-regression-v3.ps1`,桌面解锁后)——本里程碑同样不触 IME 链,门约束合并顺序;若本轮中途桌面解锁则顺带执行并销账。

## 五项功能/健壮性(①组)

1. **菜单翻转与屏幕钳制**:`ShowMenu` 传入(或查询)屏幕逻辑尺寸:底部溢出(`anchor.Bottom + menuH > screenH`)时向上翻到 `anchor.Y - menuH`;右侧溢出钳制到 `screenW - menuW`;负坐标钳到 0。Select 复用同路径自动生效。
2. **SubMenu 子菜单**(已批准:悬停 0.2s + Right/Enter 展开,Left/Esc 退回父菜单,2 层上限=主菜单+一级子菜单):
   - `MenuSub(string label, params object?[] children)`(与 MenuItem/MenuSeparator 同混排);父项尾绘 ▶ 箭头;
   - 展开=PopupHost.Push 子菜单(锚=父项右缘,翻转钳制同规则);切到兄弟父项时先按身份 Remove 旧子菜单再展开新子菜单;
   - 键盘:父项聚焦按 Right/Enter 展开并聚焦子首项;子菜单内按 Left/Esc 仅关子菜单、焦点归还父项(PopupHost 围栏栈天然支持);子菜单项激活则**连带关闭父菜单**(整棵菜单树收起)。
3. **三套“定位所属窗”收敛**:现并存 `FocusManager.SdlWindow` 字段、`FocusManager._byWindow` 注册表、`Element.OwnerFocus` 回链——收敛为 **`FocusManager` 单一真相**:Element.OwnerFocus 保留(声明式入口),`OwnerFocus.SdlWindow` 即窗句柄;`_byWindow` 仅留作命令式 `Ui.ShowMenu(IntPtr)` 的入口桥(注释标明分工,消除冗余字段或统一经同一注册点)。
4. **浮层开时滚轮栅门**:`Scroll.Dispatch` 在浮层栈非空时:仅对**栈顶浮层根**分发滚轮(浮层内有 Scroll 则滚浮层,无则吞掉不穿透到声明树)——堵住 Dialog/Menu 打开时滚遮罩下内容的模态破口。
5. **Slider Range 键盘切换活动 thumb**:Range 模式下聚焦按 `Tab`(或新增 `PageUp/PageDown`?——Tab 与焦点移动冲突!改用 **方向键之外的切换键:`Home/End` 已占;选 `PageUp/PageDown` 或 `Q/E`?不——最自然且不抢 Tab 的是:Range 滑块注册为两个逻辑可聚焦子项,或按 `Space` 切换活动 thumb**):定案 **`Space` 切换活动 thumb**(Slider 非 IActivatable,Space 现空闲;按下翻转 StateStore 中 `LastThumb` 0↔1,视觉上活动 thumb 描亮边区分)。

## 七项打磨批(②组)

- M-a:`Ui.Slider` 回调参数改可空 `Action<float>? = null`(对齐 Checkbox/Radio)。
- M-b:已选中 Radio 点击/按键:`CanActivate` 在 `IsSelected` 时返回 false(已选中再点为无操作,ClickedIds 账本与回调一致不记——闭合 D4 终审 M5)。
- M-c:D3D11 上传微基线脚本入库 `tests/SharpUI.Tests/UploadBenchTests.cs`(Skip 默认,env `SHARPUI_RUN_BENCH=1` 开启,使 49× 可复现)。
- M-d:`FocusManager._radioGroups` 组名键留存假设补注释。
- M-e:捕获“Down 于 A 再 Down 于 B 改写为 B”显式单测。
- M-f:DialogTests 承重用例替身菜单换真 `MenuElement`;SelectTests 5 条补尾 Cleanup;`App.ShowDialog` 零窗守卫(抛 InvalidOperationException 带指引)。
- M-g:`ShaderCompiler` 错误 blob 按 ANSI(`Encoding.Default` 或 Latin1 回退,AOT 零引包)解码——闭合 M3d-ii 缓议。

## 验收

- 379 → ~400+ 全绿;Counter demo 菜单加一级 SubMenu 演示;AOT 复测;裁定#47(D6 收尾定稿)。

## 实施纪要(2026-10-27 收官,裁定#47)

- **Task 1 (`65cdddc`, 386/386)**:
  - `Ui.ShowMenu` 与 `AppWindow.ShowMenu` 增加可选 `SizeF? screenSize` 重载(`FocusManager.ScreenSize` 由 `App.CreateWindow`/`App.Run` 同步更新;Select 自动复用);底部溢出上翻 `Math.Max(0, anchor.Y - menuH)`、右侧溢出左钳 `Math.Max(0, screen.Width - menuW)`。
  - `MenuSub(string label, params object?[] children)` 落地(2 层上限,超层抛 `InvalidOperationException`);父项尾绘 `▶` 箭头(两段 quad 结构体运算,Draw 零新分配);悬停 `0.2s`(`Anim.Now` 墙钟)+ `Right`/`Enter`/点击双触发展开,切兄弟父项先按身份 `Remove` 旧子菜单;`Left`/`Esc` 仅关子菜单归还父项焦点,子菜单叶子激活调 `CloseTree()` 连带收起父菜单。
- **Task 2 (`448818e`, 389/389)**:
  - 三套“定位所属窗”收敛为 `FocusManager.SdlWindow` 唯一真相(`BindWindow` 同步置位、`UnbindWindow` 清零;`Element.OwnerFocus` 为声明式唯一入口;`_byWindow` 仅留作命令式 `Ui.ShowMenu(IntPtr)` 入口桥;`DialogElement`/`MenuElement` 移除冗余 `_owner` 字段)。
  - `Scroll.Dispatch(declarationRoot, in input, sdlWindow)` 接入 `PopupHost.TopNonTooltipOf(sdlWindow)` 模态栅门:栈顶有 Dialog/Menu 时仅对栈顶浮层根分发滚轮(或模态吞掉),Tooltip 穿透滚声明树;`App.Run` 与单测共用同一入口。
  - `SliderElement` Range 模式聚焦按 `Space` 翻转 `StateStore` 活动 thumb `0 ↔ 1`(单 thumb 不消费 Space),聚焦/捕获时活动 thumb 外扩 1px 底层圆角矩形绘 `0x93C5FD` 亮色描边(`ActiveKey` 按 `ResolvedId` 引用判等缓存,Draw 零新分配)。
- **Task 3 (收官, 392/392)**:
  - M-a..M-g 七项打磨批全数落码:`Ui.Slider` 可空回调默认 `null`、`RadioElement.CanActivate => Enabled && !_isSelected`、`UploadBenchTests.cs` 1/8/32 区段微基线入库(`SHARPUI_RUN_BENCH=1` 门控,常态早退)、`FocusManager._radioGroups` 组名键留存注释、`Capture_NewDownOnSecondCapturable_OverridesToSecond` 单测、`DialogTests` 真 `MenuElement` 叠栈 + `SelectTests` 全量 `try/finally { Cleanup(h); }` + `App.ShowDialog` 零窗守卫、`ShaderCompiler.ReadBlobString` 改 `Encoding.Latin1`。
  - Counter demo 新增 `Menu` 按钮(含 `+10`、`MenuSeparator`、`MenuSub("More", "+100", "Zero")` 与状态回显),`SelectTests.DemoSegment` 覆盖完整 SubMenu 演示路径;AOT 发布产物 `Counter.exe` `2,798,592` B、总部署面 `6,263,296` B (`≈5.97 MiB`,≤8MB 持续达成);主规格书 `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md` 记录裁定#47。
