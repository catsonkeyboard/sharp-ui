using Veldrid;

namespace SharpUI;

/// <summary>键盘事件(按时间序)。Key 为 Veldrid.Key(公开面技术债,规格书 D12)。</summary>
public readonly record struct KeyEventData(Key Key, bool Down, bool Shift);

/// <summary>App 从 SDL InputSnapshot 经 InputMapper 映射;LeftHeld 为持久按键态(D15);WheelDelta 为滚轮增量(D19)。</summary>
public readonly record struct RawInput(
    PointF Mouse, bool[] LeftEvents, KeyEventData[]? KeyEvents = null, bool LeftHeld = false,
    float WheelDelta = 0f);

public readonly record struct InputState(
    PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased,
    Element? Hovered, Element? Focused = null,
    string? HoveredId = null, string? FocusedId = null,
    /// <summary>本帧完成点击的 id 账本(指针路径当帧;键盘激活经队列下一帧可见,裁定#18)。调用方不得跨帧持有该列表(别名复用内部缓冲)。</summary>
    IReadOnlyList<string>? ClickedIds = null,
    /// <summary>滚轮增量(D19):App 在构建前对上帧树调 Scroll.Dispatch 消费。</summary>
    float WheelDelta = 0f)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

public static class Interaction
{
    static readonly List<string> _clicks = new();
    static readonly List<string> _queued = new();   // 键盘激活排队:下一帧 Update 开头并入账本(裁定#18)
    static string? _pressedOn;   // 按下瞬间的命中元素稳定 id;点击 = 按下与松开同 id(规格书 D10)

    /// <summary>取消进行中的按压(窗口失焦时由 App 调用;规格书 M2b-i)。</summary>
    public static void CancelPress() => _pressedOn = null;

    /// <summary>键盘激活入队:HandleKeys 当帧回调已触发,账本在下一帧 Update 开头可见(裁定#18)。</summary>
    public static void QueueClick(string id) => _queued.Add(id);

    public static InputState Update(Element root, in RawInput raw, FocusManager? focus = null)
    {
        // 防御(偏离任务书代码清单的一行):未经 Assign 的树直连 Update 时补算稳定 id,
        // 否则 ResolvedId 全 null,`_pressedOn is not null` 门会把所有点击吞掉。
        // App.Run 恒以上帧树(构建时已 Assign)调 Update,生产路径此行零命中。
        if (root.ResolvedId is null) IdResolver.Assign(root);
        bool pressed = false, released = false;
        Element? hovered = root.HitTest(raw.Mouse);
        _clicks.Clear();
        if (_queued.Count > 0)   // 键盘激活(裁定#18):先清当帧再并入上一帧队列
        {
            _clicks.AddRange(_queued);
            _queued.Clear();
        }
        foreach (bool down in raw.LeftEvents)
        {
            if (down)
            {
                pressed = true;
                _pressedOn = hovered?.ResolvedId;
                if (hovered is { Focusable: true })
                    focus?.SetFocus(hovered);
                if (hovered is null)
                    focus?.Clear();
            }
            else
            {
                released = true;
                if (_pressedOn is not null && hovered?.ResolvedId == _pressedOn
                    && hovered is ButtonElement button)
                {
                    button.InvokeClick();
                    if (hovered.ResolvedId is string id) _clicks.Add(id);
                }
                _pressedOn = null;
            }
        }
        bool leftDown = (raw.LeftHeld && !released) || pressed;
        return new InputState(
            raw.Mouse, leftDown, pressed, released, hovered,
            HoveredId: hovered?.ResolvedId,
            ClickedIds: _clicks.Count > 0 ? _clicks : null,
            WheelDelta: raw.WheelDelta);
    }
}
