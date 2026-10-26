using System.Runtime.InteropServices;

namespace SharpUI.Text;

/// <summary>
/// D3 文本层 Task 1:HarfBuzz 8.5.0 原生声明层(零 unsafe、NativeAOT 兼容;cdecl)。
/// vendored harfbuzz.dll 由本机 VS2022 编译自官方 tarball 且 HB_HAVE_FREETYPE=ON 直链配对
/// freetype.dll(源/哈希/dumpbin 证据见 native/VERSIONS.md;hb_ft_font_create=ordinal 239)。
/// 结构按 hb-buffer.h(hb_glyph_info_t L62-72 / hb_glyph_position_t L191-199)对 tarball 头文件树
/// 逐字段 C offsetof/sizeof 实测:两结构各 20B(8.5.0 中 cluster 为独立 public 字段、position 尾部
/// 有 var 联合;简报 16B 系 HB 2.x 前旧布局),经 TextNativeTests Marshal.SizeOf/OffsetOf 自检锁定。
/// 原型出处行号:hb-version.h L79-85、hb-buffer.h L253/L259/L269/L343/L538-542/L576/L581/L585、
/// hb-font.h L1001-1002、hb-shape.h L43-47、hb-ft.h L101-103、hb-common.h L94-138(类型定义)。
/// </summary>
internal static class HarfBuzzNative
{
    internal const string Lib = "harfbuzz.dll";

    static HarfBuzzNative()
    {
        // 预载兜底覆盖测试宿主 runtimes/<rid>/native 布局(依赖 freetype.dll,FreeTypeNative 静态构造先载)
        try
        {
            if (!NativeLibrary.TryLoad(Lib, typeof(HarfBuzzNative).Assembly, null, out _))
                NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", Lib), out _);
        }
        catch { /* 预载失败交给 DllImport 自身解析路径报错 */ }
    }

    // ---- 结构体(hb-buffer.h 原序;hb-common.h 类型映射:hb_codepoint_t/hb_mask_t→uint、
    //      hb_position_t→int(26.6 定点)、hb_var_int_t(4B 联合)→uint) ----

    /// <summary>hb_glyph_info_t(hb-buffer.h L62-72)实测 sizeof=20:codepoint@0/mask@4/cluster@8/var1@12/var2@16。
    /// cluster 为 2.x 起的独立 public 字段(hb_buffer_add_utf16 语义:初值=item_offset+序号,即 UTF-16 下标);
    /// 简报"Cluster 并入 Var1"系 8.5.0 之前旧布局。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphInfo
    {
        public uint Codepoint;   // @0  shape 后为字形 ID(FT_Get_Char_Index 同源)
        public uint Mask;        // @4
        public uint Cluster;     // @8  UTF-16 下标(hb_buffer_add_utf16;直接字段,非 var1 别名)
        public uint Var1;        // @12 hb_var_int_t(OT 内部用)
        public uint Var2;        // @16 hb_var_int_t
    }

    /// <summary>hb_glyph_position_t(hb-buffer.h L191-199)实测 sizeof=20:4×hb_position_t(int32,26.6 定点)
    /// +var@16(变轴联合,本层不用;简报 16B 系无 var 的旧布局)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphPosition
    {
        public int XAdvance;     // @0  26.6
        public int YAdvance;     // @4  26.6(水平文本恒 0)
        public int XOffset;      // @8  26.6(标记/组合字形偏移)
        public int YOffset;      // @12 26.6
        public int Var;          // @16 hb_var_int_t
    }

    // ---- P/Invoke(cdecl;导出经 dumpbin 验证见 native/VERSIONS.md) ----

    /// <summary>hb_version(hb-version.h L79-82):三段指针输出。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_version(out int major, out int minor, out int micro);

    /// <summary>hb_version_string(hb-version.h L84-85):返回静态串指针(无需释放);与 hb_version 交叉校验防串错 DLL。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr hb_version_string();

    /// <summary>hb_buffer_create(hb-buffer.h L252-253):引用计数 1 的新 buffer;失败返回空表常量非 NULL。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr hb_buffer_create();

    /// <summary>hb_buffer_destroy(hb-buffer.h L268-269):引用计数归零才真正释放。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_buffer_destroy(IntPtr buffer);

    /// <summary>hb_buffer_reset(hb-buffer.h L258-259):清内容并还原全部属性(复用池路径,免重建)。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_buffer_reset(IntPtr buffer);

    /// <summary>hb_buffer_guess_segment_properties(hb-buffer.h L342-343):按已有内容猜脚本/方向/语言。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_buffer_guess_segment_properties(IntPtr buffer);

    /// <summary>hb_buffer_add_utf16(hb-buffer.h L537-542):text_length 为 UTF-16 码元数(不含代理项拆分,-1=NUL 结尾);
    /// item_offset/item_length 切出实际参与 shape 的段(item_length=-1 取到段尾)——简报"item_end"按头文件实为长度。
    /// cluster 初值 = item_offset+段内序号(item_offset=0 时即整串 UTF-16 下标)。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_buffer_add_utf16(IntPtr buffer, ushort[] text, int textLength, uint itemOffset, int itemLength);

    /// <summary>hb_buffer_get_length(hb-buffer.h L575-576):shape 后的字形数。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint hb_buffer_get_length(IntPtr buffer);

    /// <summary>hb_buffer_get_glyph_infos(hb-buffer.h L580-582):返回内部数组首址(buffer 存续期有效),
    /// length 出参=字形数;逐条 Marshal.PtrToStructure&lt;GlyphInfo&gt; 读(调用方不释放返回指针)。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr hb_buffer_get_glyph_infos(IntPtr buffer, out uint length);

    /// <summary>hb_buffer_get_glyph_positions(hb-buffer.h L584-586):同上,位置数组;须在 hb_shape 之后调用。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr hb_buffer_get_glyph_positions(IntPtr buffer, out uint length);

    /// <summary>hb_shape(hb-shape.h L42-47):features 传 IntPtr.Zero + numFeatures=0 即无特性。
    /// 8.5.0 原型返回 void(hb_shape_full 才返回 hb_bool_t)——简报"→bool"按头文件修正;
    /// 成功与否由调用侧以 buffer 长度/内容判断。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_shape(IntPtr font, IntPtr buffer, IntPtr features, uint numFeatures);

    /// <summary>hb_ft_font_create(hb-ft.h L101-103):包装既有 FT_Face 为 hb_font(度量/字形 ID 与 FT 同源);
    /// destroy 传 IntPtr.Zero(hb_font 不接管 face 生命周期,FontLibrary 各自管理);dumpbin 实证 ordinal 239。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr hb_ft_font_create(IntPtr ftFace, IntPtr destroy);

    /// <summary>hb_font_destroy(hb-font.h L1001-1002):引用计数归零才真正释放。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_font_destroy(IntPtr font);
}
