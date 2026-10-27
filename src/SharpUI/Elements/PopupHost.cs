namespace SharpUI;

/// <summary>浮层种类(F-1):Dialog=模态居中;Menu=锚定下拉;Tooltip=悬停提示(无焦点语义,对 Esc 透明)。</summary>
internal enum PopupKind { Dialog, Menu, Tooltip }

/// <summary>浮层条目(每窗栈;F-1)。Root 为浮层根元素(Focusable 树)。Arrange 责任分层(D5 T2 已知项 B
/// 裁定):Dialog 类根由 App.Run 每帧重排(居中随窗口 resize 重算);Menu/Tooltip 由打开者在 Push 前
/// Arrange 定格(锚定语义)。AnchorRect 为锚定矩形(逻辑坐标;Dialog=全屏居中忽略)。</summary>
internal sealed class Popup
{
    public Popup(Element root, RectF anchorRect, PopupKind kind)
    {
        Root = root;
        AnchorRect = anchorRect;
        Kind = kind;
    }

    public Element Root { get; }
    public RectF AnchorRect { get; }
    public PopupKind Kind { get; }
    public bool DismissOnOutsideClick { get; init; } = true;
    public Action? OnClosed { get; init; }   // Pop 时收尾回调(焦点归还等,T2/T3 消费)

    /// <summary>Esc 消费钩子(T2 Dialog:栈顶非 Tooltip 的 IsCancel 按钮=Esc 等效):返回 true=已消费
    ///(合成点击+关闭),FocusManager 不再走通用弹栈;null/未消费走 PopTopNonTooltip(Esc 仍关闭)。</summary>
    public Func<bool>? OnEscape { get; init; }

    /// <summary>Enter 缺省动作钩子(T2 Dialog:IsDefault 按钮,焦点在非可激活元素时的 Enter):
    /// 返回 true=已消费;null/未消费=既有 no-op。</summary>
    public Func<bool>? OnEnterDefault { get; init; }
}

/// <summary>每窗浮层栈(静态按窗服务定位,同 App.Atlas 模式;F-1)。App.Run 每帧接线:
/// 注册(声明树后逐层 TreeWalk.Register)/绘制(声明树 Draw 后栈底→栈顶)/命中与外点
/// (Interaction.Update 经 session 句柄)/Esc(FocusManager.HandleKeys 经 focus 句柄)。
/// Push/Pop 为应用回调内命令式动作(分配自由);空栈查询零分配(Array.Empty 兜底)。
/// 可访问性裁定:Popup 为 internal(简报 F-1 定形),故涉其签名的 Push/StackOf 随之 internal
/// (public 方法暴露 internal 类型不可编译;消费方 T2-T4 同程序集,测试经 InternalsVisibleTo)。</summary>
public static class PopupHost
{
    static readonly Dictionary<IntPtr, List<Popup>> _stacks = new();
    static int _seq;   // 自动根键序号(Root.Key 缺省时补唯一键,保围栏前缀语义;UI 线程单线程访问)

    /// <summary>入栈(后进=栈顶=在上)。同时一次性指派浮层树稳定 id:根 Key 缺省补 "__popupN"
    ///(T1 评审携带①:双下划线前缀防与用户键 "popup1" 碰撞),根 ResolvedId="/键"、子树经
    /// IdResolver.AssignChildren —— 焦点围栏以 scopeId+"/" 前缀圈定子树,空串根会吞掉整棵声明树
    ///(IdResolver.Assign 语义下声明树根恒为 ""),浮层根必须避开。</summary>
    internal static void Push(IntPtr sdlWindow, Popup popup)
    {
        popup.Root.Key ??= "__popup" + ++_seq;
        popup.Root.ResolvedId = "/" + popup.Root.Key;
        IdResolver.AssignChildren(popup.Root);
        StackFor(sdlWindow).Add(popup);
    }

    /// <summary>弹栈顶并触发其 OnClosed(回调内分配自由;焦点归还等收尾由 T2/T3 在回调中落)。
    /// 盲弹契约:调用方保证目标即栈顶(Esc 路由 PopTopNonTooltip/Interaction 外点消费 —— 取栈顶快照后
    /// 才动作);自关闭类(可能被后进浮层叠于其上)走 <see cref="Remove"/> 按身份移除,盲弹会误弹他者。</summary>
    public static void Pop(IntPtr sdlWindow)
    {
        List<Popup> stack = StackFor(sdlWindow);
        if (stack.Count == 0) return;
        Popup top = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        top.OnClosed?.Invoke();
    }

    /// <summary>按实例移除(D5 T2 评审承重:叠栈下自关闭须按身份,栈中任意位、其余栈序保持;D5 T3 起
    /// Dialog.Close/Menu.Close/Tooltip 移除共用)。不在栈为无操作(幂等 —— Interaction 外点 Pop 与
    /// Draw 侧自 Remove 可能同帧同目标;不触发 OnClosed,目标已不在栈即视为已收尾)。</summary>
    internal static void Remove(IntPtr sdlWindow, Popup popup)
    {
        if (!_stacks.TryGetValue(sdlWindow, out List<Popup>? s)) return;
        int i = s.LastIndexOf(popup);   // 引用相等(Popup 未覆写 Equals)
        if (i < 0) return;
        s.RemoveAt(i);
        popup.OnClosed?.Invoke();
    }

    public static int Count(IntPtr sdlWindow) =>
        _stacks.TryGetValue(sdlWindow, out List<Popup>? s) ? s.Count : 0;

    /// <summary>该窗栈的只读观测(索引 0=栈底,末位=栈顶;测试/调试用,勿跨帧持有 —— live 列表别名)。</summary>
    internal static IReadOnlyList<Popup> StackOf(IntPtr sdlWindow) =>
        _stacks.TryGetValue(sdlWindow, out List<Popup>? s) ? s : Array.Empty<Popup>();

    /// <summary>栈顶浮层(null=空栈);Interaction 命中优先/外点消费接线。</summary>
    internal static Popup? TopOf(IntPtr sdlWindow) =>
        _stacks.TryGetValue(sdlWindow, out List<Popup>? s) && s.Count > 0 ? s[^1] : null;

    /// <summary>Esc 弹栈:自栈顶向下首个非 Tooltip(Tooltip 无焦点语义,对 Esc 透明 —— 不拦截下层关闭);
    /// 全 Tooltip 栈 Esc 为无操作(不清焦点、不弹)。</summary>
    internal static void PopTopNonTooltip(IntPtr sdlWindow)
    {
        List<Popup> stack = StackFor(sdlWindow);
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i].Kind == PopupKind.Tooltip) continue;
            Popup p = stack[i];
            stack.RemoveAt(i);
            p.OnClosed?.Invoke();
            return;
        }
    }

    /// <summary>Esc 钩子分发(T2):自栈顶向下首个非 Tooltip 若设 OnEscape 则调之(IsCancel=Esc 等效);
    /// 未设/全 Tooltip/空栈返回 false=走通用弹栈。</summary>
    internal static bool TryConsumeEscape(IntPtr sdlWindow) =>
        TopNonTooltipOf(sdlWindow)?.OnEscape?.Invoke() ?? false;

    /// <summary>Enter 缺省动作分发(T2):自栈顶向下首个非 Tooltip 若设 OnEnterDefault(IsDefault 按钮)
    /// 则调之;未设/空栈返回 false=既有 no-op。</summary>
    internal static bool TryConsumeEnterDefault(IntPtr sdlWindow) =>
        TopNonTooltipOf(sdlWindow)?.OnEnterDefault?.Invoke() ?? false;

    static Popup? TopNonTooltipOf(IntPtr sdlWindow)
    {
        if (!_stacks.TryGetValue(sdlWindow, out List<Popup>? s)) return null;
        for (int i = s.Count - 1; i >= 0; i--)
            if (s[i].Kind != PopupKind.Tooltip) return s[i];
        return null;
    }

    /// <summary>窗销毁清栈(App.Run 移除窗/退出兜底;不触发 OnClosed —— 窗已死,回调可能触已释放资源)。</summary>
    internal static void Clear(IntPtr sdlWindow) => _stacks.Remove(sdlWindow);

    static List<Popup> StackFor(IntPtr sdlWindow)
    {
        if (!_stacks.TryGetValue(sdlWindow, out List<Popup>? s))
            _stacks[sdlWindow] = s = new List<Popup>(4);
        return s;
    }
}
