using System.Runtime.InteropServices;

namespace SharpUI.Text;

/// <summary>
/// FT 光栅化结果快照(不可变)。度量均为<strong>物理像素</strong>(与 pixelSize 同单位):
/// T4 消费时再 /scale 转逻辑(快照语义同现状 TextAtlas Glyph:位图物理、外层换算)。
/// Rgba 为 RGBA 预乘白:FT A8 覆盖 cov → R=G=B=A=cov(白源 255 × alpha=cov,预乘即 RGB=cov;
/// 直线型写法 (255,255,255,cov) 仅用于理解,入库必须预乘——图集 SKAlphaType.Premul 同约定)。
/// BearingLeft/BearingTop=bitmap_left/bitmap_top:笔位原点到位图左/上边(物理 px,top 以基线
/// 向上为正)。Advance=metrics.horiAdvance(26.6/64,物理 px)——空字形(空格类)位图 W=H=0
/// 但推进量仍在。
/// </summary>
internal readonly record struct RasterGlyph(
    byte[] Rgba, int Width, int Height, float BearingLeft, float BearingTop, float Advance);

/// <summary>
/// D3 文本层 Task 3:字形位图光栅化。FT_Load_Glyph(FT_LOAD_DEFAULT,hint 后度量)→
/// FT_Render_Glyph(FT_RENDER_MODE_NORMAL → FT_PIXEL_MODE_GRAY 8bpp 抗锯齿)→ 逐行读 A8 覆盖
/// 展开为 RGBA 预乘白 byte[]。脸/句柄生命周期全归 FontLibrary(FaceHandle 调用方不得 Dispose);
/// Rasterize 为 T4 字形缓存未命中路径(非每帧热路径),GlyphSlotRec 结构体单读(同
/// FontLibrary.LineMetrics 的 SizeRec 单读纪律)。空字形(FT_GLYPH_FORMAT_NONE:空格类无轮廓)
/// 跳过渲染直接返回 W=H=0 快照 + horiAdvance 推进量(hmtx 仍在,FT_Render_Glyph 对无轮廓槽位
/// 报 Cannot_Render_Glyph,先判格式免无谓错误路径)。零 unsafe、AOT 兼容;单线程 UI 模型。
/// </summary>
internal sealed class GlyphRasterizer : IDisposable
{
    const uint GlyphFormatNone = 0;   // FT_GLYPH_FORMAT_NONE(ftimage.h L320):加载后无轮廓亦无内嵌位图

    readonly FontLibrary _fonts;

    public GlyphRasterizer(FontLibrary fonts) =>
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));

    /// <summary>光栅化单码点(pixelSize=物理 px;结果度量亦物理 px,T4 换算逻辑)。</summary>
    public RasterGlyph Rasterize(int slot, uint codepoint, int pixelSize, bool bold)
    {
        FaceHandle face = _fonts.Face(slot, bold);
        face.SetPixelSize(pixelSize);   // 幂等(尺寸未变不重设)
        uint glyphIndex = FreeTypeNative.FT_Get_Char_Index(face.FtFace, codepoint);
        int err = FreeTypeNative.FT_Load_Glyph(face.FtFace, glyphIndex, FreeTypeNative.LoadDefault);
        if (err != 0)
            throw new InvalidOperationException($"FT_Load_Glyph(U+{codepoint:X4}, gi={glyphIndex}) failed: {err}");

        // face->glyph(FaceGlyphOffset=120,T0 实测锚点);GlyphSlotRec 结构体单读(冷路径免逐字段偏移)
        IntPtr slotPtr = Marshal.ReadIntPtr(face.FtFace, FreeTypeNative.FaceGlyphOffset);
        var slotRec = Marshal.PtrToStructure<FreeTypeNative.GlyphSlotRec>(slotPtr);
        // advance 由 Load 填充(hmtx 网格拟合),26.6→物理 px;空字形亦有效(布局推进不因无位图消失)
        float advancePx = slotRec.Metrics.HoriAdvance / 64f;
        if (slotRec.Format == GlyphFormatNone)
            return new RasterGlyph(Array.Empty<byte>(), 0, 0, 0f, 0f, advancePx);   // 空格类:无轮廓免渲染

        err = FreeTypeNative.FT_Render_Glyph(slotPtr, FreeTypeNative.RenderModeNormal);
        if (err != 0)
            throw new InvalidOperationException($"FT_Render_Glyph(U+{codepoint:X4}) failed: {err}");
        slotRec = Marshal.PtrToStructure<FreeTypeNative.GlyphSlotRec>(slotPtr);   // 渲染直写原生槽位,重读
        var bitmap = slotRec.Bitmap;
        if (bitmap.Rows == 0 || bitmap.Width == 0)
            return new RasterGlyph(Array.Empty<byte>(), 0, 0, 0f, 0f, advancePx);  // 空轮廓渲染后仍空
        if (bitmap.PixelMode != FreeTypeNative.PixelModeGray)
            throw new InvalidOperationException(
                $"unexpected pixel mode {bitmap.PixelMode} for U+{codepoint:X4} (expect GRAY=2)");

        int rows = (int)bitmap.Rows, width = (int)bitmap.Width;
        var rgba = new byte[checked(rows * width * 4)];
        // A8 → RGBA 预乘白:行行展开(buffer 恒指内存首行;pitch 为带符号行跨度,负值自底向上布局
        // 时 row*pitch 步进依旧正确)。白源 255 × cov/255 = cov → 四通道同值
        for (int row = 0; row < rows; row++)
        {
            IntPtr srcRow = bitmap.Buffer + row * bitmap.Pitch;
            int dst = row * width * 4;
            for (int x = 0; x < width; x++)
            {
                byte cov = Marshal.ReadByte(srcRow, x);
                rgba[dst++] = cov;   // R(预乘=白 255×cov/255)
                rgba[dst++] = cov;   // G
                rgba[dst++] = cov;   // B
                rgba[dst++] = cov;   // A(FT A8 覆盖原值)
            }
        }
        return new RasterGlyph(rgba, width, rows, slotRec.BitmapLeft, slotRec.BitmapTop, advancePx);
    }

    /// <summary>Dispose 接口对称空操作(原生资源 FT_Library/Face 全归 FontLibrary 持有,本层无自有句柄);
    /// 无状态天然幂等——留接口为实现细节留白(若后续引入自有原生资源在此释放)。</summary>
    public void Dispose() { }
}
