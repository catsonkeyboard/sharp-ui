namespace SharpUI;

/// <summary>焦点管理:焦点以稳定 id 跨帧保持;Tab 序为 DOM 序(父先子后)。
/// Radio 分组(T2):每帧 Register 时按 groupName 分桶(组表跨帧复用免重分配,列表每帧清空重注册 ——
/// 组内恒为当帧元素,与 _byId 同帧一致);FocusManager 每窗一份,分组天然按窗隔离(不落 RadioElement 静态注册表)。
/// D5 焦点围栏(F-2):PushScope/PopScope 栈式围栏 —— Tab/方向键导航与激活限制在栈顶 scope 子树内,
/// 出栈归还 push 时焦点;围栏跨帧存活(BeginFrame 只清当帧注册表,不清围栏栈)。</summary>
public sealed class FocusManager
{
    readonly List<string> _tabOrder = new();
    readonly Dictionary<string, Element> _byId = new();
    readonly Dictionary<string, List<RadioElement>> _radioGroups = new();
    readonly List<(string ScopeId, string? SavedFocusId)> _scopes = new();
    string? _focusedId;

    /// <summary>本窗 SDL 句柄(D5):Esc 优先路由该窗浮层栈(PopupHost 按窗服务定位);
    /// App.CreateWindow 接线,缺省 IntPtr.Zero = 无浮层耦合(纯单测模式,既有语义不变)。</summary>
    internal IntPtr SdlWindow;

    /// <summary>句柄 → 该窗 FocusManager 注册表(D5 T3:Ui.ShowMenu(IntPtr) 命令式 API 的围栏服务定位,
    /// 同 PopupHost 按窗定位惯例)。App.CreateWindow 绑定/窗销毁解绑;合成测试经 BindWindow 注入假句柄。</summary>
    static readonly Dictionary<IntPtr, FocusManager> _byWindow = new();

    internal static void BindWindow(IntPtr sdlWindow, FocusManager focus) => _byWindow[sdlWindow] = focus;

    internal static void UnbindWindow(IntPtr sdlWindow) => _byWindow.Remove(sdlWindow);

    internal static FocusManager? ForWindow(IntPtr sdlWindow) =>
        _byWindow.TryGetValue(sdlWindow, out FocusManager? f) ? f : null;

    public string? FocusedId => _focusedId;
    public Element? FocusedThisFrame { get; private set; }

    /// <summary>栈顶围栏 scope(null=无围栏;D5 F-2)。</summary>
    public string? ActiveScopeId => _scopes.Count > 0 ? _scopes[^1].ScopeId : null;

    /// <summary>围栏入栈(D5 F-2):此后 Tab/方向键导航与激活限制在 scopeId 子树内(scopeId 须为该子树根的
    /// 稳定 id —— 浮层根经 PopupHost.Push 指派 "/键" 形态,子树 id 天然带 scopeId+"/" 前缀);
    /// push 时 FocusedId 记账,PopScope 归还。打开浮层的控件在 Push 后自行 SetFocus 入圈。</summary>
    public void PushScope(string scopeId) => _scopes.Add((scopeId, _focusedId));

    /// <summary>围栏出栈:焦点归还 push 时的 FocusedId(打开者);空栈为无操作。
    /// 盲弹契约:调用方保证目标即栈顶围栏(浮层自身是当前顶层的常规关闭路径)。</summary>
    public void PopScope()
    {
        if (_scopes.Count == 0) return;
        _focusedId = _scopes[^1].SavedFocusId;
        _scopes.RemoveAt(_scopes.Count - 1);
    }

    /// <summary>围栏按身份出栈(D5 T2 评审承重):自栈顶向下首个 ScopeId 匹配项移除(其余围栏栈序保持),
    /// 焦点归还该围栏 push 时的记账 —— 下层浮层先行关闭(Dialog 下叠 Menu 时 Dialog.Close)不误弹上层
    /// 围栏。未命中为无操作。scopeId 唯一性由 PopupHost.Push 的浮层根 id 指派保证("/__popupN")。</summary>
    public void PopScopeOf(string scopeId)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (string.Equals(_scopes[i].ScopeId, scopeId, StringComparison.Ordinal))
            {
                _focusedId = _scopes[i].SavedFocusId;
                _scopes.RemoveAt(i);
                return;
            }
    }

    public void BeginFrame()
    {
        _tabOrder.Clear();
        _byId.Clear();
        foreach (List<RadioElement> group in _radioGroups.Values) group.Clear();
        FocusedThisFrame = null;
    }

    public void Register(Element e)
    {
        if (string.IsNullOrEmpty(e.ResolvedId)) return;
        _byId[e.ResolvedId!] = e;
        if (e.Focusable) _tabOrder.Add(e.ResolvedId!);
        if (e is RadioElement radio)
        {
            if (!_radioGroups.TryGetValue(radio.GroupName, out List<RadioElement>? group))
                _radioGroups[radio.GroupName] = group = new List<RadioElement>(4);
            group.Add(radio);
        }
    }

    public void SetFocus(Element e) => _focusedId = e.ResolvedId;

    public void Clear() => _focusedId = null;

    public bool IsFocused(Element e) =>
        e.ResolvedId is not null && e.ResolvedId == _focusedId;

    /// <summary>键路由(Tab/Escape/激活/Radio 方向键/Slider 方向微调)。session 为 null 时激活仍触发 InvokeClick,但跳过 QueueClick(纯焦点单测免会话)。</summary>
    public void HandleKeys(ReadOnlySpan<KeyEventData> keys, InputSession? session = null)
    {
        foreach (KeyEventData ke in keys)
        {
            if (!ke.Down) continue;
            switch (ke.Key)
            {
                case Key.Tab:
                    Cycle(ke.Shift);
                    break;
                case Key.Escape:
                    // D5:浮层栈非空时 Esc 先服务浮层 —— 栈顶非 Tooltip 的 OnEscape 钩子优先(T2 Dialog
                    // IsCancel=Esc 等效,合成点击+关闭),未消费则弹自顶向下首个非 Tooltip;均不落清焦点
                    //(焦点归还由浮层 OnClosed 内的 PopScope 收尾,T2/T3 接线)。栈空保持既有语义:
                    // 清焦点。Esc 无其他消费者(TextEditing/TextField 无 Esc 分支,已核实无冲突)。
                    if (SdlWindow != IntPtr.Zero && PopupHost.Count(SdlWindow) > 0)
                    {
                        if (!PopupHost.TryConsumeEscape(SdlWindow))
                            PopupHost.PopTopNonTooltip(SdlWindow);
                    }
                    else
                        _focusedId = null;
                    break;
                case Key.Enter:
                case Key.KeypadEnter:
                case Key.Space:
                {
                    bool invoked = false;
                    if (_focusedId is not null && InsideActiveScope(_focusedId)
                        && _byId.TryGetValue(_focusedId, out Element? e)
                        && e is IActivatable act && act.CanActivate)
                    {
                        act.InvokeClick();
                        if (session is not null && e.ResolvedId is string kbId)
                            Interaction.QueueClick(session, kbId);   // 裁定#18:下一帧账本可见
                        invoked = true;
                    }
                    // D5 T2:IsDefault=Enter —— 焦点不在可激活元素(TextField/无焦/圈外)时落栈顶非
                    // Tooltip 浮层的缺省动作钩子(Dialog 默认按钮);Space 不触发(空格=激活聚焦元素
                    // 既有语义);无浮层/未设钩子返回 false=既有 no-op。
                    if (!invoked && ke.Key != Key.Space && SdlWindow != IntPtr.Zero)
                        PopupHost.TryConsumeEnterDefault(SdlWindow);
                    break;
                }
                case Key.Left:
                case Key.Up:
                    if (!SteerFocused(ke.Key)) CycleRadio(backward: true);
                    break;
                case Key.Right:
                case Key.Down:
                    if (!SteerFocused(ke.Key)) CycleRadio(backward: false);
                    break;
                case Key.Home:
                case Key.End:
                    SteerFocused(ke.Key);   // 方向微调(T3 Slider);非 IKeySteerable 元素无 Home/End 语义(原未处理)
                    break;
            }
        }
        FocusedThisFrame = _focusedId is not null && _byId.TryGetValue(_focusedId, out Element? cur)
            ? cur
            : null;
    }

    /// <summary>方向键/Home/End 微调路由(T3):聚焦元素实现 IKeySteerable(SliderElement)则先予消费,
    /// 返回 true 即短路 —— Slider 聚焦时方向键调值不移焦;非 Steerable(Radio/Button/Checkbox)保持
    /// 原语义(Radio 组内环移 / 无操作)。禁用元素由 HandleKey 自行拒消费(返回 false)。
    /// D5:聚焦元素在围栏外时拒微调(围栏外交互整体屏蔽,返回 false 落到同样被栅的 CycleRadio)。</summary>
    bool SteerFocused(Key key)
    {
        if (_focusedId is null || !InsideActiveScope(_focusedId)
            || !_byId.TryGetValue(_focusedId, out Element? e)) return false;
        return e is IKeySteerable steerable && steerable.HandleKey(key);
    }

    void Cycle(bool backward)
    {
        if (_tabOrder.Count == 0) return;
        if (_scopes.Count > 0) { CycleScoped(backward); return; }
        int index = _focusedId is null ? -1 : _tabOrder.IndexOf(_focusedId);
        index = backward
            ? (index <= 0 ? _tabOrder.Count - 1 : index - 1)
            : (index + 1) % _tabOrder.Count;
        _focusedId = _tabOrder[index];
    }

    /// <summary>围栏内 Tab(D5 F-2):焦点只在栈顶 scope 子树的 Tab 序内环形移动;圈外/无焦时入圈
    /// (正向=圈首,反向=圈末);圈内无可聚焦元素为无操作。两遍扫描零分配(同帧 Tab 序条目数有限)。</summary>
    void CycleScoped(bool backward)
    {
        string? first = null, last = null;
        int n = 0, k = -1;
        for (int i = 0; i < _tabOrder.Count; i++)
        {
            string id = _tabOrder[i];
            if (!InsideActiveScope(id)) continue;
            first ??= id;
            last = id;
            if (id == _focusedId) k = n;
            n++;
        }
        if (n == 0) return;
        if (k < 0) { _focusedId = backward ? last : first; return; }   // 圈外/无焦 → 入圈
        if (n == 1) return;                                            // 单项原地
        int target = backward ? (k == 0 ? n - 1 : k - 1) : (k == n - 1 ? 0 : k + 1);
        int seen = 0;
        for (int i = 0; i < _tabOrder.Count; i++)
        {
            string id = _tabOrder[i];
            if (!InsideActiveScope(id)) continue;
            if (seen++ == target) { _focusedId = id; return; }
        }
    }

    /// <summary>id 是否在栈顶围栏 scope 内(无围栏恒 true;scope 根自身=圈):前缀匹配带 "/" 分隔判断,
    /// 防 "/scope" 误吞 "/scopeX" 形邻;StartsWith+下标查零分配。</summary>
    bool InsideActiveScope(string? id)
    {
        if (_scopes.Count == 0) return true;
        if (id is null) return false;
        string scope = _scopes[^1].ScopeId;
        return id.StartsWith(scope, StringComparison.Ordinal)
            && (id.Length == scope.Length || id[scope.Length] == '/');
    }

    /// <summary>Radio 方向键组内循环(T2):仅聚焦元素为 RadioElement 时消费方向键,焦点在同 groupName 内
    /// 环形移动(Left/Up 反向,Right/Down 正向);禁用项(Focusable=false)跳过,组内全禁用即原地不动。
    /// 方向键只移焦 —— 选中仍由 Space/Enter 经 IActivatable 承担(HTML 原生"方向键即选中"不采纳:
    /// 与本库"激活显式化"的 Button 键盘语义一致)。</summary>
    void CycleRadio(bool backward)
    {
        if (_focusedId is null || !InsideActiveScope(_focusedId)
            || !_byId.TryGetValue(_focusedId, out Element? e)
            || e is not RadioElement radio
            || !_radioGroups.TryGetValue(radio.GroupName, out List<RadioElement>? group)
            || group.Count < 2)
            return;
        int index = group.FindIndex(r => r.ResolvedId == radio.ResolvedId);
        if (index < 0) return;
        for (int step = 1; step <= group.Count; step++)
        {
            RadioElement candidate = group[(index + (backward ? group.Count - step : step)) % group.Count];
            if (candidate.Focusable && candidate.ResolvedId is not null)
            {
                SetFocus(candidate);
                return;
            }
        }
    }
}

public static class TreeWalk
{
    public static void Register(Element root, FocusManager focus)
    {
        focus.Register(root);
        if (root.Children is not { Count: > 0 }) return;
        foreach (Element child in root.Children)
            Register(child, focus);
    }

    /// <summary>按稳定 id 深度优先查找元素(先子后己,内层优先);id 为 null 或未命中返回 null(D22 键路由)。</summary>
    public static Element? Find(Element root, string? id)
    {
        if (id is null) return null;
        if (root.Children is { Count: > 0 })
            foreach (Element child in root.Children)
            {
                if (Find(child, id) is { } found) return found;
            }
        return root.ResolvedId == id ? root : null;
    }
}
