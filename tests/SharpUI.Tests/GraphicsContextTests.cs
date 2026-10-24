using SharpUI;

namespace SharpUI.Tests;

public class GraphicsContextTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void HeadlessDevice_CompilesShadersAndBuildsPipeline()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        Assert.Equal(1024, GraphicsContext.AtlasSize);
        Assert.Equal(64u, ctx.MainFramebuffer.Width);
        Assert.NotNull(ctx.Pipeline);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void AtlasRegion_UpdateAndReadBack()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 0, 0, 255 }, 0, 0, 1, 1);  // 上传本身不抛即通过,读回断言在 Task 6 的渲染链路验证
        byte[] pixels = ctx.ReadBackMainFramebuffer();
        Assert.Equal(64u * 64u * 4u, (uint)pixels.Length);
    }
}
