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

    // ---- 审计修复波 I-5:光标代理对感知(😀 = 高+低代理对,2 个 UTF-16 码元)----
    // "😀abc":[0,2)=对,2='a',3='b',4='c',len=5。位置语义仍 = char 索引;仅移动/删改步长按码点取整。

    const string Emoji = "\U0001F600";   // 😀 U+1F600

    [Fact] public void Left_AcrossSurrogatePair_StepsTwoUnits()
    {
        string t = Emoji + "abc";
        Assert.Equal(new TextState(t, 0),                           // 对后一步 Left:跨对步进 2(旧 ±1 会停高低代理之间)
            TextEditing.Apply(new TextState(t, 2), new[] { Key(SharpUI.Key.Left) }, null));
        // 尾部 Left×4:位置序列 [4,3,2,0] —— 前三步各 1,过对一步 2,四步回开头(旧实现 [4,3,2,1] 劈在对中间)
        TextState s = new(t, 5);
        Assert.Equal(4, TextEditing.Apply(s, new[] { Key(SharpUI.Key.Left) }, null).Cursor);
        Assert.Equal(3, TextEditing.Apply(new TextState(t, 4), new[] { Key(SharpUI.Key.Left) }, null).Cursor);
        Assert.Equal(2, TextEditing.Apply(new TextState(t, 3), new[] { Key(SharpUI.Key.Left) }, null).Cursor);
        Assert.Equal(0, TextEditing.Apply(new TextState(t, 2), new[] { Key(SharpUI.Key.Left) }, null).Cursor);
        Assert.Equal(0, TextEditing.Apply(new TextState(t, 0), new[] { Key(SharpUI.Key.Left) }, null).Cursor);   // 0 钳制
    }

    [Fact] public void Right_AcrossSurrogatePair_StepsTwoUnits()
    {
        string t = Emoji + "abc";
        Assert.Equal(new TextState(t, 2),                           // 头部 Right:首步跨对步进 2(不停高低代理之间)
            TextEditing.Apply(new TextState(t, 0), new[] { Key(SharpUI.Key.Right) }, null));
        Assert.Equal(new TextState(t, 3),                           // 对后续步各 1
            TextEditing.Apply(new TextState(t, 2), new[] { Key(SharpUI.Key.Right) }, null));
        Assert.Equal(new TextState(t, 5),                           // 尾部钳制
            TextEditing.Apply(new TextState(t, 5), new[] { Key(SharpUI.Key.Right) }, null));
    }

    [Fact] public void BackSpace_AtTailDeletesWholeSurrogatePair()
    {
        Assert.Equal(new TextState("abc", 0),                       // 光标紧随 😀:一次退格删整个码点(旧实现只删低代理 → 孤立高代理)
            TextEditing.Apply(new TextState(Emoji + "abc", 2), new[] { Key(SharpUI.Key.BackSpace) }, null));
        Assert.Equal(new TextState(Emoji + "ab", 4),                // 普通字符处退格仍删 1 码元
            TextEditing.Apply(new TextState(Emoji + "abc", 5), new[] { Key(SharpUI.Key.BackSpace) }, null));
        Assert.Equal(new TextState("ab", 1),                        // 中位对:a[😀]b 光标在对后:删对不伤 'a'/'b'
            TextEditing.Apply(new TextState("a" + Emoji + "b", 3), new[] { Key(SharpUI.Key.BackSpace) }, null));
    }

    [Fact] public void Delete_AtHeadDeletesWholeSurrogatePair()
    {
        Assert.Equal(new TextState("abc", 0),                       // 光标在 😀 前:Delete 删整个码点(旧只删高代理)
            TextEditing.Apply(new TextState(Emoji + "abc", 0), new[] { Key(SharpUI.Key.Delete) }, null));
        Assert.Equal(new TextState("ab", 1),                        // 中位对:a[😀]b 光标在对前:删对不伤 'a'/'b'
            TextEditing.Apply(new TextState("a" + Emoji + "b", 1), new[] { Key(SharpUI.Key.Delete) }, null));
    }

    [Fact] public void ShiftArrows_OverPair_ExtendSelectionByCodepoint()
    {
        string t = Emoji + "abc";
        Assert.Equal(new TextState(t, 0, 2),                        // 对后 Shift+Left:整对入选区(旧只选低代理)
            TextEditing.Apply(new TextState(t, 2), new[] { Key(SharpUI.Key.Left, shift: true) }, null));
        Assert.Equal(new TextState("a" + Emoji + "b", 3, 1),        // 对前 Shift+Right:整对入选区
            TextEditing.Apply(new TextState("a" + Emoji + "b", 1), new[] { Key(SharpUI.Key.Right, shift: true) }, null));
    }
}
