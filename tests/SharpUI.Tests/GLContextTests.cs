using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>GL 后端像素验证(M3e T3):镜像 D3D11ContextTests 七项 + 图集零初始 + core profile 判别。
/// 期望值按 GL 语义换算:glReadPixels 字节序 RGBA(D3D11 为 BGRA)——纯红 [255,0,0,255] 表红、
/// 清屏 (0.07,0.07,0.08,1) 为 [18,18,20,255](D3D11 同色 BGRA [20,18,18,255]);行序已由
/// ReadBackMainFramebuffer 翻转到左上原点,Pixel helper 与 D3D11 同款同向。
/// 测试主机并行禁用见 AssemblyInfo.cs(T4 实证:GPU 类并行 0xC0000005 崩主机)。</summary>
public class GLContextTests
{
    static byte[] Pixel(byte[] px, uint x, uint y, uint w = 64) =>
        new[] { px[(y * w + x) * 4], px[(y * w + x) * 4 + 1], px[(y * w + x) * 4 + 2], px[(y * w + x) * 4 + 3] };

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_BeginFrameClearsToColor()
    {
        using var ctx = GLContext.CreateHeadless(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.Present();   // headless no-op(不交换,后备缓冲供读回)
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 4, 4));   // RGBA 读回:R=G=0.07*255≈18,B=0.08*255≈20
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 60, 60));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawWhiteQuad_AttributelessAtlasWhitePixel()
    {
        using var ctx = GLContext.CreateHeadless(64, 64);
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
    public void Headless_DrawRedQuad_LandsInRedChannel()   // 通道映射锁定(Color32.R→红;GL RGBA 字节序)
    {
        using var ctx = GLContext.CreateHeadless(64, 64);
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
    public void Headless_ScissorClipsDraw()
    {
        using var ctx = GLContext.CreateHeadless(64, 64);
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
        ctx.SetScissor(0, 0, 64, 32);   // 上半(评审 Minor#1:纵向不对称方锁 GL 特有 Y 翻转方向,全高矩形漏检)
        ctx.Draw(verts, 4, idx, 6, 0);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 32, 4));   // 上半可见
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(px, 32, 40));     // 下半被裁(Y 翻转错则两断言对调失败)
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_BeginFrame_ResetsScissorForClear()   // 评审 Important#1:glClear 受残留 scissor 作用
    {
        using var ctx = GLContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / ctx.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 0f : 64f, i < 2 ? 0f : 64f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        // 帧 1:部分剪裁 + 全屏四边形 —— 帧末 SCISSOR_TEST 残留于 (16,16,32,32)
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.SetScissor(16, 16, 32, 32);
        ctx.Draw(verts, 4, idx, 6, 0);
        // 帧 2:不同清屏色(0.2,0.4,0.6 → [51,102,153] 精确无舍入)—— 若 Clear 被残留 scissor 作用,
        // 仅中心 32×32 为新色,其余为帧 1 残留(旧清屏色/白);全帧逐一断言新清屏色。
        ctx.BeginFrame(0.2f, 0.4f, 0.6f, 1f);
        byte[] px = ctx.ReadBackMainFramebuffer();
        for (int i = 0; i < px.Length; i += 4)
            if (px[i] != 51 || px[i + 1] != 102 || px[i + 2] != 153 || px[i + 3] != 255)
                Assert.Fail($"帧 2 清屏后像素 {i / 4} = [{px[i]},{px[i + 1]},{px[i + 2]},{px[i + 3]}] 非新清屏色(残留 scissor 裁了 glClear)");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawTwoSegments_StartIndexOffset()   // 分段契约:startIndex>0 各段各就各位(DrawElements 原生偏移路径)
    {
        using var ctx = GLContext.CreateHeadless(64, 64);
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
    public void Headless_FreshAtlas_IsAllTransparentZero()
    {
        // M3e 契约:图集构造期显式上传全零(字形 padding 依赖透明零;TexImage2D null 像素
        // 在 GL 语义下初始内容未定义,不得依赖驱动隐式清零)。防御性:仅当驱动残留旧数据
        // 且零初始化缺失时才失败。白块放图集 (8,8)-(10,10):屏幕 (0,0) 中心 UV=(0.5/64)
        // →点采样 texel 恰为 8;未写区域(如 (32,32)→texel 520)透明源 (0,0,0,0) 经预乘
        // 混合 (One,OneMinusSrcAlpha) → 清屏色。
        using var ctx = GLContext.CreateHeadless(64, 64);
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
    public void Headless_ResizeZero_Throws_AndResizeRecords()
    {
        using var ctx = GLContext.CreateHeadless(64, 64);
        Assert.Throws<ArgumentException>(() => ctx.Resize(0, 0));   // 不接受 0(同 D3D11 语义)
        ctx.Resize(32, 48);   // GL 窗口由 SDL 管理:仅记录尺寸,viewport 随 BeginFrame 刷新(headless 不抛)
        Assert.Equal(32u, ctx.TargetWidth);
        Assert.Equal(48u, ctx.TargetHeight);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void AtlasSize_Is1024()   // M3e:经 IRenderContext 断言契约(GL 后端同值实现)
    {
        using IRenderContext ctx = GLContext.CreateHeadless(64, 64);
        Assert.Equal(1024u, ctx.AtlasSize);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_GlVersion_33CoreOrHigher_NotCompatibility()   // T2 评审 Minor#2:兼容上下文不得蒙混
    {
        using var ctx = GLContext.CreateHeadless(64, 64);   // 构造期 MakeCurrent,本线程可直接查询
        string ver = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(GL.GetString(GL.Version))!;
        string[] nums = ver.Split(' ')[0].Split('.');
        Assert.True(nums.Length >= 2 && int.TryParse(nums[0], out int major) && int.TryParse(nums[1], out int minor)
            && (major > 3 || (major == 3 && minor >= 3)), $"GL 版本不足 3.3:{ver}");
        Assert.False(ver.Contains("Compatibility"),
            $"上下文非 core profile(属性未达窗口属主模块时 SDL 回落兼容上下文):{ver}");
    }
}
