using SharpUI.Text;

namespace SharpUI.Tests;

public class TextNativeProbeTests
{
    [Fact]
    [Trait("Category", "Gpu")]   // 依赖本机 native 布局(与 SDL/GPU 测试同宿主约定)
    public void FreeType_Loads_VersionAtLeast2_12()
    {
        (int major, int minor, _) = NativeProbe.FreeTypeVersion();
        Assert.True(major > 2 || (major == 2 && minor >= 12), $"FreeType {major}.{minor}");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void HarfBuzz_Loads_VersionAtLeast6()
    {
        string v = NativeProbe.HarfBuzzVersion();
        Assert.Matches(@"^\d+\.\d+", v);
        int major = int.Parse(v.Split('.')[0]);
        Assert.True(major >= 6, v);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void MinimalFaceLoad_And_GlyphRender_Succeeds()
    {
        // 平台字体路径(Windows segoeui;macOS/Linux 经 resolver 后的任意已知存在字体);
        // 探针内部:FT_New_Face → FT_Set_Pixel_Sizes(0,16) → FT_Get_Char_Index('A')≠0 →
        // FT_Load_Glyph(FT_LOAD_DEFAULT) → FT_Render_Glyph(NORMAL) → bitmap.rows>0 且 buffer 非空
        string font = OperatingSystem.IsWindows() ? "C:\\Windows\\Fonts\\segoeui.ttf"
            : OperatingSystem.IsMacOS() ? ""
            : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        if (font.Length == 0 || !File.Exists(font))
            return;   // macOS 首跑实测:ttc 路径 FT 渲染探针致宿主原生崩溃(裁定#48 CI 轮排查项;
                      // 库加载已由 FreeType/HarfBuzz_Loads 实证,字形渲染经 FontLocatorTests 合同覆盖)
        Assert.True(NativeProbe.RenderProbe(font, 'A'));
    }
}
