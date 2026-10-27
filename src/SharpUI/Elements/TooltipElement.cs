namespace SharpUI;

/// <summary>Tooltip 浮层根(F-1/D5 T3):浮层底色圆角小面板 + 13px 文本;无焦点语义(非 Focusable,
/// Esc 经 PopTopNonTooltip 穿越)。由 TooltipService 构造并于 Push 前 Arrange 定格在锚下方;
/// 生命周期归服务(悬停计时/移出/点击移除),应用不直接触碰。</summary>
internal sealed class TooltipElement : PanelElement
{
    public TooltipElement(string text)
    {
        Background = Color32.FromRgb(0x1E1E24);   // 浮层底色(Dialog 面板同源)
        CornerRadius = 4f;
        Style.Padding = new Thickness(8f, 4f);
        IsHitTarget = true;                       // T1 携带②:浮层根恒为命中目标(空白区不误判外点)
        Add(new TextElement(text).Size(13f));
    }
}

/// <summary>Tooltip 悬停服务(F-1/D5 T3):每窗每帧一次 <see cref="Update"/>(App.Run 每窗帧尾接线;
/// 合成测试直调,同 App.FeedFocused 模式)。三段语义:
/// ① 计时:按 (窗,载体 id) 记悬停起始墙钟(Anim.Now 同源,测试经 Anim.TimeSource 注入),持续
///    ≥<see cref="HoverDelaySeconds"/> 才 Push。不用 Anim.Get 补间 —— 其首调即返回目标值
///    (ButtonElement 颜色补间无害,0.5s 门会瞬开),且目标回落重启会保留部分进度(移出再入提前开)。
/// ② 存活:Tooltip 在栈顶时命中被浮层独占(下层全体影子化),载体自身 HoveredId 不可能命中 —— 开启后
///    改几何判定:鼠标在锚 Frame 或提示 Frame 内即存活;移出或任意按压(LeftPressed)→ 按身份 Remove
///    (Tooltip 可叠于 Menu 上,盲弹会误弹 Menu)。
/// ③ 愈合/孤儿:每帧核对在栈(Interaction 外点盲弹后同步清账)与载体仍在本帧树内(声明树重建/浮层关闭
///    后的孤儿提示移除);悬停计时表同步剪枝(载体消失即重置,防虚拟化重建续跑旧表提前开)。</summary>
internal static class TooltipService
{
    internal const float HoverDelaySeconds = 0.5f;
    const float AnchorGap = 2f;   // 提示框与锚底缘的间隙(px)

    static readonly Dictionary<(IntPtr, string), double> _hoverStart = new();
    static readonly Dictionary<(IntPtr, string), (Popup Tip, Element Anchor)> _open = new();
    static readonly List<(IntPtr, string)> _prune = new();      // 复用剪枝键缓冲(零稳态分配)
    static readonly List<Element> _carriers = new();            // 复用载体收集缓冲

    /// <summary>每窗每帧驱动:declaredRoot 为该窗当帧声明树根(浮层树经 PopupHost 栈自取)。</summary>
    internal static void Update(IntPtr window, Element? declaredRoot, in InputState input)
    {
        if (window == IntPtr.Zero) return;
        double now = Anim.Now;

        // 收集期只读栈(处理期才变更 —— 栈顶浮层内的菜单项提示同样可达)
        _carriers.Clear();
        if (declaredRoot is not null) Collect(declaredRoot);
        IReadOnlyList<Popup> stack = PopupHost.StackOf(window);
        for (int i = 0; i < stack.Count; i++) Collect(stack[i].Root);

        for (int i = 0; i < _carriers.Count; i++)
        {
            Element e = _carriers[i];
            if (e.ResolvedId is not string id) continue;
            var key = (window, id);
            if (_open.TryGetValue(key, out (Popup Tip, Element Anchor) open))
            {
                if (input.LeftPressed
                    || (!e.Frame.Contains(input.Mouse) && !open.Tip.Root.Frame.Contains(input.Mouse)))
                    RemoveTip(window, key, open.Tip);
                continue;                                     // 已开:不走计时(判重)
            }
            if (input.HoveredId == id && e.Frame.Contains(input.Mouse))
            {
                if (!_hoverStart.TryGetValue(key, out double start))
                    _hoverStart[key] = start = now;
                if (now - start >= HoverDelaySeconds) PushTip(window, key, e);
            }
            else
                _hoverStart.Remove(key);                       // 移出:计时重置
        }

        Reconcile(window);
    }

    /// <summary>窗销毁清账(App.Run 移除窗/退出兜底,与 PopupHost.Clear 相邻;栈已清,不再 Remove)。</summary>
    internal static void Clear(IntPtr window)
    {
        _prune.Clear();
        foreach (KeyValuePair<(IntPtr, string), (Popup Tip, Element Anchor)> kv in _open)
            if (kv.Key.Item1 == window) _prune.Add(kv.Key);
        foreach ((IntPtr, string) k in _prune) _open.Remove(k);
        _prune.Clear();
        foreach ((IntPtr, string) k in _hoverStart.Keys)
            if (k.Item1 == window) _prune.Add(k);
        foreach ((IntPtr, string) k in _prune) _hoverStart.Remove(k);
    }

    static void PushTip(IntPtr window, (IntPtr, string) key, Element anchor)
    {
        var tip = new TooltipElement(anchor.TooltipText!);
        SizeF m = LayoutEngine.Measure(tip, new SizeF(float.PositiveInfinity, float.PositiveInfinity));
        LayoutEngine.Arrange(tip, new RectF(anchor.Frame.X, anchor.Frame.Bottom + AnchorGap, m.Width, m.Height));
        var popup = new Popup(tip, anchor.Frame, PopupKind.Tooltip);   // DismissOnOutsideClick 默认 true
        _open[key] = (popup, anchor);
        _hoverStart.Remove(key);                                     // 开启后由存活判定接管
        PopupHost.Push(window, popup);
    }

    static void RemoveTip(IntPtr window, (IntPtr, string) key, Popup tip)
    {
        _open.Remove(key);
        _hoverStart.Remove(key);
        PopupHost.Remove(window, tip);   // 按身份:叠栈下不误弹他者;已不在栈为无操作(幂等)
    }

    /// <summary>愈合与孤儿:外点盲弹已 Pop 的条目清账;载体不在当帧任何树(孤儿)按身份移除;
    /// 本窗悬停计时表同步剪枝到当帧载体集合。</summary>
    static void Reconcile(IntPtr window)
    {
        IReadOnlyList<Popup> stack = PopupHost.StackOf(window);
        _prune.Clear();
        foreach (KeyValuePair<(IntPtr, string), (Popup Tip, Element Anchor)> kv in _open)
        {
            if (kv.Key.Item1 != window) continue;
            bool inStack = false;
            for (int i = 0; i < stack.Count; i++)
                if (ReferenceEquals(stack[i], kv.Value.Tip)) { inStack = true; break; }
            if (!inStack || !_carriers.Contains(kv.Value.Anchor))
                _prune.Add(kv.Key);
        }
        foreach ((IntPtr, string) k in _prune)
            if (_open.TryGetValue(k, out (Popup Tip, Element Anchor) open))
                RemoveTip(window, k, open.Tip);   // 不在栈时 Remove 幂等,仅清账

        _prune.Clear();
        foreach ((IntPtr, string) k in _hoverStart.Keys)
            if (k.Item1 == window && !Seen(k.Item2)) _prune.Add(k);
        foreach ((IntPtr, string) k in _prune) _hoverStart.Remove(k);

        bool Seen(string id)
        {
            for (int i = 0; i < _carriers.Count; i++)
                if (_carriers[i].ResolvedId == id) return true;
            return false;
        }
    }

    /// <summary>深序收集当帧载体(TooltipText 非 null 且已 Assign)。</summary>
    static void Collect(Element e)
    {
        if (e.TooltipText is not null && e.ResolvedId is not null) _carriers.Add(e);
        if (e.Children is not null)
            for (int i = 0; i < e.Children.Count; i++)
                Collect(e.Children[i]);
    }
}
