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

    // ---- T3 承重携带:按身份 Remove(D5 T2 评审:叠栈下盲弹 Pop 会弹错条目)----

    [Fact]
    public void Remove_ByInstance_PopsMiddleKeepsOrder_AbsentIsNoOp()
    {
        IntPtr w = Win(10);
        PopupHost.Clear(w);
        int aClosed = 0, bClosed = 0, cClosed = 0;
        Popup a = MenuAt(Ui.Panel(), new RectF(0, 0, 10, 10), onClosed: () => aClosed++);
        Popup b = MenuAt(Ui.Panel(), new RectF(0, 0, 10, 10), onClosed: () => bClosed++);
        Popup c = MenuAt(Ui.Panel(), new RectF(0, 0, 10, 10), onClosed: () => cClosed++);
        PopupHost.Push(w, a);
        PopupHost.Push(w, b);
        PopupHost.Push(w, c);

        PopupHost.Remove(w, b);                            // 中位按实例移除(引用相等)
        Assert.Equal(2, PopupHost.Count(w));
        Assert.Same(a, PopupHost.StackOf(w)[0]);
        Assert.Same(c, PopupHost.StackOf(w)[1]);           // 其余栈序保持
        Assert.Equal(1, bClosed);
        Assert.Equal(0, aClosed);
        Assert.Equal(0, cClosed);

        PopupHost.Remove(w, b);                            // 不在栈 = 无操作(幂等:外点 Pop 与自 Remove 可同帧同目标)
        Assert.Equal(2, PopupHost.Count(w));
        Assert.Equal(1, bClosed);
        PopupHost.Clear(w);
    }

    [Fact]
    public void PopScopeOf_RemovesByIdentity_DeepScopeKeepsTopFence()
    {
        int aClicks = 0;
        (PanelElement root, PanelElement _, ButtonElement a, ButtonElement _, ButtonElement c, ButtonElement d)
            = FencedTree(() => aClicks++);
        var focus = new FocusManager();
        focus.BeginFrame();
        TreeWalk.Register(root, focus);

        focus.SetFocus(a);
        focus.PushScope("/s1");                            // 下层浮层(Dialog 类)围栏
        focus.SetFocus(c);
        focus.PushScope("/s2");                            // 上层浮层(Menu 类)围栏
        focus.SetFocus(d);
        Assert.Equal("/s2", focus.ActiveScopeId);

        focus.PopScopeOf("/s1");                           // 下层先行关闭:按身份出栈,不误弹上层
        Assert.Equal("/s2", focus.ActiveScopeId);          // 上层围栏仍在
        focus.HandleKeys(new[] { new KeyEventData(Key.Enter, true, false) });
        Assert.Equal(0, aClicks);                          // 归还焦点(a)在 s2 圈外:激活被围栏屏蔽
        Assert.True(focus.IsFocused(a));                   // 归还 s1 push 时的记账焦点

        focus.PopScope();                                  // 上层正常关闭(栈顶盲弹语义不变)
        Assert.Null(focus.ActiveScopeId);
        Assert.True(focus.IsFocused(c));                   // 归还 s2 push 时的记账焦点
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

    // ---- D6 T2: 浮层滚轮栅门 + 定位窗收敛 ----

    static ScrollElement TallScroll(string key, int rows = 10, float rowH = 30f)
    {
        ScrollElement s = Ui.Scroll();
        s.Key = key;
        s.Style.FlexBasis = 100f;
        for (int i = 0; i < rows; i++)
            s.Add(new Spacer(new SizeF(50, rowH)));
        return s;
    }

    [Fact]
    public void Wheel_ModalGate_DialogWithoutScrollSwallows_DialogWithScrollScrollsDialogOnly_TooltipPassesThrough()
    {
        IntPtr w = Win(11);
        PopupHost.Clear(w);
        App.State = new StateStore();
        try
        {
            ScrollElement declaredScroll = TallScroll("decl");
            PanelElement root = Ui.Column(declaredScroll);
            root.Style.CrossAlign = CrossAxisAlignment.Stretch;
            LayoutEngine.Arrange(root, new RectF(0, 0, 200, 100));
            IdResolver.Assign(root);
            string declKey = "scroll:" + declaredScroll.ResolvedId;
            var wheelInput = new InputState(new PointF(50, 50), false, false, false, null, WheelDelta: 1f);

            // 1) 声明树含 Scroll(初始 offset 0)+ Dialog 打开(无 Scroll)→ 合成 Wheel → 声明树 Scroll offset 仍为 0(被吞)
            var dlgNoScroll = new Popup(Ui.Column(Ui.Text("无滚动弹窗")), new RectF(0, 0, 200, 100), PopupKind.Dialog);
            LayoutEngine.Arrange(dlgNoScroll.Root, new RectF(0, 0, 200, 100));
            PopupHost.Push(w, dlgNoScroll);
            Scroll.Dispatch(root, in wheelInput, w);
            Assert.Equal(0f, App.State.Get(declKey, 0f));
            PopupHost.Pop(w);

            // 2) Dialog 内含 Scroll → 合成 Wheel → Dialog 内 Scroll offset 变化、声明树不变
            ScrollElement dialogScroll = TallScroll("dlgScroll");
            PanelElement dlgRoot = Ui.Column(dialogScroll);
            dlgRoot.Style.CrossAlign = CrossAxisAlignment.Stretch;
            var dlgWithScroll = new Popup(dlgRoot, new RectF(0, 0, 200, 100), PopupKind.Dialog);
            PopupHost.Push(w, dlgWithScroll);
            LayoutEngine.Arrange(dlgWithScroll.Root, new RectF(0, 0, 200, 100));
            string dlgKey = "scroll:" + dialogScroll.ResolvedId;
            Scroll.Dispatch(root, in wheelInput, w);
            Assert.Equal(40f, App.State.Get(dlgKey, 0f));
            Assert.Equal(0f, App.State.Get(declKey, 0f));
            PopupHost.Pop(w);

            // 3) 仅 Tooltip 在顶 → 声明树 Scroll 正常滚动(穿透)
            var tip = new Popup(Ui.Column(Ui.Text("提示")), new RectF(0, 0, 80, 24), PopupKind.Tooltip);
            LayoutEngine.Arrange(tip.Root, new RectF(0, 0, 80, 24));
            PopupHost.Push(w, tip);
            Scroll.Dispatch(root, in wheelInput, w);
            Assert.Equal(40f, App.State.Get(declKey, 0f));
        }
        finally
        {
            App.State = null;
            PopupHost.Clear(w);
        }
    }

    [Fact]
    public void BindWindow_SyncsFocusSdlWindow_UnbindClears_OwnerFocusBridgesToHandle()
    {
        IntPtr w = Win(12);
        FocusManager.UnbindWindow(w);
        var focus = new FocusManager();
        Assert.Equal(IntPtr.Zero, focus.SdlWindow);

        FocusManager.BindWindow(w, focus);
        Assert.Equal(w, focus.SdlWindow);                    // BindWindow 自动同步置 SdlWindow 唯一真相
        Assert.Same(focus, FocusManager.ForWindow(w));

        (PanelElement root, ButtonElement btn) = Declared("B", () => { });
        focus.BeginFrame();
        TreeWalk.Register(root, focus);
        Assert.Same(focus, btn.OwnerFocus);                  // Element.OwnerFocus 回链
        Assert.Equal(w, btn.OwnerFocus!.SdlWindow);          // 声明式元素统一经 OwnerFocus.SdlWindow 定位窗句柄

        FocusManager.UnbindWindow(w);
        Assert.Equal(IntPtr.Zero, focus.SdlWindow);          // UnbindWindow 清零
        Assert.Null(FocusManager.ForWindow(w));
    }
}
