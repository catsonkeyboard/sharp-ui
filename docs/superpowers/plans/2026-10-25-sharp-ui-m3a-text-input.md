# SharpUI M3a 文本输入 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 字符通道与键路由(D22)、TextField(纯函数编辑语义 + 元素壳)、shaping LRU 缓存、性能与冒烟。

**Architecture:** 编辑语义 = 纯静态类 `TextEditing.Apply(TextState, keys, chars) → TextState`(零 UI 依赖,全单测);`TextFieldElement` 是壳:状态键 "tf:"+ResolvedId 绑 StateStore,Feed 在 App 构建前经 `TreeWalk.Find(lastTree, FocusedId)` 定位聚焦元素后调用(改状态→本帧构建即读到新文本)。shaping LRU 以 (text,size,bold) 为键缓存逻辑宽与分 run 位置快照,Measure/DrawRun 共用。

**Tech Stack:** 零新增包。

**Spec:** M3 阶段表 + D22。

## Global Constraints

- 沿用全部既有约束;性能预算不放宽;`dotnet test` 全绿为提交前置;新增公开成员仅限 Interfaces;既有测试零语义改动。

---

### Task 1: 字符通道与键路由基建

**Files:**
- Modify: `src/SharpUI/Interaction.cs`(RawInput.Chars、KeyEventData.Ctrl)、`src/SharpUI/InputMapper.cs`(映射 chars 与 Ctrl)、`src/SharpUI/Focus.cs`(TreeWalk.Find)、`src/SharpUI/App.cs`(构建前路由)
- Test: `tests/SharpUI.Tests/InputMapperTests.cs`(追加 2)

**Interfaces:**
- Produces:
  - `RawInput(PointF, bool[], KeyEventData[]? KeyEvents = null, bool LeftHeld = false, float WheelDelta = 0f, char[]? Chars = null)`
  - `KeyEventData(Key Key, bool Down, bool Shift, bool Ctrl = false)`
  - `InputMapper.Map` 传 `snapshot.KeyCharPresses`(IReadOnlyList<char>→ToArray,空则 null)与 Ctrl 位
  - `TreeWalk.Find(Element root, string? id) → Element?`
  - `App.Run` 构建前:`FeedFocused(lastTree, focus, raw);`(新私有静态:Find(focus.FocusedId) 后 `if (el is TextFieldElement tf) tf.Feed(raw.KeyEvents, raw.Chars);`——TextFieldElement 在 T3 定义,本任务以接口前瞻:先落 `internal interface ITextFeedable { void Feed(KeyEventData[]? keys, char[]? chars); }`,App 路由判接口,T3 的 TextFieldElement 实现之)

- [ ] **Step 1: 追加测试(InputMapper Ctrl 位;TreeWalk.Find)**

```csharp
    [Fact]
    public void MapKeys_ExtractsCtrl()
    {
        KeyEventData[] keys = InputMapper.MapKeys(new[]
        {
            new KeyEvent(Key.A, true, ModifierKeys.Control),
            new KeyEvent(Key.B, true, ModifierKeys.Control | ModifierKeys.Shift),
        });
        Assert.True(keys[0].Ctrl);
        Assert.True(keys[1].Shift && keys[1].Ctrl);
    }
```

```csharp
// FocusTests.cs 追加:
    [Fact]
    public void TreeWalkFind_LocatesById()
    {
        (PanelElement root, ButtonElement a, ButtonElement b) = TwoButtons();
        a.Key = "alpha"; b.Key = "beta";
        IdResolver.Assign(root);
        Assert.Same(a, TreeWalk.Find(root, a.ResolvedId));
        Assert.Same(b, TreeWalk.Find(root, "/beta"));
        Assert.Null(TreeWalk.Find(root, "/missing"));
    }
```

- [ ] **Step 2: RED → 实现(签名扩展全默认参;MapKeys 加 `ke.Modifiers.HasFlag(ModifierKeys.Control)`;TreeWalk.Find 深度优先先子后己按 ResolvedId 匹配;App.FeedFocused 按上述)→ GREEN → 全量 118/118**
- [ ] **Step 3: 提交** `git add -A && git commit -m "feat: char channel ctrl flag and focused-key routing"`

---

### Task 2: TextEditing 纯函数(编辑语义)

**Files:**
- Create: `src/SharpUI/TextEditing.cs`
- Test: `tests/SharpUI.Tests/TextEditingTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TextState(string Text = "", int Cursor = 0, int Anchor = 0);`
  - `public static class TextEditing { public static TextState Apply(TextState s, KeyEventData[]? keys, char[]? chars); public static (int Start, int Len) Selection(TextState s); }`(Selection 返回有序选区)

- [ ] **Step 1: 写失败测试(10 个)**

```csharp
using SharpUI;
using Veldrid;

namespace SharpUI.Tests;

public class TextEditingTests
{
    static KeyEventData Key(Veldrid.Key k, bool shift = false, bool ctrl = false) => new(k, true, shift, ctrl);

    [Fact] public void Chars_AppendAtCursor()
    {
        var s = TextEditing.Apply(new TextState("ab", 1), null, "XY".ToCharArray());
        Assert.Equal(new TextState("aXYb", 3), s);
    }

    [Fact] public void Chars_ReplaceSelection()
    {
        var s = TextEditing.Apply(new TextState("abcd", 1, 3), null, "Z".ToCharArray());
        Assert.Equal(new TextState("aZd", 2), s);
    }

    [Fact] public void Backspace_DeleteBeforeCursor()
    {
        var s = TextEditing.Apply(new TextState("ab", 1), new[] { Key(Key.BackSpace) }, null);
        Assert.Equal(new TextState("b", 0), s);
    }

    [Fact] public void Backspace_DeleteSelection()
    {
        var s = TextEditing.Apply(new TextState("abcd", 3, 1), new[] { Key(Key.BackSpace) }, null);
        Assert.Equal(new TextState("ad", 1), s);
    }

    [Fact] public void Delete_DeleteAfterCursor()
    {
        var s = TextEditing.Apply(new TextState("ab", 1), new[] { Key(Key.Delete) }, null);
        Assert.Equal(new TextState("a", 1), s);
    }

    [Fact] public void Arrows_MoveAndSelect()
    {
        var s = new TextState("abc", 1);
        Assert.Equal(new TextState("abc", 2), TextEditing.Apply(s, new[] { Key(Key.Right) }, null));
        Assert.Equal(new TextState("abc", 0), TextEditing.Apply(s, new[] { Key(Key.Left) }, null));
        Assert.Equal(new TextState("abc", 2, 1), TextEditing.Apply(s, new[] { Key(Key.Right, shift: true) }, null));
        Assert.Equal(new TextState("abc", 3), TextEditing.Apply(s, new[] { Key(Key.End) }, null));
        Assert.Equal(new TextState("abc", 3, 1), TextEditing.Apply(s, new[] { Key(Key.End, shift: true) }, null));
    }

    [Fact] public void CtrlA_SelectAllThenReplace()
    {
        var s = TextEditing.Apply(new TextState("abc", 2), new[] { Key(Key.A, ctrl: true) }, null);
        Assert.Equal(new TextState("abc", 3, 0), s);
        Assert.Equal(new TextState("XY", 2), TextEditing.Apply(s, null, "XY".ToCharArray()));
    }

    [Fact] public void Selection_IsOrdered()
    {
        Assert.Equal((1, 2), TextEditing.Selection(new TextState("abcd", 3, 1)));
        Assert.Equal((1, 2), TextEditing.Selection(new TextState("abcd", 1, 3)));
        Assert.Equal((2, 0), TextEditing.Selection(new TextState("abcd", 2)));
    }

    [Fact] public void UnknownKeys_Ignored()
    {
        var s = new TextState("ab", 1);
        Assert.Equal(s, TextEditing.Apply(s, new[] { Key(Key.F5), new KeyEventData(Key.Enter, false, false) }, null));
    }

    [Fact] public void Chars_EmptyIsNoop_CursorClamped()
    {
        var s = new TextState("ab", 9);
        Assert.Equal(s with { Cursor = 2 }, TextEditing.Apply(s, null, null));
    }
}
```

- [ ] **Step 2: RED → 实现**

```csharp
namespace SharpUI;

public sealed record TextState(string Text = "", int Cursor = 0, int Anchor = 0);

/// <summary>单行文本编辑纯函数(D22):位置语义 = 字符索引;选区 = [min(Cursor,Anchor), max)。}</summary>
public static class TextEditing
{
    public static (int Start, int Len) Selection(TextState s)
    {
        int a = Math.Min(s.Cursor, s.Anchor), b = Math.Max(s.Cursor, s.Anchor);
        return (a, b - a);
    }

    public static TextState Apply(TextState s, KeyEventData[]? keys, char[]? chars)
    {
        int cursor = Math.Clamp(s.Cursor, 0, s.Text.Length);
        int anchor = Math.Clamp(s.Anchor, 0, s.Text.Length);
        string text = s.Text;
        foreach (char c in chars ?? Array.Empty<char>())
        {
            (int start, int len) = Selection(new TextState(text, cursor, anchor));
            text = text.Remove(start, len).Insert(start, c.ToString());
            cursor = anchor = start + 1;
        }
        if (keys is not null)
            foreach (KeyEventData k in keys)
            {
                if (!k.Down) continue;
                (int start, int len) = Selection(new TextState(text, cursor, anchor));
                if (k.Ctrl && k.Key == Key.A)
                {
                    cursor = text.Length; anchor = 0;
                }
                else if (k.Key == Key.BackSpace)
                {
                    if (len > 0) { text = text.Remove(start, len); cursor = anchor = start; }
                    else if (cursor > 0) { text = text.Remove(cursor - 1, 1); cursor = anchor = cursor - 1; }
                }
                else if (k.Key == Key.Delete)
                {
                    if (len > 0) { text = text.Remove(start, len); cursor = anchor = start; }
                    else if (cursor < text.Length) text = text.Remove(cursor, 1);
                }
                else if (k.Key == Key.Left) { int t = Math.Max(0, cursor - 1); cursor = t; if (!k.Shift) anchor = t; }
                else if (k.Key == Key.Right) { int t = Math.Min(text.Length, cursor + 1); cursor = t; if (!k.Shift) anchor = t; }
                else if (k.Key == Key.Home) { cursor = 0; if (!k.Shift) anchor = 0; }
                else if (k.Key == Key.End) { cursor = text.Length; if (!k.Shift) anchor = text.Length; }
            }
        return new TextState(text, Math.Clamp(cursor, 0, text.Length), Math.Clamp(anchor, 0, text.Length));
    }
}
```

**实现指令**:`using Veldrid;` 于文件头(枚举比较需要);`Key.A` 在 Veldrid 枚举为 `Key.A`(若名异,以编译器为准并报告)。上块 doc 注释中的 `}` 笔误删除。
- [ ] **Step 3: GREEN → 全量 128/128 → 提交** `git add -A && git commit -m "feat: pure text editing semantics with selection"`

---

### Task 3: TextFieldElement(壳)

**Files:**
- Create: `src/SharpUI/Elements/TextFieldElement.cs`
- Modify: `src/SharpUI/Elements/Ui.cs`(工厂)
- Test: `tests/SharpUI.Tests/TextFieldTests.cs`

**Interfaces:**
- Produces:
  - `sealed class TextFieldElement : PanelElement, ITextFeedable`:`ctor(string placeholder = "")`;`Feed(KeyEventData[]?, char[]?)`(StateStore "tf:"+ResolvedId ← TextEditing.Apply);`CurrentText`(internal,测试用);MeasureSelf(占位/文本宽+左右 padding 8);Draw(底 0x101014、圆角 6、聚焦描边 0x60A5FA[1px 内缩矩形]、文本 DrawRun、选区半透明 0x3B82F6@96α 覆盖、光标 1×行高 白);Focusable=IsHitTarget=true
  - `Ui.TextField(string placeholder = "")`

- [ ] **Step 1: 失败测试(状态往返 + Feed 语义 + 度量 + 绘制含光标)**

```csharp
using SharpUI;
using static SharpUI.Ui;

namespace SharpUI.Tests;

public class TextFieldTests
{
    static (TextFieldElement tf, PanelElement root) Field(string text = "", string key = "f")
    {
        TextFieldElement tf = TextField("ph");
        tf.Key = key;
        tf.Focusable();
        PanelElement root = Column(tf);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        App.State = new StateStore();
        tf.SetStateForTest(new TextState(text, text.Length));
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 30));
        IdResolver.Assign(root);
        return (tf, root);
    }

    [Fact]
    public void Feed_CharsPersistThroughStateStore()
    {
        (TextFieldElement tf, PanelElement root) = Field();
        tf.Feed(new[] { new KeyEventData(Veldrid.Key.A, true, false) }, "hi".ToCharArray());   // 键被忽略,字符入文
        tf.Feed(null, "!".ToCharArray());
        Assert.Equal("hi!", tf.CurrentText);
        App.State = null;
    }

    [Fact]
    public void Feed_BackspaceAndArrows()
    {
        (TextFieldElement tf, _) = Field("abc");
        tf.Feed(new[] { new KeyEventData(Veldrid.Key.Left, true, false) }, null);
        tf.Feed(new[] { new KeyEventData(Veldrid.Key.Backspace, true, false) }, null);
        Assert.Equal("ac", tf.CurrentText);
        App.State = null;
    }

    [Fact]
    public void Measure_PadsText()
    {
        (TextFieldElement tf, _) = Field("hello");
        SizeF m = LayoutEngine.Measure(tf, new SizeF(500, 40));
        Assert.True(m.Width > 20f);
        App.State = null;
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Draw_IncludesCursorAndBackgroundQuads()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        (TextFieldElement tf, PanelElement root) = Field("ab");
        var mesh = new MeshBuilder();
        root.Draw(new PaintContext(mesh, atlas, InputState.Empty));
        Assert.True(mesh.VertexCount >= 3 * 4);   // 底/描边 + 文本 + 光标 ≥3 四边
        App.Atlas = null;
        App.State = null;
    }
}
```

(`SetStateForTest` 为 internal 测试辅助:`internal void SetStateForTest(TextState s) => App.State!.Set("tf:" + ResolvedId, s);`——注意 ResolvedId 需先 Assign;Field 辅助里 SetStateForTest 在 Assign 之前调用会得到 null id!**实现指令:Field 辅助把 SetStateForTest 移到 IdResolver.Assign 之后**。)

- [ ] **Step 2: RED → 实现(完整文件)**

```csharp
namespace SharpUI;

/// <summary>单行文本框(D22):编辑语义全在 TextEditing;本类只做状态绑定/度量/绘制。状态键 tf:+id。</summary>
public sealed class TextFieldElement : PanelElement, ITextFeedable
{
    readonly string _placeholder;
    public const float PadX = 8f;

    public TextFieldElement(string placeholder = "")
    {
        _placeholder = placeholder;
        Focusable = true;
        IsHitTarget = true;
        CornerRadius = 6f;
        Background = Color32.FromRgb(0x101014);
        Style.Padding = new Thickness(horizontal: PadX, vertical: 6f);
    }

    internal TextState State => App.State is { } st
        ? st.Get<TextState>("tf:" + ResolvedId)
        : new TextState();

    internal void SetStateForTest(TextState s) => App.State!.Set("tf:" + ResolvedId, s);
    internal string CurrentText => State.Text;

    public void Feed(KeyEventData[]? keys, char[]? chars)
    {
        if (App.State is not { } st) return;
        st.Set("tf:" + ResolvedId, TextEditing.Apply(State, keys, chars));
    }

    protected internal override SizeF MeasureSelf(SizeF available)
    {
        if (App.Atlas is null) return new SizeF(80f, 28f);
        string t = State.Text.Length > 0 ? State.Text : _placeholder;
        SizeF m = App.Atlas.Measure(t, 14f);
        return new SizeF(m.Width + PadX * 2f, m.Height + 12f);
    }

    public override void Draw(in PaintContext pc)
    {
        if (Frame.IsEmpty) { base.Draw(pc); return; }
        bool focused = pc.Input.FocusedId == ResolvedId;
        Background = focused ? Color32.FromRgb(0x16161D) : Color32.FromRgb(0x101014);
        RectF parentClip = pc.Mesh.CurrentClip;
        pc.Mesh.SetClip(Frame.Intersect(parentClip));
        Tessellator.AddRoundedRect(pc.Mesh, Frame, CornerRadius, Background,
            new PointF(pc.WhiteUv.X + 0.0001f, pc.WhiteUv.Y + 0.0001f));
        if (focused)
            Tessellator.AddRoundedRect(pc.Mesh, Frame.Deflate(new Thickness(1f)), CornerRadius - 1f,
                Color32.FromRgb(0x60A5FA).WithAlpha(255),
                new PointF(pc.WhiteUv.X + 0.0001f, pc.WhiteUv.Y + 0.0001f));   // 描边近似:内缩实心覆盖底色
        TextState st = State;
        string text = st.Text.Length > 0 ? st.Text : _placeholder;
        if (text.Length > 0 && App.Atlas is { } atlas)
        {
            (int selStart, int selLen) = TextEditing.Selection(st);
            if (selLen > 0 && st.Text.Length > 0)
            {
                float before = atlas.Measure(st.Text[..selStart], 14f).Width;
                float selW = atlas.Measure(st.Text.Substring(selStart, selLen), 14f).Width;
                pc.Mesh.AddQuad(
                    new RectF(Frame.X + PadX + before, Frame.Y + 4f, selW, Frame.Height - 8f),
                    pc.WhiteUv, Color32.FromRgb(0x3B82F6).WithAlpha(96));
            }
            float textTop = Frame.Y + (Frame.Height - atlas.Measure(st.Text.Length > 0 ? st.Text : _placeholder, 14f).Height) / 2f;
            atlas.DrawRun(pc.Mesh, text, new PointF(Frame.X + PadX, textTop), 14f, false, Color32.White);
            if (focused)
            {
                float cursorX = Frame.X + PadX + atlas.Measure(st.Text[..st.Cursor], 14f).Width;
                pc.Mesh.AddQuad(new RectF(cursorX, Frame.Y + 4f, 1f, Frame.Height - 8f),
                    pc.WhiteUv, Color32.White);
            }
        }
        if (Children is not null)
            foreach (Element child in Children)
                child.Draw(in pc);
        pc.Mesh.SetClip(parentClip);
    }
}
```

**实现指令(必读)**:① 上述 Draw 的"描边近似"会盖住底色——改为**先画外圈描边圆角矩形(全帧)再画内缩底色**(外 0x60A5FA 半径 6,内底色 Frame.Deflate(Thickness(1)) 半径 5),文本/选区/光标照旧;② `ITextFeedable` 在 Task 1 已落 `internal interface ITextFeedable { void Feed(KeyEventData[]? keys, char[]? chars); }`(Interaction.cs 或独立文件);③ 选区/文本度量多次调用 Measure——性能由 T4 的 shaping LRU 吸收;④ 文本颜色:占位文本用 0x666B78,正文白——Draw 里按 `st.Text.Length > 0` 分色。⑤ `Ui.TextField(string placeholder = "") => new(placeholder);`
- [ ] **Step 3: GREEN → 全量 132/132 → 提交** `git add -A && git commit -m "feat: text field element with cursor and selection rendering"`

---

### Task 4: shaping LRU 缓存 + Demo

**Files:**
- Modify: `src/SharpUI/Elements/TextAtlas.cs`(ShapedRun 缓存)、`examples/Counter/Program.cs`(可编辑行)
- Test: `tests/SharpUI.Tests/TextAtlasTests.cs`(追加 2)

**Interfaces:**
- Produces: TextAtlas 内部 `ShapedCache`:键 `(string Text, int Size, bool Bold)`,值 `ShapedLayout`(逻辑总宽 + 每 run 的(slot、Codepoints/Points/Clusters 快照数组、逻辑宽));容量超 512 全清;`Measure`/`DrawRun` 均走缓存;公开面零变化。

- [ ] **Step 1: 追加测试**

```csharp
    [Fact]
    [Trait("Category", "Gpu")]
    public void ShapedCache_ReuseYieldsIdenticalLayout()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var m1 = new MeshBuilder();
        atlas.DrawRun(m1, "cache me 你", new PointF(0, 0), 16f, false, Color32.White);
        var m2 = new MeshBuilder();
        atlas.DrawRun(m2, "cache me 你", new PointF(0, 0), 16f, false, Color32.White);
        Assert.Equal(m1.VertexCount, m2.VertexCount);
        Assert.Equal(atlas.Measure("cache me 你", 16f).Width, atlas.Measure("cache me 你", 16f).Width);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void ShapedCache_EvictsPastCap()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        for (int i = 0; i < 600; i++)
            atlas.Measure($"unique-{i}", 12f);      // 超容量全清,不抛不漏
        Assert.True(atlas.Measure("unique-0", 12f).Width > 0f);
    }
```

- [ ] **Step 2: RED→实现**:Measure/DrawRun 内部先查缓存(`GetShaped(text,size,bold)`);未命中则 SplitRuns+逐 run Shape+快照(Points/Codepoints/Clusters 拷贝为数组,逻辑宽=r.Width/_scale)入缓存;DrawRun 用快照位置输出四边形(逻辑除法已在快照时完成:快照存 Points/逻辑坐标);Measure 返回(缓存总宽,行高)。缓存条目含 glyph 循环所需全部数据,DrawRun 不再调 shaper。
- [ ] **Step 3: Program.cs 追加(Immediate 面板之前)**:`Ui.TextField("type here…").Key("edit"),`
- [ ] **Step 4: Release 记录数字(Counter 场景)→ Debug 全量 134/134 → 冒烟(maxFrames)→ 恢复 → 提交** `git add -A && git commit -m "feat: shaped run lru cache and editable demo field"`

---

### Task 5: 收官性能与冒烟

**Files:** 无源码改动(验证 + 基线)
- [ ] **Step 1:** `dotnet test -c Release --filter FullyQualifiedName~PerfTests` 记录(shaping LRU 后 Counter 场景预期显著低于 17.5KB)
- [ ] **Step 2:** Debug 全量 134/134;冒烟:输入框点击聚焦、打字(含中文 IME 提交)、Backspace、Shift+← 选区、Ctrl+A;恢复 verbatim
- [ ] **Step 3:** 提交基线 `git add -A && git commit -m "test: m3a perf baseline with shaped run cache"`

---

## 依赖: T1 → T2 → T3 → T4 → T5(T2 纯函数仅依赖 KeyEventData,可与 T1 后并行,仍串行)

## M3a 完成定义
- [ ] 134/134 全绿(含 GPU);预算不放宽且 Counter 场景分配较 17.5KB/帧显著下降(记录数字)
- [ ] Demo:输入框可打字/退格/选区/Ctrl+A;中文 IME 提交文本可见(组合窗口定位为 M3c)
- [ ] 零 unsafe;公开面与 Interfaces 一致
