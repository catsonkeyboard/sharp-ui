using Veldrid;

namespace SharpUI;

/// <summary>焦点管理:焦点以稳定 id 跨帧保持;Tab 序为 DOM 序(父先子后)。</summary>
public sealed class FocusManager
{
    readonly List<string> _tabOrder = new();
    readonly Dictionary<string, Element> _byId = new();
    string? _focusedId;

    public string? FocusedId => _focusedId;
    public Element? FocusedThisFrame { get; private set; }

    public void BeginFrame()
    {
        _tabOrder.Clear();
        _byId.Clear();
        FocusedThisFrame = null;
    }

    public void Register(Element e)
    {
        if (string.IsNullOrEmpty(e.ResolvedId)) return;
        _byId[e.ResolvedId!] = e;
        if (e.Focusable) _tabOrder.Add(e.ResolvedId!);
    }

    public void SetFocus(Element e) => _focusedId = e.ResolvedId;

    public void Clear() => _focusedId = null;

    public bool IsFocused(Element e) =>
        e.ResolvedId is not null && e.ResolvedId == _focusedId;

    /// <summary>键路由(Tab/Escape/激活)。session 为 null 时激活仍触发 InvokeClick,但跳过 QueueClick(纯焦点单测免会话)。</summary>
    public void HandleKeys(ReadOnlySpan<KeyEventData> keys, InputSession? session = null)
    {
        foreach (KeyEventData ke in keys)
        {
            if (!ke.Down) continue;
            switch (ke.Key)
            {
                case Veldrid.Key.Tab:
                    Cycle(ke.Shift);
                    break;
                case Veldrid.Key.Escape:
                    _focusedId = null;
                    break;
                case Veldrid.Key.Enter:
                case Veldrid.Key.KeypadEnter:
                case Veldrid.Key.Space:
                    if (_focusedId is not null
                        && _byId.TryGetValue(_focusedId, out Element? e)
                        && e is ButtonElement button)
                    {
                        button.InvokeClick();
                        if (session is not null && button.ResolvedId is string kbId)
                            Interaction.QueueClick(session, kbId);   // 裁定#18:下一帧账本可见
                    }
                    break;
            }
        }
        FocusedThisFrame = _focusedId is not null && _byId.TryGetValue(_focusedId, out Element? cur)
            ? cur
            : null;
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
