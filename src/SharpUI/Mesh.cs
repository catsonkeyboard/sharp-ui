using System.Runtime.InteropServices;

namespace SharpUI;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UiVertex
{
    public float X, Y;      // 屏幕像素,左上原点
    public float U, V;      // 图集 UV,归一化
    public byte R, G, B, A; // 预乘颜色
    public const uint SizeInBytes = 20;

    public static UiVertex Create(PointF pos, PointF uv, Color32 color)
    {
        (byte r, byte g, byte b) = color.Premultiplied;
        return new UiVertex
        {
            X = pos.X, Y = pos.Y, U = uv.X, V = uv.Y,
            R = r, G = g, B = b, A = color.A,
        };
    }
}

public sealed class MeshBuilder
{
    public readonly struct ClipRun
    {
        public readonly RectF Clip;
        public readonly int Start, Count;
        public ClipRun(RectF clip, int start, int count) { Clip = clip; Start = start; Count = count; }
    }

    public static readonly RectF FullClip = new(0, 0, float.MaxValue, float.MaxValue);

    UiVertex[] _vertices = new UiVertex[1024];
    ushort[] _indices = new ushort[2048];
    int _vertexCount, _indexCount;
    RectF _clip = FullClip;
    int _runStart;
    readonly List<ClipRun> _clipRuns = new();

    public int VertexCount => _vertexCount;
    public int IndexCount => _indexCount;
    public ReadOnlySpan<UiVertex> Vertices => _vertices.AsSpan(0, _vertexCount);
    public ReadOnlySpan<ushort> Indices => _indices.AsSpan(0, _indexCount);
    public RectF CurrentClip => _clip;

    /// <summary>读取时物化尾段(最后一次 SetClip 之后的开放区段),否则丢失末端几何。</summary>
    public ReadOnlySpan<ClipRun> ClipRuns
    {
        get
        {
            if (_indexCount > _runStart)
            {
                _clipRuns.Add(new ClipRun(_clip, _runStart, _indexCount - _runStart));
                _runStart = _indexCount;
            }
            return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_clipRuns);
        }
    }

    public void SetClip(RectF clip)
    {
        if (clip == _clip) return;
        if (_indexCount > _runStart)
            _clipRuns.Add(new ClipRun(_clip, _runStart, _indexCount - _runStart));
        _clip = clip;
        _runStart = _indexCount;
    }

    public void Clear()
    {
        _vertexCount = 0;
        _indexCount = 0;
        _clipRuns.Clear();
        _clip = FullClip;
        _runStart = 0;
    }

    void Ensure(int extraVerts, int extraIndices)
    {
        if (_vertexCount + extraVerts > ushort.MaxValue + 1)
            throw new InvalidOperationException(
                $"Mesh exceeds 65536 vertices ({_vertexCount + extraVerts}); the frame must be split or cleared.");
        if (_vertexCount + extraVerts > _vertices.Length)
            Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _vertexCount + extraVerts));
        if (_indexCount + extraIndices > _indices.Length)
            Array.Resize(ref _indices, Math.Max(_indices.Length * 2, _indexCount + extraIndices));
    }

    public void AddQuad(RectF rect, RectF uv, Color32 color)
    {
        Ensure(4, 6);
        ushort b = (ushort)_vertexCount;
        _vertices[_vertexCount++] = UiVertex.Create(new PointF(rect.X, rect.Y), new PointF(uv.X, uv.Y), color);
        _vertices[_vertexCount++] = UiVertex.Create(new PointF(rect.Right, rect.Y), new PointF(uv.Right, uv.Y), color);
        _vertices[_vertexCount++] = UiVertex.Create(new PointF(rect.Right, rect.Bottom), new PointF(uv.Right, uv.Bottom), color);
        _vertices[_vertexCount++] = UiVertex.Create(new PointF(rect.X, rect.Bottom), new PointF(uv.X, uv.Bottom), color);
        _indices[_indexCount++] = b;
        _indices[_indexCount++] = (ushort)(b + 1);
        _indices[_indexCount++] = (ushort)(b + 2);
        _indices[_indexCount++] = b;
        _indices[_indexCount++] = (ushort)(b + 2);
        _indices[_indexCount++] = (ushort)(b + 3);
    }

    public void AddTriangle(PointF a, PointF b, PointF c, Color32 color, PointF uv)
    {
        Ensure(3, 3);
        ushort i = (ushort)_vertexCount;
        _vertices[_vertexCount++] = UiVertex.Create(a, uv, color);
        _vertices[_vertexCount++] = UiVertex.Create(b, uv, color);
        _vertices[_vertexCount++] = UiVertex.Create(c, uv, color);
        _indices[_indexCount++] = i;
        _indices[_indexCount++] = (ushort)(i + 1);
        _indices[_indexCount++] = (ushort)(i + 2);
    }
}
