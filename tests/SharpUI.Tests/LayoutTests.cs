using SharpUI;

namespace SharpUI.Tests;

public class LayoutTests
{
    [Fact]
    public void Column_StacksVerticallyWithGapAndPadding()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(10, 20)),
            new Spacer(new SizeF(10, 30)))
        { Style = { Gap = 5, Padding = new Thickness(2) } };
        SizeF m = LayoutEngine.Measure(root, new SizeF(100, 100));
        Assert.Equal(14f, m.Width);      // 10 + padding 4
        Assert.Equal(59f, m.Height);     // 20 + 30 + gap 5 + padding 4
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 100));
        Assert.Equal(new RectF(2, 2, 10, 20), root.Children![0].Frame);
        Assert.Equal(new RectF(2, 27, 10, 30), root.Children![1].Frame);
    }

    [Fact]
    public void Row_ArrangesHorizontally()
    {
        Element root = new ColumnShim(new Spacer(new SizeF(20, 10)), new Spacer(new SizeF(30, 10)))
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 100));
        Assert.Equal(new RectF(0, 0, 20, 10), root.Children![0].Frame);
        Assert.Equal(new RectF(20, 0, 30, 10), root.Children![1].Frame);
    }

    [Fact]
    public void MainCenter_CentersContent()
    {
        Element root = new ColumnShim(new Spacer(new SizeF(10, 10)))
        { Style = { MainAlign = MainAxisAlignment.Center } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 60));
        Assert.Equal(25f, root.Children![0].Frame.Y);   // (60-10)/2
    }

    [Fact]
    public void CrossCenter_AndStretch()
    {
        Element center = new ColumnShim(new Spacer(new SizeF(10, 10)))
            { Style = { CrossAlign = CrossAxisAlignment.Center } };
        LayoutEngine.Arrange(center, new RectF(0, 0, 100, 60));
        Assert.Equal(45f, center.Children![0].Frame.X); // (100-10)/2

        Element stretch = new ColumnShim(new Spacer(new SizeF(10, 10)))
            { Style = { CrossAlign = CrossAxisAlignment.Stretch } };
        LayoutEngine.Arrange(stretch, new RectF(0, 0, 100, 60));
        Assert.Equal(100f, stretch.Children![0].Frame.Width);
    }

    [Fact]
    public void NestedContainers_UseAbsoluteCoordinates()
    {
        Element root = new ColumnShim(
            new ColumnShim(new Spacer(new SizeF(5, 5))) { Style = { Padding = new Thickness(10) } })
        { Style = { Padding = new Thickness(1) } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 500, 500));
        Assert.Equal(new RectF(1, 1, 25, 25), root.Children![0].Frame);
        Assert.Equal(new RectF(11, 11, 5, 5), root.Children![0].Children![0].Frame);
    }

    [Fact]
    public void Measure_LeavesDecideOwnSize_SpacerDoesNotShrink()
    {
        // M1 语义:叶子自报固定尺寸,不因可用空间截断;溢出处理(裁剪/滚动)是 M2 的事
        Element root = new ColumnShim(new Spacer(new SizeF(10, 60)), new Spacer(new SizeF(10, 60)))
            { Style = { Gap = 5 } };
        SizeF m = LayoutEngine.Measure(root, new SizeF(100, 80));
        Assert.Equal(125f, m.Height);  // 60 + 5 + 60,Spacer 不收缩
        Assert.Equal(10f, m.Width);
    }

    [Fact]
    public void Wrap_OverlongColumnBreaksIntoLinesStackedHorizontally()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(10, 30)),
            new Spacer(new SizeF(10, 30)))
        { Style = { Wrap = FlexWrap.Wrap, Gap = 5 } };
        SizeF m = LayoutEngine.Measure(root, new SizeF(100, 50));
        Assert.Equal(30f, m.Height);          // 最长行
        Assert.Equal(25f, m.Width);           // 两行交叉宽 10+10+gap5
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 50));
        Assert.Equal(new RectF(0, 0, 10, 30), root.Children![0].Frame);
        Assert.Equal(new RectF(15, 0, 10, 30), root.Children[1].Frame);   // 第二行 x=10+gap5
    }

    [Fact]
    public void Grow_DistributesFreeSpaceWeighted()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(20, 10)) { Style = { FlexGrow = 1 } },
            new Spacer(new SizeF(30, 10)) { Style = { FlexGrow = 3 } })
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 50));
        Assert.Equal(57.5f, root.Children![0].Frame.Width);   // 20 + 150×1/4
        Assert.Equal(142.5f, root.Children[1].Frame.Width);   // 30 + 150×3/4
    }

    [Fact]
    public void Shrink_ShrinksOverflowWeightedByBasis()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(60, 10)) { Style = { FlexShrink = 1 } },
            new Spacer(new SizeF(60, 10)) { Style = { FlexShrink = 1 } })
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 50, 20));
        Assert.Equal(25f, root.Children![0].Frame.Width);     // 60 - 70×60/120
        Assert.Equal(25f, root.Children[1].Frame.Width);
    }

    [Fact]
    public void DefaultNoShrink_OverflowPreserved()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(60, 10)),
            new Spacer(new SizeF(60, 10)))
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 50, 20));
        Assert.Equal(60f, root.Children![0].Frame.Width);     // M1 语义
    }

    [Fact]
    public void Basis_OverridesContentMainSize()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(20, 10)) { Style = { FlexBasis = 45 } })
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 20));
        Assert.Equal(45f, root.Children![0].Frame.Width);
    }

    [Fact]
    public void WrappedLines_StackVerticallyInRow()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(30, 10)),
            new Spacer(new SizeF(30, 10)))
        { Style = { Axis = Axis.Horizontal, Wrap = FlexWrap.Wrap, Gap = 5 } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 50, 100));
        Assert.Equal(new RectF(0, 0, 30, 10), root.Children![0].Frame);
        Assert.Equal(new RectF(0, 15, 30, 10), root.Children[1].Frame);   // 第二行 y=10+gap5
    }

    /// 测试用容器:无叶子自测尺寸,纯靠子元素。
    sealed class ColumnShim : Element
    {
        public ColumnShim(params Element[] children)
        {
            foreach (Element c in children) Add(c);
        }
    }
}
