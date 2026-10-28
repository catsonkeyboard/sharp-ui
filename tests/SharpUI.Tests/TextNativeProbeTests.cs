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
            : OperatingSystem.IsMacOS() ? "/System/Library/Fonts/Helvetica.ttc"
            : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        if (!File.Exists(font))
            return;   // 该发行版无对应字体文件(实机字形渲染已由 FontLocatorTests 合同与金样本覆盖)
        Assert.True(NativeProbe.RenderProbe(font, 'A'));
    }
}
