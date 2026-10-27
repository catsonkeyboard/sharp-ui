using System.Runtime.InteropServices;

namespace SharpUI.Text;

/// <summary>
/// 字体文件族名判读抽象(X-2):目录扫描发现器把"文件 → (族名, 粗体样式)"外包给本接口,
/// FT 真实现(FreeTypeFontFamilyProbe)与单测假实现可互换——扫描/匹配的纯文件系统逻辑
/// 不依赖原生库即可在 Windows 宿主全覆盖。
/// </summary>
internal interface IFontFamilyProbe
{
    /// <summary>探测字体文件的族名与粗体样式。wantBold 为偏好提示:TTC 多脸文件按序找首个
    /// 粗体脸,单脸文件即其唯一脸(常规提示恒 face 0,与 FontLibrary 的 face index 0 约定一致);
    /// family 为 FT family_name(粗体独立命名时形如 "Noto Sans Bold");isBold 为 style_flags
    /// 含 Bold。文件不可读/非字体/损坏返回 false,调用方跳过。</summary>
    bool TryGetFamily(string path, bool wantBold, out string family, out bool isBold);
}

/// <summary>
/// IFontFamilyProbe 的 FreeType 默认实现:FT_New_Memory_Face 内存脸读 family_name/style_flags
/// (复用 FreeTypeNative 声明层,零新增 DllImport;探测脸用完即 FT_Done_Face,数据钉住随脸释放,
/// 同 FontLibrary.LoadFace 所有权契约)。FT_Library 惰性建一次、进程生命周期持有(单线程 UI
/// 构造期使用,与 WindowsFontLocator 注册表快照同约定;显式 Dispose 可提前释放,发现器不强制)。
/// **Linux/macOS 真实字体树探测待 CI 实机验证**(Windows 宿主经系统字体文件合同测试覆盖)。
/// </summary>
internal sealed class FreeTypeFontFamilyProbe : IFontFamilyProbe, IDisposable
{
    IntPtr _ftLib;
    bool _initTried, _disposed;

    public bool TryGetFamily(string path, bool wantBold, out string family, out bool isBold)
    {
        family = "";
        isBold = false;
        if (_disposed || !EnsureLibrary()) return false;
        byte[] data;
        try { data = File.ReadAllBytes(path); }   // 托管读取按 UTF-16 文件名:非 ASCII 路径安全(T0 携带项)
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);   // FT 不拷贝入参内存:存活至 FT_Done_Face
        try
        {
            if (FreeTypeNative.FT_New_Memory_Face(_ftLib, pin.AddrOfPinnedObject(), data.Length, 0, out IntPtr face) != 0
                || face == IntPtr.Zero)
                return false;
            string family0 = "";
            bool bold0 = false;
            int numFaces;
            try
            {
                var rec = Marshal.PtrToStructure<FreeTypeNative.FaceRec>(face);
                numFaces = rec.NumFaces;                                        // TTC 脸数(face 0 起)
                family0 = Marshal.PtrToStringAnsi(rec.FamilyName) ?? "";        // 代码页字节串,同 FaceHandle 读取约定
                bold0 = (rec.StyleFlags & FreeTypeNative.StyleFlagBold) != 0;
            }
            finally { _ = FreeTypeNative.FT_Done_Face(face); }
            family = family0;
            isBold = bold0;
            if (family0.Length == 0) return false;
            // TTC:粗体提示且 face 0 非粗体 → 按序找首个粗体脸(找不到保留 face 0 判读)
            if (wantBold && !bold0 && numFaces > 1)
            {
                for (int i = 1; i < numFaces; i++)
                {
                    if (FreeTypeNative.FT_New_Memory_Face(_ftLib, pin.AddrOfPinnedObject(), data.Length, i, out IntPtr f) != 0
                        || f == IntPtr.Zero) continue;
                    try
                    {
                        var rec = Marshal.PtrToStructure<FreeTypeNative.FaceRec>(f);
                        if ((rec.StyleFlags & FreeTypeNative.StyleFlagBold) == 0) continue;
                        string fam = Marshal.PtrToStringAnsi(rec.FamilyName) ?? "";
                        if (fam.Length == 0) continue;
                        family = fam;
                        isBold = true;
                        return true;
                    }
                    finally { _ = FreeTypeNative.FT_Done_Face(f); }
                }
            }
            return true;
        }
        finally { pin.Free(); }   // 最后一个探测脸已 Done,可解除钉住
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ftLib != IntPtr.Zero) _ = FreeTypeNative.FT_Done_FreeType(_ftLib);
    }

    /// <summary>FT_Library 惰性初始化(失败探测一次后不再重试;库缺失时全部判 false 静默跳过)。</summary>
    bool EnsureLibrary()
    {
        if (_ftLib != IntPtr.Zero) return true;
        if (_initTried) return false;
        _initTried = true;
        return FreeTypeNative.FT_Init_FreeType(out _ftLib) == 0 && _ftLib != IntPtr.Zero;
    }
}

/// <summary>
/// 目录扫描式字体发现公共实现(X-2):roots 递归收集 ttf/otf/ttc → IFontFamilyProbe 判读族名,
/// LinuxFontLocator(fontconfig 约定目录)与 MacFontLocator(回退路径)共用。匹配三级
/// (与 WindowsFontLocator 语义对齐):① 直名(family == 目标;粗体目标即 "family Bold",
/// 大小写不敏感)即时返回——直名优先级须压过扫描顺序,故 ②③ 只记录、全表扫完再择优;
/// ② 粗体查询时同 family 且 style flags 含 Bold 的脸;③ 粗体回落主脸(IFontLocator 契约的
/// BoldOf 回退;FontLibrary 以路径同异决定是否共用句柄)。
/// 扫描文件表与探测结果均按需缓存一次(单线程 UI 构造期,与 WindowsFontLocator 注册表快照
/// 同约定;探测缓存键含 wantBold:TTC 粗体提示可指向不同脸)。**Linux/macOS 目录/权限/真实
/// 字体树语义待 CI 实机验证**(Windows 测试宿主经 roots 注入 + 假 Probe 覆盖扫描/匹配逻辑)。
/// </summary>
internal abstract class DirectoryFontLocator : IFontLocator
{
    readonly IFontFamilyProbe _probe;
    readonly Dictionary<(string Path, bool WantBold), (string Family, bool IsBold)?> _probeCache = new();
    List<string>? _files;   // 扫描快照(一次)

    protected DirectoryFontLocator(IEnumerable<string>? roots, IFontFamilyProbe? probe)
    {
        Roots = roots?.ToArray() ?? DefaultRoots();
        _probe = probe ?? new FreeTypeFontFamilyProbe();
    }

    /// <summary>扫描根目录集(测试断言默认值用;注入即覆盖)。</summary>
    internal string[] Roots { get; }

    /// <summary>平台默认扫描根(Linux:fontconfig 约定;Mac:系统三目录)。</summary>
    protected abstract string[] DefaultRoots();

    /// <summary>用户主目录(HOME 缺失的异常环境保留 "~" 前缀;扫描侧 Directory.Exists 判不存在,无害)。</summary>
    protected static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is string p && p.Length > 0 ? p : "~";

    public string? Locate(string familyName, bool bold)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        string wanted = familyName.Trim();
        if (wanted.Length == 0) return null;
        string direct = bold ? wanted + " Bold" : wanted;
        string? styleFlagHit = null, regularHit = null;
        foreach (string file in FontFiles())
        {
            if (!TryProbe(file, bold, out string family, out bool isBold)) continue;
            if (string.Equals(family, direct, StringComparison.OrdinalIgnoreCase)) return file;   // ① 直名即时返回
            if (bold && string.Equals(family, wanted, StringComparison.OrdinalIgnoreCase))
            {
                if (isBold) styleFlagHit ??= file;   // ② 样式标志粗体(记首见)
                else regularHit ??= file;            // ③ 主脸回退(BoldOf;记首见)
            }
        }
        return styleFlagHit ?? regularHit;
    }

    /// <summary>扫描快照:存在的 root 递归收集字体扩展文件,按路径序排序(确定性:直名优先级
    /// 判定与 ②③ 首见记录不依赖文件系统枚举序);不可访问目录静默跳过。</summary>
    List<string> FontFiles()
    {
        if (_files is not null) return _files;
        var files = new List<string>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (string root in Roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", opts))
                    if (IsFontFile(file)) files.Add(file);
            }
            catch (IOException) { }                    // 枚举中途目录被删/设备错误:该根所得部分结果保留
            catch (UnauthorizedAccessException) { }
        }
        files.Sort(StringComparer.Ordinal);
        _files = files;
        return _files;
    }

    /// <summary>字体扩展过滤(ttf/otf/ttc,大小写不敏感;其余扩展不进探测)。</summary>
    static bool IsFontFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".ttf" or ".otf" or ".ttc";
    }

    /// <summary>探测(缓存):失败亦缓存(坏文件不重试)。</summary>
    bool TryProbe(string path, bool wantBold, out string family, out bool isBold)
    {
        if (_probeCache.TryGetValue((path, wantBold), out (string Family, bool IsBold)? cached))
        {
            if (cached is { } hit) { family = hit.Family; isBold = hit.IsBold; return true; }
            family = "";
            isBold = false;
            return false;
        }
        bool ok = _probe.TryGetFamily(path, wantBold, out family, out isBold);
        _probeCache[(path, wantBold)] = ok ? (family, isBold) : null;
        return ok;
    }
}

/// <summary>
/// Linux 字体发现(X-2):fontconfig 约定目录递归扫描 + FT face 族名匹配(默认
/// FreeTypeFontFamilyProbe:FT_New_Memory_Face 内存探测,不落盘、无 fontconfig 依赖)。
/// roots 可注入(Windows 宿主单测纯文件系统逻辑);匹配三级语义见 DirectoryFontLocator 头注。
/// **目录约定与 FT 探测在真实发行版字体树上的语义待 CI 实机验证。**
/// </summary>
internal sealed class LinuxFontLocator : DirectoryFontLocator
{
    public LinuxFontLocator(IEnumerable<string>? roots = null, IFontFamilyProbe? probe = null)
        : base(roots, probe) { }

    protected override string[] DefaultRoots() =>
        new[]
        {
            "/usr/share/fonts",                                        // 发行版(fontconfig 系统目录)
            Path.Combine(Home(), ".local", "share", "fonts"),          // XDG 用户目录
            Path.Combine(Home(), ".fonts"),                            // 传统用户目录
        };
}
