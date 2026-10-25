using SharpUI;
using static SharpUI.Ui;

namespace SharpUI.Tests;

[Collection("interaction")]   // 键盘账本测试触碰 Interaction/IdResolver 静态,与其余交互测试串行
public class UiScopeTests
{
    [Fact]
    public void Immediate_BuildsTreeWithBakedKeys_MatchingIdResolver()
    {
        PanelElement panel = Immediate("dbg", static ui =>
        {
            ui.Text("fps: 60", 14);
            ui.Row(static row => { row.Button("Reset"); row.Button("Apply"); });
        });
        PanelElement root = Column(panel);
        IdResolver.Assign(root);

        Assert.Equal(2, panel.Children!.Count);                                    // Text + Row
        PanelElement rowPanel = (PanelElement)panel.Children[1];
        Assert.Equal(Axis.Horizontal, rowPanel.Style.Axis);
        Assert.EndsWith("/dbg/PanelElement1/ButtonElement0", rowPanel.Children![0].ResolvedId);   // 局部路径=全局后缀
        Assert.EndsWith("/dbg/PanelElement1/ButtonElement1", rowPanel.Children[1].ResolvedId);
    }

    [Fact]
    public void Clicked_MatchesLedgerBySuffix()
    {
        PanelElement? clicked = null;   // 根容器引用:闭包在 probe 赋值前执行,须从作用域内取根
        var ledger = new List<string> { "/Column0/dbg/ButtonElement0" };
        UiContext.CurrentInput = new InputState(
            new PointF(0, 0), false, false, false, null, ClickedIds: ledger);
        PanelElement probe = Immediate("dbg", ui =>
        {
            if (ui.Button("Reset").Clicked()) clicked = ui.Root;
        });
        Assert.NotNull(clicked);
        Assert.Same(probe, clicked);
    }

    [Fact]
    public void Hovered_UsesHoveredIdSuffix()
    {
        UiContext.CurrentInput = new InputState(
            new PointF(0, 0), false, false, false, null, HoveredId: "/x/dbg/PanelElement0/ButtonElement1");   // Row 为首子段,实测 ResolvedId 后缀(与测试1的次子段场景区分)
        bool hovered = false;
        Immediate("dbg", ui => ui.Row(row => { row.Button("A"); if (row.Button("B").Hovered()) hovered = true; }));
        Assert.True(hovered);
        UiContext.CurrentInput = default;
    }

    [Fact]
    public void State_BridgesToStoreWithNamespacedKey()
    {
        App.State = new StateStore();
        Immediate("dbg", static ui => ui.SetState("count", ui.State("count", 5) + 1));   // Get(5) → Set(6)
        int read = 0;
        Immediate("dbg", ui => read = ui.State("count", 0));                          // Get → 6
        Assert.Equal(6, read);
        App.State = null;
    }

    [Fact]
    public void EmptyLedger_NoClickAndNoCrash()
    {
        UiContext.CurrentInput = default;   // ClickedIds null
        bool any = false;
        Immediate("dbg", ui => { if (ui.Button("X").Clicked()) any = true; });
        Assert.False(any);
    }

    [Fact]
    public void KeyboardActivation_AppearsInNextFrameLedger()
    {
        ButtonElement? activated = null;
        ButtonElement b = null!;   // C# 不允许在自身初始化器内捕获 var 声明(CS0841),先声明后赋值
        b = new ButtonElement("A", () => activated = b);
        PanelElement root = Ui.Panel();
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        root.Add(b);
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 40));
        IdResolver.Assign(root);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Enter, true, false) });
        Assert.NotNull(activated);                                   // 当帧回调已触发
        UiContext.CurrentInput = Interaction.Update(root, new RawInput(new PointF(0, 0), Array.Empty<bool>()));
        Assert.Contains(b.ResolvedId, UiContext.CurrentInput.ClickedIds!);   // 下一帧账本可见
        UiContext.CurrentInput = default;
    }

    [Fact]
    public void Depth2Path_SuffixMatchesForwardIdChain()
    {
        PanelElement panel = Ui.Immediate("dbg", static ui =>
        {
            ui.Text("t", 14);
            ui.Row(static r => r.Column(static c => c.Button("OK")));
        });
        PanelElement root = Ui.Column(panel);
        IdResolver.Assign(root);
        PanelElement row = (PanelElement)panel.Children![1];          // Text=0, Row=1
        PanelElement col = (PanelElement)row.Children![0];            // Column=0
        Assert.EndsWith("/dbg/PanelElement1/PanelElement0/ButtonElement0",
            col.Children![0].ResolvedId);
    }
}
