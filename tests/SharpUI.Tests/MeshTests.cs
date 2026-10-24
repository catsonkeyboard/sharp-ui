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
}
