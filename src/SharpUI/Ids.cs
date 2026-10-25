using System.Text;

namespace SharpUI;

/// <summary>每帧为元素树计算稳定 id:Key 优先,类型名+兄弟索引回退(规格书 D10)。</summary>
public static class IdResolver
{
    [ThreadStatic]
    static StringBuilder? _sb;

    public static void Assign(Element root)
    {
        _sb ??= new StringBuilder(256);
        Visit(root, "", _sb);
    }

    static void Visit(Element e, string parentId, StringBuilder sb)
    {
        e.ResolvedId = parentId;
        if (e.Children is not { Count: > 0 }) return;
        for (int i = 0; i < e.Children.Count; i++)
        {
            Element child = e.Children[i];
            sb.Clear();
            sb.Append(parentId).Append('/').Append(child.Key ?? child.GetType().Name + i);
            Visit(child, sb.ToString(), sb);
        }
    }
}
