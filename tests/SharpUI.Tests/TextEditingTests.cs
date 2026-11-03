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

    // ---- 审计修复波 2A-1(I-4):字符通道控制符过滤 ----
    // SDL_TEXTINPUT 在部分平台/版本(Windows WM_CHAR 路径、X11 路径)把 Ctrl+字母 的控制码
    // (C0 0x01..0x1A)作为文本投递 —— 无过滤时 Ctrl+A 全选同帧 \x01 先入正文,Ctrl+Z 注入 0x1A。

    [Fact] public void Chars_ControlCodesFiltered_LettersKept()
    {
        var s = TextEditing.Apply(new TextState("", 0), null, new[] { '\x01', 'a', '\x1a', 'b', '\x7f' });
        Assert.Equal(new TextState("ab", 2), s);   // C0(0x01/0x1A)与 DEL(0x7F)全滤,仅剩正文
    }

    [Fact] public void Chars_TabAndNewlinePreserved()
    {
        var s = TextEditing.Apply(new TextState("", 0), null, new[] { '\t', 'a', '\n', 'b', '\r' });
        Assert.Equal(new TextState("\ta\nb\r", 5), s);   // Tab/CR/LF 保留(未来多行需要)
    }

    [Fact] public void Chars_ControlCode_DoesNotDisturbSelection()
    {
        // 有选区时控制码不得替换选区(过滤发生在选区替换之前,控制码直接跳过)
        var s = TextEditing.Apply(new TextState("abcd", 1, 3), null, new[] { '\x02' });
        Assert.Equal(new TextState("abcd", 1, 3), s);
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

    // ---- 波 3B(B8 部分):撤销/重做(按 Apply 变更记录快照,容量 100)----

    [Fact]
    public void Apply_WithUndoStack_RecordsSnapshotPerTextChange()
    {
        var stack = new TextUndoStack();
        TextState s1 = TextEditing.Apply(new TextState("ab", 1), null, "X".ToCharArray(), stack);
        Assert.Equal(new TextState("aXb", 2), s1);
        Assert.Equal(1, stack.UndoCount);
        _ = TextEditing.Apply(s1, null, "Y".ToCharArray(), stack);
        Assert.Equal(2, stack.UndoCount);
        // 既有无栈重载(零形变):不触碰任何栈
        _ = TextEditing.Apply(s1, null, "Y".ToCharArray());
        Assert.Equal(2, stack.UndoCount);
    }

    [Fact]
    public void Undo_ChainedRestoresPriorStates_EmptyStackNoOp()
    {
        var stack = new TextUndoStack();
        TextState s = TextEditing.Apply(new TextState("", 0), null, "a".ToCharArray(), stack);
        s = TextEditing.Apply(s, null, "b".ToCharArray(), stack);
        s = TextEditing.Apply(s, null, "c".ToCharArray(), stack);
        Assert.Equal("abc", s.Text);
        s = TextEditing.Undo(s, stack);
        Assert.Equal(new TextState("ab", 2), s);
        s = TextEditing.Undo(s, stack);
        Assert.Equal(new TextState("a", 1), s);
        s = TextEditing.Undo(s, stack);
        Assert.Equal(new TextState("", 0), s);
        s = TextEditing.Undo(s, stack);                             // 空栈:原样返回不崩
        Assert.Equal(new TextState("", 0), s);
    }

    [Fact]
    public void Redo_ReappliesUndoneState_NewEditClearsRedo()
    {
        var stack = new TextUndoStack();
        TextState s = TextEditing.Apply(new TextState("", 0), null, "ab".ToCharArray(), stack);
        s = TextEditing.Undo(s, stack);
        s = TextEditing.Redo(s, stack);                             // 重做恢复
        Assert.Equal(new TextState("ab", 2), s);
        s = TextEditing.Undo(s, stack);
        s = TextEditing.Apply(s, null, "z".ToCharArray(), stack);   // 撤销后的新编辑:清空 redo
        Assert.Equal(1, stack.UndoCount);
        s = TextEditing.Redo(s, stack);                             // redo 空:原样返回
        Assert.Equal(new TextState("z", 1), s);
    }

    [Fact]
    public void UndoRedo_AlternatingChain_PreservesFullHistory()
    {
        var stack = new TextUndoStack();
        TextState s = TextEditing.Apply(new TextState("", 0), null, "ab".ToCharArray(), stack);
        s = TextEditing.Apply(s, null, "cd".ToCharArray(), stack);
        Assert.Equal("abcd", s.Text);
        s = TextEditing.Undo(s, stack);
        s = TextEditing.Undo(s, stack);
        Assert.Equal(new TextState("", 0), s);
        s = TextEditing.Redo(s, stack);
        Assert.Equal(new TextState("ab", 2), s);
        s = TextEditing.Redo(s, stack);
        Assert.Equal(new TextState("abcd", 4), s);
        s = TextEditing.Undo(s, stack);                             // 重做后仍可再撤销
        Assert.Equal(new TextState("ab", 2), s);
    }

    [Fact]
    public void UndoStack_Capacity100_OldestDropped()
    {
        var stack = new TextUndoStack();
        TextState s = new("", 0);
        for (int i = 0; i < TextUndoStack.Capacity + 5; i++)
            s = TextEditing.Apply(s, null, new[] { 'x' }, stack);
        Assert.Equal(TextUndoStack.Capacity, stack.UndoCount);      // 容量钳制
        while (stack.UndoCount > 0) s = TextEditing.Undo(s, stack);
        Assert.Equal("xxxxx", s.Text);                              // 最旧 5 步已丢:undo 到底停在第 5 步后
    }

    [Fact]
    public void Apply_WithUndoStack_CursorOnlyChange_NoSnapshot()
    {
        var stack = new TextUndoStack();
        _ = TextEditing.Apply(new TextState("ab", 1), new[] { Key(SharpUI.Key.Left) }, null, stack);
        Assert.Equal(0, stack.UndoCount);                           // 移动光标不是编辑
        _ = TextEditing.Apply(new TextState("ab", 1), new[] { Key(SharpUI.Key.A, ctrl: true) }, null, stack);
        Assert.Equal(0, stack.UndoCount);                           // 全选(仅光标/锚)不是编辑
    }

    // ---- 波 3B(B8 部分):Ctrl+Left/Right 词级导航(字母数字连续段为词)----

    [Fact]
    public void CtrlLeft_MovesToWordStart()
    {
        string t = "hello world";
        Assert.Equal(6, TextEditing.Apply(new TextState(t, 8), new[] { Key(SharpUI.Key.Left, ctrl: true) }, null).Cursor);
        Assert.Equal(0, TextEditing.Apply(new TextState(t, 6), new[] { Key(SharpUI.Key.Left, ctrl: true) }, null).Cursor);
        Assert.Equal(0, TextEditing.Apply(new TextState(t, 5), new[] { Key(SharpUI.Key.Left, ctrl: true) }, null).Cursor);
        Assert.Equal(0, TextEditing.Apply(new TextState(t, 0), new[] { Key(SharpUI.Key.Left, ctrl: true) }, null).Cursor);
    }

    [Fact]
    public void CtrlRight_MovesToWordEnd()
    {
        string t = "hello world";
        Assert.Equal(5, TextEditing.Apply(new TextState(t, 0), new[] { Key(SharpUI.Key.Right, ctrl: true) }, null).Cursor);
        Assert.Equal(11, TextEditing.Apply(new TextState(t, 5), new[] { Key(SharpUI.Key.Right, ctrl: true) }, null).Cursor);
        Assert.Equal(11, TextEditing.Apply(new TextState(t, 11), new[] { Key(SharpUI.Key.Right, ctrl: true) }, null).Cursor);
    }

    [Fact]
    public void CtrlLeft_PunctuationRunSkippedToWordBoundary()
    {
        // "ab.cd":标点段整跳 —— 尾部光标落 "cd" 首(5→3);"cd" 前光标跳过 '.' 落 "ab" 首(3→0)
        Assert.Equal(3, TextEditing.Apply(new TextState("ab.cd", 5), new[] { Key(SharpUI.Key.Left, ctrl: true) }, null).Cursor);
        Assert.Equal(0, TextEditing.Apply(new TextState("ab.cd", 3), new[] { Key(SharpUI.Key.Left, ctrl: true) }, null).Cursor);
    }

    [Fact]
    public void CtrlArrows_WithShift_ExtendSelectionByWord()
    {
        string t = "hello world";
        Assert.Equal(new TextState(t, 6, 8),
            TextEditing.Apply(new TextState(t, 8), new[] { Key(SharpUI.Key.Left, shift: true, ctrl: true) }, null));
        Assert.Equal(new TextState(t, 5, 0),
            TextEditing.Apply(new TextState(t, 0), new[] { Key(SharpUI.Key.Right, shift: true, ctrl: true) }, null));
    }

    [Fact]
    public void CtrlWordNav_SurrogateSeparatorSkippedWholePair()
    {
        // 😀 非字母数字:分隔段按码点跳过,光标不落高低代理之间
        string t = "a" + Emoji + "b";
        Assert.Equal(3, TextEditing.Apply(new TextState(t, 4), new[] { Key(SharpUI.Key.Left, ctrl: true) }, null).Cursor);
        Assert.Equal(0, TextEditing.Apply(new TextState(t, 3), new[] { Key(SharpUI.Key.Left, ctrl: true) }, null).Cursor);
        Assert.Equal(1, TextEditing.Apply(new TextState(t, 0), new[] { Key(SharpUI.Key.Right, ctrl: true) }, null).Cursor);
        Assert.Equal(4, TextEditing.Apply(new TextState(t, 1), new[] { Key(SharpUI.Key.Right, ctrl: true) }, null).Cursor);
    }
}
