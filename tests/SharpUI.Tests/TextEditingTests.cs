using SharpUI;

namespace SharpUI.Tests;

public class TextEditingTests
{
    static KeyEventData Key(Key k, bool shift = false, bool ctrl = false) => new(k, true, shift, ctrl);

    [Fact] public void Chars_AppendAtCursor()
    {
        var s = TextEditing.Apply(new TextState("ab", 1), null, "XY".ToCharArray());
        Assert.Equal(new TextState("aXYb", 3), s);
    }

    [Fact] public void Chars_ReplaceSelection()
    {
        var s = TextEditing.Apply(new TextState("abcd", 1, 3), null, "Z".ToCharArray());
        Assert.Equal(new TextState("aZd", 2), s);
    }

    [Fact] public void Backspace_DeleteBeforeCursor()
    {
        var s = TextEditing.Apply(new TextState("ab", 1), new[] { Key(SharpUI.Key.BackSpace) }, null);
        Assert.Equal(new TextState("b", 0), s);
    }

    [Fact] public void Backspace_DeleteSelection()
    {
        var s = TextEditing.Apply(new TextState("abcd", 3, 1), new[] { Key(SharpUI.Key.BackSpace) }, null);
        Assert.Equal(new TextState("ad", 1), s);
    }

    [Fact] public void Delete_DeleteAfterCursor()
    {
        var s = TextEditing.Apply(new TextState("ab", 1), new[] { Key(SharpUI.Key.Delete) }, null);
        Assert.Equal(new TextState("a", 1), s);
    }

    [Fact] public void Arrows_MoveAndSelect()
    {
        var s = new TextState("abc", 1);
        Assert.Equal(new TextState("abc", 2), TextEditing.Apply(s, new[] { Key(SharpUI.Key.Right) }, null));
        Assert.Equal(new TextState("abc", 0), TextEditing.Apply(s, new[] { Key(SharpUI.Key.Left) }, null));
        Assert.Equal(new TextState("abc", 2, 1), TextEditing.Apply(s, new[] { Key(SharpUI.Key.Right, shift: true) }, null));
        Assert.Equal(new TextState("abc", 3), TextEditing.Apply(s, new[] { Key(SharpUI.Key.End) }, null));
        Assert.Equal(new TextState("abc", 3, 1), TextEditing.Apply(s, new[] { Key(SharpUI.Key.End, shift: true) }, null));
    }

    [Fact] public void CtrlA_SelectAllThenReplace()
    {
        var s = TextEditing.Apply(new TextState("abc", 2), new[] { Key(SharpUI.Key.A, ctrl: true) }, null);
        Assert.Equal(new TextState("abc", 3, 0), s);
        Assert.Equal(new TextState("XY", 2), TextEditing.Apply(s, null, "XY".ToCharArray()));
    }

    [Fact] public void Selection_IsOrdered()
    {
        Assert.Equal((1, 2), TextEditing.Selection(new TextState("abcd", 3, 1)));
        Assert.Equal((1, 2), TextEditing.Selection(new TextState("abcd", 1, 3)));
        Assert.Equal((2, 0), TextEditing.Selection(new TextState("abcd", 2)));
    }

    [Fact] public void UnknownKeys_Ignored()
    {
        var s = new TextState("ab", 1);
        Assert.Equal(s, TextEditing.Apply(s, new[] { Key(SharpUI.Key.F5), new KeyEventData(SharpUI.Key.Enter, false, false) }, null));
    }

    [Fact] public void Chars_EmptyIsNoop_CursorClamped()
    {
        var s = new TextState("ab", 9);
        Assert.Equal(s with { Cursor = 2 }, TextEditing.Apply(s, null, null));
    }

    [Fact] public void Paste_EmptyString_NoSelection_NoOp()   // Feed 层已挡空剪贴板,纯函数边界:原样返回
    {
        var s = new TextState("ab", 1);
        Assert.Equal(s, TextEditing.Paste(s, ""));
    }

    [Fact] public void Paste_NoSelection_InsertsAtCursor_AnchorCollapsed()
    {
        Assert.Equal(new TextState("aXYb", 3), TextEditing.Paste(new TextState("ab", 1), "XY"));
    }

    [Fact] public void Paste_ReplacesSelection_CursorAtPastedEnd()
    {
        Assert.Equal(new TextState("aZd", 2), TextEditing.Paste(new TextState("abcd", 1, 3), "Z"));
    }

    [Fact] public void CutCopy_SelectionDeletesAndReturns_NoSelectionOriginalAndNull()
    {
        var (after, copied) = TextEditing.CutCopy(new TextState("abcd", 3, 1));   // 选区 [1,3)
        Assert.Equal(new TextState("ad", 1), after);
        Assert.Equal("bc", copied);
        var plain = new TextState("ab", 1);
        (TextState same, string? none) = TextEditing.CutCopy(plain);
        Assert.Equal(plain, same);
        Assert.Null(none);
    }
}
