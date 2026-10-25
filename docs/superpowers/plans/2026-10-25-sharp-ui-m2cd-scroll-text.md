# SharpUI M2c+M2d 收官计划(滚动列表 + 文本升级)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 滚轮通道与滚动容器(D19)、虚拟化列表(D20)、HarfBuzz shaping + 字体回退 + 中文/emoji(D21)、SDF 评估文档、性能与冒烟——M2 收官(除需 remote 的 M2e)。

**Architecture:** 滚轮在构建前对上帧保留树做深度优先分发(最深可滚者赢);`ScrollElement` 经新的 `ArrangeChildren` 虚挂钩实现偏移堆叠与内容度量缓存;`VirtualListElement` 在 Arrange 时按可见窗重建行(Key=索引)并调用 `IdResolver.AssignChildren` 保自 id 重排子树。文本:字形位图不动,**位置由 shaping 提供**——按 ContainsGlyph 回退链分 run,每 run `SKShaper.Shape(text,x,y,font)`,光栅化改用非废弃 `SKTextAlign.Left` 重载。

**Tech Stack:** 新增包 `SkiaSharp.HarfBuzz 4.153.1`(传递 HarfBuzzSharp 14.2.1.301,已探针解析干净)。

**Spec:** D19/D20/D21(2026-10-25 已入档);附录 A 的 SkiaSharp 4 事实:无 glyph 级画布绘制,`SKShaper.Shape(string,float,float,SKFont)` 四参与 `Result{Codepoints,Clusters,Points,Width}`、`SKFont.ContainsGlyph(uint)`、`SKTextBlob.CreatePositioned` 均已验证。

## Global Constraints

- 沿用全部既有约束;性能预算不放宽;`dotnet test` 全绿为提交前置;新增包仅 SkiaSharp.HarfBuzz 4.153.1。
- 既有测试零语义改动(构造调用形态可随签名默认参扩展)。
- 新增公开成员仅限各任务 Interfaces。

---

### Task 1+2(合并派发): 滚轮通道 + ScrollElement

**Files:**
- Create: `src/SharpUI/Scroll.cs`
- Modify: `src/SharpUI/Interaction.cs`(RawInput/InputState 加 WheelDelta)、`src/SharpUI/InputMapper.cs`(传 WheelDelta)、`src/SharpUI/Layout.cs`(ArrangeChildren 虚挂钩 + ComputeLines internal + AssignChildren 提取)、`src/SharpUI/Element.cs`(虚挂钩声明)、`src/SharpUI/App.cs`(分发接线)、`src/SharpUI/Elements/Ui.cs`(Scroll 工厂)
- Test: `tests/SharpUI.Tests/ScrollTests.cs`

**Interfaces:**
- Produces:
  - `RawInput(PointF, bool[], KeyEventData[]?, bool LeftHeld = false, float WheelDelta = 0f)`;`InputState` 尾参 `float WheelDelta = 0f`(Update 透传)
  - `Element` 新增 `protected internal virtual void ArrangeChildren(RectF inner)`(默认空——LayoutEngine.Arrange 的行布局循环整体移入 LayoutEngine 静态默认实现 `LayoutEngine.ArrangeDefault(Element, RectF)`? **实现指令:Element.ArrangeChildren 默认调用 `LayoutEngine.ArrangeLines(this, inner)`(原循环原样搬家),ScrollElement 覆写**)
  - `class ScrollElement : PanelElement`:`ContentMain/ViewportMain`(internal float)、`ScrollBy(float)`、`StateOffset()`(internal);`Ui.Scroll(params Element?[])`
  - `static class Scroll { public static void Dispatch(Element root, in InputState input); }`(子先父后最深者赢;不可滚的下渗)
  - `IdResolver.AssignChildren(Element e)`(internal,保自 id 只排子树)
  - App.Run:Interaction.Update 之后、构建之前 `if (input.WheelDelta != 0f && lastTree is not null) Scroll.Dispatch(lastTree, in input);`

- [ ] **Step 1: 写失败测试 ScrollTests.cs**

```csharp
using SharpUI;
using static SharpUI.Ui;

namespace SharpUI.Tests;

public class ScrollTests
{
    static ScrollElement Tall(int rows, float rowH = 30f)
    {
        ScrollElement s = Scroll();
        for (int i = 0; i < rows; i++)
            s.Add(new Spacer(new SizeF(50, rowH)));
        return s;
    }

    [Fact]
    public void WheelDelta_FlowsThroughInputState()
    {
        PanelElement root = Panel();
        LayoutEngine.Arrange(root, new RectF(0, 0, 10, 10));
        IdResolver.Assign(root);
        InputState s = Interaction.Update(root, new RawInput(new PointF(5, 5), Array.Empty<bool>(), WheelDelta: 1.5f));
        Assert.Equal(1.5f, s.WheelDelta);
    }

    [Fact]
    public void Wheel_ScrollsAndClamps()
    {
        App.State = new StateStore();
        ScrollElement s = Tall(10);                       // 300 高内容
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));
        string id = s.ResolvedId!;
        Scroll.Dispatch(s, new InputState(new PointF(50, 50), false, false, false, null, WheelDelta: 1f));
        Assert.Equal(40f, App.State!.Get("scroll:" + id, 0f));
        Scroll.Dispatch(s, new InputState(new PointF(50, 50), false, false, false, null, WheelDelta: 100f));
        Assert.Equal(200f, App.State.Get("scroll:" + id, 0f));   // 钳 300-100
        App.State = null;
    }

    [Fact]
    public void WheelOutsideFrame_Ignored()
    {
        App.State = new StateStore();
        ScrollElement s = Tall(10);
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));
        Scroll.Dispatch(s, new InputState(new PointF(500, 500), false, false, false, null, WheelDelta: 1f));
        Assert.Equal(0f, App.State!.Get("scroll:" + s.ResolvedId!, 0f));
        App.State = null;
    }

    [Fact]
    public void DeepestScrollable_Wins()
    {
        App.State = new StateStore();
        ScrollElement inner = Tall(10).Basis(100f);   // D19:滚动测量按内容,须显式限高才可滚
        ScrollElement outer = Scroll(new Spacer(new SizeF(50, 500)), inner);   // 外层也含长内容
        LayoutEngine.Arrange(outer, new RectF(0, 0, 200, 100));
        PointF p = new(inner.Frame.X + 10, inner.Frame.Y + 10);                 // 内层内
        Scroll.Dispatch(outer, new InputState(p, false, false, false, null, WheelDelta: 1f));
        Assert.Equal(40f, App.State!.Get("scroll:" + inner.ResolvedId!, 0f));
        Assert.Equal(0f, App.State.Get("scroll:" + outer.ResolvedId!, 0f));
        App.State = null;
    }

    [Fact]
    public void NonScrollableInner_FallsThroughToOuter()
    {
        App.State = new StateStore();
        var inner = Panel();  inner.Add(new Spacer(new SizeF(50, 20)));         // 短内容,不滚
        ScrollElement outer = Scroll(inner, new Spacer(new SizeF(50, 900)));
        LayoutEngine.Arrange(outer, new RectF(0, 0, 200, 100));
        Scroll.Dispatch(outer, new InputState(new PointF(50, 10), false, false, false, null, WheelDelta: 1f));
        Assert.Equal(40f, App.State!.Get("scroll:" + outer.ResolvedId!, 0f));
        App.State = null;
    }

    [Fact]
    public void ArrangedContent_ShiftsByOffset()
    {
        App.State = new StateStore();
        ScrollElement s = Tall(10);
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));
        Scroll.Dispatch(s, new InputState(new PointF(50, 50), false, false, false, null, WheelDelta: 1f));   // offset=40
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));                     // 重排应用偏移
        Assert.Equal(-40f, s.Children![0].Frame.Y);
        Assert.Equal(-10f, s.Children[1].Frame.Y);
        App.State = null;
    }
}
```

- [ ] **Step 2: 确认失败** → Run: `dotnet test --filter FullyQualifiedName~ScrollTests`
- [ ] **Step 3: 实现**
  - Interaction.cs:RawInput/InputState 尾参 + Update 返回 `WheelDelta: raw.WheelDelta`(命名参);InputMapper.Map 末参传 `snapshot.WheelDelta`
  - Layout.cs:`Arrange` 的行布局循环提取为 `internal static void ArrangeLines(Element e, RectF inner)`(原样搬家);`ComputeLines` 改 internal;Element.cs 加 `protected internal virtual void ArrangeChildren(RectF inner) => LayoutEngine.ArrangeLines(this, inner);`——**注意 Element.cs 无法引用? 同程序集 ✓**;LayoutEngine.Arrange 尾段改调 `e.ArrangeChildren(inner)`;**既有 6 布局测试必须零改动绿(纯搬家)**
  - Ids.cs:提取 `internal static void AssignChildren(Element e)`(Assign 的子循环体;Assign 调它)
  - Scroll.cs(新):

```csharp
namespace SharpUI;

/// <summary>竖向滚动容器(D19):偏移存 StateStore["scroll:"+id];Arrange 时内容上移 offset;wheel 分发由 App 调 Scroll.Dispatch。</summary>
public class ScrollElement : PanelElement
{
    internal float ContentMain;
    internal float ViewportMain;

    internal float StateOffset() => App.State?.Get("scroll:" + ResolvedId, 0f) ?? 0f;

    internal void ScrollBy(float delta)
    {
        float max = MathF.Max(0f, ContentMain - ViewportMain);
        App.State?.Set("scroll:" + ResolvedId, Math.Clamp(StateOffset() + delta, 0f, max));
    }

    protected internal override void ArrangeChildren(RectF inner)
    {
        if (Children is not { Count: > 0 }) { ContentMain = ViewportMain = 0f; return; }
        int n = Children.Count;
        var childMain = new float[n];
        var childCross = new float[n];
        var lineOf = new int[n];
        var lineMain = new float[n];
        var lineCross = new float[n];
        int lines = LayoutEngine.ComputeLines(this, float.MaxValue, inner.Width,
            childMain, childCross, lineOf, lineMain, lineCross);   // 主轴无限 → 单行内容度量
        float content = lineMain[0];
        ContentMain = content;
        ViewportMain = inner.Height;
        float offset = Math.Clamp(StateOffset(), 0f, MathF.Max(0f, content - inner.Height));
        float y = inner.Top - offset;
        for (int i = 0; i < n; i++)
        {
            LayoutEngine.Arrange(Children[i], new RectF(inner.X, y, inner.Width, childMain[i]));
            y += childMain[i] + Style.Gap;
        }
    }
}

/// <summary>滚轮分发:深度优先,最深可滚者赢(D19)。</summary>
public static class Scroll
{
    public static void Dispatch(Element root, in InputState input)
    {
        if (input.WheelDelta != 0f)
            DispatchDeep(root, input.Mouse, input.WheelDelta);
    }

    static bool DispatchDeep(Element e, PointF mouse, float delta)
    {
        if (!e.Frame.Contains(mouse)) return false;
        if (e.Children is not null)
            for (int i = e.Children.Count - 1; i >= 0; i--)
                if (DispatchDeep(e.Children[i], mouse, delta)) return true;
        if (e is ScrollElement { ContentMain: > 0 } s && s.ContentMain > s.ViewportMain)
        {
            s.ScrollBy(delta * 40f);
            return true;
        }
        return false;
    }
}
```

  - Ui.cs 工厂:`public static ScrollElement Scroll(params Element?[] children)`(与 Container 同构,但 new ScrollElement)
  - App.Run:`Interaction.Update(...)` 与 `UiContext.CurrentInput = input;` 之间插 `if (input.WheelDelta != 0f && lastTree is not null) Scroll.Dispatch(lastTree, in input);`

- [ ] **Step 4: 通过 + 全量 107/107** → Run: `dotnet test`
- [ ] **Step 5: 提交** `git add -A && git commit -m "feat: wheel channel and scroll containers with deepest-wins dispatch"`

---

### Task 3: VirtualListElement

**Files:**
- Modify: `src/SharpUI/Scroll.cs`(追加类)、`src/SharpUI/Elements/Ui.cs`(工厂)
- Test: `tests/SharpUI.Tests/ScrollTests.cs`(追加 3)

**Interfaces:**
- Produces:
  - `VirtualListElement(string key, int count, float itemHeight, Action<int, PanelElement> render) : ScrollElement`(Key=key;行=PanelElement{Key=索引串,CrossAlign=Stretch};±1 过扫;Arrange 时重建行+定位+`IdResolver.AssignChildren(this)`;ContentMain=count×itemHeight)
  - `Ui.VirtualList(string key, int count, float itemHeight, Action<int, PanelElement> render)`

- [ ] **Step 1: 追加失败测试**

```csharp
    [Fact]
    public void VirtualList_BuildsOnlyVisibleWindow()
    {
        App.State = new StateStore();
        VirtualListElement list = Ui.VirtualList("rows", 100, 24f, static (i, row) =>
            row.Add(new TextElement($"row {i}")));
        LayoutEngine.Arrange(list, new RectF(0, 0, 200, 96));     // 4 行可见 +2 过扫
        Assert.InRange(list.Children!.Count, 4, 8);
        Assert.Equal("0", list.Children[0].Key);
        Assert.Equal(0f, list.Children[0].Frame.Y);
        Assert.Equal(24f, list.Children[1].Frame.Y);
        App.State = null;
    }

    [Fact]
    public void VirtualList_ScrollShiftsWindowAndKeysStayStable()
    {
        App.State = new StateStore();
        VirtualListElement list = Ui.VirtualList("rows", 100, 24f, static (i, row) =>
            row.Add(new TextElement($"row {i}")));
        LayoutEngine.Arrange(list, new RectF(0, 0, 200, 96));
        list.ScrollBy(48f);                                        // offset=48
        LayoutEngine.Arrange(list, new RectF(0, 0, 200, 96));      // 重建窗
        Assert.Equal("1", list.Children![0].Key);                  // 窗口下移,键随索引
        Assert.Equal(-24f, list.Children[0].Frame.Y);              // 行1 y=1*24-48(部分滚出)
        Assert.Equal("6", list.Children[^1].Key);                  // 可见到 5,+1 过扫
        App.State = null;
    }

    [Fact]
    public void VirtualList_ClampsAndReportsFullContent()
    {
        App.State = new StateStore();
        VirtualListElement list = Ui.VirtualList("rows", 100, 24f, static (i, row) => row.Add(new TextElement(i.ToString())));
        LayoutEngine.Arrange(list, new RectF(0, 0, 200, 96));
        Assert.Equal(2400f, list.ContentMain);
        Scroll.Dispatch(list, new InputState(new PointF(50, 10), false, false, false, null, WheelDelta: 1000f));
        Assert.Equal(2304f, App.State!.Get("scroll:" + list.ResolvedId!, 0f));   // 钳 2400-96
        App.State = null;
    }
```

- [ ] **Step 2: 确认失败** → RED
- [ ] **Step 3: 实现(Scroll.cs 追加)**

```csharp
/// <summary>固定行高虚拟化列表(D20):Arrange 时按可见窗 ±1 重建行;行 Key=索引字符串。</summary>
public sealed class VirtualListElement : ScrollElement
{
    readonly int _count;
    readonly float _itemHeight;
    readonly Action<int, PanelElement> _render;

    public VirtualListElement(string key, int count, float itemHeight, Action<int, PanelElement> render)
    {
        Key = key;
        _count = count;
        _itemHeight = itemHeight;
        _render = render;
    }

    protected internal override void ArrangeChildren(RectF inner)
    {
        Children ??= new List<Element>(32);
        Children.Clear();
        ContentMain = _count * _itemHeight;
        ViewportMain = inner.Height;
        float offset = Math.Clamp(StateOffset(), 0f, MathF.Max(0f, ContentMain - inner.Height));
        int first = Math.Max(0, (int)MathF.Floor(offset / _itemHeight) - 1);
        int lastExcl = Math.Min(_count, (int)MathF.Ceiling((offset + inner.Height) / _itemHeight) + 1);
        for (int i = first; i < lastExcl; i++)
        {
            var row = new PanelElement { Key = i.ToString() };
            row.Style.CrossAlign = CrossAxisAlignment.Stretch;
            _render(i, row);
            Children.Add(row);
        }
        for (int i = first; i < lastExcl; i++)
            LayoutEngine.Arrange(Children[i - first],
                new RectF(inner.X, inner.Top + i * _itemHeight - offset, inner.Width, _itemHeight));
        IdResolver.AssignChildren(this);   // 重建后重排子树 id(保自 id)
    }
}
```

  Ui.cs:`public static VirtualListElement VirtualList(string key, int count, float itemHeight, Action<int, PanelElement> render) => new(key, count, itemHeight, render);`
- [ ] **Step 4: 通过 + 全量 110/110** → Run: `dotnet test`
- [ ] **Step 5: 提交** `git add -A && git commit -m "feat: fixed-height virtualized list with stable row keys"`

---

### Task 4: Demo、虚拟化性能门禁与冒烟

**Files:**
- Modify: `examples/Counter/Program.cs`(滚动区 + 虚拟列表示例 + 中文/emoji 行)、`tests/SharpUI.Tests/PerfTests.cs`(追加虚拟化测试)
- Test: PerfTests 追加 1(Gpu)

- [ ] **Step 1: PerfTests 追加**

```csharp
    [Fact]
    [Trait("Category", "Gpu")]
    public void VirtualList10k_StaysBounded()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        App.State = new StateStore();
        var mesh = new MeshBuilder();
        var screen = new SizeF(480, 320);
        VirtualListElement list = Ui.VirtualList("big", 10000, 24f, static (i, row) =>
            row.Add(new TextElement($"item {i}")));
        void Frame()
        {
            LayoutEngine.Arrange(list, new RectF(0, 0, screen.Width, screen.Height));
            mesh.Clear();
            list.Draw(new PaintContext(mesh, atlas, InputState.Empty));
        }
        for (int i = 0; i < 30; i++) Frame();                       // 预热
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int Frames = 200;
        for (int i = 0; i < Frames; i++) Frame();
        sw.Stop();
        double bpf = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Frames;
        Assert.True(mesh.VertexCount < 4000, $"visible mesh {mesh.VertexCount} verts unbounded");
        Assert.True(bpf <= 64 * 1024, $"每帧分配 {bpf:F0}B 超预算");
        Assert.True(sw.Elapsed.TotalMilliseconds / Frames < 8.0);
        App.State = null;
        App.Atlas = null;
    }
```

- [ ] **Step 2: Counter Program.cs 的 Column 子列表追加(Row(−/+) 之后、Immediate 之前)**

```csharp
                Ui.Scroll(
                    Enumerable.Range(1, 12).Select(i => (Element)Ui.Text($"line {i}").Size(14).Color(0x9AA2B3)).ToArray()
                ).Basis(96f).Fill(0x16161A).Radius(8f),
                Ui.VirtualList("ticks", 1000, 22f, static (i, row) =>
                    row.Add(Ui.Text($"tick {i:0000}").Size(13).Color(0x7AA2F7))).Basis(110f).Fill(0x101014).Radius(8f),
                Ui.Text("你好 SharpUI 😀").Size(15).Color(0x9AA2B3),
```

(注:此刻中文/emoji 仍走 M1 单字体路径,可能缺字形——T5 完成后自然正确;先落位。)

- [ ] **Step 3: Release + Debug 全绿(111/111)→ 冒烟(maxFrames:30 → 恢复)→ 提交** `git add -A && git commit -m "feat: scroll and virtual list demo with bounded mesh perf gate"`

---

### Task 5: TextAtlas v2(shaping + 回退链 + 中文/emoji)

**Files:**
- Modify: `src/SharpUI/SharpUI.csproj`(加 SkiaSharp.HarfBuzz 4.153.1)、`src/SharpUI/Elements/TextAtlas.cs`(v2 重写)
- Test: `tests/SharpUI.Tests/TextAtlasTests.cs`(追加 4)

**Interfaces:**
- Produces:
  - ctor `TextAtlas(GraphicsContext ctx, float scale = 1f, string[]? fallbackFamilies = null)`(默认 ["Microsoft YaHei", "Segoe UI Emoji"];槽位 0=主字体)
  - Measure/DrawRun 签名不变;内部:按 ContainsGlyph 分 run → 每 run SKShaper.Shape(物理坐标)→ 位置/步进出 shaping,字形位图仍 per-char 图集(键加槽位)
  - 光栅化用 `DrawText(s, x, y, SKTextAlign.Left, font, paint)`(非废弃;CS0618 清偿)
  - `internal int GlyphSlotCount` 仅供测试断言回退命中

- [ ] **Step 1: 追加失败测试**

```csharp
    [Fact]
    [Trait("Category", "Gpu")]
    public void Cjk_MeasuresAndFallsBack()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        SizeF m = atlas.Measure("你好", 16f);
        Assert.True(m.Width > 20f, $"cjk width={m.Width}");
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "你好", new PointF(0, 0), 16f, false, Color32.White);
        Assert.Equal(2, mesh.VertexCount / 4);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Emoji_RendersSingleQuad()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        Assert.True(atlas.Measure("😀", 16f).Width > 5f);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "😀", new PointF(0, 0), 16f, false, Color32.White);
        Assert.Equal(1, mesh.VertexCount / 4);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Shaping_KerningNarrowsAv()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        float shaped = atlas.Measure("AV", 32f).Width;
        float naive = atlas.Measure("A", 32f).Width + atlas.Measure("V", 32f).Width;
        Assert.True(shaped < naive - 0.01f, $"shaped={shaped} naive={naive}");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void MixedScript_SplitsRunsCorrectly()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "a你b😀c", new PointF(0, 0), 16f, false, Color32.White);
        Assert.Equal(5, mesh.VertexCount / 4);      // 5 个可见字形,各归各槽
    }
```

- [ ] **Step 2: 确认失败** → RED(中文宽度 0/quad 0、kerning 不成立)
- [ ] **Step 3: 实现 TextAtlas v2(整体替换;以下为完整终稿)**

```csharp
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace SharpUI;

/// <summary>文本图集 v2(D21):回退链分 run + HarfBuzz shaping 定位;字形位图 per-char 缓存(键含槽位)。</summary>
public sealed class TextAtlas : IDisposable
{
    readonly GraphicsContext _ctx;
    readonly float _scale;
    readonly SKTypeface[] _typefaces;                    // 槽 0 = 主字体,1.. = 回退
    readonly SKShaper[] _shapers;
    readonly SKPaint _paint = new() { Color = SKColors.White, IsAntialias = true };
    readonly SKFont[] _probeFonts;                       // ContainsGlyph 探针(槽级,字号无关)
    readonly Dictionary<int, SKFont> _fonts = new();     // ((slot<<12)|size)<<1|bold,size<4096
    readonly Dictionary<(string Unit, int Size, bool Bold, int Slot), Glyph> _glyphs = new();   // Unit=单 BMP 字符或代理对(emoji)
    readonly Dictionary<int, (float Ascent, float Descent)> _lineMetrics = new();
    readonly ShelfPacker _packer;
    readonly SKBitmap _bitmap;
    readonly SKCanvas _canvas;

    public RectF WhiteUv { get; private set; }
    internal int GlyphSlotCount => _typefaces.Length;

    public TextAtlas(GraphicsContext ctx, float scale = 1f, string[]? fallbackFamilies = null)
    {
        _ctx = ctx;
        _scale = scale;
        string[] families = fallbackFamilies ?? new[] { "Microsoft YaHei", "Segoe UI Emoji" };
        _typefaces = new SKTypeface[1 + families.Length];
        _shapers = new SKShaper[_typefaces.Length];
        _probeFonts = new SKFont[_typefaces.Length];
        _typefaces[0] = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        for (int i = 0; i < families.Length; i++)
        {
            SKTypeface? tf = SKTypeface.FromFamilyName(families[i]);
            _typefaces[1 + i] = tf ?? _typefaces[0];
        }
        for (int i = 0; i < _typefaces.Length; i++)
        {
            _shapers[i] = new SKShaper(_typefaces[i]);
            _probeFonts[i] = new SKFont(_typefaces[i], 16f);
        }
        _packer = new ShelfPacker(GraphicsContext.AtlasSize, GraphicsContext.AtlasSize);
        _bitmap = new SKBitmap(new SKImageInfo(
            GraphicsContext.AtlasSize, GraphicsContext.AtlasSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        _canvas = new SKCanvas(_bitmap);
        _canvas.Clear(SKColors.Transparent);
        _canvas.Flush();
        SeedWhitePixel();
    }

    void SeedWhitePixel()
    {
        (int x, int y) = _packer.TryAlloc(1, 1)!.Value;
        _ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, (uint)x, (uint)y, 1, 1);
        WhiteUv = new RectF(
            x / (float)GraphicsContext.AtlasSize, y / (float)GraphicsContext.AtlasSize,
            1f / GraphicsContext.AtlasSize, 1f / GraphicsContext.AtlasSize);
    }

    int SlotFor(uint codepoint)
    {
        for (int i = 0; i < _probeFonts.Length; i++)
            if (_probeFonts[i].ContainsGlyph(codepoint))
                return i;
        return 0;
    }

    static int UnitLen(string s, int i) =>
        char.IsHighSurrogate(s, i) && i + 1 < s.Length && char.IsLowSurrogate(s, i + 1) ? 2 : 1;

    SKFont GetFont(int slot, int size, bool bold)
    {
        int key = ((slot << 12 | size) << 1) | (bold ? 1 : 0);
        if (!_fonts.TryGetValue(key, out SKFont? font))
        {
            font = new SKFont(bold ? BoldOf(slot) : _typefaces[slot], size)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = false,
            };
            _fonts[key] = font;
        }
        return font;
    }

    SKTypeface BoldOf(int slot)
    {
        // 主字体粗体取既有路径;回退槽粗体简化为同型face(粗体回退为可接受折衷,D21)
        return slot == 0 ? (_boldTf ??= SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? _typefaces[0]) : _typefaces[slot];
    }

    SKTypeface? _boldTf;

    (float Ascent, float Descent) GetLineMetrics(int size, bool bold)
    {
        int key = (size << 1) | (bold ? 1 : 0);
        if (!_lineMetrics.TryGetValue(key, out var lm))
        {
            GetFont(0, size, bold).MeasureText("Ág", out SKRect bounds, _paint);
            lm = (-bounds.Top, bounds.Bottom);
            _lineMetrics[key] = lm;
        }
        return lm;
    }

    /// <summary>按槽位分 run(连续同槽字符一段)。</summary>
    List<(int Slot, int Start, int Len)> SplitRuns(string text)
    {
        var runs = new List<(int, int, int)>();
        int i = 0, runStart = 0;
        int runSlot = SlotFor(CodePoint(text, 0));
        while (i < text.Length)
        {
            int ul = UnitLen(text, i);
            int slot = SlotFor(CodePoint(text, i));
            if (slot != runSlot)
            {
                runs.Add((runSlot, runStart, i - runStart));
                runSlot = slot;
                runStart = i;
            }
            i += ul;
        }
        runs.Add((runSlot, runStart, text.Length - runStart));
        return runs;
    }

    static uint CodePoint(string s, int i) =>
        UnitLen(s, i) == 2 ? (uint)char.ConvertToUtf32(s, i) : s[i];

    public SizeF Measure(string text, float fontSize, bool bold = false)
    {
        if (text.Length == 0) return SizeF.Zero;
        int size = SizeKey(fontSize);
        (float ascent, float descent) = GetLineMetrics(size, bold);
        float width = 0f;
        foreach ((int slot, int start, int len) in SplitRuns(text))
        {
            string run = text.Substring(start, len);
            SKShaper.Result r = _shapers[slot].Shape(run, 0f, 0f, GetFont(slot, size, bold));
            width += r.Width;
        }
        return new SizeF(width / _scale, (ascent + descent) / _scale);
    }

    public void DrawRun(MeshBuilder mesh, string text, PointF topLeft, float fontSize, bool bold, Color32 color)
    {
        int size = SizeKey(fontSize);
        (float ascent, _) = GetLineMetrics(size, bold);
        float xLogical = topLeft.X;
        float baselineY = topLeft.Y + ascent / _scale;
        foreach ((int slot, int start, int len) in SplitRuns(text))
        {
            string run = text.Substring(start, len);
            SKFont font = GetFont(slot, size, bold);
            SKShaper.Result r = _shapers[slot].Shape(run, 0f, 0f, font);   // 逻辑→物理:x*scale
            for (int gi = 0; gi < r.Codepoints.Length; gi++)
            {
                int ci = (int)r.Clusters[gi];
                int ul = UnitLen(run, ci);
                if (run[ci] != ' ')
                {
                    Glyph g = GetGlyph(run.Substring(ci, ul), size, bold, slot);
                    float gx = xLogical + r.Points[gi].X / _scale;
                    float top = baselineY - g.BearingY / _scale;
                    mesh.AddQuad(new RectF(gx, top, g.Width / _scale, g.Height / _scale), g.Uv, color);
                }
            }
            xLogical += r.Width / _scale;
        }
    }

    static int SizeKey(float fontSize, float scale = 1f) => Math.Max(4, (int)MathF.Ceiling(fontSize * scale));

    Glyph GetGlyph(string unit, int size, bool bold, int slot)
    {
        if (_glyphs.TryGetValue((unit, size, bold, slot), out Glyph glyph)) return glyph;
        glyph = Rasterize(unit, size, bold, slot);
        _glyphs[(unit, size, bold, slot)] = glyph;
        return glyph;
    }

    Glyph Rasterize(string unit, int size, bool bold, int slot)
    {
        SKFont font = GetFont(slot, size, bold);
        string s = unit;
        float advance = font.MeasureText(s, out SKRect bounds, _paint);
        int width = Math.Max(1, (int)MathF.Ceiling(bounds.Right - bounds.Left));
        int height = Math.Max(1, (int)MathF.Ceiling(bounds.Bottom - bounds.Top));
        (int x, int y)? slotPx = _packer.TryAlloc(width + 1, height + 1);
        if (slotPx is null)
        {
            _packer.Reset();
            _glyphs.Clear();
            SeedWhitePixel();
            slotPx = _packer.TryAlloc(width + 1, height + 1)
                ?? throw new InvalidOperationException($"字形 {unit}@{size} 放不进图集");
        }
        using var gb = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var gc = new SKCanvas(gb);
        gc.Clear(SKColors.Transparent);
        gc.DrawText(s, -bounds.Left, -bounds.Top, SKTextAlign.Left, font, _paint);   // 非废弃重载,CS0618 清偿
        gc.Flush();
        _ctx.UpdateAtlasRegion(gb.GetPixelSpan().ToArray(), (uint)slotPx.Value.X, (uint)slotPx.Value.Y, (uint)width, (uint)height);
        return new Glyph(
            new RectF(slotPx.Value.X / (float)GraphicsContext.AtlasSize,
                      slotPx.Value.Y / (float)GraphicsContext.AtlasSize,
                      width / (float)GraphicsContext.AtlasSize,
                      height / (float)GraphicsContext.AtlasSize),
            width, height, -bounds.Top, advance);
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _bitmap.Dispose();
        foreach (SKFont f in _fonts.Values) f.Dispose();
        foreach (SKFont f in _probeFonts) f.Dispose();
        foreach (SKShaper s in _shapers) s.Dispose();
        _paint.Dispose();
        for (int i = _typefaces.Length - 1; i >= 1; i--)
            if (!ReferenceEquals(_typefaces[i], _typefaces[0])) _typefaces[i].Dispose();
        _typefaces[0].Dispose();
    }

    readonly record struct Glyph(RectF Uv, float Width, float Height, float BearingY, float Advance);
}
```

**实现指令(以上终稿中两处留白按此补全,不得留 NotImplementedException):**
2. `SizeKey(fontSize, scale = 1f)` 的 scale 参数来自 `_scale` 调用点(与 v1 相同语义):Measure/DrawRun/Rasterize 内一律 `SizeKey(fontSize, _scale)`;上面代码里 `SizeKey(fontSize)` 的裸调全部改为传 `_scale`。
3. 其余与终稿逐字一致;既有 TextAtlas 测试(默认链、scale=1)必须零改动绿。

- [ ] **Step 4: csproj 加 `<PackageReference Include="SkiaSharp.HarfBuzz" Version="4.153.1" />`**
- [ ] **Step 5: 通过 + 全量 115/115;构建零 CS0618** → Run: `dotnet test`
- [ ] **Step 6: 提交** `git add -A && git commit -m "feat: harfbuzz shaping with font fallback for cjk and emoji"`

---

### Task 6: SDF 评估文档 + 性能再基线 + M2 收官冒烟

**Files:**
- Create: `docs/notes/sdf-evaluation.md`
- Test: 既有套件;PerfTests Release 刷新

- [ ] **Step 1: docs/notes/sdf-evaluation.md**(完整内容照录)

```markdown
# SDF 文本渲染评估(M2d 交付物,决策文档)

结论:**M2/M3 不做 SDF,维持位图图集 + Point 采样**。

- 现状:位图按物理字号(⌈字号×scale⌉)光栅,整数倍缩放(100%/200%)texel 精确;
  Windows 常见 125%/150% 下实测轻微锯齿(可接受,用户手测未提出异议)。
- SDF 收益:任意缩放/旋转下的边缘质量、运行时字号无关图集体积。
- SDF 代价:字形边缘伪影(锐利小字号退化,需双通道/8sdf 缓解)、字形面积 4× 内存、
  光栅化管线整体替换(与 M3 自研光栅化耦合)、emoji 彩色字形不适用 SDF(需双路径)。
- 决策:SDF 与 M3「自研字形光栅化」合并评估;在此之前,若小数缩放锯齿成为问题,
  优先尝试 Linear 采样 + UV 半纹素内缩(改动一处,规格 D7 先例)。
- 复评触发条件:用户报告小数缩放模糊不可接受 / M3 自研光栅化立项。
```

- [ ] **Step 2: Release 门禁(含 shaping 成本)记录数字;Debug 全量 115/115;Counter 冒烟(中文/emoji 行、滚轮滚动两区、虚拟列表)** → 恢复 Program.cs
- [ ] **Step 3: 提交** `git add -A && git commit -m "docs: sdf evaluation and m2 closeout perf baseline"`

---

## 任务依赖图
T1+2 → T3 → T4 → T5 → T6(T5 仅依赖 T4 的 demo 落位;T3 依赖 T1+2 的 Scroll 基建)

## M2c+M2d 完成定义
- [ ] 全量测试绿(115/115 含 GPU);构建零 CS0618
- [ ] 虚拟化 10k 门禁绿(网格 <4000 顶点、预算内)
- [ ] 中文/emoji/kerning/混排四测试绿
- [ ] Counter 冒烟:两滚动区 + 1000 行虚拟列表 + 中文 emoji 行(视觉验收归用户)
- [ ] SDF 评估文档入库;M2 除 M2e(待 remote)外收官
