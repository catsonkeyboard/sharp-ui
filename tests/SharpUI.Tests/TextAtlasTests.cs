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

    // ---- 审计修复波 2A-3(I-7):图集满 Reset 延迟覆写 ----
    // 修复前:Rasterize 分配失败即原地 Reset+清缓存+重分配+UpdateAtlasRegion —— 本帧已 AddQuad 的字形
    // 仍引用旧 UV,新字形光栅化覆写同一纹理区域,当帧提交采样错位(整屏文本一帧花屏,每次图集填满触发一轮)。

    /// <summary>记录 UpdateAtlasRegion 的假上下文:TextAtlas 仅消费 AtlasSize/UpdateAtlasRegion,字形仍经
    /// 真 FT 光栅化(依赖本机字体与 vendored FT/HB,Gpu trait 与 GlyphRasterizerTests 同宿主约定;无 GPU 设备)。
    /// AtlasSize 可任意调小(如 64),几字形即逼出"图集满"。</summary>
    sealed class RecordingContext : IRenderContext
    {
        public uint AtlasSize { get; set; } = 64;
        public uint TargetWidth => 64;
        public uint TargetHeight => 64;
        public readonly List<(uint X, uint Y, uint W, uint H)> Uploads = new();
        public void Resize(uint width, uint height) { }
        public void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint width, uint height) => Uploads.Add((x, y, width, height));
        public void BeginFrame(float r, float g, float b, float a) { }
        public void SetScreenSize(float logicalWidth, float logicalHeight) { }
        public void SetScissor(uint x, uint y, uint w, uint h) { }
        public void Draw(UiVertex[] vertices, int vertexCount, ushort[] indices, uint indexCount, uint startIndex) { }
        public void Present() { }
        public event Action? DeviceLost;   // 审计修复波 2B/A10:接口新增成员,假上下文恒不触发
        public void Dispose() { }
    }

    /// <summary>64 图集 + 40px 唯一 CJK(16 字形需 ~16×41² ≈ 27k px² ≫ 4096)必耗尽:满后新字形当帧跳过
    /// (quad 数 &lt; 字形数),且**帧内任意两次上传区域不相交** —— 修复前 Reset 后重分配从 (0,0) 重走,
    /// 与本帧早期字形区域重叠(覆写),本断言即拦。</summary>
    [Fact]
    [Trait("Category", "Gpu")]
    public void AtlasFull_ExhaustedGlyphsSkipped_NoUploadOverlapWithinFrame()
    {
        var ctx = new RecordingContext { AtlasSize = 64 };
        using var atlas = new TextAtlas(ctx);
        string text = string.Concat(Enumerable.Range(0, 16).Select(i => (char)(0x4E00 + i)));
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, text, new PointF(0, 0), 40f, bold: false, color: Color32.White);
        int quads = mesh.VertexCount / 4;
        Assert.True(quads > 0 && quads < 16, $"quads={quads}(应有产出且满后字形缺席)");
        for (int i = 0; i < ctx.Uploads.Count; i++)
            for (int j = i + 1; j < ctx.Uploads.Count; j++)
            {
                (uint xi, uint yi, uint wi, uint hi) = ctx.Uploads[i];
                (uint xj, uint yj, uint wj, uint hj) = ctx.Uploads[j];
                bool overlap = xi < xj + wj && xj < xi + wi && yi < yj + hj && yj < yi + hi;
                Assert.False(overlap, $"upload#{i} {ctx.Uploads[i]} 与 #{j} {ctx.Uploads[j]} 帧内重叠(I-7 当帧覆写)");
            }
    }

    /// <summary>耗尽后经 BeginFrame(帧首统一失效,此刻上一帧网格已提交)下帧重光栅化自愈;白点槽位由
    /// shelf 首分配重占,WhiteUv 数值恒定(修复前靠 SeedWhitePixel 重播恰好同位的巧合)。</summary>
    [Fact]
    [Trait("Category", "Gpu")]
    public void AtlasFull_DeferredReset_NextFrameRecovers_WhiteUvStable()
    {
        var ctx = new RecordingContext { AtlasSize = 64 };
        using var atlas = new TextAtlas(ctx);
        RectF whiteBefore = atlas.WhiteUv;
        var m1 = new MeshBuilder();
        atlas.DrawRun(m1, "一丁丂七丄丅丆万丈三", new PointF(0, 0), 40f, bold: false, color: Color32.White);   // 必耗尽
        Assert.True(m1.VertexCount / 4 < 8);
        int uploadsFrame1 = ctx.Uploads.Count;

        atlas.BeginFrame();   // 帧首维护(App.Run 每窗帧首调用):Reset 只清 packer/缓存,不触纹理
        Assert.Equal(whiteBefore, atlas.WhiteUv);

        var m2 = new MeshBuilder();
        atlas.DrawRun(m2, "一", new PointF(0, 0), 12f, bold: false, color: Color32.White);   // 小字形,空图集必容纳
        Assert.Equal(1, m2.VertexCount / 4);                          // 下帧全量重光栅化自愈
        Assert.True(ctx.Uploads.Count > uploadsFrame1);              // 重光栅化真实上传
    }

    /// <summary>I-7.3:单字形超图集(宽/高 +1 padding 超边长,空图集也放不下)—— 修复前二次分配失败抛
    /// InvalidOperationException 击穿帧循环;现优雅跳过,且不标记 Reset(避免每帧全量重光栅化颠簸)。</summary>
    [Fact]
    [Trait("Category", "Gpu")]
    public void OversizedGlyph_SkippedGracefully_DoesNotPoisonCache()
    {
        var ctx = new RecordingContext { AtlasSize = 64 };
        using var atlas = new TextAtlas(ctx);
        var m1 = new MeshBuilder();
        atlas.DrawRun(m1, "丁", new PointF(0, 0), 80f, bold: false, color: Color32.White);   // 80px ≫ 64 图集
        Assert.Equal(0, m1.VertexCount / 4);
        var m2 = new MeshBuilder();
        atlas.DrawRun(m2, "丁", new PointF(0, 0), 12f, bold: false, color: Color32.White);   // 无 BeginFrame 也正常
        Assert.Equal(1, m2.VertexCount / 4);
    }
}
