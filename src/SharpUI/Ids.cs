using System.Text;

namespace SharpUI;

/// <summary>每帧为元素树计算稳定 id:Key 优先,类型名+兄弟索引回退(规格书 D10)。
/// LastDuplicateKeys 记录同父同键兄弟(用户错误,React 纪律);不同分支同键合法。</summary>
public static class IdResolver
{
    [ThreadStatic]
    static StringBuilder? _sb;

    static readonly List<string> _duplicates = new();

    public static IReadOnlyList<string> LastDuplicateKeys => _duplicates;

    public static void Assign(Element root)
    {
        _sb ??= new StringBuilder(256);
        _duplicates.Clear();
        Visit(root, "", _sb);
    }

    static void Visit(Element e, string parentId, StringBuilder sb)
    {
        e.ResolvedId = parentId;
        if (e.Children is not { Count: > 0 }) return;
        HashSet<string>? seenKeys = null;
        for (int i = 0; i < e.Children.Count; i++)
        {
            Element child = e.Children[i];
            if (child.Key is string key)
            {
                seenKeys ??= new HashSet<string>();
                sb.Clear();
                sb.Append(parentId).Append('/').Append(key);
                if (!seenKeys.Add(key))
                    _duplicates.Add(sb.ToString());
            }
            sb.Clear();
            sb.Append(parentId).Append('/').Append(child.Key ?? child.GetType().Name + i);
            Visit(child, sb.ToString(), sb);
        }
    }
}
