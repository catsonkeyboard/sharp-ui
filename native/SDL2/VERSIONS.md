# Native Vendored Binaries — SDL2

Provenance record for the SDL2 vendored tree under `native/SDL2/`.
Source of the constants/offsets discipline: every SDL event constant, `SdlEvent`
field offset and keycode used by `src/SharpUI/Sdl/SdlEvents.cs` is verified
field-by-field against the official 2.30.8 headers listed below (per-line
citations live in the code comments).

## SDL2.dll (win-x64)

| field | value |
|---|---|
| library | SDL2 (Simple DirectMedia Layer) |
| version | 2.30.8 |
| architecture | x64 |
| acquisition route | vendored C 库(同 freetype/harfbuzz 地位;W-1 裁定:替换的是 C# 绑定包 Veldrid.SDL2,SDL2.dll 本体保留) |
| source tarball | `SDL2-2.30.8.tar.gz` |
| source URL (primary) | https://www.libsdl.org/release/SDL2-2.30.8.tar.gz |
| source URL (cross-check) | https://github.com/libsdl-org/SDL/releases/download/release-2.30.8/SDL2-2.30.8.tar.gz |
| tarball sha256 | `380c295ea76b9bd72d90075793971c8bcb232ba0a69a9b14da4ae8f603350058` — 两源独立下载同哈希(2026-10-26 核验) |
| vendored DLL sha256 | `c3b50f5d8e277ae1f13642ca036f0eba3155a62b141142f254ce995c74f49fbd` |
| headers used for T1 核数 | `include/SDL_events.h`、`include/SDL_video.h`、`include/SDL_keycode.h`、`include/SDL_scancode.h`、`include/SDL_keyboard.h`、`include/SDL_mouse.h` |
| license | zlib — see `SDL2/LICENSE.txt` |

### Header 核数记录(2026-10-26,Task 1)

- 事件类型常量:`SDL_events.h` `SDL_EventType`(L58-111;Quit=0x100/WindowEvent=0x200/
  KeyDown=0x300/KeyUp=0x301/TextEditing=0x302/TextInput=0x303/MouseMotion=0x400/
  MouseButtonDown=0x401/MouseButtonUp=0x402/MouseWheel=0x403)。
- `SDL_Event` 联合体 56B:`SDL_events.h` L617-644 尾部 `padding[...]` 注释明言
  "force the size to be 56 bytes"(≤8 字节指针架构)。
- 窗口子事件:`SDL_video.h` `SDL_WindowEventID`(L155-176;Resized=5/SizeChanged=6/
  FocusGained=12/FocusLost=13/Close=14)。
- 窗口 flags:`SDL_video.h` `SDL_WindowFlags`(L104-127;Shown=0x4/Hidden=0x8/
  Resizable=0x20 —— 计划稿 "Shown=0x10、Hidden=0x20" 系记忆值,实为 Borderless/Resizable)。
- KMOD:`SDL_keycode.h` `SDL_Keymod`(L334-346)。
- 键码:`SDL_keycode.h` `SDL_Keycode`(L49-137)+ `SDL_scancode.h`
  (`SDL_SCANCODE_TO_KEYCODE(X) = X | SDLK_SCANCODE_MASK`,keycode.h L48)。
- 按钮索引:`SDL_mouse.h` L445-449(Left=1/Middle=2/Right=3/X1=4/X2=5);
  滚轮方向 `SDL_MouseWheelID` L67-70(NORMAL=0/FLIPPED=1);
  按下态 `SDL_events.h` L49-50(RELEASED=0/PRESSED=1)。
- 导出名大小写:`SDL_GetDisplayDPI`(SDL_video.h L473,大写 DPI;vendored DLL
  导出表实测含 `SDL_GetDisplayDPI` 不含 `SDL_GetDisplayDpi`)。

### Header 核数记录(2026-10-26,Task 2)

- `SDL_Scancode` 全表(`SDL_scancode.h`,T2 泵键路径 `SdlEvents.KeyFromScancode`):USB HID
  usage 值序 A=4..Z=29、1=30..0=39、RETURN=40..SLASH=56、CAPSLOCK=57、F1=58..F12=69、
  PRINTSCREEN=70..UP=82、NUMLOCKCLEAR=83、KP_DIVIDE=84..KP_PERIOD=99、NONUSBACKSLASH=100、
  KP_EQUALS=103、F13=104..F24=115、MENU=118、LCTRL=224..RGUI=231;
  NONUSHASH=50/APPLICATION=101/POWER=102/EXECUTE=116/HELP=117/CLEAR=156/SLEEP=282 不映射。
- 新增导出绑定(窗口生命周期):`SDL_CreateWindow`/`SDL_DestroyWindow`(SDL_video.h;标题
  const char* 为 UTF-8)/`SDL_GetError`(SDL_error.h)。
- 映射基线互证:Veldrid.Sdl2Window `MapKey`(switch on `keysym.scancode`,private 实例方法,
  nuspec 钉版提交 a121087)反射直调全值域 0..511 比对 —— 守卫测试
  `SdlEventsTests.KeyFromScancode_MirrorsVeldridMapKey_AllScancodes`(源码下载 + 反射双证)。
