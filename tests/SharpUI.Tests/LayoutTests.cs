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

    /// 测试用容器:无叶子自测尺寸,纯靠子元素。
    sealed class ColumnShim : Element
    {
        public ColumnShim(params Element[] children)
        {
            foreach (Element c in children) Add(c);
        }
    }
}
