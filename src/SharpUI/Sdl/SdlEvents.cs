using System.Runtime.InteropServices;
using System.Text;
using SharpUI.Backends;

namespace SharpUI;

/// <summary>SDL 事件声明层(T1):常量 + SDL_Event 联合体 + 键码/修饰/按钮映射 + 事件泵与窗口查询 P/Invoke。
/// 全部常量与 FieldOffset 对 SDL 2.30.8 官方头文件逐字段核数(出处逐条注释;tarball sha256 与核数记录见
/// native/SDL2/VERSIONS.md)。模块纪律(裁定#38,同 SdlNative):不裸 DllImport —— 函数指针经 SdlNative
/// 活模块句柄按名取导出;窗口相关查询经 SdlNative.OwnerModule(窗口属主模块应答),泵/显示器查询经
/// 首个活候选(T2 裁定:多候选下泵只打首个活副本队列 —— 创建(SdlWindow)亦钉同选择器,事件可达性成立;
/// 遗留非本层窗口事件由 SdlPump 安全丢弃,等价排空)。零 unsafe,AOT 兼容(委托 GetDelegateForFunctionPointer)。</summary>
internal static class SdlEvents
{
    // ---- 事件类型(SDL_events.h SDL_EventType,L58-111;与 Veldrid.SDL2 编译内 SDL_EventType 反射互证一致)----
    internal const uint EventTypeQuit = 0x100;             // SDL_QUIT(L60)
    internal const uint EventTypeWindow = 0x200;           // SDL_WINDOWEVENT(L94)
    internal const uint EventTypeKeyDown = 0x300;          // SDL_KEYDOWN(L98)
    internal const uint EventTypeKeyUp = 0x301;            // SDL_KEYUP(L99)
    internal const uint EventTypeTextEditing = 0x302;      // SDL_TEXTEDITING(L100)
    internal const uint EventTypeTextInput = 0x303;        // SDL_TEXTINPUT(L101)
    internal const uint EventTypeMouseMotion = 0x400;      // SDL_MOUSEMOTION(L108)
    internal const uint EventTypeMouseButtonDown = 0x401;  // SDL_MOUSEBUTTONDOWN(L109)
    internal const uint EventTypeMouseButtonUp = 0x402;    // SDL_MOUSEBUTTONUP(L110)
    internal const uint EventTypeMouseWheel = 0x403;       // SDL_MOUSEWHEEL(L111)

    // ---- 窗口子事件(SDL_video.h SDL_WindowEventID,L155-176;声明序自 NONE=0 递增)----
    internal const byte WindowEventResized = 5;        // SDL_WINDOWEVENT_RESIZED(L163)
    internal const byte WindowEventSizeChanged = 6;    // SDL_WINDOWEVENT_SIZE_CHANGED(L164)
    internal const byte WindowEventFocusGained = 12;   // SDL_WINDOWEVENT_FOCUS_GAINED(L173)
    internal const byte WindowEventFocusLost = 13;     // SDL_WINDOWEVENT_FOCUS_LOST(L174)
    internal const byte WindowEventClose = 14;         // SDL_WINDOWEVENT_CLOSE(L175)

    // ---- KMOD(SDL_keycode.h SDL_Keymod,L334-346,Uint16 位域)----
    internal const ushort KmodLShift = 0x0001;
    internal const ushort KmodRShift = 0x0002;
    internal const ushort KmodLControl = 0x0040;
    internal const ushort KmodRControl = 0x0080;
    internal const ushort KmodLAlt = 0x0100;
    internal const ushort KmodRAlt = 0x0200;
    internal const ushort KmodLGui = 0x0400;
    internal const ushort KmodRGui = 0x0800;
    // NUM=0x1000/CAPS=0x2000/MODE=0x4000/SCROLL=0x8000(L343-346)非键修饰,不声明

    // ---- 按钮索引/按下态/滚轮方向 ----
    internal const byte ButtonLeft = 1;    // SDL_BUTTON_LEFT(SDL_mouse.h L445)
    internal const byte ButtonMiddle = 2;  // SDL_BUTTON_MIDDLE(L446)
    internal const byte ButtonRight = 3;   // SDL_BUTTON_RIGHT(L447)
    internal const byte ButtonX1 = 4;      // SDL_BUTTON_X1(L448;T2 泵层消费)
    internal const byte ButtonX2 = 5;      // SDL_BUTTON_X2(L449)
    internal const byte Pressed = 1;       // SDL_PRESSED(SDL_events.h L50;SDL_RELEASED=0,L49)
    internal const uint WheelFlipped = 1;  // SDL_MOUSEWHEEL_FLIPPED(SDL_mouse.h L67-70,NORMAL=0);T2 裁定:
                                           // 泵 WheelDelta 忽略方向位(Veldrid HandleMouseWheelEvent 源码实证
                                           // "WheelDelta += y" 无方向分支,同基线保留;Windows 恒 NORMAL)

    // ---- 窗口 flags(SDL_video.h SDL_WindowFlags,L104-127)----
    // 计划稿记忆值 "Shown=0x10、Hidden=0x20" 已按头文件实数校正:Shown=0x4、Hidden=0x8;
    // 0x10 实为 BORDERLESS、0x20 实为 RESIZABLE(计划稿将 Resizable 与 Hidden 写重)。
    internal const uint WindowFlagFullscreen = 0x00000001;      // L104
    internal const uint WindowFlagOpenGL = 0x00000002;          // L105
    internal const uint WindowFlagShown = 0x00000004;           // L106
    internal const uint WindowFlagHidden = 0x00000008;          // L107
    internal const uint WindowFlagBorderless = 0x00000010;      // L108
    internal const uint WindowFlagResizable = 0x00000020;       // L109
    internal const uint WindowFlagMinimized = 0x00000040;       // L110
    internal const uint WindowFlagMaximized = 0x00000080;       // L111
    internal const uint WindowFlagInputFocus = 0x00000200;      // L113(焦点态查询,T2 消费)
    internal const uint WindowFlagMouseFocus = 0x00000400;      // L114
    internal const uint WindowFlagFullscreenDesktop = 0x00001001;  // L105+0x1000(L115 定义)
    internal const uint WindowFlagAllowHighDpi = 0x00002000;    // L117

    /// <summary>SDL_Event 联合体(SDL_events.h L617-644,尾部 padding 注释明言 56B):显式布局重叠读,零 unsafe。
    /// 公共头(SDL_CommonEvent,L182-188):type@0/timestamp@4/windowID@8,各分支一致。
    /// 偏移逐字段核数出处:键盘 SDL_KeyboardEvent(L221-232)+ SDL_Keysym(SDL_keyboard.h L47-53);
    /// 文本 SDL_TextInputEvent(L267-274);按钮 SDL_MouseButtonEvent(L314-329)、移动 SDL_MouseMotionEvent
    /// (L277-289)、滚轮 SDL_MouseWheelEvent(L302-316);窗口 SDL_WindowEvent(L208-218)。
    /// 注意:鼠标三分支在 windowID 后均有 which@12(Uint32,鼠标实例 id)—— 计划稿略去该字段致
    /// 鼠标分支偏移整体左移 4B,已按实数校正;重叠值字段为显式布局所允许(零引用类型字段)。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    internal struct SdlEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Timestamp;
        [FieldOffset(8)] public uint WindowId;

        // 键盘分支:state@12/repeat@13/pad@14-15,keysym@16{scancode@16,sym@20,mod@24(U16),unused@28}
        [FieldOffset(12)] public byte KeyState;
        [FieldOffset(13)] public byte KeyRepeat;
        [FieldOffset(16)] public uint KeysymScancode;
        [FieldOffset(20)] public uint KeysymSym;
        [FieldOffset(24)] public ushort KeysymMod;

        // 文本输入分支:text@12(char[32] UTF-8,0 终止)
        [FieldOffset(12)] public byte TextInputByte0;   // 全串解码经 Marshal.PtrToStringAnsi(事件基址+12)或逐字节读

        // 鼠标按钮分支:which@12(略读),button@16/state@17/clicks@18/pad@19,x@20/y@24
        [FieldOffset(16)] public byte MouseButtonId;
        [FieldOffset(17)] public byte MouseButtonState;
        [FieldOffset(18)] public byte MouseClicks;
        // 鼠标移动分支:which@12(略读),state@16(Uint32 按位),x@20/y@24,xrel@28/yrel@32
        [FieldOffset(16)] public uint MotionState;      // 与 MouseButtonId/State 同偏重叠:motion 分支的按位按钮态
        [FieldOffset(20)] public int MouseX;            // button 与 motion 两分支的 x 同在 @20
        [FieldOffset(24)] public int MouseY;
        [FieldOffset(28)] public int MouseRelX;         // motion.xrel
        [FieldOffset(32)] public int MouseRelY;         // motion.yrel

        // 滚轮分支:which@12(略读),x@16/y@20,direction@24;preciseX/Y@28/32、mouseX/Y@36/40 未声明(T2 需要再扩)
        [FieldOffset(16)] public int WheelX;
        [FieldOffset(20)] public int WheelY;
        [FieldOffset(24)] public uint WheelDirection;   // 0=NORMAL/1=FLIPPED(SDL_mouse.h L67-70)

        // 窗口分支:event@12,pad@13-15,data1@16/data2@20
        [FieldOffset(12)] public byte WindowEventCode;
        [FieldOffset(16)] public int WindowData1;
        [FieldOffset(20)] public int WindowData2;
    }

    // ---- 映射 ----

    /// <summary>SDL 键码(SDL_Keycode = keysym.sym)→ Key;未映射 → Key.Unknown。
    /// 语义基线:Veldrid.Sdl2Window.MapKey(nuspec 钉版提交 a121087,源码比对)按 scancode 映射,本表按 sym
    /// 映射 —— 对 Veldrid 所映射的全部物理键两法同果(字母 sym 恒小写 'a'..'z'=0x61..0x7A);残余差异仅在
    /// 非 US 布局(sym 随布局、scancode 随物理位),留 T2 泵语义裁定。Veldrid 怪quirk 保留:
    /// KP_EQUALS → KeypadPlus(Key 枚举无 KeypadEquals 成员)、default → Unknown。
    /// 常量出处:字面量键 SDL_keycode.h L52-150;派生键 KC(X)=0x40000000|X(keycode.h L48,X 取 SDL_scancode.h 实数)。</summary>
    internal static Key KeyFromSym(uint sym) => sym switch
    {
        // 字母(SDLK_a..SDLK_z,L102-140)
        0x61 => Key.A, 0x62 => Key.B, 0x63 => Key.C, 0x64 => Key.D, 0x65 => Key.E,
        0x66 => Key.F, 0x67 => Key.G, 0x68 => Key.H, 0x69 => Key.I, 0x6A => Key.J,
        0x6B => Key.K, 0x6C => Key.L, 0x6D => Key.M, 0x6E => Key.N, 0x6F => Key.O,
        0x70 => Key.P, 0x71 => Key.Q, 0x72 => Key.R, 0x73 => Key.S, 0x74 => Key.T,
        0x75 => Key.U, 0x76 => Key.V, 0x77 => Key.W, 0x78 => Key.X, 0x79 => Key.Y,
        0x7A => Key.Z,
        // 数字(SDLK_0..SDLK_9,L74-83)
        0x30 => Key.Number0, 0x31 => Key.Number1, 0x32 => Key.Number2, 0x33 => Key.Number3,
        0x34 => Key.Number4, 0x35 => Key.Number5, 0x36 => Key.Number6, 0x37 => Key.Number7,
        0x38 => Key.Number8, 0x39 => Key.Number9,
        // 控制字面量键(L52-58、L150)
        0x08 => Key.BackSpace,     // SDLK_BACKSPACE '\b'
        0x09 => Key.Tab,           // SDLK_TAB '\t'
        0x0D => Key.Enter,         // SDLK_RETURN '\r'
        0x1B => Key.Escape,        // SDLK_ESCAPE '\x1B'
        0x20 => Key.Space,         // SDLK_SPACE ' '
        0x7F => Key.Delete,        // SDLK_DELETE '\x7F'(字面量,非 scancode 派生)
        // 标点字面量键(L59-101;0x2E/0x2F/0x56 等勿与 Win32 VK 码混淆 —— 计划稿混用已校正)
        0x2D => Key.Minus,         // SDLK_MINUS '-'
        0x3D => Key.Plus,          // SDLK_EQUALS '='(Veldrid:SCANCODE_EQUALS→Plus)
        0x5B => Key.BracketLeft,   // SDLK_LEFTBRACKET '['
        0x5D => Key.BracketRight,  // SDLK_RIGHTBRACKET ']'
        0x5C => Key.BackSlash,     // SDLK_BACKSLASH '\'
        0x3B => Key.Semicolon,     // SDLK_SEMICOLON ';'
        0x27 => Key.Quote,         // SDLK_QUOTE '\''
        0x60 => Key.Tilde,         // SDLK_BACKQUOTE '`'(Veldrid 用同值别名 Grave)
        0x2C => Key.Comma,         // SDLK_COMMA ','
        0x2E => Key.Period,        // SDLK_PERIOD '.'
        0x2F => Key.Slash,         // SDLK_SLASH '/'
        // scancode 派生键(SDL_keycode.h L129-165 + SDL_scancode.h 实数;注释 KC() 内为 SDL_Scancode 值)
        0x40000039 => Key.CapsLock,      // KC(57)
        0x4000003A => Key.F1,            // KC(58)
        0x4000003B => Key.F2,            // KC(59)
        0x4000003C => Key.F3,            // KC(60)
        0x4000003D => Key.F4,            // KC(61)
        0x4000003E => Key.F5,            // KC(62)
        0x4000003F => Key.F6,            // KC(63)
        0x40000040 => Key.F7,            // KC(64)
        0x40000041 => Key.F8,            // KC(65)
        0x40000042 => Key.F9,            // KC(66)
        0x40000043 => Key.F10,           // KC(67)
        0x40000044 => Key.F11,           // KC(68)
        0x40000045 => Key.F12,           // KC(69)
        0x40000046 => Key.PrintScreen,   // KC(70)
        0x40000047 => Key.ScrollLock,    // KC(71)
        0x40000048 => Key.Pause,         // KC(72)
        0x40000049 => Key.Insert,        // KC(73)
        0x4000004A => Key.Home,          // KC(74)
        0x4000004B => Key.PageUp,        // KC(75)
        0x4000004D => Key.End,           // KC(77)
        0x4000004E => Key.PageDown,      // KC(78)
        0x4000004F => Key.Right,         // KC(79)
        0x40000050 => Key.Left,          // KC(80)
        0x40000051 => Key.Down,          // KC(81)
        0x40000052 => Key.Up,            // KC(82)
        0x40000053 => Key.NumLock,       // KC(83)SDLK_NUMLOCKCLEAR
        0x40000054 => Key.KeypadDivide,  // KC(84)SDLK_KP_DIVIDE
        0x40000055 => Key.KeypadMultiply,// KC(85)SDLK_KP_MULTIPLY
        0x40000056 => Key.KeypadMinus,   // KC(86)SDLK_KP_MINUS(别名 KeypadSubtract 同值)
        0x40000057 => Key.KeypadPlus,    // KC(87)SDLK_KP_PLUS(别名 KeypadAdd 同值)
        0x40000058 => Key.KeypadEnter,   // KC(88)SDLK_KP_ENTER
        0x40000059 => Key.Keypad1,       // KC(89)SDLK_KP_1
        0x4000005A => Key.Keypad2,       // KC(90)SDLK_KP_2
        0x4000005B => Key.Keypad3,       // KC(91)SDLK_KP_3
        0x4000005C => Key.Keypad4,       // KC(92)SDLK_KP_4
        0x4000005D => Key.Keypad5,       // KC(93)SDLK_KP_5
        0x4000005E => Key.Keypad6,       // KC(94)SDLK_KP_6
        0x4000005F => Key.Keypad7,       // KC(95)SDLK_KP_7
        0x40000060 => Key.Keypad8,       // KC(96)SDLK_KP_8
        0x40000061 => Key.Keypad9,       // KC(97)SDLK_KP_9
        0x40000062 => Key.Keypad0,       // KC(98)SDLK_KP_0
        0x40000063 => Key.KeypadPeriod,  // KC(99)SDLK_KP_PERIOD(别名 KeypadDecimal 同值)
        0x40000064 => Key.NonUSBackSlash,// KC(100)
        0x40000067 => Key.KeypadPlus,    // KC(103)SDLK_KP_EQUALS —— Veldrid 映 KeypadPlus 的怪quirk,同基线保留
        0x40000068 => Key.F13,           // KC(104)
        0x40000069 => Key.F14,           // KC(105)
        0x4000006A => Key.F15,           // KC(106)
        0x4000006B => Key.F16,           // KC(107)
        0x4000006C => Key.F17,           // KC(108)
        0x4000006D => Key.F18,           // KC(109)
        0x4000006E => Key.F19,           // KC(110)
        0x4000006F => Key.F20,           // KC(111)
        0x40000070 => Key.F21,           // KC(112)
        0x40000071 => Key.F22,           // KC(113)
        0x40000072 => Key.F23,           // KC(114)
        0x40000073 => Key.F24,           // KC(115)
        0x40000076 => Key.Menu,          // KC(118)
        0x400000E0 => Key.ControlLeft,   // KC(224)SDLK_LCTRL
        0x400000E1 => Key.ShiftLeft,     // KC(225)SDLK_LSHIFT
        0x400000E2 => Key.AltLeft,       // KC(226)SDLK_LALT
        0x400000E3 => Key.LWin,          // KC(227)SDLK_LGUI(= WinLeft,镜像别名)
        0x400000E4 => Key.ControlRight,  // KC(228)SDLK_RCTRL
        0x400000E5 => Key.ShiftRight,    // KC(229)SDLK_RSHIFT
        0x400000E6 => Key.AltRight,      // KC(230)SDLK_RALT
        0x400000E7 => Key.RWin,          // KC(231)SDLK_RGUI(= WinRight)
        _ => Key.Unknown,
    };

    /// <summary>SDL_Scancode(SDL_keyboard.h SDL_Keysym.scancode 实为 SDL_scancode.h 的 SDL_Scancode)→ Key;
    /// 未映射 → Key.Unknown。语义基线 = Veldrid.Sdl2Window.MapKey 源码(nuspec 钉版提交 a121087):其 switch
    /// 恰取 keysym.<b>scancode</b>(非 sym —— T2 泵层实证比对源码;携带项④),故本表为泵 KeyDown/KeyUp 的
    /// 键路径;KeyFromSym(sym 路径)保留(T1 测试在用,非 US 布局下两者语义不同:scancode 随物理位)。
    /// 怪quirk 对齐:KP_EQUALS → KeypadPlus(Key 枚举无 KeypadEquals)、SLEEP(282)/CLEAR(156)/APPLICATION(101)/
    /// POWER(102)/NONUSHASH(50) 等不映射 → Unknown。守卫:KeyFromScancode_MirrorsVeldridMapKey_AllScancodes
    /// 反射直调 Veldrid MapKey 全值域(0..511)比对。常量出处:SDL_scancode.h release-2.30.8(USB HID usage
    /// 值序:A=4..Z=29、1=30..0=39、CAPSLOCK=57、F1=58..F12=69、DELETE=76、NUMLOCK=83、KP_PERIOD=99、
    /// NONUSBACKSLASH=100、KP_EQUALS=103、F13=104..F24=115、MENU=118、LCTRL=224..RGUI=231)。</summary>
    internal static Key KeyFromScancode(uint scancode) => scancode switch
    {
        // 字母(SDL_SCANCODE_A..Z,L54-79)
        4 => Key.A, 5 => Key.B, 6 => Key.C, 7 => Key.D, 8 => Key.E,
        9 => Key.F, 10 => Key.G, 11 => Key.H, 12 => Key.I, 13 => Key.J,
        14 => Key.K, 15 => Key.L, 16 => Key.M, 17 => Key.N, 18 => Key.O,
        19 => Key.P, 20 => Key.Q, 21 => Key.R, 22 => Key.S, 23 => Key.T,
        24 => Key.U, 25 => Key.V, 26 => Key.W, 27 => Key.X, 28 => Key.Y,
        29 => Key.Z,
        // 数字(SDL_SCANCODE_1..9、0,L80-89)
        30 => Key.Number1, 31 => Key.Number2, 32 => Key.Number3, 33 => Key.Number4,
        34 => Key.Number5, 35 => Key.Number6, 36 => Key.Number7, 37 => Key.Number8,
        38 => Key.Number9, 39 => Key.Number0,
        // 控制键(L90-94)
        40 => Key.Enter,        // SDL_SCANCODE_RETURN
        41 => Key.Escape,       // SDL_SCANCODE_ESCAPE
        42 => Key.BackSpace,    // SDL_SCANCODE_BACKSPACE
        43 => Key.Tab,          // SDL_SCANCODE_TAB
        44 => Key.Space,        // SDL_SCANCODE_SPACE
        // 标点(L95-110;NONUSHASH=50 Veldrid 未映射,跳过)
        45 => Key.Minus,        // SDL_SCANCODE_MINUS
        46 => Key.Plus,         // SDL_SCANCODE_EQUALS
        47 => Key.BracketLeft,  // SDL_SCANCODE_LEFTBRACKET
        48 => Key.BracketRight, // SDL_SCANCODE_RIGHTBRACKET
        49 => Key.BackSlash,    // SDL_SCANCODE_BACKSLASH
        51 => Key.Semicolon,    // SDL_SCANCODE_SEMICOLON
        52 => Key.Quote,        // SDL_SCANCODE_APOSTROPHE
        53 => Key.Tilde,        // SDL_SCANCODE_GRAVE(Veldrid 返回同值别名 Grave)
        54 => Key.Comma,        // SDL_SCANCODE_COMMA
        55 => Key.Period,       // SDL_SCANCODE_PERIOD
        56 => Key.Slash,        // SDL_SCANCODE_SLASH
        // 锁定/功能/导航(L111-142)
        57 => Key.CapsLock,     // SDL_SCANCODE_CAPSLOCK
        58 => Key.F1, 59 => Key.F2, 60 => Key.F3, 61 => Key.F4, 62 => Key.F5, 63 => Key.F6,
        64 => Key.F7, 65 => Key.F8, 66 => Key.F9, 67 => Key.F10, 68 => Key.F11, 69 => Key.F12,
        70 => Key.PrintScreen,  // SDL_SCANCODE_PRINTSCREEN
        71 => Key.ScrollLock,   // SDL_SCANCODE_SCROLLLOCK
        72 => Key.Pause,        // SDL_SCANCODE_PAUSE
        73 => Key.Insert,       // SDL_SCANCODE_INSERT
        74 => Key.Home,         // SDL_SCANCODE_HOME
        75 => Key.PageUp,       // SDL_SCANCODE_PAGEUP
        76 => Key.Delete,       // SDL_SCANCODE_DELETE
        77 => Key.End,          // SDL_SCANCODE_END
        78 => Key.PageDown,     // SDL_SCANCODE_PAGEDOWN
        79 => Key.Right,        // SDL_SCANCODE_RIGHT
        80 => Key.Left,         // SDL_SCANCODE_LEFT
        81 => Key.Down,         // SDL_SCANCODE_DOWN
        82 => Key.Up,           // SDL_SCANCODE_UP
        // 小键盘(L143-159)
        83 => Key.NumLock,       // SDL_SCANCODE_NUMLOCKCLEAR
        84 => Key.KeypadDivide,  // SDL_SCANCODE_KP_DIVIDE
        85 => Key.KeypadMultiply,// SDL_SCANCODE_KP_MULTIPLY
        86 => Key.KeypadMinus,   // SDL_SCANCODE_KP_MINUS(别名 KeypadSubtract 同值)
        87 => Key.KeypadPlus,    // SDL_SCANCODE_KP_PLUS
        88 => Key.KeypadEnter,   // SDL_SCANCODE_KP_ENTER
        89 => Key.Keypad1, 90 => Key.Keypad2, 91 => Key.Keypad3,
        92 => Key.Keypad4, 93 => Key.Keypad5, 94 => Key.Keypad6,
        95 => Key.Keypad7, 96 => Key.Keypad8, 97 => Key.Keypad9,
        98 => Key.Keypad0,
        99 => Key.KeypadPeriod,  // SDL_SCANCODE_KP_PERIOD(别名 KeypadDecimal 同值)
        // 扩展区(L161-175;APPLICATION=101/POWER=102 Veldrid 未映射,跳过)
        100 => Key.NonUSBackSlash,  // SDL_SCANCODE_NONUSBACKSLASH
        103 => Key.KeypadPlus,      // SDL_SCANCODE_KP_EQUALS —— Veldrid 映 KeypadPlus 怪quirk,同基线保留
        104 => Key.F13, 105 => Key.F14, 106 => Key.F15, 107 => Key.F16, 108 => Key.F17,
        109 => Key.F18, 110 => Key.F19, 111 => Key.F20, 112 => Key.F21, 113 => Key.F22,
        114 => Key.F23, 115 => Key.F24,
        118 => Key.Menu,          // SDL_SCANCODE_MENU(EXECUTE=116/HELP=117 Veldrid 未映射)
        // 修饰键(SDL_keyboard.h L77-84;值 = 224+ 物理位)
        224 => Key.ControlLeft,   // SDL_SCANCODE_LCTRL
        225 => Key.ShiftLeft,     // SDL_SCANCODE_LSHIFT
        226 => Key.AltLeft,       // SDL_SCANCODE_LALT
        227 => Key.LWin,          // SDL_SCANCODE_LGUI(= WinLeft,镜像别名)
        228 => Key.ControlRight,  // SDL_SCANCODE_RCTRL
        229 => Key.ShiftRight,    // SDL_SCANCODE_RSHIFT
        230 => Key.AltRight,      // SDL_SCANCODE_RALT
        231 => Key.RWin,          // SDL_SCANCODE_RGUI(= WinRight)
        _ => Key.Unknown,
    };

    /// <summary>KMOD 位 → ModifierKeys:左右同键聚合(Veldrid.Sdl2Window.MapModifierKeys 同语义)。
    /// NUM/CAPS/MODE/SCROLL 状态位不属键修饰,忽略。</summary>
    internal static ModifierKeys ModifiersFromMod(ushort mod)
    {
        ModifierKeys m = ModifierKeys.None;
        if ((mod & (KmodLShift | KmodRShift)) != 0) m |= ModifierKeys.Shift;
        if ((mod & (KmodLControl | KmodRControl)) != 0) m |= ModifierKeys.Control;
        if ((mod & (KmodLAlt | KmodRAlt)) != 0) m |= ModifierKeys.Alt;
        if ((mod & (KmodLGui | KmodRGui)) != 0) m |= ModifierKeys.Gui;
        return m;
    }

    /// <summary>SDL 原生按钮索引 → MouseButton:1→左/2→中/3→右/4(X1)→Button1/5(X2)→Button2;未知 → Left。
    /// T2 泵语义裁定(携带项②):X1/X2 对齐 Veldrid.Sdl2Window.MapMouseButton(源码实证,提交 a121087),
    /// Veldrid.MouseButton 枚举无 X 命名而以 Button1/Button2 承接;default → Left 同 Veldrid。</summary>
    internal static MouseButton MouseButtonFromId(byte id) => id switch
    {
        ButtonMiddle => MouseButton.Middle,
        ButtonRight => MouseButton.Right,
        ButtonX1 => MouseButton.Button1,
        ButtonX2 => MouseButton.Button2,
        _ => MouseButton.Left,
    };

    // ---- P/Invoke(经 SdlNative 活模块;SDLCALL = cdecl)----

    /// <summary>取一事件(非阻塞)。SDL_PollEvent 内部自带 pump(SDL_events.h SDL_PollEvent 文档节),
    /// 单模块一次调用即完成"泵+取";多候选模块的排空策略属 T2 PumpAll。返回 false = 队列空。
    /// 委托解析经 SdlNative.Fn 缓存(携带项③:热路径,每帧多次调用,GetDelegateForFunctionPointer
    /// 每调用新分配委托,不缓存即热路径持续分配)。</summary>
    internal static bool PollEvent(out SdlEvent e) =>
        SdlNative.Fn<SdlPollEventFn>(LiveModule(), "SDL_PollEvent")(out e) == 1;

    /// <summary>创建窗口(T2)。模块落点 = LiveModule(与 PollEvent 同一选择器)—— SDL 事件队列是
    /// <b>每模块</b>一份:窗口建在模块 M,其事件仅入 M 的队列;PollEvent 泵的恰是首个活候选,故创建必须
    /// 钉在同选择器上,事件才可达(裁定#38 双副本分裂下若创建/泵各落一端,事件即整队丢失 —— 这是与
    /// SdlNative.OwnerModule 纪律"窗口调用问属主"不同的新裁定:创建时窗口尚无属主,属主由创建落点定义)。
    /// 标题 UTF-8(SDL_CreateWindow 文档明言 const char* 为 UTF-8;CharSet.Ansi 在非英文代码页会 mojibake,
    /// Marshal.StringToHGlobalUTF8 为 .NET Framework 专属已不存在,AllocHGlobal+Copy 手编;创建非热帧路,
    /// 暂存可接受)。失败返回 Zero(SDL 语义)。</summary>
    internal static IntPtr CreateWindow(string title, int x, int y, int w, int h, uint flags)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(title);
        IntPtr p = Marshal.AllocHGlobal(utf8.Length + 1);   // 含 0 终止
        try
        {
            Marshal.Copy(utf8, 0, p, utf8.Length);
            Marshal.WriteByte(p, utf8.Length, 0);
            return SdlNative.Fn<SdlCreateWindowFn>(LiveModule(), "SDL_CreateWindow")(p, x, y, w, h, flags);
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>销毁窗口。落 LiveModule:本类创建的窗口属主即 LiveModule(创建落点定义属主,见
    /// CreateWindow 注),与 OwnerModule 探测结论恒同(首个活候选),无需再探测。</summary>
    internal static void DestroyWindow(IntPtr window) =>
        SdlNative.Fn<SdlDestroyWindowFn>(LiveModule(), "SDL_DestroyWindow")(window);

    /// <summary>最近一次 SDL 错误(SDL_error.h;线程局部静态缓冲,PtrToStringAnsi 即拷)。诊断用。</summary>
    internal static string GetError() =>
        Marshal.PtrToStringAnsi(SdlNative.Fn<SdlGetErrorFn>(LiveModule(), "SDL_GetError")()) ?? "";

    /// <summary>解码 SDL_TextInputEvent.text(char[32] UTF-8,0 终止,@12)并逐字符追加。零 unsafe:56B 联合体
    /// 栈拷贝后 MemoryMarshal 取字节视图(联合体不能含引用字段,AsBytes 合法);文本输入非热路径,UTF-8 解码
    /// 中间 string 分配可接受(对齐 Veldrid HandleTextInputEvent 的 GetCharCount/GetChars 语义)。</summary>
    internal static void AppendTextChars(in SdlEvent e, List<char> into)
    {
        SdlEvent copy = e;   // in 参数不可直接 CreateSpan,按值栈拷贝
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref copy, 1));
        ReadOnlySpan<byte> text = bytes.Slice(12, 32);
        int len = text.IndexOf((byte)0);    // 0 终止符;无终止按满 32B 截断(SDL 同上限)
        if (len < 0) len = text.Length;
        if (len == 0) return;
        foreach (char c in Encoding.UTF8.GetString(text[..len]))
            into.Add(c);
    }

    /// <summary>窗口属主 id(SDL_video.h L818:Uint32 SDL_GetWindowID;导出名大写 ID)。</summary>
    internal static uint GetWindowId(IntPtr window) =>
        SdlNative.Fn<SdlGetWindowIdFn>(SdlNative.OwnerModule(window), "SDL_GetWindowID")(window);

    /// <summary>窗口尺寸(逻辑/屏幕坐标,SDL_video.h L1011;高 DPI 像素尺寸是另一 API GetWindowSizeInPixels
    /// L1068,T2 ScaleFactor 语义再裁定)。</summary>
    internal static void GetWindowSize(IntPtr window, out int w, out int h) =>
        SdlNative.Fn<SdlGetWindowSizeFn>(SdlNative.OwnerModule(window), "SDL_GetWindowSize")(window, out w, out h);

    /// <summary>窗口所在显示器索引(SDL_video.h L643;失败 -1,SDL 语义)。</summary>
    internal static int GetWindowDisplayIndex(IntPtr window) =>
        SdlNative.Fn<SdlGetWindowDisplayIndexFn>(SdlNative.OwnerModule(window), "SDL_GetWindowDisplayIndex")(window);

    /// <summary>显示器 DPI(SDL_video.h L473;导出名 SDL_GetDisplayDPI —— 大写 DPI,vendored DLL 导出表
    /// 实测核对)。返回约定是 SDL 系少见的"0=成功/负数=失败"(L465-466 文档明言,非 SDL_bool;本机实测
    /// 2.0.8 副本返回 0 且写出 ddpi=96)。任一输出指针可为 null 的原生语义此处简化为全输出。</summary>
    internal static bool GetDisplayDpi(int index, out float ddpi, out float hdpi, out float vdpi) =>
        SdlNative.Fn<SdlGetDisplayDpiFn>(LiveModule(), "SDL_GetDisplayDPI")(index, out ddpi, out hdpi, out vdpi) == 0;

    static IntPtr LiveModule()
    {
        foreach (IntPtr h in SdlNative.Candidates())
            return h;   // 首个活候选(序同 SdlNative:Primary→ByName→Runtimes)
        throw new DllNotFoundException(
            "SDL2.dll 无活候选模块(基目录/runtimes/<rid>/native/程序集探测均未命中,探测序见 SdlNative)");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlPollEventFn(out SdlEvent e);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint SdlGetWindowIdFn(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlGetWindowSizeFn(IntPtr window, out int w, out int h);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlGetWindowDisplayIndexFn(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlGetDisplayDpiFn(int index, out float ddpi, out float hdpi, out float vdpi);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlCreateWindowFn(IntPtr title, int x, int y, int w, int h, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlDestroyWindowFn(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetErrorFn();
}
