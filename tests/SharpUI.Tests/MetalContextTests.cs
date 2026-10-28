using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>跨平台 T3:Metal 后端 headless 像素验证 —— 镜像 GLContextTests 结构九项
/// (清屏/白四边形/红通道/scissor/残留 scissor 复位/两段/Resize 语义/AtlasSize/**图集全零**,
/// 前七项=简报列集,后两项=GL 镜像补齐[残留 scissor 复位=GL Important#1 同位防护;FreshAtlas=
/// 评审 Important#2,AtlasSize 契约——构造期 4MB replaceRegion 全零,透明零×预乘混合→清屏色,
/// 白四边形测试只传 1 像素触不到该面])。期望值同 GL 形:RGBA8Unorm 读回字节序 R,G,B,A、
/// 行序自顶向下(Metal 纹理原点左上,无 GL 式翻转,ReadBack 产出与 GL 翻转后同向)—— 纯红
/// [255,0,0,255]、清屏 (0.07,0.07,0.08,1)=[18,18,20,255]。
/// **macOS 实机门控**:全部测试首行 `SHARPUI_RUN_MAC != "1"` 即 return —— Windows 全量跑
/// 0ms 空过(编译结构即验证);CI macOS 跑道置 SHARPUI_RUN_MAC=1 实跑(裁定#48 待验清单主体)。
/// 测试主机并行禁用见 AssemblyInfo.cs。</summary>
public class MetalContextTests
{
    /// <summary>macOS 实机门(Windows 空过;CI macOS 置 SHARPUI_RUN_MAC=1)。</summary>
    static bool MacGate => Environment.GetEnvironmentVariable("SHARPUI_RUN_MAC") == "1";

    static byte[] Pixel(byte[] px, uint x, uint y, uint w = 64) =>
        new[] { px[(y * w + x) * 4], px[(y * w + x) * 4 + 1], px[(y * w + x) * 4 + 2], px[(y * w + x) * 4 + 3] };

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_BeginFrameClearsToColor()
    {
        if (!MacGate) return;
        using var ctx = MetalContext.CreateHeadless(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.Present();   // headless no-op(读回路径自会 flush+wait)
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 4, 4));   // RGBA 读回:R=G=0.07*255≈18,B=0.08*255≈20
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 60, 60));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawWhiteQuad_AttributelessAtlasWhitePixel()
    {
        if (!MacGate) return;
        using var ctx = MetalContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);   // 图集 (0,0) 白
        float inv = 1f / ctx.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 16f : 48f, i < 2 ? 16f : 48f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.SetScissor(0, 0, 64, 64);
        ctx.Draw(verts, 4, idx, 6, 0);
        ctx.Present();
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 32, 32));
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 4, 4));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawRedQuad_LandsInRedChannel()   // 通道映射锁定(Color32.R→红;RGBA8Unorm 字节序)
    {
        if (!MacGate) return;
        using var ctx = MetalContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / ctx.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 16f : 48f, i < 2 ? 16f : 48f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            new Color32(255, 0, 0));
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.BeginFrame(0, 0, 0, 1f);
        ctx.SetScissor(0, 0, 64, 64);
        ctx.Draw(verts, 4, idx, 6, 0);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(px, 32, 32));   // RGBA 存储:纯红 → [R=255,G=0,B=0]
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_ScissorClipsDraw()   // 纵向不对称锁方向:MTLScissorRect 原点左上(无 GL 式 Y 翻)
    {
        if (!MacGate) return;
        using var ctx = MetalContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / ctx.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 0f : 64f, i < 2 ? 0f : 64f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.SetScissor(0, 0, 64, 32);   // 上半
        ctx.Draw(verts, 4, idx, 6, 0);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 32, 4));   // 上半可见
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 32, 40));     // 下半被裁(方向错则两断言对调失败)
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_BeginFrame_ResetsScissorForClear()   // 镜像 GL Important#1:清屏不受残留 scissor 作用
    {
        if (!MacGate) return;
        using var ctx = MetalContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / ctx.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 0f : 64f, i < 2 ? 0f : 64f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        // 帧 1:部分剪裁 + 全屏四边形(Metal 每帧新 encoder,scissor 态不跨帧;本测试锁定该语义)
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.SetScissor(16, 16, 32, 32);
        ctx.Draw(verts, 4, idx, 6, 0);
        // 帧 2:loadAction=Clear 覆全目标(pass 装载清屏不受 scissor 作用,Metal 规约)—— 全帧逐一断言新色
        ctx.BeginFrame(0.2f, 0.4f, 0.6f, 1f);
        byte[] px = ctx.ReadBackMainFramebuffer();
        for (int i = 0; i < px.Length; i += 4)
            if (px[i] != 51 || px[i + 1] != 102 || px[i + 2] != 153 || px[i + 3] != 255)
                Assert.Fail($"帧 2 清屏后像素 {i / 4} = [{px[i]},{px[i + 1]},{px[i + 2]},{px[i + 3]}] 非新清屏色");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawTwoSegments_StartIndexOffset()   // 分段契约:startIndex>0 经索引字节偏移就位
    {
        if (!MacGate) return;
        using var ctx = MetalContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / ctx.AtlasSize;
        var verts = new UiVertex[8];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(          // 段 A:左上 (8..24)² 白
            new PointF(i % 2 == 0 ? 8f : 24f, i < 2 ? 8f : 24f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            Color32.White);
        for (int i = 4; i < 8; i++) verts[i] = UiVertex.Create(          // 段 B:右下 (40..56)² 红
            new PointF(i % 2 == 0 ? 40f : 56f, i < 6 ? 40f : 56f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 6 ? 0f : inv),
            new Color32(255, 0, 0));
        ushort[] idx = { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 };   // [0..6) 段 A,[6..12) 段 B
        ctx.SetScreenSize(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.SetScissor(0, 0, 64, 64);
        ctx.Draw(verts, 8, idx, 6, 0);
        ctx.Draw(verts, 8, idx, 6, 6);   // startIndex>0:错误实现会漏传段 B 索引或偏移错位
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 16, 16));   // 段 A 就位(左上)
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(px, 48, 48));       // 段 B 就位(RGBA:纯红,右下)
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 48, 16));      // 两段之外仍为清屏色
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_FreshAtlas_IsAllTransparentZero()   // 评审 Important#2:GL 镜像补齐(AtlasSize 契约)
    {
        if (!MacGate) return;
        // 构造期 4MB replaceRegion 全零(契约)—— 白块放图集 (8,8)-(10,10):屏幕 (0,0) 中心
        // UV=(0.5/64) → 点采样 texel 恰为 8;未写区域(如 (32,32)→texel≈520)透明源 (0,0,0,0)
        // 经预乘混合 (One,OneMinusSourceAlpha) → 清屏色黑。
        using var ctx = MetalContext.CreateHeadless(64, 64);
        ctx.BeginFrame(0, 0, 0, 1f);
        ctx.UpdateAtlasRegion(new byte[]
        {
            255, 255, 255, 255, 255, 255, 255, 255,
            255, 255, 255, 255, 255, 255, 255, 255,
        }, 8, 8, 2, 2);
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 0f : 64f, i < 2 ? 0f : 64f),
            new PointF(i % 2 == 0 ? 0f : 1f, i < 2 ? 0f : 1f),   // UV 覆盖全图集
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.SetScissor(0, 0, 64, 64);
        ctx.Draw(verts, 4, idx, 6, 0);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 0, 0));   // 白块就位(texel 8)
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(px, 32, 32));       // 未写区域=透明零 → 清屏色黑(RGBA)
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_ResizeZero_Throws_AndResizeRecords()   // 属性语义与 GL 镜像(重建 vs 记录之差见实现注)
    {
        if (!MacGate) return;
        using var ctx = MetalContext.CreateHeadless(64, 64);
        Assert.Throws<ArgumentException>(() => ctx.Resize(0, 0));   // 不接受 0(同 D3D11/GL 语义)
        ctx.Resize(32, 48);   // 离屏 texture 按新尺寸重建(无法"长出"像素;Target 属性语义同 GL)
        Assert.Equal(32u, ctx.TargetWidth);
        Assert.Equal(48u, ctx.TargetHeight);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void AtlasSize_Is1024()   // 经 IRenderContext 断言契约(Metal 后端同值实现)
    {
        if (!MacGate) return;
        using IRenderContext ctx = MetalContext.CreateHeadless(64, 64);
        Assert.Equal(1024u, ctx.AtlasSize);
    }
}
