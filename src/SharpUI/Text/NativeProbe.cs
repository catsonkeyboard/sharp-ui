using System.Runtime.InteropServices;

namespace SharpUI.Text;

/// <summary>文本层依赖探针(D3-T0,T1 起为薄壳):FreeType/HarfBuzz 可用性/版本/最小渲染管线验证。
/// 全部 P/Invoke 与结构偏移已并入声明层(FreeTypeNative/HarfBuzzNative),本类零 DllImport、
/// 仅保留探针编排与断言,供 TextNativeProbeTests 使用;偏移单源见 FreeTypeNative 常量
/// (C offsetof 对 vendored 头文件实测,出处与推导注释见声明层)。</summary>
internal static class NativeProbe
{
    /// <summary>FreeType 运行库版本(经 FT_Library_Version,须先 Init 取句柄)。</summary>
    internal static (int Major, int Minor, int Patch) FreeTypeVersion()
    {
        int err = FreeTypeNative.FT_Init_FreeType(out IntPtr lib);
        if (err != 0 || lib == IntPtr.Zero)
            throw new InvalidOperationException($"FT_Init_FreeType failed: {err}");
        try
        {
            FreeTypeNative.FT_Library_Version(lib, out int major, out int minor, out int patch);
            return (major, minor, patch);
        }
        finally
        {
            _ = FreeTypeNative.FT_Done_FreeType(lib);
        }
    }

    /// <summary>HarfBuzz 运行库版本串("8.5.0" 形);字符串/数字两 API 交叉校验防串错 DLL。</summary>
    internal static string HarfBuzzVersion()
    {
        string s = Marshal.PtrToStringAnsi(HarfBuzzNative.hb_version_string()) ?? "";
        HarfBuzzNative.hb_version(out int major, out int minor, out int micro);
        if (!s.StartsWith($"{major}.{minor}.", StringComparison.Ordinal))
            throw new InvalidOperationException($"hb_version mismatch: '{s}' vs {major}.{minor}.{micro}");
        return s;
    }

    /// <summary>最小渲染管线探针:New_Face → Set_Pixel_Sizes(0,16) → Get_Char_Index('c')≠0 →
    /// Load_Glyph(DEFAULT) → Render_Glyph(NORMAL) → bitmap.rows&gt;0 且 buffer≠NULL;
    /// 顺带 hb_ft_font_create(face) 桥烟测(D3 后续任务的硬依赖,失败即探针红)。</summary>
    internal static bool RenderProbe(string fontPath, char c)
    {
        int err = FreeTypeNative.FT_Init_FreeType(out IntPtr lib);
        if (err != 0 || lib == IntPtr.Zero) return false;
        try
        {
            if (FreeTypeNative.FT_New_Face(lib, fontPath, 0, out IntPtr face) != 0 || face == IntPtr.Zero) return false;
            try
            {
                if (FreeTypeNative.FT_Set_Pixel_Sizes(face, 0, 16) != 0) return false;
                uint glyphIndex = FreeTypeNative.FT_Get_Char_Index(face, c);
                if (glyphIndex == 0) return false;
                if (FreeTypeNative.FT_Load_Glyph(face, glyphIndex, FreeTypeNative.LoadDefault) != 0) return false;

                IntPtr hbFont = HarfBuzzNative.hb_ft_font_create(face, IntPtr.Zero);   // hb-ft 桥烟测
                if (hbFont == IntPtr.Zero) return false;
                HarfBuzzNative.hb_font_destroy(hbFont);

                IntPtr slot = Marshal.ReadIntPtr(face, FreeTypeNative.FaceGlyphOffset);
                if (FreeTypeNative.FT_Render_Glyph(slot, FreeTypeNative.RenderModeNormal) != 0) return false;
                var bmp = Marshal.PtrToStructure<FreeTypeNative.BitmapRec>(IntPtr.Add(slot, FreeTypeNative.SlotBitmapOffset));
                return bmp.Rows > 0 && bmp.Width > 0 && bmp.Buffer != IntPtr.Zero;
            }
            finally
            {
                _ = FreeTypeNative.FT_Done_Face(face);
            }
        }
        finally
        {
            _ = FreeTypeNative.FT_Done_FreeType(lib);
        }
    }
}
