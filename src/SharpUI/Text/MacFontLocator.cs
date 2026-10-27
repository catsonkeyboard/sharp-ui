namespace SharpUI.Text;

/// <summary>
/// macOS 字体发现(X-2):**CoreText 优先路径待 objc 层落地后接线(T3;X-2 已批准 objc 直连,
/// CTFontCollectionCreateFromAvailableFonts 族名匹配)**;本任务先落目录扫描回退路径——与
/// LinuxFontLocator 同法(DirectoryFontLocator),roots 默认 Mac 三目录:
/// /System/Library/Fonts + /Library/Fonts + ~/Library/Fonts。匹配三级语义见基类头注。
/// **目录约定在真实 macOS 字体树上的语义待 CI 实机验证**(Windows 宿主经 roots 注入 +
/// 假 Probe 单测覆盖扫描/匹配逻辑)。
/// </summary>
internal sealed class MacFontLocator : DirectoryFontLocator
{
    public MacFontLocator(IEnumerable<string>? roots = null, IFontFamilyProbe? probe = null)
        : base(roots, probe) { }

    protected override string[] DefaultRoots() =>
        new[]
        {
            "/System/Library/Fonts",                          // 系统字体(San Francisco 等)
            "/Library/Fonts",                                 // 本机全局
            Path.Combine(Home(), "Library", "Fonts"),         // 用户
        };
}
