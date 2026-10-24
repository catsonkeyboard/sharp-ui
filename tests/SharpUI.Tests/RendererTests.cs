using SharpUI;

namespace SharpUI.Tests;

public class RendererTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void RendersWhiteQuadOverClearColor()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        var mesh = new MeshBuilder();
        float inv = 1f / GraphicsContext.AtlasSize;
        mesh.AddQuad(new RectF(16, 16, 32, 32), new RectF(0, 0, inv, inv), Color32.White);

        using var renderer = new Renderer(ctx);
        renderer.Submit(mesh, new SizeF(64, 64), ctx.MainFramebuffer);

        byte[] pixels = ctx.ReadBackMainFramebuffer();
        // B8G8R8A8:中心点应为白色
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(pixels, 32, 32));
        // 角落为清屏色 RgbaFloat(0.07,0.07,0.08,1):R=0.07→18, G=0.07→18, B=0.08→20, 存储 B8G8R8A8 字节序 [B,G,R,A] = [20,18,18,255]
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(pixels, 4, 4));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void EmptyMesh_SubmitsNothing()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var renderer = new Renderer(ctx);
        renderer.Submit(new MeshBuilder(), new SizeF(64, 64), ctx.MainFramebuffer);
        byte[] pixels = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(pixels, 32, 32));
    }

    static byte[] Pixel(byte[] rgba, int x, int y) => rgba.AsSpan((y * 64 + x) * 4, 4).ToArray();
}
