namespace SharpUI;

/// <summary>单行文本编辑状态(D22)。Anchor &lt; 0(默认 -1)= 无选区(视同 Cursor);
/// 非负 Anchor 为选区另一端,选区 = [min(Cursor,Anchor), max)。锁定于 TextEditingTests 10 测试。</summary>
public sealed record TextState(string Text = "", int Cursor = 0, int Anchor = -1);

/// <summary>单行文本编辑纯函数(D22):位置语义 = 字符索引;选区 = [min(Cursor,Anchor), max)。</summary>
public static class TextEditing
{
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

    public static TextState Apply(TextState s, KeyEventData[]? keys, char[]? chars)
    {
        int cursor = Math.Clamp(s.Cursor, 0, s.Text.Length);
        int anchor = s.Anchor < 0 ? cursor : Math.Clamp(s.Anchor, 0, s.Text.Length);
        string text = s.Text;
        foreach (char c in chars ?? Array.Empty<char>())
        {
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
                else if (k.Key == Key.BackSpace)
                {
                    if (len > 0) { text = text.Remove(start, len); cursor = anchor = start; }
                    else if (cursor > 0) { text = text.Remove(cursor - 1, 1); cursor = anchor = cursor - 1; }
                }
                else if (k.Key == Key.Delete)
                {
                    if (len > 0) { text = text.Remove(start, len); cursor = anchor = start; }
                    else if (cursor < text.Length) text = text.Remove(cursor, 1);
                }
                else if (k.Key == Key.Left) { int t = Math.Max(0, cursor - 1); cursor = t; if (!k.Shift) anchor = t; }
                else if (k.Key == Key.Right) { int t = Math.Min(text.Length, cursor + 1); cursor = t; if (!k.Shift) anchor = t; }
                else if (k.Key == Key.Home) { cursor = 0; if (!k.Shift) anchor = 0; }
                else if (k.Key == Key.End) { cursor = text.Length; if (!k.Shift) anchor = text.Length; }
            }
        cursor = Math.Clamp(cursor, 0, text.Length);
        anchor = Math.Clamp(anchor, 0, text.Length);
        return new TextState(text, cursor, anchor == cursor ? -1 : anchor);
    }
}
