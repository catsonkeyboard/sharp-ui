using SharpUI;

namespace SharpUI.Tests;

public class UiDslTests
{
    [Fact]
    public void String_ImplicitlyConvertsToTextElement()
    {
        Element e = "hello";
        Assert.IsType<TextElement>(e);
        Assert.Equal("hello", ((TextElement)e).Text);
    }

    [Fact]
    public void Column_FiltersNullsAndSetsAxis()
    {
        PanelElement column = Ui.Column("a", null, Ui.Text("b"));
        Assert.Equal(Axis.Vertical, column.Style.Axis);
        Assert.Equal(2, column.Children!.Count);
        PanelElement row = Ui.Row("a", "b");
        Assert.Equal(Axis.Horizontal, row.Style.Axis);
    }

    [Fact]
    public void ModifierChain_SetsStyleAndVisualFields()
    {
        TextElement t = Ui.Text("x").Size(28).Bold().Color(0xE8E8EC);
        Assert.Equal(28f, t.FontSize);
        Assert.True(t.IsBold);
        Assert.Equal(0xE8, t.Color.R);

        PanelElement p = Ui.Panel().Fill(0x0C63E4).Radius(6f).Center().Gap(4f).Padding(24f);
        Assert.Equal(0x0C, p.Background.R);
        Assert.Equal(6f, p.CornerRadius);
        Assert.Equal(MainAxisAlignment.Center, p.Style.MainAlign);
        Assert.Equal(4f, p.Style.Gap);
        Assert.Equal(new Thickness(24f), p.Style.Padding);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Measure_WiresTextThroughAtlas()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        TextElement t = Ui.Text("Hello").Size(20);
        SizeF m = LayoutEngine.Measure(t, new SizeF(200, 100));
        Assert.True(m.Width > 10f);
        App.Atlas = null;   // 不影响后续测试
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void PanelDraw_ProducesRoundedRectMesh()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        PanelElement panel = Ui.Panel().Fill(Color32.White).Radius(8f);
        LayoutEngine.Arrange(panel, new RectF(0, 0, 100, 50));
        var mesh = new MeshBuilder();
        panel.Draw(new PaintContext(mesh, atlas, InputState.Empty));
        Assert.Equal(108, mesh.VertexCount);   // 与 TessellationTests 计数一致
    }

    [Fact]
    public void Button_FactoryCreatesWiredButton()
    {
        int clicks = 0;
        ButtonElement b = Ui.Button("OK", () => clicks++);
        Assert.Equal("OK", ((TextElement)b.Children![0]).Text);
        b.InvokeClick();
        Assert.Equal(1, clicks);
    }

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
}
