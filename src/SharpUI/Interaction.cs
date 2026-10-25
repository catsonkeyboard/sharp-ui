namespace SharpUI;

/// <summary>App 从 SDL InputSnapshot 映射;LeftEvents 按时间序记录左键按下(true)/抬起(false)。</summary>
public readonly record struct RawInput(PointF Mouse, bool[] LeftEvents);

public readonly record struct InputState(PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased, Element? Hovered)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

public static class Interaction
{
    static bool _leftHeld;
    static int[]? _pressedOn;   // 按下瞬间的命中路径;点击 = 按下与松开命中同一路径(App 每帧重建树,实例引用不跨帧稳定)

    public static InputState Update(Element root, in RawInput raw)
    {
        bool pressed = false, released = false;
        (Element Hit, int[] Path)? hit = HitTestWithPath(root, raw.Mouse);
        Element? hovered = hit?.Hit;
        int[]? hoveredPath = hit?.Path;
        foreach (bool down in raw.LeftEvents)
        {
            if (down)
            {
                _leftHeld = true;
                pressed = true;
                _pressedOn = hoveredPath;
            }
            else
            {
                _leftHeld = false;
                released = true;
                if (_pressedOn is not null && hovered is ButtonElement button
                    && PathsEqual(hoveredPath!, _pressedOn))
                {
                    button.InvokeClick();
                }
                _pressedOn = null;
            }
        }
        return new InputState(raw.Mouse, _leftHeld, pressed, released, hovered);
    }

    /// <summary>与 Element.HitTest 同序(子节点从后往前、Frame.Contains 门控),额外记录根到命中目标的兄弟索引路径。</summary>
    static (Element Hit, int[] Path)? HitTestWithPath(Element root, PointF p)
    {
        var path = new List<int>(8);
        Element? hit = Descend(root, p, path);
        return hit is null ? null : (hit, path.ToArray());
    }

    static Element? Descend(Element e, PointF p, List<int> path)
    {
        if (!e.Frame.Contains(p)) return null;
        if (e.Children is not null)
            for (int i = e.Children.Count - 1; i >= 0; i--)
            {
                path.Add(i);
                if (Descend(e.Children[i], p, path) is { } hit)
                    return hit;
                path.RemoveAt(path.Count - 1);
            }
        return e.IsHitTarget ? e : null;
    }

    static bool PathsEqual(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
