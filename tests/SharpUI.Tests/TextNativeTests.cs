using System.Runtime.InteropServices;
using SharpUI.Text;

namespace SharpUI.Tests;

/// <summary>
/// D3 文本层 Task 1:FT/HB 声明层结构布局自检(锁定 Marshal.SizeOf/OffsetOf 与 vendored 头文件的
/// x64 LLP64 原生布局一致)。预期值 = C offsetof/sizeof 程序对本仓构建所用头文件树逐字段实测:
///   FreeType 2.13.3(freetype.h/ftimage.h/fttypes.h)与 HarfBuzz 8.5.0(hb-buffer.h)。
/// LLP64 关键:sizeof(long)=4 → FT_Long/FT_Pos/FT_Fixed/FT_ULong 均 4 字节(非 LP64 的 8),
/// 简报原稿内联值(GlyphMetrics=64/Vector=16/GlyphSlotRec=256+/hb 结构=16)系 LP64 记忆误推,
/// 此处按头文件实测改判并逐条注释依据(同 D3D11NativeTests 先例:断言锁定"实现与推导一致")。
/// </summary>
public class TextNativeTests
{
    [Fact]
    public void NativeStructSizes_MatchHeaderLayouts()
    {
        // FreeType(freetype.h L444 GlyphMetrics:L444-457,8×FT_Pos;FT_Pos=signed long=4B,ftimage.h L57)
        Assert.Equal(32, Marshal.SizeOf<FreeTypeNative.GlyphMetrics>());  // 8×4=32(简报 64 系 LP64 误推)
        // FT_Bitmap(ftimage.h L271-282):rows4+width4+pitch4+pad4+buf8+gray2+pm1+pal1+pad4+pal8=40(T0 实测同)
        Assert.Equal(40, Marshal.SizeOf<FreeTypeNative.BitmapRec>());
        // FT_Vector(ftimage.h L75-80):2×FT_Pos=8(简报 16 系 LP64 误推)
        Assert.Equal(8, Marshal.SizeOf<FreeTypeNative.Vector>());
        // FT_Generic(fttypes.h L470-475):ptr+函数指针=16
        Assert.Equal(16, Marshal.SizeOf<FreeTypeNative.Generic>());
        // FT_BBox(ftimage.h L118-123):4×FT_Pos=16
        Assert.Equal(16, Marshal.SizeOf<FreeTypeNative.BBox>());
        // FT_Size_Metrics(freetype.h L1974-1987):2×ushort(2+2 无填充)+6×4B(FT_Fixed/FT_Pos)=28
        // (简报"XPpem/YPpem 各含 2 填充"误——两 ushort 相邻打包,无填充)
        Assert.Equal(28, Marshal.SizeOf<FreeTypeNative.SizeMetricsRec>());
        // FT_SizeRec(freetype.h L2011-2018):face8+generic16+metrics28+pad4+internal8=64
        Assert.Equal(64, Marshal.SizeOf<FreeTypeNative.SizeRec>());
        // FT_Outline(ftimage.h L347-358):2×ushort+pad4+3×ptr+int+尾pad4=40
        Assert.Equal(40, Marshal.SizeOf<FreeTypeNative.OutlineRec>());
        // FT_GlyphSlotRec(freetype.h L2264-2298):实测 248(简报 256+ 系 LP64 误推;LLP64 下
        // metrics=32/outline=40/control_len=4B 等,逐字段见 HotPathOffsets 测试)
        Assert.Equal(248, Marshal.SizeOf<FreeTypeNative.GlyphSlotRec>());
        // FT_FaceRec(freetype.h L1229-1283):实测 216
        Assert.Equal(216, Marshal.SizeOf<FreeTypeNative.FaceRec>());

        // HarfBuzz 8.5.0(hb-buffer.h L62-72/L191-199):GlyphInfo 五字段(codepoint/mask/cluster/var1/var2)、
        // GlyphPosition 五字段(4×hb_position_t+var)→ 各 20(简报 16 系 HB 2.x 之前旧布局:
        // cluster 曾并入 var1、position 无尾 var)
        Assert.Equal(20, Marshal.SizeOf<HarfBuzzNative.GlyphInfo>());
        Assert.Equal(20, Marshal.SizeOf<HarfBuzzNative.GlyphPosition>());
    }

    /// <summary>
    /// 热路径字段偏移自检:T0 探针经 C offsetof 实测的锚点(FaceRec.glyph=120、GlyphSlotRec.bitmap=104)
    /// 与本声明层全字段推导的逐字段偏移;并将零分配读路径的字面偏移常量与结构布局绑定(防漂移)。
    /// </summary>
    [Fact]
    public void HotPathOffsets_MatchHeaderLayouts()
    {
        // FaceRec(freetype.h L1229 起,LLP64):4×FT_Long(0..16)+pad+2×ptr(24..40)+int(40..44)+pad+
        // ptr(48..56)+int(56..60)+pad+ptr(64..72)+generic(72..88)+bbox(88..104)+8×FT_UShort/FT_Short
        // (104..120,units_per_EM 起)+glyph(120)★T0 实测锚点+size(128)+charmap(136)+…
        Assert.Equal(16, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.NumGlyphs)));
        Assert.Equal(104, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.UnitsPerEM)));
        Assert.Equal(106, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Ascender)));
        Assert.Equal(108, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Descender)));
        Assert.Equal(110, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Height)));
        Assert.Equal(120, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Glyph)));
        Assert.Equal(128, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Size)));

        // GlyphSlotRec(freetype.h L2264 起,LLP64):3×ptr+uint+pad+generic16(32..48)+metrics32(48..80)
        // +2×FT_Fixed(80..88)+advance8(88..96)+format4(96..100)+pad+bitmap40(104)★T0 实测锚点(104..144)
        // +bitmap_left(144)+bitmap_top(148)+outline40(152..192)+uint+pad+…
        Assert.Equal(48, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.Metrics)));
        Assert.Equal(88, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.Advance)));
        Assert.Equal(96, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.Format)));
        Assert.Equal(104, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.Bitmap)));
        Assert.Equal(144, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.BitmapLeft)));
        Assert.Equal(148, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.BitmapTop)));

        // SizeMetricsRec(freetype.h L1974 起):x_ppem@0/y_ppem@2/x_scale@4/y_scale@8/ascender@12/
        // descender@16/height@20/max_advance@24(全部 4B 粒度,无填充)
        Assert.Equal(0, (int)Marshal.OffsetOf<FreeTypeNative.SizeMetricsRec>(nameof(FreeTypeNative.SizeMetricsRec.XPpem)));
        Assert.Equal(4, (int)Marshal.OffsetOf<FreeTypeNative.SizeMetricsRec>(nameof(FreeTypeNative.SizeMetricsRec.XScale)));
        Assert.Equal(12, (int)Marshal.OffsetOf<FreeTypeNative.SizeMetricsRec>(nameof(FreeTypeNative.SizeMetricsRec.Ascender)));
        Assert.Equal(24, (int)Marshal.OffsetOf<FreeTypeNative.SizeMetricsRec>(nameof(FreeTypeNative.SizeMetricsRec.MaxAdvance)));

        // hb_glyph_info_t(hb-buffer.h L62-72):cluster 是独立 public 字段@8(2.x 起;旧版并入 var1)
        Assert.Equal(8, (int)Marshal.OffsetOf<HarfBuzzNative.GlyphInfo>(nameof(HarfBuzzNative.GlyphInfo.Cluster)));
        // hb_glyph_position_t(hb-buffer.h L191-199):4×int32 连续
        Assert.Equal(0, (int)Marshal.OffsetOf<HarfBuzzNative.GlyphPosition>(nameof(HarfBuzzNative.GlyphPosition.XAdvance)));
        Assert.Equal(12, (int)Marshal.OffsetOf<HarfBuzzNative.GlyphPosition>(nameof(HarfBuzzNative.GlyphPosition.YOffset)));

        // 零分配读路径的字面偏移常量与结构布局绑定(单源锁定)
        Assert.Equal(FreeTypeNative.FaceGlyphOffset, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Glyph)));
        Assert.Equal(FreeTypeNative.FaceSizeOffset, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Size)));
        Assert.Equal(FreeTypeNative.FaceNumGlyphsOffset, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.NumGlyphs)));
        Assert.Equal(FreeTypeNative.FaceUnitsPerEmOffset, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.UnitsPerEM)));
        Assert.Equal(FreeTypeNative.FaceAscenderOffset, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Ascender)));
        Assert.Equal(FreeTypeNative.FaceDescenderOffset, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Descender)));
        Assert.Equal(FreeTypeNative.FaceHeightOffset, (int)Marshal.OffsetOf<FreeTypeNative.FaceRec>(nameof(FreeTypeNative.FaceRec.Height)));
        Assert.Equal(FreeTypeNative.SlotBitmapOffset, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.Bitmap)));
        Assert.Equal(FreeTypeNative.SlotMetricsOffset, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.Metrics)));
        Assert.Equal(FreeTypeNative.SlotAdvanceOffset, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.Advance)));
        Assert.Equal(FreeTypeNative.SlotBitmapLeftOffset, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.BitmapLeft)));
        Assert.Equal(FreeTypeNative.SlotBitmapTopOffset, (int)Marshal.OffsetOf<FreeTypeNative.GlyphSlotRec>(nameof(FreeTypeNative.GlyphSlotRec.BitmapTop)));
    }

    // GlyphSlotRec/SizeMetricsRec/FaceRec 字段的运行时读取由 T0 探针测试(TextNativeProbeTests)继续覆盖(单源不重复)
}
