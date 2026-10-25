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
