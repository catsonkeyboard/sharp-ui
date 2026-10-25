using Veldrid;
using Veldrid.Sdl2;

namespace SharpUI;

/// <summary>SDL InputSnapshot → RawInput 纯映射(可单测)。LeftHeld 来自 IsMouseDown 持久按键态(D13/D15)。</summary>
public static class InputMapper
{
    public static RawInput Map(InputSnapshot snapshot, float scale = 1f) => new(
        new PointF(snapshot.MousePosition.X / scale, snapshot.MousePosition.Y / scale),
        LeftEvents(snapshot.MouseEvents),
        MapKeys(snapshot.KeyEvents),
        snapshot.IsMouseDown(MouseButton.Left),
        snapshot.WheelDelta,
        snapshot.KeyCharPresses.Count == 0 ? null : snapshot.KeyCharPresses.ToArray());

    public static bool[] LeftEvents(IReadOnlyList<MouseEvent> mouseEvents)
    {
        int n = 0;
        foreach (MouseEvent me in mouseEvents)
            if (me.MouseButton == MouseButton.Left) n++;
        if (n == 0) return Array.Empty<bool>();
        var events = new bool[n];
        int i = 0;
        foreach (MouseEvent me in mouseEvents)
            if (me.MouseButton == MouseButton.Left)
                events[i++] = me.Down;
        return events;
    }

    public static KeyEventData[] MapKeys(IReadOnlyList<KeyEvent> keyEvents)
    {
        if (keyEvents.Count == 0) return Array.Empty<KeyEventData>();
        var keys = new KeyEventData[keyEvents.Count];
        for (int i = 0; i < keyEvents.Count; i++)
        {
            KeyEvent ke = keyEvents[i];
            keys[i] = new KeyEventData(ke.Key, ke.Down,
                ke.Modifiers.HasFlag(ModifierKeys.Shift), ke.Modifiers.HasFlag(ModifierKeys.Control));
        }
        return keys;
    }
}
