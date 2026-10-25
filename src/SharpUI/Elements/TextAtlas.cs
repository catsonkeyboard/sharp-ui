using SkiaSharp;

namespace SharpUI;

public sealed class TextAtlas : IDisposable
{
    readonly GraphicsContext _ctx;
    readonly SKPaint _paint = new() { Color = SKColors.White, IsAntialias = true };
    readonly SKTypeface _regular;
    readonly SKTypeface _bold;
    readonly Dictionary<int, SKFont> _fonts = new();                 // size << 1 | bold
    readonly Dictionary<(char Char, int Size, bool Bold), Glyph> _glyphs = new();
    readonly Dictionary<int, (float Ascent, float Descent)> _lineMetrics = new();
    readonly ShelfPacker _packer;
    readonly SKBitmap _bitmap;
    readonly SKCanvas _canvas;
    readonly float _scale;                                             // D18:物理/逻辑比,字形按物理尺寸光栅

    public RectF WhiteUv { get; private set; }

    public TextAtlas(GraphicsContext ctx, float scale = 1f)
    {
        _ctx = ctx;
        _scale = scale;
        _regular = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        _bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? _regular;
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
            1f / GraphicsContext.AtlasSize, 1f / GraphicsContext.AtlasSize);
    }

    SKFont GetFont(int size, bool bold)
    {
        int key = size << 1 | (bold ? 1 : 0);
        if (!_fonts.TryGetValue(key, out SKFont? font))
        {
            font = new SKFont(bold ? _bold : _regular, size)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = false,
            };
            _fonts[key] = font;
        }
        return font;
    }

    (float Ascent, float Descent) GetLineMetrics(int size, bool bold)
    {
        int key = size << 1 | (bold ? 1 : 0);
        if (!_lineMetrics.TryGetValue(key, out var lm))
        {
            GetFont(size, bold).MeasureText("Ág", out SKRect bounds, _paint);
            lm = (-bounds.Top, bounds.Bottom);
            _lineMetrics[key] = lm;
        }
        return lm;
    }

    public SizeF Measure(string text, float fontSize, bool bold = false)
    {
        if (text.Length == 0) return SizeF.Zero;
        int size = SizeKey(fontSize, _scale);
        (float ascent, float descent) = GetLineMetrics(size, bold);
        return new SizeF(GetFont(size, bold).MeasureText(text, out _, _paint) / _scale, (ascent + descent) / _scale);
    }

    public void DrawRun(MeshBuilder mesh, string text, PointF topLeft, float fontSize, bool bold, Color32 color)
    {
        int size = SizeKey(fontSize, _scale);
        (float ascent, _) = GetLineMetrics(size, bold);
        float baselineY = topLeft.Y + ascent / _scale;
        float x = topLeft.X;
        foreach (char c in text)
        {
            Glyph g = GetGlyph(c, size, bold);
            if (c != ' ')
                mesh.AddQuad(new RectF(x, baselineY - g.BearingY / _scale, g.Width / _scale, g.Height / _scale), g.Uv, color);
            x += g.Advance / _scale;
        }
    }

    static int SizeKey(float fontSize, float scale) => Math.Max(4, (int)MathF.Ceiling(fontSize * scale));

    Glyph GetGlyph(char c, int size, bool bold)
    {
        if (_glyphs.TryGetValue((c, size, bold), out Glyph glyph)) return glyph;
        glyph = Rasterize(c, size, bold);
        _glyphs[(c, size, bold)] = glyph;
        return glyph;
    }

    Glyph Rasterize(char c, int size, bool bold)
    {
        SKFont font = GetFont(size, bold);
        string s = c.ToString();
        float advance = font.MeasureText(s, out SKRect bounds, _paint);
        int width = Math.Max(1, (int)MathF.Ceiling(bounds.Right - bounds.Left));
        int height = Math.Max(1, (int)MathF.Ceiling(bounds.Bottom - bounds.Top));

        (int X, int Y)? slot = _packer.TryAlloc(width + 1, height + 1);   // +1 防渗色
        if (slot is null)
        {
            _packer.Reset();
            _glyphs.Clear();
            SeedWhitePixel();                                             // (0,0) 重播白像素
            slot = _packer.TryAlloc(width + 1, height + 1)
                ?? throw new InvalidOperationException($"字形 {c}@{size} 放不进图集");
        }

        using var glyphBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var glyphCanvas = new SKCanvas(glyphBitmap);
        glyphCanvas.Clear(SKColors.Transparent);
        glyphCanvas.DrawText(s, -bounds.Left, -bounds.Top, font, _paint);
        glyphCanvas.Flush();
        _ctx.UpdateAtlasRegion(glyphBitmap.GetPixelSpan().ToArray(), (uint)slot.Value.X, (uint)slot.Value.Y, (uint)width, (uint)height);

        return new Glyph(
            new RectF(slot.Value.X / (float)GraphicsContext.AtlasSize,
                      slot.Value.Y / (float)GraphicsContext.AtlasSize,
                      width / (float)GraphicsContext.AtlasSize,
                      height / (float)GraphicsContext.AtlasSize),
            width, height, -bounds.Top, advance);
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _bitmap.Dispose();
        foreach (SKFont f in _fonts.Values) f.Dispose();
        _paint.Dispose();
        if (!ReferenceEquals(_bold, _regular)) _bold.Dispose();
        _regular.Dispose();
    }

    readonly record struct Glyph(RectF Uv, float Width, float Height, float BearingY, float Advance);
}
