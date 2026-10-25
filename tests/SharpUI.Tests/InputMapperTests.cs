using Veldrid;
using SharpUI;

namespace SharpUI.Tests;

public class InputMapperTests
{
    [Fact]
    public void LeftEvents_FiltersLeftInOrder()
    {
        bool[] events = InputMapper.LeftEvents(new MouseEvent[]
        {
            new(MouseButton.Right, true),
            new(MouseButton.Left, true),
            new(MouseButton.Left, false),
        });
        Assert.Equal(new[] { true, false }, events);
    }

    [Fact]
    public void LeftEvents_EmptyListNoAlloc()
    {
        Assert.Same(Array.Empty<bool>(), InputMapper.LeftEvents(Array.Empty<MouseEvent>()));
    }

    [Fact]
    public void MapKeys_TranslatesWithShift()
    {
        KeyEventData[] keys = InputMapper.MapKeys(new[]
        {
            new KeyEvent(Key.Tab, true, ModifierKeys.Shift),
            new KeyEvent(Key.Enter, true, ModifierKeys.None),
        });
        Assert.Equal(Veldrid.Key.Tab, keys[0].Key);
        Assert.True(keys[0].Shift);
        Assert.False(keys[1].Shift);
    }

    [Fact]
    public void MapKeys_EmptyListNoAlloc()
    {
        Assert.Same(Array.Empty<KeyEventData>(), InputMapper.MapKeys(Array.Empty<KeyEvent>()));
    }

    [Fact]
    public void MapKeys_ExtractsCtrl()
    {
        KeyEventData[] keys = InputMapper.MapKeys(new[]
        {
            new KeyEvent(Key.A, true, ModifierKeys.Control),
            new KeyEvent(Key.B, true, ModifierKeys.Control | ModifierKeys.Shift),
        });
        Assert.True(keys[0].Ctrl);
        Assert.True(keys[1].Shift && keys[1].Ctrl);
    }

    [Fact]
    public void Map_CharsFromKeyCharPresses_EmptyIsNull()
    {
        RawInput raw = InputMapper.Map(new FakeSnapshot(new[] { 'h', 'i' }));
        Assert.Equal(new[] { 'h', 'i' }, raw.Chars);
        Assert.Null(InputMapper.Map(new FakeSnapshot(Array.Empty<char>())).Chars);
    }

    sealed class FakeSnapshot(IReadOnlyList<char> chars) : InputSnapshot
    {
        public System.Numerics.Vector2 MousePosition => default;
        public IReadOnlyList<MouseEvent> MouseEvents => Array.Empty<MouseEvent>();
        public IReadOnlyList<KeyEvent> KeyEvents => Array.Empty<KeyEvent>();
        public IReadOnlyList<char> KeyCharPresses => chars;
        public bool IsMouseDown(MouseButton button) => false;
        public bool IsAnyMouseDown { get; }
        public float WheelDelta => 0f;
    }
}
