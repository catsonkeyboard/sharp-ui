using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>T3(SliderElement 全功能版):拖拽捕获消费(InputState.CapturedId 面)+ 轨道点击就位续拖 +
/// Step 吸附 + 边界钳制 + Vertical(上=大)+ Range 双 thumb(独立回调/交叉钳制/Down 帧锁存活动 thumb)+
/// 键盘(IKeySteerable 经 FocusManager.HandleKeys,±step/Home/End/连续 ±量程/20)+ 禁用统一门 +
/// 视觉分支(headless 网格断言,模式参照 ControlsTests)。
///
/// 合成事件时序镜像 App:树每帧重建(Rig 回调改调用方值 → 下一帧以新值重建)→ Interaction.Update →
/// ConsumeInput(Draw 首行同款消费面直调,免 GPU)。控件恒包 Stretch 根取得非空稳定 id("/s"):
/// 根 id 恒空串且 FocusManager.Register 跳过空 id。拖拽帧 held=true(RawInput.LeftHeld 生产语义)。
///
/// 坐标换算(公开常量推得,测试不写死魔数):轨道行程 = 100 − ThumbMain,内缩 ThumbMain/2;
/// 横向 XOf(v) = 4 + t·92(t=(v−min)/(max−min));纵向 YOf(v) = 4 + (1−t)·92(上=大)。</summary>
[Collection("interaction")]
public class SliderTests
{
    const float MainLen = 100f;   // 主轴 Frame 长(横向宽/纵向高)

    static float XOf(float value, float min = 0f, float max = 100f) =>
        SliderElement.ThumbMain / 2f + (value - min) / (max - min) * (MainLen - SliderElement.ThumbMain);

    static float YOf(float value, float min = 0f, float max = 100f) =>
        SliderElement.ThumbMain / 2f + (1f - (value - min) / (max - min)) * (MainLen - SliderElement.ThumbMain);

    /// <summary>每帧重建树的 harness(同 App:构建→Assign→Update→消费;键盘帧→注册→聚焦→HandleKeys)。</summary>
    sealed class Rig
    {
        public SliderElement Slider = null!;
        PanelElement _root = null!;
        readonly InputSession _session = new();
        readonly FocusManager _focus = new();
        readonly Func<SliderElement> _build;
        readonly bool _vertical;

        public Rig(Func<SliderElement> build, bool vertical = false)
        {
            _build = build;
            _vertical = vertical;
            Rebuild();
        }

        public void Rebuild()
        {
            Slider = _build();
            Slider.Key = "s";
            _root = _vertical ? Ui.Row(Slider) : Ui.Column(Slider);
            _root.Style.CrossAlign = CrossAxisAlignment.Stretch;   // 子拉伸至根 Frame(横向宽 100 / 纵向高 100)
            LayoutEngine.Arrange(_root, _vertical ? new RectF(0, 0, 40, MainLen) : new RectF(0, 0, MainLen, 40));
            IdResolver.Assign(_root);
        }

        /// <summary>指针帧:Update(合成事件,held = LeftHeld 生产语义)→ ConsumeInput(Draw 首行同款)。</summary>
        public InputState Frame(PointF mouse, bool[] events, bool held = false)
        {
            Rebuild();
            InputState input = Interaction.Update(_session, _root, new RawInput(mouse, events, LeftHeld: held));
            Slider.ConsumeInput(in input);
            return input;
        }

        /// <summary>键盘帧:重建 → 注册 → 程序性聚焦 → 单键 HandleKeys(App 中 HandleKeys 落在新树上)。</summary>
        public FocusManager KeyFrame(Key key)
        {
            Rebuild();
            _focus.BeginFrame();
            TreeWalk.Register(_root, _focus);
            _focus.SetFocus(Slider);
            _focus.HandleKeys(new[] { new KeyEventData(key, true, false) });
            return _focus;
        }

        public bool TabFocus()
        {
            Rebuild();
            _focus.BeginFrame();
            TreeWalk.Register(_root, _focus);
            _focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
            return _focus.IsFocused(Slider);
        }
    }

    // ---- DSL ----

    [Fact]
    public void UiDsl_Slider_ConstructsElement()
    {
        SliderElement s = Ui.Slider(0f, 10f, 5f);             // M-a: 省略 onChanged 默认 null
        Assert.IsType<SliderElement>(s);
        Assert.True(s.Focusable);                             // 默认入焦点序

        var rig = new Rig(() => Ui.Slider(0f, 10f, 5f));      // 无回调拖拽与键盘交互不抛
        rig.Frame(new PointF(XOf(8f, 0f, 10f), 10f), new[] { true });
        rig.KeyFrame(Key.Right);
    }

    // ---- 拖拽(捕获消费面)----

    [Fact]
    public void Drag_MotionFollows_ClampsAtBounds_CallbackOnlyOnChange_UpReleases()
    {
        var calls = new List<float>();
        float value = 50f;
        var rig = new Rig(() => new SliderElement(0f, 100f, value, v => { value = v; calls.Add(v); }).Step(10f));

        InputState down = rig.Frame(new PointF(XOf(50f), 10f), new[] { true });
        Assert.NotNull(down.CapturedId);                      // Down 于 thumb → 捕获
        Assert.Empty(calls);                                  // thumb 中心按下:值未变化不回调

        rig.Frame(new PointF(500f, 10f), Array.Empty<bool>(), held: true);   // Motion 出界右 → 钳制 100
        Assert.Equal(new float[] { 100f }, calls);

        rig.Frame(new PointF(500f, 10f), Array.Empty<bool>(), held: true);   // 同位再 Motion:值未变不回调
        Assert.Equal(new float[] { 100f }, calls);

        rig.Frame(new PointF(-500f, 10f), Array.Empty<bool>(), held: true);  // 出界左 → 钳制 0
        Assert.Equal(new float[] { 100f, 0f }, calls);

        rig.Frame(new PointF(-500f, 10f), Array.Empty<bool>(), held: true);  // 边界上继续 Motion:不回调
        Assert.Equal(new float[] { 100f, 0f }, calls);

        InputState up = rig.Frame(new PointF(-500f, 10f), new[] { false });
        Assert.Null(up.CapturedId);                           // Up 释放捕获

        rig.Frame(new PointF(200f, 10f), Array.Empty<bool>(), held: true);   // 释放后 Motion:不跟随
        Assert.Equal(new float[] { 100f, 0f }, calls);
    }

    [Fact]
    public void TrackClick_JumpsToPosition_ThenCapturesAndContinues()
    {
        var calls = new List<float>();
        float value = 0f;
        var rig = new Rig(() => new SliderElement(0f, 100f, value, v => { value = v; calls.Add(v); }).Step(10f));

        InputState down = rig.Frame(new PointF(XOf(47f), 10f), new[] { true });   // 点轨道 47 → 吸附 50 就位
        Assert.NotNull(down.CapturedId);                      // 轨道点击同样捕获续拖
        Assert.Equal(new float[] { 50f }, calls);

        rig.Frame(new PointF(XOf(70f), 10f), Array.Empty<bool>(), held: true);   // 就位后继续拖
        Assert.Equal(new float[] { 50f, 70f }, calls);

        InputState up = rig.Frame(new PointF(XOf(70f), 10f), new[] { false });
        Assert.Null(up.CapturedId);
    }

    [Fact]
    public void Step_SnapsToGrid_ContinuousKeepsRawPosition()
    {
        var snapped = new List<float>();
        float sValue = 0f;
        var stepped = new Rig(() => new SliderElement(0f, 100f, sValue, v => { sValue = v; snapped.Add(v); }).Step(10f));
        stepped.Frame(new PointF(XOf(47f), 10f), new[] { true });   // 简报例:0..100 step 10 拖至 47 → 50

        var continuous = new List<float>();
        float cValue = 0f;
        var raw = new Rig(() => new SliderElement(0f, 100f, cValue, v => { cValue = v; continuous.Add(v); }));
        raw.Frame(new PointF(XOf(47f), 10f), new[] { true });       // 连续:同位 → 原始映射 47

        Assert.Equal(new float[] { 50f }, snapped);
        float single = Assert.Single(continuous);
        Assert.InRange(single, 46.99f, 47.01f);
    }

    [Fact]
    public void Vertical_TopIsMax_DragDownDecreases()
    {
        var calls = new List<float>();
        float value = 100f;
        var rig = new Rig(() => new SliderElement(0f, 100f, value, v => { value = v; calls.Add(v); }).Step(10f).Vertical(), vertical: true);

        InputState down = rig.Frame(new PointF(10f, YOf(100f)), new[] { true });   // thumb 顶(=max)按下
        Assert.NotNull(down.CapturedId);
        Assert.Empty(calls);

        rig.Frame(new PointF(10f, 50f), Array.Empty<bool>(), held: true);          // 向下拖 → 减小(上=大)
        Assert.Equal(new float[] { 50f }, calls);

        rig.Frame(new PointF(10f, 500f), Array.Empty<bool>(), held: true);         // 出下界 → min
        Assert.Equal(new float[] { 50f, 0f }, calls);
    }

    // ---- Range(双 thumb)----

    [Fact]
    public void Range_FastDragPastMidpoint_KeepsLatchedThumb()
    {
        // Down 帧锁存活动 thumb:单帧大步拖越两 thumb 中点不得误切(无锁存会按"最近 thumb"翻面劫持)。
        App.State = new StateStore();
        try
        {
            var minCalls = new List<float>();
            var maxCalls = new List<float>();
            float minV = 40f, maxV = 60f;
            var rig = new Rig(() => new SliderElement(0f, 100f, minV, v => { minV = v; minCalls.Add(v); })
                .Range(maxV, v => { maxV = v; maxCalls.Add(v); }).Step(10f));

            InputState down = rig.Frame(new PointF(XOf(60f), 10f), new[] { true });   // max thumb 上按下
            Assert.NotNull(down.CapturedId);
            rig.Frame(new PointF(XOf(20f), 10f), Array.Empty<bool>(), held: true);    // 单帧拖到 min thumb 以左

            Assert.Equal(new float[] { 50f }, maxCalls);          // max 侧操作:交叉钳制抬到 minV+step = 50
            Assert.Empty(minCalls);                               // min thumb 未被劫持
        }
        finally { App.State = null; }
    }

    [Fact]
    public void Range_IndependentCallbacks_PerThumb()
    {
        App.State = new StateStore();
        try
        {
            var minCalls = new List<float>();
            var maxCalls = new List<float>();
            float minV = 40f, maxV = 60f;
            var rig = new Rig(() => new SliderElement(0f, 100f, minV, v => { minV = v; minCalls.Add(v); })
                .Range(maxV, v => { maxV = v; maxCalls.Add(v); }).Step(10f));

            rig.Frame(new PointF(XOf(60f), 10f), new[] { true });                     // max thumb:就位(值未变)
            rig.Frame(new PointF(XOf(30f), 10f), Array.Empty<bool>(), held: true);    // 拖 max → 交叉钳制到 50
            rig.Frame(new PointF(XOf(10f), 10f), new[] { true });                     // 改按 min thumb(重新锁存)
            rig.Frame(new PointF(XOf(45f), 10f), Array.Empty<bool>(), held: true);    // 拖 min → 吸附 50 → 钳回 maxV−step = 40

            Assert.Equal(new float[] { 10f, 40f }, minCalls);     // min 侧独立回调
            Assert.Equal(new float[] { 50f }, maxCalls);          // max 侧不受 min 拖拽影响
        }
        finally { App.State = null; }
    }

    [Fact]
    public void Range_CrossClamp_BothDirections_NoOverflowCallback()
    {
        App.State = new StateStore();
        try
        {
            var minCalls = new List<float>();
            var maxCalls = new List<float>();
            float minV = 40f, maxV = 50f;
            var rig = new Rig(() => new SliderElement(0f, 100f, minV, v => { minV = v; minCalls.Add(v); })
                .Range(maxV, v => { maxV = v; maxCalls.Add(v); }).Step(10f));

            rig.Frame(new PointF(XOf(38f), 10f), new[] { true });                     // min thumb 上按下
            rig.Frame(new PointF(XOf(55f), 10f), Array.Empty<bool>(), held: true);    // 拖 min 越 max → 钳回 maxV−gap

            rig.Frame(new PointF(XOf(50f), 10f), new[] { true });                     // max thumb 上按下(重锁存)
            rig.Frame(new PointF(XOf(30f), 10f), Array.Empty<bool>(), held: true);    // 拖 max 越 min → 钳回 minV+gap

            Assert.Empty(minCalls);                               // 交叉钳制:不产生越界回调
            Assert.Empty(maxCalls);
        }
        finally { App.State = null; }
    }

    // ---- 键盘(IKeySteerable 经 FocusManager.HandleKeys)----

    [Fact]
    public void Keyboard_StepArrows_HomeEnd_BoundaryNoCallback()
    {
        var calls = new List<float>();
        float value = 50f;
        var rig = new Rig(() => new SliderElement(0f, 100f, value, v => { value = v; calls.Add(v); }).Step(10f));

        Assert.True(rig.TabFocus());                          // Tab 可达
        rig.KeyFrame(Key.Right);
        Assert.Equal(new float[] { 60f }, calls);
        rig.KeyFrame(Key.Left);
        Assert.Equal(new float[] { 60f, 50f }, calls);
        rig.KeyFrame(Key.Home);
        Assert.Equal(new float[] { 60f, 50f, 0f }, calls);
        rig.KeyFrame(Key.End);
        Assert.Equal(new float[] { 60f, 50f, 0f, 100f }, calls);
        rig.KeyFrame(Key.Right);                              // 已在 max:值未变不回调
        Assert.Equal(new float[] { 60f, 50f, 0f, 100f }, calls);
    }

    [Fact]
    public void Keyboard_Continuous_StepIsRangeOver20()
    {
        var calls = new List<float>();
        float value = 50f;
        var rig = new Rig(() => new SliderElement(0f, 100f, value, v => { value = v; calls.Add(v); }));   // step 0 连续

        rig.KeyFrame(Key.Right);                              // ±(max−min)/20 = ±5
        Assert.Equal(new float[] { 55f }, calls);
        rig.KeyFrame(Key.Left);
        Assert.Equal(new float[] { 55f, 50f }, calls);
    }

    [Fact]
    public void Keyboard_Vertical_UpIncreases_DownDecreases()
    {
        var calls = new List<float>();
        float value = 50f;
        var rig = new Rig(() => new SliderElement(0f, 100f, value, v => { value = v; calls.Add(v); }).Step(10f).Vertical(), vertical: true);

        rig.KeyFrame(Key.Up);                                 // 上 = 大
        Assert.Equal(new float[] { 60f }, calls);
        rig.KeyFrame(Key.Down);
        Assert.Equal(new float[] { 60f, 50f }, calls);
    }

    [Fact]
    public void Keyboard_Range_DefaultMinThumb_ThenPointerLatched()
    {
        App.State = new StateStore();
        try
        {
            var minCalls = new List<float>();
            var maxCalls = new List<float>();
            float minV = 40f, maxV = 60f;
            var rig = new Rig(() => new SliderElement(0f, 100f, minV, v => { minV = v; minCalls.Add(v); })
                .Range(maxV, v => { maxV = v; maxCalls.Add(v); }).Step(10f));

            rig.KeyFrame(Key.Right);                          // 从未操作:默认 min thumb
            Assert.Equal(new float[] { 50f }, minCalls);
            Assert.Empty(maxCalls);

            rig.Frame(new PointF(XOf(60f), 10f), new[] { true });   // 指针锁存 max thumb(按下即"最后操作")
            rig.KeyFrame(Key.Right);                          // 键盘续接 max thumb
            Assert.Equal(new float[] { 50f }, minCalls);
            Assert.Equal(new float[] { 70f }, maxCalls);

            rig.KeyFrame(Key.Home);                           // Home = 全局 min,交叉钳制抬到 minV+gap = 60
            Assert.Equal(new float[] { 50f }, minCalls);
            Assert.Equal(new float[] { 70f, 60f }, maxCalls);
        }
        finally { App.State = null; }
    }

    [Fact]
    public void Keyboard_Range_SpaceTogglesActiveThumb_SingleThumbDoesNotConsumeSpace()
    {
        App.State = new StateStore();
        try
        {
            var minCalls = new List<float>();
            var maxCalls = new List<float>();
            float minV = 20f, maxV = 80f;
            var rig = new Rig(() => new SliderElement(0f, 100f, minV, v => { minV = v; minCalls.Add(v); })
                .Range(maxV, v => { maxV = v; maxCalls.Add(v); }).Step(10f));

            // 聚焦 Range(20,80) → Right 动 min(默认 0: 20 → 30)
            rig.KeyFrame(Key.Right);
            Assert.Equal(new float[] { 30f }, minCalls);
            Assert.Empty(maxCalls);

            // Space 切到 1 → Right 动 max(80 → 90)
            rig.KeyFrame(Key.Space);
            rig.KeyFrame(Key.Right);
            Assert.Equal(new float[] { 30f }, minCalls);
            Assert.Equal(new float[] { 90f }, maxCalls);

            // 再 Space 切回 0 → Home 动 min(30 → 0)
            rig.KeyFrame(Key.Space);
            rig.KeyFrame(Key.Home);
            Assert.Equal(new float[] { 30f, 0f }, minCalls);
            Assert.Equal(new float[] { 90f }, maxCalls);

            // 单 thumb 模式下 Key.Space 不消费(返回 false)
            var single = new SliderElement(0f, 100f, 50f, null);
            Assert.False(single.HandleKey(Key.Space));
        }
        finally { App.State = null; }
    }

    // ---- 禁用 ----

    [Fact]
    public void Disabled_NoPointerResponse_NoKeyboardResponse()
    {
        int fires = 0;
        var rig = new Rig(() =>
        {
            var s = new SliderElement(0f, 100f, 50f, _ => fires++);
            s.Enabled = false;
            return s;
        });

        InputState down = rig.Frame(new PointF(50f, 10f), new[] { true });
        Assert.Null(down.CapturedId);                         // 禁用不捕获(CapturesDrag 随 Enabled 置位)
        rig.Frame(new PointF(500f, 10f), Array.Empty<bool>(), held: true);
        Assert.Equal(0, fires);

        Assert.False(rig.TabFocus());                         // 禁用退出焦点序
        rig.KeyFrame(Key.Right);                              // 程序性聚焦仍不消费(统一禁用门)
        Assert.Equal(0, fires);

        var fake = new InputState(new PointF(50f, 10f), true, true, false, rig.Slider,
            CapturedId: rig.Slider.ResolvedId);               // 绕过 Interaction 门直喂捕获态
        rig.Slider.ConsumeInput(in fake);
        Assert.Equal(0, fires);
    }

    // ---- 视觉分支(headless 网格断言,同 ControlsTests 手法)----

    static SliderElement Arrange(SliderElement s)
    {
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 40));
        IdResolver.Assign(s);                                 // 先 Assign:调用方读 ResolvedId 作 hover/focus 入参
        return s;
    }

    static (int Verts, bool Accent, bool Hover, bool Focus, bool White, bool ActiveOutline) DrawInto(
        MeshBuilder mesh, TextAtlas atlas, Element e, string? hoverId, string? focusedId, string? capturedId = null)
    {
        IdResolver.Assign(e);
        int before = mesh.VertexCount;
        var input = new InputState(new PointF(-1f, -1f), false, false, false, null,
            HoveredId: hoverId, FocusedId: focusedId, CapturedId: capturedId);
        e.Draw(new PaintContext(mesh, atlas, input));
        bool accent = false, hover = false, focus = false, white = false, outline = false;
        foreach (UiVertex v in mesh.Vertices.Slice(before))   // 只扫本次新增段(mesh 跨控件累积)
        {
            if (v.R == 0x0C && v.G == 0x63 && v.B == 0xE4) accent = true;    // 常态蓝 0x0C63E4
            if (v.R == 0x3B && v.G == 0x82 && v.B == 0xF6) hover = true;     // 悬停 0x3B82F6
            if (v.R == 0x60 && v.G == 0xA5 && v.B == 0xFA) focus = true;     // 聚焦 0x60A5FA
            if (v.R == 0x93 && v.G == 0xC5 && v.B == 0xFD) outline = true;   // Range 活动 thumb 描边 0x93C5FD
            if (v.R == 255 && v.G == 255 && v.B == 255) white = true;
        }
        return (mesh.VertexCount - before, accent, hover, focus, white, outline);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Slider_DrawBranches_VisualStateDiffers()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();

        var normal = Arrange(new SliderElement(0f, 100f, 50f, null));
        var hovered = Arrange(new SliderElement(0f, 100f, 50f, null));
        var focused = Arrange(new SliderElement(0f, 100f, 50f, null));
        var disabled = Arrange(new SliderElement(0f, 100f, 50f, null) { Enabled = false });
        var range = Arrange(new SliderElement(0f, 100f, 40f, null).Range(60f, null));
        var rangeFocused = Arrange(new SliderElement(0f, 100f, 40f, null).Range(60f, null));
        var rangeCaptured = Arrange(new SliderElement(0f, 100f, 40f, null).Range(60f, null));

        var n = DrawInto(mesh, atlas, normal, null, null);
        var h = DrawInto(mesh, atlas, hovered, hovered.ResolvedId, null);
        var f = DrawInto(mesh, atlas, focused, null, focused.ResolvedId);
        var d = DrawInto(mesh, atlas, disabled, null, null);
        var r = DrawInto(mesh, atlas, range, null, null);
        var rf = DrawInto(mesh, atlas, rangeFocused, null, rangeFocused.ResolvedId);
        var rc = DrawInto(mesh, atlas, rangeCaptured, rangeCaptured.ResolvedId, null, rangeCaptured.ResolvedId);

        Assert.True(n.Accent);                                // 常态:已选段 0x0C63E4 + 白 thumb
        Assert.True(n.White);
        Assert.False(n.Hover || n.Focus || n.ActiveOutline);
        Assert.True(h.Hover);                                 // 悬停:0x3B82F6,非常态蓝
        Assert.False(h.Accent);
        Assert.True(f.Focus);                                 // 聚焦:0x60A5FA(聚焦压过悬停)
        Assert.False(f.Accent || f.ActiveOutline);            // 单 thumb 聚焦不画 Range 活动 thumb 描边
        Assert.False(d.Accent || d.Hover || d.Focus || d.White || d.ActiveOutline);   // 禁用:全灰,无蓝无白
        Assert.True(r.Verts > n.Verts);                       // Range:双 thumb 几何更多
        Assert.True(r.Accent && r.White);                     // 已选段照常着色
        Assert.False(r.ActiveOutline);                        // Range 未聚焦/未捕获不画活动描边
        Assert.True(rf.ActiveOutline);                        // Range 聚焦时画 0x93C5FD 活动 thumb 1px 描边
        Assert.True(rf.Verts > r.Verts);
        Assert.True(rc.ActiveOutline);                        // Range 捕获中也画 0x93C5FD 活动 thumb 1px 描边
    }
}
