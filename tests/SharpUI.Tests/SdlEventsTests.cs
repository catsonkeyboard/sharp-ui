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
    public void MouseButton_MapsIds()      // SDL_mouse.h L445-449:Left=1/Middle=2/Right=3
    {
        Assert.Equal(MouseButton.Left, SdlEvents.MouseButtonFromId(1));
        Assert.Equal(MouseButton.Middle, SdlEvents.MouseButtonFromId(2));
        Assert.Equal(MouseButton.Right, SdlEvents.MouseButtonFromId(3));
        Assert.Equal(MouseButton.Left, SdlEvents.MouseButtonFromId(0));   // 契约:其余 → Left
        Assert.Equal(MouseButton.Left, SdlEvents.MouseButtonFromId(4));   // X1(Veldrid 映 Button1,T2 泵语义裁定)
    }

    // ---- 镜像守卫(包删前有效;T3 删 Veldrid.SDL2 时随使命完成退役)----
    // 成员名+值全比对:防名字镜像而值漂移。

    [Fact]
    public void KeyEnum_MirrorsVeldridMembersAndValues()
    {
        var veldrid = Enum.GetNames<Veldrid.Key>()
            .Zip(Enum.GetValues<Veldrid.Key>().Cast<int>(), (n, v) => (n, v)).OrderBy(t => t.n).ToArray();
        var ours = Enum.GetNames<Key>()
            .Zip(Enum.GetValues<Key>().Cast<int>(), (n, v) => (n, v)).OrderBy(t => t.n).ToArray();
        Assert.Equal(147, ours.Length);
        Assert.Equal(veldrid, ours);
    }

    [Fact]
    public void MouseAndModifierEnums_MirrorVeldridMembersAndValues()
    {
        Assert.Equal(
            Enum.GetNames<Veldrid.MouseButton>().Zip(Enum.GetValues<Veldrid.MouseButton>().Cast<int>(), (n, v) => (n, v)).OrderBy(t => t.n),
            Enum.GetNames<MouseButton>().Zip(Enum.GetValues<MouseButton>().Cast<int>(), (n, v) => (n, v)).OrderBy(t => t.n));
        Assert.Equal(
            Enum.GetNames<Veldrid.ModifierKeys>().Zip(Enum.GetValues<Veldrid.ModifierKeys>().Cast<int>(), (n, v) => (n, v)).OrderBy(t => t.n),
            Enum.GetNames<ModifierKeys>().Zip(Enum.GetValues<ModifierKeys>().Cast<int>(), (n, v) => (n, v)).OrderBy(t => t.n));
    }

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
