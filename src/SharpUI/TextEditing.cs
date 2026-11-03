namespace SharpUI;

/// <summary>单行文本编辑状态(D22)。Anchor &lt; 0(默认 -1)= 无选区(视同 Cursor);
/// 非负 Anchor 为选区另一端,选区 = [min(Cursor,Anchor), max)。锁定于 TextEditingTests 10 测试。</summary>
public sealed record TextState(string Text = "", int Cursor = 0, int Anchor = -1);

/// <summary>撤销/重做栈(波 3B/B8 部分):按整次变更记录快照(TextState 全态:text+cursor+anchor),
/// undo 容量 100(超容量丢最旧);redo 随新编辑清空。经 StateStore("tf:undo:"+id) 持引用跨帧存活
/// (D25 宽限:字段 300 帧无交互随键清扫)。纯数据类,语义在 TextEditing.Undo/Redo —— 调用方
/// (TextFieldElement.Feed)负责在剪切/粘贴/Apply 变更前 Record。</summary>
public sealed class TextUndoStack
{
    /// <summary>undo 深度上限(任务口径 100;超容量丢最旧,undo 链保持最近 100 步)。</summary>
    internal const int Capacity = 100;

    readonly List<TextState> _undo = new();
    readonly List<TextState> _redo = new();

    /// <summary>可撤销步数(测试观察点)。</summary>
    public int UndoCount => _undo.Count;

    /// <summary>记录变更前状态(入 undo,超容量丢最旧)并清空 redo(新编辑使重做链失效)。</summary>
    internal void Record(TextState before)
    {
        _undo.Add(before);
        if (_undo.Count > Capacity) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>弹出 undo 栈顶恢复,当前态压入 redo(撤销后仍可重做/再撤销);空栈原样返回。</summary>
    internal TextState PopUndo(TextState current)
    {
        if (_undo.Count == 0) return current;
        TextState restored = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        return restored;
    }

    /// <summary>弹出 redo 栈顶恢复,当前态压回 undo(重做后仍可再撤销);空栈原样返回。
    /// redo 深度天然 ≤ undo 历史 ≤ Capacity,无独立上限。</summary>
    internal TextState PopRedo(TextState current)
    {
        if (_redo.Count == 0) return current;
        TextState restored = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        return restored;
    }
}

/// <summary>单行文本编辑纯函数(D22):位置语义 = 字符索引;选区 = [min(Cursor,Anchor), max)。
/// 审计修复波 I-5:BackSpace/Delete/Left/Right 按码点步进而非 UTF-16 码元 ±1 —— 代理对
/// (emoji/BMP 外 CJK,高+低两码元)不再被光标劈开或删半。仅覆盖代理对子集(非完整 UAX#29
/// grapheme cluster:ZWJ 序列、组合变音仍可被分开 —— emoji 与扩展汉字是覆盖面最大的实用子集)。
/// 波 3B(B8 部分):Ctrl+Left/Right 词级导航(字母数字连续段为词);可选撤销栈的 Apply 重载
/// (undo 非 null 且本次净改正文时记录一步快照)。</summary>
public static class TextEditing
{
    /// <summary>光标后一个码点的码元长度(位置 i ∈ [0, len)):高+低代理对 = 2,否则 1(同 TextAtlas.UnitLen)。
    /// internal:TextFieldElement.HitToOffset 消费(波 3B/B8 hit-to-offset)。</summary>
    internal static int NextLen(string s, int i) =>
        char.IsHighSurrogate(s, i) && i + 1 < s.Length && char.IsLowSurrogate(s, i + 1) ? 2 : 1;

    /// <summary>光标前一个码点的码元长度(位置 i ∈ [0, len]):前两码元构成代理对 = 2,否则 1。</summary>
    internal static int PrevLen(string s, int i) =>
        i >= 2 && char.IsHighSurrogate(s, i - 2) && char.IsLowSurrogate(s, i - 1) ? 2 : 1;

    public static (int Start, int Len) Selection(TextState s)
    {
        int anchor = s.Anchor < 0 ? s.Cursor : s.Anchor;
        int a = Math.Min(s.Cursor, anchor), b = Math.Max(s.Cursor, anchor);
        return (a, b - a);
    }

    /// <summary>粘贴(M3c):替换选区(无选区即在光标处插入),光标落粘贴串末,锚塌陷为无选区。</summary>
    public static TextState Paste(TextState s, string text)
    {
        text ??= "";
        int cursor = Math.Clamp(s.Cursor, 0, s.Text.Length);
        int anchor = s.Anchor < 0 ? cursor : Math.Clamp(s.Anchor, 0, s.Text.Length);
        (int start, int len) = Selection(new TextState(s.Text, cursor, anchor));
        string result = s.Text.Remove(start, len).Insert(start, text);
        return new TextState(result, start + text.Length);
    }

    /// <summary>复制/剪切(M3c):有选区 → After=删除选区+塌陷至选区头,Copied=选区文本;
    /// 无选区 → (原态, null)(调用方据此免写剪贴板)。</summary>
    public static (TextState After, string? Copied) CutCopy(TextState s)
    {
        int cursor = Math.Clamp(s.Cursor, 0, s.Text.Length);
        int anchor = s.Anchor < 0 ? cursor : Math.Clamp(s.Anchor, 0, s.Text.Length);
        (int start, int len) = Selection(new TextState(s.Text, cursor, anchor));
        if (len == 0) return (s, null);
        return (new TextState(s.Text.Remove(start, len), start), s.Text.Substring(start, len));
    }

    public static TextState Apply(TextState s, KeyEventData[]? keys, char[]? chars) =>
        Apply(s, keys, chars, undo: null);

    /// <summary>带撤销栈的 Apply(波 3B/B8):undo 非 null 且本次调用净改了正文(text 变化)时记录
    /// 一步快照(整次调用 = 一步撤销,变更前全态含光标/锚);仅光标/锚移动(方向键/全选)不记录。
    /// 既有无栈重载零形变(委托本重载传 null)。</summary>
    public static TextState Apply(TextState s, KeyEventData[]? keys, char[]? chars, TextUndoStack? undo)
    {
        // 快照取钳制后的有效态(调用方可能传越界 cursor/anchor —— 恢复时不再二次钳制)
        TextState before = new(s.Text,
            Math.Clamp(s.Cursor, 0, s.Text.Length),
            s.Anchor < 0 ? -1 : Math.Clamp(s.Anchor, 0, s.Text.Length));
        TextState after = ApplyCore(before, keys, chars);
        if (undo is not null && after.Text != before.Text) undo.Record(before);
        return after;
    }

    /// <summary>撤销/重做入口(波 3B/B8):弹出栈顶恢复;空栈原样返回。Undo 把当前态压入 redo,
    /// Redo 对称压回 undo。剪贴板/字符编辑的快照由 Feed 在变更前 Record(见 TextFieldElement.Feed)。</summary>
    public static TextState Undo(TextState s, TextUndoStack stack) => stack.PopUndo(s);

    public static TextState Redo(TextState s, TextUndoStack stack) => stack.PopRedo(s);

    static TextState ApplyCore(TextState s, KeyEventData[]? keys, char[]? chars)
    {
        int cursor = Math.Clamp(s.Cursor, 0, s.Text.Length);
        int anchor = s.Anchor < 0 ? cursor : Math.Clamp(s.Anchor, 0, s.Text.Length);
        string text = s.Text;
        foreach (char c in chars ?? Array.Empty<char>())
        {
            // 审计修复波 2A-1(I-4):控制符过滤。SDL_TEXTINPUT 在部分平台/版本把 Ctrl+字母 的控制码
            // (C0 0x01..0x1A,Windows WM_CHAR 路径与 X11 路径的历史行为,SDL3 才收紧)作为文本投递 ——
            // 不过滤则 Ctrl+A 全选同帧 \x01 先入正文、Ctrl+Z 注入 0x1A,剪贴板往返后外溢。
            // 过滤范围 = char.IsControl(C0 + DEL + C1),保留 Tab/CR/LF(未来多行编辑需要)。
            if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r') continue;
            (int start, int len) = Selection(new TextState(text, cursor, anchor));
            text = text.Remove(start, len).Insert(start, c.ToString());
            cursor = anchor = start + 1;
        }
        if (keys is not null)
            foreach (KeyEventData k in keys)
            {
                if (!k.Down) continue;
                (int start, int len) = Selection(new TextState(text, cursor, anchor));
                if (k.Ctrl && k.Key == Key.A)
                {
                    cursor = text.Length; anchor = 0;
                }
                else if (k.Ctrl && k.Key == Key.Left)
                { int t = WordLeft(text, cursor); cursor = t; if (!k.Shift) anchor = t; }   // 波 3B/B8:词级导航
                else if (k.Ctrl && k.Key == Key.Right)
                { int t = WordRight(text, cursor); cursor = t; if (!k.Shift) anchor = t; }
                else if (k.Key == Key.BackSpace)
                {
                    if (len > 0) { text = text.Remove(start, len); cursor = anchor = start; }
                    else if (cursor > 0)
                    {
                        int n = PrevLen(text, cursor);   // I-5:退格删整个码点(代理对不删半,免孤立代理入文)
                        text = text.Remove(cursor - n, n);
                        cursor = anchor = cursor - n;
                    }
                }
                else if (k.Key == Key.Delete)
                {
                    if (len > 0) { text = text.Remove(start, len); cursor = anchor = start; }
                    else if (cursor < text.Length)
                        text = text.Remove(cursor, NextLen(text, cursor));   // I-5:前删同样按码点取整
                }
                else if (k.Key == Key.Left)
                { int t = cursor > 0 ? cursor - PrevLen(text, cursor) : 0; cursor = t; if (!k.Shift) anchor = t; }
                else if (k.Key == Key.Right)
                { int t = cursor < text.Length ? cursor + NextLen(text, cursor) : text.Length; cursor = t; if (!k.Shift) anchor = t; }
                else if (k.Key == Key.Home) { cursor = 0; if (!k.Shift) anchor = 0; }
                else if (k.Key == Key.End) { cursor = text.Length; if (!k.Shift) anchor = text.Length; }
            }
        cursor = Math.Clamp(cursor, 0, text.Length);
        anchor = Math.Clamp(anchor, 0, text.Length);
        return new TextState(text, cursor, anchor == cursor ? -1 : anchor);
    }

    /// <summary>Ctrl+Left 词界(波 3B/B8):向左先跳过非字母数字段(按码点步进,代理对整对不劈),
    /// 再跳过连续字母数字词至词首。"字母数字连续段为词";边界钳制 [0, len]。</summary>
    static int WordLeft(string text, int cursor)
    {
        int i = Math.Clamp(cursor, 0, text.Length);
        while (i > 0)
        {
            if (char.IsLetterOrDigit(text[i - 1])) break;   // 词字符:分隔段结束
            i -= PrevLen(text, i);                          // 空白/标点/代理对整码点跳过
        }
        while (i > 0 && char.IsLetterOrDigit(text[i - 1]))
            i -= PrevLen(text, i);
        return i;
    }

    /// <summary>Ctrl+Right 词界(波 3B/B8):向右对称 —— 跳过非字母数字段后跳至连续字母数字词尾。</summary>
    static int WordRight(string text, int cursor)
    {
        int i = Math.Clamp(cursor, 0, text.Length);
        while (i < text.Length)
        {
            if (char.IsLetterOrDigit(text[i])) break;
            i += NextLen(text, i);
        }
        while (i < text.Length && char.IsLetterOrDigit(text[i]))
            i += NextLen(text, i);
        return i;
    }
}
