namespace SharpUI;

/// <summary>即时模式逃生舱句柄(D14):Clicked/Hovered 按稳定 id 后缀匹配本帧账本。</summary>
public readonly struct ImmediateButton
{
    readonly UiScope _scope;
    readonly string _path;   // 含 rootKey 的局部路径,如 "dbg/PanelElement1/ButtonElement0"
    internal ImmediateButton(UiScope scope, string path)
    {
        _scope = scope;
        _path = path;
    }

    public bool Clicked() => _scope.LedgerContains(_path);

    public bool Hovered() =>
        UiContext.CurrentInput.HoveredId?.EndsWith("/" + _path, StringComparison.Ordinal) == true;
}

public readonly struct ImmediateText
{
    internal ImmediateText(string _) { }
}

/// <summary>即时模式作用域:把调用序列记录为元素树,与声明式共用布局/绘制管线(D11/D14)。</summary>
public sealed class UiScope
{
    readonly PanelElement _root;
    readonly string _rootKey;
    readonly Stack<PanelElement> _stack = new();      // 当前父链(root 在底)
    readonly Stack<string> _pathStack = new();        // 与 _stack 同进同退;root 段为 ""
    readonly Dictionary<PanelElement, int> _childIndex = new();

    internal UiScope(PanelElement root, string rootKey)
    {
        _root = root;
        _rootKey = rootKey;
        _stack.Push(root);
        _pathStack.Push("");
    }

    /// <summary>本作用域构建的根容器(测试/宿主取回即时树用)。</summary>
    internal PanelElement Root => _root;

    internal bool LedgerContains(string path)
    {
        IReadOnlyList<string>? ledger = UiContext.CurrentInput.ClickedIds;
        if (ledger is null) return false;
        string suffix = "/" + path;
        for (int i = 0; i < ledger.Count; i++)
            if (ledger[i].EndsWith(suffix, StringComparison.Ordinal)) return true;
        return false;
    }

    string NextSegment<TElement>() where TElement : Element
    {
        PanelElement parent = _stack.Peek();
        int i = _childIndex.TryGetValue(parent, out int seen) ? seen : 0;
        _childIndex[parent] = i + 1;
        return typeof(TElement).Name + i;             // 与 IdResolver 回退公式一致(D14)
    }

    public ImmediateText Text(string text, float size = 16f, uint color = 0xFFFFFF)
    {
        var t = new TextElement(text) { FontSize = size, Color = Color32.FromRgb(color) };
        t.Key = NextSegment<TextElement>();
        _stack.Peek().Add(t);
        return new ImmediateText(text);
    }

    public ImmediateButton Button(string label)
    {
        var b = new ButtonElement(label, static () => { });   // 逃生舱内点击走账本,不走 Action
        string segment = NextSegment<ButtonElement>();
        b.Key = segment;
        _stack.Peek().Add(b);
        return new ImmediateButton(this, AggregatePath() + "/" + segment);
    }

    public void Spacer(SizeF size)
    {
        var spacer = new SharpUI.Spacer(size);
        spacer.Key = NextSegment<SharpUI.Spacer>();
        _stack.Peek().Add(spacer);
    }

    public void Row(Action<UiScope> children) => Nest(Axis.Horizontal, children);

    public void Column(Action<UiScope> children) => Nest(Axis.Vertical, children);

    void Nest(Axis axis, Action<UiScope> children)
    {
        var panel = new PanelElement();
        string segment = NextSegment<PanelElement>();
        panel.Key = segment;
        panel.Style.Axis = axis;
        _stack.Peek().Add(panel);
        _stack.Push(panel);
        _pathStack.Push(segment);
        children(this);
        _stack.Pop();
        _pathStack.Pop();
    }

    string AggregatePath()
    {
        var sb = new System.Text.StringBuilder(_rootKey);
        foreach (string segment in _pathStack.Reverse())   // Stack 枚举序为 LIFO,反转回压栈序
            if (segment.Length > 0)
                sb.Append('/').Append(segment);
        return sb.ToString();
    }

    public T State<T>(string key, T initial = default!) =>
        App.State is { } store ? store.Get("imm:" + _rootKey + ":" + key, initial) : initial;

    /// <summary>写回即时状态(命名对齐 StateStore.Set;与 T State 同参数表,C# 不允许仅返回类型不同的重载)。</summary>
    public void SetState<T>(string key, T value)
    {
        App.State?.Set("imm:" + _rootKey + ":" + key, value);
    }
}
