namespace SharpUI;

/// <summary>SDL InputSnapshot → RawInput 纯映射(可单测)。LeftHeld 来自 IsMouseDown 持久按键态(D13/D15)。
/// T3:入参换自有 SharpUI.InputSnapshot(T2 交付,消费成员名对齐旧窗口层接口,形变为零);MouseEvents/
/// KeyEvents 为值元组列表(T2 形制:每事件零堆分配),MouseEvent/KeyEvent 类引用随旧包退役。</summary>
public static class InputMapper
{
    public static RawInput Map(InputSnapshot snapshot, float scale = 1f) => new(
        new PointF(snapshot.MousePosition.X / scale, snapshot.MousePosition.Y / scale),
        LeftEvents(snapshot.MouseEvents),
        MapKeys(snapshot.KeyEvents),
        snapshot.IsMouseDown(MouseButton.Left),
        snapshot.WheelDelta,
        snapshot.KeyCharPresses.Count == 0 ? null : snapshot.KeyCharPresses.ToArray());

    public static bool[] LeftEvents(IReadOnlyList<(MouseButton Button, bool Down)> mouseEvents)
    {
        int n = 0;
        foreach ((MouseButton Button, bool Down) me in mouseEvents)
            if (me.Button == MouseButton.Left) n++;
        if (n == 0) return Array.Empty<bool>();
        var events = new bool[n];
        int i = 0;
        foreach ((MouseButton Button, bool Down) me in mouseEvents)
            if (me.Button == MouseButton.Left)
                events[i++] = me.Down;
        return events;
    }

    public static KeyEventData[] MapKeys(IReadOnlyList<(Key Key, bool Down, ModifierKeys Modifiers)> keyEvents)
    {
        if (keyEvents.Count == 0) return Array.Empty<KeyEventData>();
        var keys = new KeyEventData[keyEvents.Count];
        for (int i = 0; i < keyEvents.Count; i++)
        {
            (Key Key, bool Down, ModifierKeys Modifiers) ke = keyEvents[i];
            keys[i] = new KeyEventData(ke.Key, ke.Down,
                ke.Modifiers.HasFlag(ModifierKeys.Shift), ke.Modifiers.HasFlag(ModifierKeys.Control));
        }
        return keys;
    }
}
