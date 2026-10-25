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
}
