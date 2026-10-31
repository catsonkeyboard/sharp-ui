using SharpUI.Text;

namespace SharpUI;

/// <summary>
/// 文本图集 v3(D3 T4):内部全量自研 FreeType/HarfBuzz(T2 FontLibrary/T3 Shaper+GlyphRasterizer),
/// 零第三方文本栈依赖(FT/HB 直连);公共 API(Measure/DrawRun/WhiteUv/Dispose)与 v2 不变。快照缓存语义承袭 D21/D22:
/// 回退链分 run、shaping 结果快照缓存(LRU 512 粗粒度逐出,Measure/DrawRun 共用)、字形位图缓存
/// (键 (slot, glyphId, size, bold)——按**字形 ID** 而非码点:hb 默认开 liga,连字经 GlyphIds 出
/// 真实字形而无码点可反查;亦修复 v2 连字按簇首字符渲染的潜在瑕疵)。坐标约定:Shaper/Rasterizer
/// 快照物理 px ÷ scale 得逻辑(同 v2);行度量 = FT hinted Á/g 墨迹并集(v2 "Ág" 语义的 FT 复刻,
/// 逐值零漂移——T-3 原 FT_SizeMetrics 方案会整体下移基线 ~4px@20px,实证修正,见 GetLineMetrics)。
/// 落位复刻 v2 怪癖:ink-tight 位图 quad 左缘 = 笔位(见 DrawRun 注)。金样本 6/6 平价验证
/// (tests/golden/text)。单线程 UI 模型;绘制路径命中缓存零原生调用。
/// </summary>
public sealed class TextAtlas : IDisposable
{
    readonly IRenderContext _ctx;   // M3e 接口化:后端中立消费面(UpdateAtlasRegion/AtlasSize);GLContext 同契约
    readonly float _scale;
    readonly FontLibrary _fonts;                        // T2:槽位脸表(0=Segoe UI,1..=回退)
    readonly Shaper _shaper;                            // T3:hb shaping → 托管快照
    readonly GlyphRasterizer _rasterizer;               // T3:FT 光栅化(按字形 ID 入口)
    readonly Dictionary<(int Slot, uint GlyphId, int Size, bool Bold), Glyph> _glyphs = new();
    readonly Dictionary<(string Text, int Size, bool Bold), ShapedLayout> _shapedCache = new();   // shaping 快照 LRU(D22 粗粒度逐出)
    const int ShapedCacheCapacity = 512;
    readonly Dictionary<int, (float Ascent, float Descent)> _lineMetrics = new();   // (size<<1)|bold
    readonly ShelfPacker _packer;

    public RectF WhiteUv { get; private set; }

    public TextAtlas(IRenderContext ctx, float scale = 1f, string[]? fallbackFamilies = null)
    {
        _ctx = ctx;
        _scale = scale;
        _fonts = new FontLibrary(fallbackFamilies);   // null → FontLibrary 平台默认回退链(Windows 现值不变;X-2)
        _shaper = new Shaper(_fonts);
        _rasterizer = new GlyphRasterizer(_fonts);
        _packer = new ShelfPacker((int)_ctx.AtlasSize, (int)_ctx.AtlasSize);
        SeedWhitePixel();
    }

    void SeedWhitePixel()
    {
        (int x, int y) = _packer.TryAlloc(1, 1)!.Value;
        _ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, (uint)x, (uint)y, 1, 1);
        WhiteUv = new RectF(
            x / (float)_ctx.AtlasSize, y / (float)_ctx.AtlasSize,
            1f / (float)_ctx.AtlasSize, 1f / (float)_ctx.AtlasSize);
    }

    int SlotFor(uint codepoint)
    {
        for (int i = 0; i < _fonts.SlotCount; i++)
            if (_fonts.ContainsGlyph(i, codepoint)) return i;
        return 0;   // 链上皆缺(如未注册码点)→ 槽 0 出 .notdef(v2 同现状)
    }

    static int UnitLen(string s, int i) =>
        char.IsHighSurrogate(s, i) && i + 1 < s.Length && char.IsLowSurrogate(s, i + 1) ? 2 : 1;

    static uint CodePoint(string s, int i) =>
        UnitLen(s, i) == 2 ? (uint)char.ConvertToUtf32(s, i) : s[i];

    /// <summary>行度量(物理 px):FT hinted Á/g 墨迹并集(GlyphRasterizer.InkLineMetrics)——
    /// 与 v2 旧栈 MeasureText("Ág") bounds 逐值相等(12/18/20/24px+bold 实证),基线与 Measure 高
    /// 零漂移;FT_SizeMetrics(hhea)会下移基线 ~4px@20px 致金样本溢阈(T-3 决策落地时的实证修正)。
    /// Descent 存正(= v2 bounds.Bottom 语义),Measure/DrawRun 消费一致。</summary>
    (float Ascent, float Descent) GetLineMetrics(int size, bool bold)
    {
        int key = (size << 1) | (bold ? 1 : 0);
        if (!_lineMetrics.TryGetValue(key, out var lm))
        {
            lm = _rasterizer.InkLineMetrics(0, size, bold);
            _lineMetrics[key] = lm;
        }
        return lm;
    }

    /// <summary>按槽位分 run(连续同槽字符一段;槽解析 = FontLibrary.ContainsGlyph 链查)。</summary>
    List<(int Slot, int Start, int Len)> SplitRuns(string text)
    {
        var runs = new List<(int, int, int)>();
        int i = 0, runStart = 0;
        int runSlot = SlotFor(CodePoint(text, 0));
        while (i < text.Length)
        {
            int ul = UnitLen(text, i);
            int slot = SlotFor(CodePoint(text, i));
            if (slot != runSlot)
            {
                runs.Add((runSlot, runStart, i - runStart));
                runSlot = slot;
                runStart = i;
            }
            i += ul;
        }
        runs.Add((runSlot, runStart, text.Length - runStart));
        return runs;
    }

    public SizeF Measure(string text, float fontSize, bool bold = false)
    {
        if (text.Length == 0) return SizeF.Zero;
        int size = SizeKey(fontSize, _scale);
        (float ascent, float descent) = GetLineMetrics(size, bold);
        ShapedLayout layout = GetShaped(text, size, bold);
        return new SizeF(layout.TotalWidth, (ascent + descent) / _scale);
    }

    public void DrawRun(MeshBuilder mesh, string text, PointF topLeft, float fontSize, bool bold, Color32 color)
    {
        if (text.Length == 0) return;   // 审计修复波 I-2:空串提前返回(与 Measure 对称)——SplitRuns 首行
                                        // CodePoint(text,0) 对空串越界抛(空 Label MenuItem 打开即崩)
        int size = SizeKey(fontSize, _scale);
        (float ascent, _) = GetLineMetrics(size, bold);
        float xLogical = topLeft.X;
        float baselineY = topLeft.Y + ascent / _scale;
        ShapedLayout layout = GetShaped(text, size, bold);   // 命中缓存即不再进原生
        foreach (ShapedLayoutRun run in layout.Runs)
        {
            ShapedRun r = run.Run;
            for (int gi = 0; gi < r.GlyphIds.Length; gi++)
            {
                Glyph g = GetGlyph(run.Slot, r.GlyphIds[gi], size, bold);
                if (g.Width == 0f || g.Height == 0f) continue;   // 空格类无位图,由 Shaper advance 推进
                // 落位(v2 平价,T4 实证修正):v2 把 ink-tight 位图 quad 左缘置于**笔位**(未加 bounds.Left,
                // DrawText(-bounds.Left) 烘的位图 ink 恒在本地 0 列)——j(left=-2)因此右置 2px、e/h(left=1)
                // 左置 1px。FT 位图同 ink-tight,quad 左缘=笔位逐字形复刻该落位;若按"笔位+bitmap_left"
                // 排版正解放,几乎全部小写整体偏 1px,金样本溢阈(实码为准,非简报理想式)。
                float gx = xLogical + r.Points[gi].X;               // Points.X 已含 hb x_offset(逻辑)
                float top = baselineY - g.BearingTop / _scale;      // bitmap_top 基线向上为正
                mesh.AddQuad(new RectF(gx, top, g.Width / _scale, g.Height / _scale), g.Uv, color);
            }
            xLogical += r.TotalWidth;
        }
    }

    /// <summary>shaping 结果缓存(D21/D22):Measure/DrawRun 共用;未命中才 SplitRuns+Shape+快照。</summary>
    ShapedLayout GetShaped(string text, int size, bool bold)
    {
        var key = (text, size, bold);
        if (!_shapedCache.TryGetValue(key, out ShapedLayout? layout))
        {
            layout = ShapeLayout(text, size, bold);
            _shapedCache[key] = layout;
            if (_shapedCache.Count > ShapedCacheCapacity)
                _shapedCache.Clear();   // D22 粗粒度逐出:超容量全清(条目为纯托管快照,全清代价低于 LRU 链表维护;下次访问自然重建)
        }
        return layout;
    }

    ShapedLayout ShapeLayout(string text, int size, bool bold)
    {
        var runs = new List<ShapedLayoutRun>();
        float total = 0f;
        foreach ((int slot, int start, int len) in SplitRuns(text))
        {
            // size=物理 px(SizeKey),Shaper 内 26.6→px→逻辑(/scale)一次换算
            ShapedRun shaped = _shaper.Shape(slot, text.Substring(start, len), size, bold, _scale);
            runs.Add(new ShapedLayoutRun(slot, shaped));
            total += shaped.TotalWidth;
        }
        return new ShapedLayout(runs.ToArray(), total);
    }

    static int SizeKey(float fontSize, float scale = 1f) => Math.Max(4, (int)MathF.Ceiling(fontSize * scale));

    Glyph GetGlyph(int slot, uint glyphId, int size, bool bold)
    {
        var key = (slot, glyphId, size, bold);
        if (_glyphs.TryGetValue(key, out Glyph glyph)) return glyph;
        glyph = Rasterize(slot, glyphId, size, bold);
        _glyphs[key] = glyph;
        return glyph;
    }

    Glyph Rasterize(int slot, uint glyphId, int size, bool bold)
    {
        RasterGlyph g = _rasterizer.RasterizeGlyphId(slot, glyphId, size, bold);
        if (g.Width == 0 || g.Height == 0)
            return Glyph.Empty;   // 空字形(空格类):不占图集、不出 quad(推进量已在 shaping 快照)
        (int X, int Y)? slotPx = _packer.TryAlloc(g.Width + 1, g.Height + 1);   // +1 padding:缩放采样防邻字形渗色(v2 同)
        if (slotPx is null)
        {
            _packer.Reset();
            _glyphs.Clear();
            SeedWhitePixel();
            slotPx = _packer.TryAlloc(g.Width + 1, g.Height + 1)
                ?? throw new InvalidOperationException($"字形 gi={glyphId}@{size} 放不进图集");
        }
        _ctx.UpdateAtlasRegion(g.Rgba, (uint)slotPx.Value.X, (uint)slotPx.Value.Y, (uint)g.Width, (uint)g.Height);
        return new Glyph(
            new RectF(slotPx.Value.X / (float)_ctx.AtlasSize,
                      slotPx.Value.Y / (float)_ctx.AtlasSize,
                      g.Width / (float)_ctx.AtlasSize,
                      g.Height / (float)_ctx.AtlasSize),
            g.Width, g.Height, g.BearingTop);
    }

    public void Dispose()
    {
        _shaper.Dispose();
        _rasterizer.Dispose();   // 无自有原生句柄(接口对称;FT 资源归 FontLibrary)
        _fonts.Dispose();
    }

    /// <summary>图集字形快照:Width/Height/BearingTop 均物理 px(光栅化同单位),DrawRun 落位时 ÷scale;
    /// 空字形哨兵 W=H=0(advance 不缓存——布局推进由 shaping 快照持有)。无 left bearing:位图 ink-tight,
    /// v2 平价落位 quad 左缘=笔位(见 DrawRun 注),RasterGlyph.BearingLeft(T3)在此路径不消费。</summary>
    readonly record struct Glyph(RectF Uv, float Width, float Height, float BearingTop)
    {
        public static readonly Glyph Empty = new(new RectF(0f, 0f, 0f, 0f), 0f, 0f, 0f);
    }

    /// <summary>单 run 的布局快照:槽位 + T3 Shaper 快照(字形 ID/原点/簇,逻辑坐标);缓存命中直接复用。</summary>
    sealed record ShapedLayoutRun(int Slot, ShapedRun Run);

    /// <summary>整行 shaping 快照(D21):逻辑总宽 + 各 run;条目不可变,缓存命中直接复用。</summary>
    sealed class ShapedLayout
    {
        public readonly ShapedLayoutRun[] Runs;
        public readonly float TotalWidth;       // 逻辑

        public ShapedLayout(ShapedLayoutRun[] runs, float totalWidth)
        {
            Runs = runs; TotalWidth = totalWidth;
        }
    }
}
