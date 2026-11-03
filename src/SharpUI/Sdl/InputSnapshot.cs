namespace SharpUI;

/// <summary>当帧输入快照(T2,W-3 窗口层自研;T3 起 InputMapper.Map(InputSnapshot, float) 消费)。
/// 成员名对齐 旧窗口层 InputSnapshot(旧窗口封装包 4.9.0 接口)以零形变迁移消费面,形制差异两处:
/// ①sealed class + 值元组列表(旧窗口层接口 + MouseEvent/KeyEvent 类实例,每事件一次堆分配;元组消除之);
/// ②MousePosition 用 PointF(库内几何基元;旧窗口层用 System.Numerics.Vector2)。
/// 生命周期契约(同 旧窗口层返回快照的时效语义:其 PumpEvents 返回共享实例,下次泵即被改写):快照内容
/// 有效至该窗下一次 GetSnapshotAndReset —— 届时其内部列表被清空复用(App.Run 帧内同步消费,无影响)。
/// IsMouseDown 的持久按键态(MouseDown[13] 语义)按 MouseButton 枚举值(0..12)编入 ulong 位掩码,
/// 快照创建时定格取值,不随后续事件漂移。</summary>
public sealed class InputSnapshot
{
    internal InputSnapshot(
        PointF mousePosition,
        ulong mouseDownMask,
        float wheelDelta,
        IReadOnlyList<(MouseButton Button, bool Down)> mouseEvents,
        IReadOnlyList<(Key Key, bool Down, ModifierKeys Modifiers)> keyEvents,
        IReadOnlyList<char> keyCharPresses,
        string preedit,
        int preeditStart,
        int preeditLength)
    {
        MousePosition = mousePosition;
        _mouseDownMask = mouseDownMask;
        WheelDelta = wheelDelta;
        MouseEvents = mouseEvents;
        KeyEvents = keyEvents;
        KeyCharPresses = keyCharPresses;
        Preedit = preedit;
        PreeditStart = preeditStart;
        PreeditLength = preeditLength;
    }

    /// <summary>窗口坐标下鼠标位置(最近一次 MOUSEMOTION 更新;初始 Zero,跨帧保留,同旧窗口层)。</summary>
    public PointF MousePosition { get; }

    public IReadOnlyList<(MouseButton Button, bool Down)> MouseEvents { get; }

    public IReadOnlyList<(Key Key, bool Down, ModifierKeys Modifiers)> KeyEvents { get; }

    /// <summary>当帧滚轮增量(多事件累计;取快照后清零)。旧窗口层语义:+= y,忽略 FLIPPED 方向位
    /// (旧窗口层 HandleMouseWheelEvent 源码实证,提交 a121087 —— Windows 恒 NORMAL,macOS 自然滚动的
    /// 方向差异随基线保留,报告注明)。</summary>
    public float WheelDelta { get; }

    public IReadOnlyList<char> KeyCharPresses { get; }

    /// <summary>IME 组合串(preedit,SDL_TEXTEDITING 整串替换;""=无组合)。审计修复波 2A-4(A4):
    /// 泵层通道 —— 修复前 SDL_TEXTEDITING 在泵中被静默丢弃,组合串对框架不可达(Windows 靠系统 IME
    /// 自绘组合窗兜底,Linux/macOS 完全不可见)。跨帧保留的状态语义(组合停顿的无事件帧不闪断),
    /// 取快照不清空。内联渲染消费方(TextField 下划线组合段)留后续波次。</summary>
    public string Preedit { get; }

    /// <summary>组合串内编辑光标起点(SDL_TextEditingEvent.start 透传,SDL 语义原样)。</summary>
    public int PreeditStart { get; }

    /// <summary>组合串内编辑选中长(SDL_TextEditingEvent.length 透传,SDL 语义原样)。</summary>
    public int PreeditLength { get; }

    /// <summary>持久按键态:MOUSEBUTTONDOWN/UP 维护、跨帧保留(旧窗口层 MouseDown 数组同语义)。</summary>
    public bool IsMouseDown(MouseButton b) => (_mouseDownMask & (1ul << (int)b)) != 0;

    readonly ulong _mouseDownMask;
}
