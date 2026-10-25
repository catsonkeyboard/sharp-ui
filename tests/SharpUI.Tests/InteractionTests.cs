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
}
