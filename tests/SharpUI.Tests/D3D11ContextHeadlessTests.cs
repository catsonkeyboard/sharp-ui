using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>M3d-ii T4:旧 GraphicsContextTests 随 GraphicsContext 退役迁移——断言面映射到 D3D11Context
/// (构造即完成 HLSL 编译与全管线对象构建,失败显式抛;MainFramebuffer.Width → TargetWidth)。</summary>
public class D3D11ContextHeadlessTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void HeadlessDevice_CompilesShadersAndBuildsPipeline()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        Assert.Equal(1024u, D3D11Context.AtlasSize);
        Assert.Equal(64u, ctx.TargetWidth);
        Assert.Equal(64u, ctx.TargetHeight);
        Assert.True(ctx.FeatureLevel >= 0xB000, $"feature level 0x{ctx.FeatureLevel:X} < D3D 11.0(sm_5 管线硬门槛)");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void AtlasRegion_UpdateAndReadBack()
    {
        using var ctx = D3D11Context.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 0, 0, 255 }, 0, 0, 1, 1);  // 上传本身不抛即通过,读回断言在渲染链路验证
        byte[] pixels = ctx.ReadBackMainFramebuffer();
        Assert.Equal(64u * 64u * 4u, (uint)pixels.Length);
    }
}
