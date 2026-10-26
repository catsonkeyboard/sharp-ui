using System.Runtime.InteropServices;

namespace SharpUI.Text;

/// <summary>
/// D3 文本层 Task 1:FreeType 2.13.3 原生声明层(零 unsafe、NativeAOT 兼容;cdecl,与 Clipboard.cs 同风格)。
/// vendored freetype.dll 由本机 VS2022 编译自官方 tarball(源/哈希见 native/VERSIONS.md),全部结构/偏移/
/// 常量对 tarball 头文件树(freetype-2.13.3/include)在 x64 LLP64 下逐字段 C offsetof/sizeof 实测:
/// sizeof(long)=4、sizeof(void*)=8 → FT_Long/FT_ULong/FT_Pos/FT_Fixed 均 4 字节(非 LP64 的 8,手算易错)。
/// 关键锚点与 T0 探针实测互证:offsetof(FT_FaceRec,glyph)=120、offsetof(FT_GlyphSlotRec,bitmap)=104、
/// sizeof(FT_Bitmap)=40。尺寸/偏移经 TextNativeTests Marshal.SizeOf/OffsetOf 自检锁定(同 D3D11Native 纪律)。
/// 头文件出处行号:freetype.h L444(GlyphMetrics)/L1229(FaceRec)/L1974(Size_Metrics)/L2011(SizeRec)/
/// L2264(GlyphSlotRec)/L3498-3524(FT_LOAD_*)/L3614-3620(FT_LOAD_TARGET_*)/L3787(Render_Mode)/
/// L3943(Kerning_Mode);ftimage.h L57(FT_Pos)/L75(Vector)/L118(BBox)/L180-193(Pixel_Mode)/
/// L271(Bitmap)/L347(Outline);fttypes.h L438/L470(Generic)/L582(ListRec)。
/// </summary>
internal static class FreeTypeNative
{
    internal const string Lib = "freetype.dll";

    static FreeTypeNative()
    {
        // 预载兜底覆盖测试宿主 runtimes/<rid>/native 布局(harfbuzz.dll 静态依赖 freetype.dll,须先载它)
        try
        {
            if (!NativeLibrary.TryLoad(Lib, typeof(FreeTypeNative).Assembly, null, out _))
                NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", Lib), out _);
        }
        catch { /* 预载失败交给 DllImport 自身解析路径报错 */ }
    }

    // ---- 零分配读路径的字面偏移(经 TextNativeTests 与 Marshal.OffsetOf 绑定自检) ----

    internal const int FaceNumGlyphsOffset = 16;    // offsetof(FT_FaceRec, num_glyphs):4×FT_Long(0..16)
    internal const int FaceUnitsPerEmOffset = 104;  // offsetof(FT_FaceRec, units_per_EM):bbox(88..104)后首字段
    internal const int FaceAscenderOffset = 106;    // units_per_EM 后连续 8×FT_Short/FT_UShort(各 2B)
    internal const int FaceDescenderOffset = 108;
    internal const int FaceHeightOffset = 110;
    internal const int FaceGlyphOffset = 120;       // ★T0 C offsetof 实测:5×FT_Long(0..20)+pad(20..24)+
                                                    //  2×ptr(24..40)+int(40..44)+pad+ptr(48..56)+int(56..60)+
                                                    //  pad+ptr(64..72)+FT_Generic(72..88)+FT_BBox(88..104)+
                                                    //  bbox 后 8×FT_UShort/FT_Short(104..120,units_per_EM 起)
    internal const int FaceSizeOffset = 128;        // glyph(120..128)后
    internal const int SlotMetricsOffset = 48;      // offsetof(FT_GlyphSlotRec, metrics):3×ptr+uint+pad+
                                                    //  FT_Generic(32..48)
    internal const int SlotAdvanceOffset = 88;      // metrics32(48..80)+2×FT_Fixed(80..88)
    internal const int SlotBitmapOffset = 104;      // ★T0 C offsetof 实测:advance8(88..96)+format4(96..100)+pad
    internal const int SlotBitmapLeftOffset = 144;  // bitmap40(104..144)后
    internal const int SlotBitmapTopOffset = 148;

    // ---- 常量(freetype.h L3498-3524 FT_LOAD_* 公开宏实数;ADVANCE_ONLY/SVG_ONLY 标注"驱动内部用",不收录) ----

    internal const int LoadDefault = 0x0;                 // FT_LOAD_DEFAULT
    internal const int LoadNoScale = 0x1;                 // FT_LOAD_NO_SCALE(1L<<0)
    internal const int LoadNoHinting = 0x2;               // FT_LOAD_NO_HINTING(1L<<1)
    internal const int LoadRender = 0x4;                  // FT_LOAD_RENDER(1L<<2)
    internal const int LoadNoBitmap = 0x8;                // FT_LOAD_NO_BITMAP(1L<<3)
    internal const int LoadVerticalLayout = 0x10;         // FT_LOAD_VERTICAL_LAYOUT(1L<<4)
    internal const int LoadForceAutohint = 0x20;          // FT_LOAD_FORCE_AUTOHINT(1L<<5)
    internal const int LoadCropBitmap = 0x40;             // FT_LOAD_CROP_BITMAP(1L<<6)
    internal const int LoadPedantic = 0x80;               // FT_LOAD_PEDANTIC(1L<<7)
    internal const int LoadIgnoreGlobalAdvanceWidth = 0x200;  // FT_LOAD_IGNORE_GLOBAL_ADVANCE_WIDTH(1L<<9,已弃用)
    internal const int LoadNoRecurse = 0x400;             // FT_LOAD_NO_RECURSE(1L<<10)
    internal const int LoadIgnoreTransform = 0x800;       // FT_LOAD_IGNORE_TRANSFORM(1L<<11)
    internal const int LoadMonochrome = 0x1000;           // FT_LOAD_MONOCHROME(1L<<12)
    internal const int LoadLinearDesign = 0x2000;         // FT_LOAD_LINEAR_DESIGN(1L<<13)
    internal const int LoadSbitsOnly = 0x4000;            // FT_LOAD_SBITS_ONLY(1L<<14)
    internal const int LoadNoAutohint = 0x8000;           // FT_LOAD_NO_AUTOHINT(1L<<15)
    internal const int LoadColor = 0x100000;              // FT_LOAD_COLOR(1L<<20;彩色 emoji 预留,spec 范围外)
    internal const int LoadComputeMetrics = 0x200000;     // FT_LOAD_COMPUTE_METRICS(1L<<21)
    internal const int LoadBitmapMetricsOnly = 0x400000;  // FT_LOAD_BITMAP_METRICS_ONLY(1L<<22)
    internal const int LoadNoSvg = 0x1000000;             // FT_LOAD_NO_SVG(1L<<24)
    // FT_LOAD_TARGET_*(freetype.h L3614-3620):((mode & 15) << 16),mode 取 FT_Render_Mode 枚举实数
    internal const int LoadTargetNormal = 0x0;            // FT_LOAD_TARGET_NORMAL(0<<16)
    internal const int LoadTargetLight = 0x10000;         // FT_LOAD_TARGET_LIGHT(1<<16)
    internal const int LoadTargetMono = 0x20000;          // FT_LOAD_TARGET_MONO(2<<16)
    internal const int LoadTargetLcd = 0x30000;           // FT_LOAD_TARGET_LCD(3<<16)
    internal const int LoadTargetLcdV = 0x40000;          // FT_LOAD_TARGET_LCD_V(4<<16)

    // FT_Render_Mode(freetype.h L3787-3798 枚举实数)
    internal const int RenderModeNormal = 0;   // FT_RENDER_MODE_NORMAL(A8 抗锯齿,T3 光栅化用)
    internal const int RenderModeLight = 1;    // FT_RENDER_MODE_LIGHT
    internal const int RenderModeMono = 2;     // FT_RENDER_MODE_MONO
    internal const int RenderModeLcd = 3;      // FT_RENDER_MODE_LCD
    internal const int RenderModeLcdV = 4;     // FT_RENDER_MODE_LCD_V
    internal const int RenderModeSdf = 5;      // FT_RENDER_MODE_SDF

    // FT_Kerning_Mode(freetype.h L3943-3949 枚举实数)
    internal const int KerningDefault = 0;     // FT_KERNING_DEFAULT(网格拟合,26.6)
    internal const int KerningUnfitted = 1;    // FT_KERNING_UNFITTED
    internal const int KerningUnscaled = 2;    // FT_KERNING_UNSCALED(字面单位)

    // FT_Pixel_Mode(ftimage.h L180-193 枚举实数;T3 读 bitmap.pixel_mode 分派)
    internal const int PixelModeNone = 0;      // FT_PIXEL_MODE_NONE
    internal const int PixelModeMono = 1;      // FT_PIXEL_MODE_MONO(1bpp)
    internal const int PixelModeGray = 2;      // FT_PIXEL_MODE_GRAY(8bpp 抗锯齿)
    internal const int PixelModeGray2 = 3;     // FT_PIXEL_MODE_GRAY2(已弃用)
    internal const int PixelModeGray4 = 4;     // FT_PIXEL_MODE_GRAY4(已弃用)
    internal const int PixelModeLcd = 5;       // FT_PIXEL_MODE_LCD
    internal const int PixelModeLcdV = 6;      // FT_PIXEL_MODE_LCD_V
    internal const int PixelModeBgra = 7;      // FT_PIXEL_MODE_BGRA(FT_LOAD_COLOR 产物)

    // ---- 结构体(freetype.h/ftimage.h/fttypes.h 原序;LLP64:x64 原生 sizeof 经 TextNativeTests 锁定) ----
    // LLP64 类型映射:FT_Long/FT_Int/FT_Error/FT_Pos(26.6)/FT_Fixed(16.16)/FT_Int32 → int;
    // FT_ULong/FT_UInt → uint;FT_Short/FT_UShort → short/ushort;指针/句柄 → IntPtr。

    /// <summary>FT_Glyph_Metrics(freetype.h L444-457):8×FT_Pos(4B)=32。26.6 定点。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphMetrics
    {
        public int Width, Height;                 // @0/@4
        public int HoriBearingX, HoriBearingY, HoriAdvance;      // @8/@12/@16
        public int VertBearingX, VertBearingY, VertAdvance;      // @20/@24/@28
    }

    /// <summary>FT_Vector(ftimage.h L75-80):2×FT_Pos=8。26.6 定点(kerning 输出/slot.advance)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Vector
    {
        public int X, Y;                          // @0/@4
    }

    /// <summary>FT_BBox(ftimage.h L118-123):4×FT_Pos=16(FaceRec.bbox 成员)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BBox
    {
        public int XMin, YMin, XMax, YMax;        // @0/@4/@8/@12
    }

    /// <summary>FT_Generic(fttypes.h L470-475):data 指针+finalizer 函数指针=16(FaceRec/SizeRec/SlotRec 成员)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Generic
    {
        public IntPtr Data;                       // @0
        public IntPtr Finalizer;                  // @8  FT_Generic_Finalizer(void*) 函数指针
    }

    /// <summary>FT_Bitmap(ftimage.h L271-282)实测 sizeof=40:rows@0/width@4/pitch@8/buffer@16(12..16 对齐填充)/
    /// num_grays@24/pixel_mode@26/palette_mode@27/palette@32(28..32 对齐填充)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapRec
    {
        public uint Rows;                         // @0
        public uint Width;                        // @4
        public int Pitch;                         // @8  行跨度(字节;位图可能为负)
        public IntPtr Buffer;                     // @16 指向像素数据(FT 管理,face 存续期有效)
        public ushort NumGrays;                   // @24
        public byte PixelMode;                    // @26  FT_Pixel_Mode(Gray=2)
        public byte PaletteMode;                  // @27
        public IntPtr Palette;                    // @32
    }

    /// <summary>FT_Size_Metrics(freetype.h L1974-1987)实测 sizeof=28:2×ushort 紧邻(无填充,简报"各含 2 填充"误)
    /// +6×4B(x_scale/y_scale 为 FT_Fixed 16.16;ascender/descender/height/max_advance 为 FT_Pos 26.6)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SizeMetricsRec
    {
        public ushort XPpem, YPpem;               // @0/@2
        public int XScale, YScale;                // @4/@8   16.16
        public int Ascender, Descender, Height, MaxAdvance;  // @12/@16/@20/@24  26.6(行度量 T-3 数据源)
    }

    /// <summary>FT_SizeRec(freetype.h L2011-2018)实测 sizeof=64:face8+generic16+metrics28+pad4(52..56)+internal8。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SizeRec
    {
        public IntPtr Face;                       // @0
        public Generic Generic;                   // @8
        public SizeMetricsRec Metrics;            // @24 (24..52)
        public IntPtr Internal;                   // @56
    }

    /// <summary>FT_Outline(ftimage.h L347-358)实测 sizeof=40:2×ushort+pad4(4..8)+3×ptr+flags4+尾pad4
    /// (GlyphSlotRec.outline 成员;仅占位保布局,本层不解轮廓)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct OutlineRec
    {
        public ushort NContours, NPoints;         // @0/@2
        public IntPtr Points;                     // @8   FT_Vector*
        public IntPtr Tags;                       // @16  FT_Byte*
        public IntPtr Contours;                   // @24  FT_Contour*(=short*)
        public int Flags;                         // @32  FT_OUTLINE_XXX 掩码
    }

    /// <summary>FT_ListRec(fttypes.h L582-586):head/tail 双指针=16(FaceRec.sizes_list 成员)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ListRec
    {
        public IntPtr Head, Tail;                 // @0/@8
    }

    /// <summary>FT_GlyphSlotRec(freetype.h L2264-2298)实测 sizeof=248(LLP64;简报 256+ 系 LP64 误推)。
    /// 逐字段:library0/face8/next16/glyph_index24(28..32 填充)/generic32..48/metrics48..80(32B)/
    /// linearHoriAdvance80/linearVertAdvance84/advance88..96/format96..100(100..104 填充)/bitmap104..144★/
    /// bitmap_left144/bitmap_top148/outline152..192(40B)/num_subglyphs192(196..200 填充)/subglyphs200/
    /// control_data208/control_len216(FT_Long→4B)/lsb_delta220/rsb_delta224(228..232 填充)/other232/internal240。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphSlotRec
    {
        public IntPtr Library;                    // @0   FT_Library
        public IntPtr Face;                       // @8   FT_Face
        public IntPtr Next;                       // @16  FT_GlyphSlot
        public uint GlyphIndex;                   // @24
        public Generic Generic;                   // @32
        public GlyphMetrics Metrics;              // @48  实测锚点(26.6)
        public int LinearHoriAdvance;             // @80  FT_Fixed 16.16
        public int LinearVertAdvance;             // @84  FT_Fixed 16.16
        public Vector Advance;                    // @88  实测锚点(26.6)
        public uint Format;                       // @96  FT_Glyph_Format 枚举(4B)
        public BitmapRec Bitmap;                  // @104 ★T0 实测锚点
        public int BitmapLeft;                    // @144
        public int BitmapTop;                     // @148
        public OutlineRec Outline;                // @152
        public uint NumSubglyphs;                 // @192
        public IntPtr Subglyphs;                  // @200
        public IntPtr ControlData;                // @208
        public int ControlLen;                    // @216 FT_Long(LLP64 4B)
        public int LsbDelta;                      // @220 FT_Pos
        public int RsbDelta;                      // @224 FT_Pos
        public IntPtr Other;                      // @232
        public IntPtr Internal;                   // @240
    }

    /// <summary>FT_FaceRec(freetype.h L1229-1283)实测 sizeof=216。逐字段:num_faces0/face_index4/face_flags8/
    /// style_flags12/num_glyphs16(20..24 填充)/family_name24/style_name32/num_fixed_sizes40(44..48 填充)/
    /// available_sizes48/num_charmaps56(60..64 填充)/charmaps64/generic72..88/bbox88..104/units_per_EM104/
    /// ascender106/descender108/height110/max_advance_width112/max_advance_height114/underline_position116/
    /// underline_thickness118/glyph120★/size128/charmap136/driver144/memory152/stream160/sizes_list168..184/
    /// autohint184..200/extensions200/internal208。粗体字段=文本层热读取路径。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FaceRec
    {
        public int NumFaces;                      // @0   FT_Long
        public int FaceIndex;                     // @4   FT_Long
        public int FaceFlags;                     // @8   FT_Long(FT_FACE_FLAG_XXX)
        public int StyleFlags;                    // @12  FT_Long(FT_STYLE_FLAG_XXX)
        public int NumGlyphs;                     // @16  实测锚点
        public IntPtr FamilyName;                 // @24  FT_String*(代码页字节串)
        public IntPtr StyleName;                  // @32  FT_String*
        public int NumFixedSizes;                 // @40  FT_Int
        public IntPtr AvailableSizes;             // @48  FT_Bitmap_Size*
        public int NumCharmaps;                   // @56  FT_Int
        public IntPtr Charmaps;                   // @64  FT_CharMap*
        public Generic Generic;                   // @72
        public BBox Bbox;                         // @88  字面单位(FaceRec 单位,非像素)
        public ushort UnitsPerEM;                 // @104 实测锚点(字面单位/em)
        public short Ascender;                    // @106 实测锚点(freetype.h:font units 字面单位;SFNT 源 hhea)
        public short Descender;                   // @108 实测锚点(基线下为负)
        public short Height;                      // @110 实测锚点(两基线距,恒正)
        public short MaxAdvanceWidth;             // @112
        public short MaxAdvanceHeight;            // @114
        public short UnderlinePosition;           // @116
        public short UnderlineThickness;          // @118
        public IntPtr Glyph;                      // @120 ★T0 C offsetof 实测锚点(FT_GlyphSlot)
        public IntPtr Size;                       // @128 实测锚点(FT_Size)
        public IntPtr Charmap;                    // @136 FT_CharMap
        public IntPtr Driver;                     // @144
        public IntPtr Memory;                     // @152
        public IntPtr Stream;                     // @160
        public ListRec SizesList;                 // @168
        public Generic Autohint;                  // @184
        public IntPtr Extensions;                 // @200
        public IntPtr Internal;                   // @208
    }

    // ---- P/Invoke(原型出处 freetype.h 行号;cdecl x64;导出经 dumpbin 验证见 native/VERSIONS.md) ----

    /// <summary>FT_Init_FreeType(freetype.h L2350-2351)→ FT_Error(0=成功)。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FT_Init_FreeType(out IntPtr library);

    /// <summary>FT_Done_FreeType(freetype.h L2370-2371)。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FT_Done_FreeType(IntPtr library);

    /// <summary>FT_New_Face(freetype.h L2574-2577)。face_index 为 FT_Long(LLP64 4B → int);
    /// 路径经系统代码页 fopen,非 ASCII 字体路径在 T2 注册表发现任务改走 FT_New_Memory_Face。</summary>
    [DllImport(Lib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FT_New_Face(IntPtr library, string filepathname, int faceIndex, out IntPtr face);

    /// <summary>FT_Done_Face(freetype.h L2879)。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FT_Done_Face(IntPtr face);

    /// <summary>FT_Set_Pixel_Sizes(freetype.h L3176-3178):pixelWidth=0 表示由 pixelHeight 推导等宽。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FT_Set_Pixel_Sizes(IntPtr face, uint pixelWidth, uint pixelHeight);

    /// <summary>FT_Get_Char_Index(freetype.h L4194-4195):charcode 为 FT_ULong(LLP64 4B → uint);0=缺字形。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint FT_Get_Char_Index(IntPtr face, uint charcode);

    /// <summary>FT_Load_Glyph(freetype.h L3231-3233):loadFlags 为 FT_Int32(→int,常量见 Load*)。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FT_Load_Glyph(IntPtr face, uint glyphIndex, int loadFlags);

    /// <summary>FT_Render_Glyph(freetype.h L3911-3912):slot 为 FT_GlyphSlot 指针(从 face.glyph 偏移读出,
    /// 渲染直写原生槽位内存;简报"ref GlyphSlotRec"拼写按 C 原型指针语义改 IntPtr)。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FT_Render_Glyph(IntPtr slot, int renderMode);

    /// <summary>FT_Get_Kerning(freetype.h L4018-4022):kern_mode 见 Kerning* 常量;输出 26.6 定点。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int FT_Get_Kerning(IntPtr face, uint leftGlyph, uint rightGlyph, uint kernMode, out Vector kerning);

    /// <summary>FT_Library_Version(freetype.h L5213-5216):须传入已初始化的 FT_Library。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FT_Library_Version(IntPtr library, out int major, out int minor, out int patch);
}
