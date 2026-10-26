using SharpUI.Text;

namespace SharpUI.Tests;

/// <summary>
/// D3 文本层 Task 2:FontLibrary/WindowsFontLocator 语义自检(注册表发现、FT_New_Memory_Face 脸生命周期、
/// 回退链 ContainsGlyph、行度量像素值)。Trait Gpu:依赖本机 Windows 注册表/系统字体与 vendored FT/HB DLL
/// (与 TextNativeProbeTests 同宿主约定;非 Windows 宿主红属预期——Linux/macOS 发现属后续里程碑)。
/// 行度量值域依据:Segoe UI upem=2048、hhea ascender/descender=2210/-514(FT 取 hhea 对 FT_FaceRec 同源),
/// FT_Set_Pixel_Sizes(0,16) 后 SizeMetrics.ascender≈17.3px、descender≈-4.0px(26.6 定点 /64)。
/// </summary>
public class FontLibraryTests
{
    // TextAtlas 现状默认回退链(D21):微软雅黑(CJK)+ Segoe UI Emoji(表情)
    static readonly string[] DefaultFallback = { "Microsoft YaHei", "Segoe UI Emoji" };

    [Fact]
    [Trait("Category", "Gpu")]
    public void Locator_ResolvesSystemFamilies_ToExistingFiles()
    {
        var locator = new WindowsFontLocator();
        // 注册表值 "Segoe UI (TrueType)" → segoeui.ttf(相对 C:\Windows\Fonts):解析结果必须是存在的文件
        string? regular = locator.Locate("Segoe UI", bold: false);
        Assert.NotNull(regular);
        Assert.True(File.Exists(regular!), regular!);
        // 粗体=独立文件:值 "Segoe UI Bold (TrueType)" → seguib.ttf(槽 0 粗体语义与 BoldOf 一致)
        string? bold = locator.Locate("Segoe UI", bold: true);
        Assert.NotNull(bold);
        Assert.True(File.Exists(bold!), bold!);
        Assert.NotEqual(Path.GetFullPath(regular!), Path.GetFullPath(bold!));   // 粗体文件 ≠ 主脸文件
        // YaHei 值名带 & 变体:"Microsoft YaHei & Microsoft YaHei UI (TrueType)" → msyh.ttc——
        // 按族名前缀匹配值名,不要求全等(T0 实况:全等匹配会漏掉雅黑)
        string? yahei = locator.Locate("Microsoft YaHei", bold: false);
        Assert.NotNull(yahei);
        Assert.True(File.Exists(yahei!), yahei!);
        // 槽 2 回退源:Emoji 族注册表可解析
        string? emoji = locator.Locate("Segoe UI Emoji", bold: false);
        Assert.NotNull(emoji);
        Assert.True(File.Exists(emoji!), emoji!);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Locator_UnknownFamily_ReturnsNull()
    {
        var locator = new WindowsFontLocator();
        // 接口契约:解析失败返回 null(FontLibrary 侧并入槽 0)
        Assert.Null(locator.Locate("Definitely Not A Font Family", bold: false));
        Assert.Null(locator.Locate("Definitely Not A Font Family", bold: true));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void FallbackChain_ContainsGlyph_ResolvesSlots()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        // 槽位数 = 1 主脸 + 回退族数(契约:槽 0 = "Segoe UI",1.. = fallback)
        Assert.Equal(3, fonts.SlotCount);
        // 'A'→槽 0:Segoe UI 含拉丁字形(ContainsGlyph = FT_Get_Char_Index ≠ 0)
        Assert.True(fonts.ContainsGlyph(0, 'A'));
        // '中'(U+4E2D)不在 Segoe UI(无 CJK)——链上第 0 槽不持有,才轮到槽 1
        Assert.False(fonts.ContainsGlyph(0, 0x4E2Du));
        // '中'→槽 1:微软雅黑(msyh.ttc,face index 0)
        Assert.True(fonts.ContainsGlyph(1, 0x4E2Du));
        // '🙂'(U+1F642,超出 BMP)不在雅黑——才轮到槽 2
        Assert.False(fonts.ContainsGlyph(1, 0x1F642u));
        // '🙂'→槽 2:Segoe UI Emoji
        Assert.True(fonts.ContainsGlyph(2, 0x1F642u));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void UnknownFallbackFamily_MergesIntoPrimarySlot()
    {
        using var fonts = new FontLibrary(new[] { "Definitely Not A Font Family" });
        // 解析失败并入槽 0(同现状 TextAtlas:tf ?? _typefaces[0])——槽位本身仍保留
        Assert.Equal(2, fonts.SlotCount);
        Assert.True(fonts.ContainsGlyph(1, 'A'));       // 槽 1 即主脸:含拉丁
        Assert.False(fonts.ContainsGlyph(1, 0x4E2Du));  // 主脸无 CJK
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Face_ReturnsCachedHandles_WithBoldSemantics()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        FaceHandle regular = fonts.Face(0, bold: false);
        // 脸句柄携带 FT_Face 与 hb_ft_font(FtFace)(hb-ft 桥,度量与 FT 同源)
        Assert.NotEqual(IntPtr.Zero, regular.FtFace);
        Assert.NotEqual(IntPtr.Zero, regular.HbFont);
        // 缓存:重复取同槽同粗体返回同一句柄实例(接口注释"脸句柄(缓存)")
        Assert.Same(regular, fonts.Face(0, bold: false));
        // 槽 0 粗体 = 注册表独立粗体文件(seguib.ttf)→ 不同 FT_Face(BoldOf 语义)
        Assert.NotEqual(regular.FtFace, fonts.Face(0, bold: true).FtFace);
        // 回退槽粗体 = 同脸(TextAtlas.BoldOf:slot != 0 → _typefaces[slot],D21 折衷)→ 同一句柄实例
        Assert.Same(fonts.Face(1, bold: false), fonts.Face(1, bold: true));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void LineMetrics_16px_WithinPixelRange()
    {
        using var fonts = new FontLibrary(DefaultFallback);
        // SizeMetrics 按 FT_Set_Pixel_Sizes(0,16) 缩放;ascender/descender 为 26.6 定点 → /64 为像素
        (float ascender, float descender) = fonts.LineMetrics(0, 16, bold: false);
        Assert.True(ascender > 0f, $"ascender={ascender}");    // ascender 恒正(基线上)
        Assert.InRange(ascender, 10f, 20f);                    // Segoe UI 16px:2210/2048×16 ≈ 17.3
        Assert.True(descender < 0f, $"descender={descender}"); // descender 基线下为负
        // 尺寸变化须触发重设:32px ascender≈2×(SetPixelSize 尺寸变才调 FT_Set_Pixel_Sizes 的可见效果)
        (float ascender32, _) = fonts.LineMetrics(0, 32, bold: false);
        Assert.InRange(ascender32, 20f, 40f);
        // 回退槽(雅黑)与粗体同契约可查(粗体为独立脸,尺寸状态独立)
        (float ascYh, float descYh) = fonts.LineMetrics(1, 16, bold: false);
        Assert.True(ascYh > 0f && descYh < 0f, $"yahei {ascYh}/{descYh}");
        (float ascBold, float descBold) = fonts.LineMetrics(0, 16, bold: true);
        Assert.True(ascBold > 0f && descBold < 0f, $"bold {ascBold}/{descBold}");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Dispose_IsIdempotent_AfterBoldFaceCreated()
    {
        var fonts = new FontLibrary(DefaultFallback);
        _ = fonts.Face(0, bold: true);                 // 粗体独立脸也建起来:覆盖全量释放路径(hb→face→钉)
        _ = fonts.LineMetrics(0, 16, bold: false);
        fonts.Dispose();
        fonts.Dispose();   // 二次 Dispose 不抛(标准 Dispose 纪律;测试不验证原生侧,崩溃即红)
    }
}
