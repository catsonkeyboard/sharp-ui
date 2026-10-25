using SharpUI;

namespace SharpUI.Tests;

public class InteractionTests
{
    static (Element Root, ButtonElement Button) ButtonAt(RectF frame)
    {
        var button = new ButtonElement("OK", () => { });
        LayoutEngine.Arrange(button, frame);
        return (button, button);
    }

    [Fact]
    public void Hover_DetectsHitTarget()
    {
        (Element root, _) = ButtonAt(new RectF(0, 0, 100, 40));
        InputState s = Interaction.Update(root, new RawInput(new PointF(50, 20), Array.Empty<bool>()));
        Assert.NotNull(s.Hovered);
        Assert.False(s.LeftDown);
        Assert.False(s.LeftPressed);
        Assert.False(s.LeftReleased);
    }

    [Fact]
    public void PressThenRelease_FiresClick()
    {
        var clicks = 0;
        var button = new ButtonElement("OK", () => clicks++);
        LayoutEngine.Arrange(button, new RectF(0, 0, 100, 40));

        Interaction.Update(button, new RawInput(new PointF(50, 20), new[] { true }));
        InputState released = Interaction.Update(button, new RawInput(new PointF(50, 20), new[] { false }));

        Assert.Equal(1, clicks);
        Assert.True(released.LeftReleased);
        Assert.False(released.LeftDown);
    }

    [Fact]
    public void Miss_DoesNotHit()
    {
        (Element root, _) = ButtonAt(new RectF(0, 0, 100, 40));
        InputState s = Interaction.Update(root, new RawInput(new PointF(150, 20), Array.Empty<bool>()));
        Assert.Null(s.Hovered);
    }

    [Fact]
    public void ClickOutsideHoverInside_ReleaseFiresNothing()
    {
        var clicks = 0;
        var button = new ButtonElement("OK", () => clicks++);
        LayoutEngine.Arrange(button, new RectF(0, 0, 100, 40));
        Interaction.Update(button, new RawInput(new PointF(500, 500), new[] { true }));    // 外面按下
        Interaction.Update(button, new RawInput(new PointF(50, 20), new[] { false }));    // 移进来松开
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void DeepestChildWins_AndNonTargetIgnored()
    {
        PanelElement panel = Ui.Panel().Fill(0x202020);
        panel.Style.Axis = Axis.Vertical;
        var inner = new ButtonElement("x", () => { });
        panel.Add(inner);
        LayoutEngine.Arrange(panel, new RectF(0, 0, 200, 100));
        InputState s = Interaction.Update(panel, new RawInput(
            new PointF(inner.Frame.X + 1, inner.Frame.Y + 1), Array.Empty<bool>()));
        Assert.Same(inner, s.Hovered);
    }

    [Fact]
    public void RebuiltTree_ClickFiresAcrossFrames()
    {
        var clicks = 0;
        Element Build()
        {
            PanelElement tree = Ui.Panel();
            tree.Add(new ButtonElement("OK", () => clicks++));
            LayoutEngine.Arrange(tree, new RectF(0, 0, 200, 100));
            return tree;
        }

        Element pressFrame = Build();
        Element releaseFrame = Build();   // 结构/几何相同的不同实例(模拟 App 每帧重建)
        Interaction.Update(pressFrame, new RawInput(new PointF(10, 10), new[] { true }));
        Interaction.Update(releaseFrame, new RawInput(new PointF(10, 10), new[] { false }));
        Assert.Equal(1, clicks);
    }

    static PanelElement BuildReorder(bool swapped, Action onInc)
    {
        ButtonElement inc = new("+", onInc); inc.Key = "inc";
        ButtonElement dec = new("-", () => { }); dec.Key = "dec";
        PanelElement col = Ui.Panel();
        col.Style.CrossAlign = CrossAxisAlignment.Stretch;   // 修正:Atlas 缺省时按钮仅 32px 宽,x=50 命不中;拉伸后断言坐标有效
        col.Add(swapped ? dec : inc);
        col.Add(swapped ? inc : dec);
        return col;
    }

    [Fact]
    public void KeyedButtons_ClickSurvivesReorderAcrossRebuild()
    {
        int incClicks = 0;
        PanelElement t1 = BuildReorder(false, () => incClicks++);
        PanelElement t2 = BuildReorder(true, () => incClicks++);
        LayoutEngine.Arrange(t1, new RectF(0, 0, 100, 200));
        LayoutEngine.Arrange(t2, new RectF(0, 0, 100, 200));
        IdResolver.Assign(t1);
        IdResolver.Assign(t2);
        float h = t1.Children![0].Frame.Height;

        Interaction.Update(t1, new RawInput(new PointF(50, 5), new[] { true }));
        Interaction.Update(t2, new RawInput(new PointF(50, h + 5), new[] { false }));

        Assert.Equal(1, incClicks);       // 键 "inc" 在两帧中位于不同位置仍被正确命中
    }

    [Fact]
    public void UnkeyedButtons_ReorderBreaksClick()
    {
        int firstClicks = 0;
        PanelElement Build(bool swapped)
        {
            ButtonElement a = new("+", () => firstClicks++);
            ButtonElement b = new("-", () => { });
            PanelElement col = Ui.Panel();
            col.Style.CrossAlign = CrossAxisAlignment.Stretch;   // 修正:同上,否则空转通过(几何上永不命中)
            col.Add(swapped ? b : a);
            col.Add(swapped ? a : b);
            return col;
        }
        PanelElement t1 = Build(false);
        PanelElement t2 = Build(true);
        LayoutEngine.Arrange(t1, new RectF(0, 0, 100, 200));
        LayoutEngine.Arrange(t2, new RectF(0, 0, 100, 200));
        IdResolver.Assign(t1);
        IdResolver.Assign(t2);
        float h = t1.Children![0].Frame.Height;

        Interaction.Update(t1, new RawInput(new PointF(50, 5), new[] { true }));
        Interaction.Update(t2, new RawInput(new PointF(50, h + 5), new[] { false }));

        Assert.Equal(0, firstClicks);     // 无键回退到索引身份:换位后语义不同,不触发(文档化语义)
    }
}
