using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SharpUI.Text;

/// <summary>族名→字体文件路径。实现:Windows=注册表(WindowsFontLocator)、Linux=fontconfig
/// 约定目录扫描(LinuxFontLocator)、macOS=目录扫描回退(MacFontLocator;CoreText 优先未接线,
/// 归 M3e 后半/CI 轮)。bold=先查 "&lt;family&gt; Bold",查不到回退 "&lt;family&gt;"(与 TextAtlas.BoldOf
/// 回退语义一致,三实现同契约)。</summary>
internal interface IFontLocator
{
    /// <summary>解析族名(含粗体族如 "Segoe UI Bold");失败返回 null。
    /// bold=先查 "&lt;family&gt; Bold",查不到回退 "&lt;family&gt;"(与 TextAtlas.BoldOf 回退语义一致)。
    /// faceIndex=命中文件的 TTC 脸序,随路径透出供 FT_New_Memory_Face(终审修复波项 b:目录扫描
    /// 常规查询可命中 TTC 后续脸,如 NotoSansCJK-Regular.ttc 的 SC 脸;Windows 注册表路径恒 0)。</summary>
    string? Locate(string familyName, bool bold, out int faceIndex);
}

/// <summary>
/// Windows 注册表字体发现:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts,
/// 值名 "&lt;family&gt; (TrueType)" → 文件名(相对 C:\Windows\Fonts)或绝对路径。
/// 值名匹配两级:① 精确族名("Segoe UI");② "&amp;" 变体前缀——微软雅黑的值名为
/// "Microsoft YaHei &amp; Microsoft YaHei UI (TrueType)"(共享 msyh.ttc),按族名前缀匹配、不要求全等;
/// 仅接 " &amp;" 分隔的变体(排除 "Segoe UI Semibold" 这类同前缀异脸值名)。粗体目标 "&lt;family&gt; Bold"
/// 同规则匹配,查不到回退主脸值(Locate 层面完成 BoldOf 回退;FontLibrary 以路径同异决定是否共用句柄)。
/// 注册表快照按需加载一次并复用(单线程 UI 构造期使用,无锁;非 Windows 宿主一律判失败)。
/// </summary>
internal sealed class WindowsFontLocator : IFontLocator
{
    Dictionary<string, string>? _entries;   // 规范化值名(去 " (TrueType)" 类类型括注)→ 绝对路径

    public string? Locate(string familyName, bool bold, out int faceIndex)
    {
        faceIndex = 0;   // 注册表值名无脸序信息:恒 face 0(msyh.ttc 首脸=常规,与 FontLibrary face index 0 约定一致)
        ArgumentNullException.ThrowIfNull(familyName);
        if (!OperatingSystem.IsWindows()) return null;   // 后续里程碑的发现器入口;亦守护注册表 API 的平台门槛(CA1416)
        _entries ??= LoadRegistryEntries();
        string target = bold ? familyName.Trim() + " Bold" : familyName.Trim();
        // ① 精确:"Segoe UI (TrueType)" → 键 "Segoe UI"(大小写不敏感:注册表值名大小写不稳定)
        if (_entries.TryGetValue(target, out string? path)) return path;
        // ② & 变体:"Microsoft YaHei" → "Microsoft YaHei & Microsoft YaHei UI"(前缀 + " &")
        string variantPrefix = target + " &";
        foreach (KeyValuePair<string, string> entry in _entries)
            if (entry.Key.StartsWith(variantPrefix, StringComparison.OrdinalIgnoreCase)) return entry.Value;
        // ③ 粗体回落主脸(BoldOf:无独立粗体文件时用主脸;查询侧仍可由返回路径与主脸比对识别回落)
        if (bold && _entries.TryGetValue(familyName.Trim(), out path)) return path;
        return null;
    }

    /// <summary>注册表值名 → 绝对路径表(一次性;REG_EXPAND_SZ 由 GetValue 默认展开)。</summary>
    static Dictionary<string, string> LoadRegistryEntries()
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return entries;
        using RegistryKey? fontsKey =
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
        if (fontsKey is null) return entries;
        string fontsDir = FontsDirectory();
        foreach (string valueName in fontsKey.GetValueNames())
        {
            if (fontsKey.GetValue(valueName) is not string file || file.Length == 0) continue;
            // 值名形如 "<族名> (TrueType)"/"(OpenType)";".fon/.fnt 位图字体无此括注,跳过(不适用 FT 文本层)
            int cut = valueName.LastIndexOf(" (", StringComparison.Ordinal);
            if (cut <= 0) continue;
            string familyPart = valueName[..cut].Trim();
            if (familyPart.Length == 0) continue;
            string full = Path.IsPathRooted(file) ? file : Path.Combine(fontsDir, file);
            entries.TryAdd(familyPart, full);   // 一族多值(罕见)保留首个
        }
        return entries;
    }

    /// <summary>字体目录:SpecialFolder.Fonts(缺省 C:\Windows\Fonts);异常环境回落 &lt;Windows&gt;\Fonts。</summary>
    static string FontsDirectory()
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return dir.Length > 0 ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
    }
}

/// <summary>
/// D3 文本层 Task 2:FT_Library + 槽位脸表生命周期。槽 0 = 平台主族(Windows "Segoe UI" /
/// Linux "Noto Sans"(缺则 DejaVu Sans)/ Mac "Helvetica Neue",跨平台里程碑 X-2),
/// 1.. = 回退族(发现失败或文件缺失并入槽 0,同现状 tf ?? _typefaces[0];文件在而 FT 加载失败
/// 属异常路径,抛出不并入);每脸配 hb_ft_font。**hb_ft 尺度同步时序(评审 Critical#1 勘误)**:
/// hb_ft_font_create 创建期从 face->size->metrics 快照尺度(hb-ft.h L95-97 明示建前先设尺寸),
/// 此后直接 FT_Set_Pixel_Sizes 不触发 hb 再同步(其变更检测只比 hb 侧 serial)——故 FaceHandle.
/// SetPixelSize 在改尺寸后显式 hb_ft_font_changed(HbFont) 通知 hb 重取尺度并失效 advance cache
/// (hb_font 对象本身仍复用,不重建)。
/// 脸加载统一 FT_New_Memory_Face(T0 携带项):文件字节读入托管数组 + GCHandle 钉住传指针——
/// FT_New_Face 路径参数走 ANSI fopen,非 ASCII 字体路径会因代码页失败;FT 不拷贝入参内存
/// (freetype.h 明文须存活至 FT_Done_Face),钉住句柄由 FaceHandle 持有并在释放脸之后 Free。
/// 粗体语义与 TextAtlas.BoldOf 一致:槽 0 粗体=注册表独立粗体文件(查不到回落主脸句柄);
/// 回退槽粗体=同脸(粗体回退为可接受折衷,D21)。单线程 UI 模型;构造期分配自由,绘制路径零分配由 T3/T4 保证。
/// </summary>
internal sealed class FontLibrary : IDisposable
{
    /// <summary>槽 0 主族名(平台映射:Windows "Segoe UI" / Linux "Noto Sans"(缺则 "DejaVu Sans",
    /// 回退在构造期解析层完成,见 PrimaryFamilyCandidates)/ Mac "Helvetica Neue";T4 TextAtlas 重写时消费)。</summary>
    public static string PrimaryFamily => OperatingSystem.IsWindows() ? "Segoe UI"
        : OperatingSystem.IsLinux() ? "Noto Sans"
        : "Helvetica Neue";

    readonly IntPtr _ftLib;
    readonly IFontLocator _locator = CreateLocator();   // 平台分派(跨平台里程碑 X-2)
    readonly string _primaryFamily = "";               // 构造期解析后的槽 0 实际族名(Linux DejaVu 回退时 ≠ PrimaryFamily)
    readonly string?[] _files;                          // null = 解析/加载失败 → 并入槽 0
    readonly int[] _faceIndex;                          // 槽位 TTC 脸序(终审修复波项 b;Windows 注册表路径恒 0)
    readonly Dictionary<int, FaceHandle> _faces = new();   // 槽 → 常规脸(懒建)
    FaceHandle? _boldFace;                              // 槽 0 粗体独立脸(懒建;null+已解析 = 回落主脸)
    bool _boldResolved;
    bool _disposed;

    static IFontLocator CreateLocator() =>
        OperatingSystem.IsWindows() ? new WindowsFontLocator()
        : OperatingSystem.IsLinux() ? new LinuxFontLocator()
        : new MacFontLocator();

    /// <summary>槽 0 主族解析候选(Linux 首选 Noto Sans,发行版最小化安装常缺 → 回退 DejaVu Sans;
    /// Windows/Mac 单候选,行为与平台分派前一致)。</summary>
    static string[] PrimaryFamilyCandidates() =>
        OperatingSystem.IsLinux() ? new[] { "Noto Sans", "DejaVu Sans" } : new[] { PrimaryFamily };

    /// <summary>平台默认回退族链(fallbackFamilies=null 时):Windows 保持现值(雅黑+Emoji);
    /// Linux Noto CJK SC + Noto Color Emoji;Mac PingFang SC + Apple Color Emoji(待 CI 实机验证)。</summary>
    static string[] DefaultFallbackFamilies() => OperatingSystem.IsWindows()
        ? new[] { "Microsoft YaHei", "Segoe UI Emoji" }
        : OperatingSystem.IsLinux()
        ? new[] { "Noto Sans CJK SC", "Noto Color Emoji" }
        : new[] { "PingFang SC", "Apple Color Emoji" };

    /// <summary>槽 0 = PrimaryFamily(平台映射;Linux 缺 Noto 时回退 DejaVu Sans),fallbackFamilies
    /// 依次入槽 1..(null = 平台默认链);主族解析/加载失败即抛(库不可用)。</summary>
    public FontLibrary(string[]? fallbackFamilies = null)
    {
        fallbackFamilies ??= DefaultFallbackFamilies();
        int err = FreeTypeNative.FT_Init_FreeType(out _ftLib);
        if (err != 0 || _ftLib == IntPtr.Zero)
            throw new InvalidOperationException($"FT_Init_FreeType failed: {err}");
        _files = new string?[1 + fallbackFamilies.Length];
        _faceIndex = new int[1 + fallbackFamilies.Length];
        try
        {
            // 槽 0 候选链依次解析(单候选平台退化为原行为;Linux Noto→DejaVu 记实际命中族名供粗体解析)
            string? primaryPath = null;
            foreach (string candidate in PrimaryFamilyCandidates())
                if (ResolveExisting(candidate, bold: false, out string? path, out int face))
                {
                    primaryPath = path;
                    _faceIndex[0] = face;
                    _primaryFamily = candidate;
                    break;
                }
            _files[0] = primaryPath
                ?? throw new InvalidOperationException($"primary font family '{PrimaryFamily}' not resolvable via font registry");
            for (int i = 0; i < fallbackFamilies.Length; i++)   // null → 并入槽 0(faceIndex 默认 0 一并闲置)
                if (ResolveExisting(fallbackFamilies[i], bold: false, out string? path, out int face))
                {
                    _files[1 + i] = path;
                    _faceIndex[1 + i] = face;
                }
        }
        catch
        {
            // 半构造对象无人 Dispose:主族解析失败抛出前释放已初始化的 FT_Library(评审 Minor#1,防句柄泄漏)
            _ = FreeTypeNative.FT_Done_FreeType(_ftLib);
            throw;
        }
    }

    /// <summary>槽位数 = 1 主脸 + 回退族数(解析失败的槽保留槽位、脸并入槽 0,同现状)。</summary>
    public int SlotCount => _files.Length;

    /// <summary>槽是否含该码点(FT_Get_Char_Index ≠ 0;纯 charmap 查表,无需先设尺寸)。</summary>
    public bool ContainsGlyph(int slot, uint codepoint) =>
        FreeTypeNative.FT_Get_Char_Index(RegularFace(slot).FtFace, codepoint) != 0;

    /// <summary>槽位脸句柄(缓存):槽 0 粗体=独立粗体文件脸(查不到回落主脸句柄);回退槽粗体=同脸。</summary>
    public FaceHandle Face(int slot, bool bold)
    {
        if ((uint)slot >= (uint)_files.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        return bold && slot == 0 ? BoldFace() : RegularFace(slot);
    }

    /// <summary>行度量(像素):SizeMetrics 按 FT_Set_Pixel_Sizes 缩放,ascender/descender 为 26.6 定点 → /64。
    /// 调用方按 (slot,size,bold) 自缓存(同现状 GetLineMetrics;本方法非每帧热路径)。
    /// **警告:不可用于布局基线**——布局行度量请用 InkLineMetrics(GlyphRasterizer,T4 裁定:
    /// 本 SizeMetrics 源与 Skia 时代基线有 ~4px@20px 差,仅保留作原始数据查询;生产零消费)。</summary>
    public (float Ascender, float Descender) LineMetrics(int slot, int pixelSize, bool bold)
    {
        FaceHandle face = Face(slot, bold);
        face.SetPixelSize(pixelSize);
        // face->size(FaceSizeOffset=128)→FT_SizeRec.Metrics(@24):结构体单读(SizeRec 64B,冷路径免逐字段)
        IntPtr size = Marshal.ReadIntPtr(face.FtFace, FreeTypeNative.FaceSizeOffset);
        var sizeRec = Marshal.PtrToStructure<FreeTypeNative.SizeRec>(size);
        return (sizeRec.Metrics.Ascender / 64f, sizeRec.Metrics.Descender / 64f);
    }

    /// <summary>释放全部脸与 FT_Library;幂等(脸→FT_Library 逆序释放)。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (FaceHandle face in _faces.Values) face.Dispose();
        _faces.Clear();
        _boldFace?.Dispose();
        _boldFace = null;
        _ = FreeTypeNative.FT_Done_FreeType(_ftLib);
    }

    FaceHandle RegularFace(int slot)
    {
        if ((uint)slot >= (uint)_files.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        if (_files[slot] is null) slot = 0;   // 解析/加载失败并入槽 0(同现状)
        if (!_faces.TryGetValue(slot, out FaceHandle? face))
        {
            face = LoadFace(_files[slot]!, _faceIndex[slot]);
            _faces[slot] = face;
        }
        return face;
    }

    FaceHandle BoldFace()
    {
        if (_boldFace is null && !_boldResolved)
        {
            _boldResolved = true;
            // Locate 内部已含回落主脸值(用实际命中族名,Linux DejaVu 回退时与槽 0 一致)
            ResolveExisting(_primaryFamily, bold: true, out string? boldFile, out int boldFaceIndex);
            // 回落主脸(路径同槽 0)→ 共用常规脸句柄,BoldOf "查不到回落主脸"语义
            if (boldFile is not null && !PathEquals(boldFile, _files[0]!))
                _boldFace = LoadFace(boldFile, boldFaceIndex);
        }
        return _boldFace ?? RegularFace(0);
    }

    /// <summary>发现器解析 + 文件存在性核验(值指向已卸载字体时判失败并入槽 0);faceIndex 随路径透传。</summary>
    bool ResolveExisting(string familyName, bool bold, out string? path, out int faceIndex)
    {
        path = _locator.Locate(familyName, bold, out faceIndex);
        if (path is not null && !File.Exists(path)) { path = null; faceIndex = 0; }
        return path is not null;
    }

    /// <summary>FT_New_Memory_Face 统一加载。faceIndex=发现器命中的 TTC 脸序(终审修复波项 b):
    /// Windows 注册表路径恒 0(msyh.ttc 首脸=常规字重);目录扫描常规查询可命中后续脸
    /// (NotoSansCJK-Regular.ttc 的 SC 脸)——face_index 位约定(freetype.h FT_Open_Face):
    /// 位 0-15=文件内脸序、位 16-30=变字体 named instance,probe 透传的裸脸序直接可用。
    /// 钉住的托管字节在 FT_Done_Face 前不得释放(freetype.h 明文),由 FaceHandle 持有句柄兜底。
    /// 备忘(评审 Minor#5):钉住为整文件常驻(非按需分页)——槽位/脸数扩容时留意常驻内存 ≈ Σ字体文件大小。</summary>
    FaceHandle LoadFace(string file, int faceIndex)
    {
        byte[] data = File.ReadAllBytes(file);   // 托管读取按 UTF-16 文件名:非 ASCII 路径安全(T0 携带项)
        GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        int err = FreeTypeNative.FT_New_Memory_Face(_ftLib, pin.AddrOfPinnedObject(), data.Length, faceIndex, out IntPtr ftFace);
        if (err != 0 || ftFace == IntPtr.Zero)
        {
            pin.Free();
            throw new InvalidOperationException($"FT_New_Memory_Face failed ({err}): {file}");
        }
        IntPtr hbFont = HarfBuzzNative.hb_ft_font_create(ftFace, IntPtr.Zero);   // destroy=0:hb 不接管 face 生命周期
        if (hbFont == IntPtr.Zero)
        {
            _ = FreeTypeNative.FT_Done_Face(ftFace);
            pin.Free();
            throw new InvalidOperationException($"hb_ft_font_create failed: {file}");
        }
        return new FaceHandle(ftFace, hbFont, pin);
    }

    static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 单文件脸句柄:FT_Face + hb_ft_font 成对创建、逆序释放(hb_font 引用 face,须先 hb_font_destroy);
/// 附带钉住的文件字节(FT_New_Memory_Face 不拷贝内存,数据须活到 FT_Done_Face——T0 携带项,
/// GCHandle 亦使 byte[] 保持可达)。
/// **所有权契约(评审 Minor#4)**:实例由 FontLibrary 缓存持有,调用方不得 Dispose——
/// 误释放将在库存活期留下已死的 FT_Face/hb_font 悬垂指针。
/// SetPixelSize 缓存当前尺寸:尺寸不变不重设;尺寸变更在 FT_Set_Pixel_Sizes 后**显式调
/// hb_ft_font_changed** 通知 hb 重取尺度(hb_ft_font_create 创建期快照,FT 直改脸不触发 hb 侧
/// 变更检测——评审 Critical#1;hb_font 对象复用不重建)。
/// </summary>
internal sealed class FaceHandle : IDisposable
{
    public IntPtr FtFace { get; }      // FT_Face(FT_Set_Pixel_Sizes 已按最近请求尺寸设置)
    public IntPtr HbFont { get; }      // hb_ft_font_create(FtFace)

    readonly GCHandle _dataPin;
    int _pixelSize;                    // 0 = 未设(FT_Get_Char_Index 无需尺寸;首次 SetPixelSize 必调)
    bool _disposed;

    internal FaceHandle(IntPtr ftFace, IntPtr hbFont, GCHandle dataPin)
    {
        FtFace = ftFace;
        HbFont = hbFont;
        _dataPin = dataPin;
    }

    /// <summary>设置像素尺寸(pixelWidth=0 由 pixelHeight 推导等宽);尺寸变化才调 FT_Set_Pixel_Sizes,
    /// 成功后显式 hb_ft_font_changed 同步 hb 尺度(评审 Critical#1:FT 直改不通知 hb,创建期快照会陈旧)。</summary>
    public void SetPixelSize(int px)
    {
        if (px <= 0) throw new ArgumentOutOfRangeException(nameof(px), px, "pixel size must be positive");
        if (px == _pixelSize) return;
        int err = FreeTypeNative.FT_Set_Pixel_Sizes(FtFace, 0, (uint)px);
        if (err != 0) throw new InvalidOperationException($"FT_Set_Pixel_Sizes(0,{px}) failed: {err}");
        HarfBuzzNative.hb_ft_font_changed(HbFont);   // 重取 face 尺度并失效 advance cache(hb_font 复用)
        _pixelSize = px;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HarfBuzzNative.hb_font_destroy(HbFont);   // hb 先于 face:hb_ft_font 内部引用 FT_Face
        _ = FreeTypeNative.FT_Done_Face(FtFace);
        _dataPin.Free();                          // 内存脸数据此后无人引用,方可解除钉住
    }
}
