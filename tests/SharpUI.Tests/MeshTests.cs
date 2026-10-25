using System.Runtime.InteropServices;
using SharpUI;

namespace SharpUI.Tests;

public class MeshTests
{
    [Fact]
    public void Vertex_Is20BytesBlittable()
    {
        Assert.Equal(UiVertex.SizeInBytes, (uint)Marshal.SizeOf<UiVertex>());
    }

    [Fact]
    public void AddQuad_AddsFourVertsSixIndices()
    {
        var mesh = new MeshBuilder();
        mesh.AddQuad(new RectF(0, 0, 10, 10), new RectF(0, 0, 1, 1), Color32.White);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(6, mesh.IndexCount);
        Assert.Equal(0, mesh.Indices[0]);
        Assert.Equal(1, mesh.Indices[1]);
        Assert.Equal(2, mesh.Indices[2]);
        Assert.Equal(0, mesh.Indices[3]);
        Assert.Equal(2, mesh.Indices[4]);
        Assert.Equal(3, mesh.Indices[5]);
    }

    [Fact]
    public void AddQuad_PremultipliesColor()
    {
        var mesh = new MeshBuilder();
        mesh.AddQuad(new RectF(0, 0, 1, 1), new RectF(0, 0, 0, 0), new Color32(255, 0, 0, 128));
        Assert.Equal(128, mesh.Vertices[0].R);
        Assert.Equal(0, mesh.Vertices[0].G);
        Assert.Equal(128, mesh.Vertices[0].A);
    }

    [Fact]
    public void AddTriangle_AddsThreeOfEach()
    {
        var mesh = new MeshBuilder();
        mesh.AddTriangle(new PointF(0, 0), new PointF(1, 0), new PointF(0, 1), Color32.White, new PointF(0.5f, 0.5f));
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(3, mesh.IndexCount);
    }

    [Fact]
    public void GrowsBeyondInitialCapacity_AndClears()
    {
        var mesh = new MeshBuilder();
        for (int i = 0; i < 400; i++)             // 初始容量 1024 顶点,400 quads = 1600 顶点
            mesh.AddQuad(new RectF(i, 0, 1, 1), new RectF(0, 0, 1, 1), Color32.White);
        Assert.Equal(1600, mesh.VertexCount);
        Assert.Equal(2400, mesh.IndexCount);
        mesh.Clear();
        Assert.Equal(0, mesh.VertexCount);
        Assert.Equal(0, mesh.IndexCount);
    }

    [Fact]
    public void AddQuad_ThrowsAtVertexOverflow()
    {
        var mesh = new MeshBuilder();
        for (int i = 0; i < 65536 / 4; i++)          // 16384 quads = 恰好 65536 顶点
            mesh.AddQuad(new RectF(i % 100, 0, 1, 1), new RectF(0, 0, 1, 1), Color32.White);
        Assert.Equal(65536, mesh.VertexCount);
        Assert.Throws<InvalidOperationException>(() =>
            mesh.AddQuad(new RectF(0, 0, 1, 1), new RectF(0, 0, 1, 1), Color32.White));
    }

    [Fact]
    public void SetClip_SplitsRunsAndClearResets()
    {
        var mesh = new MeshBuilder();
        RectF uv = new(0, 0, 1, 1);
        mesh.AddQuad(new RectF(0, 0, 8, 8), uv, Color32.White);
        mesh.SetClip(new RectF(0, 0, 4, 4));
        mesh.AddQuad(new RectF(0, 0, 8, 8), uv, Color32.White);
        mesh.SetClip(new RectF(0, 0, 4, 4));       // 同值 no-op
        mesh.AddQuad(new RectF(0, 0, 8, 8), uv, Color32.White);
        Assert.Equal(2, mesh.ClipRuns.Length);
        Assert.Equal(6, mesh.ClipRuns[0].Count);   // 第一个 quad(全屏剪裁)
        Assert.Equal(12, mesh.ClipRuns[1].Count);  // 后两个 quad(4,4 剪裁)
        mesh.Clear();
        Assert.Equal(0, mesh.ClipRuns.Length);
        Assert.Equal(new RectF(0, 0, float.MaxValue, float.MaxValue), mesh.CurrentClip);
    }

    [Fact]
    public void Intersect_ReturnsClampedEmpty()
    {
        Assert.Equal(new RectF(5, 5, 5, 5), new RectF(0, 0, 10, 10).Intersect(new RectF(5, 5, 10, 10)));
        RectF none = new RectF(0, 0, 10, 10).Intersect(new RectF(20, 20, 5, 5));
        Assert.True(none.IsEmpty);
        Assert.Equal(0f, none.Width);
    }
}
