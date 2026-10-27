using SharpUI;

namespace SharpUI.Tests;

/// <summary>
/// 跨平台里程碑 Task 1:NativeLib 三平台原生库名映射(X-1)纯函数全矩阵自检。
/// 语义依据:Windows vendored 基名(.dll);Linux 发行版包 SONAME(libSDL2-2.0-0 →
/// libSDL2-2.0.so.0、libfreetype6 → libfreetype.so.6、libharfbuzz0b → libharfbuzz.so.0);
/// macOS Homebrew install_name(lib*.dylib,版本号置于 .dylib 前)。零原生依赖,纯逻辑测试。
/// </summary>
public class NativeLibTests
{
    // NativeLib.Platform 为 internal(public 测试方法不可直接作参数,经字符串解析)
    static NativeLib.Platform ParsePlatform(string platform) =>
        (NativeLib.Platform)Enum.Parse(typeof(NativeLib.Platform), platform);

    [Theory]
    [InlineData("Windows", "SDL2", "SDL2.dll")]
    [InlineData("Windows", "freetype", "freetype.dll")]
    [InlineData("Windows", "harfbuzz", "harfbuzz.dll")]
    [InlineData("Linux", "SDL2", "libSDL2-2.0.so.0")]
    [InlineData("Linux", "freetype", "libfreetype.so.6")]
    [InlineData("Linux", "harfbuzz", "libharfbuzz.so.0")]
    [InlineData("Mac", "SDL2", "libSDL2-2.0.dylib")]
    [InlineData("Mac", "freetype", "libfreetype.6.dylib")]
    [InlineData("Mac", "harfbuzz", "libharfbuzz.0.dylib")]
    public void Resolve_KnownBaseNames_MapToPlatformSonames(string platform, string baseName, string expected)
        => Assert.Equal(expected, NativeLib.Resolve(ParsePlatform(platform), baseName));

    [Theory]
    [InlineData("Windows", "zip", "zip.dll")]
    [InlineData("Linux", "zip", "libzip.so")]
    [InlineData("Mac", "objc", "libobjc.dylib")]
    public void Resolve_UnknownBaseNames_UseGenericPlatformConvention(string platform, string baseName, string expected)
        => Assert.Equal(expected, NativeLib.Resolve(ParsePlatform(platform), baseName));

    [Fact]
    public void Resolve_NullBaseName_Throws()
        => Assert.Throws<ArgumentNullException>(() => NativeLib.Resolve(NativeLib.Platform.Linux, null!));

    [Fact]
    public void Current_OnWindowsHost_IsWindows()
    {
        // 测试宿主约定为 Windows(与 FontLibraryTests 同);其余宿主静默跳过(CI 实机另验)
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(NativeLib.Platform.Windows, NativeLib.Current);
    }

    [Fact]
    public void DllImportConstants_EqualWindowsResolution()
    {
        // 零行为差门槛锁死:DllImport 解析键(const)在 Windows 上与 Resolve 结果恒等——
        // 常量化改造(5 处 Lib 常量)不改变 Windows 宿主的库名解析
        Assert.Equal(NativeLib.Resolve(NativeLib.Platform.Windows, "SDL2"), NativeLib.Sdl2);
        Assert.Equal(NativeLib.Resolve(NativeLib.Platform.Windows, "freetype"), NativeLib.FreeType);
        Assert.Equal(NativeLib.Resolve(NativeLib.Platform.Windows, "harfbuzz"), NativeLib.HarfBuzz);
    }
}
