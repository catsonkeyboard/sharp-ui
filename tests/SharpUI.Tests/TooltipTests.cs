using SharpUI;

namespace SharpUI.Tests;

/// <summary>D5 T3(Tooltip:DSL 链式 + 悬停服务):时间经 Anim.TimeSource 注入(AnimTests 惯例,ctor/Dispose
/// 复位),帧驱动经 TooltipService.Update(App.Run 每窗接线点的合成等价;模式同 DialogTests 的
/// App.FeedFocused —— 内部静态直调免原生窗)。假句柄段 0xD50300(独立于 PopupHostTests 0xD501xx /
/// DialogTests 0xD502xx / MenuTests 0xD50380+)。</summary>
[Collection("interaction")]
public class TooltipTests : IDisposable
{
    double _t;
    public TooltipTests() => Anim.TimeSource = () => _t;
    public void Dispose() => Anim.TimeSource = null;

    static IntPtr Win(int n) => (IntPtr)(0xD50300 + n);

    /// <summary>悬停帧:鼠标在载体中心,HoveredId=载体 id(命中语义的合成等价)。</summary>
    static InputState Hovering(Element e) =>
        new(new PointF(e.Frame.X + e.Frame.Width / 2f, e.Frame.Y + e.Frame.Height / 2f),
            false, false, false, null) { HoveredId = e.ResolvedId };

    /// <summary>任意位置帧(可带按压):HoveredId=null(移出/点击路径)。</summary>
    static InputState At(PointF p, bool pressed = false) => new(p, pressed, pressed, false, null);

    /// <summary>载体按钮:全宽首行(Frame=(0,0,200,40),Arrange+Assign 就绪)+ Tooltip 附文本。</summary>
    static (PanelElement Root, ButtonElement Carrier) Declared(string text)
    {
        ButtonElement carrier = new ButtonElement("载", () => { }).Tooltip(text);
        PanelElement root = Ui.Column(carrier);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 100));
        IdResolver.Assign(root);
        return (root, carrier);
    }

    [Fact]
    public void Tooltip_ChainSetsText_HoverBelowDelay_NoPush()
    {
        IntPtr h = Win(1);
        PopupHost.Clear(h);
        (PanelElement root, ButtonElement carrier) = Declared("提示");
        Assert.Equal("提示", carrier.TooltipText);        // 链式附加落 Element 内部字段

        TooltipService.Update(h, root, Hovering(carrier));   // t=0:起表
        _t = 0.3;
        TooltipService.Update(h, root, Hovering(carrier));
        Assert.Equal(0, PopupHost.Count(h));                 // <0.5s:不显示
        _t = TooltipService.HoverDelaySeconds - 0.01;
        TooltipService.Update(h, root, Hovering(carrier));
        Assert.Equal(0, PopupHost.Count(h));                 // 门限前一帧仍不显示
        PopupHost.Clear(h);
    }

    [Fact]
    public void Hover_ReachesDelay_PushesTooltip_AnchoredBelow_NoDuplicate()
    {
        IntPtr h = Win(2);
        PopupHost.Clear(h);
        (PanelElement root, ButtonElement carrier) = Declared("提示文本");
        TooltipService.Update(h, root, Hovering(carrier));
        _t = TooltipService.HoverDelaySeconds;
        TooltipService.Update(h, root, Hovering(carrier));
        Assert.Equal(1, PopupHost.Count(h));                 // ≥0.5s → Push

        Popup tip = PopupHost.StackOf(h)[0];
        Assert.Equal(PopupKind.Tooltip, tip.Kind);
        Assert.Equal(carrier.Frame, tip.AnchorRect);         // 锚=元素 Frame
        Assert.Equal(carrier.Frame.X, tip.Root.Frame.X);     // 定位=锚左下(定格)
        Assert.Equal(carrier.Frame.Bottom + 2f, tip.Root.Frame.Y);
        Assert.True(tip.DismissOnOutsideClick);              // 外点路径可弹(服务同帧愈合同步清账)

        _t = 2.0;                                            // 持续悬停:在栈判重,不重复 Push
        TooltipService.Update(h, root, Hovering(carrier));
        TooltipService.Update(h, root, Hovering(carrier));
        Assert.Equal(1, PopupHost.Count(h));
        PopupHost.Clear(h);
    }

    [Fact]
    public void MoveAway_RemovesTooltip_ByIdentity_MenuBelowIntact()
    {
        IntPtr h = Win(3);
        PopupHost.Clear(h);
        // 实景:Tooltip 叠于 Menu 之上(菜单项悬停提示)—— 移出必须按身份 Remove,盲弹会误弹 Menu
        ButtonElement item = new ButtonElement("项", () => { }).Tooltip("项说明");
        PanelElement menuRoot = Ui.Column(item);
        menuRoot.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(menuRoot, new RectF(0, 0, 120, 40));
        Popup menu = new(menuRoot, new RectF(0, 0, 120, 40), PopupKind.Menu);
        PopupHost.Push(h, menu);

        TooltipService.Update(h, Ui.Panel(), Hovering(item));
        _t = 0.6;
        TooltipService.Update(h, Ui.Panel(), Hovering(item));
        Assert.Equal(2, PopupHost.Count(h));                 // Menu + Tooltip(判重:单条)

        TooltipService.Update(h, Ui.Panel(), At(new PointF(300, 300)));   // 移出锚与提示
        Assert.Equal(1, PopupHost.Count(h));                 // 只移除 Tooltip
        Assert.Same(menu, PopupHost.StackOf(h)[0]);          // Menu 完好(未被盲弹误弹)
        PopupHost.Clear(h);
    }

    [Fact]
    public void Click_RemovesTooltip()
    {
        IntPtr h = Win(4);
        PopupHost.Clear(h);
        (PanelElement root, ButtonElement carrier) = Declared("提示");
        TooltipService.Update(h, root, Hovering(carrier));
        _t = TooltipService.HoverDelaySeconds;
        TooltipService.Update(h, root, Hovering(carrier));
        Assert.Equal(1, PopupHost.Count(h));

        TooltipService.Update(h, root, At(new PointF(50, 20), pressed: true));   // 点击(锚上也一样)
        Assert.Equal(0, PopupHost.Count(h));                 // 点击 → 移除
        PopupHost.Clear(h);
    }

    [Fact]
    public void SwitchToOtherElement_NoCrossTalk()
    {
        IntPtr h = Win(5);
        PopupHost.Clear(h);
        ButtonElement a = new ButtonElement("A", () => { }).Tooltip("甲提示");
        ButtonElement b = new ButtonElement("B", () => { }).Tooltip("乙提示");
        PanelElement root = Ui.Column(a, b);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 200, 80));
        IdResolver.Assign(root);

        TooltipService.Update(h, root, Hovering(a));
        _t = 0.6;
        TooltipService.Update(h, root, Hovering(a));
        Assert.Equal(1, PopupHost.Count(h));                 // A 的提示

        TooltipService.Update(h, root, Hovering(b));         // 移到 B:A 出圈即移除
        Assert.Equal(0, PopupHost.Count(h));
        _t = 0.6 + TooltipService.HoverDelaySeconds;
        TooltipService.Update(h, root, Hovering(b));         // B 计时独立满 0.5s
        Assert.Equal(1, PopupHost.Count(h));
        Popup tip = PopupHost.StackOf(h)[0];
        Assert.Equal(b.Frame, tip.AnchorRect);               // 锚=B(不串台)
        Assert.IsType<TooltipElement>(tip.Root);
        PopupHost.Clear(h);
    }

    [Fact]
    public void AnchorVanishes_OrphanTooltipRemovedNextFrame()
    {
        IntPtr h = Win(6);
        PopupHost.Clear(h);
        (PanelElement root, ButtonElement carrier) = Declared("提示");
        TooltipService.Update(h, root, Hovering(carrier));
        _t = TooltipService.HoverDelaySeconds;
        TooltipService.Update(h, root, Hovering(carrier));
        Assert.Equal(1, PopupHost.Count(h));

        // 树重建后载体消失(虚拟化滚出/声明树变更):孤儿提示按身份移除
        PanelElement rebuilt = Ui.Column(Ui.Text("空"));
        LayoutEngine.Arrange(rebuilt, new RectF(0, 0, 200, 100));
        IdResolver.Assign(rebuilt);
        TooltipService.Update(h, rebuilt, At(new PointF(50, 20)));
        Assert.Equal(0, PopupHost.Count(h));
        PopupHost.Clear(h);
    }

    [Fact]
    public void MouseOverTooltipFrame_KeepsOpen_MovingOffBothRemoves()
    {
        IntPtr h = Win(7);
        PopupHost.Clear(h);
        (PanelElement root, ButtonElement carrier) = Declared("提示");
        TooltipService.Update(h, root, Hovering(carrier));
        _t = TooltipService.HoverDelaySeconds;
        TooltipService.Update(h, root, Hovering(carrier));
        Element tipRoot = PopupHost.StackOf(h)[0].Root;
        LayoutEngine.Arrange(tipRoot, new RectF(150, 150, 100, 30));   // 测试重排模拟(提示覆盖鼠标区)

        TooltipService.Update(h, root, At(new PointF(200, 165)));      // 鼠标在提示框内(不在锚上)
        Assert.Equal(1, PopupHost.Count(h));                           // 存活:提示自身 Frame 计入存活域

        TooltipService.Update(h, root, At(new PointF(400, 400)));      // 移出锚与提示
        Assert.Equal(0, PopupHost.Count(h));
        PopupHost.Clear(h);
    }

    /// <summary>审计修复波 C-2:生产帧序(App.Run 每帧 w.Render() 重建整棵声明树)下,载体是每帧新实例、
    /// 同 ResolvedId。孤儿判定若按引用比对(修复前 !_carriers.Contains(anchor)),开启后下一帧即被误判
    /// 孤儿强删 → Tooltip 每 0.5s 闪一帧、永远无法稳定显示。本测模拟逐帧重建(同 id 载体),断言连续
    /// 多帧存活。</summary>
    [Fact]
    public void RebuiltTreeEachFrame_SameIdCarrier_TooltipStaysOpen()
    {
        IntPtr h = Win(8);
        PopupHost.Clear(h);
        (PanelElement root, ButtonElement carrier) = Declared("提示");
        TooltipService.Update(h, root, Hovering(carrier));             // 帧 0:起表
        _t = TooltipService.HoverDelaySeconds;
        TooltipService.Update(h, root, Hovering(carrier));             // 满 0.5s → Push
        Assert.Equal(1, PopupHost.Count(h));

        for (int frame = 1; frame <= 3; frame++)                       // 生产常态:每帧重建树 + 同 id 载体
        {
            _t += 0.1;
            (root, carrier) = Declared("提示");                        // 新实例;同结构 → 同回退 ResolvedId
            TooltipService.Update(h, root, Hovering(carrier));
            Assert.True(PopupHost.Count(h) == 1,                        // 不被当孤儿删(≥3 帧持续在栈)
                $"frame {frame}: tooltip was orphan-pruned (id semantics broken)");
        }

        // 对照:载体彻底消失(重建后无 Tooltip 载体)仍按孤儿移除 —— id 语义不放宽这条
        PanelElement rebuilt = Ui.Column(Ui.Text("空"));
        LayoutEngine.Arrange(rebuilt, new RectF(0, 0, 200, 100));
        IdResolver.Assign(rebuilt);
        TooltipService.Update(h, rebuilt, At(new PointF(50, 20)));
        Assert.Equal(0, PopupHost.Count(h));
        PopupHost.Clear(h);
    }
}
