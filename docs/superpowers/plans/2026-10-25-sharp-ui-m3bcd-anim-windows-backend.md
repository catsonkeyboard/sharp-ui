# SharpUI M3b+c+d-i 联合计划(动画性能 / 多窗口集成 / 后端探针)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 动画系统 + 性能 ≤8KB/帧(D23);Interaction 会话化 + 多窗口 + 剪贴板 + 状态宽限清扫(D24/D25);渲染后端窄接口 + D3D11 直连探针(D27)。

**Architecture:** Anim 为墙钟补间字典(可注入时间源,不入 StateStore);布局数组深度 ThreadStatic 池化;App 拆为设备壳+AppWindow 表,服务定位静态改"当前窗口"语义;剪贴板走 SDL2 原生导出;GraphicsContext 公开面收束为 IRenderContext,D3D11 探针以独立类证明 P/Invoke 互操作闭环。

**Tech Stack:** 零新增包(原生 P/Invoke 内置)。

**Spec:** M3 阶段表 + D23~D27。

## Global Constraints

- 沿用全部既有约束;`dotnet test` 全绿为提交前置;既有测试因签名会话化/接口化所需的调整**仅限机械传参**,断言语义零改动并在报告列明;新增公开成员仅限 Interfaces;预算:Counter 场景 ≤8192B/帧(M3b 后生效)。

---

## 阶段 M3b(动画与性能)

### Task 1: Anim 补间系统

**Files:** Create `src/SharpUI/Anim.cs`;Modify `src/SharpUI/App.cs`(App.Time 每帧更新+Anim 默认时间源)
**Interfaces:** `static class Anim`:`internal static Func<double>? TimeSource`(默认 `(App.Time)`);`double Now`;`float Get(string id, float target, float duration = 0.2f, Easing easing = Easing.EaseOutCubic)`;`bool Blink(float period = 1.06f, float duty = 0.5f)`;`enum Easing { Linear, EaseOutQuad, EaseOutCubic }` + `static float Apply(Easing, float t)`;`public static float Time`(App 侧,秒)。

**Anim.cs 完整实现:**

```csharp
namespace SharpUI;

public enum Easing { Linear, EaseOutQuad, EaseOutCubic }

public static class Anim
{
    sealed class Tween { public float From, To; public double Start; }
    static readonly Dictionary<string, Tween> _tweens = new();

    /// <summary>测试注入;null 时用 App.Time(D23)。</summary>
    internal static Func<double>? TimeSource;

    public static double Now => TimeSource?.Invoke() ?? App.Time;

    /// <summary>取补间当前值;目标变化时从当前值重启(D23)。</summary>
    public static float Get(string id, float target, float duration = 0.2f, Easing easing = Easing.EaseOutCubic)
    {
        double now = Now;
        if (!_tweens.TryGetValue(id, out Tween? t))
        {
            _tweens[id] = t = new Tween { From = target, To = target, Start = now };
            return target;
        }
        if (MathF.Abs(t.To - target) > 0.0001f)
        {
            t.From = ValueAt(t, now, duration, easing);
            t.To = target;
            t.Start = now;
        }
        return ValueAt(t, now, duration, easing);
    }

    static float ValueAt(Tween t, double now, float duration, Easing easing)
    {
        if (duration <= 0f) return t.To;
        float p = (float)Math.Clamp((now - t.Start) / duration, 0.0, 1.0);
        return t.From + (t.To - t.From) * Apply(easing, p);
    }

    public static bool Blink(float period = 1.06f, float duty = 0.5f) =>
        (Now % period) / period < duty;

    public static float Apply(Easing e, float t) => e switch
    {
        Easing.Linear => t,
        Easing.EaseOutQuad => 1f - (1f - t) * (1f - t),
        Easing.EaseOutCubic => 1f - (1f - t) * (1f - t) * (1f - t),
        _ => t,
    };
}
```

App.cs:公开 `public static float Time { get; private set; }`;Run 循环首行 `Time = (float)_stopwatch.Elapsed.TotalSeconds;`(新增 readonly Stopwatch 字段,Run 首次进入时 Start——用 `if (!_stopwatch.IsRunning) _stopwatch.Start();`)。

**AnimTests.cs(追加 5 测试,注入时间源 0/0.1/0.2/…):**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class AnimTests
{
    double _t;
    public AnimTests() => Anim.TimeSource = () => _t;

    [Fact]
    public void Get_ReturnsTargetImmediatelyOnFirstCall()
    {
        _t = 0;
        Assert.Equal(5f, Anim.Get("a", 5f));
    }

    [Fact]
    public void Get_TweensTowardNewTarget()
    {
        _t = 0; Anim.Get("b", 0f);
        _t = 0.1; float mid = Anim.Get("b", 10f, 0.2f);      // 50% EaseOutCubic = 1-(0.5)³=0.875 → 8.75
        Assert.InRange(mid, 8.5f, 9.0f);
        _t = 0.2; Assert.Equal(10f, Anim.Get("b", 10f, 0.2f), 3);
    }

    [Fact]
    public void TargetChange_RestartsFromCurrent()
    {
        _t = 0; Anim.Get("c", 0f);
        _t = 0.1; float mid = Anim.Get("c", 10f, 0.2f);      // ~8.75
        _t = 0.2; float after = Anim.Get("c", 20f, 0.2f);    // 从 8.75 出发 0%(EaseOutCubic 起点即原值)
        Assert.Equal(mid, after, 2);
    }

    [Fact]
    public void Blink_IsStatelessClockMath()
    {
        _t = 0.0; Assert.True(Anim.Blink());
        _t = 0.8; Assert.False(Anim.Blink());
        _t = 1.06; Assert.True(Anim.Blink());
    }

    [Fact]
    public void Easing_Monotonic()
    {
        for (float t = 0; t <= 1f; t += 0.25f)
            Assert.InRange(Anim.Apply(Easing.EaseOutCubic, t), 0f, 1f);
        Assert.True(Anim.Apply(Easing.EaseOutQuad, 0.5f) >= 0.5f);
    }
}
```

**注意**:AnimTests 是新静态写入方(Anim._tweens),加 `[Collection("interaction")]`(惯例)。测试 Dispose 复位 TimeSource?xUnit 无 Dispose——用类实现 IDisposable 复位 `Anim.TimeSource = null`。
- [ ] TDD → 全量 139/139 → 提交 `feat: wall-clock tween animation system`

### Task 2: 消费者(按钮悬停过渡 + 光标闪烁)

**Files:** Modify `src/SharpUI/Elements/ButtonElement.cs`、`src/SharpUI/Elements/TextFieldElement.cs`
**Interfaces:** ButtonElement.Draw 用 `float h = Anim.Get("hover:" + ResolvedId, pc.Input.HoveredId == ResolvedId ? 1f : 0f, 0.12f);` 在常态蓝 0x0C63E4 与悬停蓝 0x3B82F6 间线性插值(按压/聚焦分支保持原样,优先级不变;插值只影响 hover↔normal);光标四边形包在 `Anim.Blink()` 内。
**颜色插值 helper**:`Color32.Lerp(Color32 a, Color32 b, float t)` 新增到 Color32.cs(R/G/B/A 字节线性;公开,Interfaces 列明)。测试:Color32.Lerp 端点/中点 2 个纯测试(AppendTo Color32Tests);视觉归手测。
- [ ] 实现 → 全量 141/141 → 冒烟(按钮悬停淡入、光标闪)→ 提交 `feat: hover transition and cursor blink via anim`

### Task 3: 性能波(≤8192B/帧)

**Files:** Modify `src/SharpUI/Layout.cs`(深度 ThreadStatic 数组池)、`src/SharpUI/Elements/TextFieldElement.cs`(static Empty + 缓存键)、`tests/SharpUI.Tests/PerfTests.cs`(预算断言 65536→8192)
**实现要点**:
- Layout:Measure/Arrange/Scroll 的 5 数组改为从 `FlexArrays.Rent(depth)` 取(新 internal static class,ThreadStatic `float[][]`/`int[][]` 按**调用深度**增长;ComputeLines 及两个调用方在入口 `_depth++` try/finally `--_depth`——ComputeLines 递归经 Measure(child) 重入,深度隔离即安全)。Scroll/VirtualList 的 ArrangeChildren 内联数组同样走池。
- TextFieldElement:`internal static readonly TextState EmptyState = new();` State 用 `Get(key, EmptyState)`;`string Key => "tf:" + ResolvedId` 缓存(字段,ResolvedId 变化即重建——每帧 id 稳定,比较后复用)。
- PerfTests:`Assert.True(bpf <= 8192, ...)` 替换 Counter 场景断言(VirtualList10k 场景单独保留 64KB 预算)。
- 若 Release 实测仍 >8192:追加元素自由列表(ElementPool:Get<T>/Put,Frame 后回收 Panel/Text/Spacer/TextField 实例,Key/Style 重置)——此为**条件步骤**,触发时先报数再实施。
- [ ] Release 记录数字(≤8192 必须)→ Debug 141/141 → 提交 `feat: depth-pooled layout arrays and m3 perf budget 8kb`

### Task 4: M3b 收官
- [ ] Release 门禁 + 冒烟 + 基线提交 `test: m3b perf baseline`(记录数字)

---

## 阶段 M3c(多窗口与集成)

### Task 5: Interaction 会话化

**Files:** Modify `src/SharpUI/Interaction.cs`、全部调用点(App/测试)
**Interfaces:** `public sealed class InputSession { internal string? PressedOn; internal readonly List<string> Clicks = new(); internal readonly List<string> Queued = new(); }`;`Interaction.Update(InputSession session, Element root, in RawInput raw, FocusManager? focus = null)`;`QueueClick(InputSession, string)`;`CancelPress(InputSession)`;InputState 不变。全部既有测试机械加 `var s = new InputSession();` 首参(断言零改动,报告列明)。
- [ ] TDD(会话隔离测试:两个 session 独立按压)→ 全量(测试数+1)→ 提交 `refactor: interaction statics to per-window input sessions`

### Task 6: App 多窗口重构

**Files:** Rewrite `src/SharpUI/App.cs`;Create `src/SharpUI/AppWindow.cs`
**Interfaces:**
- `public sealed class AppWindow { internal Sdl2Window Window; internal GraphicsContext Ctx; internal Renderer Renderer; internal TextAtlas Atlas; internal FocusManager Focus; internal InputSession Session; internal Element? LastTree; public Func<Element> Render; }`
- App:`Create(title,w,h,render)`(主窗,Run 退出于主窗关闭)、`public AppWindow OpenWindow(string title, int w, int h, Func<Element> render)`(Run 循环内注册,下一帧纳入)、`Run(maxFrames)` 每帧:遍历窗口表 → 每窗:PumpEvents→InputMapper.Map→session.Update(LastTree)→Scroll.Dispatch→FeedFocused→**App.Atlas=该窗.Atlas;UiContext.CurrentInput=…**→构建→Assign→focus→Arrange→Draw→Submit→Swap→State.Sweep(全局一次/帧)→LastTree;窗 !Exists 移除;主窗关闭 break。VSync/FocusLost(→session.CancelPress)每窗挂。
- 现有单窗行为与 API(App.Create/Run/ScaleFactor/App.State/App.Atlas)完全保持(demo 零改动)。
**测试**:AppWindowBookkeeping 纯逻辑单测难(需真窗)——以 maxFrames 冒烟 + review 为主;1 个纯单测:OpenWindow 在 Run 前调用入队逻辑(内部 List 可查 Count)——internal 可见性。
- [ ] 全量绿 → 双窗冒烟(临时 demo 代码开第二窗,maxFrames 60,两窗闪现退出)→ 恢复 → 提交 `feat: multi window app with per window sessions and contexts`

### Task 7: 剪贴板 + TextEditing 粘贴/剪切

**Files:** Create `src/SharpUI/Clipboard.cs`;Modify `src/SharpUI/TextEditing.cs`(Paste/CutResult 纯函数)、`src/SharpUI/Elements/TextFieldElement.cs`(Feed 前置 Ctrl+C/X/V)
**Interfaces:** `public static class Clipboard { public static void SetText(string s); public static string GetText(); }`(SDL2 P/Invoke:SDL_SetClipboardText/SDL_GetClipboardText→Marshal.PtrToStringAnsi,已探针);`TextEditing.Paste(TextState, string text) → TextState`(替换选区,光标至末)、`CutCopy(TextState) → (TextState After, string? Copied)`(有选区:After=删除+塌陷,Copied=选区文本;无选区:(原态,null))。Feed:Ctrl+C 仅复制;Ctrl+X 剪;Ctrl+V 粘(均先于 Apply;Ctrl+A 逻辑不动)。
**测试**:Paste/CutCopy 纯测 4 个(空剪贴板串、选区替换、无选区剪切成原态);剪贴板原生往返 1 个(真机,`[Trait("Category","Gpu")]`——其实无 GPU,同惯例隔离:SetText("x")+GetText 断言往返;注意并行集合)。
- [ ] TDD → 全量(+5)→ 提交 `feat: clipboard with sdl native bridge and paste cut semantics`

### Task 8: StateStore 宽限清扫(修 VirtualList 丢字)

**Files:** Modify `src/SharpUI/State.cs`、`src/SharpUI/App.cs`(每帧 `State.Tick()`)
**Interfaces:** `StateStore.Tick()`(internal 帧计数+1);条目记帧;Sweep 只清 `当前帧-最后触碰 > 300` 的键(D25);既有 StateTests 零改动必须仍绿(现有测试 Sweep 语义 = 立即清——**冲突**:改宽限后旧测试 `Sweep_RemovesUntouched` 红!裁定:测试改为两次 Sweep 间 `Tick×301` 循环触发宽限过期,断言语义不变仅机械补 Tick 循环,报告列明)。
**测试**:+2(宽限内保留、301 帧后清除)。
- [ ] TDD → 全量(+2)→ 提交 `feat: grace sweep keeps virtualized text state for five seconds`

### Task 9: M3c 收官(IME 评估文档 + 冒烟)
- [ ] `docs/notes/ime-evaluation.md`:现状(KeyCharPresses 承载提交文本已可用)、SDL2 组合窗局限(无 SDL_SetTextInputRect 于 2.0.16+?探针备注)、Windows 定位候选窗需 IMM32/TSF(M3e 平台层)、结论与触发条件——照此提纲 8~12 行成文
- [ ] 冒烟(双窗 + 输入框 Ctrl+C/V 往返)→ 提交 `docs: ime evaluation and m3c closeout`

---

## 阶段 M3d-i(后端接口 + D3D11 探针)

### Task 10: IRenderContext 抽象

**Files:** Create `src/SharpUI/RenderContext.cs`(接口);Modify `src/SharpUI/GraphicsContext.cs`(实现接口)、`src/SharpUI/Renderer.cs`(依赖接口)
**Interfaces:** `public interface IRenderContext { Framebuffer MainFramebuffer { get; } void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint w, uint h); }`——**薄到 Renderer 实际所需**;GraphicsContext 实现之(AppWindow 持 IRenderContext 类型);Renderer ctor/Submit 收 IRenderContext。Veldrid 类型(Framebuffer 参数)仍暴露=既定 D12 债。RendererTests/TextAtlas 构造不变(GraphicsContext 具体类仍可用)。
**测试**:无新测试(纯结构性;全量绿即证)。
- [ ] 全量绿 → 提交 `refactor: renderer behind thin irendercontext abstraction`

### Task 11: D3D11 直连探针(清屏呈现闭环)

**Files:** Create `src/SharpUI/Backends/D3D11Spike.cs`、`tests/SharpUI.Tests/D3D11SpikeTests.cs`(Gpu)
**实现要点(P/Invoke 全安全模式,零 unsafe;完整互操作表模式)**:
- vtable 调用模式:`static T Vfn<T>(IntPtr comObj, int slot)`? — 安全做法:`Marshal.GetObjectForIUnknown`? 不行。**模式**:`Marshal.ReadIntPtr(comObj, 0)` 得 vtable;`Marshal.ReadIntPtr(vtable, slot*IntPtr.Size)` 得函数指针;`Marshal.GetDelegateForFunctionPointer<T>` 得委托;逐接口封装为静态委托表(IUnknown:3 slot;ID3D11Device:QueryInterface/ AddRef/Release/ CreateBuffer/…/CreateTexture2D(槽位表以 d3d11.h 顺序硬编码注释)+CreateRenderTargetView 等;IDXGIFactory:EnumAdapters/CreateSwapChain(槽 7/…);IDXGISwapChain:Present(槽 8));**精确槽位以探针运行验证为准:探针测试先创建设备→校验 Release 槽(3)往返引用计数,任何槽位错误立即显式失败**。
- 最小闭环:CreateDXGIFactory1→EnumAdapters(0)→D3D11CreateDevice(驱动类型硬解:传 adapter;flags=0;BGRA 支持 flag 0x20)→IDXGIFactory2?不:CreateSwapChain(legacy,DXGI_SWAP_CHAIN_DESC:BufferDesc 640×480 Format=28(B8G8R8A8) SampleDesc 1/0 BufferUsage=32(RenderTargetOutput) BufferCount=2 OutputWindow=SDL 窗句柄(Sdl2Window 有 Win32Handle? 探针:`window.Win32Handle`? Veldrid.Sdl2 Sdl2Window:有 `WindowHandle`? 以反射为准——探针步骤) Windowed=1 SwapEffect=0)→GetBuffer(0)→CreateRenderTargetView→OMSetRenderTargets→ClearView(RGBA 轮转)→Present(1,0)。
- `D3D11Spike.RunFrame(IntPtr hwnd, float r, float g, float b) → bool`(创建惰性静态状态;返回呈现成功)。
- 测试(Gpu):真窗(Sdl2Window 640×480 隐藏?无隐藏 API——小窗闪现)+ 60 帧颜色轮转 Present 成功 + 引用计数守恒(Release 前后 IUnknown AddRef 计数)断言。
- [ ] 探针槽位验证(失败即修表,硬编码注释记录 d3d11.h 依据)→ 测试绿 → 全量 → 提交 `spike: direct d3d11 interop clear-present loop`

### Task 12: 收官
- [ ] 全量 Release + 冒烟 + 基线;提交 `test: m3bcd closeout baseline`

---

## 依赖:T1→T2→T3→T4→T5→T6→T7→T8→T9→T10→T11→T12
## 完成定义:全量绿(约 158±);Counter ≤8192B/帧;双窗/剪贴板/宽限保留/D3D11 探针全部有测试或冒烟;规格 D23-D27 全兑现。
