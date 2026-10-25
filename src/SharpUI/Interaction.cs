namespace SharpUI;

/// <summary>App 从 SDL InputSnapshot 映射;LeftEvents 按时间序记录左键按下(true)/抬起(false)。</summary>
public readonly record struct RawInput(PointF Mouse, bool[] LeftEvents);

public readonly record struct InputState(PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased, Element? Hovered)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

public static class Interaction
{
    static bool _leftHeld;
    static Element? _pressedOn;   // 按下瞬间的命中目标;点击 = 按下与松开命中同一目标

    public static InputState Update(Element root, in RawInput raw)
    {
        bool pressed = false, released = false;
        Element? hovered = root.HitTest(raw.Mouse);
        foreach (bool down in raw.LeftEvents)
        {
            if (down)
            {
                _leftHeld = true;
                pressed = true;
                _pressedOn = hovered;
            }
            else
            {
                _leftHeld = false;
                released = true;
                if (_pressedOn is not null && ReferenceEquals(_pressedOn, hovered)
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
