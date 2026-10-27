namespace SharpUI;

/// <summary>键盘事件(按时间序)。Key 为自有 SharpUI.Key(T3 平移:旧窗口层同构枚举已随包退役,
/// 值序零形变 —— T1 镜像交付);Ctrl 位供 TextEditing 编辑语义(D22)。</summary>
public readonly record struct KeyEventData(Key Key, bool Down, bool Shift, bool Ctrl = false);

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

/// <summary>可激活元素(T2):指针"按下+松开同元素"与键盘"聚焦+Space/Enter"的统一激活管道
/// (ITextFeedable 式内部接口,消费点 = Interaction.Update 松开分支 + FocusManager.HandleKeys 激活分支)。
/// ButtonElement 恒可激活;Checkbox/Radio 以 Enabled 作 CanActivate 门 —— 禁用拒激活,
/// 点击账本(ClickedIds)与回调一致不记禁用元素。</summary>
internal interface IActivatable
{
    /// <summary>当前是否接受激活(禁用元素 false)。</summary>
    bool CanActivate { get; }

    /// <summary>激活语义:Button = onClick;Checkbox = onChanged(!isChecked);Radio = onSelected(未选中时)。</summary>
    void InvokeClick();
}

public readonly record struct InputState(
    PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased,
    Element? Hovered, Element? Focused = null,
    string? HoveredId = null, string? FocusedId = null,
    /// <summary>本帧完成点击的 id 账本(指针路径当帧;键盘激活经队列下一帧可见,裁定#18)。调用方不得跨帧持有该列表(别名复用内部缓冲)。</summary>
    IReadOnlyList<string>? ClickedIds = null,
    /// <summary>滚轮增量(D19):App 在构建前对上帧树调 Scroll.Dispatch 消费。</summary>
    float WheelDelta = 0f,
    /// <summary>拖拽捕获原语(T2):捕获中的元素稳定 id;null=无。LeftDown 命中 CapturesDrag 元素时置位,
    /// LeftUp 释放;捕获期间 HoveredId 锁定捕获者(HitTest 路径旁路),新 LeftDown 覆盖旧捕获。
    /// 按窗口会话隔离(同 PressedOn),本字段为当帧快照。</summary>
    string? CapturedId = null)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

/// <summary>每窗输入会话(T6 多窗):按下目标/捕获者/点击账本/键盘激活队列按窗口隔离,取代 Interaction 静态字段。</summary>
public sealed class InputSession
{
    internal string? PressedOn;   // 按下瞬间的命中元素稳定 id;点击 = 按下与松开同 id(规格书 D10)
    internal string? CapturedId;  // 捕获中的元素稳定 id(T2 捕获原语);null=无 —— LeftUp 释放,新 LeftDown 覆盖
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
        string? capturedAtFrameStart = session.CapturedId;   // 上帧遗留捕获(跨帧拖拽)
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
                // 捕获原语(T2):新 LeftDown 覆盖旧捕获 —— 命中声明可捕获的命中目标即改写,否则清空
                session.CapturedId = hovered is { CapturesDrag: true, ResolvedId: not null }
                    ? hovered.ResolvedId
                    : null;
                if (hovered is { Focusable: true })
                    focus?.SetFocus(hovered);
                if (hovered is null)
                    focus?.Clear();
            }
            else
            {
                released = true;
                // 捕获旁路(T2):松开路由到捕获元素(拖出元素外松开仍属捕获者,HitTest 路径旁路);
                // 非捕获元素维持空间命中。捕获者若已不在本帧树(重建后移除)则回退空间命中。
                Element? routed = session.CapturedId is not null
                    ? TreeWalk.Find(root, session.CapturedId) ?? hovered
                    : hovered;
                if (session.PressedOn is not null && routed?.ResolvedId == session.PressedOn
                    && routed is IActivatable act && act.CanActivate)
                {
                    act.InvokeClick();
                    if (routed.ResolvedId is string id) session.Clicks.Add(id);
                }
                session.PressedOn = null;
                session.CapturedId = null;   // LeftUp 释放捕获
            }
        }
        bool leftDown = (raw.LeftHeld && !released) || pressed;
        // 捕获期间 HoveredId 锁定捕获者:捕获进行中按捕获者报告(HitTest 旁路的 Motion 面);
        // 本帧 Up 收尾的释放帧仍按捕获期报告(捕获持续到 Up 生效),下一帧回归空间命中。
        string? captureLock = session.CapturedId
            ?? (released && !pressed ? capturedAtFrameStart : null);
        Element? hoverEffective = captureLock is not null
            ? TreeWalk.Find(root, captureLock) ?? hovered
            : hovered;
        return new InputState(
            raw.Mouse, leftDown, pressed, released, hoverEffective,
            HoveredId: hoverEffective?.ResolvedId,
            ClickedIds: session.Clicks.Count > 0 ? session.Clicks : null,
            WheelDelta: raw.WheelDelta,
            CapturedId: session.CapturedId);
    }
}
