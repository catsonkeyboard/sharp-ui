namespace SharpUI.Tests;

public class GeometryTests
{
    [Fact]
    public void Contains_IsInclusiveExclusive()
    {
        var r = new RectF(10, 10, 20, 20);
        Assert.True(r.Contains(new PointF(10, 10)));
        Assert.True(r.Contains(new PointF(29.9f, 29.9f)));
        Assert.False(r.Contains(new PointF(30, 10)));
        Assert.False(r.Contains(new PointF(9.9f, 10)));
    }

    [Fact]
    public void InflateDeflate_AreInverse()
    {
        var r = new RectF(10, 20, 30, 40);
        var t = new Thickness(2, 3, 4, 5);
        Assert.Equal(r, r.Inflate(t).Deflate(t));
    }

    [Fact]
    public void Deflate_ShrinksRect()
    {
        var r = new RectF(0, 0, 30, 30);
        var d = r.Deflate(new Thickness(5));
        Assert.Equal(new RectF(5, 5, 20, 20), d);
    }

    [Fact]
    public void Intersects_BasicCases()
    {
        var a = new RectF(0, 0, 10, 10);
        Assert.True(a.Intersects(new RectF(5, 5, 10, 10)));
        Assert.False(a.Intersects(new RectF(10, 10, 10, 10)));
        Assert.False(new RectF(0, 0, 0, 10).Intersects(a));
    }

    [Fact]
    public void Thickness_Aggregates()
    {
        var t = new Thickness(3, 7);
        Assert.Equal(6f, t.Horizontal);
        Assert.Equal(14f, t.Vertical);
    }
}
