namespace SharpUI;

/// <summary>Dialog 按钮描述(D5 T2):Label/OnClick 必备;IsDefault=焦点在非可激活元素时 Enter 触发
///(聚焦按钮时 Enter 激活按钮自身,既有语义);IsCancel=Esc 等效(合成点击+关闭)。</summary>
public sealed class DialogButton(string label, Action onClick)
{
    public string Label { get; } = label;
    public Action OnClick { get; } = onClick;
    public bool IsDefault { get; init; }     // Enter 触发(聚焦非按钮时)
    public bool IsCancel { get; init; }      // 效果同 Esc
}

/// <summary>模态对话框浮层根(D5 T2):全屏半透明遮罩(自身=命中目标,可配遮罩点击关闭)+
/// 居中面板(浮层底色 0x1E1E24/圆角 8:标题+内容槽+按钮行)。
/// 打开/关闭经 DialogExtensions.ShowDialog(PopupHost.Push(Dialog 类,全屏锚)+PushScope 焦点围栏,
/// 关闭 OnClosed → PopScopeOf 按身份归还打开者焦点);根/面板/遮罩均为命中目标(T1 评审携带② —— 浮层空白区
/// 不设命中目标会被外点 dismiss 误关)。布局责任:App.Run 对 Dialog 类浮层根每帧 Arrange(居中随
/// resize 重算,T1 已知项 B 裁定;Menu/Tooltip 保持打开者 Arrange 定格);合成测试自行 Arrange。</summary>
public sealed class DialogElement : PanelElement, IActivatable
{
    readonly DialogButton[] _buttons;
    readonly bool _dismissOnMaskClick;
    bool _closed;
    IntPtr _owner = IntPtr.Zero;   // ShowDialog 绑定(PopupHost 按窗服务定位键)
    Popup? _popup;                 // 同上:Close 按身份移除的实例凭证(D5 T2 评审承重)

    internal DialogElement(string title, Action<PanelElement> build, DialogButton[] buttons,
        bool dismissOnMaskClick = true)
    {
        _buttons = buttons;
        _dismissOnMaskClick = dismissOnMaskClick;
        Background = Color32.Black.WithAlpha(0x80);   // 遮罩:半透明黑(alpha 0x80,色板)
        IsHitTarget = true;                           // 遮罩命中目标(携带②)
        Style.MainAlign = MainAxisAlignment.Center;   // 面板全屏居中
        Style.CrossAlign = CrossAxisAlignment.Center;

        var panel = new PanelElement();
        panel.Fill(0x1E1E24).Radius(8f);              // 面板=浮层底色+圆角 8(色板)
        panel.IsHitTarget = true;                     // 面板 padding 空白区命中=不误关(携带②)
        panel.Style.Padding = new Thickness(16f);
        panel.Style.Gap = 12f;
        panel.Add(new TextElement(title).Size(18f));  // 标题栏文本
        Content = new PanelElement();                 // 内容槽:声明式注入任意元素(build 闭包)
        panel.Add(Content);
        build(Content);
        var row = new PanelElement();                 // 按钮行:右对齐
        row.Style.Axis = Axis.Horizontal;
        row.Style.MainAlign = MainAxisAlignment.End;
        row.Style.Gap = 8f;
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;                            // 闭包捕获循环下标
            row.Add(new ButtonElement(buttons[i].Label, () => InvokeButton(index)));
        }
        panel.Add(row);
        Panel = panel;
        Add(panel);
    }

    /// <summary>内容槽(build 注入目标;测试/宿主定位用)。</summary>
    internal PanelElement Content { get; }

    /// <summary>居中面板(视觉/布局断言定位用)。</summary>
    internal PanelElement Panel { get; }

    /// <summary>按钮统一触发:用户回调后关闭(点击/IsDefault/IsCancel 三渠道同一漏斗)。</summary>
    void InvokeButton(int index)
    {
        _buttons[index].OnClick();
        Close();
    }

    /// <summary>绑定宿主窗句柄与浮层实例(ShowDialog 在 Push 前接线;Close 经 PopupHost.Remove 按身份
    /// 移除 —— Dialog 下可叠 Menu/Tooltip(D5 T3),盲弹 Pop 会误弹栈顶他者成僵尸 Dialog)。</summary>
    internal void Bind(IntPtr owner, Popup popup)
    {
        _owner = owner;
        _popup = popup;
    }

    /// <summary>关闭(按身份 Remove 自身);幂等 —— 外部直接 PopupHost.Pop/Remove 的路径经 NotifyClosed
    /// 置位,防二次移除误弹下层浮层。</summary>
    internal void Close()
    {
        if (_closed || _owner == IntPtr.Zero || _popup is null) return;
        _closed = true;
        PopupHost.Remove(_owner, _popup);
    }

    /// <summary>OnClosed 收尾置位(外部 Pop 路径的幂等记账;ShowDialog 接线)。</summary>
    internal void NotifyClosed() => _closed = true;

    /// <summary>Esc 消费(Popup.OnEscape 接线):IsCancel=Esc 等效(合成点击+关闭);
    /// 无 IsCancel 按钮返回 false → FocusManager 走通用弹栈(Esc 仍关闭,不触发任何按钮)。</summary>
    internal bool ConsumeEscape()
    {
        for (int i = 0; i < _buttons.Length; i++)
            if (_buttons[i].IsCancel) { InvokeButton(i); return true; }
        return false;
    }

    /// <summary>Enter 缺省动作(Popup.OnEnterDefault 接线):焦点不在可激活元素时 IsDefault 触发。</summary>
    internal bool ConsumeEnterDefault()
    {
        for (int i = 0; i < _buttons.Length; i++)
            if (_buttons[i].IsDefault) { InvokeButton(i); return true; }
        return false;
    }

    /// <summary>IActivatable=遮罩点击(按下+松开同根):可配关闭;不可关时仅吞按压(焦点留守围栏内)。</summary>
    public bool CanActivate => true;

    public void InvokeClick()
    {
        if (_dismissOnMaskClick) Close();
    }
}

/// <summary>Dialog 打开 API(D5 T2)。DSL 裁定:浮层打开为 App/AppWindow 级命令式动作(非元素工厂),
/// 与 Ui 的声明式工厂分流 —— 本文件自持扩展类,不入 Ui.cs。App 扩展转发主窗(单窗 API 语义);
/// 多窗/合成测试用 AppWindow 扩展(Focus.SdlWindow 为 PopupHost 服务定位键,CreateWindow 接线)。
/// 签名形态:(title, build, params buttons) 为主(简报签名,遮罩默认可关);(title, build, bool,
/// params buttons) 为配置重载 —— bool 置于 params 前的可选位会拦截位置实参(仅命名传法可达),
/// 故拆双载而非可选参,位置调用两种形态各自直达。</summary>
public static class DialogExtensions
{
    public static void ShowDialog(this App app, string title, Action<PanelElement> build,
        params DialogButton[] buttons) =>
        ShowDialog(app.Windows[0], title, build, dismissOnMaskClick: true, buttons);

    public static void ShowDialog(this App app, string title, Action<PanelElement> build,
        bool dismissOnMaskClick, params DialogButton[] buttons) =>
        ShowDialog(app.Windows[0], title, build, dismissOnMaskClick, buttons);

    /// <summary>打开模态 Dialog:遮罩+居中面板(标题+内容槽+按钮行);焦点围栏入栈(PushScope),
    /// 关闭(Pop → OnClosed)PopScope 归还打开者焦点;初始焦点=Dialog 内首个可聚焦元素(DOM 序,
    /// 内容槽先于按钮行 —— 重命名类弹窗即聚焦输入框)。Esc/IsCancel 关闭;IsDefault=Enter(焦点在
    /// 非可激活元素时);遮罩点击默认关闭(dismissOnMaskClick:false 关闭之)。同时仅一个 Dialog:
    /// 重复 ShowDialog 抛 InvalidOperationException(单实例裁定)。</summary>
    public static void ShowDialog(this AppWindow window, string title, Action<PanelElement> build,
        params DialogButton[] buttons) =>
        ShowDialog(window, title, build, dismissOnMaskClick: true, buttons);

    public static void ShowDialog(this AppWindow window, string title, Action<PanelElement> build,
        bool dismissOnMaskClick, params DialogButton[] buttons)
    {
        IntPtr handle = window.Focus.SdlWindow;
        foreach (Popup p in PopupHost.StackOf(handle))
            if (p.Kind == PopupKind.Dialog)
                throw new InvalidOperationException(
                    "ShowDialog: this window already has an open Dialog (single-dialog policy).");
        var dialog = new DialogElement(title, build, buttons, dismissOnMaskClick);
        var popup = new Popup(dialog, new RectF(0, 0, 0, 0), PopupKind.Dialog)
        {
            DismissOnOutsideClick = false,   // 根=全屏遮罩,真外点不存在;遮罩点击走 DialogElement.IActivatable(可配)
            // 关闭收尾:围栏按身份出栈=归还打开者焦点(PopScopeOf:Dialog 下可叠 Menu,盲弹会误弹其围栏)
            OnClosed = () => { dialog.NotifyClosed(); window.Focus.PopScopeOf(dialog.ResolvedId!); },
            OnEscape = dialog.ConsumeEscape,                       // Esc → IsCancel 合成点击(T2 接线)
            OnEnterDefault = dialog.ConsumeEnterDefault,           // Enter 无激活焦点 → IsDefault(T2 接线)
        };
        dialog.Bind(handle, popup);
        PopupHost.Push(handle, popup);
        window.Focus.PushScope(dialog.ResolvedId!);   // 围栏入栈:root id 由 Push 指派("/__popupN")
        if (FindFirstFocusable(dialog) is { } first)
            window.Focus.SetFocus(first);             // 初始焦点入圈(打开者焦点已由 PushScope 记账)
    }

    static Element? FindFirstFocusable(Element e)
    {
        if (e.Focusable) return e;
        if (e.Children is not null)
            foreach (Element c in e.Children)
                if (FindFirstFocusable(c) is { } hit) return hit;
        return null;
    }
}
