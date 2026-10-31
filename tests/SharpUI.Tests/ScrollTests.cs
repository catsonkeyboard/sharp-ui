using SharpUI;
using static SharpUI.Ui;

namespace SharpUI.Tests;

[Collection("interaction")]   // 触碰 App.State 静态,与其余静态用户串行(沿用 UiScopeTests 惯例)
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
        InputState state = Interaction.Update(new InputSession(), root, new RawInput(new PointF(5, 5), Array.Empty<bool>(), WheelDelta: 1.5f));
        Assert.Equal(1.5f, state.WheelDelta);
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

    // ---- 审计修复波 I-1:VirtualList 行进焦点注册表(Arrange 期重建的行从未被 Register)----

    /// <summary>假句柄段 0xD50500(独立于 TooltipTests 0xD50300 / DialogTests 0xD502xx / SelectTests 0xD504C0)。</summary>
    static IntPtr VWin(int n) => (IntPtr)(0xD50500 + n);

    /// <summary>App.Run 帧序镜像(修复后序):构建树 → Assign → BeginFrame → Register(此时行尚未建,
    /// 只有占位 Spacer)→ Arrange(行在 ArrangeChildren 内重建 + 补注册)。vlist 嵌套于容器根下(Grow(1)
    /// 撑满视口 —— 占位 Spacer 使自身测量高度为 0,须 grow 分配;根元素 id 恒为 "" 不入注册表,故 vlist
    /// 不可作根,与生产"根恒为容器"常态一致)。</summary>
    static (PanelElement Root, VirtualListElement List) VFrame(AppWindow w, Action<int, PanelElement> render)
    {
        VirtualListElement list = Ui.VirtualList("rows", 100, 24f, render).Grow(1f);
        PanelElement root = Ui.Column(list);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        IdResolver.Assign(root);
        w.Focus.BeginFrame();
        TreeWalk.Register(root, w.Focus);
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 96));
        return (root, list);
    }

    [Fact]
    public void VirtualList_RowSelect_ClickOpensMenu_NoThrow()
    {
        App.State = new StateStore();
        IntPtr h = VWin(1);
        PopupHost.Clear(h);
        FocusManager.UnbindWindow(h);
        var w = new AppWindow();
        w.Session.SdlWindow = h;
        FocusManager.BindWindow(h, w.Focus);   // Select.Open 经 OwnerFocus.SdlWindow 定位浮层栈(SelectTests 同款)
        try
        {
            (PanelElement root, VirtualListElement list) = VFrame(w, static (i, row) =>
                row.Add(Ui.Select(new[] { "a", "b" }, 0, static _ => { })));
            SelectElement sel = (SelectElement)list.Children![0].Children![0];

            // 行内 Select 点击开下拉:修复前行元素从未进注册表(OwnerFocus=null)→ Open 直接抛
            // InvalidOperationException;修复后 Arrange 期补注册,点击即开菜单。
            PointF c = new(sel.Frame.X + sel.Frame.Width / 2f, sel.Frame.Y + sel.Frame.Height / 2f);
            Interaction.Update(w.Session, root, new RawInput(c, new[] { true }), w.Focus);
            Interaction.Update(w.Session, root, new RawInput(c, new[] { false }), w.Focus);
            Assert.Equal(1, PopupHost.Count(h));
            Assert.Equal(PopupKind.Menu, PopupHost.StackOf(h)[0].Kind);
        }
        finally
        {
            PopupHost.Clear(h);
            FocusManager.UnbindWindow(h);
            App.State = null;
        }
    }

    [Fact]
    public void VirtualList_RowButton_FocusedEnter_Activates()
    {
        App.State = new StateStore();
        IntPtr h = VWin(2);
        PopupHost.Clear(h);
        FocusManager.UnbindWindow(h);
        var w = new AppWindow();
        w.Session.SdlWindow = h;
        FocusManager.BindWindow(h, w.Focus);
        try
        {
            int activated = 0;
            (PanelElement root, VirtualListElement list) = VFrame(w, (i, row) =>
                row.Add(new ButtonElement("B" + i, () => activated++)));
            ButtonElement btn = (ButtonElement)list.Children![0].Children![0];

            w.Focus.SetFocus(btn);                                          // 焦点落行内按钮(id 稳定)
            w.Focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) }, w.Session);
            Assert.Equal(1, activated);                                     // 修复前 _byId 无此 id → Enter 静默失效
        }
        finally
        {
            PopupHost.Clear(h);
            FocusManager.UnbindWindow(h);
            App.State = null;
        }
    }
}
