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
}
