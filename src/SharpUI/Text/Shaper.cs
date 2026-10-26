using System.Runtime.InteropServices;

namespace SharpUI.Text;

/// <summary>
/// 单 run shaping 托管快照(不可变;T4 Measure/DrawRun 共用,命中缓存后不再进原生)。
/// 坐标均为<strong>逻辑像素</strong>:hb position 26.6 定点 → /64(物理 px)→ /scale(逻辑),
/// 与现状 TextAtlas 快照语义一致(逻辑=物理/_scale)。cluster 为 UTF-16 码元下标
/// (hb_buffer_add_utf16 item_offset=0 时 cluster=段内序号=整串 UTF-16 下标)——T4 直接
/// ci=Clusters[gi] 作字符下标用,旧 ClusterToCharIndex/Utf8Len 的 UTF-8 舞步整体删除的依据。
/// </summary>
internal sealed class ShapedRun
{
    /// <summary>hb shape 后字形 ID(cmap 同源;连字/置换后的真实字形,0=.notdef)。</summary>
    public readonly uint[] GlyphIds;

    /// <summary>UTF-16 码元下标(相对 run 起点;GlyphInfo.Cluster@8 直接读,非 var1 别名)。</summary>
    public readonly uint[] Clusters;

    /// <summary>字形原点(逻辑):x=笔位+x_offset,y=-y_offset(hb y 轴向上 → UI y 向下取负)。</summary>
    public readonly PointF[] Points;

    /// <summary>每字形逻辑 advance(x_advance/64/scale)。</summary>
    public readonly float[] Advances;

    /// <summary>总宽=Σ x_advance(逻辑;26.6 整型累加后一次换算,免浮点漂移)。</summary>
    public readonly float TotalWidth;

    internal ShapedRun(uint[] glyphIds, uint[] clusters, PointF[] points, float[] advances, float totalWidth)
    {
        GlyphIds = glyphIds;
        Clusters = clusters;
        Points = points;
        Advances = advances;
        TotalWidth = totalWidth;
    }
}

/// <summary>
/// D3 文本层 Task 3:单 run HarfBuzz shaping(hb_buffer_add_utf16,cluster=UTF-16 下标)。
/// hb_buffer 构造期建 1 个、每次 Shape 前 reset 复用(hb_buffer_reset 清内容+还原属性,
/// 池化路径免重建);hb_font 由 FontLibrary 每脸常驻(FaceHandle.HbFont,调用方不得 Dispose——
/// T2 所有权契约),尺度经 SetPixelSize 内的 hb_ft_font_changed 保持同步(评审 Critical#1),
/// 本层直接消费不再自行同步。Shape 是 T4 shaped 快照缓存未命中路径(非每帧热路径):
/// 允许逐字形 Marshal.PtrToStructure 快照分配,但不做可免的重复工作。
/// hb_shape 8.5.0 返回 void——成功与否由 buffer 长度体现(空串/全长丢弃→0 字形快照)。
/// 单线程 UI 模型;零 unsafe、AOT 兼容。
/// </summary>
internal sealed class Shaper : IDisposable
{
    const int PositionToPixel = 64;   // hb_position 26.6 定点:1px=64

    readonly FontLibrary _fonts;
    readonly IntPtr _buffer;
    bool _disposed;

    public Shaper(FontLibrary fonts)
    {
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        // hb_buffer_create 失败时返回不可变空表常量而非 NULL(此后调用静默 no-op)——
        // 无公开 API 可鉴别,OOM 路径以 shape 结果为空浮现,不在此预判
        _buffer = HarfBuzzNative.hb_buffer_create();
    }

    /// <summary>shape 单 run(同槽同粗体;pixelSize=物理 px,scale=物理/逻辑,快照坐标=逻辑)。</summary>
    public ShapedRun Shape(int slot, string run, int pixelSize, bool bold, float scale)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale), scale, "scale must be positive");
        FaceHandle face = _fonts.Face(slot, bold);
        face.SetPixelSize(pixelSize);   // 幂等(尺寸未变不重设;变更时库内已 hb_ft_font_changed 同步)
        if (run.Length == 0)
            return new ShapedRun([], [], [], [], 0f);

        HarfBuzzNative.hb_buffer_reset(_buffer);   // 池化复用:清内容+还原 script/direction/language
        var text = new ushort[run.Length];         // char→ushort(码元值相同,uint16_t* 传参;缓存未命中路径分配可接受)
        for (int i = 0; i < run.Length; i++) text[i] = run[i];
        // item_offset=0 + item_length=全长:整串即切片,cluster 初值=段内序号=UTF-16 码元下标
        HarfBuzzNative.hb_buffer_add_utf16(_buffer, text, text.Length, 0, text.Length);
        HarfBuzzNative.hb_buffer_guess_segment_properties(_buffer);   // 按内容猜 script/direction/LTR-RTL
        HarfBuzzNative.hb_shape(face.HbFont, _buffer, IntPtr.Zero, 0);   // 无 features(kern 等默认特性由 shaper 开启)

        // 返回指针指向 buffer 内部数组(buffer 存续期有效,调用方不释放);infos/positions 等长
        IntPtr infos = HarfBuzzNative.hb_buffer_get_glyph_infos(_buffer, out uint count);
        IntPtr positions = HarfBuzzNative.hb_buffer_get_glyph_positions(_buffer, out _);
        var glyphIds = new uint[count];
        var clusters = new uint[count];
        var points = new PointF[count];
        var advances = new float[count];
        int infoSize = Marshal.SizeOf<HarfBuzzNative.GlyphInfo>();
        int positionSize = Marshal.SizeOf<HarfBuzzNative.GlyphPosition>();
        float toLogical = 1f / (PositionToPixel * scale);   // 26.6→px→逻辑 合并一次换算
        long pen = 0;   // 26.6 整型累加(无浮点漂移;long 防超长 run 溢出 int)
        for (uint i = 0; i < count; i++)
        {
            var info = Marshal.PtrToStructure<HarfBuzzNative.GlyphInfo>(infos + (int)(i * infoSize));
            var pos = Marshal.PtrToStructure<HarfBuzzNative.GlyphPosition>(positions + (int)(i * positionSize));
            glyphIds[i] = info.Codepoint;   // shape 后 codepoint 字段=字形 ID(hb-buffer.h 语义)
            clusters[i] = info.Cluster;
            points[i] = new PointF((pen + pos.XOffset) * toLogical, -pos.YOffset * toLogical);
            advances[i] = pos.XAdvance * toLogical;
            pen += pos.XAdvance;
        }
        // count==0:非空输入被整体丢弃(hb 异常路径)→ 空快照如实呈现(TotalWidth=0),不抛
        return new ShapedRun(glyphIds, clusters, points, advances, pen * toLogical);
    }

    /// <summary>释放池化 hb_buffer;幂等(FaceHandle/FT 资源归 FontLibrary,此处只释放自有 buffer)。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HarfBuzzNative.hb_buffer_destroy(_buffer);
    }
}
