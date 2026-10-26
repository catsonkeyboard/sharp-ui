# IME 组合窗定位评估(M3c 收口,决策文档)

结论:**M3c/M3d 不做组合窗定位;提交文本通道维持现状(KeyCharPresses)**。

- 现状:SDL_TEXTINPUT → `Sdl2Window.KeyCharPresses` → `RawInput.Chars` → `FeedFocused` → TextField,
  Windows 下中文 IME 提交文本实测可用(M3a 冒烟);组合中(preedit)文本无显示(SDL_TextEditingEvent 未被
  Veldrid.Sdl2 映射:枚举值存在,窗口类不暴露组合文本)。
- 探针备注(2026-10-25,本机 Veldrid.SDL2 4.9.0 元数据全文扫描):托管绑定**未封装**
  `SDL_StartTextInput/SDL_StopTextInput/SDL_SetTextInputRect`(仅封装剪贴板两函数);而随包 native
  SDL2.dll(文件版本 2.0.8)**本身导出**这三个函数——简报"SDL_SetTextInputRect 始于 2.0.22+"系误记,
  2.0.8 导出表已有。即:缺口在 Veldrid.Sdl2 绑定层,不在 SDL2 库。
- SharpUI 从未调用 SDL_StartTextInput,提交文本仍可用 = 旧版 SDL2 WM_CHAR 路径无门控;较新 SDL2 各后端
  已逐步以 SDL_StartTextInput 为文本发送门(SDL3 全面门控)。**升级随包 native SDL2 时提交文本可能失效**,
  须复验并自行 P/Invoke 开启(绑定缺失自补导出,同 D25 剪贴板先例)。
- 组合窗定位:SDL/imm32 后端经 `SDL_SetTextInputRect` 矩形驱动 `ImmSetCandidateWindow`;绑定未封装 →
  SharpUI 无法设置,候选窗落 SDL 默认位置(不随光标)。
- Windows 精确候选跟随 + 内联组合(preedit 下划线/文本)需直连 IMM32/TSF(hwnd + 光标屏幕坐标),
  即 D5 既定"手写平台层",归 **M3e**;SDL3(Silk.NET)/GLFW 为 R3 备选路线。
- 复评触发条件:用户报告候选窗位置影响输入体验 / 升级 native SDL2 后提交文本失效 / M3e 无障碍(TSF/UIA)立项。
- 2026-10-25 修复一(不够):SDL_StartTextInput/SetTextInputRect 桥接落地,但用户复测候选框仍不显示。
- 2026-10-25 修复二(根因,裁定#34):查证 SDL 源码——SDL ≤2.0.17 在 WM_IME_SETCONTEXT 无条件 `*lParam = 0`,
  掐掉 ISC_SHOWUICOMPOSITIONWINDOW|ISC_SHOWUICANDIDATEWINDOW(原生组合/候选窗被 SDL **设计性压制**,
  其原意是自绘候选,仅 UI-less TSF 路径)。2.0.18 新增 `SDL_HINT_IME_SHOW_UI("SDL_IME_SHOW_UI")` 放行原生 UI。
  处置:vendor SDL 2.30.8(native/SDL2/,zlib 许可随附)经构建目标覆盖 Veldrid 包内 2.0.8(ABI 兼容,
  探针实证),App.Create 窗口创建前 `SDL_SetHint("SDL_IME_SHOW_UI","1")`;StartTextInput/SetTextInputRect 接线保留。
  preedit 内联显示仍待 Veldrid 缺口(SDL_TextEditingEvent 未透传,组合文本显示于 IME 自身 UI)。
- 2026-10-25 修复三(定位,裁定#36):候选框已显示(修复二)但飘至屏幕右下角——SDL 2.30 的
  SetTextInputRect→ImmSetCompositionWindow 链落点不准且从不调用 ImmSetCandidateWindow。
  处置:SetRect 双通道:保留 SDL 官方路径 + 直接 imm32(ImmGetContext→
  ImmSetCompositionWindow(CFS_POINT,光标点)/ImmSetCandidateWindow(CFS_CANDIDATE_POS,光标下方),
  ImmReleaseContext;HWND 经 SDL_GetWindowWMInfo 取,客户区像素坐标即 IMM 坐标系;Win 守卫+句柄缓存)。
- 2026-10-25 修复四(caret,裁定#37):修复三用户复测仍右下角——Win10/11 微软拼音(TSF)候选 UI 跟随
  **系统插入符**而非仅 IMM 调用,无 caret 时飘至屏幕角落(ImGui Win32 后端同款处置)。
  处置:PositionImm 尾部 CreateCaret/SetCaretPos/ShowCaret,Stop 时 DestroyCaret。
- 2026-10-25 修复五(computer-use 自测闭环,裁定#38+#39,终获实证通过):
  自测发现修复三/四的 IMM 通道**从未执行**——`SDL_GetWindowWMInfo` 恒返 0。
  两个叠加根因:
  - **#38 双模块**:Veldrid.Sdl2 自身预载 `runtimes/<rid>/native/SDL2.dll`,其 DllImport 又解析到应用
    基目录副本 → 进程内同时存在两份 SDL2 模块(模块枚举实证双 base 地址);窗口属主(已初始化,
    WasInit=VIDEO)是基目录副本,而 SdlTextInput 旧预载落到孤儿副本,一切 SDL 调用进错模块
    (WMInfo 报 "Video subsystem has not been initialized";修复二的 SetHint 同样落空——候选框出现
    纯系 2.30.8 升级本身)。处置:弃 DllImport/预载,显式 LoadLibrary 候选路径 +
    `SDL_WasInit(VIDEO)` 判定活模块,函数指针直调(SdlApi 绑定层);hint 改在活模块绑定时生效。
  - **#39 WMInfo 结构越界写**:SDL 2.24+ Windows 联合体 4 指针(window/hdc/hglrc/helperWindow,
    至 offset 40),旧 SdlSysWmInfo 仅 16 字节——绑定修好后 WMInfo 立刻成功并越界冲毁封送暂存区,
    纯托管 return 处段错误(版本字读出随机乱码即此副作用)。处置:结构补全 4 指针 + 8 指针余量。
  另:caret 坐标系修正(SetCaretPos 收**屏幕**坐标,IMM 收**客户区**坐标,同一逻辑点须 ClientToScreen 换算);
  demo 增大至 900x960(原 480x320 输入框在折叠线下)。
  **实测通过**(keybd_event 真实键盘管线):拼音 "nihao" → 输入框内组合串(下划线)+
  候选框出现在输入框正下方偏右(不再屏幕右下角)→ 回车 "你好" 上屏,候选框消失。
  测试配套:SHARPUI_IME_TRACE=1 逐帧 trace 至 %TEMP%\sharpui-ime-trace.log;SHARPUI_TEST_IME_OPEN=1
  强制 IME 打开(自动化注入键不触发 Shift 中英切换时用);TextFieldElement.FocusOnStart() 声明式启动聚焦
  (FindAutoFocus 深度优先,App.Run 无焦点时 SetFocus)绕开合成指针不可靠问题。测试 156→158。

- 2026-10-26 T4 窗口层收官实测回归(裁定#43 收账,证据 artifacts/ime-test/t4-regression-v3):
  泵重构后 IME 链路完整——SHARPUI_TEST_IME_OPEN=1 + keybd_event 真键盘注入 "nihao":组合串 "ni hao"
  + 候选窗(1.你好…)出现在输入框正下方偏右(PositionImm 逐帧 SetRect/ClientToScreen 实证,466 次);
  **空格提交「你好」经 SDL_TEXTINPUT→自有泵→TextField 上屏读回**(3-committed-space 截图)。
  组合串呈"ni hao"分音节(黄底系统合成窗、输入框下方)而旧记录"nihao"整串输入框内——系 MS 拼音
  分音节呈现随系统版本/设置变化,非链路回归(PositionImm trace 466 次定位链完好)。
  **勘误上文"回车 '你好' 上屏"**:本机微软拼音回车=提交原始拼音 "nihao"(2-committed-enter 实证;
  Veldrid 时代同理,系当时记录不精确,非本次回归)。回车/空格均为 IME 侧语义,与框架链路无关;
  自动化验证以空格提交为准。Ctrl+A/Backspace 清空在 IME 强制打开态被 IME 吞键(连续注入下),
  两次回归均如此;IME 非强制态 Backspace 正常(t4-interactions 实证),亦属系统 IME 行为。
