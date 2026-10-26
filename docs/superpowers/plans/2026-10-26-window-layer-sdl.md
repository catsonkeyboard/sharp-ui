# 窗口/输入层自研(去 Veldrid.SDL2)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 自有 SDL2 绑定替换 Veldrid.SDL2(窗口/事件泵/输入快照/三枚举),删 Veldrid.SDL2+Newtonsoft 包,全仓 Veldrid 零残留。

**Architecture:** `src/SharpUI/Sdl/` 四文件分层(事件声明层→自有枚举→快照→SdlWindow);App.Run 全局单泵按 windowID 分发(弃每窗各泵);模块纪律复用 SdlNative 活模块路径。

**Tech Stack:** .NET 10、SDL2 2.30.8(vendored,仅换 C# 绑定)、xUnit。

**Spec:** `docs/superpowers/specs/2026-10-26-window-layer-design.md`(W-1~W-4 已批准)

## Global Constraints

- 零 `unsafe`;NativeAOT 兼容。
- SDL 常量/结构偏移以 SDL 2.30.8 官方头文件实数为准(下载源码包核对,哈希记 `native/SDL2/VERSIONS.md`;**offsetof/尺寸自检测试锁定**,同 D3D11/FT 纪律——禁止凭记忆)。
- SDL 模块纪律(裁定#38):窗口相关调用走 SdlNative 活模块路径;禁止新裸 DllImport("SDL2.dll")。
- 公共 API 形状保持:`KeyEventData(Key Key, bool Down, bool Shift, bool Ctrl = false)` 等签名零形变——Key 换为自有枚举(成员名对齐 Veldrid)。
- 语义保持:ScaleFactor=DPI/96(对齐现观察 ~1.588);flags Resizable|Shown(+GL 时 OpenGL);`Math.Clamp(0.5,4)` 不变;InputMapper 逻辑零改动(仅换入参类型)。
- **IME 复验硬门槛**:泵重构后拼音组合+提交实测回归(SDL_TEXTINPUT 经新泵到达 TextField)。
- 全量测试门槛:基线 226/226(迁移平移后总数守恒±新增);AOT ≤6.5MB。
- git 每任务至少一提交。

---

### Task 1: SdlEvents 声明层 + 自有 Key/MouseButton/ModifierKeys 枚举

**Files:**
- Create: `src/SharpUI/Sdl/SdlEvents.cs`、`src/SharpUI/Sdl/Key.cs`
- Test: `tests/SharpUI.Tests/SdlEventsTests.cs`

**Interfaces(Produces,T2/T3 消费):**

```csharp
namespace SharpUI;   // 根命名空间(公共枚举);SdlEvents internal

public enum Key { /* ~140 成员,名逐一对齐 Veldrid.Key(A..Z、D0..D9、Left/Right/Up/Down、BackSpace、
                    Enter、Escape、Tab、Delete、Home/End、PageUp/PageDown、F1..F12、ShiftLeft…全部);
                    生成法:反射枚举 Veldrid.Sdl2 程序集(包删前)→ 完整镜像 */ }
public enum MouseButton { Left, Middle, Right }   // 对齐 Veldrid
[Flags] public enum ModifierKeys { None = 0, Shift = 1, Control = 2, Alt = 4 }   // 对齐 Veldrid

internal static class SdlEvents
{
    // 事件类型常量(SDL_events.h 实数,注释出处):Quit=0x100、WindowEvent=0x200、KeyDown=0x300、
    // KeyUp=0x301、TextEditing=0x302、TextInput=0x303、MouseMotion=0x400、MouseButtonDown=0x401、
    // MouseButtonUp=0x402、MouseWheel=0x403
    // 窗口子类型(SDL_windowevents.h):Resized=5、SizeChanged=6、FocusGained=12、FocusLost=13、Close=14
    // KMOD(SDL_keycode.h):LShift=0x1、RShift=0x2、LCtrl=0x40、RCtrl=0x80、LAlt=0x100、RAlt=0x200
    // 按钮:左=1 中=2 右=3;SDL_POLL 已含 pump
    // 窗口 flags(SDL_video.h):Fullscreen=0x1、OpenGL=0x2、Shown=0x10、Hidden=0x20、Resizable=0x20
    //   ——各值以头文件为准,此处勿信计划记忆值

    /// <summary>SDL_Event(union,总 56B):显式布局重叠读,零 unsafe。
    /// 公共头:type@0(Uint32)、timestamp@4、windowID@8(各分支一致,SDL_CommonEvent)。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    internal struct SdlEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public uint WindowId;
        // 键盘分支:type/timestamp/windowID@8、state@12(byte)、repeat@13(byte)、keysym@16{scancode@16,sym@20,mod@24(U16),unused@28}
        [FieldOffset(12)] public byte KeyState;
        [FieldOffset(13)] public byte KeyRepeat;
        [FieldOffset(16)] public uint KeysymScancode;
        [FieldOffset(20)] public uint KeysymSym;
        [FieldOffset(24)] public ushort KeysymMod;
        // 文本输入分支:text@12(char[32] UTF-8,0 终止)
        [FieldOffset(12)] public byte TextInputByte0;   // 解码经 Marshal.PtrToStringAnsi(event+12) 或逐字节读
        // 鼠标分支:button@12(byte)、state@13、clicks@14、x@16(int)、y@20;motion:x@16/y@20/relx@24/rely@28、state@12(Uint32 按位)
        [FieldOffset(12)] public byte MouseButtonId;
        [FieldOffset(13)] public byte MouseButtonState;
        [FieldOffset(16)] public int MouseX;
        [FieldOffset(20)] public int MouseY;
        [FieldOffset(24)] public int MouseRelX;
        [FieldOffset(28)] public int MouseRelY;
        // 滚轮分支:x@16(int)、y@20、direction@24(Uint32)
        [FieldOffset(24)] public int WheelDirectionRaw;
        // 窗口分支:event@12(byte)、pad@13-15、data1@16(int)、data2@20(int)
        [FieldOffset(12)] public byte WindowEventCode;
        [FieldOffset(16)] public int WindowData1;
        [FieldOffset(20)] public int WindowData2;
    }
    // ——各 FieldOffset 以 SDL_events.h 各分支结构逐字段核数(纪律:头文件实测,自检测试锁定 SizeOf==56)

    /// <summary>keycode(SDLK_*)→ Key;mod 位 → ModifierKeys。</summary>
    internal static Key KeyFromSym(uint sym);
    internal static ModifierKeys ModifiersFromMod(ushort mod);
    internal static MouseButton MouseButtonFromId(byte id);   // 1/2/3 → Left/Middle/Right,其余 → Left

    internal static bool PollEvent(out SdlEvent e);           // SDL_PollEvent(经活模块)
    internal static uint GetWindowId(IntPtr window);          // SDL_GetWindowID
    internal static void GetWindowSize(IntPtr window, out int w, out int h);
    internal static int GetWindowDisplayIndex(IntPtr window);
    internal static bool GetDisplayDpi(int index, out float ddpi, out float hdpi, out float vdpi);
}
```

- [ ] **Step 1: 下载 SDL 2.30.8 官方源码包**(libsdl.org,哈希记 `native/SDL2/VERSIONS.md` 追加节)——全部常量/偏移对 `include/SDL_events.h`/`SDL_video.h`/`SDL_keycode.h` 核数,注释出处(行号)。
- [ ] **Step 2: 反射枚举 Veldrid.Key 全成员**(包删前,PowerShell `[Enum]::GetNames` 落清单入报告)→ 镜像生成 Key.cs。
- [ ] **Step 3: 写失败测试**

```csharp
using System.Runtime.InteropServices;
using SharpUI;

namespace SharpUI.Tests;

public class SdlEventsTests
{
    [Fact]
    public void SdlEvent_UnionSize_Is56() => Assert.Equal(56, Marshal.SizeOf<SdlEvents.SdlEvent>());

    [Theory]
    [InlineData(0x61u, Key.A)] [InlineData(0x30u, Key.D0)] [InlineData(0x4000004Fu, Key.Right)]
    [InlineData(0x08u, Key.Backspace)] [InlineData(0x0Du, Key.Enter)] [InlineData(0x1Bu, Key.Escape)]
    [InlineData(0x40000050u, Key.Up)] [InlineData(0x40000051u, Key.Down)]
    [InlineData(0x4000004Eu, Key.Left)] [InlineData(0x2Eu, Key.C)] [InlineData(0x2Fu, Key.X)]
    [InlineData(0x56u, Key.V)] [InlineData(0x4000004Cu, Key.Home)] [InlineData(0x4000004Du, Key.End)]
    [InlineData(0x4000004Eu, Key.PageUp)]   // ← 此行值系计划记忆示例,以头文件实数写对(PageUp 实为 0x4000004B)
    public void KeyFromSym_MapsSdlKeycodes(uint sym, Key expected) =>
        Assert.Equal(expected, SdlEvents.KeyFromSym(sym));

    [Fact]
    public void Modifiers_FromKmodBits() =>
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift,
            SdlEvents.ModifiersFromMod(0x40 | 0x1));   // KMOD_LCTRL|KMOD_LSHIFT,值以头文件为准

    [Fact]
    public void MouseButton_MapsIds()   // 1→Left 2→Middle 3→Right
    {
        Assert.Equal(MouseButton.Left, SdlEvents.MouseButtonFromId(1));
        Assert.Equal(MouseButton.Middle, SdlEvents.MouseButtonFromId(2));
        Assert.Equal(MouseButton.Right, SdlEvents.MouseButtonFromId(3));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void KeyEnum_MirrorsVeldridMembers()   // 包删前的镜像守卫(反射比对成员名集合)
    {
        var veldrid = Enum.GetNames<Veldrid.Key>().OrderBy(n => n).ToArray();
        var ours = Enum.GetNames<Key>().OrderBy(n => n).ToArray();
        Assert.Equal(veldrid, ours);
    }
}
```

(InlineData 的 keycode 十六进制值以 SDL_keycode.h 实数校正——上面几处标注了计划记忆值不可信;Baskspace/BackSpace 成员名以 Veldrid 实名为准。)

- [ ] **Step 4-6: 红→实现(常量/偏移全部头文件核数)→全量绿(226+新增)→提交**

```bash
git commit -m "feat(win): sdl event declaration layer + owned key/mouse/modifier enums"
```

---

### Task 2: InputSnapshot + SdlWindow + 全局泵分发

**Files:**
- Create: `src/SharpUI/Sdl/InputSnapshot.cs`、`src/SharpUI/Sdl/SdlWindow.cs`
- Test: `tests/SharpUI.Tests/SdlWindowTests.cs`

**Interfaces(Produces,T3 消费):**

```csharp
namespace SharpUI;

public sealed class InputSnapshot   // 成员名对齐 Veldrid 消费面(InputMapper 零形变迁移)
{
    public PointF MousePosition { get; }
    public IReadOnlyList<(MouseButton Button, bool Down)> MouseEvents { get; }
    public IReadOnlyList<(Key Key, bool Down, ModifierKeys Modifiers)> KeyEvents { get; }
    public bool IsMouseDown(MouseButton b);
    public float WheelDelta { get; }
    public IReadOnlyList<char> KeyCharPresses { get; }
}

public sealed class SdlWindow   // 替代 Veldrid.Sdl2.Sdl2Window;App/AppWindow 消费面同名
{
    public SdlWindow(string title, int x, int y, int width, int height, SdlWindowFlags flags);
    public IntPtr SdlWindowHandle { get; }
    public bool Exists { get; }          // Close()/窗口 Close 事件后 false
    public int Width { get; }            // SDL_GetWindowSize 现取
    public int Height { get; }
    public float ScaleFactor { get; }    // DPI/96(缓存,创建时定格,同现状语义)
    public event Action? Resized;        // 窗口 RESIZED 事件(尺寸>0)时触发
    public event Action? FocusLost;
    public void Close();                 // 置 closing 标记(销毁随 App 移除路径)
    public void Dispose();               // SDL_DestroyWindow(经活模块)

    internal uint WindowId { get; }
    internal InputSnapshot GetSnapshotAndReset();   // 全局泵累积的当帧快照,取后清空
    internal void MarkClosed();
}

[Flags] public enum SdlWindowFlags { None = 0, Fullscreen = 0x1, OpenGL = 0x2, Shown = 0x10,
    Hidden = 0x20, Resizable = 0x20 /*值以 SDL_video.h 实数为准——各标志位头文件核数*/ }

internal static class SdlPump   // 全局单泵(W-3)
{
    /// <summary>每帧一次:PumpAll → 按 WindowId 路由到注册窗口;QUIT→全部 MarkClosed。
    /// 鼠标位置默认传播:无窗口归属的 MOUSEMOTION 更新最近聚焦窗(与 Veldrid 行为对齐的务实近似,
    /// 报告论证;多窗归属精确化留 M3e 后半)。</summary>
    public static void PumpAll();
    internal static void Register(SdlWindow w); internal static void Unregister(SdlWindow w);
}
```

- [ ] **Step 1: 写失败测试**(SdlWindowTests,Gpu trait:真窗口;合成事件测路由):
  - 创建 64×64 隐藏窗 → WindowId≠0、Width/Height 回读、ScaleFactor∈(0.5,4];Dispose 二次幂等。
  - 全局路由:`SdlPump.PumpAll()` 后 `GetSnapshotAndReset()` 空快照;**合成注入接口**——为可单测,SdlWindow 加 `internal void InjectTest(in SdlEvents.SdlEvent e)`(与 PumpAll 同路由入口),测试构造 KeyDown/TextInput("A"/UTF-8 字节)/MouseMotion/MouseWheel 事件注入 → 快照断言(KeyEvents/KeyCharPresses/MousePosition/WheelDelta);两窗注册 → 各自只收本 WindowId 事件。
  - TextInput 解码:注入 text="你好" 的 UTF-8 字节 → KeyCharPresses == ['你','好']。
  - Resized/FocusLost 事件触发(注入 WINDOWEVENT Resized(640,480)/FocusLost → 事件回调用 TestSyncContext 或 bool 标志断言)。
- [ ] **Step 2-5: 红→实现→全量绿→提交**(SDL_CreateWindow/DestroyWindow/GetWindowSize/GetWindowDisplayIndex/GetDisplayDPI 经 SdlNative 活模块绑定扩展)

```bash
git commit -m "feat(win): owned input snapshot + sdl window + global pump dispatch"
```

---

### Task 3: App/AppWindow/InputMapper 迁移 + 测试平移 + 删包

**Files:**
- Modify: `src/SharpUI/App.cs`(CreateWindow 用 SdlWindow;Run 循环:帧首 `SdlPump.PumpAll()` 一次 + 各窗 `GetSnapshotAndReset()`;Exists/Close 对接)、`src/SharpUI/AppWindow.cs`(Window 类型)、`src/SharpUI/InputMapper.cs`(入参 InputSnapshot 自有类型;`MouseEvent`→元组)、`src/SharpUI/Focus.cs`/`Interaction.cs`/`TextEditing.cs`/`src/SharpUI/Elements/TextFieldElement.cs`(删 `using Veldrid;`——Key 已在 SharpUI 命名空间,引用零形变)
- Modify: tests(InputMapperTests/FocusTests/TextEditingTests/TextFieldTests/UiScopeTests 等改自有类型,断言零漂移)
- Modify: `src/SharpUI/SharpUI.csproj`(删 Veldrid.SDL2 + Newtonsoft.Json 两行)
- Test: 全量 + grep + AOT

- [ ] **Step 1: 迁移(机械面)**:`using Veldrid;` 全删;InputMapper.Map 签名换 `InputSnapshot`;MouseEvent/KeyEvent 元组化改写(逻辑零改动,逐行对照)。
- [ ] **Step 2: 全量测试**(平移后 226±,断言零漂移;`KeyEnum_MirrorsVeldridMembers` 镜像守卫测试此时**删除**——Veldrid 包已不在,守卫使命完成,报告记录)。
- [ ] **Step 3: 删两包 + grep 收账**:`dotnet list package --include-transitive` 无 Veldrid/无 NU1903;`grep -rni "veldrid" src/ examples/` 零命中(注意 Counter.csproj 的 VendorModernSdl2 注释提及 Veldrid 属注释性文字,一并清理措辞)。
- [ ] **Step 4: AOT 复测**:`dotnet publish -c Release -r win-x64 /p:PublishAot=true` → 体积实测(≤6.5MB 记录)+ 冒烟存活 5s。
- [ ] **Step 5: 提交**

```bash
git commit -m "feat(win): app on owned sdl layer, drop veldrid.sdl2 + newtonsoft"
```

---

### Task 4: 双后端窗口冒烟 + IME 实测回归 + 收官文档

**Files:**
- Test: `tests/SharpUI.Tests/WindowSmokeTests.cs`(Gpu)
- Modify: `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(裁定#43:窗口层自研定稿+全局泵+依赖清零收官[D1-D3 三借全还];依赖锁清单更新)、`docs/notes/m1-aot.md`(产物对比)、`docs/notes/perf-baselines.md`(如变)

- [ ] **Step 1: 双后端窗口 harness 测试**(模式同 GL harness:真窗+90 帧清屏+读回像素断言;D3D11 与 GL 各一;经 App.CreateWindow 全路径或直接 SdlWindow+Context 组装,取更全者)。
- [ ] **Step 2: IME 实测回归(硬门槛)**:Counter + `SHARPUI_TEST_IME_OPEN=1` + keybd_event 注入 "nihao"(复用 `artifacts/ime-test/type.ps1` 路线)→ 读回断言:输入框组合串出现+候选窗位于输入框旁(全屏 GDI 抓取+像素判读;桌面锁定则降级为组合串提交"你好"读回断言并如实记录)。**任何一步失败即 BLOCKED 上报**——泵重构不得破坏 IME 链路。
- [ ] **Step 3: Counter 交互手测项清单**(点击计数/键盘输入/滚动/拖窗 resize):桌面锁定时以 harness 读回+进程存活承证并记录,解锁后建议用户补目视。
- [ ] **Step 4: 文档收官 + 全量 + 提交**

```bash
dotnet test tests/SharpUI.Tests/SharpUI.Tests.csproj -v q
git commit -m "feat(win): dual-backend window smoke, ime regression green, d-closeout docs"
```

---

## Self-Review 结论(已执行)

- **Spec 覆盖**:W-1(SDL 保留)全局约束;W-2 Key 枚举→T1(镜像守卫测试+包删前反射清单);W-3 全局泵→T2;W-4 模块纪律→T2 绑定扩展;语义保持清单→T2(ScaleFactor/flags)+T3(迁移零形变);IME 复验→T4 硬门槛;验收清单→T3(grep/AOT)+T4(双后端/文档)。
- **占位符扫描**:计划中标注"以头文件实数为准"的常量值系**有意的实现期核对项**(纪律既定,勿信记忆值——含计划 InlineData 示例的两处已知可疑值);Key 枚举生成法(反射镜像)为确定性程序非 TBD。
- **类型一致性**:InputSnapshot/SdlWindow/SdlPump 签名 T2 定义、T3 消费一致;KeyEventData 签名零形变(T1 Key 同名成员保证)。
