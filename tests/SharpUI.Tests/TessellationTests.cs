using SharpUI;

namespace SharpUI.Tests;

public class TessellationTests
{
    static readonly PointF White = new(0.0005f, 0.0005f);

    [Fact]
    public void ZeroRadius_FastPathSingleQuad()
    {
        var mesh = new MeshBuilder();
        Tessellator.AddRoundedRect(mesh, new RectF(0, 0, 100, 50), 0f, Color32.White, White);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(6, mesh.IndexCount);
    }

    [Fact]
    public void Rounded_ExactVertexIndexCounts()
    {
        // 3 个矩形条带:12 顶点/18 索引;4 角 × 8 扇形三角:96 顶点/96 索引
        var mesh = new MeshBuilder();
        Tessellator.AddRoundedRect(mesh, new RectF(0, 0, 100, 50), 8f, Color32.White, White);
        Assert.Equal(108, mesh.VertexCount);
        Assert.Equal(114, mesh.IndexCount);
    }

    [Fact]
    public void Radius_ClampedToHalfOfSmallerSide()
    {
        var mesh = new MeshBuilder();
        Tessellator.AddRoundedRect(mesh, new RectF(0, 0, 100, 50), 500f, Color32.White, White);
        Assert.Equal(108, mesh.VertexCount);   // 未爆掉、未 NaN,与正常圆角路径相同的计数
        bool anyNaN = false;
        for (int i = 0; i < mesh.VertexCount; i++)
            if (float.IsNaN(mesh.Vertices[i].X) || float.IsNaN(mesh.Vertices[i].Y)) anyNaN = true;
        Assert.False(anyNaN);
    }

    [Fact]
    public void CornerTriangles_StayWithinRectBounds()
    {
        var mesh = new MeshBuilder();
        Tessellator.AddRoundedRect(mesh, new RectF(10, 10, 100, 50), 10f, Color32.White, White);
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            Assert.InRange(mesh.Vertices[i].X, 9.9f, 110.1f);
            Assert.InRange(mesh.Vertices[i].Y, 9.9f, 60.1f);
        }
    }
}
