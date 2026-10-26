namespace SharpUI;

/// <summary>窗口 flags(T1 已按 SDL_video.h SDL_WindowFlags L104-127 实数核;计划稿 "Shown=0x10、
/// Hidden=0x20" 系记忆值已校正 —— 0x10 实为 BORDERLESS、0x20 实为 RESIZABLE)。仅声明消费面所需五位。</summary>
[Flags]
public enum SdlWindowFlags
{
    None = 0,
    Fullscreen = 0x1,   // SDL_WINDOW_FULLSCREEN(L104)
    OpenGL = 0x2,       // SDL_WINDOW_OPENGL(L105)
    Shown = 0x4,        // SDL_WINDOW_SHOWN(L106)
    Hidden = 0x8,       // SDL_WINDOW_HIDDEN(L107)
    Resizable = 0x20,   // SDL_WINDOW_RESIZABLE(L109)
}

/// <summary>自研 SDL 窗口(T2,W-3):替代 旧窗口封装包.Sdl2Window 的 App/AppWindow 消费面(同名成员:
/// SdlWindowHandle/Exists/Width/Height/ScaleFactor/Resized/FocusLost/Close/Dispose;T3 迁移接线)。
/// 输入不走本类方法泵(旧窗口层每窗自泵),而由全局单泵 SdlPump.PumpAll 按 WindowId 路由,本类持有
/// 每窗累积缓冲,GetSnapshotAndReset 取当帧快照。语义差异(有意,报告注明):Close() 仅置 closing 标记
/// 不销毁(销毁随 App 移除路径 Dispose —— 旧窗口层 CloseCore 即时 SDL_DestroyWindow);ScaleFactor 为
/// DPI/96 创建定格(旧窗口层硬编码 One)。
/// 分配纪律:泵与 GetSnapshotAndReset 零稳态分配 —— 事件列表双份轮换(累积/已释放),取快照时快照独占
/// 累积侧、下帧累积改入对侧并在再下次取时清空复用(快照时效契约见 InputSnapshot);快照对象本身每帧
/// 新分配 1 次(旧窗口层亦每帧新引用返回,可接受;报告注明)。鼠标按键态为 ulong 位掩码(0..12 编位)。</summary>
public sealed class SdlWindow : IDisposable
{
    readonly float _scaleFactor;
    IntPtr _handle;

    // 事件累积双缓冲(见类注分配纪律)
    List<(MouseButton Button, bool Down)> _mouseEvents = new(8), _mouseEventsReleased = new(8);
    List<(Key Key, bool Down, ModifierKeys Modifiers)> _keyEvents = new(8), _keyEventsReleased = new(8);
    List<char> _keyChars = new(4), _keyCharsReleased = new(4);
    PointF _mousePosition;
    ulong _mouseDownMask;
    float _wheelDelta;

    bool _closing;   // Close() 请求(App 移除路径随后 Dispose)
    bool _closed;    // QUIT/窗口 Close 事件(MarkClosed)或已 Dispose

    /// <summary>创建窗口(经 SdlEvents.CreateWindow → 活模块;视频子系统初始化幂等全候选)。失败抛
    /// InvalidOperationException(附 SDL_GetError)。窗口坐标 x/y 传 SDL_WINDOWPOS_* 常量值亦可
    /// (SDL_video.h L801-802:0x1FFF0000 系列,直接透传)。</summary>
    public SdlWindow(string title, int x, int y, int width, int height, SdlWindowFlags flags)
    {
        Backends.SdlNative.EnsureVideoInit();
        _handle = SdlEvents.CreateWindow(title, x, y, width, height, (uint)flags);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException($"SDL_CreateWindow 失败:{SdlEvents.GetError()}");
        WindowId = SdlEvents.GetWindowId(_handle);
        SdlPump.Register(this);
        _scaleFactor = ComputeScaleFactor();
    }

    /// <summary>SDL 窗口原生句柄(D3D11 HWND 探测/GL 上下文等消费面)。</summary>
    public IntPtr SdlWindowHandle => _handle;

    /// <summary>Close() 或 QUIT/窗口 Close 事件后为 false(销毁与否无关,纯逻辑存在性)。</summary>
    public bool Exists => !_closing && !_closed;

    /// <summary>窗口宽(SDL_GetWindowSize 现取;已 Dispose 返回 0)。</summary>
    public int Width
    {
        get
        {
            if (_handle == IntPtr.Zero) return 0;
            SdlEvents.GetWindowSize(_handle, out int w, out _);
            return w;
        }
    }

    /// <summary>窗口高(同 Width)。</summary>
    public int Height
    {
        get
        {
            if (_handle == IntPtr.Zero) return 0;
            SdlEvents.GetWindowSize(_handle, out _, out int h);
            return h;
        }
    }

    /// <summary>DPI 缩放比:窗口所在显示器 hdpi/96,创建时定格缓存;失败兜底 1,并夹紧 [0.5,4]
    /// (App.CreateWindow 现状语义内聚:其 Math.Clamp 对本值幂等;本机实测 ~1.588)。旧窗口层 Sdl2Window
    /// 此属性硬编码 One,本值为行为改进非形变迁移(报告注明)。</summary>
    public float ScaleFactor => _scaleFactor;

    /// <summary>窗口尺寸变化(SDL_WINDOWEVENT RESIZED/SIZE_CHANGED 且尺寸&gt;0 —— 0 尺寸=最小化边缘,
    /// App 侧换算会除零/后端 ValidateSize 对 0 抛,故在源头上防御)。回调在 PumpAll 调用线程同步执行。</summary>
    public event Action? Resized;

    /// <summary>输入焦点丢失(SDL_WINDOWEVENT FOCUS_LOST;D15 失焦取消按压的触发源)。</summary>
    public event Action? FocusLost;

    /// <summary>请求关闭:置 closing 标记(Exists→false),原生窗口不动 —— 销毁随 App 移除路径 Dispose
    /// (与旧窗口层 Close 即销毁不同:所有权归 App 生命周期,避免泵回调中销毁的重入风险)。</summary>
    public void Close() => _closing = true;

    /// <summary>销毁原生窗口(SDL_DestroyWindow 经活模块;创建落点=LiveModule=属主,见
    /// SdlEvents.CreateWindow 注)并从全局泵注销。幂等。</summary>
    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        SdlPump.Unregister(this);
        SdlEvents.DestroyWindow(_handle);
        _handle = IntPtr.Zero;
        _closed = true;
    }

    /// <summary>窗口属主 id(SDL_GetWindowID;泵按此路由事件)。</summary>
    internal uint WindowId { get; }

    /// <summary>取全局泵累积的当帧快照并复位累积缓冲(空窗帧亦返回空快照)。列表为轮换复用实例:
    /// 返回快照独占当前累积侧,复位后下帧累积入对侧,本快照内容至该窗下一次 GetSnapshotAndReset 前不变
    /// (InputSnapshot 时效契约)。稳态零分配(仅快照对象 1 次/帧)。</summary>
    internal InputSnapshot GetSnapshotAndReset()
    {
        InputSnapshot snapshot = new(_mousePosition, _mouseDownMask, _wheelDelta,
            _mouseEvents, _keyEvents, _keyChars);
        // 已释放侧(上一快照独占,按时效契约此刻已可回收)清空并转正为新的累积侧;
        // 刚被快照包走的旧累积侧转为已释放侧,待下次取时回收 —— 每列表每两帧一次 Clear,容量保留。
        _mouseEventsReleased.Clear();
        _keyEventsReleased.Clear();
        _keyCharsReleased.Clear();
        (_mouseEvents, _mouseEventsReleased) = (_mouseEventsReleased, _mouseEvents);
        (_keyEvents, _keyEventsReleased) = (_keyEventsReleased, _keyEvents);
        (_keyChars, _keyCharsReleased) = (_keyCharsReleased, _keyChars);
        _wheelDelta = 0f;   // MousePosition/_mouseDownMask 跨帧保留(旧窗口层同语义:Clear 不复位二者)
        return snapshot;
    }

    /// <summary>泵/QUIT/窗口 Close 事件置逻辑关闭(Exists→false);原生销毁仍由 Dispose(App 移除路径)。</summary>
    internal void MarkClosed() => _closed = true;

    /// <summary>合成事件注入(测试):与 SdlPump.PumpAll 同一分发入口 Dispatch,路由逻辑单一来源。</summary>
    internal void InjectTest(in SdlEvents.SdlEvent e) => SdlPump.Dispatch(in e);

    // ---- 泵回调(经 SdlPump.Dispatch;简单标志事件,回调内异常由泵调用方(App 帧)承接)----

    internal void RaiseResized() => Resized?.Invoke();
    internal void RaiseFocusLost() => FocusLost?.Invoke();

    internal void UpdateMousePosition(int x, int y) => _mousePosition = new PointF(x, y);

    internal void AddMouseEvent(MouseButton button, bool down)
    {
        _mouseEvents.Add((button, down));
        if (down) _mouseDownMask |= 1ul << (int)button;
        else _mouseDownMask &= ~(1ul << (int)button);
    }

    internal void AddKeyEvent(Key key, bool down, ModifierKeys modifiers) => _keyEvents.Add((key, down, modifiers));

    internal void AddWheel(float delta) => _wheelDelta += delta;

    internal void AppendTextChars(in SdlEvents.SdlEvent e) => SdlEvents.AppendTextChars(in e, _keyChars);

    float ComputeScaleFactor()
    {
        int display = SdlEvents.GetWindowDisplayIndex(_handle);
        if (display >= 0 && SdlEvents.GetDisplayDpi(display, out _, out float hdpi, out _) && hdpi > 0f)
            return Math.Clamp(hdpi / 96f, 0.5f, 4f);
        return 1f;
    }
}

/// <summary>全局单泵(T2,W-3 裁定:一进程一泵,按 WindowId 路由到注册窗口,替代 旧窗口层每窗自泵 +
/// Sdl2Events 全局广播的混合模式)。每帧一次 PumpAll:循环 PollEvent 排空活模块队列(队列非空即续,
/// 事件吞吐由队列自限)—— 未注册 WindowId 的事件(前序测试窗口残留/已注销窗口尾部事件)安全丢弃,
/// 等价于 T1 冒烟实证的"泵前遗留对本泵可见且必须排空"约束;仅无归属 MOUSEMOTION 默认传播(见
/// Dispatch 注)。QUIT → 全部注册窗口 MarkClosed(进程级退出语义,旧窗口层逐窗 Close 的全局化)。
/// 线程模型:UI 单线程(App.Run 帧)调用,不加锁(与 SDL 事件队列的线程约定一致)。</summary>
internal static class SdlPump
{
    static readonly Dictionary<uint, SdlWindow> _windows = new();
    static SdlWindow? _lastFocused;   // 无归属鼠标事件的默认传播目标(见 Dispatch 注)

    /// <summary>每帧一次:排空 SDL 事件队列并按 WindowId 分发到注册窗口。</summary>
    public static void PumpAll()
    {
        while (SdlEvents.PollEvent(out SdlEvents.SdlEvent e))
            Dispatch(in e);
    }

    internal static void Register(SdlWindow w)
    {
        _windows[w.WindowId] = w;
        _lastFocused ??= w;   // 首窗默认聚焦近似(未有 FOCUS_GAINED 前的传播兜底)
    }

    internal static void Unregister(SdlWindow w)
    {
        _windows.Remove(w.WindowId);
        if (_lastFocused == w) _lastFocused = null;
    }

    /// <summary>单事件分发(PumpAll 与 SdlWindow.InjectTest 共用入口)。
    /// 无归属鼠标事件传播策略(简报裁定的务实近似,与旧窗口层行为对齐性论证):
    /// ① 旧窗口层基线:每窗自泵时以 windowID==self 过滤,无归属事件对该窗不可见;但其 Sdl2Events
    ///   全局泵向所有订阅窗广播后各窗再过滤 —— 归属判定同为"事件自带 windowID"。
    /// ② 本泵将无归属 MOUSEMOTION 传播给最近聚焦窗:SDL 的 motion 必带光标下窗口 id,无归属仅出现于
    ///   (a) 已注销/已销毁窗口的队列尾部事件(关闭瞬间)(b) 混宿进程里他方(旧窗口层遗留/T3 前测试)窗口
    ///   (c) 焦点簿记空窗。三态下丢弃会使最近聚焦窗鼠标位置冻结(悬浮/拖拽断流),传播给"最近聚焦窗"
    ///   在 (a)(c) 是正确归属,(b) 下目标窗未聚焦不消费点击(按键/按钮事件仍严格按 id 路由、不传播),
    ///   误漂移仅一处只读位置 —— 收益/风险比如此;多窗精确归属留 M3e 后半(SDK 侧 FOCUS/MOUSE_FOCUS
    ///   flags 查询可判真实光标窗)。
    /// 键盘/文本/按钮/滚轮不传播:输入语义必须归属精确,无归属即丢弃。</summary>
    internal static void Dispatch(in SdlEvents.SdlEvent e)
    {
        switch (e.Type)
        {
            case SdlEvents.EventTypeQuit:
                foreach (SdlWindow w in _windows.Values)   // MarkClosed 不改字典,安全直接枚举
                    w.MarkClosed();
                return;
            case SdlEvents.EventTypeWindow:
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? win))
                    DispatchWindowEvent(win, e);
                return;
            case SdlEvents.EventTypeKeyDown:
            case SdlEvents.EventTypeKeyUp:
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? keyWin))
                    keyWin.AddKeyEvent(
                        SdlEvents.KeyFromScancode(e.KeysymScancode),   // 泵键路径=scancode(旧窗口层 MapKey 基线,携带项④)
                        e.Type == SdlEvents.EventTypeKeyDown,
                        SdlEvents.ModifiersFromMod(e.KeysymMod));
                return;
            case SdlEvents.EventTypeTextInput:
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? textWin))
                    textWin.AppendTextChars(in e);
                return;
            case SdlEvents.EventTypeMouseMotion:
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? motionWin))
                    motionWin.UpdateMousePosition(e.MouseX, e.MouseY);
                else if (_lastFocused is { } focused)   // 无归属移动:默认传播(见方法注)
                    focused.UpdateMousePosition(e.MouseX, e.MouseY);
                return;
            case SdlEvents.EventTypeMouseButtonDown:
            case SdlEvents.EventTypeMouseButtonUp:
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? buttonWin))
                    buttonWin.AddMouseEvent(
                        SdlEvents.MouseButtonFromId(e.MouseButtonId),
                        e.MouseButtonState == SdlEvents.Pressed);
                return;
            case SdlEvents.EventTypeMouseWheel:
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? wheelWin))
                    wheelWin.AddWheel(e.WheelY);   // += y,忽略 FLIPPED(旧窗口层源码实证语义)
                return;
        }
        // 其余事件类型(TEXTEDITING/拖放/游戏手柄等)T1 声明面未覆盖或不消费,安全丢弃(排空语义)。
    }

    static void DispatchWindowEvent(SdlWindow w, in SdlEvents.SdlEvent e)
    {
        switch (e.WindowEventCode)
        {
            case SdlEvents.WindowEventResized:
            case SdlEvents.WindowEventSizeChanged:   // 旧窗口层对两子事件同触发 Resized(SizeChanged 亦覆盖程序化 SetWindowSize)
                if (e.WindowData1 > 0 && e.WindowData2 > 0)
                    w.RaiseResized();               // 0 尺寸=最小化边缘,源头防御(见 SdlWindow.Resized 注)
                return;
            case SdlEvents.WindowEventFocusGained:
                _lastFocused = w;
                return;
            case SdlEvents.WindowEventFocusLost:
                if (_lastFocused == w) _lastFocused = null;
                w.RaiseFocusLost();
                return;
            case SdlEvents.WindowEventClose:
                w.MarkClosed();   // 旧窗口层此处调 Close()(即销毁);本层仅置标记,销毁归 App(见 SdlWindow.Close 注)
                return;
        }
        // 其余窗口子事件(SHOWN/HIDDEN/MOVED/EXPOSED/MINIMIZED/...)不消费,安全丢弃。
    }
}
