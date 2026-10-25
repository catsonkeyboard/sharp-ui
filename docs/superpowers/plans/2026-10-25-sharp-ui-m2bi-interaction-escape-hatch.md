# SharpUI M2b-i 交互内核与即时模式逃生舱 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 管线重排为"上帧几何交互 + 点击账本"(D13),InputState id 化,InputMapper 可测化,StateStore.Set 与 state↔id 桥(D14),UiScope 即时模式逃生舱,焦点硬化与同键碰撞检测。

**Architecture:** 每帧顺序变为:PumpEvents → InputMapper.Map → **Interaction.Update(上帧保留树)**(点击/悬停/按压全在此结算,产出 id 账本)→ UiContext.CurrentInput = input → 构建(声明式或 UiScope)→ Assign → focus.BeginFrame/Register(本帧树)→ focus.HandleKeys → Arrange → Draw(视觉门按 id)→ Submit → Swap → State.Sweep → 保留本帧树。UiScope 把自身路径段烤成元素 Key,与 IdResolver 公式天然一致(D14)。

**Tech Stack:** 不变。零新增包。

**Spec:** `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(M2 阶段表 + D13/D14/D15 为本计划依据)。

## Global Constraints

- 沿用全部既有约束:namespace SharpUI/SharpUI.Tests;禁止 unsafe;英文 Conventional Commits;main;每任务一提交;`dotnet test` 全绿为提交前置。
- 性能预算不放宽(≤64KB/帧、<8ms,Release);PerfTests 场景须计入本计划新增管线(T7)。
- 既有测试修改最小化:InteractionTests/FocusTests 因 InputState/Interaction 语义演进所需的调整,仅限构造调用形态与字段名,断言语义不得改;调整须在提交信息或报告中列明。
- 新增公开成员仅限各任务 Interfaces 节所列。
- 版本锁定不变。

---

### Task 1: StateStore.Set

**Files:**
- Modify: `src/SharpUI/State.cs`(追加 Set)
- Test: `tests/SharpUI.Tests/StateTests.cs`(追加 2)

**Interfaces:**
- Produces: `StateStore.Set<T>(string id, T value) → void`(触碰 + 写入)

- [ ] **Step 1: 追加失败测试**

```csharp
    [Fact]
    public void Set_PersistsValueTypesAcrossSweeps()
    {
        var store = new StateStore();
        store.Set("count", 41);
        store.Sweep();
        Assert.Equal(41, store.Get("count", 0));
        store.Set("count", store.Get("count", 0) + 1);
        store.Sweep();
        Assert.Equal(42, store.Get("count", 0));
    }

    [Fact]
    public void Set_OverwritesAndTouchesExisting()
    {
        var store = new StateStore();
        store.Get<int>("n");
        store.Sweep();
        store.Set("n", 7);
        store.Sweep();
        Assert.Equal(7, store.Get("n", 0));
        Assert.Equal(1, store.Count);
    }
```

- [ ] **Step 2: 运行确认失败** — Run: `dotnet test --filter FullyQualifiedName~StateTests` → FAIL(Set 未定义)。

- [ ] **Step 3: 实现(State.cs,Get 重载之后)**

```csharp
    /// <summary>写入并触碰(值类型状态的持久化路径;规格书 M2b)。</summary>
    public void Set<T>(string id, T value)
    {
        _touched.Add(id);
        _values[id] = value;
    }
```

- [ ] **Step 4: 运行确认通过 + 全量 70/70** → Run: `dotnet test`
- [ ] **Step 5: 提交** `git add -A && git commit -m "feat: state store set for value types"`

---

### Task 2: InputMapper(可测快照映射)

**Files:**
- Create: `src/SharpUI/InputMapper.cs`
- Modify: `src/SharpUI/Interaction.cs`(RawInput 加 LeftHeld 第四参,默认 false)
- Test: `tests/SharpUI.Tests/InputMapperTests.cs`

**Interfaces:**
- Produces(Task 3/5 消费):
  - `static class InputMapper`:`Map(InputSnapshot) → RawInput`、`LeftEvents(IReadOnlyList<MouseEvent>) → bool[]`、`MapKeys(IReadOnlyList<KeyEvent>) → KeyEventData[]`
  - `RawInput(PointF Mouse, bool[] LeftEvents, KeyEventData[]? KeyEvents = null, bool LeftHeld = false)`
- 已验证事实(D15):`new MouseEvent(MouseButton, bool)`、`new KeyEvent(Key, bool, ModifierKeys)` 可构造;`snapshot.IsMouseDown(MouseButton.Left)` 存在。

- [ ] **Step 1: 写失败测试 InputMapperTests.cs**

```csharp
using Veldrid;
using SharpUI;

namespace SharpUI.Tests;

public class InputMapperTests
{
    [Fact]
    public void LeftEvents_FiltersLeftInOrder()
    {
        bool[] events = InputMapper.LeftEvents(new MouseEvent[]
        {
            new(MouseButton.Right, true),
            new(MouseButton.Left, true),
            new(MouseButton.Left, false),
        });
        Assert.Equal(new[] { true, false }, events);
    }

    [Fact]
    public void LeftEvents_EmptyListNoAlloc()
    {
        Assert.Same(Array.Empty<bool>(), InputMapper.LeftEvents(Array.Empty<MouseEvent>()));
    }

    [Fact]
    public void MapKeys_TranslatesWithShift()
    {
        KeyEventData[] keys = InputMapper.MapKeys(new[]
        {
            new KeyEvent(Key.Tab, true, ModifierKeys.Shift),
            new KeyEvent(Key.Enter, true, ModifierKeys.None),
        });
        Assert.Equal(Veldrid.Key.Tab, keys[0].Key);
        Assert.True(keys[0].Shift);
        Assert.False(keys[1].Shift);
    }

    [Fact]
    public void MapKeys_EmptyListNoAlloc()
    {
        Assert.Same(Array.Empty<KeyEventData>(), InputMapper.MapKeys(Array.Empty<KeyEvent>()));
    }
}
```

- [ ] **Step 2: 确认失败** — Run: `dotnet test --filter FullyQualifiedName~InputMapperTests` → 编译 FAIL。
- [ ] **Step 3: RawInput 加第四参(Interaction.cs)**

```csharp
public readonly record struct RawInput(
    PointF Mouse, bool[] LeftEvents, KeyEventData[]? KeyEvents = null, bool LeftHeld = false);
```

- [ ] **Step 4: 写实现 src/SharpUI/InputMapper.cs**

```csharp
using Veldrid;
using Veldrid.Sdl2;

namespace SharpUI;

/// <summary>SDL InputSnapshot → RawInput 纯映射(可单测)。LeftHeld 来自 IsMouseDown 持久按键态(D13/D15)。</summary>
public static class InputMapper
{
    public static RawInput Map(InputSnapshot snapshot) => new(
        new PointF(snapshot.MousePosition.X, snapshot.MousePosition.Y),
        LeftEvents(snapshot.MouseEvents),
        MapKeys(snapshot.KeyEvents),
        snapshot.IsMouseDown(MouseButton.Left));

    public static bool[] LeftEvents(IReadOnlyList<MouseEvent> mouseEvents)
    {
        int n = 0;
        foreach (MouseEvent me in mouseEvents)
            if (me.MouseButton == MouseButton.Left) n++;
        if (n == 0) return Array.Empty<bool>();
        var events = new bool[n];
        int i = 0;
        foreach (MouseEvent me in mouseEvents)
            if (me.MouseButton == MouseButton.Left)
                events[i++] = me.Down;
        return events;
    }

    public static KeyEventData[] MapKeys(IReadOnlyList<KeyEvent> keyEvents)
    {
        if (keyEvents.Count == 0) return Array.Empty<KeyEventData>();
        var keys = new KeyEventData[keyEvents.Count];
        for (int i = 0; i < keyEvents.Count; i++)
        {
            KeyEvent ke = keyEvents[i];
            keys[i] = new KeyEventData(ke.Key, ke.Down, ke.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        return keys;
    }
}
```

- [ ] **Step 5: 通过 + 全量 74/74** → Run: `dotnet test`
- [ ] **Step 6: 提交** `git add -A && git commit -m "feat: testable input mapper with held-state from sdl"`

---

### Task 3: 焦点硬化(Escape/空白失焦/激活负例/失焦取消按压)

**Files:**
- Modify: `src/SharpUI/Focus.cs`(Escape 清焦 + Clear)、`src/SharpUI/Interaction.cs`(CancelPress + 空压失焦)、`src/SharpUI/App.cs`(Create 里挂 FocusLost)
- Test: `tests/SharpUI.Tests/FocusTests.cs`(追加 3)

**Interfaces:**
- Produces:
  - `FocusManager.Clear()`;`HandleKeys` 中 `Key.Escape → _focusedId = null`
  - `Interaction.CancelPress()`(清 `_pressedOn` 与按压态;App 挂 `window.FocusLost`)
  - `Interaction.Update` 按压时 `hovered == null → focus?.Clear()`

- [ ] **Step 1: 追加失败测试(沿用 TwoButtons/Frame 辅助)**

```csharp
    [Fact]
    public void Escape_UnfocusesAndEnterBecomesNoOp()
    {
        (PanelElement root, ButtonElement a, _) = TwoButtons();
        FocusManager focus = Frame(root);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Escape, true, false) });
        Assert.Null(focus.FocusedId);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Enter, true, false) });   // 无焦:不崩、无激活
        Assert.Null(focus.FocusedThisFrame);
    }

    [Fact]
    public void Enter_OnFocusedNonButton_IsNoOp()
    {
        TextElement t = Ui.Text("hi").Focusable();
        PanelElement root = Ui.Column(t);
        IdResolver.Assign(root);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Enter, true, false) });   // 聚焦非按钮:不崩
        Assert.True(focus.IsFocused(t));
    }

    [Fact]
    public void PressOnEmpty_ClearsFocus_AndCancelPressKillsPendingClick()
    {
        (PanelElement root, ButtonElement a, _) = TwoButtons();
        FocusManager focus = Frame(root);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a));

        Interaction.Update(root, new RawInput(new PointF(50, 190), new[] { true }), focus);  // 空白处按压(按钮高约 100)
        Assert.Null(focus.FocusedId);

        int clicks = 0;
        ButtonElement b = new("B", () => clicks++);
        PanelElement root2 = Ui.Column(b);
        root2.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root2, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root2);
        Interaction.Update(root2, new RawInput(new PointF(50, 5), new[] { true }));          // 按下
        Interaction.CancelPress();                                                            // 窗口失焦
        Interaction.Update(root2, new RawInput(new PointF(50, 5), new[] { false }));          // 迟到的释放
        Assert.Equal(0, clicks);
    }
```


- [ ] **Step 2: 确认失败** — Run: `dotnet test --filter FullyQualifiedName~FocusTests` → 新 3 FAIL。
- [ ] **Step 3: Focus.cs — Clear 与 Escape**

`SetFocus` 之后追加:

```csharp
    public void Clear() => _focusedId = null;
```

`HandleKeys` 的 switch 中追加 case:

```csharp
                case Key.Escape:
                    _focusedId = null;
                    break;
```

- [ ] **Step 4: Interaction.cs — CancelPress 与空压失焦**

类内追加:

```csharp
    /// <summary>取消进行中的按压(窗口失焦时由 App 调用;规格书 M2b-i)。</summary>
    public static void CancelPress() => _pressedOn = null;
```

`if (down)` 分支末尾(`focus?.SetFocus(hovered);` 之后)追加:

```csharp
                if (hovered is null)
                    focus?.Clear();
```

- [ ] **Step 5: App.cs — Create 中挂失焦(在 Resized 挂线之后)**

```csharp
        window.FocusLost += Interaction.CancelPress;   // D15:Sdl2Window.FocusLost 存在
```

- [ ] **Step 6: 通过 + 全量 77/77** → Run: `dotnet test`
- [ ] **Step 7: 提交** `git add -A && git commit -m "feat: focus hardening with escape, empty-press unfocus, cancel on focus loss"`

---

### Task 4: 同键碰撞调试检测

**Files:**
- Modify: `src/SharpUI/Ids.cs`
- Test: `tests/SharpUI.Tests/IdResolverTests.cs`(追加 2)

**Interfaces:**
- Produces: `IdResolver.LastDuplicateKeys : IReadOnlyList<string>`(每次 Assign 清空重填;记录 `父id + "/" + 重复键` 形式的完整重复段)。仅检测**同一父节点下**的同键兄弟;分支隔离的同键(裁定#13 语义)不误报。

- [ ] **Step 1: 追加失败测试**

```csharp
    [Fact]
    public void DuplicateSiblingKeys_AreDetected()
    {
        PanelElement col = Ui.Panel();
        col.Add(Ui.Text("a").Key("dup"));
        col.Add(Ui.Text("b").Key("dup"));
        col.Add(Ui.Text("c").Key("ok"));
        IdResolver.Assign(col);
        Assert.Contains(IdResolver.LastDuplicateKeys, id => id.EndsWith("/dup"));
        Assert.Equal(1, IdResolver.LastDuplicateKeys.Count);   // 第二次出现记一次
    }

    [Fact]
    public void SameKeyInDifferentBranches_NotFlagged()
    {
        PanelElement root = Ui.Column(
            Ui.Column(Ui.Text("l").Key("t")),
            Ui.Column(Ui.Text("r").Key("t")));
        IdResolver.Assign(root);
        Assert.Empty(IdResolver.LastDuplicateKeys);
    }
```

- [ ] **Step 2: 确认失败** → Run: `dotnet test --filter FullyQualifiedName~IdResolverTests`
- [ ] **Step 3: 实现 Ids.cs(Assign 检测;Visit 内对有键子项做惰性 HashSet)**

```csharp
using System.Text;

namespace SharpUI;

/// <summary>每帧为元素树计算稳定 id:Key 优先,类型名+兄弟索引回退(规格书 D10)。
/// LastDuplicateKeys 记录同父同键兄弟(用户错误,React 纪律);不同分支同键合法。</summary>
public static class IdResolver
{
    [ThreadStatic]
    static StringBuilder? _sb;

    static readonly List<string> _duplicates = new();

    public static IReadOnlyList<string> LastDuplicateKeys => _duplicates;

    public static void Assign(Element root)
    {
        _sb ??= new StringBuilder(256);
        _duplicates.Clear();
        Visit(root, "", _sb);
    }

    static void Visit(Element e, string parentId, StringBuilder sb)
    {
        e.ResolvedId = parentId;
        if (e.Children is not { Count: > 0 }) return;
        HashSet<string>? seenKeys = null;
        for (int i = 0; i < e.Children.Count; i++)
        {
            Element child = e.Children[i];
            if (child.Key is string key)
            {
                seenKeys ??= new HashSet<string>();
                sb.Clear();
                sb.Append(parentId).Append('/').Append(key);
                if (!seenKeys.Add(key))
                    _duplicates.Add(sb.ToString());
            }
            sb.Clear();
            sb.Append(parentId).Append('/').Append(child.Key ?? child.GetType().Name + i);
            Visit(child, sb.ToString(), sb);
        }
    }
}
```

- [ ] **Step 4: 通过 + 全量 79/79** → Run: `dotnet test`
- [ ] **Step 5: 提交** `git add -A && git commit -m "feat: duplicate sibling key detection in id resolver"`

---

### Task 5: 管线重排(上帧树交互 + 点击账本 + id 化 InputState)

**Files:**
- Modify: `src/SharpUI/Interaction.cs`(InputState 扩展、_leftHeld 退役、ClickedIds)、`src/SharpUI/Elements/ButtonElement.cs`(视觉门按 id)、`src/SharpUI/App.cs`(Run 重排 + 保留树 + UiContext)
- Create: `src/SharpUI/UiContext.cs`
- Test: `tests/SharpUI.Tests/InteractionTests.cs`(追加 2)

**Interfaces:**
- Produces(Task 6/7 消费):
  - `InputState` 新增尾部默认参:`string? HoveredId = null, string? FocusedId = null, IReadOnlyList<string>? ClickedIds = null`(Hovered/Focused 元素引用保留=上帧实例,仅供几何查询)
  - `Interaction._leftHeld` 静态退役:`LeftDown = (raw.LeftHeld && !released) || pressed`;`ClickedIds` 为本帧完成点击的 id 账本(静态 List 复用,无点击帧为 null)
  - `internal static class UiContext { public static InputState CurrentInput; }`(App 于构建前置;UiScope 读)
  - `ButtonElement.Draw` 门:`pc.Input.FocusedId == ResolvedId` / `pc.Input.HoveredId == ResolvedId`(实例引用比较退役——上帧实例与本帧实例必不同)
  - App.Run:上帧树交互 → UiContext → 构建 → Assign → focus 本帧注册/HandleKeys → with FocusedId → Arrange → Draw → 保留树

- [ ] **Step 1: 追加失败测试**

```csharp
    [Fact]
    public void ClickLedger_RecordsClickedIds_AndHeldFromRaw()
    {
        ButtonElement b = new("+", () => { }); b.Key = "plus";
        PanelElement root = Ui.Panel();
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        root.Add(b);
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 40));
        IdResolver.Assign(root);
        string id = b.ResolvedId;

        InputState press = Interaction.Update(root, new RawInput(new PointF(50, 5), new[] { true }, LeftHeld: true));
        Assert.True(press.LeftDown);
        InputState release = Interaction.Update(root, new RawInput(new PointF(50, 5), new[] { false }, LeftHeld: false));
        Assert.False(release.LeftDown);
        Assert.NotNull(release.ClickedIds);
        Assert.Contains(id, release.ClickedIds!);
    }

    [Fact]
    public void HeldFlagFromSDL_SustainsAcrossFramesWithoutEvents()
    {
        ButtonElement b = new("+", () => { }); b.Key = "plus";
        PanelElement root = Ui.Panel();
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        root.Add(b);
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 40));
        IdResolver.Assign(root);

        Interaction.Update(root, new RawInput(new PointF(50, 5), new[] { true }, LeftHeld: true));
        InputState held = Interaction.Update(root, new RawInput(new PointF(50, 5), Array.Empty<bool>(), LeftHeld: true));   // 持续按住、无事件帧
        Assert.True(held.LeftDown);
        InputState up = Interaction.Update(root, new RawInput(new PointF(50, 5), new[] { false }, LeftHeld: false));
        Assert.NotNull(up.ClickedIds);   // 跨帧按压-松开成立
    }
```

- [ ] **Step 2: 确认失败** → 新 2 FAIL(ClickedIds/HeldFromRaw 不存在)。
- [ ] **Step 3: Interaction.cs 最终形态(整体替换)**

```csharp
using Veldrid;

namespace SharpUI;

/// <summary>键盘事件(按时间序)。Key 为 Veldrid.Key(公开面技术债,规格书 D12)。</summary>
public readonly record struct KeyEventData(Key Key, bool Down, bool Shift);

/// <summary>App 从 SDL InputSnapshot 经 InputMapper 映射;LeftHeld 为持久按键态(D15)。</summary>
public readonly record struct RawInput(
    PointF Mouse, bool[] LeftEvents, KeyEventData[]? KeyEvents = null, bool LeftHeld = false);

public readonly record struct InputState(
    PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased,
    Element? Hovered, Element? Focused = null,
    string? HoveredId = null, string? FocusedId = null,
    IReadOnlyList<string>? ClickedIds = null)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

public static class Interaction
{
    static readonly List<string> _clicks = new();
    static string? _pressedOn;   // 按下瞬间的命中元素稳定 id;点击 = 按下与松开同 id(D10)

    public static void CancelPress() => _pressedOn = null;

    public static InputState Update(Element root, in RawInput raw)
    {
        if (root.ResolvedId is null) IdResolver.Assign(root);   // 惰性守卫:未赋 id 的根兜底;App 恒先 Assign
        bool pressed = false, released = false;
        Element? hovered = root.HitTest(raw.Mouse);
        _clicks.Clear();
        foreach (bool down in raw.LeftEvents)
        {
            if (down)
            {
                pressed = true;
                _pressedOn = hovered?.ResolvedId;
            }
            else
            {
                released = true;
                if (_pressedOn is not null && hovered?.ResolvedId == _pressedOn
                    && hovered is ButtonElement button)
                {
                    button.InvokeClick();
                    if (hovered.ResolvedId is string id) _clicks.Add(id);
                }
                _pressedOn = null;
            }
        }
        bool leftDown = (raw.LeftHeld && !released) || pressed;
        return new InputState(
            raw.Mouse, leftDown, pressed, released, hovered,
            HoveredId: hovered?.ResolvedId,
            ClickedIds: _clicks.Count > 0 ? _clicks : null);
    }
}
```

- [ ] **Step 4: UiContext.cs**

```csharp
namespace SharpUI;

/// <summary>本帧输入的服务定位(与 App.Atlas/State 同款;UiScope 在用户构建闭包内读取)。</summary>
internal static class UiContext
{
    public static InputState CurrentInput;
}
```

- [ ] **Step 5: ButtonElement.Draw 视觉门(id 比较;ResolvedId 已在 Draw 前 Assign)**

```csharp
        string? id = ResolvedId;
        Background = pc.Input.FocusedId == id ? Color32.FromRgb(0x60A5FA)
            : pc.Input.HoveredId == id
                ? (pc.Input.LeftDown ? Color32.FromRgb(0x1D4ED8) : Color32.FromRgb(0x3B82F6))
            : Color32.FromRgb(0x0C63E4);
```

- [ ] **Step 6: App.Run 重排(整体替换 Run;Create 不动)**

```csharp
    public int Run(int maxFrames = 0)
    {
        Atlas = new TextAtlas(_ctx);
        State = new StateStore();
        var mesh = new MeshBuilder();
        var focus = new FocusManager();
        Element? lastTree = null;
        int frames = 0;
        while (_window.Exists)
        {
            RawInput raw = InputMapper.Map(_window.PumpEvents());
            InputState input = lastTree is not null
                ? Interaction.Update(lastTree, in raw, focus)
                : InputState.Empty;
            UiContext.CurrentInput = input;

            Element root = _render is null ? new Spacer(SizeF.Zero) : _render();
            IdResolver.Assign(root);
            focus.BeginFrame();
            TreeWalk.Register(root, focus);
            focus.HandleKeys(raw.KeyEvents ?? Array.Empty<KeyEventData>());
            input = input with { Focused = focus.FocusedThisFrame, FocusedId = focus.FocusedId };

            SizeF screen = new(_window.Width, _window.Height);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            mesh.Clear();
            root.Draw(new PaintContext(mesh, Atlas, input));
            _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer);
            _ctx.Device.SwapBuffers();
            State.Sweep();
            lastTree = root;
            if (maxFrames > 0 && ++frames >= maxFrames)
                _window.Close();
        }
        return 0;
    }
```

(LeftEvents/MapKeys 两个私有辅助从 App.cs 删除——InputMapper 取代。)

- [ ] **Step 7: 通过 + 全量 81/81;既有测试因 InputState 语义演进的适配仅限构造调用形态** → Run: `dotnet test`
- [ ] **Step 8: 提交** `git add -A && git commit -m "feat: retained-tree interaction with click ledger and id-based visuals"`

---

### Task 6: UiScope 即时模式逃生舱 + state↔id 桥

**Files:**
- Create: `src/SharpUI/Elements/UiScope.cs`
- Modify: `src/SharpUI/Elements/Ui.cs`(Immediate 工厂)
- Test: `tests/SharpUI.Tests/UiScopeTests.cs`

**Interfaces:**
- Consumes: T5 全部;`App.State`(StateStore)。
- Produces(demo/M2c 消费):
  - `Ui.Immediate(string rootKey, Action<UiScope> build) → PanelElement`(root.Key = rootKey;竖排容器)
  - `sealed class UiScope`:`Text(string, float size = 16f, uint color = 0xFFFFFF) → ImmediateText`、`Button(string label) → ImmediateButton`、`Spacer(SizeF)`、`Row(Action<UiScope>)`、`Column(Action<UiScope>)`、`State<T>(string key, T initial = default!) → T`
  - `readonly struct ImmediateButton { bool Clicked(); bool Hovered(); }`、`readonly struct ImmediateText { }`(占位句柄,链式后续里程碑)
  - 路径段 Key 烤入规则:子段 = `TypeName + 同级序号`(与 IdResolver 回退公式一致),使 `局部路径` 与全局 ResolvedId 后缀严格对应(D14);账本匹配 `id.EndsWith("/" + rootKey + 局部路径)`

- [ ] **Step 1: 写失败测试 UiScopeTests.cs**

```csharp
using SharpUI;
using static SharpUI.Ui;

namespace SharpUI.Tests;

public class UiScopeTests
{
    [Fact]
    public void Immediate_BuildsTreeWithBakedKeys_MatchingIdResolver()
    {
        PanelElement panel = Immediate("dbg", static ui =>
        {
            ui.Text("fps: 60", 14);
            ui.Row(static row => { row.Button("Reset"); row.Button("Apply"); });
        });
        PanelElement root = Column(panel);
        IdResolver.Assign(root);

        Assert.Equal(2, panel.Children!.Count);                                    // Text + Row
        PanelElement rowPanel = (PanelElement)panel.Children[1];
        Assert.Equal(Axis.Horizontal, rowPanel.Style.Axis);
        Assert.EndsWith("/dbg/PanelElement1/ButtonElement0", rowPanel.Children![0].ResolvedId);   // 局部路径=全局后缀
        Assert.EndsWith("/dbg/PanelElement1/ButtonElement1", rowPanel.Children[1].ResolvedId);
    }

    [Fact]
    public void Clicked_MatchesLedgerBySuffix()
    {
        ButtonElement? clicked = null;
        var ledger = new List<string> { "/Column0/dbg/ButtonElement0" };
        UiContext.CurrentInput = new InputState(
            new PointF(0, 0), false, false, false, null, ClickedIds: ledger);
        PanelElement probe = Immediate("dbg", ui =>
        {
            if (ui.Button("Reset").Clicked()) clicked = probe;
        });
        Assert.NotNull(clicked);
        Assert.Same(probe, clicked);
    }

    [Fact]
    public void Hovered_UsesHoveredIdSuffix()
    {
        UiContext.CurrentInput = new InputState(
            new PointF(0, 0), false, false, false, null, HoveredId: "/x/dbg/PanelElement1/ButtonElement1");
        bool hovered = false;
        Immediate("dbg", ui => ui.Row(row => { row.Button("A"); if (row.Button("B").Hovered()) hovered = true; }));
        Assert.True(hovered);
        UiContext.CurrentInput = default;
    }

    [Fact]
    public void State_BridgesToStoreWithNamespacedKey()
    {
        App.State = new StateStore();
        Immediate("dbg", static ui => ui.State("count", ui.State("count", 5) + 1));   // Get(5) → Set(6)
        int read = 0;
        Immediate("dbg", ui => read = ui.State("count", 0));                          // Get → 6
        Assert.Equal(6, read);
        App.State = null;
    }

    [Fact]
    public void EmptyLedger_NoClickAndNoCrash()
    {
        UiContext.CurrentInput = default;   // ClickedIds null
        bool any = false;
        Immediate("dbg", ui => { if (ui.Button("X").Clicked()) any = true; });
        Assert.False(any);
    }
}
```


- [ ] **Step 2: 确认失败** → 编译 FAIL(UiScope/Immediate 未定义)。
- [ ] **Step 3: 写实现 src/SharpUI/Elements/UiScope.cs**

```csharp
namespace SharpUI;

/// <summary>即时模式逃生舱句柄(D14):Clicked/Hovered 按稳定 id 后缀匹配本帧账本。</summary>
public readonly struct ImmediateButton
{
    readonly UiScope _scope;
    readonly string _path;   // 含 rootKey 的局部路径,如 "dbg/PanelElement1/ButtonElement0"
    internal ImmediateButton(UiScope scope, string path)
    {
        _scope = scope;
        _path = path;
    }

    public bool Clicked() => _scope.LedgerContains(_path);

    public bool Hovered() =>
        UiContext.CurrentInput.HoveredId?.EndsWith("/" + _path, StringComparison.Ordinal) == true;
}

public readonly struct ImmediateText
{
    internal ImmediateText(string _) { }
}

/// <summary>即时模式作用域:把调用序列记录为元素树,与声明式共用布局/绘制管线(D11/D14)。</summary>
public sealed class UiScope
{
    readonly PanelElement _root;
    readonly string _rootKey;
    readonly Stack<PanelElement> _stack = new();      // 当前父链(root 在底)
    readonly Stack<string> _pathStack = new();        // 与 _stack 同进同退;root 段为 ""
    readonly Dictionary<PanelElement, int> _childIndex = new();

    internal UiScope(PanelElement root, string rootKey)
    {
        _root = root;
        _rootKey = rootKey;
        _stack.Push(root);
        _pathStack.Push("");
    }

    internal bool LedgerContains(string path)
    {
        IReadOnlyList<string>? ledger = UiContext.CurrentInput.ClickedIds;
        if (ledger is null) return false;
        string suffix = "/" + path;
        for (int i = 0; i < ledger.Count; i++)
            if (ledger[i].EndsWith(suffix, StringComparison.Ordinal)) return true;
        return false;
    }

    string NextSegment<TElement>() where TElement : Element
    {
        PanelElement parent = _stack.Peek();
        int i = _childIndex.TryGetValue(parent, out int seen) ? seen : 0;
        _childIndex[parent] = i + 1;
        return typeof(TElement).Name + i;             // 与 IdResolver 回退公式一致(D14)
    }

    public ImmediateText Text(string text, float size = 16f, uint color = 0xFFFFFF)
    {
        var t = new TextElement(text) { FontSize = size, Color = Color32.FromRgb(color) };
        t.Key = NextSegment<TextElement>();
        _stack.Peek().Add(t);
        return new ImmediateText(text);
    }

    public ImmediateButton Button(string label)
    {
        var b = new ButtonElement(label, static () => { });   // 逃生舱内点击走账本,不走 Action
        string segment = NextSegment<ButtonElement>();
        b.Key = segment;
        _stack.Peek().Add(b);
        return new ImmediateButton(this, AggregatePath() + "/" + segment);
    }

    public void Spacer(SizeF size)
    {
        var spacer = new SharpUI.Spacer(size);
        spacer.Key = NextSegment<SharpUI.Spacer>();
        _stack.Peek().Add(spacer);
    }

    public void Row(Action<UiScope> children) => Nest(Axis.Horizontal, children);

    public void Column(Action<UiScope> children) => Nest(Axis.Vertical, children);

    void Nest(Axis axis, Action<UiScope> children)
    {
        var panel = new PanelElement();
        string segment = NextSegment<PanelElement>();
        panel.Key = segment;
        panel.Style.Axis = axis;
        _stack.Peek().Add(panel);
        _stack.Push(panel);
        _pathStack.Push(segment);
        children(this);
        _stack.Pop();
        _pathStack.Pop();
    }

    string AggregatePath()
    {
        var sb = new System.Text.StringBuilder(_rootKey);
        foreach (string segment in _pathStack)
            if (segment.Length > 0)
                sb.Append('/').Append(segment);
        return sb.ToString();
    }

    public T State<T>(string key, T initial = default!) =>
        App.State is { } store ? store.Get("imm:" + _rootKey + ":" + key, initial) : initial;

    public void State<T>(string key, T value)
    {
        App.State?.Set("imm:" + _rootKey + ":" + key, value);
    }
}
```

- [ ] **Step 4: Ui.cs 追加工厂(Text 工厂之后)**

```csharp
    /// <summary>即时模式逃生舱(D14):rootKey 须全树唯一(同 Key 纪律)。</summary>
    public static PanelElement Immediate(string rootKey, Action<UiScope> build)
    {
        var root = new PanelElement { Key = rootKey };
        build(new UiScope(root, rootKey));
        return root;
    }
```

- [ ] **Step 5: 通过 + 全量 86/86** → Run: `dotnet test`(State 测试里 `App.State` 赋值依赖 IVT,已就位)
- [ ] **Step 6: 提交** `git add -A && git commit -m "feat: immediate-mode escape hatch with id-suffixed click ledger"`

---

### Task 7: Demo 升级 + 性能计量 + 冒烟

**Files:**
- Modify: `examples/Counter/Program.cs`(Immediate 调试面板)、`tests/SharpUI.Tests/PerfTests.cs`(场景计入 Immediate)
- Test: 既有套件;无新测试文件

**Interfaces:**
- Consumes: T6 全部。
- Produces: 含逃生舱的 Counter demo;计入 Immediate 的性能门禁数字。

- [ ] **Step 1: Program.cs 的 Column 子列表追加(在 Row(−/+) 之后)**

```csharp
                Ui.Immediate("debug", ui =>
                {
                    ui.Text($"count = {state.Count}", 14, 0x9AA2B3);
                    if (ui.Button("Reset").Clicked()) state.Count = 0;
                }),
```

(`using static SharpUI.Ui;` 已有;`Ui.Immediate` 全名调用亦可。)

- [ ] **Step 2: PerfTests 帧场景同样追加该 Immediate 块(Build 闭包内、Row 之后),Assign/Arrange 已有调用覆盖其 id/布局**
- [ ] **Step 3: Release 门禁 + Debug 全量 + 冒烟(maxFrames:30 → 恢复)** → Run: `dotnet test -c Release --filter FullyQualifiedName~PerfTests`(记录数字)→ `dotnet test`(86/86)→ 冒烟
- [ ] **Step 4: 提交** `git add -A && git commit -m "feat: counter demo immediate debug panel and perf metering"`

---

## 任务依赖图

```
T1 ┐
T2 ├(可并行,执行仍串行)→ T3 → T5 → T6 → T7
T4 ┘
```

## M2b-i 完成定义

- [ ] `dotnet test` 与 Release 全绿(86/86 含 GPU)
- [ ] 性能门禁绿且场景含 Immediate;数字记录
- [ ] Counter 冒烟通过(Immediate 面板渲染、Reset 账本点击生效——视觉验收归用户)
- [ ] 零 unsafe、公开面与 Interfaces 一致;既有测试适配仅限调用形态
