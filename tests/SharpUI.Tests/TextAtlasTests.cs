using SharpUI;
using SharpUI.Backends;
using SharpUI.Text;

namespace SharpUI.Tests;

public class TextAtlasTests
{
    // ---- 精确数值布局平价(T4 评审 Important #2b)----
    // 金样本三闸(包围盒 ±1px / 均值 / max)对"系统性 ≤1px 平移"存在检出缺口:平移若同时压窄
    // 包围盒差并摊薄均值,三闸皆可穿。本组单测以写死的精确期望值(当前实现实测,与 v2 Skia 侧
    // 逐值互证:InkLineMetrics = Skia MeasureText("Ág") bounds 表;Measure 宽 = SKShaper 整行宽
    // 238.320 vs 238.3125,差 <0.01px)接管该缺口——Shaper/Rasterizer 既有单测为区间断言,兜不住。

    /// <summary>行度量精确平价:FT hinted Á/g 墨迹 = v2 Skia "Ág" 表逐值(12/18/20/24 + bold20;
    /// 度量等值机制见 GlyphRasterizer.InkLineMetrics 注释:同一 TT hinting 字节码,非同引擎)。</summary>
    [Fact]
    [Trait("Category", "Gpu")]
    public void LineMetrics_PreciseParityWithSkiaEraValues()
    {
        using var fonts = new FontLibrary(new[] { "Microsoft YaHei", "Segoe UI Emoji" });
        using var raster = new GlyphRasterizer(fonts);
        (float ascent, float descent)[] expected =
        { (12f, 3f), (17f, 4f), (18f, 5f), (22f, 6f), (19f, 5f) };   // @12/18/20/24 reg + 20 bold
        (int px, bool bold)[] points = { (12, false), (18, false), (20, false), (24, false), (20, true) };
        for (int i = 0; i < points.Length; i++)
        {
            (float a, float d) = raster.InkLineMetrics(0, points[i].px, points[i].bold);
            Assert.Equal(expected[i].ascent, a);        // 整值:hint 网格拟合,精确断言
            Assert.Equal(expected[i].descent, d);
        }
    }

    /// <summary>Measure 总宽/行高精确平价(逻辑 px,scale=1;期望值当前实现实测写死——advances 为
    /// 26.6/64 二进分数,确定性好;precision 2 容忍浮点求和次序微扰,拦 ≥0.01px 级系统平移)。</summary>
    [Theory]
    [Trait("Category", "Gpu")]
    [InlineData(20f, false, 238.3125f, 23f)]    // "The quick brown fox jumps" @20(SKShaper 238.320)
    [InlineData(18f, false, 214.5f, 21f)]       // @18(= 18/20 × 20px 宽的 26.6 缩放)
    [InlineData(12f, false, 143f, 15f)]         // @12
    [InlineData(20f, true, 131.546875f, 24f)]   // "Bold text 123 " @20 bold(SKShaper 131.562)
    public void Measure_PreciseWidthParityWithSkiaEraValues(float size, bool bold, float width, float height)
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        SizeF m = bold
            ? atlas.Measure("Bold text 123 ", size, bold)
            : atlas.Measure("The quick brown fox jumps", size, bold);
        Assert.Equal(width, m.Width, 2);
        Assert.Equal(height, m.Height, 2);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void WhiteUv_PointsIntoFirstPixel()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        Assert.Equal(0f, atlas.WhiteUv.X, 5);
        Assert.Equal(0f, atlas.WhiteUv.Y, 5);
        Assert.True(atlas.WhiteUv.Width > 0f && atlas.WhiteUv.Height > 0f);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Measure_NonEmptyLatinText()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
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
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "Hi!", new PointF(0, 0), 16f, bold: false, Color32.White);
        Assert.Equal(3, mesh.VertexCount / 4);   // 3 个可见字形
        Assert.Equal(18, mesh.IndexCount);
    }

    /// <summary>审计修复波 I-2:DrawRun("") 空串守卫 —— 修复前 SplitRuns 首行 CodePoint(text,0) 对空串
    /// 抛 ArgumentOutOfRangeException(空 Label 的 MenuItem 打开菜单即崩,异常冒穿 App.Run)。
    /// 与 Measure 的空串提前返回对称。</summary>
    [Fact]
    [Trait("Category", "Gpu")]
    public void DrawRun_EmptyText_NoThrow_NoVertices()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "", new PointF(0, 0), 16f, bold: false, Color32.White);
        Assert.Equal(0, mesh.VertexCount);
        Assert.Equal(SizeF.Zero, atlas.Measure("", 16f));   // Measure 对称既有语义
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void SameGlyphTwice_DoesNotReallocate()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
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
        using var ctx1 = D3D11Context.CreateHeadless(64, 64);
        using var atlas1 = new TextAtlas(ctx1, 1f);
        using var ctx2 = D3D11Context.CreateHeadless(64, 64);
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
        using var ctx = D3D11Context.CreateHeadless(64, 64);
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
        using var ctx = D3D11Context.CreateHeadless(64, 64);
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
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        float shaped = atlas.Measure("AV", 32f).Width;
        float naive = atlas.Measure("A", 32f).Width + atlas.Measure("V", 32f).Width;
        Assert.True(shaped < naive - 0.01f, $"shaped={shaped} naive={naive}");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void MixedScript_SplitsRunsCorrectly()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "a你b😀c", new PointF(0, 0), 16f, false, Color32.White);
        Assert.Equal(5, mesh.VertexCount / 4);      // 5 个可见字形,各归各槽
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void ShapedCache_ReuseYieldsIdenticalLayout()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
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
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        for (int i = 0; i < 600; i++)
            atlas.Measure($"unique-{i}", 12f);      // 超容量全清,不抛不漏
        Assert.True(atlas.Measure("unique-0", 12f).Width > 0f);
    }
}
