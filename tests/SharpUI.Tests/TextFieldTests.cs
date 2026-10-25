using SharpUI;
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
        tf.Feed(new[] { new KeyEventData(Veldrid.Key.BackSpace, true, false) }, null);
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
}
