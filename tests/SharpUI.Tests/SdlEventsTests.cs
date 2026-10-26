using System.Runtime.InteropServices;
using SharpUI;

namespace SharpUI.Tests;

/// <summary>T1:SdlEvents 声明层自检 + 映射单测。全部 keycode/KMOD/常量值以 SDL 2.30.8 官方头文件
/// 实数核数(来源 tarball sha256 与逐行出处见 native/SDL2/VERSIONS.md;计划稿记忆值校正处逐条注明)。
/// KC(X) = SDL_SCANCODE_TO_KEYCODE(X) = 0x40000000 | X(SDL_keycode.h L48)。
/// 镜像守卫为纯托管反射(不触原生模块),不带 Gpu trait;真模块绑定冒烟另测(Trait Gpu)。</summary>
public class SdlEventsTests
{
    [Fact]
    public void SdlEvent_UnionSize_Is56()   // SDL_events.h L617-644:union 尾部 padding 明言 56B
        => Assert.Equal(56, Marshal.SizeOf<SdlEvents.SdlEvent>());

    [Theory]
    // 字面量键(SDL_keycode.h SDL_Keycode 枚举,字母 sym 恒小写)
    [InlineData(0x61u, Key.A)]              // SDLK_a(L102)
    [InlineData(0x63u, Key.C)]              // SDLK_c(L104;计划稿 0x2E 系 Win32 VK_DELETE,校正)
    [InlineData(0x78u, Key.X)]              // SDLK_x(L125;计划稿 0x2F 系 Win32 VK_INSERT,校正)
    [InlineData(0x76u, Key.V)]              // SDLK_v(L123;计划稿 0x56 系 Win32 VK 'V',校正)
    [InlineData(0x30u, Key.Number0)]        // SDLK_0(L74)
    [InlineData(0x08u, Key.BackSpace)]      // SDLK_BACKSPACE(L56)
    [InlineData(0x0Du, Key.Enter)]          // SDLK_RETURN(L54)
    [InlineData(0x1Bu, Key.Escape)]         // SDLK_ESCAPE(L55)
    [InlineData(0x09u, Key.Tab)]            // SDLK_TAB(L57)
    [InlineData(0x20u, Key.Space)]          // SDLK_SPACE(L58)
    [InlineData(0x7Fu, Key.Delete)]         // SDLK_DELETE(L150,字面量 '\x7F',非 scancode 派生)
    // scancode 派生键(SDL_keycode.h L129-165 + SDL_scancode.h 实数)
    [InlineData(0x40000039u, Key.CapsLock)]     // KC(SCANCODE_CAPSLOCK=57)
    [InlineData(0x4000003Au, Key.F1)]           // KC(SCANCODE_F1=58)
    [InlineData(0x4000004Au, Key.Home)]         // KC(74;计划稿 0x4000004C 实为 SCANCODE_DELETE=76,校正)
    [InlineData(0x4000004Bu, Key.PageUp)]       // KC(75;计划稿 0x4000004E 实为 PAGEDOWN,简报已预告校正)
    [InlineData(0x4000004Du, Key.End)]          // KC(77)
    [InlineData(0x4000004Eu, Key.PageDown)]     // KC(78)
    [InlineData(0x4000004Fu, Key.Right)]        // KC(79)
    [InlineData(0x40000050u, Key.Left)]         // KC(80;计划稿 0x4000004E 与 PageUp 重复,校正)
    [InlineData(0x40000051u, Key.Down)]         // KC(81)
    [InlineData(0x40000052u, Key.Up)]           // KC(82;计划稿 0x40000050 实为 LEFT,校正)
    [InlineData(0x40000059u, Key.Keypad1)]      // KC(SCANCODE_KP_1=89)
    [InlineData(0x400000E0u, Key.ControlLeft)]  // KC(SCANCODE_LCTRL=224)
    [InlineData(0x400000E3u, Key.LWin)]         // KC(SCANCODE_LGUI=227)
    // 未映射 → Unknown(对齐 Veldrid.MapKey default 分支)
    [InlineData(0u, Key.Unknown)]
    [InlineData(0x4000015Au, Key.Unknown)]      // SDLK_SLEEP(睡眠键,Veldrid 未映射)
    public void KeyFromSym_MapsSdlKeycodes(uint sym, Key expected) =>
        Assert.Equal(expected, SdlEvents.KeyFromSym(sym));

    [Fact]
    public void Modifiers_FromKmodBits()   // KMOD 值:SDL_keycode.h L334-342
    {
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift,
            SdlEvents.ModifiersFromMod(0x40 | 0x1));        // KMOD_LCTRL|KMOD_LSHIFT
        Assert.Equal(ModifierKeys.Alt | ModifierKeys.Gui,
            SdlEvents.ModifiersFromMod(0x200 | 0x800));     // KMOD_RALT|KMOD_RGUI(右侧聚合同左侧)
        Assert.Equal(ModifierKeys.None, SdlEvents.ModifiersFromMod(0x1000 | 0x2000));  // NUM|CAPS 非修饰
    }

    [Fact]
    public void MouseButton_MapsIds()      // SDL_mouse.h L445-449:Left=1/Middle=2/Right=3/X1=4/X2=5
    {
        Assert.Equal(MouseButton.Left, SdlEvents.MouseButtonFromId(1));
        Assert.Equal(MouseButton.Middle, SdlEvents.MouseButtonFromId(2));
        Assert.Equal(MouseButton.Right, SdlEvents.MouseButtonFromId(3));
        Assert.Equal(MouseButton.Button1, SdlEvents.MouseButtonFromId(4));  // X1(Veldrid MapMouseButton 源码实证)
        Assert.Equal(MouseButton.Button2, SdlEvents.MouseButtonFromId(5));  // X2(同上)
        Assert.Equal(MouseButton.Left, SdlEvents.MouseButtonFromId(0));     // 未知 → Left(Veldrid default 分支)
    }

    [Theory]
    // SDL_Scancode 实数(SDL_scancode.h release-2.30.8,USB HID usage 序:A=4..Z=29、1=30..0=39)
    [InlineData(4u, Key.A)]                 // SDL_SCANCODE_A
    [InlineData(29u, Key.Z)]                // SDL_SCANCODE_Z
    [InlineData(30u, Key.Number1)]          // SDL_SCANCODE_1
    [InlineData(39u, Key.Number0)]          // SDL_SCANCODE_0
    [InlineData(40u, Key.Enter)]            // SDL_SCANCODE_RETURN
    [InlineData(44u, Key.Space)]            // SDL_SCANCODE_SPACE
    [InlineData(45u, Key.Minus)]            // SDL_SCANCODE_MINUS
    [InlineData(46u, Key.Plus)]             // SDL_SCANCODE_EQUALS
    [InlineData(53u, Key.Tilde)]            // SDL_SCANCODE_GRAVE(Veldrid 返回别名 Grave)
    [InlineData(57u, Key.CapsLock)]         // SDL_SCANCODE_CAPSLOCK
    [InlineData(58u, Key.F1)]               // SDL_SCANCODE_F1
    [InlineData(69u, Key.F12)]              // SDL_SCANCODE_F12
    [InlineData(76u, Key.Delete)]           // SDL_SCANCODE_DELETE
    [InlineData(79u, Key.Right)]            // SDL_SCANCODE_RIGHT
    [InlineData(82u, Key.Up)]               // SDL_SCANCODE_UP
    [InlineData(83u, Key.NumLock)]          // SDL_SCANCODE_NUMLOCKCLEAR
    [InlineData(87u, Key.KeypadPlus)]       // SDL_SCANCODE_KP_PLUS
    [InlineData(98u, Key.Keypad0)]          // SDL_SCANCODE_KP_0
    [InlineData(99u, Key.KeypadPeriod)]     // SDL_SCANCODE_KP_PERIOD
    [InlineData(100u, Key.NonUSBackSlash)]  // SDL_SCANCODE_NONUSBACKSLASH
    [InlineData(103u, Key.KeypadPlus)]      // SDL_SCANCODE_KP_EQUALS —— Veldrid 怪quirk:映 KeypadPlus(无 KeypadEquals 成员)
    [InlineData(115u, Key.F24)]             // SDL_SCANCODE_F24
    [InlineData(118u, Key.Menu)]            // SDL_SCANCODE_MENU
    [InlineData(224u, Key.ControlLeft)]     // SDL_SCANCODE_LCTRL
    [InlineData(231u, Key.RWin)]            // SDL_SCANCODE_RGUI
    // 未映射(Veldrid MapKey 无分支 → Unknown)
    [InlineData(0u, Key.Unknown)]
    [InlineData(50u, Key.Unknown)]          // SDL_SCANCODE_NONUSHASH(Veldrid 未映射)
    [InlineData(101u, Key.Unknown)]         // SDL_SCANCODE_APPLICATION
    [InlineData(102u, Key.Unknown)]         // SDL_SCANCODE_POWER
    [InlineData(156u, Key.Unknown)]         // SDL_SCANCODE_CLEAR(携带项:Veldrid 不映射)
    [InlineData(282u, Key.Unknown)]         // SDL_SCANCODE_SLEEP(携带项:Veldrid 不映射)
    public void KeyFromScancode_MapsSdlScancodes(uint scancode, Key expected) =>
        Assert.Equal(expected, SdlEvents.KeyFromScancode(scancode));

    /// <summary>历史(T1-T2):泵键路径(scancode)曾以反射直调旧窗口层(4.9.0)私有 MapKey 全值域
    /// 0..511 锁定镜像(实测其映射 117 键);连同 KeyEnum/MouseAndModifierEnums 两成员镜像守卫共 3 测试,
    /// 随 T3 删包使命完成退役(退役账目见 T3 报告)。锁定责任此后由本文件静态映射表 Theory 承担。</summary>

    // ---- 真模块加载冒烟(Trait Gpu:经 SdlNative 活模块绑定,实触 vendored SDL2.dll)----

    [Fact]
    [Trait("Category", "Gpu")]
    public void NativeEntryPoints_BindThroughLiveModule()
    {
        SharpUI.Backends.SdlNative.EnsureVideoInit();   // 全候选 SDL_Init(VIDEO),幂等
        // 前序 GL/窗口测试可能遗留未泵事件(本套件实测:全量序下队列非空)——先排空再断言空队列应答
        int drained = 0;
        while (SdlEvents.PollEvent(out _)) { drained++; if (drained > 1000) break; }
        Assert.False(SdlEvents.PollEvent(out _));       // 排空后应答 false,且导出解析不抛
        Assert.True(SdlEvents.GetDisplayDpi(0, out float ddpi, out _, out _));   // SDL_GetDisplayDPI(大写 DPI)
        Assert.True(ddpi > 0);                          // 主显示器诊断 DPI 恒为正
    }
}
