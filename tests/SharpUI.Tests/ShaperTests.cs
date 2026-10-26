using SharpUI.Text;

namespace SharpUI.Tests;

/// <summary>
/// D3 文本层 Task 3:Shaper(hb utf16 shaping → 托管快照)语义自检。Trait Gpu:依赖本机系统字体
/// (Segoe UI/微软雅黑)与 vendored hb/FT DLL(与 FontLibraryTests 同宿主约定)。
/// 值域依据:Segoe UI 16px 大写 advance ≈ 8-10px(upem=2048,大写字宽 ~1100-1300 units);
/// "AV" 为经典 GPOS kerning 对(hb 默认开启 kern 特性)——总宽 &lt; 两字 advance 之和但 &gt; 单字宽
/// (kerning 只收紧字距,首字 advance 恒计入);微软雅黑 CJK 全角 advance = 1.0em = 16.0px@16。
/// 坐标契约:快照为逻辑像素 = 26.6/64(物理 px)/scale;hb cluster = UTF-16 码元下标
/// (hb_buffer_add_utf16 语义——旧 SkiaSharp 路径为 UTF-8 字节偏移,'中' 计 3 字节)。
/// </summary>
public class ShaperTests
{
    // TextAtlas 现状默认回退链(D21):微软雅黑(CJK)+ Segoe UI Emoji(表情)
    static readonly string[] DefaultFallback = { "Microsoft YaHei", "Segoe UI Emoji" };

    [Fact]
    [Trait("Category", "Gpu")]
    public void Shape_AV_TotalWidthWithinKernedRange()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var shaper = new Shaper(fonts);
        // 基准:单字 "A" @16px 的逻辑宽(scale=1 → 逻辑=物理)
        ShapedRun a = shaper.Shape(0, "A", 16, bold: false, scale: 1f);
        ShapedRun av = shaper.Shape(0, "AV", 16, bold: false, scale: 1f);
        // "AV" shape 出 2 个字形(拉丁 LTR 无合并/重排)
        Assert.Equal(2, av.GlyphIds.Length);
        // 两个字形都在 cmap 命中(非 .notdef,glyph id ≠ 0)
        Assert.All(av.GlyphIds, id => Assert.NotEqual(0u, id));
        // 区间断言:总宽 ∈ [1.0×, 2.0×] 单字宽——下界:kerning 不消灭次字 advance(V advance>0 恒计入);
        // 上界:GPOS kern 对 AV 收紧 + V advance ≈ A advance → 总宽 &lt; 2×A(经典 kerning 对,Segoe UI 开启 kern)
        Assert.InRange(av.TotalWidth, 1.0f * a.TotalWidth, 2.0f * a.TotalWidth);
        // 字形原点 x 递增:首字形原点=笔位 0+x_offset('A' 无偏移=0),次字形原点=首字 advance>0
        Assert.Equal(0f, av.Points[0].X, 3);
        Assert.True(av.Points[1].X > 0f, $"second origin {av.Points[1].X}");
        // 拉丁基础字母无 y 偏移:Points[i].Y = -y_offset/64/scale = 0(hb y 向上、UI y 向下的符号约定)
        Assert.Equal(0f, av.Points[0].Y, 3);
        Assert.Equal(0f, av.Points[1].Y, 3);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Shape_CJK_EveryGlyphAdvanceEqual_FullWidth()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var shaper = new Shaper(fonts);
        // 雅黑(槽 1)CJK 表意文字全角:每字 advance = 1.0em(16.0px@16)——互等
        ShapedRun zh = shaper.Shape(1, "中文字形", 16, bold: false, scale: 1f);
        Assert.Equal(4, zh.Advances.Length);
        for (int i = 1; i < zh.Advances.Length; i++)
            Assert.Equal(zh.Advances[0], zh.Advances[i], 3);   // 全角等宽(容 26.6→float 舍入噪声)
        // 全角 advance ≈ em=16px(区间锚:15-17,容忍 hinting ±1px)
        Assert.InRange(zh.Advances[0], 15f, 17f);
        // 总宽 = Σ advance(快照自洽:不靠另测 advance 推算)
        Assert.Equal(4 * zh.Advances[0], zh.TotalWidth, 2);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Shape_Clusters_AreUtf16CodeUnitIndices()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var shaper = new Shaper(fonts);
        // 雅黑同含 '中' 与 'a'(run 不跨槽):shape "中a" → cluster {0,1}
        ShapedRun r = shaper.Shape(1, "中a", 16, bold: false, scale: 1f);
        Assert.Equal(2, r.Clusters.Length);
        // UTF-16 码元下标:'中'=1 码元 → cluster 0,'a' → cluster 1。
        // 若误用 UTF-8 字节偏移(旧 SkiaSharp/HB add_utf8 语义),'a' 应为 3('中' UTF-8 占 3 字节)
        Assert.Equal(0u, r.Clusters[0]);
        Assert.Equal(1u, r.Clusters[1]);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Shape_BoldA_AtLeastRegularWidth()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var shaper = new Shaper(fonts);
        // 槽 0 粗体=独立粗体文件(segoeuib.ttf,FontLibrary BoldOf 语义):粗体字形宽 ≥ 常规
        // (Segoe UI Bold 大写 advance 略大于常规,同字号同 scale)
        ShapedRun regular = shaper.Shape(0, "A", 16, bold: false, scale: 1f);
        ShapedRun bold = shaper.Shape(0, "A", 16, bold: true, scale: 1f);
        Assert.True(bold.TotalWidth >= regular.TotalWidth,
            $"bold={bold.TotalWidth} regular={regular.TotalWidth}");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Shape_ScaleHalfsLogicalWidth()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var shaper = new Shaper(fonts);
        // 逻辑=物理/scale:同 pixelSize(=物理 px)下 scale=2 的逻辑总宽为 scale=1 的一半
        // (快照语义同现状 TextAtlas:Points/Width 物理/_scale)
        ShapedRun logical1 = shaper.Shape(0, "AV", 16, bold: false, scale: 1f);
        ShapedRun logical2 = shaper.Shape(0, "AV", 16, bold: false, scale: 2f);
        Assert.Equal(logical1.TotalWidth / 2f, logical2.TotalWidth, 3);
        Assert.Equal(logical1.Points[1].X / 2f, logical2.Points[1].X, 3);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Shape_EmptyRun_ReturnsEmptySnapshot()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        using var shaper = new Shaper(fonts);
        // 空串契约(UI 常态:T4 Measure 对 "" 早退,但 Shape 不得崩):0 字形、总宽 0
        ShapedRun empty = shaper.Shape(0, "", 16, bold: false, scale: 1f);
        Assert.Empty(empty.GlyphIds);
        Assert.Empty(empty.Points);
        Assert.Empty(empty.Clusters);
        Assert.Empty(empty.Advances);
        Assert.Equal(0f, empty.TotalWidth);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Dispose_IsIdempotent_AfterShaping()
    {
        var fonts = new FontLibrary(DefaultFallback);
        var shaper = new Shaper(fonts);
        _ = shaper.Shape(0, "AV", 16, bold: false, scale: 1f);
        shaper.Dispose();
        shaper.Dispose();   // 二次 Dispose 不抛不崩(标准 Dispose 纪律;原生侧二次释放即崩溃,由守卫保证)
        fonts.Dispose();
    }
}
