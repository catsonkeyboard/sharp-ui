using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

public class D3D11ContextTests
{
    static byte[] Pixel(byte[] px, uint x, uint y, uint w = 64) =>
        new[] { px[(y * w + x) * 4], px[(y * w + x) * 4 + 1], px[(y * w + x) * 4 + 2], px[(y * w + x) * 4 + 3] };

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_BeginFrameClearsToColor()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.Present();   // headless no-op
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 4, 4));   // BGRA 读回,同旧断言
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 60, 60));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawWhiteQuad_AttributelessAtlasWhitePixel()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);   // 图集 (0,0) 白
        float inv = 1f / D3D11Context.AtlasSize;
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
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 4, 4));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawRedQuad_LandsInRedChannel()   // 通道映射锁定(Color32.R→红)
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / D3D11Context.AtlasSize;
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
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(px, 32, 32));   // BGRA 存储:纯红 → [B=0,G=0,R=255]
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_ScissorClipsDraw()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / D3D11Context.AtlasSize;
        var verts = new UiVertex[4];
        for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
            new PointF(i % 2 == 0 ? 0f : 64f, i < 2 ? 0f : 64f),
            new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv),
            Color32.White);
        ushort[] idx = { 0, 1, 2, 2, 1, 3 };
        ctx.SetScreenSize(64, 64);
        ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        ctx.SetScissor(0, 0, 32, 64);   // 左半
        ctx.Draw(verts, 4, idx, 6, 0);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 4, 32));
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 40, 32));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_DrawTwoSegments_StartIndexOffset()   // 分段契约:startIndex>0 各段各就各位
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        float inv = 1f / D3D11Context.AtlasSize;
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
        ctx.Draw(verts, 8, idx, 6, 6);   // startIndex>0:错误实现会读到 scratch 陈旧尾段
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 16, 16));   // 段 A 就位
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(px, 48, 48));       // 段 B 就位(BGRA:纯红)
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 48, 16));      // 两段之外仍为清屏色
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Headless_Resize_Throws()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        Assert.Throws<NotSupportedException>(() => ctx.Resize(32, 32));
    }

    [Fact]
    public void AtlasSize_Is1024()
    {
        Assert.Equal(1024u, D3D11Context.AtlasSize);
    }
}
