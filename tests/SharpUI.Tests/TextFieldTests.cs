using SharpUI;
using SharpUI.Backends;
using static SharpUI.Ui;

namespace SharpUI.Tests;

[Collection("interaction")]   // 触碰 App.State/App.Atlas 静态,与其余静态用户串行(沿用 UiScopeTests 惯例)
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
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 30));
        IdResolver.Assign(root);
        tf.SetStateForTest(new TextState(text, text.Length));   // Assign 之后:ResolvedId 已解析
        return (tf, root);
    }

    [Fact]
    public void Feed_CharsPersistThroughStateStore()
    {
        (TextFieldElement tf, PanelElement root) = Field();
        tf.Feed(new[] { new KeyEventData(Key.A, true, false) }, "hi".ToCharArray());   // 键被忽略,字符入文
        tf.Feed(null, "!".ToCharArray());
        Assert.Equal("hi!", tf.CurrentText);
        App.State = null;
    }

    [Fact]
    public void Feed_BackspaceAndArrows()
    {
        (TextFieldElement tf, _) = Field("abc");
        tf.Feed(new[] { new KeyEventData(Key.Left, true, false) }, null);
        tf.Feed(new[] { new KeyEventData(Key.BackSpace, true, false) }, null);
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
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        (TextFieldElement tf, PanelElement root) = Field("ab");
        var mesh = new MeshBuilder();
        root.Draw(new PaintContext(mesh, atlas, InputState.Empty));
        Assert.True(mesh.VertexCount >= 3 * 4);   // 底/描边 + 文本 + 光标 ≥3 四边
        App.Atlas = null;
        App.State = null;
    }

    [Fact]
    public void FocusOnStart_MarksAutoFocusChainably()
    {
        var tf = new TextFieldElement("ph");
        Assert.False(tf.AutoFocus);
        Assert.Same(tf, tf.FocusOnStart());
        Assert.True(tf.AutoFocus);
    }

    [Fact]
    public void FindAutoFocus_LocatesMarkedFieldInTree()
    {
        var marked = Ui.TextField("a").FocusOnStart();
        Element root = Ui.Column(Ui.Text("t"), Ui.TextField("b"), marked);
        Assert.Same(marked, App.FindAutoFocus(root));
        Assert.Null(App.FindAutoFocus(Ui.Column(Ui.Text("t"))));   // 无标记 → null
        Assert.Null(App.FindAutoFocus(Ui.TextField("c")));          // 未标记单元素 → null
    }

    // ---- 波 3B(B8 部分):撤销/重做键消费(Ctrl+Z 撤销 / Ctrl+Y、Ctrl+Shift+Z 重做)----

    [Fact]
    public void Feed_CtrlZUndo_CtrlYRedo_ChainThroughStateStore()
    {
        (TextFieldElement tf, _) = Field();
        // 撤销粒度 = 整次 Apply 调用(整帧 Feed)一步:逐字符 Feed 得逐字符撤销链
        tf.Feed(null, "a".ToCharArray());
        tf.Feed(null, "b".ToCharArray());
        tf.Feed(null, "c".ToCharArray());
        Assert.Equal("abc", tf.CurrentText);
        tf.Feed(new[] { new KeyEventData(Key.Z, true, false, Ctrl: true) }, null);
        Assert.Equal("ab", tf.CurrentText);
        tf.Feed(new[] { new KeyEventData(Key.Z, true, false, Ctrl: true) }, null);
        Assert.Equal("a", tf.CurrentText);
        tf.Feed(new[] { new KeyEventData(Key.Y, true, false, Ctrl: true) }, null);
        Assert.Equal("ab", tf.CurrentText);
        tf.Feed(new[] { new KeyEventData(Key.Z, true, true, Ctrl: true) }, null);   // Ctrl+Shift+Z = 重做
        Assert.Equal("abc", tf.CurrentText);
        App.State = null;
    }

    [Fact]
    public void Feed_AltZ_DoesNotUndo_AltReachesButNotBound()
    {
        // B11 联证:Alt 位随 KeyEventData 到达消费层,但撤销只绑 Ctrl+Z —— Alt/Gui 不触发编辑历史。
        (TextFieldElement tf, _) = Field();
        tf.Feed(null, "hi".ToCharArray());
        tf.Feed(new[] { new KeyEventData(Key.Z, true, false, Alt: true) }, null);
        Assert.Equal("hi", tf.CurrentText);
        tf.Feed(new[] { new KeyEventData(Key.Z, true, false, Gui: true) }, null);
        Assert.Equal("hi", tf.CurrentText);
        App.State = null;
    }

    [Fact]
    public void UndoStack_SurvivesSweepWhileMeasuredFieldLives()
    {
        // 评审波 3B Important#2:撤销栈与文本键同寿命 —— MeasureSelf 每帧触碰 "tf:undo:" 帧戳,
        // 失焦超 GraceFrames 后 Sweep 不清(修复前 undo 键仅 Feed 内触碰,帧戳冻结即被清扫:
        // 文本在、历史亡,失焦约 5 秒后撤销静默失效)。
        // 本测试取无图集强场景(触碰位于图集早退之前的实证):文本键随 Sweep 失,undo 链经度量期
        // 触碰存活 —— 撤销从历史恢复文本;生产(有图集)文本键经 State 读同帧存活,更宽松。
        (TextFieldElement tf, _) = Field();
        tf.Feed(null, "h".ToCharArray());                           // 撤销粒度 = 整次 Apply(逐字符 Feed 构造两步链)
        tf.Feed(null, "i".ToCharArray());
        Assert.Equal("hi", tf.CurrentText);
        try
        {
            for (int frame = 0; frame < StateStore.GraceFrames + 1; frame++)
            {
                App.State!.Tick();                                  // 帧推进
                LayoutEngine.Measure(tf, new SizeF(200, 40));       // 每帧度量该字段(App.Run Arrange 同款)
                App.State.Sweep();
            }
            tf.Feed(new[] { new KeyEventData(Key.Z, true, false, Ctrl: true) }, null);
            Assert.Equal("h", tf.CurrentText);                      // 历史存活:undo 链未被清扫(修复前此处回不到 "h")
        }
        finally { App.State = null; }
    }

    // ---- 波 3B(B8 部分):鼠标定位 + 拖选 ----

    [Fact]
    public void TextField_CapturesDrag_Declared()
    {
        Assert.True(Ui.TextField("ph").CapturesDrag);                       // 拖选捕获声明
        Assert.False(Ui.Button("b", () => { }).CapturesDrag);               // 点击控件不受影响
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void MouseClick_PositionsCursorByGlyphAdvancePrefix()
    {
        // hit-to-offset:按图集字形 advance 前缀定位("he|llo" 边界右侧 1px → 光标 2,锚塌陷)。
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        (TextFieldElement tf, PanelElement root) = Field("hello world");
        try
        {
            var s = new InputSession();
            float x = tf.Frame.X + TextFieldElement.PadX + atlas.Measure("he", 14f).Width + 1f;
            InputState down = Interaction.Update(s, root,
                new RawInput(new PointF(x, tf.Frame.Y + 10), new[] { true }));
            Assert.Equal(tf.ResolvedId, down.CapturedId);                   // 捕获声明生效
            tf.ConsumeInput(in down);                                       // Draw 首行同款消费面直调
            TextState st = tf.State;
            Assert.Equal(2, st.Cursor);
            Assert.True(st.Anchor < 0 || st.Anchor == st.Cursor);           // 无选区(塌陷)
        }
        finally { App.Atlas = null; App.State = null; }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void MouseClick_BeyondTextEnd_ClampsCursorToLength()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        (TextFieldElement tf, PanelElement root) = Field("abc");
        try
        {
            var s = new InputSession();
            InputState down = Interaction.Update(s, root,
                new RawInput(new PointF(tf.Frame.Right - 2f, tf.Frame.Y + 10), new[] { true }));
            tf.ConsumeInput(in down);
            Assert.Equal(3, tf.State.Cursor);
        }
        finally { App.Atlas = null; App.State = null; }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void MouseDrag_SelectsRange_AnchorAtPress_CursorFollows()
    {
        // 拖拽选择:按下帧光标+锚塌陷于 hit;捕获期间 Motion 帧光标跟随、锚留守;松开选区保留。
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        (TextFieldElement tf, PanelElement root) = Field("hello world");
        try
        {
            var s = new InputSession();
            float x1 = tf.Frame.X + TextFieldElement.PadX + atlas.Measure("h", 14f).Width + 1f;
            float x2 = tf.Frame.X + TextFieldElement.PadX + atlas.Measure("hell", 14f).Width + 1f;
            InputState down = Interaction.Update(s, root,
                new RawInput(new PointF(x1, tf.Frame.Y + 10), new[] { true }, LeftHeld: true));
            tf.ConsumeInput(in down);
            Assert.Equal(1, tf.State.Cursor);
            InputState drag = Interaction.Update(s, root,
                new RawInput(new PointF(x2, tf.Frame.Y + 10), Array.Empty<bool>(), LeftHeld: true));
            Assert.Equal(tf.ResolvedId, drag.CapturedId);                   // 捕获跨帧持续
            tf.ConsumeInput(in drag);
            Assert.Equal((1, 3), TextEditing.Selection(tf.State));          // 选区 [1,4)
            InputState up = Interaction.Update(s, root,
                new RawInput(new PointF(x2, tf.Frame.Y + 10), new[] { false }));
            tf.ConsumeInput(in up);                                         // 松开帧:无捕获不消费,选区保留
            Assert.Equal((1, 3), TextEditing.Selection(tf.State));
        }
        finally { App.Atlas = null; App.State = null; }
    }
}
