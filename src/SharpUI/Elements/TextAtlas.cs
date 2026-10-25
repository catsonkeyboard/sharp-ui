using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace SharpUI;

/// <summary>文本图集 v2(D21):回退链分 run + HarfBuzz shaping 定位;shaping 结果快照缓存(T4,Measure/DrawRun 共用);字形位图 per-char 缓存(键含槽位)。</summary>
public sealed class TextAtlas : IDisposable
{
    readonly GraphicsContext _ctx;
    readonly float _scale;
    readonly SKTypeface[] _typefaces;                    // 槽 0 = 主字体,1.. = 回退
    readonly SKShaper[] _shapers;
    readonly SKPaint _paint = new() { Color = SKColors.White, IsAntialias = true };
    readonly SKFont[] _probeFonts;                       // ContainsGlyph 探针(槽级,字号无关)
    readonly Dictionary<int, SKFont> _fonts = new();     // ((slot<<12)|size)<<1|bold,size<4096
    readonly Dictionary<(string Unit, int Size, bool Bold, int Slot), Glyph> _glyphs = new();   // Unit=单 BMP 字符或代理对(emoji)
    readonly Dictionary<(string Text, int Size, bool Bold), ShapedLayout> _shapedCache = new();   // shaping 快照 LRU(D22 粗粒度逐出)
    const int ShapedCacheCapacity = 512;
    readonly Dictionary<int, (float Ascent, float Descent)> _lineMetrics = new();
    readonly ShelfPacker _packer;
    readonly SKBitmap _bitmap;
    readonly SKCanvas _canvas;

    public RectF WhiteUv { get; private set; }

    public TextAtlas(GraphicsContext ctx, float scale = 1f, string[]? fallbackFamilies = null)
    {
        _ctx = ctx;
        _scale = scale;
        string[] families = fallbackFamilies ?? new[] { "Microsoft YaHei", "Segoe UI Emoji" };
        _typefaces = new SKTypeface[1 + families.Length];
        _shapers = new SKShaper[_typefaces.Length];
        _probeFonts = new SKFont[_typefaces.Length];
        _typefaces[0] = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        for (int i = 0; i < families.Length; i++)
        {
            SKTypeface? tf = SKTypeface.FromFamilyName(families[i]);
            _typefaces[1 + i] = tf ?? _typefaces[0];
        }
        for (int i = 0; i < _typefaces.Length; i++)
        {
            _shapers[i] = new SKShaper(_typefaces[i]);
            _probeFonts[i] = new SKFont(_typefaces[i], 16f);
        }
        _packer = new ShelfPacker(GraphicsContext.AtlasSize, GraphicsContext.AtlasSize);
        _bitmap = new SKBitmap(new SKImageInfo(
            GraphicsContext.AtlasSize, GraphicsContext.AtlasSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        _canvas = new SKCanvas(_bitmap);
        _canvas.Clear(SKColors.Transparent);
        _canvas.Flush();
        SeedWhitePixel();
    }

    void SeedWhitePixel()
    {
        (int x, int y) = _packer.TryAlloc(1, 1)!.Value;
        _ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, (uint)x, (uint)y, 1, 1);
        WhiteUv = new RectF(
            x / (float)GraphicsContext.AtlasSize, y / (float)GraphicsContext.AtlasSize,
            1f / (float)GraphicsContext.AtlasSize, 1f / (float)GraphicsContext.AtlasSize);
    }

    int SlotFor(uint codepoint)
    {
        for (int i = 0; i < _probeFonts.Length; i++)
            if (_probeFonts[i].ContainsGlyph((int)codepoint))
                return i;
        return 0;
    }

    static int UnitLen(string s, int i) =>
        char.IsHighSurrogate(s, i) && i + 1 < s.Length && char.IsLowSurrogate(s, i + 1) ? 2 : 1;

    SKFont GetFont(int slot, int size, bool bold)
    {
        int key = ((slot << 12 | size) << 1) | (bold ? 1 : 0);
        if (!_fonts.TryGetValue(key, out SKFont? font))
        {
            font = new SKFont(bold ? BoldOf(slot) : _typefaces[slot], size)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = false,
            };
            _fonts[key] = font;
        }
        return font;
    }

    SKTypeface BoldOf(int slot)
    {
        // 主字体粗体取既有路径;回退槽粗体简化为同型face(粗体回退为可接受折衷,D21)
        return slot == 0 ? (_boldTf ??= SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? _typefaces[0]) : _typefaces[slot];
    }

    SKTypeface? _boldTf;

    (float Ascent, float Descent) GetLineMetrics(int size, bool bold)
    {
        int key = (size << 1) | (bold ? 1 : 0);
        if (!_lineMetrics.TryGetValue(key, out var lm))
        {
            GetFont(0, size, bold).MeasureText("Ág", out SKRect bounds, _paint);
            lm = (-bounds.Top, bounds.Bottom);
            _lineMetrics[key] = lm;
        }
        return lm;
    }

    /// <summary>按槽位分 run(连续同槽字符一段)。</summary>
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

    static uint CodePoint(string s, int i) =>
        UnitLen(s, i) == 2 ? (uint)char.ConvertToUtf32(s, i) : s[i];

    /// <summary>HarfBuzz cluster 是 run 内 UTF-8 字节偏移;转 UTF-16 单位起点下标。</summary>
    static int ClusterToCharIndex(string run, uint cluster)
    {
        int b = 0;
        for (int i = 0; i < run.Length; i += UnitLen(run, i))
        {
            if (b >= cluster) return i;
            b += Utf8Len(CodePoint(run, i));
        }
        return run.Length - 1;
    }

    static int Utf8Len(uint cp) => cp < 0x80u ? 1 : cp < 0x800u ? 2 : cp < 0x10000u ? 3 : 4;

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
        int size = SizeKey(fontSize, _scale);
        (float ascent, _) = GetLineMetrics(size, bold);
        float xLogical = topLeft.X;
        float baselineY = topLeft.Y + ascent / _scale;
        ShapedLayout layout = GetShaped(text, size, bold);   // 命中缓存即不再调 shaper
        foreach (ShapedRun r in layout.Runs)
        {
            string run = r.Text;
            for (int gi = 0; gi < r.Codepoints.Length; gi++)
            {
                int ci = ClusterToCharIndex(run, r.Clusters[gi]);   // cluster=run 内 UTF-8 字节偏移,非字符下标(ASCII 时两者重合)
                int ul = UnitLen(run, ci);
                if (run[ci] != ' ')
                {
                    Glyph g = GetGlyph(run.Substring(ci, ul), size, bold, r.Slot);
                    float gx = xLogical + r.Points[gi].X;   // 快照已存逻辑坐标(物理/_scale)
                    float top = baselineY - g.BearingY / _scale;
                    mesh.AddQuad(new RectF(gx, top, g.Width / _scale, g.Height / _scale), g.Uv, color);
                }
            }
            xLogical += r.Width;
        }
    }

    /// <summary>shaping 结果缓存(T4):Measure/DrawRun 共用;未命中才 SplitRuns+Shape+快照。</summary>
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
        var runs = new List<ShapedRun>();
        float total = 0f;
        foreach ((int slot, int start, int len) in SplitRuns(text))
        {
            string run = text.Substring(start, len);
            SKShaper.Result r = _shapers[slot].Shape(run, 0f, 0f, GetFont(slot, size, bold));   // 逻辑→物理:x*scale
            var points = new SKPoint[r.Codepoints.Length];   // Points 快照转逻辑坐标(物理/_scale),DrawRun 直接消费
            for (int i = 0; i < points.Length; i++)
                points[i] = new SKPoint(r.Points[i].X / _scale, r.Points[i].Y / _scale);
            float width = r.Width / _scale;
            total += width;
            runs.Add(new ShapedRun(run, slot, (uint[])r.Codepoints.Clone(), points, (uint[])r.Clusters.Clone(), width));
        }
        return new ShapedLayout(runs.ToArray(), total);
    }

    static int SizeKey(float fontSize, float scale = 1f) => Math.Max(4, (int)MathF.Ceiling(fontSize * scale));

    Glyph GetGlyph(string unit, int size, bool bold, int slot)
    {
        if (_glyphs.TryGetValue((unit, size, bold, slot), out Glyph glyph)) return glyph;
        glyph = Rasterize(unit, size, bold, slot);
        _glyphs[(unit, size, bold, slot)] = glyph;
        return glyph;
    }

    Glyph Rasterize(string unit, int size, bool bold, int slot)
    {
        SKFont font = GetFont(slot, size, bold);
        string s = unit;
        float advance = font.MeasureText(s, out SKRect bounds, _paint);
        int width = Math.Max(1, (int)MathF.Ceiling(bounds.Right - bounds.Left));
        int height = Math.Max(1, (int)MathF.Ceiling(bounds.Bottom - bounds.Top));
        (int X, int Y)? slotPx = _packer.TryAlloc(width + 1, height + 1);
        if (slotPx is null)
        {
            _packer.Reset();
            _glyphs.Clear();
            SeedWhitePixel();
            slotPx = _packer.TryAlloc(width + 1, height + 1)
                ?? throw new InvalidOperationException($"字形 {unit}@{size} 放不进图集");
        }
        using var gb = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var gc = new SKCanvas(gb);
        gc.Clear(SKColors.Transparent);
        gc.DrawText(s, -bounds.Left, -bounds.Top, SKTextAlign.Left, font, _paint);   // 非废弃重载,CS0618 清偿
        gc.Flush();
        _ctx.UpdateAtlasRegion(gb.GetPixelSpan().ToArray(), (uint)slotPx.Value.X, (uint)slotPx.Value.Y, (uint)width, (uint)height);
        return new Glyph(
            new RectF(slotPx.Value.X / (float)GraphicsContext.AtlasSize,
                      slotPx.Value.Y / (float)GraphicsContext.AtlasSize,
                      width / (float)GraphicsContext.AtlasSize,
                      height / (float)GraphicsContext.AtlasSize),
            width, height, -bounds.Top, advance);
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _bitmap.Dispose();
        foreach (SKFont f in _fonts.Values) f.Dispose();
        foreach (SKFont f in _probeFonts) f.Dispose();
        foreach (SKShaper s in _shapers) s.Dispose();
        _paint.Dispose();
        // 粗体 face 单独持有,BoldOf 可能回落到槽 0
        if (_boldTf is not null && !ReferenceEquals(_boldTf, _typefaces[0])) _boldTf.Dispose();
        for (int i = _typefaces.Length - 1; i >= 1; i--)
            if (!ReferenceEquals(_typefaces[i], _typefaces[0])) _typefaces[i].Dispose();
        _typefaces[0].Dispose();
    }

    readonly record struct Glyph(RectF Uv, float Width, float Height, float BearingY, float Advance);

    /// <summary>单 run 的 shaping 快照(T4):坐标已转逻辑(物理/_scale);含 glyph 循环所需全部数据,DrawRun 命中缓存后不再调 shaper。</summary>
    sealed class ShapedRun
    {
        public readonly string Text;            // run 原文(ClusterToCharIndex/UnitLen/GetGlyph 键)
        public readonly int Slot;
        public readonly uint[] Codepoints;
        public readonly SKPoint[] Points;       // 逻辑坐标(快照时已除 _scale)
        public readonly uint[] Clusters;        // run 内 UTF-8 字节偏移
        public readonly float Width;            // 逻辑宽

        public ShapedRun(string text, int slot, uint[] codepoints, SKPoint[] points, uint[] clusters, float width)
        {
            Text = text; Slot = slot; Codepoints = codepoints; Points = points; Clusters = clusters; Width = width;
        }
    }

    /// <summary>整行 shaping 快照(T4):逻辑总宽 + 各 run;条目不可变,缓存命中直接复用。</summary>
    sealed class ShapedLayout
    {
        public readonly ShapedRun[] Runs;
        public readonly float TotalWidth;       // 逻辑

        public ShapedLayout(ShapedRun[] runs, float totalWidth)
        {
            Runs = runs; TotalWidth = totalWidth;
        }
    }
}
