using System.Text;

namespace SharpUI;

/// <summary>每帧为元素树计算稳定 id:Key 优先,类型名+兄弟索引回退(规格书 D10)。
/// 同父同键兄弟自动消歧(审计修复波 2C I-3):第 n 次出现追加 "#n"、首现无后缀 —— 两个同键
/// TextField 不再共用同一 StateStore 条目互相串影;LastDuplicateKeys 仍记基础 id 供诊断
/// (React 纪律的用户错误提示,消歧只是兜底不替代改键)。不同分支同键合法。
/// 跨窗作用域(审计修复波 2C I-2):CurrentScope 非空时全树 id 冠 "scope:" 前缀 —— App.Run
/// 每窗以 SDL WindowId 为 scope,两窗同构树的 "scroll:"+id 等状态键按窗隔离;null 保持
/// 单窗/测试直调的无前缀基线语义。</summary>
public static class IdResolver
{
    [ThreadStatic]
    static StringBuilder? _sb;

    static readonly List<string> _duplicates = new();

    /// <summary>跨窗 id 作用域前缀(审计修复波 2C I-2):App.Run 每窗 Assign 前置为本窗 SDL
    /// WindowId 数字串、Assign 后即清 —— 两窗同构树 id 互不相同,StateStore 全局单份下不再
    /// 单向串扰(窗 A 滚动带动窗 B 的 I-2 实形)。测试直调 Assign 保持 null = 无前缀。</summary>
    internal static string? CurrentScope { get; set; }

    public static IReadOnlyList<string> LastDuplicateKeys => _duplicates;

    public static void Assign(Element root)
    {
        _sb ??= new StringBuilder(256);
        _duplicates.Clear();
        Visit(root, CurrentScope is string scope ? scope + ":" : "", _sb);
    }

    static void Visit(Element e, string parentId, StringBuilder sb)
    {
        e.ResolvedId = parentId;
        AssignChildren(e);
    }

    /// <summary>保自 id 只排子树(D20:VirtualList 按窗重建行后对其调用);Assign 的子循环体提取。
    /// 消歧计数为本调用局部:树每帧重建即随 Assign 重置,第二帧仍是 /dup 与 /dup#2(不累计);
    /// 无键回退(类型名+索引)索引天然唯一,不参与消歧。</summary>
    internal static void AssignChildren(Element e)
    {
        if (e.Children is not { Count: > 0 }) return;
        string parentId = e.ResolvedId!;
        _sb ??= new StringBuilder(256);
        StringBuilder sb = _sb;
        Dictionary<string, int>? keyCounts = null;
        for (int i = 0; i < e.Children.Count; i++)
        {
            Element child = e.Children[i];
            sb.Clear();
            if (child.Key is string key)
            {
                keyCounts ??= new Dictionary<string, int>();
                keyCounts.TryGetValue(key, out int occurrence);
                occurrence++;
                keyCounts[key] = occurrence;
                sb.Append(parentId).Append('/').Append(key);
                if (occurrence > 1)
                {
                    _duplicates.Add(sb.ToString());   // 基础 id(无后缀),每次重复各记一次
                    sb.Append('#').Append(occurrence);   // 第 n 次出现:#n 后缀(I-3 消歧)
                }
            }
            else
            {
                sb.Append(parentId).Append('/').Append(child.GetType().Name + i);
            }
            Visit(child, sb.ToString(), sb);
        }
    }
}
