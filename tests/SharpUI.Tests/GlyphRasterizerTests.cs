using SharpUI.Text;

namespace SharpUI.Tests;

/// <summary>
/// D3 文本层 Task 3:GlyphRasterizer(FT_LOAD_DEFAULT + FT_RENDER_MODE_NORMAL → A8 → RGBA 预乘白)
/// 语义自检。Trait Gpu:依赖本机系统字体与 vendored FT DLL(与 FontLibraryTests 同宿主约定)。
/// 值域依据:Segoe UI 16px 大写 'A' hint 后位图约 9-12×11-12px(宽 [8,20]/高 [8,24] 区间留余量);
/// FT_RENDER_MODE_NORMAL 产 FT_PIXEL_MODE_GRAY(8bpp 覆盖,grays 256)——边缘必有 0&lt;cov&lt;255
/// 的抗锯齿中间值、内部必有 cov=255 全覆盖像素。预乘白:RGB=A=cov(直线型 (255,255,255,cov)
/// × alpha 即预乘),全覆盖像素 R=G=B=255 由此成立。度量均为物理像素(T4 消费时 /scale)。
/// </summary>
public class GlyphRasterizerTests
{
    // TextAtlas 现状默认回退链(D21):微软雅黑(CJK)+ Segoe UI Emoji(表情)
    static readonly string[] DefaultFallback = { "Microsoft YaHei", "Segoe UI Emoji" };

    [Fact]
    [Trait("Category", "Gpu")]
    public void Rasterize_A_16px_WhiteAntialiasedBitmap()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var rasterizer = new GlyphRasterizer(fonts);
        RasterGlyph g = rasterizer.Rasterize(0, 'A', 16, bold: false);
        // Segoe UI 16px 大写 A:hint 后位图约 9-12px 宽 / 11-12px 高(cap height),区间留余量
        Assert.InRange(g.Width, 8, 20);
        Assert.InRange(g.Height, 8, 24);
        // RGBA 紧排:W*H*4 字节,通道序 R,G,B,A
        Assert.Equal(g.Width * g.Height * 4, g.Rgba.Length);
        bool hasOpaque = false, hasEdge = false;
        for (int px = 0; px < g.Width * g.Height; px++)
        {
            byte r = g.Rgba[px * 4], gg = g.Rgba[px * 4 + 1], b = g.Rgba[px * 4 + 2], a = g.Rgba[px * 4 + 3];
            // 白字形:通道平衡(预乘白 R=G=B=A=cov;直线型 (255,255,255,cov) 经 ×alpha 预乘即此)
            Assert.Equal(r, gg);
            Assert.Equal(gg, b);
            Assert.Equal(a, r);   // 预乘:RGB 已乘 alpha=白源 255×(a/255)=a
            if (a == 255) hasOpaque = true;
            if (a > 0 && a < 255) hasEdge = true;   // NORMAL 渲染的抗锯齿边缘必有中间覆盖值
        }
        // 位图非全零:内部笔画全覆盖像素存在(且该像素 R=G=B=255——白的不变量)
        Assert.True(hasOpaque, "no fully-covered pixel");
        // 抗锯齿:边缘存在 0<cov<255(16px A 的斜边/圆滑拐角必有灰阶过渡)
        Assert.True(hasEdge, "no antialiased edge pixel");
        // bitmap_top:基线到位图顶(正值,字形主体在基线上方)且不超位图高(下伸部为零的大写)
        Assert.InRange(g.BearingTop, 1f, g.Height);
        // advance 来自 metrics.horiAdvance(26.6/64):16px 大写 advance 约 8-10px,正值
        Assert.InRange(g.Advance, 4f, 20f);
        // 粗体脸路径同契约(segoeuib.ttf 独立脸,尺寸状态独立):亦产出合法位图
        RasterGlyph bold = rasterizer.Rasterize(0, 'A', 16, bold: true);
        Assert.True(bold.Width > 0 && bold.Height > 0, $"bold {bold.Width}x{bold.Height}");
        Assert.Equal(bold.Width * bold.Height * 4, bold.Rgba.Length);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Rasterize_Space_16px_EmptyBitmapWithAdvance()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var rasterizer = new GlyphRasterizer(fonts);
        // 空格=空字形(FT_GLYPH_FORMAT_NONE:无轮廓)——位图 W=H=0、无 RGBA 字节
        RasterGlyph g = rasterizer.Rasterize(0, ' ', 16, bold: false);
        Assert.Equal(0, g.Width);
        Assert.Equal(0, g.Height);
        Assert.Empty(g.Rgba);
        // 推进量仍在:hmtx 的 horiAdvance(26.6/64)≈ 1/4 em ≈ 4px@16,恒正(布局推进不因无位图而消失)
        Assert.True(g.Advance > 0f, $"space advance={g.Advance}");
        Assert.InRange(g.Advance, 2f, 8f);
        // 空字形 bearing 无意义:0
        Assert.Equal(0f, g.BearingTop);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Rasterize_CJK_16px_NonEmptyBitmap()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var rasterizer = new GlyphRasterizer(fonts);
        // '中'(U+4E2D)走槽 1(微软雅黑,ContainsGlyph 链解析):光栅化非空
        RasterGlyph g = rasterizer.Rasterize(1, 0x4E2Du, 16, bold: false);
        Assert.True(g.Width > 0, $"width={g.Width}");
        Assert.True(g.Height > 0, $"height={g.Height}");
        Assert.Equal(g.Width * g.Height * 4, g.Rgba.Length);
        // 位图非全零:汉字笔画必有全覆盖像素
        bool any = false;
        for (int i = 3; i < g.Rgba.Length; i += 4)
            if (g.Rgba[i] != 0) { any = true; break; }
        Assert.True(any, "all-zero alpha");
        // 全角字形 16px:位图占满 em 方格约 16×16,advance=1.0em=16px(hint ±1px)
        Assert.InRange(g.Advance, 15f, 17f);
        Assert.True(g.BearingTop > 0f, $"bearingTop={g.BearingTop}");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Rasterize_PixelSizeChange_ReScalesBitmap()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var rasterizer = new GlyphRasterizer(fonts);
        // 同脸 16→32px:SetPixelSize 尺寸变更生效,位图约 2×(FT_Set_Pixel_Sizes 重新 hint)
        RasterGlyph g16 = rasterizer.Rasterize(0, 'A', 16, bold: false);
        RasterGlyph g32 = rasterizer.Rasterize(0, 'A', 32, bold: false);
        Assert.True(g32.Width > g16.Width, $"{g32.Width} vs {g16.Width}");
        Assert.True(g32.Height > g16.Height, $"{g32.Height} vs {g16.Height}");
        Assert.True(g32.Advance > g16.Advance, $"advance {g32.Advance} vs {g16.Advance}");
        // 再回 16px(尺寸回退路径):与首次一致(确定性 hint,非单调缓存)
        RasterGlyph g16Again = rasterizer.Rasterize(0, 'A', 16, bold: false);
        Assert.Equal((g16.Width, g16.Height), (g16Again.Width, g16Again.Height));
        Assert.Equal(g16.Advance, g16Again.Advance, 3);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Dispose_IsIdempotent_AfterRasterizing()
    {
        var fonts = new FontLibrary(DefaultFallback);
        var rasterizer = new GlyphRasterizer(fonts);
        _ = rasterizer.Rasterize(0, 'A', 16, bold: false);
        rasterizer.Dispose();
        rasterizer.Dispose();   // 二次 Dispose 不抛(原生资源皆 FontLibrary 持有,接口对称)
        fonts.Dispose();
    }
}
