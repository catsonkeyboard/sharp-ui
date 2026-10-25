# SharpUI M2b-ii 布局与呈现 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 全量 flexbox(wrap/basis/grow/shrink,D16)、帧相交剪裁与 scissor 多批次(D17)、DPI 缩放管线(D18),外加 M2b-i 终审热身项。

**Architecture:** LayoutEngine 重写为"行打包 + 行内 grow/shrink 分配"两段式(Measure 用 available 主轴、Arrange 用实际帧内主轴);MeshBuilder 记录 ClipRun 区段,Renderer 每区段一次 `SetScissorRect(0,x,y,w,h)+DrawIndexed(count,1,start,0,0)`;DPI 只作用于 scissor(物理)、鼠标(÷scale)与字体光栅(物理字号)——几何 uniform 保持逻辑尺寸,NDC 分数与分辨率无关(零着色器改动)。

**Tech Stack:** 不变。零新增包。

**Spec:** `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(D16/D17/D18 为本计划依据;附录 A 含已验证的 SetScissorRect/DrawIndexed 五参形态)。

## Global Constraints

- 沿用全部既有约束:namespace;禁止 unsafe;英文 Conventional Commits;main;每任务一提交;`dotnet test` 全绿为提交前置。
- 性能预算不放宽(≤64KB/帧、<8ms,Release);PerfTests 场景须计入剪裁批次(T6 记录)。
- **既有 LayoutTests 6 个测试一字不改仍绿**(flexbox 默认值 FlexShrink=0/Wrap=NoWrap/Basis=null 必须精确退化为 M1 行为——这是 D16 的锁定语义)。
- 新增公开成员仅限各任务 Interfaces 节;版本锁定不变。

---

### Task 1: 终审热身项(文档×2 + 组合测试 + 基线文案)

**Files:**
- Modify: `src/SharpUI/Elements/Ui.cs`(Immediate 文档行)、`src/SharpUI/Interaction.cs`(ClickedIds 文档补句)、`tests/SharpUI.Tests/PerfTests.cs`(场景文案串)、`tests/SharpUI.Tests/UiScopeTests.cs`(追加 1)

**Interfaces:**
- Produces: 文档与测试,零 API 变化。

- [ ] **Step 1: Ui.Immediate 的 doc 注释末尾追加一行**

```
    /// 不可作为渲染根(App.Create 的 render 闭包直接返回 Immediate 面板时根 id 为空串,后缀匹配永不成立);
```
(并入现有 summary,保持单段。)

- [ ] **Step 2: Interaction.cs 的 ClickedIds 参数 doc 补句**(InputState record 注释):追加 `调用方不得跨帧持有该列表(别名复用内部缓冲)。`

- [ ] **Step 3: PerfTests 场景文案串**:`含 id 解析+焦点注册管线` → `含 id+焦点+即时面板管线`

- [ ] **Step 4: 追加组合测试到 UiScopeTests.cs**

```csharp
    [Fact]
    public void PointerClick_OnImmediateTree_ReachesClickedPoll()
    {
        bool resetClicked = false;
        Element BuildTree()
        {
            PanelElement root = Ui.Column(Ui.Immediate("dbg", ui =>
            {
                if (ui.Button("Reset").Clicked()) resetClicked = true;
            }));
            root.Style.CrossAlign = CrossAxisAlignment.Stretch;   // Atlas-null 下按钮仅 32px 宽,拉伸全宽命中 x=50
            return root;
        }
        Element t1 = BuildTree();
        LayoutEngine.Arrange(t1, new RectF(0, 0, 100, 200));
        IdResolver.Assign(t1);

        Interaction.Update(t1, new RawInput(new PointF(50, 5), new[] { true }));
        UiContext.CurrentInput = Interaction.Update(t1, new RawInput(new PointF(50, 5), new[] { false }));

        BuildTree();                       // 新帧构建:poll 在闭包内读账本
        Assert.True(resetClicked);
        UiContext.CurrentInput = default;
    }
```

- [ ] **Step 5: 运行 + 提交** — Run: `dotnet test`(89/89)→ `git add -A && git commit -m "test: end-to-end immediate click composition and doc hardening"`

---

### Task 2: 全量 flexbox(Style 扩展 + LayoutEngine 重写)

**Files:**
- Modify: `src/SharpUI/Layout.cs`(整体替换)、`src/SharpUI/Elements/Ui.cs`(ElementExtensions 追加 4)
- Test: `tests/SharpUI.Tests/LayoutTests.cs`(追加 6)

**Interfaces:**
- Produces(T4/T5 与 M2c 消费):
  - `enum FlexWrap { NoWrap, Wrap }`
  - Style 新增:`FlexWrap Wrap = NoWrap; float FlexGrow; float FlexShrink; float? FlexBasis;`(FlexShrink 默认 0=M1 语义)
  - `ElementExtensions`:`Grow<T>(float)`、`Shrink<T>(float)`、`Basis<T>(float)`、`Wrap<T>()`(容器)
  - LayoutEngine.Measure/Arrange 签名不变;内部两段式:行打包(basis+gap,wrap 阈值)+ 行内分配(grow 加权剩余 / shrink 加权溢出,basis=FlexBasis??内容主尺寸);行沿交叉轴堆叠,行间距复用 Gap;单行时交叉对齐基准 = 容器内交叉宽(M1 行为),多行时 = 行宽

- [ ] **Step 1: 追加失败测试(6 个)**

```csharp
    [Fact]
    public void Wrap_OverlongColumnBreaksIntoLinesStackedHorizontally()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(10, 30)),
            new Spacer(new SizeF(10, 30)))
        { Style = { Wrap = FlexWrap.Wrap, Gap = 5 } };
        SizeF m = LayoutEngine.Measure(root, new SizeF(100, 50));
        Assert.Equal(30f, m.Height);          // 最长行
        Assert.Equal(25f, m.Width);           // 两行交叉宽 10+10+gap5
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 50));
        Assert.Equal(new RectF(0, 0, 10, 30), root.Children![0].Frame);
        Assert.Equal(new RectF(15, 0, 10, 30), root.Children[1].Frame);   // 第二行 x=10+gap5
    }

    [Fact]
    public void Grow_DistributesFreeSpaceWeighted()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(20, 10)) { Style = { FlexGrow = 1 } },
            new Spacer(new SizeF(30, 10)) { Style = { FlexGrow = 3 } })
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 50));
        Assert.Equal(57.5f, root.Children![0].Frame.Width);   // 20 + 150×1/4
        Assert.Equal(142.5f, root.Children[1].Frame.Width);   // 30 + 150×3/4
    }

    [Fact]
    public void Shrink_ShrinksOverflowWeightedByBasis()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(60, 10)) { Style = { FlexShrink = 1 } },
            new Spacer(new SizeF(60, 10)) { Style = { FlexShrink = 1 } })
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 50, 20));
        Assert.Equal(25f, root.Children![0].Frame.Width);     // 60 - 70×60/120
        Assert.Equal(25f, root.Children[1].Frame.Width);
    }

    [Fact]
    public void DefaultNoShrink_OverflowPreserved()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(60, 10)),
            new Spacer(new SizeF(60, 10)))
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 50, 20));
        Assert.Equal(60f, root.Children![0].Frame.Width);     // M1 语义
    }

    [Fact]
    public void Basis_OverridesContentMainSize()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(20, 10)) { Style = { FlexBasis = 45 } })
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 20));
        Assert.Equal(45f, root.Children![0].Frame.Width);
    }

    [Fact]
    public void WrappedLines_StackVerticallyInRow()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(30, 10)),
            new Spacer(new SizeF(30, 10)))
        { Style = { Axis = Axis.Horizontal, Wrap = FlexWrap.Wrap, Gap = 5 } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 50, 100));
        Assert.Equal(new RectF(0, 0, 30, 10), root.Children![0].Frame);
        Assert.Equal(new RectF(0, 15, 30, 10), root.Children[1].Frame);   // 第二行 y=10+gap5
    }
```

- [ ] **Step 2: 确认失败** — Run: `dotnet test --filter FullyQualifiedName~LayoutTests` → 新 6 FAIL(枚举/属性未定义则编译失败即为 RED)。
- [ ] **Step 3: 整体替换 src/SharpUI/Layout.cs**

```csharp
namespace SharpUI;

public enum Axis { Vertical, Horizontal }
public enum MainAxisAlignment { Start, Center, End }
public enum CrossAxisAlignment { Start, Center, End, Stretch }
public enum FlexWrap { NoWrap, Wrap }

public sealed class Style
{
    public Axis Axis = Axis.Vertical;
    public MainAxisAlignment MainAlign = MainAxisAlignment.Start;
    public CrossAxisAlignment CrossAlign = CrossAxisAlignment.Start;
    public FlexWrap Wrap = FlexWrap.NoWrap;
    public float Gap;
    public Thickness Padding;
    public float FlexGrow;          // 作为子项:行内剩余主轴加权分配
    public float FlexShrink;        // 作为子项:默认 0(M1"叶子不收缩"语义,规格 D16)
    public float? FlexBasis;        // null=auto(内容主尺寸)
}

public static class LayoutEngine
{
    public static SizeF Measure(Element e, SizeF available)
    {
        if (e.Children is not { Count: > 0 })
            return e.MeasureSelf(available);

        bool vertical = e.Style.Axis == Axis.Vertical;
        Thickness pad = e.Style.Padding;
        float availMain = MathF.Max(0f, (vertical ? available.Height : available.Width)
            - (vertical ? pad.Vertical : pad.Horizontal));
        float availCross = MathF.Max(0f, (vertical ? available.Width : available.Height)
            - (vertical ? pad.Horizontal : pad.Vertical));

        var childMain = new float[e.Children.Count];
        var childCross = new float[e.Children.Count];
        var lineOf = new int[e.Children.Count];
        var lineMain = new float[e.Children.Count];
        var lineCross = new float[e.Children.Count];
        int lines = ComputeLines(e, availMain, availCross, childMain, childCross, lineOf, lineMain, lineCross);

        float maxLineMain = 0f, totalCross = 0f;
        for (int l = 0; l < lines; l++)
        {
            maxLineMain = MathF.Max(maxLineMain, lineMain[l]);
            totalCross += lineCross[l];
        }
        totalCross += MathF.Max(0, lines - 1) * e.Style.Gap;
        SizeF content = vertical ? new SizeF(totalCross, maxLineMain) : new SizeF(maxLineMain, totalCross);
        return new SizeF(content.Width + pad.Horizontal, content.Height + pad.Vertical);
    }

    public static void Arrange(Element e, RectF frame)
    {
        e.Frame = frame;
        if (e.Children is not { Count: > 0 }) return;

        bool vertical = e.Style.Axis == Axis.Vertical;
        RectF inner = frame.Deflate(e.Style.Padding);
        float availMain = MathF.Max(0f, vertical ? inner.Height : inner.Width);
        float availCross = MathF.Max(0f, vertical ? inner.Width : inner.Height);

        var childMain = new float[e.Children.Count];
        var childCross = new float[e.Children.Count];
        var lineOf = new int[e.Children.Count];
        var lineMain = new float[e.Children.Count];
        var lineCross = new float[e.Children.Count];
        int lines = ComputeLines(e, availMain, availCross, childMain, childCross, lineOf, lineMain, lineCross);

        float crossPos = vertical ? inner.X : inner.Y;
        for (int l = 0; l < lines; l++)
        {
            float mainSize = vertical ? inner.Height : inner.Width;
            float pos = e.Style.MainAlign switch
            {
                MainAxisAlignment.Center => (vertical ? inner.Top : inner.Left) + MathF.Max(0f, (mainSize - lineMain[l]) / 2f),
                MainAxisAlignment.End => (vertical ? inner.Bottom : inner.Right) - lineMain[l],
                _ => vertical ? inner.Top : inner.Left,
            };
            float extent = lines == 1 ? availCross : lineCross[l];   // 单行对齐基准=容器内交叉宽(M1)
            bool firstInLine = true;
            for (int i = 0; i < e.Children.Count; i++)
            {
                if (lineOf[i] != l) continue;
                Element child = e.Children[i];
                float m = childMain[i];
                float c = childCross[i];
                if (!firstInLine) pos += e.Style.Gap;
                firstInLine = false;
                if (vertical)
                {
                    float w = e.Style.CrossAlign == CrossAxisAlignment.Stretch ? extent : c;
                    float x = e.Style.CrossAlign switch
                    {
                        CrossAxisAlignment.Center => crossPos + (extent - c) / 2f,
                        CrossAxisAlignment.End => crossPos + extent - c,
                        _ => crossPos,
                    };
                    Arrange(child, new RectF(x, pos, w, m));
                }
                else
                {
                    float h = e.Style.CrossAlign == CrossAxisAlignment.Stretch ? extent : c;
                    float y = e.Style.CrossAlign switch
                    {
                        CrossAxisAlignment.Center => crossPos + (extent - c) / 2f,
                        CrossAxisAlignment.End => crossPos + extent - c,
                        _ => crossPos,
                    };
                    Arrange(child, new RectF(pos, y, m, h));
                }
                pos += m;
            }
            crossPos += extent + e.Style.Gap;
        }
    }

    /// <summary>行打包 + 行内分配(D16)。输出数组由调用方分配;返回行数。</summary>
    static int ComputeLines(Element e, float availMain, float availCross,
        float[] childMain, float[] childCross, int[] lineOf, float[] lineMain, float[] lineCross)
    {
        bool vertical = e.Style.Axis == Axis.Vertical;
        bool wrap = e.Style.Wrap == FlexWrap.Wrap && availMain < float.MaxValue;
        float gap = e.Style.Gap;
        System.Collections.Generic.List<Element> children = e.Children!;
        int line = 0, curFrom = 0, curCount = 0;
        float curMain = 0f, curCross = 0f;

        void CloseLine()
        {
            lineMain[line] = curMain;
            lineCross[line] = curCross;
            Distribute(children, availMain, gap, childMain, lineMain, line, curFrom, curCount);
            line++;
            curMain = 0f; curCross = 0f; curCount = 0;
        }

        for (int i = 0; i < children.Count; i++)
        {
            Element c = children[i];
            SizeF m = Measure(c, vertical
                ? new SizeF(availCross, float.PositiveInfinity)
                : new SizeF(float.PositiveInfinity, availCross));
            float basis = c.Style.FlexBasis ?? (vertical ? m.Height : m.Width);
            childMain[i] = basis;
            childCross[i] = vertical ? m.Width : m.Height;

            float projected = curCount == 0 ? basis : curMain + gap + basis;
            if (wrap && curCount > 0 && projected > availMain)
            {
                CloseLine();
                curFrom = i;
                projected = basis;
            }
            if (curCount == 0) curFrom = i;
            if (curCount > 0) curMain += gap;
            curMain += basis;
            curCross = MathF.Max(curCross, childCross[i]);
            lineOf[i] = line;
            curCount++;
        }
        CloseLine();
        return line;
    }

    static void Distribute(System.Collections.Generic.List<Element> children, float availMain, float gap,
        float[] childMain, float[] lineMain, int line, int from, int count)
    {
        if (count <= 0 || availMain >= float.MaxValue) return;   // 无限主轴不分配(Measure 阶段)
        float basisSum = 0f, growSum = 0f, shrinkSum = 0f;
        for (int k = 0; k < count; k++)
        {
            int i = from + k;
            basisSum += childMain[i];
            growSum += children[i].Style.FlexGrow;
            shrinkSum += children[i].Style.FlexShrink * childMain[i];
        }
        float free = availMain - basisSum - MathF.Max(0, count - 1) * gap;
        if (free > 0 && growSum > 0)
            for (int k = 0; k < count; k++)
            {
                int i = from + k;
                childMain[i] += free * children[i].Style.FlexGrow / growSum;
            }
        else if (free < 0 && shrinkSum > 0)
            for (int k = 0; k < count; k++)
            {
                int i = from + k;
                childMain[i] = MathF.Max(0f, childMain[i]
                    + free * (children[i].Style.FlexShrink * childMain[i]) / shrinkSum);
            }
        float total = 0f;
        for (int k = 0; k < count; k++)
        {
            int i = from + k;
            total = k == 0 ? childMain[i] : total + gap + childMain[i];
        }
        lineMain[line] = total;
    }
}
```

- [ ] **Step 4: Ui.cs 的 ElementExtensions 追加**

```csharp
    public static T Grow<T>(this T e, float grow) where T : Element { e.Style.FlexGrow = grow; return e; }
    public static T Shrink<T>(this T e, float shrink) where T : Element { e.Style.FlexShrink = shrink; return e; }
    public static T Basis<T>(this T e, float basis) where T : Element { e.Style.FlexBasis = basis; return e; }
    public static T Wrap<T>(this T e) where T : Element { e.Style.Wrap = FlexWrap.Wrap; return e; }
```

- [ ] **Step 5: 通过 + 全量 95/95(既有 6 布局测试零改动仍绿)** — Run: `dotnet test --filter FullyQualifiedName~LayoutTests` 然后 `dotnet test`
- [ ] **Step 6: 提交** `git add -A && git commit -m "feat: full flexbox with wrap grow shrink basis"`

---

### Task 3: 剪裁区段与 scissor 多批次

**Files:**
- Modify: `src/SharpUI/Mesh.cs`(ClipRun/SetClip/CurrentClip)、`src/SharpUI/Geometry.cs`(RectF.Intersect(RectF)→RectF)、`src/SharpUI/Renderer.cs`(区段绘制 + scale 参数)
- Test: `tests/SharpUI.Tests/MeshTests.cs`(追加 2)、`tests/SharpUI.Tests/RendererTests.cs`(追加 1,Gpu)

**Interfaces:**
- Produces(T4/T5 消费):
  - `MeshBuilder.ClipRun readonly struct { RectF Clip; int Start, Count; }`、`SetClip(RectF)`、`CurrentClip : RectF`、`ClipRuns : ReadOnlySpan<ClipRun>`(Clear 复位全屏哨兵)
  - `RectF.Intersect(RectF other) → RectF`(空交返回 0 尺寸矩形,不返回负)
  - `Renderer.Submit(MeshBuilder, SizeF logicalScreen, Framebuffer, float scale = 1f)`;无区段时整帧单批次(FullClip 哨兵→视口全 scissor);已验证 API:`SetScissorRect(0,x,y,w,h)`、`DrawIndexed(count,1u,start,0,0u)`

- [ ] **Step 1: 追加失败测试 MeshTests(2)**

```csharp
    [Fact]
    public void SetClip_SplitsRunsAndClearResets()
    {
        var mesh = new MeshBuilder();
        RectF uv = new(0, 0, 1, 1);
        mesh.AddQuad(new RectF(0, 0, 8, 8), uv, Color32.White);
        mesh.SetClip(new RectF(0, 0, 4, 4));
        mesh.AddQuad(new RectF(0, 0, 8, 8), uv, Color32.White);
        mesh.SetClip(new RectF(0, 0, 4, 4));       // 同值 no-op
        mesh.AddQuad(new RectF(0, 0, 8, 8), uv, Color32.White);
        Assert.Equal(2, mesh.ClipRuns.Length);
        Assert.Equal(6, mesh.ClipRuns[0].Count);   // 第一个 quad(全屏剪裁)
        Assert.Equal(12, mesh.ClipRuns[1].Count);  // 后两个 quad(4,4 剪裁)
        mesh.Clear();
        Assert.Equal(0, mesh.ClipRuns.Length);
        Assert.Equal(new RectF(0, 0, float.MaxValue, float.MaxValue), mesh.CurrentClip);
    }

    [Fact]
    public void Intersect_ReturnsClampedEmpty()
    {
        Assert.Equal(new RectF(5, 5, 5, 5), new RectF(0, 0, 10, 10).Intersect(new RectF(5, 5, 10, 10)));
        RectF none = new RectF(0, 0, 10, 10).Intersect(new RectF(20, 20, 5, 5));
        Assert.True(none.IsEmpty);
        Assert.Equal(0f, none.Width);
    }
```

- [ ] **Step 2: 追加失败 GPU 测试 RendererTests(1)**

```csharp
    [Fact]
    [Trait("Category", "Gpu")]
    public void ClipRuns_DrawWithPerRunScissor()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        var mesh = new MeshBuilder();
        float inv = 1f / GraphicsContext.AtlasSize;
        var uv = new RectF(0, 0, inv, inv);
        mesh.AddQuad(new RectF(0, 0, 32, 64), uv, Color32.White);        // 全屏剪裁(默认)
        mesh.SetClip(new RectF(0, 0, 16, 64));                            // 只露出左半
        mesh.AddQuad(new RectF(32, 0, 32, 64), uv, Color32.White);       // 右半区域,被剪
        mesh.SetClip(new RectF(0, 0, float.MaxValue, float.MaxValue));
        using var renderer = new Renderer(ctx);
        renderer.Submit(mesh, new SizeF(64, 64), ctx.MainFramebuffer);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 4, 32));    // 左区可见
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 40, 32));      // 右区被剪→清屏色(RGB 18,18,20→BGR 存储)
    }
```

(存储字节序按 M1 修正后的推导:B8G8R8A8 下 RGB(0.07,0.07,0.08) 清屏读回 [B,G,R,A]=[20,18,18,255]——本测试两处像素断言以:**左 [255,255,255,255]、右 [20,18,18,255]** 为准。)

- [ ] **Step 3: 确认失败** → Run: 聚焦过滤器(编译 FAIL 即 RED)。
- [ ] **Step 4: 实现 Mesh.cs 追加(字段区 + Clear 改造 + AddQuad/AddTriangle 收尾不动——区段按索引计数自动切分)**

```csharp
    public readonly struct ClipRun
    {
        public readonly RectF Clip;
        public readonly int Start, Count;
        public ClipRun(RectF clip, int start, int count) { Clip = clip; Start = start; Count = count; }
    }

    public static readonly RectF FullClip = new(0, 0, float.MaxValue, float.MaxValue);

    RectF _clip = FullClip;
    int _runStart;
    readonly List<ClipRun> _clipRuns = new();

    public RectF CurrentClip => _clip;

    /// <summary>读取时物化尾段(最后一次 SetClip 之后的开放区段),否则丢失末端几何。</summary>
    public ReadOnlySpan<ClipRun> ClipRuns
    {
        get
        {
            if (_indexCount > _runStart)
            {
                _clipRuns.Add(new ClipRun(_clip, _runStart, _indexCount - _runStart));
                _runStart = _indexCount;
            }
            return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_clipRuns);
        }
    }

    public void SetClip(RectF clip)
    {
        if (clip == _clip) return;
        if (_indexCount > _runStart)
            _clipRuns.Add(new ClipRun(_clip, _runStart, _indexCount - _runStart));
        _clip = clip;
        _runStart = _indexCount;
    }
```

(Clear() 追加:`_clipRuns.Clear(); _clip = FullClip; _runStart = 0;`)

- [ ] **Step 5: Geometry.cs 的 RectF 追加**

```csharp
    public RectF Intersect(RectF o)
    {
        float x = MathF.Max(X, o.X), y = MathF.Max(Y, o.Y);
        return new RectF(x, y, MathF.Max(0f, MathF.Min(Right, o.Right) - x), MathF.Max(0f, MathF.Min(Bottom, o.Bottom) - y));
    }
```

- [ ] **Step 6: Renderer.cs——Submit 加 scale 参数与区段绘制(替换 DrawIndexed 段)**

```csharp
    public void Submit(MeshBuilder mesh, SizeF logicalScreen, Framebuffer framebuffer, float scale = 1f)
    {
        // …上传与 Clear/SetPipeline/SetVertexBuffer/SetIndexBuffer/SetGraphicsResourceSet 不变(UpdateBuffer 无几何帧仍执行)…
        if (mesh.IndexCount == 0) { /* End+Submit 同现状 */ }
        else
        {
            uint fbW = framebuffer.Width, fbH = framebuffer.Height;
            ReadOnlySpan<MeshBuilder.ClipRun> runs = mesh.ClipRuns;
            if (runs.Length == 0)
            {
                cl.SetScissorRect(0, 0, 0, fbW, fbH);
                cl.DrawIndexed((uint)mesh.IndexCount, 1u, 0u, 0, 0u);
            }
            else
            {
                foreach (ref readonly MeshBuilder.ClipRun run in runs)
                {
                    if (run.Count == 0) continue;
                    if (run.Clip == MeshBuilder.FullClip)
                    {
                        cl.SetScissorRect(0, 0, 0, fbW, fbH);
                    }
                    else
                    {
                        uint x = (uint)MathF.Max(0f, run.Clip.X * scale);
                        uint y = (uint)MathF.Max(0f, run.Clip.Y * scale);
                        uint w = (uint)MathF.Min(fbW - MathF.Min(x, fbW), MathF.Ceiling(run.Clip.Width * scale));
                        uint h = (uint)MathF.Min(fbH - MathF.Min(y, fbH), MathF.Ceiling(run.Clip.Height * scale));
                        if (w == 0 || h == 0) continue;
                        cl.SetScissorRect(0, x, y, w, h);
                    }
                    cl.DrawIndexed((uint)run.Count, 1u, (uint)run.Start, 0, 0u);
                }
            }
        }
        // End/SubmitCommands 同现状
    }
```

(实现注:保留既有 hasGeometry 门控与清屏色;scale 仅进 scissor 换算,uniform 仍收 logicalScreen——NDC 与分辨率无关,规格 D18。)

- [ ] **Step 7: 通过 + 全量 98/98** → Run: `dotnet test`
- [ ] **Step 8: 提交** `git add -A && git commit -m "feat: clip runs with per-run scissor batches"`

---

### Task 4: PanelElement 帧相交剪裁

**Files:**
- Modify: `src/SharpUI/Elements/PanelElement.cs`(Draw 压/还原剪裁)
- Test: `tests/SharpUI.Tests/UiDslTests.cs`(追加 1,Gpu)

**Interfaces:**
- Consumes: T3 SetClip/CurrentClip/Intersect。
- Produces: 所有容器(含 UiScope 面板)默认剪裁子内容到自身帧 ∩ 父剪裁(D17);M1"不剪裁"简化退役。

- [ ] **Step 1: 追加失败 GPU 测试**

```csharp
    [Fact]
    [Trait("Category", "Gpu")]
    public void PanelChildren_ClipToPanelFrame()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        PanelElement outer = Ui.Panel().Fill(Color32.White);
        PanelElement inner = Ui.Panel().Fill(Color32.FromRgb(0xFF0000));
        outer.Style.CrossAlign = CrossAxisAlignment.Stretch;
        outer.Add(inner);
        inner.Add(new Spacer(new SizeF(64, 100)));   // 撑到 100 高,超出 outer
        LayoutEngine.Arrange(outer, new RectF(0, 0, 64, 32));
        var mesh = new MeshBuilder();
        outer.Draw(new PaintContext(mesh, atlas, InputState.Empty));
        using var renderer = new Renderer(ctx);
        renderer.Submit(mesh, new SizeF(64, 64), ctx.MainFramebuffer);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, PixelAt(px, 32, 10));    // 内部:red(B 存 0,G 0,R 255)
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, PixelAt(px, 32, 50));   // 越界区被剪→清屏色
        App.Atlas = null;
    }

    static byte[] PixelAt(byte[] px, int x, int y) => px.AsSpan((y * 64 + x) * 4, 4).ToArray();
```

- [ ] **Step 2: 确认失败**(未剪裁时 (32,50) 为红)→ RED。
- [ ] **Step 3: PanelElement.Draw 改造**

```csharp
    public override void Draw(in PaintContext pc)
    {
        RectF parentClip = pc.Mesh.CurrentClip;
        RectF ownClip = Frame.Intersect(parentClip);
        pc.Mesh.SetClip(ownClip);
        if (!Frame.IsEmpty && Background.A != 0)
            Tessellator.AddRoundedRect(pc.Mesh, Frame, CornerRadius, Background,
                new PointF(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f));
        if (Children is not null)
            foreach (Element child in Children)
                child.Draw(in pc);
        pc.Mesh.SetClip(parentClip);
    }
```

- [ ] **Step 4: 通过 + 全量 99/99** → Run: `dotnet test`
- [ ] **Step 5: 提交** `git add -A && git commit -m "feat: frame-intersection clipping in panels"`

---

### Task 5: DPI 缩放管线

**Files:**
- Modify: `src/SharpUI/App.cs`(ScaleFactor + 接线)、`src/SharpUI/InputMapper.cs`(scale 参)、`src/SharpUI/Elements/TextAtlas.cs`(物理光栅)、`src/SharpUI/Renderer.cs`(T3 已含 scale)
- Test: `tests/SharpUI.Tests/RendererTests.cs`(追加 1,Gpu)、`tests/SharpUI.Tests/TextAtlasTests.cs`(追加 1,Gpu)

**Interfaces:**
- Produces:
  - `App.ScaleFactor : float`(公开只读;`Math.Clamp(window.ScaleFactor.X, 0.5f, 4f)`)
  - `InputMapper.Map(InputSnapshot, float scale = 1f)`(鼠标 ÷scale)
  - `TextAtlas(GraphicsContext, float scale = 1f)`:物理字号 `⌈字号×scale⌉` 光栅;Measure 返回逻辑尺寸;DrawRun 四边形 = 物理位图 ÷scale(规格 D18)
  - App.Run 接线:TextAtlas(scale)、Map(snapshot, scale)、screen 逻辑尺寸、Submit(mesh, screen, fb, scale)

- [ ] **Step 1: 追加失败 GPU 测试(2)**

RendererTests:
```csharp
    [Fact]
    [Trait("Category", "Gpu")]
    public void ScaleTwoX_LogicalQuadCoversSameFraction()
    {
        using var ctx = GraphicsContext.CreateHeadless(128, 128);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        var mesh = new MeshBuilder();
        float inv = 1f / GraphicsContext.AtlasSize;
        mesh.AddQuad(new RectF(16, 16, 32, 32), new RectF(0, 0, inv, inv), Color32.White);
        using var renderer = new Renderer(ctx);
        renderer.Submit(mesh, new SizeF(64, 64), ctx.MainFramebuffer, scale: 2f);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 64, 64));   // 物理中心=逻辑中心
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 4, 4));
    }
```

TextAtlasTests:
```csharp
    [Fact]
    [Trait("Category", "Gpu")]
    public void Measure_LogicalSizeInvariantAcrossScales()
    {
        using var ctx1 = GraphicsContext.CreateHeadless(64, 64);
        using var atlas1 = new TextAtlas(ctx1, 1f);
        using var ctx2 = GraphicsContext.CreateHeadless(64, 64);
        using var atlas2 = new TextAtlas(ctx2, 2f);
        SizeF m1 = atlas1.Measure("Hello", 16f);
        SizeF m2 = atlas2.Measure("Hello", 16f);
        Assert.InRange(m2.Width, m1.Width - 1.5f, m1.Width + 1.5f);   // 字体度量比例回除,±1.5 容差
        Assert.InRange(m2.Height, m1.Height - 1.5f, m1.Height + 1.5f);
    }
```

- [ ] **Step 2: 确认失败** → RED。
- [ ] **Step 3: InputMapper.Map 加 scale**

```csharp
    public static RawInput Map(InputSnapshot snapshot, float scale = 1f) => new(
        new PointF(snapshot.MousePosition.X / scale, snapshot.MousePosition.Y / scale),
        LeftEvents(snapshot.MouseEvents),
        MapKeys(snapshot.KeyEvents),
        snapshot.IsMouseDown(MouseButton.Left));
```

- [ ] **Step 4: TextAtlas 改造(要点,全文以既有文件为基线最小修改)**
  - 字段 `readonly float _scale;`,ctor `(GraphicsContext ctx, float scale = 1f)`
  - `static int SizeKey(float fontSize, float scale) => Math.Max(4, (int)MathF.Ceiling(fontSize * scale));` — 所有 GetFont/GetLineMetrics/Measure/DrawRun/Rasterize 的字号键统一走物理键;Measure/DrawRun 的 size 参数仍为逻辑值,入口处 `int size = SizeKey(fontSize, _scale)`
  - `Measure` 返回:`new SizeF(物理宽 / _scale, (ascent + descent) / _scale)`
  - `DrawRun`:`baselineY = topLeft.Y + ascent / _scale`;字形四边形 `(g.Width / _scale, g.Height / _scale)`,顶 `baselineY - g.BearingY / _scale`,步进 `g.Advance / _scale`(GlyphEntry 保持物理值,除法只在 DrawRun/Measure 出口)
- [ ] **Step 5: App.cs——ScaleFactor 与接线**

Create 内(设备创建后):`float scale = Math.Clamp(window.ScaleFactor.X, 0.5f, 4f);` 并传入 App ctor(新增私有字段+公开属性 `public float ScaleFactor { get; }`)。Run 内:
```csharp
        Atlas = new TextAtlas(_ctx, ScaleFactor);
        // 循环内:
        RawInput raw = InputMapper.Map(snapshot, ScaleFactor);
        SizeF screen = new(_window.Width / ScaleFactor, _window.Height / ScaleFactor);
        // Submit:
        _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer, ScaleFactor);
```
(RawInput 声明行随循环首行 `InputSnapshot snapshot = _window.PumpEvents();` 保持不变。)

- [ ] **Step 6: 通过 + 全量 101/101** → Run: `dotnet test`
- [ ] **Step 7: 提交** `git add -A && git commit -m "feat: dpi scale pipeline for input text and scissor"`

---

### Task 6: 性能再基线 + 冒烟

**Files:**
- Modify: `tests/SharpUI.Tests/PerfTests.cs`(场景文案已 T1 改;此处仅确认)、无源码改动
- Test: 既有套件

- [ ] **Step 1: Release 门禁(场景已含剪裁路径——Panel Draw 现在走 SetClip×2/面板)** → Run: `dotnet test -c Release --filter FullyQualifiedName~PerfTests`,记录数字(预期:面板 SetClip 使 ClipRuns 非零,走区段批次;分配略增)
- [ ] **Step 2: Debug 全量 + 冒烟(maxFrames:30 → 恢复)** → Run: `dotnet test`(101/101)→ 冒烟
- [ ] **Step 3: 提交**(基线自动刷新;如有变化)→ `git add -A && git commit -m "test: m2b-ii perf baseline with clipping batches"`

---

## 任务依赖图

```
T1(独立)→ T2 → T3 → T4 → T5 → T6
```

## M2b-ii 完成定义

- [ ] `dotnet test` 与 Release 全绿(101/101 含 GPU)
- [ ] 既有 LayoutTests 6 个零改动仍绿(D16 锁定语义)
- [ ] 性能门禁绿;数字记录(含剪裁批次)
- [ ] Counter 冒烟(视觉验收归用户:溢出内容被裁、高 DPI 屏文字清晰度提升)
- [ ] 零 unsafe、公开面与 Interfaces 一致
