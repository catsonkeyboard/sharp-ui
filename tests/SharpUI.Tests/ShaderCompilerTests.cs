using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

public class ShaderCompilerTests
{
    [Fact]  // 纯 CPU(d3dcompiler_47),无 GPU 依赖
    public void Compile_HlslVertexAndFragment_SucceedWithNonEmptyBytecode()
    {
        if (!OperatingSystem.IsWindows()) return;   // d3dcompiler_47.dll 为 Windows 系统库,非 Windows 跳过(D3D11 后端平台绑定)
        (byte[] vs, string? vsErr) = ShaderCompiler.Compile(Shaders.HlslVertexSource, "VSMain", "vs_5_0");
        Assert.Null(vsErr);
        Assert.True(vs.Length > 0);

        (byte[] ps, string? psErr) = ShaderCompiler.Compile(Shaders.HlslFragmentSource, "PSMain", "ps_5_0");
        Assert.Null(psErr);
        Assert.True(ps.Length > 0);
    }

    [Fact]
    public void Compile_SyntaxError_ReturnsErrorMessage()
    {
        if (!OperatingSystem.IsWindows()) return;   // d3dcompiler_47.dll 为 Windows 系统库,非 Windows 跳过(D3D11 后端平台绑定)
        (byte[] code, string? err) = ShaderCompiler.Compile("float4 garbage(", "VSMain", "vs_5_0");
        Assert.Empty(code);
        Assert.NotNull(err);
        Assert.Contains("error", err, StringComparison.OrdinalIgnoreCase);
    }
}
