namespace SharpUI;

/// <summary>焦点管理:焦点以稳定 id 跨帧保持;Tab 序为 DOM 序(父先子后)。
/// Radio 分组(T2):每帧 Register 时按 groupName 分桶(组表跨帧复用免重分配,列表每帧清空重注册 ——
/// 组内恒为当帧元素,与 _byId 同帧一致);FocusManager 每窗一份,分组天然按窗隔离(不落 RadioElement 静态注册表)。</summary>
public sealed class FocusManager
{
    readonly List<string> _tabOrder = new();
    readonly Dictionary<string, Element> _byId = new();
    readonly Dictionary<string, List<RadioElement>> _radioGroups = new();
    string? _focusedId;

    public string? FocusedId => _focusedId;
    public Element? FocusedThisFrame { get; private set; }

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
                    _focusedId = null;
                    break;
                case Key.Enter:
                case Key.KeypadEnter:
                case Key.Space:
                    if (_focusedId is not null
                        && _byId.TryGetValue(_focusedId, out Element? e)
                        && e is IActivatable act && act.CanActivate)
                    {
                        act.InvokeClick();
                        if (session is not null && e.ResolvedId is string kbId)
                            Interaction.QueueClick(session, kbId);   // 裁定#18:下一帧账本可见
                    }
                    break;
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
    /// 原语义(Radio 组内环移 / 无操作)。禁用元素由 HandleKey 自行拒消费(返回 false)。</summary>
    bool SteerFocused(Key key)
    {
        if (_focusedId is null || !_byId.TryGetValue(_focusedId, out Element? e)) return false;
        return e is IKeySteerable steerable && steerable.HandleKey(key);
    }

    void Cycle(bool backward)
    {
        if (_tabOrder.Count == 0) return;
        int index = _focusedId is null ? -1 : _tabOrder.IndexOf(_focusedId);
        index = backward
            ? (index <= 0 ? _tabOrder.Count - 1 : index - 1)
            : (index + 1) % _tabOrder.Count;
        _focusedId = _tabOrder[index];
    }

    /// <summary>Radio 方向键组内循环(T2):仅聚焦元素为 RadioElement 时消费方向键,焦点在同 groupName 内
    /// 环形移动(Left/Up 反向,Right/Down 正向);禁用项(Focusable=false)跳过,组内全禁用即原地不动。
    /// 方向键只移焦 —— 选中仍由 Space/Enter 经 IActivatable 承担(HTML 原生"方向键即选中"不采纳:
    /// 与本库"激活显式化"的 Button 键盘语义一致)。</summary>
    void CycleRadio(bool backward)
    {
        if (_focusedId is null || !_byId.TryGetValue(_focusedId, out Element? e)
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
