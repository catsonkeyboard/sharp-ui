namespace SharpUI;

/// <summary>SDL InputSnapshot → RawInput 纯映射(可单测)。LeftHeld 来自 IsMouseDown 持久按键态(D13/D15)。
/// T3:入参换自有 SharpUI.InputSnapshot(T2 交付,消费成员名对齐旧窗口层接口,形变为零);MouseEvents/
/// KeyEvents 为值元组列表(T2 形制:每事件零堆分配),MouseEvent/KeyEvent 类引用随旧包退役。</summary>
public static class InputMapper
{
    public static RawInput Map(InputSnapshot snapshot, float scale = 1f) => new(
        new PointF(snapshot.MousePosition.X / scale, snapshot.MousePosition.Y / scale),
        LeftEvents(snapshot.ButtonEvents),   // 波 3A:左键过滤改从全按钮通道取(序与语义不变)
        MapKeys(snapshot.KeyEventsFull),     // 波 3A:全键记录通道(Alt/Gui/IsRepeat 透出)
        snapshot.IsMouseDown(MouseButton.Left),
        snapshot.WheelDelta,
        snapshot.KeyCharPresses.Count == 0 ? null : snapshot.KeyCharPresses.ToArray(),
        snapshot.ButtonEvents.Count == 0 ? null : ButtonEventsCopy(snapshot.ButtonEvents));

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

    /// <summary>全按钮通道的左键过滤(波 3A):语义与 2 元组重载一致,源为含 clicks 的记录通道。</summary>
    public static bool[] LeftEvents(IReadOnlyList<MouseButtonEventData> mouseEvents)
    {
        int n = 0;
        foreach (MouseButtonEventData me in mouseEvents)
            if (me.Button == MouseButton.Left) n++;
        if (n == 0) return Array.Empty<bool>();
        var events = new bool[n];
        int i = 0;
        foreach (MouseButtonEventData me in mouseEvents)
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
            // 波 3A(B11)透传修复:旧实现只映 Shift/Ctrl,Alt/Gui 被丢弃(无 Alt 快捷键/macOS Cmd 无从谈起)。
            keys[i] = new KeyEventData(ke.Key, ke.Down,
                ke.Modifiers.HasFlag(ModifierKeys.Shift), ke.Modifiers.HasFlag(ModifierKeys.Control),
                ke.Modifiers.HasFlag(ModifierKeys.Alt), ke.Modifiers.HasFlag(ModifierKeys.Gui));
        }
        return keys;
    }

    /// <summary>全键记录通道直拷(波 3A/B11/B12):泵侧已按 (Key, Down, Shift, Ctrl, Alt, Gui, IsRepeat)
    /// 一次成形,此处仅 IReadOnlyList → 数组(快照记录为 struct,无堆分配;空则 Array.Empty 零分配)。</summary>
    public static KeyEventData[] MapKeys(IReadOnlyList<KeyEventData> keyEvents)
    {
        if (keyEvents.Count == 0) return Array.Empty<KeyEventData>();
        var keys = new KeyEventData[keyEvents.Count];
        for (int i = 0; i < keyEvents.Count; i++)
            keys[i] = keyEvents[i];
        return keys;
    }

    static MouseButtonEventData[] ButtonEventsCopy(IReadOnlyList<MouseButtonEventData> events)
    {
        var copy = new MouseButtonEventData[events.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = events[i];
        return copy;
    }
}
