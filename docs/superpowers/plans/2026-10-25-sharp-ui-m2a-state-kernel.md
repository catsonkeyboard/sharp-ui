# SharpUI M2a 内核里程碑 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** M2a 内核——稳定 id + State\<T\> 状态内核、点击身份 id 化、焦点与键盘(Tab/Enter/Space)、Mesh 硬化与 Ui.Button,性能门禁再确认。

**Architecture:** 保持每帧重建管线不变;新增每帧树走查 `IdResolver.Assign` 为每个元素计算稳定 id(Key 优先、`类型名+兄弟索引` 回退,规格书 D10)。`StateStore` 按 id 存取跨帧状态、帧末清扫失联键。`Interaction` 点击门从路径数组切换为 ResolvedId 比较。`FocusManager` 维护焦点 id + Tab 序,键盘事件经 `RawInput.KeyEvents` 路由。

**Tech Stack:** 不变:.NET 10、Veldrid 4.9.0 系、SkiaSharp 4.153.1、xUnit。零新增包。

**Spec:** `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(M2 阶段划分与裁定 D10-D12 为本计划依据;执行者须同时阅读规格书 §7 M2 节)

## Global Constraints

- 沿用 M1 全部约束:`namespace SharpUI` / `SharpUI.Tests`;禁止 `unsafe`;`IsAotCompatible` 零自有警告;GPU 测试打 `[Trait("Category","Gpu")]`;英文 Conventional Commits;main 分支;每任务至少一次提交。
- 性能预算**不放宽**:Counter 场景(含 id 解析与焦点)稳态每帧分配 ≤64KB、CPU <8ms(Release)。若实测超限,报告 BLOCKED 等裁定,不得改断言。
- 公开面最小化:本阶段新增公开成员仅限各任务 Interfaces 节列出的成员;`Veldrid.Key` 进入公开 API 是已裁定技术债(规格书 D12)。
- 版本锁定不变:Veldrid 4.9.0 / Veldrid.SDL2 4.9.0 / Veldrid.SPIRV 1.0.15 / Newtonsoft.Json 13.0.3 / SkiaSharp 4.153.1。
- 命令均在仓库根 `C:\Users/<user>/sharp-ui` 运行;`dotnet test` 全绿是每任务提交前置。

---

### Task 1: Mesh 守卫 + Ui.Button 工厂

**Files:**
- Modify: `src/SharpUI/Mesh.cs`(Ensure 加守卫)、`src/SharpUI/Elements/Ui.cs`(Button 工厂)
- Test: `tests/SharpUI.Tests/MeshTests.cs`、`tests/SharpUI.Tests/UiDslTests.cs`(各追加)

**Interfaces:**
- Produces:
  - `MeshBuilder.Ensure` 抛 `InvalidOperationException`(消息含 "65536"),当 `_vertexCount + extraVerts > 65536`
  - `Ui.Button(string label, Action onClick) → ButtonElement`

- [ ] **Step 1: 追加失败测试到 MeshTests.cs**

```csharp
    [Fact]
    public void AddQuad_ThrowsAtVertexOverflow()
    {
        var mesh = new MeshBuilder();
        for (int i = 0; i < 65536 / 4; i++)          // 16384 quads = 恰好 65536 顶点
            mesh.AddQuad(new RectF(i % 100, 0, 1, 1), new RectF(0, 0, 1, 1), Color32.White);
        Assert.Equal(65536, mesh.VertexCount);
        Assert.Throws<InvalidOperationException>(() =>
            mesh.AddQuad(new RectF(0, 0, 1, 1), new RectF(0, 0, 1, 1), Color32.White));
    }
```

- [ ] **Step 2: 追加失败测试到 UiDslTests.cs**

```csharp
    [Fact]
    public void Button_FactoryCreatesWiredButton()
    {
        int clicks = 0;
        ButtonElement b = Ui.Button("OK", () => clicks++);
        Assert.Equal("OK", ((TextElement)b.Children![0]).Text);
        b.InvokeClick();
        Assert.Equal(1, clicks);
    }
```

- [ ] **Step 3: 运行确认失败**

Run: `dotnet test --filter "FullyQualifiedName~MeshTests|FullyQualifiedName~UiDslTests"`
Expected: FAIL(守卫不抛出;`Ui.Button` 未定义)。

- [ ] **Step 4: Mesh.cs 的 Ensure 首行插入守卫**

```csharp
    void Ensure(int extraVerts, int extraIndices)
    {
        if (_vertexCount + extraVerts > ushort.MaxValue + 1)
            throw new InvalidOperationException(
                $"Mesh exceeds 65536 vertices ({_vertexCount + extraVerts}); the frame must be split or cleared.");
        if (_vertexCount + extraVerts > _vertices.Length)
            Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _vertexCount + extraVerts));
        if (_indexCount + extraIndices > _indices.Length)
            Array.Resize(ref _indices, Math.Max(_indices.Length * 2, _indexCount + extraIndices));
    }
```

- [ ] **Step 5: Ui.cs 工厂区追加(Button 放在 Panel 之后)**

```csharp
    public static ButtonElement Button(string label, Action onClick) => new(label, onClick);
```

- [ ] **Step 6: 运行确认通过(51/51)并全量回归**

Run: `dotnet test --filter "FullyQualifiedName~MeshTests|FullyQualifiedName~UiDslTests"` 然后 `dotnet test`
Expected: 全部 PASS。

- [ ] **Step 7: 提交**

```bash
git add -A && git commit -m "feat: mesh vertex overflow guard and ui button factory"
```

---

### Task 2: 稳定 id 解析(IdResolver)

**Files:**
- Create: `src/SharpUI/Ids.cs`
- Modify: `src/SharpUI/Element.cs`(Key/ResolvedId)、`src/SharpUI/Elements/Ui.cs`(Key 扩展)
- Test: `tests/SharpUI.Tests/IdResolverTests.cs`

**Interfaces:**
- Produces(Task 3/4/5 依赖,签名精确一致):
  - `Element` 新增:`public string? Key { get; internal set; }`、`internal string? ResolvedId { get; set; }`
  - `ElementExtensions` 新增:`public static T Key<T>(this T e, string key) where T : Element`(与既有 Color 字段+扩展同名先例一致,属性不可调用时扩展方法生效)
  - `public static class IdResolver`:`Assign(Element root)`——根 ResolvedId 为 `""`;子 id = `父id + "/" + (child.Key ?? child.GetType().Name + 兄弟索引)`;StringBuilder 线程静态复用

- [ ] **Step 1: 写失败测试 IdResolverTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class IdResolverTests
{
    static PanelElement Build(bool swapped = false)
    {
        ButtonElement inc = new("+", () => { }); inc.Key = "inc";
        ButtonElement dec = new("-", () => { }); dec.Key = "dec";
        PanelElement col = Ui.Panel();
        col.Add(swapped ? dec : inc);
        col.Add(swapped ? inc : dec);
        return col;
    }

    [Fact]
    public void Assign_IsDeterministicAcrossIdenticalRebuilds()
    {
        PanelElement a = Build();
        PanelElement b = Build();
        IdResolver.Assign(a);
        IdResolver.Assign(b);
        Assert.Equal(a.Children![0].ResolvedId, b.Children![0].ResolvedId);
        Assert.Equal(a.Children[1].ResolvedId, b.Children[1].ResolvedId);
    }

    [Fact]
    public void KeyedChild_KeepsIdWhenReordered()
    {
        PanelElement a = Build();
        PanelElement b = Build(swapped: true);
        IdResolver.Assign(a);
        IdResolver.Assign(b);
        // a 的第一个孩子与 b 的第二个孩子是同一个语义按钮(键 "inc")
        Assert.Equal(a.Children![0].ResolvedId, b.Children![1].ResolvedId);
    }

    [Fact]
    public void UnkeyedChildren_FallBackToTypeNameAndIndex()
    {
        PanelElement col = Ui.Column(Ui.Text("a"), Ui.Text("b"));
        IdResolver.Assign(col);
        Assert.Equal("/TextElement0", col.Children![0].ResolvedId);
        Assert.Equal("/TextElement1", col.Children[1].ResolvedId);
        Assert.Equal("", col.ResolvedId);
    }

    [Fact]
    public void NestedIds_AreParentPrefixed()
    {
        PanelElement root = Ui.Column(Ui.Column(Ui.Text("x").Key("t")));
        IdResolver.Assign(root);
        Assert.StartsWith(root.Children![0].ResolvedId + "/",
            root.Children[0].Children![0].ResolvedId);
        Assert.EndsWith("/t", root.Children[0].Children![0].ResolvedId);
    }

    [Fact]
    public void SameKeyInDifferentBranches_StaysDistinct()
    {
        // 注意:同分支的同键兄弟按 D10 会碰撞(用户错误,React 语义),故必须分放分支
        TextElement left = Ui.Text("l").Key("t");
        TextElement right = Ui.Text("r").Key("t");
        PanelElement root = Ui.Column(Ui.Column(left), Ui.Column(right));
        IdResolver.Assign(root);
        Assert.NotEqual(left.ResolvedId, right.ResolvedId);
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test --filter FullyQualifiedName~IdResolverTests`
Expected: 编译 FAIL(`Key`/`ResolvedId`/`IdResolver` 未定义)。

- [ ] **Step 3: Element.cs 追加字段(放在 `IsHitTarget` 之后)**

```csharp
    /// <summary>用户提供的稳定键;未提供时按 类型名+兄弟索引 回退(规格书 D10)。</summary>
    public string? Key { get; internal set; }
    internal string? ResolvedId { get; set; }
```

- [ ] **Step 4: Ui.cs 的 ElementExtensions 追加**

```csharp
    public static T Key<T>(this T e, string key) where T : Element { e.Key = key; return e; }
```

- [ ] **Step 5: 写实现 src/SharpUI/Ids.cs**

```csharp
using System.Text;

namespace SharpUI;

/// <summary>每帧为元素树计算稳定 id:Key 优先,类型名+兄弟索引回退(规格书 D10)。</summary>
public static class IdResolver
{
    [ThreadStatic]
    static StringBuilder? _sb;

    public static void Assign(Element root)
    {
        _sb ??= new StringBuilder(256);
        Visit(root, "", _sb);
    }

    static void Visit(Element e, string parentId, StringBuilder sb)
    {
        e.ResolvedId = parentId;
        if (e.Children is not { Count: > 0 }) return;
        for (int i = 0; i < e.Children.Count; i++)
        {
            Element child = e.Children[i];
            sb.Clear();
            sb.Append(parentId).Append('/').Append(child.Key ?? child.GetType().Name + i);
            Visit(child, sb.ToString(), sb);
        }
    }
}
```

- [ ] **Step 6: 运行确认通过并全量回归**

Run: `dotnet test --filter FullyQualifiedName~IdResolverTests` 然后 `dotnet test`
Expected: PASS(5 new)+ 全量 56/56。

- [ ] **Step 7: 提交**

```bash
git add -A && git commit -m "feat: stable element id resolution with key preference"
```

---

### Task 3: StateStore + App 接线

**Files:**
- Create: `src/SharpUI/State.cs`
- Modify: `src/SharpUI/App.cs`(`State` 静态 + Run 中 Assign/Sweep)
- Test: `tests/SharpUI.Tests/StateTests.cs`

**Interfaces:**
- Consumes: Task 2 `IdResolver.Assign`。
- Produces(Task 5 与 M2b/c 依赖):
  - `public sealed class StateStore`:`T Get<T>(string id) where T : new()`、`T Get<T>(string id, T initial)`、`void Sweep()`、`int Count`
  - `App.State : StateStore`(static,internal set,Run 开头赋值;与 `App.Atlas` 同款 M1 服务定位模式)

- [ ] **Step 1: 写失败测试 StateTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class StateTests
{
    [Fact]
    public void Get_CreatesThenPersistsAcrossSweeps()
    {
        var store = new StateStore();
        store.Get<int>("a");          // 触碰帧 1
        store.Sweep();
        store.Get<int>("a");          // 触碰帧 2
        store.Sweep();
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Get_InitialValueUsedOnFirstAccessOnly()
    {
        var store = new StateStore();
        Assert.Equal(5, store.Get("n", 5));
        store.Sweep();
        store.Get("n", 100);          // 已存在,初值被忽略
        store.Sweep();
        Assert.Equal(5, store.Get("n", 0));
    }

    [Fact]
    public void ReferenceTypeState_MutatesAcrossFrames()
    {
        var store = new StateStore();
        store.Get("list", new List<int> { 1 });
        store.Sweep();
        store.Get<List<int>>("list").Add(2);
        store.Sweep();
        Assert.Equal(new[] { 1, 2 }, store.Get<List<int>>("list"));
    }

    [Fact]
    public void Sweep_RemovesUntouched_KeepsTouched()
    {
        var store = new StateStore();
        store.Get<int>("kept");
        store.Get<int>("dropped");
        store.Sweep();
        store.Get<int>("kept");       // 第二帧只触碰 kept
        store.Sweep();
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Sweep_WithNoTouches_ClearsAll()
    {
        var store = new StateStore();
        store.Get<int>("x");
        store.Sweep();
        store.Sweep();                // 本帧零触碰
        Assert.Equal(0, store.Count);
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test --filter FullyQualifiedName~StateTests`
Expected: 编译 FAIL(`StateStore` 未定义)。

- [ ] **Step 3: 写实现 src/SharpUI/State.cs**

```csharp
namespace SharpUI;

/// <summary>按稳定 id 存取的跨帧状态(规格书 D10)。每帧 Get 视为触碰;Sweep 移除失联键。</summary>
public sealed class StateStore
{
    readonly Dictionary<string, object> _values = new();
    readonly HashSet<string> _touched = new();
    List<string>? _stale;

    public int Count => _values.Count;

    public T Get<T>(string id) where T : new()
    {
        _touched.Add(id);
        if (_values.TryGetValue(id, out object? v)) return (T)v;
        T created = new();
        _values[id] = created;
        return created;
    }

    public T Get<T>(string id, T initial)
    {
        _touched.Add(id);
        if (_values.TryGetValue(id, out object? v)) return (T)v;
        _values[id] = initial;
        return initial;
    }

    public void Sweep()
    {
        _stale?.Clear();
        foreach (KeyValuePair<string, object> kv in _values)
            if (!_touched.Contains(kv.Key))
                (_stale ??= new List<string>()).Add(kv.Key);
        if (_stale is { Count: > 0 })
        {
            foreach (string key in _stale) _values.Remove(key);
            _stale.Clear();
        }
        _touched.Clear();
    }
}
```

- [ ] **Step 4: App.cs 接线**

在 `App.Atlas` 属性声明之后追加:

```csharp
    /// <summary>跨帧状态存储(M1 服务定位模式,同 Atlas;M2b 度量上下文重构时收编)。</summary>
    public static StateStore? State { get; internal set; }
```

`Run` 内,`Atlas = new TextAtlas(_ctx);` 之后追加 `State = new StateStore();`;在 `Element root = ...` 构建之后、`LayoutEngine.Arrange` 之前插入:

```csharp
            IdResolver.Assign(root);
```

在帧尾 `SwapBuffers()` 之后、maxFrames 判定之前插入:

```csharp
            State.Sweep();
```

- [ ] **Step 5: 运行确认通过并全量回归**

Run: `dotnet test --filter FullyQualifiedName~StateTests` 然后 `dotnet test`
Expected: PASS(5 new)+ 全量 61/61。

- [ ] **Step 6: 提交**

```bash
git add -A && git commit -m "feat: id-keyed state store wired into frame loop"
```

---

### Task 4: 点击身份切换为 ResolvedId

**Files:**
- Modify: `src/SharpUI/Interaction.cs`(整体替换)、`src/SharpUI/App.cs`(Run 内 Arrange 前已有 Assign,无需改动;确认顺序 build→Assign→Arrange→Update)
- Test: `tests/SharpUI.Tests/InteractionTests.cs`(追加 2 个;既有 6 个必须不改一字仍绿)

**Interfaces:**
- Consumes: Task 2 `ResolvedId`。
- Produces(Task 5 在此签名上加可选 focus 参数):
  - `Interaction.Update(Element root, in RawInput raw) → InputState`:`_pressedOn : string?` 存按下时 hover 的 ResolvedId;释放时 `hovered?.ResolvedId == _pressedOn && hovered is ButtonElement` 才触发
  - 移除 `HitTestWithPath/Descend/PathsEqual`(M1 路径身份整体退役,消除 Descend/HitTest 双遍历漂移风险)

- [ ] **Step 1: 追加失败测试到 InteractionTests.cs**

```csharp
    static PanelElement BuildReorder(bool swapped, Action onInc)
    {
        ButtonElement inc = new("+", onInc); inc.Key = "inc";
        ButtonElement dec = new("-", () => { }); dec.Key = "dec";
        PanelElement col = Ui.Panel();
        col.Style.CrossAlign = CrossAxisAlignment.Stretch;   // Atlas-null 下按钮自身仅 32px 宽,须拉伸到全宽命中 x=50
        col.Add(swapped ? dec : inc);
        col.Add(swapped ? inc : dec);
        return col;
    }

    [Fact]
    public void KeyedButtons_ClickSurvivesReorderAcrossRebuild()
    {
        int incClicks = 0;
        PanelElement t1 = BuildReorder(false, () => incClicks++);
        PanelElement t2 = BuildReorder(true, () => incClicks++);
        LayoutEngine.Arrange(t1, new RectF(0, 0, 100, 200));
        LayoutEngine.Arrange(t2, new RectF(0, 0, 100, 200));
        IdResolver.Assign(t1);
        IdResolver.Assign(t2);
        float h = t1.Children![0].Frame.Height;

        Interaction.Update(t1, new RawInput(new PointF(50, 5), new[] { true }));
        Interaction.Update(t2, new RawInput(new PointF(50, h + 5), new[] { false }));

        Assert.Equal(1, incClicks);       // 键 "inc" 在两帧中位于不同位置仍被正确命中
    }

    [Fact]
    public void UnkeyedButtons_ReorderBreaksClick()
    {
        int firstClicks = 0;
        PanelElement Build(bool swapped)
        {
            ButtonElement a = new("+", () => firstClicks++);
            ButtonElement b = new("-", () => { });
            PanelElement col = Ui.Panel();
            col.Style.CrossAlign = CrossAxisAlignment.Stretch;   // 同上:全宽命中
            col.Add(swapped ? b : a);
            col.Add(swapped ? a : b);
            return col;
        }
        PanelElement t1 = Build(false);
        PanelElement t2 = Build(true);
        LayoutEngine.Arrange(t1, new RectF(0, 0, 100, 200));
        LayoutEngine.Arrange(t2, new RectF(0, 0, 100, 200));
        IdResolver.Assign(t1);
        IdResolver.Assign(t2);
        float h = t1.Children![0].Frame.Height;

        Interaction.Update(t1, new RawInput(new PointF(50, 5), new[] { true }));
        Interaction.Update(t2, new RawInput(new PointF(50, h + 5), new[] { false }));

        Assert.Equal(0, firstClicks);     // 无键回退到索引身份:换位后语义不同,不触发(文档化语义)
    }
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test --filter FullyQualifiedName~InteractionTests`
Expected: 新 2 个 FAIL(旧路径身份不识别键),旧 6 个 PASS。

- [ ] **Step 3: 整体替换 src/SharpUI/Interaction.cs(KeyEventData 一并落地)**

```csharp
using Veldrid;

namespace SharpUI;

/// <summary>键盘事件(按时间序)。Key 为 Veldrid.Key(公开面技术债,规格书 D12)。</summary>
public readonly record struct KeyEventData(Key Key, bool Down, bool Shift);

/// <summary>App 从 SDL InputSnapshot 映射;LeftEvents 按时间序记录左键按下(true)/抬起(false)。</summary>
public readonly record struct RawInput(PointF Mouse, bool[] LeftEvents, KeyEventData[]? KeyEvents = null);

public readonly record struct InputState(
    PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased,
    Element? Hovered, Element? Focused = null)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

public static class Interaction
{
    static bool _leftHeld;
    static string? _pressedOn;   // 按下瞬间的命中元素稳定 id;点击 = 按下与松开同 id(规格书 D10)

    public static InputState Update(Element root, in RawInput raw)
    {
        if (root.ResolvedId is null) IdResolver.Assign(root);   // 惰性守卫:未赋 id 的根(遗留测试)兜底;App.Run 恒先 Assign,生产为死代码
        bool pressed = false, released = false;
        Element? hovered = root.HitTest(raw.Mouse);
        foreach (bool down in raw.LeftEvents)
        {
            if (down)
            {
                _leftHeld = true;
                pressed = true;
                _pressedOn = hovered?.ResolvedId;
            }
            else
            {
                _leftHeld = false;
                released = true;
                if (_pressedOn is not null && hovered?.ResolvedId == _pressedOn
                    && hovered is ButtonElement button)
                {
                    button.InvokeClick();
                }
                _pressedOn = null;
            }
        }
        return new InputState(raw.Mouse, _leftHeld, pressed, released, hovered);
    }
}
```

注:`RawInput` 第三参数与 `InputState.Focused` 第六参数均带默认值,既有测试与调用点零改动编译通过;`KeyEventData` 在此落地,Task 5 只新增 FocusManager 与接线。

- [ ] **Step 4: 运行确认通过并全量回归**

Run: `dotnet test --filter FullyQualifiedName~InteractionTests` 然后 `dotnet test`
Expected: 8/8 + 全量 63/63。

- [ ] **Step 5: 提交**

```bash
git add -A && git commit -m "feat: click identity by stable resolved id, path code retired"
```

---

### Task 5: 焦点与键盘(FocusManager + Tab/Enter/Space)

**Files:**
- Create: `src/SharpUI/Focus.cs`
- Modify: `src/SharpUI/Interaction.cs`(Update 加可选 focus 参数)、`src/SharpUI/Element.cs`(Focusable 字段)、`src/SharpUI/Elements/Ui.cs`(Focusable 扩展)、`src/SharpUI/Elements/ButtonElement.cs`(Focusable 默认 true + 聚焦色)、`src/SharpUI/App.cs`(Run 完整接线)
- Test: `tests/SharpUI.Tests/FocusTests.cs`

**Interfaces:**
- Consumes: `IdResolver.Assign`、`KeyEventData`、`ResolvedId`。
- Produces(M2b/c 依赖):
  - `Element` 新增:`public bool Focusable;`
  - `ElementExtensions` 新增:`public static T Focusable<T>(this T e) where T : Element`
  - `public sealed class FocusManager`:`string? FocusedId`、`Element? FocusedThisFrame`、`BeginFrame()`、`Register(Element)`、`SetFocus(Element)`、`IsFocused(Element) → bool`、`HandleKeys(ReadOnlySpan<KeyEventData>)`(Tab/Shift+Tab 循环、Enter/Space 激活聚焦 Button、末尾刷新 FocusedThisFrame)
  - `public static class TreeWalk`:`Register(Element root, FocusManager focus)`(父先子后 DOM 序)
  - `Interaction.Update(Element root, in RawInput raw, FocusManager? focus = null)`(按下命中 Focusable 元素时 `focus?.SetFocus(hovered)`)
  - `InputState.Focused` 由 App 在 HandleKeys 后用 `with` 填充;`ButtonElement` 聚焦色 `0x60A5FA`(优先于悬停/按压)

- [ ] **Step 1: 写失败测试 FocusTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class FocusTests
{
    static (PanelElement Root, ButtonElement First, ButtonElement Second) TwoButtons()
    {
        ButtonElement a = new("A", () => { });
        ButtonElement b = new("B", () => { });
        PanelElement root = Ui.Column(a, b);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;   // Atlas-null 下按钮自身仅 32px 宽,须拉伸全宽命中 x=50(裁定#16)
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        return (root, a, b);
    }

    static FocusManager Frame(PanelElement root)
    {
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        return focus;
    }

    [Fact]
    public void PressOnButton_FocusesIt()
    {
        (PanelElement root, ButtonElement a, _) = TwoButtons();
        FocusManager focus = Frame(root);
        Interaction.Update(root, new RawInput(new PointF(50, 5), new[] { true }), focus);
        Assert.True(focus.IsFocused(a));
    }

    [Fact]
    public void Tab_CyclesForwardAndBackward()
    {
        (PanelElement root, ButtonElement a, ButtonElement b) = TwoButtons();
        FocusManager focus = Frame(root);

        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a));
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(b));
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a));       // 环回
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, true) });
        Assert.True(focus.IsFocused(b));       // Shift+Tab 反向
    }

    [Fact]
    public void EnterOrSpace_ActivatesFocusedButton()
    {
        int clicks = 0;
        ButtonElement a = new("A", () => clicks++);
        ButtonElement b = new("B", () => { });
        PanelElement root = Ui.Column(a, b);
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        FocusManager focus = Frame(root);

        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });   // 聚焦 a
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Enter, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Space, true, false) });
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void Focus_SurvivesTreeRebuild()
    {
        (PanelElement root1, ButtonElement a1, _) = TwoButtons();
        FocusManager focus = Frame(root1);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a1));

        (PanelElement root2, ButtonElement a2, _) = TwoButtons();   // 全新实例,同结构同 id
        focus.BeginFrame();
        TreeWalk.Register(root2, focus);
        focus.HandleKeys(System.Array.Empty<KeyEventData>());
        Assert.NotNull(focus.FocusedThisFrame);
        Assert.Same(a2, focus.FocusedThisFrame);   // 焦点 id 解析到新树的同位按钮
    }

    [Fact]
    public void NonFocusableElement_IsNotInTabOrder()
    {
        TextElement t = Ui.Text("hi").Focusable();
        TextElement plain = Ui.Text("plain");
        PanelElement root = Ui.Column(t, plain);
        IdResolver.Assign(root);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.Equal(t.ResolvedId, focus.FocusedId);
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test --filter FullyQualifiedName~FocusTests`
Expected: 编译 FAIL(`FocusManager/TreeWalk/Focusable` 未定义)。

- [ ] **Step 3: 写实现 src/SharpUI/Focus.cs**

```csharp
using Veldrid;

namespace SharpUI;

/// <summary>焦点管理:焦点以稳定 id 跨帧保持;Tab 序为 DOM 序(父先子后)。</summary>
public sealed class FocusManager
{
    readonly List<string> _tabOrder = new();
    readonly Dictionary<string, Element> _byId = new();
    string? _focusedId;

    public string? FocusedId => _focusedId;
    public Element? FocusedThisFrame { get; private set; }

    public void BeginFrame()
    {
        _tabOrder.Clear();
        _byId.Clear();
    }

    public void Register(Element e)
    {
        if (string.IsNullOrEmpty(e.ResolvedId)) return;
        _byId[e.ResolvedId!] = e;
        if (e.Focusable) _tabOrder.Add(e.ResolvedId!);
    }

    public void SetFocus(Element e) => _focusedId = e.ResolvedId;

    public bool IsFocused(Element e) =>
        e.ResolvedId is not null && e.ResolvedId == _focusedId;

    public void HandleKeys(ReadOnlySpan<KeyEventData> keys)
    {
        foreach (KeyEventData ke in keys)
        {
            if (!ke.Down) continue;
            switch (ke.Key)
            {
                case Key.Tab:
                    Cycle(ke.Shift);
                    break;
                case Key.Enter:
                case Key.KeypadEnter:
                case Key.Space:
                    if (_focusedId is not null
                        && _byId.TryGetValue(_focusedId, out Element? e)
                        && e is ButtonElement button)
                    {
                        button.InvokeClick();
                    }
                    break;
            }
        }
        FocusedThisFrame = _focusedId is not null && _byId.TryGetValue(_focusedId, out Element? cur)
            ? cur
            : null;
    }

    void Cycle(bool backward)
    {
        if (_tabOrder.Count == 0) return;
        int index = _focusedId is null ? -1 : _tabOrder.IndexOf(_focusedId);
        index = backward
            ? (index <= 0 ? _tabOrder.Count - 1 : index - 1)
            : (index + 1) % _tabOrder.Count;
        _focusedId = _tabOrder[index];
    }
}

public static class TreeWalk
{
    public static void Register(Element root, FocusManager focus)
    {
        focus.Register(root);
        if (root.Children is not { Count: > 0 }) return;
        foreach (Element child in root.Children)
            Register(child, focus);
    }
}
```

- [ ] **Step 4: Element.cs 追加(放在 `Key` 之后)**

```csharp
    public bool Focusable;
```

- [ ] **Step 5: Ui.cs 扩展追加**

```csharp
    public static T Focusable<T>(this T e) where T : Element { e.Focusable = true; return e; }
```

- [ ] **Step 6: Interaction.cs 的 Update 加 focus 参数与按下聚焦**

签名改为 `public static InputState Update(Element root, in RawInput raw, FocusManager? focus = null)`;`if (down)` 分支内、`_pressedOn = hovered?.ResolvedId;` 之后追加:

```csharp
                if (hovered is { Focusable: true })
                    focus?.SetFocus(hovered);
```

- [ ] **Step 7: ButtonElement.cs——ctor 中 `IsHitTarget = true;` 之后追加 `Focusable = true;`;Draw 的 Background 三元链改为四段(聚焦优先)**

```csharp
        Background = pc.Input.Focused == this ? Color32.FromRgb(0x60A5FA)
            : pc.Input.Hovered == this
                ? (pc.Input.LeftDown ? Color32.FromRgb(0x1D4ED8) : Color32.FromRgb(0x3B82F6))
            : Color32.FromRgb(0x0C63E4);
```

- [ ] **Step 8: App.cs——Run 整体替换为最终接线**

```csharp
    public int Run(int maxFrames = 0)
    {
        Atlas = new TextAtlas(_ctx);
        State = new StateStore();
        var mesh = new MeshBuilder();
        var focus = new FocusManager();
        int frames = 0;
        while (_window.Exists)
        {
            InputSnapshot snapshot = _window.PumpEvents();
            Element root = _render is null ? new Spacer(SizeF.Zero) : _render();
            IdResolver.Assign(root);
            focus.BeginFrame();
            TreeWalk.Register(root, focus);
            SizeF screen = new(_window.Width, _window.Height);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            RawInput raw = new(
                new PointF(snapshot.MousePosition.X, snapshot.MousePosition.Y),
                LeftEvents(snapshot),
                MapKeys(snapshot));
            InputState input = Interaction.Update(root, in raw, focus);
            focus.HandleKeys(raw.KeyEvents ?? Array.Empty<KeyEventData>());
            input = input with { Focused = focus.FocusedThisFrame };
            mesh.Clear();
            root.Draw(new PaintContext(mesh, Atlas, input));
            _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer);
            _ctx.Device.SwapBuffers();
            State.Sweep();
            if (maxFrames > 0 && ++frames >= maxFrames)
                _window.Close();
        }
        return 0;
    }

    static KeyEventData[] MapKeys(InputSnapshot snapshot)
    {
        if (snapshot.KeyEvents.Count == 0) return Array.Empty<KeyEventData>();
        var keys = new KeyEventData[snapshot.KeyEvents.Count];
        for (int i = 0; i < snapshot.KeyEvents.Count; i++)
        {
            KeyEvent ke = snapshot.KeyEvents[i];
            keys[i] = new KeyEventData(ke.Key, ke.Down, ke.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        return keys;
    }
```

(`LeftEvents` 辅助保持不变;文件头已有 `using Veldrid;`。)

- [ ] **Step 9: 运行确认通过并全量回归**

Run: `dotnet test --filter FullyQualifiedName~FocusTests` 然后 `dotnet test`
Expected: 5 new + 全量 68/68(若 UiDslTests 等旧测试因 InputState 构造断言失败,只允许按 Interfaces 的默认参数形态修正构造调用,不得改断言语义)。

- [ ] **Step 10: 提交**

```bash
git add -A && git commit -m "feat: focus manager with tab cycle and keyboard activation"
```

---

### Task 6: 性能再基线 + 冒烟回归

**Files:**
- Modify: `tests/SharpUI.Tests/PerfTests.cs`(场景加 Key,基线行说明 id/焦点开销)
- Test: PerfTests.cs 本身

**Interfaces:**
- Consumes: 全部 M2a 公开面。
- Produces: 更新后的性能门禁与 `docs/notes/perf-baselines.md` 同路径的 M2a 基线数字(预算不变 64KB/8ms)。

- [ ] **Step 1: PerfTests 场景两个 Button 构造改为带键,帧函数计入 id/焦点管线**

`new ButtonElement("−", () => state--)` → `Ui.Button("−", () => state--).Key("minus")`;`"+"` 同理 `.Key("plus")`。在 `Frame()` 外创建单例 `var focus = new FocusManager();`(避免每帧分配污染计量);帧函数体内 `LayoutEngine.Arrange` 之前插入:

```csharp
            IdResolver.Assign(root);
```

`Arrange` 之后、`mesh.Clear()` 之前插入:

```csharp
            focus.BeginFrame();
            TreeWalk.Register(root, focus);
```

`Interaction.Update(root, in raw)` 与 `root.Draw(new PaintContext(mesh, atlas, InputState.Empty))` 保持不变(场景无交互,焦点保持空转)。

- [ ] **Step 2: Release 运行记录数字**

Run: `dotnet test -c Release --filter FullyQualifiedName~PerfTests`
Expected: PASS;把实测 bytes/frame 与 ms/frame 写进报告(基线文件由测试写盘,含 id/焦点管线)。

- [ ] **Step 3: Debug 全量回归 + Counter 冒烟**

Run: `dotnet test`(68/68);临时 `Run(maxFrames: 30)` 冒烟 `dotnet run -c Debug --project examples/Counter`(窗口闪现、退出码 0、无异常)后恢复 verbatim Program.cs。

- [ ] **Step 4: 提交**

```bash
git add -A && git commit -m "test: m2a perf baseline with id and focus pipeline active"
```

---

## 任务依赖图

```
Task1(独立硬化)
Task2 → Task3 → Task4 → Task5 → Task6
(Task4 消费 Task2 的 ResolvedId 并顺带落地 KeyEventData;Task5 消费 Task3/4 全部。)
```

串行执行;Task 1 可与 Task 2 无序,但保持串行最稳。

## M2a 完成定义

- [ ] `dotnet test` 与 `dotnet test -c Release` 全绿(68/68,含 GPU)
- [ ] 性能门禁绿,基线数字记录(id+焦点管线计入)
- [ ] Counter 冒烟通过(视觉验收仍归用户)
- [ ] 零 `unsafe`、零自有 AOT 警告、公开面与各任务 Interfaces 一致
