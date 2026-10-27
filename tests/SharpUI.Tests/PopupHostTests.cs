using SharpUI;

namespace SharpUI.Tests;

/// <summary>D5 T1(PopupHost 浮层栈 F-1 + 焦点围栏 F-2):合成事件经 Interaction.Update、键盘经
/// FocusManager.HandleKeys(模式同 ControlsTests)。PopupHost 为静态按窗服务定位 —— 测试以 IntPtr
/// 假句柄驱动栈逻辑(不触原生);每测独立句柄 + 起手 Clear 防跨测残留(collection=interaction 串行)。</summary>
[Collection("interaction")]
public class PopupHostTests
{
    /// <summary>每测 +N 的假句柄(互不重叠;IntPtr 值仅作字典键,不触原生)。</summary>
    static IntPtr Win(int n) => (IntPtr)(0xD50100 + n);

    /// <summary>声明树:全宽按钮(高约 100;Arrange 后命中 y∈(0,100))。</summary>
    static (PanelElement Root, ButtonElement Button) Declared(string label, Action onClick)
    {
        ButtonElement btn = new(label, onClick);
        PanelElement root = Ui.Column(btn);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 200));
        IdResolver.Assign(root);
        return (root, btn);
    }

    /// <summary>浮层替身:根 Column 包 content 并 Arrange 于 frame(稳定 id 由 PopupHost.Push 指派)。</summary>
    static Popup MenuAt(Element content, RectF frame, bool dismiss = true, Action? onClosed = null)
    {
        PanelElement root = Ui.Column(content);
        root.Style.CrossAlign = CrossAxisAlignment.Stretch;
        LayoutEngine.Arrange(root, frame);
        return new Popup(root, frame, PopupKind.Menu) { DismissOnOutsideClick = dismiss, OnClosed = onClosed };
    }

    // ---- F-1 栈语义 ----

    [Fact]
    public void Stack_PushPopLifo_OnClosedFires_PerWindowIsolation()
    {
        IntPtr a = Win(1), b = Win(2);
        PopupHost.Clear(a);
        PopupHost.Clear(b);
        int bottomClosed = 0, topClosed = 0;
        Popup bottom = MenuAt(Ui.Panel(), new RectF(0, 0, 10, 10), onClosed: () => bottomClosed++);
        Popup top = MenuAt(Ui.Panel(), new RectF(0, 0, 10, 10), onClosed: () => topClosed++);

        PopupHost.Push(a, bottom);
        PopupHost.Push(a, top);
        PopupHost.Push(b, bottom);
        Assert.Equal(2, PopupHost.Count(a));
        Assert.Equal(1, PopupHost.Count(b));
        Assert.Same(bottom, PopupHost.StackOf(a)[0]);   // 栈底=先进
        Assert.Same(top, PopupHost.StackOf(a)[^1]);     // 栈顶=后进

        PopupHost.Pop(a);
        Assert.Equal(1, PopupHost.Count(a));
        Assert.Same(bottom, PopupHost.StackOf(a)[0]);   // 后进先出
        Assert.Equal(1, topClosed);                     // Pop 触发栈顶 OnClosed
        Assert.Equal(0, bottomClosed);
        Assert.Equal(1, PopupHost.Count(b));            // 两窗隔离:a 的 Pop 不触 b

        PopupHost.Pop(a);
        Assert.Equal(0, PopupHost.Count(a));
        Assert.Equal(1, bottomClosed);
        PopupHost.Clear(b);
    }

    [Fact]
    public void HitTest_PopupOpen_DeclaredTreeShadowed_PopupHitWins()
    {
        IntPtr w = Win(3);
        PopupHost.Clear(w);
        (PanelElement root, ButtonElement _) = Declared("声明", () => { });
        ButtonElement item = new("项", () => { });
        PopupHost.Push(w, MenuAt(item, new RectF(0, 100, 100, 40)));
        var session = new InputSession { SdlWindow = w };

        InputState overDeclared = Interaction.Update(session, root,
            new RawInput(new PointF(50, 5), Array.Empty<bool>()));
        Assert.Null(overDeclared.HoveredId);                  // 落声明树元素 → null(栈顶浮层独占命中)

        InputState overPopup = Interaction.Update(session, root,
            new RawInput(new PointF(50, 108), Array.Empty<bool>()));
        Assert.Equal(item.ResolvedId, overPopup.HoveredId);   // 落浮层元素 → 浮层元素

        PopupHost.Pop(w);                                     // 栈空后同位置恢复声明树命中(既有行为回归)
        InputState afterClose = Interaction.Update(session, root,
            new RawInput(new PointF(50, 5), Array.Empty<bool>()));
        Assert.NotNull(afterClose.HoveredId);
        PopupHost.Clear(w);
    }

    [Fact]
    public void OutsideClick_Dismisible_PopsWithOnClosed_ConsumesPress_InsideClickKeeps()
    {
        IntPtr w = Win(4);
        PopupHost.Clear(w);
        int declaredClicks = 0, itemClicks = 0, closed = 0;
        (PanelElement root, ButtonElement _) = Declared("声明", () => declaredClicks++);
        var session = new InputSession { SdlWindow = w };

        PopupHost.Push(w, MenuAt(new ButtonElement("项", () => itemClicks++),
            new RectF(0, 100, 100, 40), onClosed: () => closed++));
        Interaction.Update(session, root, new RawInput(new PointF(50, 5), new[] { true }));    // 浮层外按压
        Interaction.Update(session, root, new RawInput(new PointF(50, 5), new[] { false }));
        Assert.Equal(0, PopupHost.Count(w));            // → Pop
        Assert.Equal(1, closed);                        // → OnClosed
        Assert.Equal(0, declaredClicks);                // 按压被消费:声明按钮不激活(模态语义)

        PopupHost.Push(w, MenuAt(new ButtonElement("项2", () => itemClicks++),
            new RectF(0, 100, 100, 40), onClosed: () => closed++));
        Interaction.Update(session, root, new RawInput(new PointF(50, 108), new[] { true }));  // 浮层内按压
        Interaction.Update(session, root, new RawInput(new PointF(50, 108), new[] { false }));
        Assert.Equal(1, PopupHost.Count(w));            // 点浮层内不 Pop
        Assert.Equal(1, itemClicks);                    // 浮层元素正常激活
        PopupHost.Clear(w);
    }

    [Fact]
    public void OutsideClick_NonDismissible_SwallowsPress_NoPop()
    {
        IntPtr w = Win(5);
        PopupHost.Clear(w);
        int declaredClicks = 0, closed = 0;
        (PanelElement root, ButtonElement _) = Declared("声明", () => declaredClicks++);
        var session = new InputSession { SdlWindow = w };
        PopupHost.Push(w, MenuAt(Ui.Text("模态"), new RectF(0, 100, 100, 40),
            dismiss: false, onClosed: () => closed++));

        Interaction.Update(session, root, new RawInput(new PointF(50, 5), new[] { true }));
        Interaction.Update(session, root, new RawInput(new PointF(50, 5), new[] { false }));
        Assert.Equal(1, PopupHost.Count(w));            // 不 Pop
        Assert.Equal(0, closed);
        Assert.Equal(0, declaredClicks);                // 按压被吞:模态外点击无激活
        PopupHost.Clear(w);
    }

    // ---- Esc 路由(F-1)----

    [Fact]
    public void Escape_WithPopup_PopsTop_DoesNotClearFocus()
    {
        IntPtr w = Win(6);
        PopupHost.Clear(w);
        (PanelElement root, ButtonElement a) = Declared("A", () => { });
        var focus = new FocusManager { SdlWindow = w };
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        focus.SetFocus(a);

        int closed = 0;
        PopupHost.Push(w, MenuAt(Ui.Panel(), new RectF(0, 0, 10, 10), onClosed: () => closed++));
        focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(0, PopupHost.Count(w));            // Esc → Pop 栈顶
        Assert.Equal(1, closed);
        Assert.True(focus.IsFocused(a));                // 浮层 Esc 不清焦点(归还=OnClosed 内 PopScope,T2 接线)

        focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Null(focus.FocusedId);                   // 栈空回归既有语义:Esc 清焦点
        PopupHost.Clear(w);
    }

    [Fact]
    public void Escape_TooltipExempt_SkipsPastToNonTooltipBelow()
    {
        IntPtr w = Win(7);
        PopupHost.Clear(w);
        var focus = new FocusManager { SdlWindow = w };

        int tipClosed = 0;
        PopupHost.Push(w, new Popup(Ui.Panel(), new RectF(0, 0, 10, 10), PopupKind.Tooltip)
        { OnClosed = () => tipClosed++ });
        focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(1, PopupHost.Count(w));            // Tooltip 不响应 Esc
        Assert.Equal(0, tipClosed);

        int menuClosed = 0;                             // Tooltip 在上、Menu 在下:Esc 穿越关 Menu
        PopupHost.Push(w, MenuAt(Ui.Panel(), new RectF(0, 0, 10, 10), onClosed: () => menuClosed++));
        PopupHost.Push(w, new Popup(Ui.Panel(), new RectF(0, 0, 10, 10), PopupKind.Tooltip));
        focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(2, PopupHost.Count(w));            // 仅 Menu 出栈,两 Tooltip 留存
        Assert.Equal(1, menuClosed);
        Assert.Equal(0, tipClosed);

        focus.HandleKeys(new[] { new KeyEventData(Key.Escape, true, false) });
        Assert.Equal(2, PopupHost.Count(w));            // 全 Tooltip 栈:Esc 无操作
        PopupHost.Clear(w);
    }

    // ---- F-2 焦点围栏 ----

    static (PanelElement Root, PanelElement Scope, ButtonElement A, ButtonElement B, ButtonElement C, ButtonElement D)
        FencedTree(Action onAClick)
    {
        ButtonElement a = new("A", onAClick); a.Key = "a";
        ButtonElement b = new("B", () => { }); b.Key = "b";
        ButtonElement c = new("C", () => { }); c.Key = "c";
        ButtonElement d = new("D", () => { }); d.Key = "d";
        PanelElement scope = Ui.Column(c, d); scope.Key = "scope";
        PanelElement root = Ui.Column(a, b, scope);
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 400));
        IdResolver.Assign(root);
        return (root, scope, a, b, c, d);
    }

    [Fact]
    public void FocusScope_TabCyclesWithinScope_PopScopeRestoresOpenerFocus()
    {
        (PanelElement root, PanelElement scope, ButtonElement a, ButtonElement b, ButtonElement c, ButtonElement d)
            = FencedTree(() => { });
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);

        Assert.Null(focus.ActiveScopeId);
        focus.SetFocus(a);                                   // 打开者焦点
        focus.PushScope(scope.ResolvedId!);
        Assert.Equal(scope.ResolvedId, focus.ActiveScopeId);

        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(c));                     // 圈外聚焦 → Tab 入圈首
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(d));
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(c));                     // 环回不出圈(永不到 a/b)
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, true) });
        Assert.True(focus.IsFocused(d));                     // Shift+Tab 反向

        focus.PopScope();
        Assert.Null(focus.ActiveScopeId);
        Assert.True(focus.IsFocused(a));                     // 归还 push 时的焦点(打开者)
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(b));                     // 出栈恢复全局 Tab 序
    }

    [Fact]
    public void FocusScope_BackwardFromNullEntersLast_OutsideActivationBlocked()
    {
        int aClicks = 0;
        (PanelElement root, PanelElement scope, ButtonElement a, ButtonElement _, ButtonElement c, ButtonElement d)
            = FencedTree(() => aClicks++);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);

        focus.PushScope(scope.ResolvedId!);                  // 无焦入圈
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, true) });
        Assert.True(focus.IsFocused(d));                     // 反向入圈末
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(c));                     // 正向环回
        focus.PopScope();
        Assert.Null(focus.FocusedId);                        // 归还 push 时的 null

        focus.SetFocus(a);
        focus.PushScope(scope.ResolvedId!);
        focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        focus.HandleKeys(new[] { new KeyEventData(Key.Space, true, false) });
        Assert.Equal(0, aClicks);                            // 圈外激活被围栏屏蔽
        focus.PopScope();
        focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        Assert.Equal(1, aClicks);                            // 出栈恢复激活
    }

    /// <summary>App.Run 组合模式:声明树+浮层树依次 Register、浮层根 PushScope —— Tab 只在浮层内循环。</summary>
    [Fact]
    public void PopupRegistration_Composition_ScopeFencesTabInsidePopupTree()
    {
        IntPtr w = Win(9);
        PopupHost.Clear(w);
        (PanelElement root, ButtonElement _) = Declared("声明", () => { });
        ButtonElement ok = new("确定", () => { });
        ButtonElement cancel = new("取消", () => { });
        Popup popup = MenuAt(Ui.Column(ok, cancel), new RectF(0, 100, 100, 80));
        PopupHost.Push(w, popup);

        var focus = new FocusManager { SdlWindow = w };
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        TreeWalk.Register(popup.Root, focus);                // App.Run 接线序:声明树后逐层浮层
        focus.PushScope(popup.Root.ResolvedId!);

        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(ok));                    // Tab 入浮层首项
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(cancel));
        focus.HandleKeys(new[] { new KeyEventData(Key.Tab, true, false) });
        Assert.True(focus.IsFocused(ok));                    // 环回在浮层内,永不到声明树
        PopupHost.Clear(w);
    }
}
