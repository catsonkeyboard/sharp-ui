using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>T2(拖拽捕获原语 + Checkbox/Radio):合成事件经 Interaction.Update(模式参照 InteractionTests)+
/// 键盘经 FocusManager.HandleKeys(模式参照 FocusTests)+ 视觉分支经 headless D3D11 图集网格断言。
/// Checkbox/Radio 不捕获拖拽(CapturesDrag=false 默认):按下+松开同元素即切换;捕获机制由
/// SliderProbe 替身(T3 Slider 前身)覆盖最小语义(Down 捕获 → 悬停锁定 → Up 释放)。</summary>
[Collection("interaction")]
public class ControlsTests
{
    /// <summary>捕获原语替身:命中目标 + 声明可捕获 + 可激活(点击完成路径与 Button 同管道)。</summary>
    sealed class SliderProbe : PanelElement, IActivatable
    {
        public int Clicks;
        internal override bool CapturesDrag => true;
        public bool CanActivate => true;
        public void InvokeClick() => Clicks++;
    }

    static SliderProbe ProbeAt(RectF frame)
    {
        var probe = new SliderProbe { IsHitTarget = true };
        LayoutEngine.Arrange(probe, frame);
        IdResolver.Assign(probe);
        return probe;
    }

    /// <summary>键盘测试 harness:控件为 Column 根的直接子元素(Resolvable id + Tab 序 = DOM 序)。</summary>
    static (PanelElement Root, FocusManager Focus) FrameWith(params Element[] controls)
    {
        PanelElement root = Ui.Column(controls);
        LayoutEngine.Arrange(root, new RectF(0, 0, 120, 40 * controls.Length));
        IdResolver.Assign(root);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        return (root, focus);
    }

    // ---- 捕获原语(Interaction 机制)----

    [Fact]
    public void Capture_DownCaptures_MotionLocksHover_UpReleasesAndCompletes()
    {
        SliderProbe probe = ProbeAt(new RectF(0, 0, 100, 40));
        var s = new InputSession();

        InputState down = Interaction.Update(s, probe, new RawInput(new PointF(50, 20), new[] { true }));
        Assert.Equal(probe.ResolvedId, down.CapturedId);      // Down 命中可捕获元素 → 捕获
        Assert.Equal(probe.ResolvedId, down.HoveredId);

        InputState drag = Interaction.Update(s, probe, new RawInput(new PointF(500, 500), Array.Empty<bool>()));
        Assert.Equal(probe.ResolvedId, drag.HoveredId);       // 捕获期间悬停锁定捕获者(HitTest 旁路:光标在元素外)
        Assert.Equal(probe.ResolvedId, drag.CapturedId);

        InputState up = Interaction.Update(s, probe, new RawInput(new PointF(500, 500), new[] { false }));
        Assert.Equal(1, probe.Clicks);                        // 元素外松开仍路由捕获者完成点击
        Assert.Null(up.CapturedId);                           // LeftUp 释放捕获
    }

    [Fact]
    public void CancelPress_ClearsCapture_MotionDoesNotFollow()
    {
        // 评审 Important#1 失焦自愈:CancelPress 只清 PressedOn 不清 CapturedId → 捕获残留,
        // 后续 Motion 继续锁定悬停(自愈缺陷)。捕获生命周期与按压一致,取消应同步清捕获。
        SliderProbe probe = ProbeAt(new RectF(0, 0, 100, 40));
        var s = new InputSession();
        Interaction.Update(s, probe, new RawInput(new PointF(50, 20), new[] { true }));
        Assert.NotNull(s.CapturedId);                         // 捕获中(前置:Down 已捕获)

        Interaction.CancelPress(s);                           // 失焦 → App 按窗取消
        InputState motion = Interaction.Update(s, probe, new RawInput(new PointF(500, 500), Array.Empty<bool>()));
        Assert.Null(motion.CapturedId);                       // 捕获已取消:后续 Motion 不再跟随
        Assert.Null(motion.HoveredId);                        // 悬停锁定解除,回归空间命中(光标在元素外 = 无)

        InputState up = Interaction.Update(s, probe, new RawInput(new PointF(500, 500), new[] { false }));
        Assert.Equal(0, probe.Clicks);                        // 按压目标随取消失效,元素外松开不完成点击
        Assert.Null(up.CapturedId);
    }

    [Fact]
    public void Capture_NewDownElsewhere_Overrides()
    {
        SliderProbe probe = ProbeAt(new RectF(0, 0, 100, 40));
        var s = new InputSession();
        Interaction.Update(s, probe, new RawInput(new PointF(50, 20), new[] { true }));
        InputState down2 = Interaction.Update(s, probe, new RawInput(new PointF(300, 300), new[] { true }));
        Assert.Null(down2.CapturedId);                        // 新 LeftDown 命中空白:旧捕获被覆盖清空
        InputState up = Interaction.Update(s, probe, new RawInput(new PointF(50, 20), new[] { false }));
        Assert.Equal(0, probe.Clicks);                        // 按压目标已随覆盖失效
        Assert.Null(up.CapturedId);
    }

    [Fact]
    public void NonCapturingControl_DoesNotCapture_OffElementUpNoToggle()
    {
        int toggles = 0;
        CheckboxElement cb = new("选项", false, _ => toggles++);
        LayoutEngine.Arrange(cb, new RectF(0, 0, 100, 40));
        IdResolver.Assign(cb);
        var s = new InputSession();

        InputState down = Interaction.Update(s, cb, new RawInput(new PointF(50, 20), new[] { true }));
        Assert.Null(down.CapturedId);                         // 点击切换控件不捕获(CapturesDrag=false 默认)
        InputState up = Interaction.Update(s, cb, new RawInput(new PointF(300, 300), new[] { false }));
        Assert.Equal(0, toggles);                             // 拖出元素外松开:点击语义不触发(既有语义保持)
        Assert.Null(up.CapturedId);
    }

    // ---- Checkbox ----

    [Fact]
    public void Checkbox_ClickToggles_OnChangedOnceWithNewValue()
    {
        bool? reported = null;
        CheckboxElement cb = new("选项", false, v => reported = v);
        LayoutEngine.Arrange(cb, new RectF(0, 0, 100, 40));
        IdResolver.Assign(cb);
        var s = new InputSession();

        Interaction.Update(s, cb, new RawInput(new PointF(50, 20), new[] { true }));
        InputState up = Interaction.Update(s, cb, new RawInput(new PointF(50, 20), new[] { false }));

        Assert.True(reported);                                // false → true 恰一次
        Assert.NotNull(up.ClickedIds);
        Assert.Contains(cb.ResolvedId!, up.ClickedIds!);
    }

    [Fact]
    public void Checkbox_FocusedSpaceOrEnter_Toggles()
    {
        int fires = 0;
        CheckboxElement cb = new("选项", false, _ => fires++); cb.Key = "opt";
        (PanelElement _, FocusManager focus) = FrameWith(cb);

        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });   // Tab 聚焦(DOM 序首个)
        Assert.True(focus.IsFocused(cb));
        focus.HandleKeys(new[] { new KeyEventData(Key.Space, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        Assert.Equal(2, fires);                               // 切换回调各触发一次
    }

    [Fact]
    public void Checkbox_Disabled_NoCallback_NoFocus_NoLedger()
    {
        int fires = 0;
        CheckboxElement cb = new("选项", true, _ => fires++) { Enabled = false }; cb.Key = "opt";
        (PanelElement _, FocusManager focus) = FrameWith(cb);

        Assert.False(cb.Focusable);                           // 禁用即退出焦点序
        Assert.False(cb.CanActivate);
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.Null(focus.FocusedId);                         // Tab 不可达
        focus.SetFocus(cb);                                   // 程序性聚焦仍不激活
        focus.HandleKeys(new[] { new KeyEventData(Key.Space, true, false) });
        Assert.Equal(0, fires);

        LayoutEngine.Arrange(cb, new RectF(0, 0, 100, 40));   // 指针路径:按下+松开同元素
        var s = new InputSession();
        Interaction.Update(s, cb, new RawInput(new PointF(50, 20), new[] { true }));
        InputState up = Interaction.Update(s, cb, new RawInput(new PointF(50, 20), new[] { false }));
        Assert.Equal(0, fires);
        Assert.Null(up.ClickedIds);                           // 禁用不入点击账本
    }

    // ---- Radio ----

    [Fact]
    public void Radio_Click_FiresOnSelectedOnce()
    {
        int selected = 0;
        RadioElement r1 = new("一", "g", false, () => selected++);
        LayoutEngine.Arrange(r1, new RectF(0, 0, 100, 40));
        IdResolver.Assign(r1);
        var s = new InputSession();

        Interaction.Update(s, r1, new RawInput(new PointF(50, 20), new[] { true }));
        Interaction.Update(s, r1, new RawInput(new PointF(50, 20), new[] { false }));

        Assert.Equal(1, selected);
    }

    [Fact]
    public void Radio_ClickAlreadySelected_NoCallback()
    {
        int selected = 0;
        RadioElement r1 = new("一", "g", true, () => selected++);
        LayoutEngine.Arrange(r1, new RectF(0, 0, 100, 40));
        IdResolver.Assign(r1);
        var s = new InputSession();

        Interaction.Update(s, r1, new RawInput(new PointF(50, 20), new[] { true }));
        Interaction.Update(s, r1, new RawInput(new PointF(50, 20), new[] { false }));

        Assert.Equal(0, selected);                            // 已选中再点 = 值未变化,不回调
    }

    [Fact]
    public void Radio_ArrowKeys_CycleFocusWithinGroup()
    {
        int sel2 = 0;
        RadioElement r1 = new("一", "g", true, () => { }); r1.Key = "r1";
        RadioElement r2 = new("二", "g", false, () => sel2++); r2.Key = "r2";
        RadioElement other = new("三", "h", false, () => { }); other.Key = "r3";   // 异组不参与
        (PanelElement _, FocusManager focus) = FrameWith(r1, r2, other);

        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(r1));
        focus.HandleKeys(new[] { new KeyEventData(Key.Right, true, false) });
        Assert.True(focus.IsFocused(r2));                     // 组内下一
        focus.HandleKeys(new[] { new KeyEventData(Key.Down, true, false) });
        Assert.True(focus.IsFocused(r1));                     // 环回(跳过异组 other)
        focus.HandleKeys(new[] { new KeyEventData(Key.Up, true, false) });
        Assert.True(focus.IsFocused(r2));                     // 反向
        focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        Assert.Equal(1, sel2);                                // 选中由 Space/Enter 承担
    }

    [Fact]
    public void Radio_Disabled_SkippedByArrows_NoActivation()
    {
        int fires = 0;
        RadioElement r1 = new("一", "g", true, () => { }); r1.Key = "r1";
        RadioElement r2 = new("二", "g", false, () => fires++) { Enabled = false }; r2.Key = "r2";
        (PanelElement _, FocusManager focus) = FrameWith(r1, r2);

        Assert.False(r2.Focusable);
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(r1));
        focus.HandleKeys(new[] { new KeyEventData(Key.Right, true, false) });
        Assert.True(focus.IsFocused(r1));                     // 禁用项被跳过,焦点原地不动
        focus.SetFocus(r2);                                   // 程序性聚焦仍不激活
        focus.HandleKeys(new[] { new KeyEventData(Key.Space, true, false) });
        Assert.Equal(0, fires);
    }

    // ---- 视觉分支(headless 网格断言,label 空文本隔离字体面)----

    static (int Verts, bool Accent, bool White) DrawInto(
        MeshBuilder mesh, TextAtlas atlas, Element e, string? hoverId, string? focusedId)
    {
        IdResolver.Assign(e);
        int before = mesh.VertexCount;
        var input = new InputState(new PointF(-1f, -1f), false, false, false, null,
            HoveredId: hoverId, FocusedId: focusedId);
        e.Draw(new PaintContext(mesh, atlas, input));
        bool accent = false, white = false;
        foreach (UiVertex v in mesh.Vertices.Slice(before))   // 只扫本次新增段(mesh 跨控件累积)
        {
            if (v.R == 0x0C && v.G == 0x63 && v.B == 0xE4) accent = true;     // 常态蓝 0x0C63E4
            if (v.R == 255 && v.G == 255 && v.B == 255) white = true;
        }
        return (mesh.VertexCount - before, accent, white);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void CheckboxRadio_DrawBranches_VisualStateDiffers()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();

        CheckboxElement off = new("", false, null);
        CheckboxElement on = new("", true, null);
        CheckboxElement disabledOn = new("", true, null) { Enabled = false };
        CheckboxElement focused = new("", false, null);
        CheckboxElement hovered = new("", false, null);
        RadioElement radioOff = new("", "g", false, null);
        RadioElement radioOn = new("", "g", true, null);

        (int offVerts, bool offAccent, bool offWhite) = DrawInto(mesh, atlas, off, null, null);
        (int onVerts, bool onAccent, bool onWhite) = DrawInto(mesh, atlas, on, null, null);
        (_, bool disAccent, bool disWhite) = DrawInto(mesh, atlas, disabledOn, null, null);
        (_, bool focusAccent, _) = DrawInto(mesh, atlas, focused, null, focused.ResolvedId);
        (_, bool hoverAccent, _) = DrawInto(mesh, atlas, hovered, hovered.ResolvedId, null);
        (_, bool radioOffAccent, bool radioOffWhite) = DrawInto(mesh, atlas, radioOff, null, null);
        (_, bool radioOnAccent, bool radioOnWhite) = DrawInto(mesh, atlas, radioOn, null, null);

        Assert.True(onVerts > offVerts);                      // 勾 = 两段 quad(选中多 8 顶点)
        Assert.False(offAccent); Assert.False(offWhite);      // 未勾:框缘/框面灰系,无白勾
        Assert.True(onAccent); Assert.True(onWhite);          // 勾选:框面 0x0C63E4 + 白勾
        Assert.False(disAccent); Assert.False(disWhite);      // 禁用:全灰,无蓝无白
        Assert.False(focusAccent); Assert.False(hoverAccent); // 聚焦 0x60A5FA / 悬停 0x3B82F6 分支非常态蓝
        Assert.False(radioOffAccent); Assert.False(radioOffWhite);
        Assert.True(radioOnAccent); Assert.True(radioOnWhite);// 选中:环面 0x0C63E4 + 白内点
    }
}
