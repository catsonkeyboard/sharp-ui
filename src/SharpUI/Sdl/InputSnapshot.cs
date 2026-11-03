namespace SharpUI;

/// <summary>全按钮鼠标事件记录(波 3A/B2/B10):SDL_MouseButtonEvent 三元组(button/state/clicks,
/// SDL_events.h L314-329)的直译。Clicks = SDL 双击/三击计数(1=单击、2=双击、3=三击;SDL_mouse.c
/// 按 500ms 窗口累计),双击/词选消费留 3B。既有 (MouseButton, bool) 元组通道保留为零形变兼容面。</summary>
public readonly record struct MouseButtonEventData(MouseButton Button, bool Down, int Clicks);

/// <summary>当帧输入快照(T2,W-3 窗口层自研;T3 起 InputMapper.Map(InputSnapshot, float) 消费)。
/// 成员名对齐 旧窗口层 InputSnapshot(旧窗口封装包 4.9.0 接口)以零形变迁移消费面,形制差异两处:
/// ①sealed class + 值元组列表(旧窗口层接口 + MouseEvent/KeyEvent 类实例,每事件一次堆分配;元组消除之);
/// ②MousePosition 用 PointF(库内几何基元;旧窗口层用 System.Numerics.Vector2)。
/// 波 3A 增补两条新通道(既有成员零形变):ButtonEvents(全按钮 + clicks)/ KeyEventsFull
/// (KeyEventData 记录,含 Alt/Gui/IsRepeat)。
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
        int preeditLength,
        IReadOnlyList<MouseButtonEventData>? buttonEvents = null,
        IReadOnlyList<KeyEventData>? keyEventsFull = null)
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
        ButtonEvents = buttonEvents ?? Array.Empty<MouseButtonEventData>();
        KeyEventsFull = keyEventsFull ?? Array.Empty<KeyEventData>();
    }

    /// <summary>窗口坐标下鼠标位置(最近一次 MOUSEMOTION 更新;初始 Zero,跨帧保留,同旧窗口层)。</summary>
    public PointF MousePosition { get; }

    public IReadOnlyList<(MouseButton Button, bool Down)> MouseEvents { get; }

    /// <summary>全按钮事件通道(波 3A/B2/B10):含右/中/X1/X2 全部按钮与 SDL clicks 计数。
    /// 设计裁量(报告注明):选择"单一全按钮通道 + 保留既有元组 MouseEvents 兼容面",而非
    /// RightEvents/MiddleEvents/OtherEvents 三分通道 —— 全通道保时间序(右键按下与左键松开的相对
    /// 顺序可辨),三分通道会丢失交错序且成员面更宽。</summary>
    public IReadOnlyList<MouseButtonEventData> ButtonEvents { get; }

    public IReadOnlyList<(Key Key, bool Down, ModifierKeys Modifiers)> KeyEvents { get; }

    /// <summary>全键事件记录通道(波 3A/B11/B12):KeyEventData 含 Alt/Gui 修饰位与 IsRepeat 标记
    /// (InputMapper.Map 的 KeyEventData[] 源)。既有 (Key, bool, ModifierKeys) 元组通道零形变保留。</summary>
    public IReadOnlyList<KeyEventData> KeyEventsFull { get; }

    /// <summary>当帧滚轮增量(多事件累计;取快照后清零)。float 语义保留,来源两段演进:旧窗口层/T2
    /// 为整型 y 累计(忽略 FLIPPED);波 3A/A6 起 = preciseY(2.0.18+ 浮点,0 回落整型 y)且 FLIPPED
    /// 归一(方向位反转的值乘 -1,"上=+1"恒成立,macOS 自然滚动不再反向)。横向增量(preciseX/x)
    /// 本波未消费(B9 横向滚动留后续波次),字段已声明(SdlEvents 注)。</summary>
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
