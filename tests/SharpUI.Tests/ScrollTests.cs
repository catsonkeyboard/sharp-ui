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
        IdResolver.Assign(s);                             // 裁定#22:直连 Dispatch 前补稳定 id
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
        IdResolver.Assign(s);                             // 裁定#22:直连 Dispatch 前补稳定 id
        Scroll.Dispatch(s, new InputState(new PointF(500, 500), false, false, false, null, WheelDelta: 1f));
        Assert.Equal(0f, App.State!.Get("scroll:" + s.ResolvedId!, 0f));
        App.State = null;
    }

    [Fact]
    public void DeepestScrollable_Wins()
    {
        App.State = new StateStore();
        ScrollElement inner = Tall(10).Basis(100f);   // D19:滚动测量按内容,须显式限高才可滚
        ScrollElement outer = Scroll(inner, new Spacer(new SizeF(50, 900)));   // inner 在顶部,处 outer 视口内可见(裁定#22)
        LayoutEngine.Arrange(outer, new RectF(0, 0, 200, 100));
        IdResolver.Assign(outer);                     // 裁定#22:直连 Dispatch 前补稳定 id
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
        IdResolver.Assign(outer);                     // 裁定#22:直连 Dispatch 前补稳定 id
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
        IdResolver.Assign(s);                             // 裁定#22:直连 Dispatch 前补稳定 id
        Scroll.Dispatch(s, new InputState(new PointF(50, 50), false, false, false, null, WheelDelta: 1f));   // offset=40
        LayoutEngine.Arrange(s, new RectF(0, 0, 100, 100));                     // 重排应用偏移
        Assert.Equal(-40f, s.Children![0].Frame.Y);
        Assert.Equal(-10f, s.Children[1].Frame.Y);
        App.State = null;
    }

    [Fact]
    public void VirtualList_BuildsOnlyVisibleWindow()
    {
        App.State = new StateStore();
        VirtualListElement list = Ui.VirtualList("rows", 100, 24f, static (i, row) =>
            row.Add(new TextElement($"row {i}")));
        LayoutEngine.Arrange(list, new RectF(0, 0, 200, 96));     // 4 行可见 +2 过扫
        Assert.InRange(list.Children!.Count, 4, 8);
        Assert.Equal("0", list.Children[0].Key);
        Assert.Equal(0f, list.Children[0].Frame.Y);
        Assert.Equal(24f, list.Children[1].Frame.Y);
        App.State = null;
    }

    [Fact]
    public void VirtualList_ScrollShiftsWindowAndKeysStayStable()
    {
        App.State = new StateStore();
        VirtualListElement list = Ui.VirtualList("rows", 100, 24f, static (i, row) =>
            row.Add(new TextElement($"row {i}")));
        LayoutEngine.Arrange(list, new RectF(0, 0, 200, 96));
        list.ScrollBy(48f);                                        // offset=48
        LayoutEngine.Arrange(list, new RectF(0, 0, 200, 96));      // 重建窗
        Assert.Equal("1", list.Children![0].Key);                  // 窗口下移,键随索引
        Assert.Equal(-24f, list.Children[0].Frame.Y);              // 行1 y=1*24-48(部分滚出)
        Assert.Equal("6", list.Children[^1].Key);                  // 可见到 5,+1 过扫
        App.State = null;
    }

    [Fact]
    public void VirtualList_ClampsAndReportsFullContent()
    {
        App.State = new StateStore();
        VirtualListElement list = Ui.VirtualList("rows", 100, 24f, static (i, row) => row.Add(new TextElement(i.ToString())));
        LayoutEngine.Arrange(list, new RectF(0, 0, 200, 96));
        Assert.Equal(2400f, list.ContentMain);
        Scroll.Dispatch(list, new InputState(new PointF(50, 10), false, false, false, null, WheelDelta: 1000f));
        Assert.Equal(2304f, App.State!.Get("scroll:" + list.ResolvedId!, 0f));   // 钳 2400-96
        App.State = null;
    }
}
