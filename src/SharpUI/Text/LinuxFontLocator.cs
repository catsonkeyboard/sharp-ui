using System.Runtime.InteropServices;

namespace SharpUI.Text;

/// <summary>
/// 字体文件族名判读抽象(X-2):目录扫描发现器把"文件 → (族名, 粗体样式, 命中脸序)"外包给本接口,
/// FT 真实现(FreeTypeFontFamilyProbe)与单测假实现可互换——扫描/匹配的纯文件系统逻辑
/// 不依赖原生库即可在 Windows 宿主全覆盖。终审修复波(项 b)增 wantedFamily 目标参数与
/// faceIndex 出参:TTC 多脸文件常规查询可命中后续脸(T1 留扩展点落实)。
/// </summary>
internal interface IFontFamilyProbe
{
    /// <summary>探测字体文件的族名、粗体样式与命中脸序。wantedFamily 为调用方在找的族名(TTC
    /// 常规查询遍历的目标,大小写不敏感);wantBold 为偏好提示:TTC 多脸文件按序找首个粗体脸,
    /// 单脸文件即其唯一脸(常规查询目标族名命中 face 0 时即 face 0)。family 为 FT family_name
    /// (粗体独立命名时形如 "Noto Sans Bold");isBold 为 style_flags 含 Bold;faceIndex 为返回
    /// 描述所在的 TTC 脸序(单脸文件恒 0;常规查询目标族落在后续脸时为该脸序,调用方
    /// FontLibrary 以此喂 FT_New_Memory_Face)。文件不可读/非字体/损坏返回 false,调用方跳过。</summary>
    bool TryGetFamily(string path, string wantedFamily, bool wantBold, out string family, out bool isBold, out int faceIndex);
}

/// <summary>
/// IFontFamilyProbe 的 FreeType 默认实现:FT_New_Memory_Face 内存脸读 family_name/style_flags
/// (复用 FreeTypeNative 声明层,零新增 DllImport;探测脸用完即 FT_Done_Face,数据钉住随脸释放,
/// 同 FontLibrary.LoadFace 所有权契约)。TTC 两向遍历:粗体提示找首个粗体脸(X-2 原有)、
/// 常规查询按目标族名找后续脸(终审修复波项 b,NotoSansCJK-Regular.ttc face 0=JP 场景)。
/// FT_Library 惰性建一次、进程生命周期持有(单线程 UI 构造期使用,与 WindowsFontLocator
/// 注册表快照同约定;显式 Dispose 可提前释放,发现器不强制)。
/// **Linux/macOS 真实字体树探测待 CI 实机验证**(Windows 宿主经系统字体文件合同测试覆盖)。
/// </summary>
internal sealed class FreeTypeFontFamilyProbe : IFontFamilyProbe, IDisposable
{
    IntPtr _ftLib;
    bool _initTried, _disposed;

    public bool TryGetFamily(string path, string wantedFamily, bool wantBold, out string family, out bool isBold, out int faceIndex)
    {
        family = "";
        isBold = false;
        faceIndex = 0;
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
                        faceIndex = i;
                        return true;
                    }
                    finally { _ = FreeTypeNative.FT_Done_Face(f); }
                }
            }
            // TTC:常规查询且 face 0 非目标族 → 遍历后续脸找目标族(CI 预备项 b):NotoSansCJK-Regular.ttc
            // face 0=JP、SC 落后续脸,只读 face 0 会使 "Noto Sans CJK SC" 常规查询不中 → 回退槽并入槽 0 出
            // .notdef。face_index 位约定(vendored 2.13.3 头文件 freetype.h FT_Open_Face/FT_FaceRec):
            // 位 0-15=文件内脸序(0..num_faces-1)、位 16-30=变字体 named instance 序(非变字体忽略)——
            // 纯 TTC 脸遍历传 0..num_faces-1 裸值即可(与上方粗体扫描同法)。匹配脸优先非粗体
            // (常规查询语义,评审 Critical#1);仅粗体脸命中时记首见兜底(渲染粗体优于 .notdef)。
            if (!wantBold && numFaces > 1
                && !string.Equals(family0, wantedFamily, StringComparison.OrdinalIgnoreCase))
            {
                int boldFallback = -1;
                string boldFallbackFamily = "";
                for (int i = 1; i < numFaces; i++)
                {
                    if (FreeTypeNative.FT_New_Memory_Face(_ftLib, pin.AddrOfPinnedObject(), data.Length, i, out IntPtr f) != 0
                        || f == IntPtr.Zero) continue;
                    try
                    {
                        var rec = Marshal.PtrToStructure<FreeTypeNative.FaceRec>(f);
                        string fam = Marshal.PtrToStringAnsi(rec.FamilyName) ?? "";
                        if (fam.Length == 0
                            || !string.Equals(fam, wantedFamily, StringComparison.OrdinalIgnoreCase)) continue;
                        if ((rec.StyleFlags & FreeTypeNative.StyleFlagBold) == 0)
                        {
                            family = fam;
                            isBold = false;
                            faceIndex = i;
                            return true;
                        }
                        if (boldFallback < 0) { boldFallback = i; boldFallbackFamily = fam; }
                    }
                    finally { _ = FreeTypeNative.FT_Done_Face(f); }
                }
                if (boldFallback >= 0)
                {
                    family = boldFallbackFamily;
                    isBold = true;
                    faceIndex = boldFallback;
                }
            }
            return true;   // face 0 描述(faceIndex=0;遍历未命中目标族时交回调用方按三级匹配处置)
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
/// 大小写不敏感)即时返回——粗体查询名自带字重意图;**常规查询须非粗体脸**(现代命名粗体文件
/// family_name==族名 + Bold 标志且路径序排前,不看标志会把粗体脸当常规返回,评审 Critical#1),
/// 粗体直名脸仅作无常规脸时的兜底;直名优先级须压过扫描顺序,故 ②③/兜底只记录首见、全表扫完
/// 再择优;② 粗体查询时同 family 且 style flags 含 Bold 的脸;③ 粗体回落主脸(IFontLocator
/// 契约的 BoldOf 回退;FontLibrary 以路径同异决定是否共用句柄)。命中随路径透出 TTC 脸序
/// faceIndex(终审修复波项 b:probe 常规查询可命中 TTC 后续脸,如 NotoSansCJK-Regular.ttc 的
/// SC 脸,FontLibrary 喂 FT_New_Memory_Face)。
/// 扫描文件表与探测结果均按需缓存一次(单线程 UI 构造期,与 WindowsFontLocator 注册表快照
/// 同约定;探测缓存键含 wanted+wantBold:TTC 脸遍历/粗体提示可指向不同脸)。**Linux/macOS
/// 目录/权限/真实字体树语义待 CI 实机验证**(Windows 测试宿主经 roots 注入 + 假 Probe
/// 覆盖扫描/匹配逻辑)。
/// </summary>
internal abstract class DirectoryFontLocator : IFontLocator
{
    readonly IFontFamilyProbe _probe;
    readonly Dictionary<(string Path, string Wanted, bool WantBold), (string Family, bool IsBold, int FaceIndex)?> _probeCache = new();
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

    public string? Locate(string familyName, bool bold, out int faceIndex)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        faceIndex = 0;
        string wanted = familyName.Trim();
        if (wanted.Length == 0) return null;
        string direct = bold ? wanted + " Bold" : wanted;
        (string File, int Face)? styleFlagHit = null, regularHit = null, boldOnlyHit = null;
        foreach (string file in FontFiles())
        {
            if (!TryProbe(file, wanted, bold, out string family, out bool isBold, out int face)) continue;
            if (string.Equals(family, direct, StringComparison.OrdinalIgnoreCase))
            {
                // ① 直名:粗体查询名自带字重意图,即时返回;常规查询须非粗体脸——现代命名粗体文件
                //    family_name==族名 + Bold 标志,且路径序确定性排前("X-Bold.ttf" < "X-Regular.ttf"),
                //    不看标志会把粗体脸当常规返回(评审 Critical#1)。粗体直名脸仅作常规查询
                //    无常规脸时的兜底(记首见,渲染粗体优于并入槽 0)。
                if (bold || !isBold) { faceIndex = face; return file; }
                boldOnlyHit ??= (file, face);
                continue;
            }
            if (bold && string.Equals(family, wanted, StringComparison.OrdinalIgnoreCase))
            {
                if (isBold) styleFlagHit ??= (file, face);   // ② 样式标志粗体(记首见)
                else regularHit ??= (file, face);            // ③ 主脸回退(BoldOf;记首见)
            }
        }
        if (styleFlagHit is { } s) { faceIndex = s.Face; return s.File; }
        if (regularHit is { } r) { faceIndex = r.Face; return r.File; }
        if (boldOnlyHit is { } b) { faceIndex = b.Face; return b.File; }
        return null;
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

    /// <summary>探测(缓存):失败亦缓存(坏文件不重试);键含 wanted+wantBold(TTC 脸遍历/粗体提示
    /// 的探测结果随目标与提示而异)。</summary>
    bool TryProbe(string path, string wanted, bool wantBold, out string family, out bool isBold, out int faceIndex)
    {
        if (_probeCache.TryGetValue((path, wanted, wantBold), out (string Family, bool IsBold, int FaceIndex)? cached))
        {
            if (cached is { } hit) { family = hit.Family; isBold = hit.IsBold; faceIndex = hit.FaceIndex; return true; }
            family = "";
            isBold = false;
            faceIndex = 0;
            return false;
        }
        bool ok = _probe.TryGetFamily(path, wanted, wantBold, out family, out isBold, out faceIndex);
        _probeCache[(path, wanted, wantBold)] = ok ? (family, isBold, faceIndex) : null;
        return ok;
    }
}

/// <summary>
/// Linux 字体发现(X-2):fontconfig 约定目录递归扫描 + FT face 族名匹配(默认
/// FreeTypeFontFamilyProbe:FT_New_Memory_Face 内存探测,不落盘、无 fontconfig 依赖)。
/// roots 可注入(Windows 宿主单测纯文件系统逻辑);匹配语义见 DirectoryFontLocator 头注。
/// **目录约定与 FT 探测在真实发行版字体树上的语义待 CI 实机验证。**
/// </summary>
internal sealed class LinuxFontLocator : DirectoryFontLocator
{
    public LinuxFontLocator(IEnumerable<string>? roots = null, IFontFamilyProbe? probe = null)
        : base(roots, probe) { }

    protected override string[] DefaultRoots() =>
        new[]
        {
            "/usr/share/fonts",                                        // fontconfig 出厂模板:发行版系统目录
            "/usr/local/share/fonts",                                  // 同模板:本机管理员手装目录(评审 Minor#2 补)
            Path.Combine(Home(), ".local", "share", "fonts"),          // XDG 用户目录
            Path.Combine(Home(), ".fonts"),                            // 传统用户目录
        };
}
