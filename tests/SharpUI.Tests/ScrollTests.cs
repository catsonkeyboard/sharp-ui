using SharpUI;
using static SharpUI.Ui;

namespace SharpUI.Tests;

public class ScrollTests
{
    static ScrollElement Tall(int rows, float rowH = 30f)
    {
        ScrollElement s = Scroll();
        for (int i = 0; i < rows; i++)
            s.Add(new Spacer(new SizeF(50, rowH)));
        return s;
    }

    [Fact]
    public void WheelDelta_FlowsThroughInputState()
    {
        PanelElement root = Panel();
        LayoutEngine.Arrange(root, new RectF(0, 0, 10, 10));
        IdResolver.Assign(root);
        InputState s = Interaction.Update(root, new RawInput(new PointF(5, 5), Array.Empty<bool>(), WheelDelta: 1.5f));
        Assert.Equal(1.5f, s.WheelDelta);
    }

    [Fact]
    public void Wheel_ScrollsAndClamps()
    {
        App.State = new StateStore();
        ScrollElement s = Tall(10);                       // 300 高内容
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));
        string id = s.ResolvedId!;
        Scroll.Dispatch(s, new InputState(new PointF(50, 50), false, false, false, null, WheelDelta: 1f));
        Assert.Equal(40f, App.State!.Get("scroll:" + id, 0f));
        Scroll.Dispatch(s, new InputState(new PointF(50, 50), false, false, false, null, WheelDelta: 100f));
        Assert.Equal(200f, App.State.Get("scroll:" + id, 0f));   // 钳 300-100
        App.State = null;
    }

    [Fact]
    public void WheelOutsideFrame_Ignored()
    {
        App.State = new StateStore();
        ScrollElement s = Tall(10);
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));
        Scroll.Dispatch(s, new InputState(new PointF(500, 500), false, false, false, null, WheelDelta: 1f));
        Assert.Equal(0f, App.State!.Get("scroll:" + s.ResolvedId!, 0f));
        App.State = null;
    }

    [Fact]
    public void DeepestScrollable_Wins()
    {
        App.State = new StateStore();
        ScrollElement inner = Tall(10).Basis(100f);   // D19:滚动测量按内容,须显式限高才可滚
        ScrollElement outer = Scroll(new Spacer(new SizeF(50, 500)), inner);   // 外层也含长内容
        LayoutEngine.Arrange(outer, new RectF(0, 0, 200, 100));
        PointF p = new(inner.Frame.X + 10, inner.Frame.Y + 10);                 // 内层内
        Scroll.Dispatch(outer, new InputState(p, false, false, false, null, WheelDelta: 1f));
        Assert.Equal(40f, App.State!.Get("scroll:" + inner.ResolvedId!, 0f));
        Assert.Equal(0f, App.State.Get("scroll:" + outer.ResolvedId!, 0f));
        App.State = null;
    }

    [Fact]
    public void NonScrollableInner_FallsThroughToOuter()
    {
        App.State = new StateStore();
        var inner = Panel();  inner.Add(new Spacer(new SizeF(50, 20)));         // 短内容,不滚
        ScrollElement outer = Scroll(inner, new Spacer(new SizeF(50, 900)));
        LayoutEngine.Arrange(outer, new RectF(0, 0, 200, 100));
        Scroll.Dispatch(outer, new InputState(new PointF(50, 10), false, false, false, null, WheelDelta: 1f));
        Assert.Equal(40f, App.State!.Get("scroll:" + outer.ResolvedId!, 0f));
        App.State = null;
    }

    [Fact]
    public void ArrangedContent_ShiftsByOffset()
    {
        App.State = new StateStore();
        ScrollElement s = Tall(10);
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));
        Scroll.Dispatch(s, new InputState(new PointF(50, 50), false, false, false, null, WheelDelta: 1f));   // offset=40
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));                     // 重排应用偏移
        Assert.Equal(-40f, s.Children![0].Frame.Y);
        Assert.Equal(-10f, s.Children[1].Frame.Y);
        App.State = null;
    }
}
