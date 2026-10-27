using SharpUI.Text;

namespace SharpUI.Tests;

/// <summary>
/// 跨平台里程碑 Task 1:Linux/macOS 字体发现器(X-2)纯逻辑自检。三层拆分使文件系统逻辑
/// 可在 Windows 宿主全测:① 目录扫描(roots 注入 + 临时目录伪造字体树:递归/扩展名过滤/
/// 空目录/不存在根);② 族名匹配(IFontFamilyProbe 假实现:直名优先/样式标志粗体/
/// BoldOf 主脸回退/大小写不敏感);③ FT 真实现小合同(Gpu trait,Windows 真系统字体文件)。
/// Linux/macOS 目录语义(默认 roots/权限/真实字体树)待 CI 实机验证(类头标注)。
/// </summary>
public class FontLocatorTests
{
    /// <summary>假族名探测器:路径→(family, isBold) 固定映射;未注册路径探测失败;记录探测轨迹。</summary>
    sealed class FakeProbe : IFontFamilyProbe
    {
        public Dictionary<string, (string Family, bool IsBold)> Map { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ProbedPaths { get; } = new();

        public bool TryGetFamily(string path, bool wantBold, out string family, out bool isBold)
        {
            ProbedPaths.Add(path);
            if (Map.TryGetValue(path, out (string Family, bool IsBold) entry))
            {
                family = entry.Family;
                isBold = entry.IsBold;
                return true;
            }
            family = "";
            isBold = false;
            return false;
        }
    }

    static string TempRoot()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sharpui-fontloc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>伪造字体文件(内容非字体:匹配逻辑经假 Probe,仅 FT 合同测试用真文件)。</summary>
    static string WriteFont(string dir, string name)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03 });
        return path;
    }

    // ---- ① 目录扫描(roots 注入,纯文件系统逻辑) ----

    [Fact]
    public void Scan_CollectsFontFiles_Recursively_WithCaseInsensitiveExtensionFilter()
    {
        string root = TempRoot();
        string nested = Path.Combine(root, "sub", "deep");
        Directory.CreateDirectory(nested);
        string ttf = WriteFont(nested, "a.ttf");
        string upper = WriteFont(root, "b.TTF");
        WriteFont(root, "ignore.txt");
        WriteFont(root, "readme.md");
        var probe = new FakeProbe
        {
            Map = { [ttf] = ("Noto Sans", false), [upper] = ("Other Family", false) }
        };
        var locator = new LinuxFontLocator(new[] { root }, probe);
        // 递归:嵌套目录字体可达
        Assert.Equal(ttf, locator.Locate("Noto Sans", bold: false));
        // 扩展名过滤大小写不敏感:.TTF 亦入扫描集
        Assert.Equal(upper, locator.Locate("Other Family", bold: false));
        // 非字体扩展未进入探测(扫描层已过滤)
        Assert.DoesNotContain(probe.ProbedPaths, p => !p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_EmptyRoot_LocateReturnsNull()
    {
        string root = TempRoot();
        var locator = new LinuxFontLocator(new[] { root }, new FakeProbe());
        Assert.Null(locator.Locate("Noto Sans", bold: false));
    }

    [Fact]
    public void Scan_MissingRoot_DoesNotThrow_LocateReturnsNull()
    {
        string missing = Path.Combine(TempRoot(), "does-not-exist");
        var locator = new LinuxFontLocator(new[] { missing }, new FakeProbe());
        Assert.Null(locator.Locate("Noto Sans", bold: false));
        Assert.Null(locator.Locate("Noto Sans", bold: true));
    }

    [Fact]
    public void RootsInjection_MultipleRoots_AllScanned()
    {
        string root1 = TempRoot();
        string root2 = TempRoot();
        string inRoot2 = WriteFont(root2, "c.ttf");
        var probe = new FakeProbe { Map = { [inRoot2] = ("DejaVu Sans", false) } };
        var locator = new LinuxFontLocator(new[] { root1, root2 }, probe);
        Assert.Equal(inRoot2, locator.Locate("DejaVu Sans", bold: false));
    }

    [Fact]
    public void LinuxLocator_DefaultRoots_AreFontconfigConventions()
    {
        // fontconfig 出厂模板系统目录(/usr/share + /usr/local/share)+ XDG 用户 ~/.local/share/fonts + 传统 ~/.fonts
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locator = new LinuxFontLocator();
        Assert.Equal(
            new[] { "/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(home, ".local", "share", "fonts"), Path.Combine(home, ".fonts") },
            locator.Roots);
    }

    [Fact]
    public void MacLocator_DefaultRoots_AreSystemFontDirectories()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locator = new MacFontLocator();
        Assert.Equal(
            new[] { "/System/Library/Fonts", "/Library/Fonts", Path.Combine(home, "Library", "Fonts") },
            locator.Roots);
    }

    // ---- ② 族名匹配(假 Probe:语义与 WindowsFontLocator 三级匹配对齐) ----

    [Fact]
    public void FamilyMatch_IsCaseInsensitive()
    {
        string root = TempRoot();
        string a = WriteFont(root, "a.ttf");
        var probe = new FakeProbe { Map = { [a] = ("Noto Sans", false) } };
        var locator = new LinuxFontLocator(new[] { root }, probe);
        Assert.Equal(a, locator.Locate("noto sans", bold: false));
        Assert.Equal(a, locator.Locate("NOTO SANS", bold: false));
    }

    [Fact]
    public void BoldQuery_PrefersDirectBoldFamilyName_OverStyleFlagFace()
    {
        // 文件按路径序扫描:a.ttf(样式标志粗体)在前、b.ttf("family Bold" 直名)在后——
        // 直名匹配优先级必须压过扫描顺序(先记录样式命中、直名即时返回)
        string root = TempRoot();
        string a = WriteFont(root, "a.ttf");   // family "Noto Sans" + style Bold
        string b = WriteFont(root, "b.ttf");   // family "Noto Sans Bold"(直名)
        var probe = new FakeProbe { Map = { [a] = ("Noto Sans", true), [b] = ("Noto Sans Bold", true) } };
        var locator = new LinuxFontLocator(new[] { root }, probe);
        Assert.Equal(b, locator.Locate("Noto Sans", bold: true));
    }

    [Fact]
    public void BoldQuery_MatchesStyleFlagBold_WhenNoDirectName()
    {
        string root = TempRoot();
        string a = WriteFont(root, "a.ttf");
        var probe = new FakeProbe { Map = { [a] = ("Noto Sans", true) } };
        var locator = new LinuxFontLocator(new[] { root }, probe);
        Assert.Equal(a, locator.Locate("Noto Sans", bold: true));
    }

    [Fact]
    public void BoldQuery_FallsBackToRegularFace_BoldOfSemantics()
    {
        // IFontLocator 契约:bold 查不到独立粗体时回退主脸(与 WindowsFontLocator ③ 同;
        // FontLibrary 以路径同异决定是否共用句柄)
        string root = TempRoot();
        string a = WriteFont(root, "a.ttf");
        var probe = new FakeProbe { Map = { [a] = ("Noto Sans", false) } };
        var locator = new LinuxFontLocator(new[] { root }, probe);
        Assert.Equal(a, locator.Locate("Noto Sans", bold: true));
    }

    [Fact]
    public void RegularQuery_PrefersNonBoldFace_OverModernNamedBold()
    {
        // 评审 Critical#1 回归:现代命名粗体文件 family_name==族名 + Bold 标志,且 Ordinal 路径序
        // 确定性排前("a.ttf" 粗 < "b.ttf" 常规)——常规查询不得把粗体脸当常规返回
        string root = TempRoot();
        string a = WriteFont(root, "a.ttf");   // family "Noto Sans" + Bold 标志(现代命名粗体)
        string b = WriteFont(root, "b.ttf");   // family "Noto Sans" 常规
        var probe = new FakeProbe { Map = { [a] = ("Noto Sans", true), [b] = ("Noto Sans", false) } };
        var locator = new LinuxFontLocator(new[] { root }, probe);
        Assert.Equal(b, locator.Locate("Noto Sans", bold: false));
    }

    [Fact]
    public void RegularQuery_BoldOnlyFamily_FallsBackToBoldFace()
    {
        // 粗体脸仅作无常规脸时的兜底:全粗体族常规查询仍可用(渲染粗体优于并入槽 0 出 .notdef);
        // 粗体查询亦仍可用(样式标志路径)
        string root = TempRoot();
        string a = WriteFont(root, "a.ttf");
        var probe = new FakeProbe { Map = { [a] = ("Noto Sans", true) } };
        var locator = new LinuxFontLocator(new[] { root }, probe);
        Assert.Equal(a, locator.Locate("Noto Sans", bold: false));
        Assert.Equal(a, locator.Locate("Noto Sans", bold: true));
    }

    [Fact]
    public void UnknownFamily_ReturnsNull()
    {
        string root = TempRoot();
        string a = WriteFont(root, "a.ttf");
        var probe = new FakeProbe { Map = { [a] = ("Noto Sans", false) } };
        var locator = new LinuxFontLocator(new[] { root }, probe);
        Assert.Null(locator.Locate("Definitely Not A Font Family", bold: false));
        Assert.Null(locator.Locate("Definitely Not A Font Family", bold: true));
    }

    [Fact]
    public void NullFamilyName_Throws()
    {
        var locator = new LinuxFontLocator(new[] { TempRoot() }, new FakeProbe());
        Assert.Throws<ArgumentNullException>(() => locator.Locate(null!, bold: false));
    }

    [Fact]
    public void MacLocator_SharesDirectoryScanEngine()
    {
        // Mac 回退路径与 Linux 同法(DirectoryFontLocator):注入 roots + 假 Probe 验证同一匹配语义
        string root = TempRoot();
        string a = WriteFont(root, "a.ttf");
        var probe = new FakeProbe { Map = { [a] = ("PingFang SC", false) } };
        var locator = new MacFontLocator(new[] { root }, probe);
        Assert.Equal(a, locator.Locate("pingfang sc", bold: false));
        Assert.Null(locator.Locate("Helvetica Neue", bold: false));
    }

    // ---- ③ FT 真实现小合同(Gpu trait:Windows 宿主真系统字体 + vendored freetype.dll) ----

    [Fact]
    [Trait("Category", "Gpu")]
    public void FreeTypeProbe_ReadsSegoeUiFamily()
    {
        using var probe = new FreeTypeFontFamilyProbe();
        Assert.True(probe.TryGetFamily(@"C:\Windows\Fonts\segoeui.ttf", wantBold: false, out string family, out bool isBold), family);
        Assert.Equal("Segoe UI", family);
        Assert.False(isBold);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void FreeTypeProbe_ReadsBoldStyleFlag()
    {
        using var probe = new FreeTypeFontFamilyProbe();
        Assert.True(probe.TryGetFamily(@"C:\Windows\Fonts\segoeuib.ttf", wantBold: true, out string family, out bool isBold), family);
        // Segoe UI 现代命名:family_name 保持 "Segoe UI",粗体经 style_flags 判定
        Assert.Equal("Segoe UI", family);
        Assert.True(isBold);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void FreeTypeProbe_TtcReportsFirstFace()
    {
        // msyh.ttc:face 0 = Microsoft YaHei 常规(与 WindowsFontLocator/FontLibrary face index 0 约定一致)
        using var probe = new FreeTypeFontFamilyProbe();
        Assert.True(probe.TryGetFamily(@"C:\Windows\Fonts\msyh.ttc", wantBold: false, out string family, out bool isBold), family);
        Assert.Equal("Microsoft YaHei", family);
        Assert.False(isBold);
    }

    [Fact]
    public void FreeTypeProbe_RejectsNonFontFile()
    {
        using var probe = new FreeTypeFontFamilyProbe();
        string junk = WriteFont(TempRoot(), "junk.ttf");
        Assert.False(probe.TryGetFamily(junk, wantBold: false, out string family, out _));
        Assert.Equal("", family);
    }
}
