using System.Diagnostics;

namespace SharpUI;

/// <summary>窗口 flags(T1 已按 SDL_video.h SDL_WindowFlags L104-127 实数核;计划稿 "Shown=0x10、
/// Hidden=0x20" 系记忆值已校正 —— 0x10 实为 BORDERLESS、0x20 实为 RESIZABLE)。</summary>
[Flags]
public enum SdlWindowFlags
{
    None = 0,
    Fullscreen = 0x1,        // SDL_WINDOW_FULLSCREEN(L104)
    FullscreenDesktop = 0x1001,  // SDL_WINDOW_FULLSCREEN_DESKTOP(L115;0x1|0x1000)
    OpenGL = 0x2,            // SDL_WINDOW_OPENGL(L105)
    Shown = 0x4,             // SDL_WINDOW_SHOWN(L106)
    Hidden = 0x8,            // SDL_WINDOW_HIDDEN(L107)
    Resizable = 0x20,        // SDL_WINDOW_RESIZABLE(L109)
    AllowHighDpi = 0x2000,   // SDL_WINDOW_ALLOW_HIGHDPI(L117;波 3A/B6:建窗时传入,见 App.CreateWindow 注)
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

    // 事件累积双缓冲(见类注分配纪律)。波 3A 增全按钮通道(含 clicks,B2/B10)与全键记录通道
    // (Alt/Gui/IsRepeat 透传,B11/B12):与既有元组通道并行双写 —— 既有元组通道保留为零形变兼容面
    // (SdlWindowTests/InputMapperTests 直接断言其形状),新通道为消费面输入。
    List<(MouseButton Button, bool Down)> _mouseEvents = new(8), _mouseEventsReleased = new(8);
    List<MouseButtonEventData> _buttonEvents = new(8), _buttonEventsReleased = new(8);
    List<(Key Key, bool Down, ModifierKeys Modifiers)> _keyEvents = new(8), _keyEventsReleased = new(8);
    List<KeyEventData> _keyEventsFull = new(8), _keyEventsFullReleased = new(8);
    List<char> _keyChars = new(4), _keyCharsReleased = new(4);
    PointF _mousePosition;
    ulong _mouseDownMask;
    float _wheelDelta;

    // 审计修复波 2A-4(A4):IME 组合串(preedit)状态 —— 持久语义(同 _mousePosition,跨帧保留):
    // SDL 每个 TEXTEDITING 事件携带**当前完整组合串**(逐事件整串替换,非增量追加),组合取消/提交后
    // 平台投递空串事件清位;组合停顿的无事件帧保持既有值(未来内联渲染消费方免闪断)。
    // string 不可变,快照持引用安全,GetSnapshotAndReset 无需为其轮换/清空。
    string _preedit = "";
    int _preeditStart, _preeditLength;

    bool _closing;   // Close() 请求(App 移除路径随后 Dispose)
    bool _closed;    // QUIT/窗口 Close 事件(MarkClosed)或已 Dispose

    // ---- 审计修复波 2B(A8):UI 线程亲和契约 ----
    // SDL 事件队列线程亲和(macOS 严格主线程;Windows/Linux 亦须与建窗线程一致 poll)+ 全部静态簿记
    // (SdlPump._windows/PopupHost._stacks/FocusManager._byWindow/App.Windows 均普通容器)依赖单线程
    // 约定(类注"线程模型"原为纯注释,零强制)。首个 SdlWindow 构造线程即 UI 线程;Debug.Assert 于
    // SdlWindow 构造/SdlPump.PumpAll/App.Run/App.OpenWindow 四入口 —— Release 编译消除零成本,Debug
    // 下偏离即 FailFast(运行时强制/抛异常为后续波次选项)。
    internal const string UiThreadAffinityMessage =
        "SharpUI: UI 操作必须在创建首个窗口的线程上执行(SDL 事件队列线程亲和,macOS 严格主线程)";

    /// <summary>UI 线程 id(A8 契约锚点):首个 SdlWindow 构造线程被记录;0 = 尚未建窗(无可违反的
    /// 亲和,断言放行)。internal 供 SdlPump/App 断言与测试文档性验证。</summary>
    internal static int UiThreadId { get; private set; }

    /// <summary>创建窗口(经 SdlEvents.CreateWindow → 活模块;视频子系统初始化幂等全候选)。失败抛
    /// InvalidOperationException(附 SDL_GetError)。窗口坐标 x/y 传 SDL_WINDOWPOS_* 常量值亦可
    /// (SDL_video.h L801-802:0x1FFF0000 系列,直接透传)。</summary>
    public SdlWindow(string title, int x, int y, int width, int height, SdlWindowFlags flags)
    {
        if (UiThreadId == 0) UiThreadId = Environment.CurrentManagedThreadId;   // A8:首个建窗线程 = UI 线程
        Debug.Assert(Environment.CurrentManagedThreadId == UiThreadId, UiThreadAffinityMessage);
        Backends.SdlNative.EnsureVideoInit();
        CreationFlags = flags;
        _handle = SdlEvents.CreateWindow(title, x, y, width, height, (uint)flags);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException($"SDL_CreateWindow 失败:{SdlEvents.GetError()}");
        WindowId = SdlEvents.GetWindowId(_handle);
        _scaleFactor = ComputeScaleFactor();
        SdlPump.Register(this);   // 构造末行:防未来可抛逻辑留下"已注册幻影窗"(当前不可达,纯防御)
    }

    /// <summary>建窗时传入的 flags(B6 部分测试消费:App.CreateWindow 的 AllowHighDpi 平台策略可断言)。</summary>
    internal SdlWindowFlags CreationFlags { get; }

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

    /// <summary>窗口客户区像素尺寸(SDL_GetWindowSizeInPixels,SDL_video.h L1068,2.26.0+;波 3A/B6)。
    /// 与 Width/Height(逻辑/屏幕坐标)的关系:SdlNative.GetDrawableSize 注 —— Windows 两值恒等(SDL
    /// Windows 坐标即物理像素);macOS Retina/Wayland 建窗传 AllowHighDpi 时像素 &gt; 逻辑。运行时 DPI
    /// 变更重算(DISPLAY_CHANGED 监听)留 M3e(B6 后半),ScaleFactor 定格语义本波不变。</summary>
    public (int Width, int Height) SizeInPixels
    {
        get
        {
            if (_handle == IntPtr.Zero) return (0, 0);
            SdlEvents.GetWindowSizeInPixels(_handle, out int w, out int h);
            return (w, h);
        }
    }

    // ---- 窗口管理方法(波 3A/B3):SDL_SetWindowTitle/SetWindowPosition/SetWindowSize/
    // SetWindowMinimumSize/SetWindowMaximumSize/MaximizeWindow/MinimizeWindow/RestoreWindow/
    // SetWindowFullscreen/SetWindowBordered/RaiseWindow/SetWindowInputFocus —— 全部经 SdlEvents
    // 绑定(OwnerModule 纪律,符号与行号出处见各绑定注)。已 Dispose(句柄 Zero)静默返回零形变。

    /// <summary>设置窗口标题(UTF-8 封送,SDL_SetWindowTitle L866)。</summary>
    public void SetTitle(string title)
    {
        if (_handle != IntPtr.Zero) SdlEvents.SetWindowTitle(_handle, title);
    }

    /// <summary>读窗口标题(SDL_GetWindowTitle L880;已 Dispose 返回空串)。</summary>
    public string Title => _handle == IntPtr.Zero ? "" : SdlEvents.GetWindowTitle(_handle);

    /// <summary>设置客户区左上坐标(SDL_SetWindowPosition L940;SDL_WINDOWPOS_* 值可透传)。</summary>
    public void SetPosition(int x, int y)
    {
        if (_handle != IntPtr.Zero) SdlEvents.SetWindowPosition(_handle, x, y);
    }

    /// <summary>设置客户区尺寸(SDL_SetWindowSize L984;平台最小窗宽钳制与 resize 回读语义同 Width/Height)。</summary>
    public void SetSize(int width, int height)
    {
        if (_handle != IntPtr.Zero) SdlEvents.SetWindowSize(_handle, width, height);
    }

    /// <summary>设置最小客户区尺寸(SDL_SetWindowMinimumSize L1083)。</summary>
    public void SetMinimumSize(int width, int height)
    {
        if (_handle != IntPtr.Zero) SdlEvents.SetWindowMinimumSize(_handle, width, height);
    }

    /// <summary>设置最大客户区尺寸(SDL_SetWindowMaximumSize L1115)。</summary>
    public void SetMaximumSize(int width, int height)
    {
        if (_handle != IntPtr.Zero) SdlEvents.SetWindowMaximumSize(_handle, width, height);
    }

    /// <summary>最大化(SDL_MaximizeWindow L1232)。随后帧首 WINDOWEVENT MAXIMIZED/RESTORED 走常规
    /// 事件通道(SIZE_CHANGED 触发 Resized,尺寸守卫过滤 0)。</summary>
    public void Maximize()
    {
        if (_handle != IntPtr.Zero) SdlEvents.MaximizeWindow(_handle);
    }

    /// <summary>最小化(SDL_MinimizeWindow L1244)。</summary>
    public void Minimize()
    {
        if (_handle != IntPtr.Zero) SdlEvents.MinimizeWindow(_handle);
    }

    /// <summary>还原常规尺寸/位置(SDL_RestoreWindow L1256)。</summary>
    public void Restore()
    {
        if (_handle != IntPtr.Zero) SdlEvents.RestoreWindow(_handle);
    }

    /// <summary>切换全屏(SDL_SetWindowFullscreen L1275):desktop=true 用 SDL_WINDOW_FULLSCREEN_DESKTOP
    /// (0x1001,合成的桌面分辨率全屏,false 用独占 0x1)。返回 SDL 语义成败(部分 WM 拒绝,调用方裁量;
    /// 失败窗口状态不变)。</summary>
    public bool SetFullscreen(bool enable, bool desktop = true)
    {
        if (_handle == IntPtr.Zero) return false;
        return SdlEvents.SetWindowFullscreen(_handle,
            enable ? (desktop ? (uint)SdlWindowFlags.FullscreenDesktop : (uint)SdlWindowFlags.Fullscreen) : 0u);
    }

    /// <summary>切换系统边框(SDL_SetWindowBordered L1151;false=无边框)。</summary>
    public void SetBordered(bool bordered)
    {
        if (_handle != IntPtr.Zero) SdlEvents.SetWindowBordered(_handle, bordered);
    }

    /// <summary>置顶并激活(SDL_RaiseWindow L1220)。</summary>
    public void Raise()
    {
        if (_handle != IntPtr.Zero) SdlEvents.RaiseWindow(_handle);
    }

    /// <summary>请求输入焦点(SDL_SetWindowInputFocus L1644;返回 SDL 语义成败 —— 部分 WM 拒绝跨窗
    /// 抢焦点,调用方裁量;失败无副作用)。</summary>
    public bool FocusInput()
    {
        if (_handle == IntPtr.Zero) return false;
        return SdlEvents.SetWindowInputFocus(_handle);
    }

    /// <summary>窗口尺寸变化(SDL_WINDOWEVENT RESIZED/SIZE_CHANGED 且尺寸&gt;0 —— 0 尺寸=最小化边缘,
    /// App 侧换算会除零/后端 ValidateSize 对 0 抛,故在源头上防御)。回调在 PumpAll 调用线程同步执行。</summary>
    public event Action? Resized;

    /// <summary>输入焦点丢失(SDL_WINDOWEVENT FOCUS_LOST;D15 失焦取消按压的触发源)。</summary>
    public event Action? FocusLost;

    /// <summary>输入焦点获得(SDL_WINDOWEVENT FOCUS_GAINED,波 3A/B4 透出;泵同时维护最近聚焦簿记)。</summary>
    public event Action? FocusGained;

    /// <summary>窗口移动(SDL_WINDOWEVENT MOVED,波 3A/B4;data1/data2 = 新客户区左上坐标,
    /// SDL_video.h L161)。跨显示器 DPI 重算消费(DISPLAY_CHANGED)留 M3e。</summary>
    public event Action<int, int>? Moved;

    /// <summary>文件拖放(SDL_DROPFILE,波 3A/B5):载荷为 UTF-8 解码后的路径(SDL 侧缓冲已由泵
    /// SDL_free,事件对象生命周期与 string 无关)。windowID=0 的全局拖放由泵广播至主窗(见 SdlPump 注)。</summary>
    public event Action<string>? FileDropped;

    /// <summary>文本拖放(SDL_DROPTEXT,波 3A/B5):载荷语义同 FileDropped(text/plain UTF-8)。</summary>
    public event Action<string>? TextDropped;

    /// <summary>拖放开始(SDL_DROPBEGIN,波 3A/B5;无载荷 —— SDL_events.h L559 file 恒 NULL)。</summary>
    public event Action? DropBegan;

    /// <summary>拖放结束(SDL_DROPCOMPLETE,波 3A/B5;无载荷)。</summary>
    public event Action? DropCompleted;

    /// <summary>窗口关闭可否决(审计修复波 2B/A3):窗口 CLOSE 事件与 SDL_QUIT 分发时逐订阅者调用,
    /// 任一返回 false 即否决该窗本次关闭(窗口存活,Exists 不变);无订阅者或全部返回 true → 放行
    /// MarkClosed。编辑器"未保存更改"场景拦截点(回调内可弹对话框/置延迟关闭标记,窗口保持存活);
    /// 回调在泵调用线程同步执行。Close() 主动关闭不经此点(应用自请关闭即已知意图);AppWindow.Closing
    /// 透传本事件(Window 为 internal,应用侧唯一入口)。</summary>
    public event Func<bool>? Closing;

    /// <summary>关闭放行判定(A3):无订阅者 → 放行;逐订阅者调用,任一返回 false → 否决。</summary>
    internal bool AllowClose()
    {
        Func<bool>? closing = Closing;
        if (closing is null) return true;
        foreach (Func<bool> handler in closing.GetInvocationList())
            if (!handler())
                return false;
        return true;
    }

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
            _mouseEvents, _keyEvents, _keyChars, _preedit, _preeditStart, _preeditLength,
            _buttonEvents, _keyEventsFull);
        // 已释放侧(上一快照独占,按时效契约此刻已可回收)清空并转正为新的累积侧;
        // 刚被快照包走的旧累积侧转为已释放侧,待下次取时回收 —— 每列表每两帧一次 Clear,容量保留。
        _mouseEventsReleased.Clear();
        _buttonEventsReleased.Clear();
        _keyEventsReleased.Clear();
        _keyEventsFullReleased.Clear();
        _keyCharsReleased.Clear();
        (_mouseEvents, _mouseEventsReleased) = (_mouseEventsReleased, _mouseEvents);
        (_buttonEvents, _buttonEventsReleased) = (_buttonEventsReleased, _buttonEvents);
        (_keyEvents, _keyEventsReleased) = (_keyEventsReleased, _keyEvents);
        (_keyEventsFull, _keyEventsFullReleased) = (_keyEventsFullReleased, _keyEventsFull);
        (_keyChars, _keyCharsReleased) = (_keyCharsReleased, _keyChars);
        _wheelDelta = 0f;   // MousePosition/_mouseDownMask/preedit 跨帧保留(状态语义:Clear 不复位)
        return snapshot;
    }

    /// <summary>泵/QUIT/窗口 Close 事件置逻辑关闭(Exists→false);原生销毁仍由 Dispose(App 移除路径)。</summary>
    internal void MarkClosed() => _closed = true;

    /// <summary>合成事件注入(测试):与 SdlPump.PumpAll 同一分发入口 Dispatch,路由逻辑单一来源。</summary>
    internal void InjectTest(in SdlEvents.SdlEvent e) => SdlPump.Dispatch(in e);

    // ---- 泵回调(经 SdlPump.Dispatch;简单标志事件,回调内异常由泵调用方(App 帧)承接)----

    internal void RaiseResized() => Resized?.Invoke();
    internal void RaiseFocusLost() => FocusLost?.Invoke();
    internal void RaiseFocusGained() => FocusGained?.Invoke();
    internal void RaiseMoved(int x, int y) => Moved?.Invoke(x, y);
    internal void RaiseFileDropped(string payload) => FileDropped?.Invoke(payload);
    internal void RaiseTextDropped(string payload) => TextDropped?.Invoke(payload);
    internal void RaiseDropBegan() => DropBegan?.Invoke();
    internal void RaiseDropCompleted() => DropCompleted?.Invoke();

    internal void UpdateMousePosition(int x, int y) => _mousePosition = new PointF(x, y);

    /// <summary>鼠标按钮事件入累积缓冲(波 3A/B2/B10 增 clicks):双通道并行双写 —— 元组通道为
    /// 零形变兼容面,全按钮记录通道(MouseButtonEventData,含 clicks)为新消费面。</summary>
    internal void AddMouseEvent(MouseButton button, bool down, int clicks)
    {
        _mouseEvents.Add((button, down));
        _buttonEvents.Add(new MouseButtonEventData(button, down, clicks));
        if (down) _mouseDownMask |= 1ul << (int)button;
        else _mouseDownMask &= ~(1ul << (int)button);
    }

    /// <summary>键事件入累积缓冲(波 3A/B11/B12 增 Alt/Gui/IsRepeat):双通道并行双写 —— 元组通道
    /// 零形变兼容,KeyEventData 记录通道为 InputMapper.MapKeys 消费面(修饰位一次解析双份复用)。</summary>
    internal void AddKeyEvent(Key key, bool down, ModifierKeys modifiers, bool isRepeat)
    {
        _keyEvents.Add((key, down, modifiers));
        _keyEventsFull.Add(new KeyEventData(key, down,
            modifiers.HasFlag(ModifierKeys.Shift), modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Alt), modifiers.HasFlag(ModifierKeys.Gui), isRepeat));
    }

    internal void AddWheel(float delta) => _wheelDelta += delta;

    internal void AppendTextChars(in SdlEvents.SdlEvent e) => SdlEvents.AppendTextChars(in e, _keyChars);

    /// <summary>IME 组合串更新(SDL_TEXTEDITING;审计修复波 2A-4/A4):SDL 每事件携带当前完整组合串,
    /// 整串替换(空串 = 清位)。快照经 GetSnapshotAndReset 透出 Preedit/PreeditStart/PreeditLength。</summary>
    internal void SetPreedit(string text, int start, int length)
    {
        _preedit = text ?? "";
        _preeditStart = start;
        _preeditLength = length;
    }

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
/// 线程模型:UI 单线程(App.Run 帧)调用,不加锁(与 SDL 事件队列的线程约定一致;审计修复波 2B/A8
/// 起 PumpAll 入口 Debug.Assert 强制 —— UI 线程 = 首个 SdlWindow 构造线程,见 SdlWindow.UiThreadId)。</summary>
internal static class SdlPump
{
    static readonly Dictionary<uint, SdlWindow> _windows = new();
    static SdlWindow? _lastFocused;   // 无归属鼠标事件的默认传播目标(见 Dispatch 注)
    static SdlWindow? _primary;       // 主窗近似 = 首个注册且仍注册的窗(App.Create 主窗先于 OpenWindow;
                                      // 波 3A/B5:DROP 事件 windowID=0(全局拖放)或无归属时的广播目标;
                                      // 注销后回退到余窗首项,保持广播面不断链)

    /// <summary>每帧一次:排空 SDL 事件队列并按 WindowId 分发到注册窗口。</summary>
    public static void PumpAll()
    {
        Debug.Assert(SdlWindow.UiThreadId == 0 || Environment.CurrentManagedThreadId == SdlWindow.UiThreadId,
            SdlWindow.UiThreadAffinityMessage);   // A8:事件 poll 线程亲和(macOS 严格主线程)
        while (SdlEvents.PollEvent(out SdlEvents.SdlEvent e))
            Dispatch(in e);
    }

    internal static void Register(SdlWindow w)
    {
        _windows[w.WindowId] = w;
        _lastFocused ??= w;   // 首窗默认聚焦近似(未有 FOCUS_GAINED 前的传播兜底)
        _primary ??= w;       // 首注册窗 = 主窗近似(B5 广播目标)
    }

    internal static void Unregister(SdlWindow w)
    {
        _windows.Remove(w.WindowId);
        if (_lastFocused == w) _lastFocused = null;
        if (_primary == w)
            _primary = _windows.Values.Count > 0 ? System.Linq.Enumerable.First(_windows.Values) : null;
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
                // 审计修复波 2B(A3):QUIT 逐窗经 Closing 可否决 —— 部分允许时只对允许窗 MarkClosed
                // (否决窗存活;主窗否决则 App.Run 循环不退出),全部允许即广播语义与修复前一致。
                foreach (SdlWindow w in _windows.Values)   // AllowClose/MarkClosed 不改字典,安全直接枚举
                    if (w.AllowClose())
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
                        SdlEvents.ModifiersFromMod(e.KeysymMod),
                        // 波 3A/B12(A7):repeat 标记透出 —— SDL 长按重复只发 KEYDOWN(e.repeat=1,
                        // SDL_events.h SDL_KeyboardEvent.repeat);KeyUp 恒不标。文本路径受益、
                        // 激活/Tab 跳过策略由 FocusManager 消费侧裁量(标记本波只透出)。
                        isRepeat: e.Type == SdlEvents.EventTypeKeyDown && e.KeyRepeat != 0);
                return;
            case SdlEvents.EventTypeTextInput:
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? textWin))
                    textWin.AppendTextChars(in e);
                return;
            case SdlEvents.EventTypeTextEditing:
                // 审计修复波 2A-4(A4):SDL_TEXTEDITING(IME 组合串)不再落入泵尾静默丢弃 —— 解码整串
                // + start/length 透传窗口累积(preedit 状态,快照经 InputSnapshot.Preedit* 透出)。
                // 键盘/文本不传播(输入语义归属精确,无归属即丢弃,同 TextInput)。
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? editWin))
                    editWin.SetPreedit(SdlEvents.DecodeUtf8Text(in e), e.TextEditingStart, e.TextEditingLength);
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
                        e.MouseButtonState == SdlEvents.Pressed,
                        e.MouseClicks);   // 波 3A/B2/B10:clicks@18(SDL 双击计数,SDL_events.h L321)透出
                return;
            case SdlEvents.EventTypeMouseWheel:
                // 波 3A/A6 滚轮三修复:
                // ① 精确增量(2.0.18+,SDL_events.h L321-322):preciseY 非零即用(触控板小数增量不截断);
                //    为 0(旧模块字段不存在 —— SDL 后端事件先 SDL_zero 再填充;或 0 增量事件)回落整型 y。
                //    (SDL ≥2.0.18 对真实事件恒同步填 x/y 与 preciseX/Y,preciseY=0 ⟺ y=0,回落无损。)
                // ② FLIPPED 归一(L320:FLIPPED 时值已反向,"Multiply by -1 to change them back")——
                //    保持"上=+1"语义(macOS 自然滚动不再反向;取代 T2"基线保留忽略方向位"的过渡裁定)。
                // ③ 自带鼠标位置(2.26.0+,L323-324):免 motion 滞后命中;0/0 视为未携带不清位
                //    (SDL <2.26 该字段不存在,恒 0;真实原点滚轮的极端角注保留上一位置,见测试注)。
                if (_windows.TryGetValue(e.WindowId, out SdlWindow? wheelWin))
                {
                    float dy = e.WheelPreciseY != 0f ? e.WheelPreciseY : e.WheelY;
                    if (e.WheelDirection == SdlEvents.WheelFlipped) dy = -dy;
                    wheelWin.AddWheel(dy);
                    if (e.WheelMouseX != 0 || e.WheelMouseY != 0)
                        wheelWin.UpdateMousePosition(e.WheelMouseX, e.WheelMouseY);
                }
                return;
            case SdlEvents.EventTypeDropFile:
            case SdlEvents.EventTypeDropText:
            {
                // 波 3A/B5:文件/文本拖放通道。解码 + SDL_free 必须无条件执行(SDL_events.h L559 契约,
                // 无论有无订阅窗 —— 释放不依赖消费),payload null = file NULL(防御,SDL 对 FILE/TEXT 恒非 NULL)。
                // 归属:windowID 命中注册窗即该窗;0(全局拖放)或无归属(注销窗尾部事件)广播至主窗近似
                // (_primary,首个注册窗 —— App 主窗恒先建)。
                string? payload = SdlEvents.DecodeDropPayload(in e);
                if (payload is null) return;
                SdlWindow? dropWin = _windows.TryGetValue(e.DropWindowId, out SdlWindow? hit) ? hit : _primary;
                if (dropWin is null) return;
                if (e.Type == SdlEvents.EventTypeDropFile) dropWin.RaiseFileDropped(payload);
                else dropWin.RaiseTextDropped(payload);
                return;
            }
            case SdlEvents.EventTypeDropBegin:
            case SdlEvents.EventTypeDropComplete:
            {
                // 波 3A/B5:拖放生命周期(无载荷;归属同 FILE/TEXT)。
                SdlWindow? dropWin = _windows.TryGetValue(e.DropWindowId, out SdlWindow? hit) ? hit : _primary;
                if (dropWin is null) return;
                if (e.Type == SdlEvents.EventTypeDropBegin) dropWin.RaiseDropBegan();
                else dropWin.RaiseDropCompleted();
                return;
            }
        }
        // 其余事件类型(游戏手柄等)T1 声明面未覆盖或不消费,安全丢弃(排空语义)。
        // 注:SDL_TEXTEDITING 自审计修复波 2A-4(A4)起经上方 case 消费;TEXTEDITING_EXT(0x305,
        // >32B 组合串)仍未声明,留后续波次(与 A4 修法建议同记);SDL_DROPFILE/TEXT/BEGIN/COMPLETE
        // 自波 3A(B5)起经上方 case 消费(DROP 事件默认即投递,SDL_events.c L642-646 实证,无需 EventState)。
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
                w.RaiseFocusGained();   // 波 3A/B4:聚焦获得透出(簿记行为不变)
                return;
            case SdlEvents.WindowEventFocusLost:
                if (_lastFocused == w) _lastFocused = null;
                w.RaiseFocusLost();
                return;
            case SdlEvents.WindowEventMoved:   // 波 3A/B4:窗口移动透出(data1/data2=新坐标,SDL_video.h L161)
                w.RaiseMoved(e.WindowData1, e.WindowData2);
                return;
            case SdlEvents.WindowEventClose:
                if (w.AllowClose())   // 审计修复波 2B(A3):Closing 订阅者可否决(任一 false → 窗口存活);修复前无条件 MarkClosed
                    w.MarkClosed();   // 旧窗口层此处调 Close()(即销毁);本层仅置标记,销毁归 App(见 SdlWindow.Close 注)
                return;
        }
        // 其余窗口子事件(SHOWN/HIDDEN/EXPOSED/MINIMIZED/...)不消费,安全丢弃。
        // DISPLAY_CHANGED(18,2.24+:跨显示器移动/DPI 变更)声明面就绪(SdlEvents 注),消费留 M3e
        // (B6 后半:ScaleFactor 运行时重算 + 图集重建一体,本波不动创建定格语义)。
    }
}
