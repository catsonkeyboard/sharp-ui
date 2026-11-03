namespace SharpUI;

/// <summary>鼠标指针形状(波 3A/B13):成员序与 SDL_SystemCursor(SDL_mouse.h L47-61)声明序一致 ——
/// (int)shape 即 SDL_SystemCursor id,映射零转换(守卫测试 SdlWave3ATests 锁序)。
/// 消费接线(TextField→IBeam、可拖拽→SizeAll 等)留 3B。</summary>
public enum CursorShape
{
    Arrow = 0,       // SDL_SYSTEM_CURSOR_ARROW
    IBeam = 1,       // SDL_SYSTEM_CURSOR_IBEAM
    Wait = 2,        // SDL_SYSTEM_CURSOR_WAIT
    Crosshair = 3,   // SDL_SYSTEM_CURSOR_CROSSHAIR
    WaitArrow = 4,   // SDL_SYSTEM_CURSOR_WAITARROW
    SizeNwse = 5,    // SDL_SYSTEM_CURSOR_SIZENWSE
    SizeNesw = 6,    // SDL_SYSTEM_CURSOR_SIZENESW
    SizeWe = 7,      // SDL_SYSTEM_CURSOR_SIZEWE
    SizeNs = 8,      // SDL_SYSTEM_CURSOR_SIZENS
    SizeAll = 9,     // SDL_SYSTEM_CURSOR_SIZEALL
    No = 10,         // SDL_SYSTEM_CURSOR_NO
    Hand = 11,       // SDL_SYSTEM_CURSOR_HAND
}

/// <summary>全局指针形状 API(波 3A/B13 绑定层,波 3B/B13 消费接线):Set 经 SdlCursor 的 12 系统游标
/// 缓存执行 SDL_SetCursor(进程级全局态,作用全体窗口 —— SDL 无每窗游标参数;SDL_CreateSystemCursor
/// 文档语义)。须在视频子系统就绪后(建窗后)调用;UI 线程调用(与窗口 API 同线程契约,SDL 游标态
/// 非线程安全)。消费路径:App.Run Draw 段帧尾调 ApplyFrameTail,按当帧 Hovered/Focused 元素声明的
/// <see cref="Element.CursorShape"/> 统一落形 —— 元素绘制代码不碰游标。</summary>
public static class UiCursor
{
    /// <summary>最近一次应用的形状(测试观察点):null = 默认箭头(SDL_GetDefaultCursor 复原)。</summary>
    internal static CursorShape? LastAppliedForTest { get; private set; }

    /// <summary>设置指针形状(12 系统形之一;平台缺该形时保持当前游标,SDL_GetError 可诊断)。</summary>
    public static void Set(CursorShape shape)
    {
        SdlCursor.Apply(shape);
        LastAppliedForTest = shape;
    }

    /// <summary>复原 SDL 默认游标(SDL_GetDefaultCursor;库所有,不销毁)。</summary>
    public static void UseDefault()
    {
        SdlCursor.ApplyDefault();
        LastAppliedForTest = null;
    }

    /// <summary>帧尾解析(波 3B/B13 纯逻辑):悬停元素声明形优先(指针跟手 —— 悬停按钮 Hand 压过聚焦
    /// 文本框 IBeam);无悬停或悬停为默认箭头时聚焦元素声明形兜底(聚焦文本框即 IBeam);均无声明 →
    /// null = 默认箭头。纯函数零分配。</summary>
    internal static CursorShape? Resolve(Element? hovered, Element? focused)
    {
        CursorShape shape = hovered?.CursorShape ?? CursorShape.Arrow;
        if (shape == CursorShape.Arrow && focused is not null)
            shape = focused.CursorShape;
        return shape == CursorShape.Arrow ? null : shape;
    }

    static CursorShape? _frameTail;

    /// <summary>Draw 管线帧尾统一消费(波 3B/B13):App.Run 每窗绘制段后调用(声明树 + 浮层的输入态
    /// 已在 InputState.Hovered/Focused)。形状未变化时跳过 SDL_SetCursor(句柄缓存本就零分配,去重只为
    /// 省一次原生调用);首帧箭头目标与初值 null 相等,启动零原生调用(游标保持系统默认)。</summary>
    internal static void ApplyFrameTail(Element? hovered, Element? focused)
    {
        CursorShape? target = Resolve(hovered, focused);
        if (target == _frameTail) return;
        _frameTail = target;
        if (target is { } shape) Set(shape);
        else UseDefault();
    }
}

/// <summary>系统游标缓存(internal,波 3A/B13):SDL_CreateSystemCursor 惰性逐形创建(首用一次,
/// 之后 SDL_SetCursor 直接命中句柄 —— GetDelegateForFunctionPointer 热路径分配同理避免重复 Create 的
/// 原生分配)。进程级生存,不逐次 SDL_FreeCursor:12 项有界集反复 create/free 无收益,SDL_Quit 兜底
/// 回收;FreeCursor 绑定保留(SdlEvents 注)。创建失败(Zero,SDL 语义)不缓存,保持当前形状。</summary>
internal static class SdlCursor
{
    static readonly IntPtr[] _handles = new IntPtr[12];   // SDL_NUM_SYSTEM_CURSORS = 12(SDL_mouse.h L60)
    static IntPtr _default;

    internal static void Apply(CursorShape shape)
    {
        int id = (int)shape;
        IntPtr handle = _handles[id];
        if (handle == IntPtr.Zero)
        {
            handle = SdlEvents.CreateSystemCursor(id);
            if (handle == IntPtr.Zero) return;   // 创建失败(平台无该形/未初始化):保持当前游标
            _handles[id] = handle;
        }
        SdlEvents.SetCursor(handle);
    }

    internal static void ApplyDefault()
    {
        if (_default == IntPtr.Zero)
        {
            _default = SdlEvents.GetDefaultCursor();
            if (_default == IntPtr.Zero) return;
        }
        SdlEvents.SetCursor(_default);
    }

    internal static IntPtr HandleForTest(CursorShape shape) => _handles[(int)shape];
}
