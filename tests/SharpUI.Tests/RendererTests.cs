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

    [Fact]
    [Trait("Category", "Gpu")]
    public void ClipRuns_DrawWithPerRunScissor()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        var mesh = new MeshBuilder();
        float inv = 1f / GraphicsContext.AtlasSize;
        var uv = new RectF(0, 0, inv, inv);
        mesh.AddQuad(new RectF(0, 0, 32, 64), uv, Color32.White);        // 全屏剪裁(默认)
        mesh.SetClip(new RectF(0, 0, 16, 64));                            // 只露出左半
        mesh.AddQuad(new RectF(32, 0, 32, 64), uv, Color32.White);       // 右半区域,被剪
        mesh.SetClip(new RectF(0, 0, float.MaxValue, float.MaxValue));
        using var renderer = new Renderer(ctx);
        renderer.Submit(mesh, new SizeF(64, 64), ctx.MainFramebuffer);
        byte[] px = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(px, 4, 32));    // 左区可见
        Assert.Equal(new byte[] { 20, 18, 18, 255 }, Pixel(px, 40, 32));      // 右区被剪→清屏色(RGB 18,18,20→BGR 存储 [B,G,R,A])
    }

    static byte[] Pixel(byte[] rgba, int x, int y) => rgba.AsSpan((y * 64 + x) * 4, 4).ToArray();
}
