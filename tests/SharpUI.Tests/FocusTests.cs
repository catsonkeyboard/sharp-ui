using SharpUI;

namespace SharpUI.Tests;

[Collection("interaction")]
public class FocusTests
{
    static (PanelElement Root, ButtonElement First, ButtonElement Second) TwoButtons()
    {
        ButtonElement a = new("A", () => { });
        ButtonElement b = new("B", () => { });
        PanelElement root = Ui.Column(a, b);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;   // Atlas-null 下按钮自身仅 32px 宽,须拉伸全宽命中 x=50(裁定#16)
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        return (root, a, b);
    }

    static FocusManager Frame(PanelElement root)
    {
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        return focus;
    }

    [Fact]
    public void PressOnButton_FocusesIt()
    {
        (PanelElement root, ButtonElement a, _) = TwoButtons();
        FocusManager focus = Frame(root);
        Interaction.Update(root, new RawInput(new PointF(50, 5), new[] { true }), focus);
        Assert.True(focus.IsFocused(a));
    }

    [Fact]
    public void Tab_CyclesForwardAndBackward()
    {
        (PanelElement root, ButtonElement a, ButtonElement b) = TwoButtons();
        FocusManager focus = Frame(root);

        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a));
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(b));
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a));       // 环回
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, true) });
        Assert.True(focus.IsFocused(b));       // Shift+Tab 反向
    }

    [Fact]
    public void EnterOrSpace_ActivatesFocusedButton()
    {
        int clicks = 0;
        ButtonElement a = new("A", () => clicks++);
        ButtonElement b = new("B", () => { });
        PanelElement root = Ui.Column(a, b);
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        FocusManager focus = Frame(root);

        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });   // 聚焦 a
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Enter, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Space, true, false) });
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void Focus_SurvivesTreeRebuild()
    {
        (PanelElement root1, ButtonElement a1, _) = TwoButtons();
        FocusManager focus = Frame(root1);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a1));

        (PanelElement root2, ButtonElement a2, _) = TwoButtons();   // 全新实例,同结构同 id
        focus.BeginFrame();
        TreeWalk.Register(root2, focus);
        focus.HandleKeys(System.Array.Empty<KeyEventData>());
        Assert.NotNull(focus.FocusedThisFrame);
        Assert.Same(a2, focus.FocusedThisFrame);   // 焦点 id 解析到新树的同位按钮
    }

    [Fact]
    public void NonFocusableElement_IsNotInTabOrder()
    {
        TextElement t = Ui.Text("hi").Focusable();
        TextElement plain = Ui.Text("plain");
        PanelElement root = Ui.Column(t, plain);
        IdResolver.Assign(root);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.Equal(t.ResolvedId, focus.FocusedId);
    }

    [Fact]
    public void Escape_UnfocusesAndEnterBecomesNoOp()
    {
        (PanelElement root, ButtonElement a, _) = TwoButtons();
        FocusManager focus = Frame(root);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Escape, true, false) });
        Assert.Null(focus.FocusedId);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Enter, true, false) });   // 无焦:不崩、无激活
        Assert.Null(focus.FocusedThisFrame);
    }

    [Fact]
    public void Enter_OnFocusedNonButton_IsNoOp()
    {
        TextElement t = Ui.Text("hi").Focusable();
        PanelElement root = Ui.Column(t);
        IdResolver.Assign(root);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Enter, true, false) });   // 聚焦非按钮:不崩
        Assert.True(focus.IsFocused(t));
    }

    [Fact]
    public void PressOnEmpty_ClearsFocus_AndCancelPressKillsPendingClick()
    {
        (PanelElement root, ButtonElement a, _) = TwoButtons();
        FocusManager focus = Frame(root);
        focus.HandleKeys(new[] { new KeyEventData(Veldrid.Key.Tab, true, false) });
        Assert.True(focus.IsFocused(a));

        Interaction.Update(root, new RawInput(new PointF(50, 190), new[] { true }), focus);  // 空白处按压(按钮高约 100)
        Assert.Null(focus.FocusedId);

        int clicks = 0;
        ButtonElement b = new("B", () => clicks++);
        PanelElement root2 = Ui.Column(b);
        root2.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root2, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root2);
        Interaction.Update(root2, new RawInput(new PointF(50, 5), new[] { true }));          // 按下
        Interaction.CancelPress();                                                            // 窗口失焦
        Interaction.Update(root2, new RawInput(new PointF(50, 5), new[] { false }));          // 迟到的释放
        Assert.Equal(0, clicks);
    }
}
