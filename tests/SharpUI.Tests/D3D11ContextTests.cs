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
