using Veldrid;

namespace SharpUI;

/// <summary>键盘事件(按时间序)。Key 为 Veldrid.Key(公开面技术债,规格书 D12)。</summary>
public readonly record struct KeyEventData(Key Key, bool Down, bool Shift);

/// <summary>App 从 SDL InputSnapshot 映射;LeftEvents 按时间序记录左键按下(true)/抬起(false)。</summary>
public readonly record struct RawInput(PointF Mouse, bool[] LeftEvents, KeyEventData[]? KeyEvents = null);

public readonly record struct InputState(
    PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased,
    Element? Hovered, Element? Focused = null)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

public static class Interaction
{
    static bool _leftHeld;
    static string? _pressedOn;   // 按下瞬间的命中元素稳定 id;点击 = 按下与松开同 id(规格书 D10)

    public static InputState Update(Element root, in RawInput raw)
    {
        // 防御(偏离任务书代码清单的一行):未经 Assign 的树直连 Update 时补算稳定 id,
        // 否则 ResolvedId 全 null,`_pressedOn is not null` 门会把所有点击吞掉。
        // App.Run 恒为 build→Assign→Arrange→Update,生产路径此行零命中(ResolvedId 已为 "")。
        if (root.ResolvedId is null) IdResolver.Assign(root);
        bool pressed = false, released = false;
        Element? hovered = root.HitTest(raw.Mouse);
        foreach (bool down in raw.LeftEvents)
        {
            if (down)
            {
                _leftHeld = true;
                pressed = true;
                _pressedOn = hovered?.ResolvedId;
            }
            else
            {
                _leftHeld = false;
                released = true;
                if (_pressedOn is not null && hovered?.ResolvedId == _pressedOn
                    && hovered is ButtonElement button)
                {
                    button.InvokeClick();
                }
                _pressedOn = null;
            }
        }
        return new InputState(raw.Mouse, _leftHeld, pressed, released, hovered);
    }
}
