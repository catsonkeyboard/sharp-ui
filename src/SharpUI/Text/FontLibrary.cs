using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SharpUI.Text;

/// <summary>族名→字体文件路径(spec:Windows 注册表实现,Linux/macOS 后续里程碑)。</summary>
internal interface IFontLocator
{
    /// <summary>解析族名(含粗体族如 "Segoe UI Bold");失败返回 null。
    /// bold=先查 "&lt;family&gt; Bold",查不到回退 "&lt;family&gt;"(与 TextAtlas.BoldOf 回退语义一致)。</summary>
    string? Locate(string familyName, bool bold);
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

    public string? Locate(string familyName, bool bold)
    {
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
/// D3 文本层 Task 2:FT_Library + 槽位脸表生命周期。槽 0 = "Segoe UI"(spec 固定,同 TextAtlas),
/// 1.. = 回退族(注册表解析或文件加载失败并入槽 0,同现状 tf ?? _typefaces[0]);每脸配 hb_ft_font
/// (hb-ft 内建 face 尺度变化检测,度量随脸自动同步,SetPixelSize 后无需重建 hb_font)。
/// 脸加载统一 FT_New_Memory_Face(T0 携带项):文件字节读入托管数组 + GCHandle 钉住传指针——
/// FT_New_Face 路径参数走 ANSI fopen,非 ASCII 字体路径会因代码页失败;FT 不拷贝入参内存
/// (freetype.h 明文须存活至 FT_Done_Face),钉住句柄由 FaceHandle 持有并在释放脸之后 Free。
/// 粗体语义与 TextAtlas.BoldOf 一致:槽 0 粗体=注册表独立粗体文件(查不到回落主脸句柄);
/// 回退槽粗体=同脸(粗体回退为可接受折衷,D21)。单线程 UI 模型;构造期分配自由,绘制路径零分配由 T3/T4 保证。
/// </summary>
internal sealed class FontLibrary : IDisposable
{
    /// <summary>槽 0 主族名(spec 固定;T4 TextAtlas 重写时消费)。</summary>
    public const string PrimaryFamily = "Segoe UI";

    readonly IntPtr _ftLib;
    readonly string?[] _files;                     // null = 解析/加载失败 → 并入槽 0
    readonly Dictionary<int, FaceHandle> _faces = new();   // 槽 → 常规脸(懒建)
    readonly IFontLocator _locator = new WindowsFontLocator();
    FaceHandle? _boldFace;                         // 槽 0 粗体独立脸(懒建;null+已解析 = 回落主脸)
    bool _boldResolved;
    bool _disposed;

    /// <summary>槽 0 = Segoe UI,fallbackFamilies 依次入槽 1..;主族解析/加载失败即抛(库不可用)。</summary>
    public FontLibrary(string[] fallbackFamilies)
    {
        ArgumentNullException.ThrowIfNull(fallbackFamilies);
        int err = FreeTypeNative.FT_Init_FreeType(out _ftLib);
        if (err != 0 || _ftLib == IntPtr.Zero)
            throw new InvalidOperationException($"FT_Init_FreeType failed: {err}");
        _files = new string?[1 + fallbackFamilies.Length];
        _files[0] = ResolveExisting(PrimaryFamily, bold: false)
            ?? throw new InvalidOperationException($"primary font family '{PrimaryFamily}' not resolvable via font registry");
        for (int i = 0; i < fallbackFamilies.Length; i++)
            _files[1 + i] = ResolveExisting(fallbackFamilies[i], bold: false);   // null → 并入槽 0
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
    /// 调用方按 (slot,size,bold) 自缓存(同现状 GetLineMetrics;本方法非每帧热路径)。</summary>
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
            face = LoadFace(_files[slot]!);
            _faces[slot] = face;
        }
        return face;
    }

    FaceHandle BoldFace()
    {
        if (_boldFace is null && !_boldResolved)
        {
            _boldResolved = true;
            string? boldFile = ResolveExisting(PrimaryFamily, bold: true);   // Locate 内部已含回落主脸值
            // 回落主脸(路径同槽 0)→ 共用常规脸句柄,BoldOf "查不到回落主脸"语义
            if (boldFile is not null && !PathEquals(boldFile, _files[0]!))
                _boldFace = LoadFace(boldFile);
        }
        return _boldFace ?? RegularFace(0);
    }

    /// <summary>注册表解析 + 文件存在性核验(值指向已卸载字体时判失败并入槽 0)。</summary>
    string? ResolveExisting(string familyName, bool bold)
    {
        string? path = _locator.Locate(familyName, bold);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>FT_New_Memory_Face 统一加载(face index 0:TTC 如 msyh.ttc 取首个 face=常规字重)。
    /// 钉住的托管字节在 FT_Done_Face 前不得释放(freetype.h 明文),由 FaceHandle 持有句柄兜底。</summary>
    FaceHandle LoadFace(string file)
    {
        byte[] data = File.ReadAllBytes(file);   // 托管读取按 UTF-16 文件名:非 ASCII 路径安全(T0 携带项)
        GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        int err = FreeTypeNative.FT_New_Memory_Face(_ftLib, pin.AddrOfPinnedObject(), data.Length, 0, out IntPtr ftFace);
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
/// GCHandle 亦使 byte[] 保持可达)。SetPixelSize 缓存当前尺寸:尺寸不变不重设(hb-ft 有 face 尺度
/// 变化检测,度量随脸自动同步,无需重建 hb_font)。
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

    /// <summary>设置像素尺寸(pixelWidth=0 由 pixelHeight 推导等宽);尺寸变化才调 FT_Set_Pixel_Sizes。</summary>
    public void SetPixelSize(int px)
    {
        if (px <= 0) throw new ArgumentOutOfRangeException(nameof(px), px, "pixel size must be positive");
        if (px == _pixelSize) return;
        int err = FreeTypeNative.FT_Set_Pixel_Sizes(FtFace, 0, (uint)px);
        if (err != 0) throw new InvalidOperationException($"FT_Set_Pixel_Sizes(0,{px}) failed: {err}");
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
