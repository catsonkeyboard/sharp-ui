using SharpUI;

namespace SharpUI.Tests;

public class TextAtlasTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void WhiteUv_PointsIntoFirstPixel()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        Assert.Equal(0f, atlas.WhiteUv.X, 5);
        Assert.Equal(0f, atlas.WhiteUv.Y, 5);
        Assert.True(atlas.WhiteUv.Width > 0f && atlas.WhiteUv.Height > 0f);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Measure_NonEmptyLatinText()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        SizeF m = atlas.Measure("Hello", 16f);
        Assert.True(m.Width > 10f, $"width={m.Width}");
        Assert.True(m.Height > 10f, $"height={m.Height}");
        Assert.True(atlas.Measure("Hello", 16f).Width < atlas.Measure("Hello World", 16f).Width);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void DrawRun_AddsOneQuadPerVisibleGlyph()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "Hi!", new PointF(0, 0), 16f, bold: false, Color32.White);
        Assert.Equal(3, mesh.VertexCount / 4);   // 3 个可见字形
        Assert.Equal(18, mesh.IndexCount);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void SameGlyphTwice_DoesNotReallocate()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "AA", new PointF(0, 0), 16f, bold: false, Color32.White);
        atlas.DrawRun(mesh, "AA", new PointF(0, 20), 16f, bold: false, Color32.White);
        // 重复调用不抛错、两次四边形数一致(缓存命中)
        Assert.Equal(4, mesh.VertexCount / 4);
    }
}
