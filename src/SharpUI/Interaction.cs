using Veldrid;

namespace SharpUI;

/// <summary>键盘事件(按时间序)。Key 暂为 Veldrid.Key(公开面技术债,规格书 D12;T1 起自有 SharpUI.Key
/// 镜像就位,T3 删包时签名平移 —— 本文件及 Focus/TextEditing 的 Veldrid. 前缀即消歧:SharpUI.Key 同名
/// 遮蔽 using Veldrid 的导入,C# 规则下内层命名空间成员优先);Ctrl 位供 TextEditing 编辑语义(D22)。</summary>
public readonly record struct KeyEventData(Veldrid.Key Key, bool Down, bool Shift, bool Ctrl = false);

/// <summary>App 从 SDL InputSnapshot 经 InputMapper 映射;LeftHeld 为持久按键态(D15);WheelDelta 为滚轮增量(D19)。
/// Chars 为字符通道(D22):源自 KeyCharPresses 的当帧文本输入,空则 null。</summary>
public readonly record struct RawInput(
    PointF Mouse, bool[] LeftEvents, KeyEventData[]? KeyEvents = null, bool LeftHeld = false,
    float WheelDelta = 0f, char[]? Chars = null);

/// <summary>D22 键路由前瞻:聚焦元素接收当帧键与字符的通道。T3 的 TextFieldElement 实现之,App.FeedFocused 判接口分发。</summary>
internal interface ITextFeedable
{
    void Feed(KeyEventData[]? keys, char[]? chars);
}

public readonly record struct InputState(
    PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased,
    Element? Hovered, Element? Focused = null,
    string? HoveredId = null, string? FocusedId = null,
    /// <summary>本帧完成点击的 id 账本(指针路径当帧;键盘激活经队列下一帧可见,裁定#18)。调用方不得跨帧持有该列表(别名复用内部缓冲)。</summary>
    IReadOnlyList<string>? ClickedIds = null,
    /// <summary>滚轮增量(D19):App 在构建前对上帧树调 Scroll.Dispatch 消费。</summary>
    float WheelDelta = 0f)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

/// <summary>每窗输入会话(T6 多窗):按下目标/点击账本/键盘激活队列按窗口隔离,取代 Interaction 静态字段。</summary>
public sealed class InputSession
{
    internal string? PressedOn;   // 按下瞬间的命中元素稳定 id;点击 = 按下与松开同 id(规格书 D10)
    internal readonly List<string> Clicks = new();
    internal readonly List<string> Queued = new();   // 键盘激活排队:下一帧 Update 开头并入账本(裁定#18)
}

public static class Interaction
{
    /// <summary>取消进行中的按压(窗口失焦时由 App 按窗调用;规格书 M2b-i)。</summary>
    public static void CancelPress(InputSession session) => session.PressedOn = null;

    /// <summary>键盘激活入队:HandleKeys 当帧回调已触发,账本在下一帧 Update 开头可见(裁定#18)。</summary>
    public static void QueueClick(InputSession session, string id) => session.Queued.Add(id);

    public static InputState Update(InputSession session, Element root, in RawInput raw, FocusManager? focus = null)
    {
        // 防御(偏离任务书代码清单的一行):未经 Assign 的树直连 Update 时补算稳定 id,
        // 否则 ResolvedId 全 null,`_pressedOn is not null` 门会把所有点击吞掉。
        // App.Run 恒以上帧树(构建时已 Assign)调 Update,生产路径此行零命中。
        if (root.ResolvedId is null) IdResolver.Assign(root);
        bool pressed = false, released = false;
        Element? hovered = root.HitTest(raw.Mouse);
        session.Clicks.Clear();
        if (session.Queued.Count > 0)   // 键盘激活(裁定#18):先清当帧再并入上一帧队列
        {
            session.Clicks.AddRange(session.Queued);
            session.Queued.Clear();
        }
        foreach (bool down in raw.LeftEvents)
        {
            if (down)
            {
                pressed = true;
                session.PressedOn = hovered?.ResolvedId;
                if (hovered is { Focusable: true })
                    focus?.SetFocus(hovered);
                if (hovered is null)
                    focus?.Clear();
            }
            else
            {
                released = true;
                if (session.PressedOn is not null && hovered?.ResolvedId == session.PressedOn
                    && hovered is ButtonElement button)
                {
                    button.InvokeClick();
                    if (hovered.ResolvedId is string id) session.Clicks.Add(id);
                }
                session.PressedOn = null;
            }
        }
        bool leftDown = (raw.LeftHeld && !released) || pressed;
        return new InputState(
            raw.Mouse, leftDown, pressed, released, hovered,
            HoveredId: hovered?.ResolvedId,
            ClickedIds: session.Clicks.Count > 0 ? session.Clicks : null,
            WheelDelta: raw.WheelDelta);
    }
}
