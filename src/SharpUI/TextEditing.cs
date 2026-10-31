namespace SharpUI;

/// <summary>单行文本编辑状态(D22)。Anchor &lt; 0(默认 -1)= 无选区(视同 Cursor);
/// 非负 Anchor 为选区另一端,选区 = [min(Cursor,Anchor), max)。锁定于 TextEditingTests 10 测试。</summary>
public sealed record TextState(string Text = "", int Cursor = 0, int Anchor = -1);

/// <summary>单行文本编辑纯函数(D22):位置语义 = 字符索引;选区 = [min(Cursor,Anchor), max)。
/// 审计修复波 I-5:BackSpace/Delete/Left/Right 按码点步进而非 UTF-16 码元 ±1 —— 代理对
/// (emoji/BMP 外 CJK,高+低两码元)不再被光标劈开或删半。仅覆盖代理对子集(非完整 UAX#29
/// grapheme cluster:ZWJ 序列、组合变音仍可被分开 —— emoji 与扩展汉字是覆盖面最大的实用子集)。</summary>
public static class TextEditing
{
    /// <summary>光标后一个码点的码元长度(位置 i ∈ [0, len)):高+低代理对 = 2,否则 1(同 TextAtlas.UnitLen)。</summary>
    static int NextLen(string s, int i) =>
        char.IsHighSurrogate(s, i) && i + 1 < s.Length && char.IsLowSurrogate(s, i + 1) ? 2 : 1;

    /// <summary>光标前一个码点的码元长度(位置 i ∈ [0, len]):前两码元构成代理对 = 2,否则 1。</summary>
    static int PrevLen(string s, int i) =>
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
}
