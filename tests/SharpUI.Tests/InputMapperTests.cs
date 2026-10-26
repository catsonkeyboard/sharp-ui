using SharpUI;

namespace SharpUI.Tests;

/// <summary>T3:入参换自有 SharpUI.InputSnapshot(元组列表形制);旧 FakeSnapshot(实现基线包接口)
/// 随包退役,断言零漂移。</summary>
public class InputMapperTests
{
    [Fact]
    public void LeftEvents_FiltersLeftInOrder()
    {
        bool[] events = InputMapper.LeftEvents(new (MouseButton, bool)[]
        {
            (MouseButton.Right, true),
            (MouseButton.Left, true),
            (MouseButton.Left, false),
        });
        Assert.Equal(new[] { true, false }, events);
    }

    [Fact]
    public void LeftEvents_EmptyListNoAlloc()
    {
        Assert.Same(Array.Empty<bool>(), InputMapper.LeftEvents(Array.Empty<(MouseButton, bool)>()));
    }

    [Fact]
    public void MapKeys_TranslatesWithShift()
    {
        KeyEventData[] keys = InputMapper.MapKeys(new (Key, bool, ModifierKeys)[]
        {
            (Key.Tab, true, ModifierKeys.Shift),
            (Key.Enter, true, ModifierKeys.None),
        });
        Assert.Equal(Key.Tab, keys[0].Key);
        Assert.True(keys[0].Shift);
        Assert.False(keys[1].Shift);
    }

    [Fact]
    public void MapKeys_EmptyListNoAlloc()
    {
        Assert.Same(Array.Empty<KeyEventData>(), InputMapper.MapKeys(Array.Empty<(Key, bool, ModifierKeys)>()));
    }

    [Fact]
    public void MapKeys_ExtractsCtrl()
    {
        KeyEventData[] keys = InputMapper.MapKeys(new (Key, bool, ModifierKeys)[]
        {
            (Key.A, true, ModifierKeys.Control),
            (Key.B, true, ModifierKeys.Control | ModifierKeys.Shift),
        });
        Assert.True(keys[0].Ctrl);
        Assert.True(keys[1].Shift && keys[1].Ctrl);
    }

    static InputSnapshot Snapshot(char[]? chars = null) => new(   // internal 构造器经 InternalsVisibleTo
        new PointF(0f, 0f), 0ul, 0f,
        Array.Empty<(MouseButton, bool)>(), Array.Empty<(Key, bool, ModifierKeys)>(),
        chars ?? Array.Empty<char>());

    [Fact]
    public void Map_CharsFromKeyCharPresses_EmptyIsNull()
    {
        RawInput raw = InputMapper.Map(Snapshot(new[] { 'h', 'i' }));
        Assert.Equal(new[] { 'h', 'i' }, raw.Chars);
        Assert.Null(InputMapper.Map(Snapshot()).Chars);
    }
}
