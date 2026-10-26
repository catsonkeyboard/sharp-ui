using System.Runtime.InteropServices;

namespace SharpUI.Text;

/// <summary>文本层依赖探针(D3-T0):FreeType/HarfBuzz 最小 P/Invoke。验证 vendored DLL 可加载、
/// 版本达标、segoeui.ttf 最小渲染管线 + hb-ft 桥可用。FT/HB 均 cdecl;x64 单调用约定,声明cdecl 防御。
/// 结构偏移按 freetype.h Windows(LLP64)布局——FT_Long/FT_Pos/FT_Fixed 均为 4 字节 long,
/// 经 C offsetof 程序对本仓 vendored 头文件实测(native/VERSIONS.md)。后续声明层任务可吸收本探针。</summary>
internal static class NativeProbe
{
    const string FtLib = "freetype.dll";
    const string HbLib = "harfbuzz.dll";

    // ---- freetype.h 偏移(LLP64 实测,sizeof(long)=4)----
    const int FaceGlyphOffset = 120;        // offsetof(FT_FaceRec, glyph):5×long(20)+2×ptr+pad+int+pad+ptr+int+pad+ptr+FT_Generic(16)+FT_BBox(4×long=16)+2+6×2+pad
    const int SlotBitmapOffset = 104;       // offsetof(FT_GlyphSlotRec, bitmap):3×ptr+uint+pad+FT_Generic(16)+FT_Glyph_Metrics(8×long=32)+2×FT_Fixed+FT_Vector+enum+pad
    const int BitmapRowsOffset = SlotBitmapOffset + 0;    // offsetof(FT_Bitmap, rows)   uint
    const int BitmapWidthOffset = SlotBitmapOffset + 4;   // offsetof(FT_Bitmap, width)  uint
    const int BitmapBufferOffset = SlotBitmapOffset + 16; // offsetof(FT_Bitmap, buffer) ptr(rows+width+pitch+pad 后 8 对齐)

    const int LoadDefault = 0;      // FT_LOAD_DEFAULT
    const int RenderModeNormal = 0; // FT_RENDER_MODE_NORMAL

    static NativeProbe()
    {
        // 预载兜底覆盖测试宿主 runtimes/<rid>/native 布局;先 freetype(harfbuzz.dll 静态依赖它)
        try
        {
            if (!NativeLibrary.TryLoad(FtLib, typeof(NativeProbe).Assembly, null, out _))
                NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", FtLib), out _);
            if (!NativeLibrary.TryLoad(HbLib, typeof(NativeProbe).Assembly, null, out _))
                NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", HbLib), out _);
        }
        catch { /* 预载失败交给 DllImport 自身解析路径报错 */ }
    }

    /// <summary>FreeType 运行库版本(经 FT_Library_Version,须先 Init 取句柄)。</summary>
    internal static (int Major, int Minor, int Patch) FreeTypeVersion()
    {
        int err = FT_Init_FreeType(out IntPtr lib);
        if (err != 0 || lib == IntPtr.Zero)
            throw new InvalidOperationException($"FT_Init_FreeType failed: {err}");
        try
        {
            FT_Library_Version(lib, out int major, out int minor, out int patch);
            return (major, minor, patch);
        }
        finally
        {
            _ = FT_Done_FreeType(lib);
        }
    }

    /// <summary>HarfBuzz 运行库版本串("8.5.0" 形);字符串/数字两 API 交叉校验防串错 DLL。</summary>
    internal static string HarfBuzzVersion()
    {
        string s = Marshal.PtrToStringAnsi(hb_version_string()) ?? "";
        hb_version(out int major, out int minor, out int micro);
        if (!s.StartsWith($"{major}.{minor}.", StringComparison.Ordinal))
            throw new InvalidOperationException($"hb_version mismatch: '{s}' vs {major}.{minor}.{micro}");
        return s;
    }

    /// <summary>最小渲染管线探针:New_Face → Set_Pixel_Sizes(0,16) → Get_Char_Index('c')≠0 →
    /// Load_Glyph(DEFAULT) → Render_Glyph(NORMAL) → bitmap.rows&gt;0 且 buffer≠NULL;
    /// 顺带 hb_ft_font_create(face) 桥烟测(D3 后续任务的硬依赖,失败即探针红)。</summary>
    internal static bool RenderProbe(string fontPath, char c)
    {
        int err = FT_Init_FreeType(out IntPtr lib);
        if (err != 0 || lib == IntPtr.Zero) return false;
        try
        {
            if (FT_New_Face(lib, fontPath, 0, out IntPtr face) != 0 || face == IntPtr.Zero) return false;
            try
            {
                if (FT_Set_Pixel_Sizes(face, 0, 16) != 0) return false;
                uint glyphIndex = FT_Get_Char_Index(face, c);
                if (glyphIndex == 0) return false;
                if (FT_Load_Glyph(face, glyphIndex, LoadDefault) != 0) return false;

                IntPtr hbFont = hb_ft_font_create(face, IntPtr.Zero);   // hb-ft 桥烟测
                if (hbFont == IntPtr.Zero) return false;
                hb_font_destroy(hbFont);

                IntPtr slot = Marshal.ReadIntPtr(face, FaceGlyphOffset);
                if (FT_Render_Glyph(slot, RenderModeNormal) != 0) return false;
                int rows = Marshal.ReadInt32(slot, BitmapRowsOffset);
                int width = Marshal.ReadInt32(slot, BitmapWidthOffset);
                IntPtr buffer = Marshal.ReadIntPtr(slot, BitmapBufferOffset);
                return rows > 0 && width > 0 && buffer != IntPtr.Zero;
            }
            finally
            {
                _ = FT_Done_Face(face);
            }
        }
        finally
        {
            _ = FT_Done_FreeType(lib);
        }
    }

    // ---- FreeType cdecl 导出(dumpbin 验证见 native/VERSIONS.md)----
    // 注意:FT_Long 在 Win64(LLP64)为 4 字节,C# 侧对应 int/uint。

    [DllImport(FtLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Init_FreeType(out IntPtr alibrary);

    [DllImport(FtLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void FT_Library_Version(IntPtr library, out int major, out int minor, out int patch);

    // 路径经系统代码页 fopen;非 ASCII 字体路径在 T2 注册表发现任务改走 FT_New_Memory_Face
    [DllImport(FtLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_New_Face(IntPtr library, string filepathname, int faceIndex, out IntPtr aface);

    [DllImport(FtLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Set_Pixel_Sizes(IntPtr face, uint pixelWidth, uint pixelHeight);

    [DllImport(FtLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint FT_Get_Char_Index(IntPtr face, uint charcode);

    [DllImport(FtLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Load_Glyph(IntPtr face, uint glyphIndex, int loadFlags);

    [DllImport(FtLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Render_Glyph(IntPtr slot, int renderMode);

    [DllImport(FtLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Done_Face(IntPtr face);

    [DllImport(FtLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int FT_Done_FreeType(IntPtr library);

    // ---- HarfBuzz cdecl 导出 ----

    [DllImport(HbLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_version_string();

    [DllImport(HbLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_version(out int major, out int minor, out int micro);

    [DllImport(HbLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_ft_font_create(IntPtr face, IntPtr destroy);   // hb-ft 桥:DLL 必须含此导出(vendored 8.5.0 ordinal 239)

    [DllImport(HbLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_font_destroy(IntPtr font);
}
