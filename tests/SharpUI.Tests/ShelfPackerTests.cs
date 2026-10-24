using SharpUI;

namespace SharpUI.Tests;

public class ShelfPackerTests
{
    [Fact]
    public void Allocates_SequentiallyInARow()
    {
        var p = new ShelfPacker(10, 10);
        Assert.Equal((0, 0), p.TryAlloc(4, 2));
        Assert.Equal((4, 0), p.TryAlloc(4, 2));
        Assert.Equal((8, 0), p.TryAlloc(2, 2));
    }

    [Fact]
    public void WrapsToNewRow_UsingTallestRowHeight()
    {
        var p = new ShelfPacker(10, 10);
        p.TryAlloc(4, 5);        // 行高 5
        Assert.Equal((4, 0), p.TryAlloc(4, 2));
        Assert.Equal((0, 5), p.TryAlloc(4, 2));   // 放不下 → 换行
    }

    [Fact]
    public void ReturnsNull_WhenExhausted()
    {
        var p = new ShelfPacker(4, 4);
        Assert.NotNull(p.TryAlloc(4, 2));
        Assert.NotNull(p.TryAlloc(4, 2));
        Assert.Null(p.TryAlloc(1, 1));
    }

    [Fact]
    public void TooWide_AlwaysNull()
    {
        var p = new ShelfPacker(4, 10);
        Assert.Null(p.TryAlloc(5, 1));
    }

    [Fact]
    public void Reset_AllowsReallocation()
    {
        var p = new ShelfPacker(4, 4);
        p.TryAlloc(4, 4);
        Assert.Null(p.TryAlloc(1, 1));
        p.Reset();
        Assert.Equal((0, 0), p.TryAlloc(1, 1));
    }
}
