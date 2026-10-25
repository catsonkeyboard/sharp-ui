# SharpUI IME 候选窗激活 热修计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** TextField 聚焦时激活 SDL 文本输入模式(`SDL_StartTextInput`/`SDL_SetTextInputRect`/`SDL_StopTextInput` 原生导出,已探针验证),使中文 IME 候选窗在光标附近显示;失焦关闭。

**Architecture:** 纯状态机 `TextInputController`(三注入委托,全单测)+ 原生桥 `SdlTextInput`;App.Run 每窗在 Arrange 之后(帧坐标就绪)喂控制器:聚焦元素为 TextField 时以 `CursorRectLogical()×Scale` 更新候选窗位置。preedit 内联显示仍属 Veldrid 缺口(文档不动结论,只补状态)。

**Spec:** D22/D25 先例(原生 SDL 导出桥);docs/notes/ime-evaluation.md 预言的缺口即此项。

## Global Constraints
- 沿例;150 既有测试零改动绿;新增公开成员仅 Interfaces 所列;零 unsafe;AOT 友好。

---

### Task 1: IME 候选窗(桥 + 状态机 + 接线 + 测试 + 文档)

**Files:**
- Create: `src/SharpUI/TextInput.cs`、`tests/SharpUI.Tests/TextInputControllerTests.cs`
- Modify: `src/SharpUI/Elements/TextFieldElement.cs`(CursorRectLogical)、`src/SharpUI/App.cs`(接线)、`docs/notes/ime-evaluation.md`(状态行)

**Interfaces:**
- `public static class SdlTextInput { public static void Start(IntPtr sdlWindow); public static void Stop(IntPtr sdlWindow); public static void SetRect(IntPtr sdlWindow, int x, int y, int w, int h); }`(SDL2.dll cdecl;SDL_SetTextInputRect(SDL_Window*, SDL_Rect*) — 已探针)
- `public sealed class TextInputController`:`ctor(Action<IntPtr> start, Action<IntPtr> stop, Action<IntPtr,int,int,int,int> setRect)`;`Update(IntPtr window, string? focusedTextFieldId, (int X,int Y,int W,int H)? cursorRectPixels)`(每帧:聚焦→未激活则 Start+置 id;每帧 SetRect 同步;失焦/无 rect→已激活则 Stop;窗口句柄变化→Stop 后按新窗 Start)
- `TextFieldElement.CursorRectLogical() : RectF?`(internal;空帧 null;`Frame.X+PadX+前缀文本宽`(App.Atlas 测量,Cursor 钳制),`Frame.Y+2, 宽1, 高 Frame.Height-4`)
- `AppWindow` 增 `internal readonly TextInputController TextInput = new(SdlTextInput.Start, SdlTextInput.Stop, SdlTextInput.SetRect);`;App.Run 每窗 **Arrange 之后**接线(见步骤);窗口移除处与主循环退出后 `TextInput.Update(IntPtr.Zero, null, null)`(确保 Stop)

**Step 1 — 失败测试(6,纯委托假体):**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class TextInputControllerTests
{
    sealed class Recorder : IDisposable
    {
        public readonly List<string> Calls = new();
        public IntPtr Win = (IntPtr)1234;
        public TextInputController Make() => new(
            w => Calls.Add($"start:{w}"),
            w => Calls.Add($"stop:{w}"),
            (w, x, y, pw, ph) => Calls.Add($"rect:{x},{y},{pw},{ph}"));
        public void Dispose() { }
    }

    [Fact] public void GainFocus_StartsOnceThenSyncsRectEachFrame()
    {
        using var r = new Recorder();
        var c = r.Make();
        c.Update(r.Win, "tf:/edit", (10, 20, 2, 24));
        c.Update(r.Win, "tf:/edit", (12, 20, 2, 24));
        Assert.Equal(new[] { "start:1234", "rect:10,20,2,24", "rect:12,20,2,24" }, r.Calls);
    }

    [Fact] public void LoseFocus_StopsOnce()
    {
        using var r = new Recorder();
        var c = r.Make();
        c.Update(r.Win, "tf:/edit", (10, 20, 2, 24));
        c.Update(r.Win, null, null);
        c.Update(r.Win, null, null);
        Assert.Equal(new[] { "start:1234", "rect:10,20,2,24", "stop:1234" }, r.Calls);
    }

    [Fact] public void FocusedButNullRect_Stops()
    {
        using var r = new Recorder();
        var c = r.Make();
        c.Update(r.Win, "tf:/edit", (1, 2, 3, 4));
        c.Update(r.Win, "tf:/edit", null);
        Assert.Contains("stop:1234", r.Calls);
    }

    [Fact] public void IdleBeforeGain_NoCalls()
    {
        using var r = new Recorder();
        r.Make().Update(r.Win, null, null);
        Assert.Empty(r.Calls);
    }

    [Fact] public void Regain_AfterStop_StartsAgain()
    {
        using var r = new Recorder();
        var c = r.Make();
        c.Update(r.Win, "a", (0, 0, 1, 1));
        c.Update(r.Win, null, null);
        c.Update(r.Win, "a", (5, 6, 1, 1));
        Assert.Equal(2, r.Calls.Count(s => s.StartsWith("start")));
    }

    [Fact] public void WindowSwitch_StopsOldThenStartsNew()
    {
        using var r = new Recorder();
        var c = r.Make();
        IntPtr w2 = (IntPtr)5678;
        c.Update(r.Win, "a", (0, 0, 1, 1));
        c.Update(w2, "b", (0, 0, 1, 1));
        Assert.Equal(new[] { "start:1234", "rect:0,0,1,1", "stop:1234", "start:5678", "rect:0,0,1,1" }, r.Calls);
    }
}
```

**Step 2 — RED → 实现 TextInput.cs(状态机如上架构节;SdlRect 结构体内部私有)**

**Step 3 — TextFieldElement.CursorRectLogical(缩进同文件;复用 State/Cursor 钳制/App.Atlas null 安全)**

**Step 4 — App 接线:AppWindow 字段 + Run 每窗 `LayoutEngine.Arrange(...)` 之后:**

```csharp
            TextFieldElement? tf = focus.FocusedThisFrame as TextFieldElement;
            w.TextInput.Update(w.Window.SdlWindowHandle, tf?.ResolvedId,
                tf is null || tf.CursorRectLogical() is not { } cr ? null
                : ((int)(cr.X * w.Scale), (int)(cr.Y * w.Scale),
                   Math.Max(1, (int)(cr.Width * w.Scale)), (int)(cr.Height * w.Scale)));
```
窗口移除处与主循环退出后各补一次 `w.TextInput.Update(IntPtr.Zero, null, null);`。Sdl2Window 句柄成员名以编译为准(`SdlWindowHandle`,M3c 探针已证存在)。

**Step 5 — ime-evaluation.md 追加状态行**:`- 2026-10-25 修复:候选窗激活已落地(App 经原生 SDL_StartTextInput/SetTextInputRect 桥接,用户报告的"候选框不显示"即此缺口);preedit 内联显示仍待 Veldrid 绑定缺口。`

**Step 6 — 全量 156/156(150+6);PerfTests Release 确认无回归;Counter 冒烟(30 帧闪退恢复)→ 提交** `fix: activate sdl text input so ime candidate window shows`

## 完成定义:156/156;控制器 6 测试绿;候选窗可见性归用户手测(输入框打拼音→候选窗应出现在光标附近;失焦后系统 IME 恢复普通状态)。
