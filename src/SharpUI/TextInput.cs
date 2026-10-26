using System.Runtime.InteropServices;

namespace SharpUI;

/// <summary>SDL 文本输入(IME)原生桥(IME 热修):旧窗口封装包(4.9.0)未封装 Start/Stop/SetTextInputRect,
/// 自补原生调用(同 M3c 剪贴板先例)。SetTextInputRect 矩形经 imm32 驱动候选窗定位。
/// 裁定#38:DllImport 按名解析在"应用基目录 + runtimes/&lt;rid&gt;/native 双份 SDL2.dll"布局下会与
/// 旧窗口层程序集解析结果分裂成两个已加载模块(实证双 base 地址),本类若落在未初始化副本上,
/// 一切窗口相关调用失效(WMInfo 报 "Video subsystem has not been initialized")。故不用 DllImport:
/// 显式加载候选路径,以 SDL_WasInit(VIDEO) 判定窗口属主"活"模块,函数指针直调;
/// 绑定惰性于首次窗口相关调用(此时旧窗口层已建窗初始化,判定可靠)。</summary>
public static class SdlTextInput
{
    static SdlApi? _api;
    static bool _wantImeUi;

    /// <summary>放行原生 IME UI(裁定#34:SDL ≤2.0.17 无条件清零 WM_IME_SETCONTEXT 的 ISC_SHOWUI* 压制组合/候选窗;
    /// 2.0.18+ 经 hint 放行)。窗口创建前调用只记意图,hint 于活模块绑定时生效(见裁定#38)。</summary>
    public static void EnableImeUi() => _wantImeUi = true;

    /// <summary>开启窗口文本输入模式(IME 组合/候选启动门);返回值 SDL_bool,窗口无效仅 0,忽略。</summary>
    public static void Start(IntPtr sdlWindow)
    {
        SdlApi api = EnsureBound();
        _ = api.StartTextInput(sdlWindow);
    }

    /// <summary>关闭窗口文本输入模式(系统 IME 恢复普通状态);从未绑定(未 Start 过)则仅清系统 caret。</summary>
    public static void Stop(IntPtr sdlWindow)
    {
        _api?.StopTextInput(sdlWindow);
        if (OperatingSystem.IsWindows()) DestroyCaret();
    }

    /// <summary>锚定组合/候选窗矩形(调用方负责逻辑→屏幕像素换算)。
    /// 双通道(裁定#36):SDL_SetTextInputRect(官方路径)+ 直接 IMM32 ImmSetCompositionWindow/ImmSetCandidateWindow
    /// (SDL 2.30 亦不调 SetCandidateWindow,且其 Composition 落点实测不准——候选框飘到屏幕右下角即此因)。</summary>
    public static void SetRect(IntPtr sdlWindow, int x, int y, int w, int h)
    {
        if (Environment.GetEnvironmentVariable("SHARPUI_IME_TRACE") == "1")
            Trace($"SetRect win=0x{sdlWindow:X} ({x},{y},{w},{h})");
        SdlApi api = EnsureBound();
        var r = new SdlRect { X = x, Y = y, W = w, H = h };
        api.SetTextInputRect(sdlWindow, ref r);
        PositionImm(api, sdlWindow, x, y, h);
    }

    static void PositionImm(SdlApi api, IntPtr sdlWindow, int x, int y, int lineHeight)
    {
        if (!OperatingSystem.IsWindows()) return;
        bool trace = Environment.GetEnvironmentVariable("SHARPUI_IME_TRACE") == "1";
        IntPtr hwnd = HwndOf(api, sdlWindow);
        if (trace) Trace($"PositionImm enter hwnd=0x{hwnd:X}");
        if (hwnd == IntPtr.Zero) return;
        IntPtr himc = ImmGetContext(hwnd);
        if (trace) Trace($"PositionImm himc=0x{himc:X}");
        if (himc == IntPtr.Zero) return;
        try
        {
            // 测试门控:强制 IME 打开(微软拼音=中模式),自动化注入键不触发 Shift 切换时用;默认关闭
            if (Environment.GetEnvironmentVariable("SHARPUI_TEST_IME_OPEN") == "1")
                ImmSetOpenStatus(himc, true);
            if (trace) Trace($"PositionImm open={ImmGetOpenStatus(himc)}");
            var cf = new CompositionForm { Style = CfsPoint, Pos = new NativePoint { X = x, Y = y } };
            bool sc = ImmSetCompositionWindow(himc, ref cf);
            if (trace) Trace($"PositionImm SetCompositionWindow={sc}");
            var cand = new CandidateForm
            {
                Index = 0,
                Style = CfsCandidatePos,
                Pos = new NativePoint { X = x, Y = y + Math.Max(16, lineHeight) },
            };
            bool sw = ImmSetCandidateWindow(himc, ref cand);
            if (trace) Trace($"PositionImm SetCandidateWindow={sw}");
        }
        finally { ImmReleaseContext(hwnd, himc); }

        // 系统 caret(裁定#37):Win10/11 微软拼音(TSF)候选 UI 跟随系统插入符而非仅 IMM 调用;
        // 无 caret 时飘至屏幕角落(ImGui Win32 后端同款处置)。SetCaretPos 收屏幕坐标,
        // IMM 调用收客户区坐标——同一逻辑点两套坐标系,caret 须经 ClientToScreen 换算。
        var caret = new NativePoint { X = x, Y = y };
        bool c2s = ClientToScreen(hwnd, ref caret);
        if (trace) Trace($"PositionImm ClientToScreen={c2s} ({caret.X},{caret.Y})");
        CreateCaret(hwnd, IntPtr.Zero, 1, Math.Max(8, lineHeight));
        SetCaretPos(caret.X, caret.Y);
        ShowCaret(hwnd);
        if (trace) Trace($"PositionImm done");
    }

    static SdlApi EnsureBound()
    {
        if (_api is { } bound) return bound;
        string baseDir = AppContext.BaseDirectory;
        string rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };
        // 候选顺序:runtimes RID 副本(旧窗口层程序集常规解析位)优先,应用基目录副本次之;
        // 活判定 = 该模块内 SDL_WasInit(VIDEO) 非零(旧窗口层建窗后必然已在属主模块置位)。
        SdlApi? api = SdlApi.TryBind(Path.Combine(baseDir, "runtimes", rid, "native", "SDL2.dll"), requireLive: true)
            ?? SdlApi.TryBind(Path.Combine(baseDir, "SDL2.dll"), requireLive: true)
            ?? SdlApi.TryBind(Path.Combine(baseDir, "runtimes", rid, "native", "SDL2.dll"), requireLive: false)
            ?? SdlApi.TryBind(Path.Combine(baseDir, "SDL2.dll"), requireLive: false);
        if (api is null)
            throw new DllNotFoundException("SDL2.dll 未找到(基目录与 runtimes/" + rid + "/native 均无)");
        if (_wantImeUi && Environment.GetEnvironmentVariable("SHARPUI_TEST_NO_HINTS") != "1")
            api.ApplyImeUiHints();   // 裁定#34 hint 落到活模块才算数
        _api = api;
        return api;
    }

    static void Trace(string msg) =>
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "sharpui-ime-trace.log"),
            $"{DateTime.Now:HH:mm:ss.fff} [ime] {msg}\n");

    // ---- 活模块绑定层(裁定#38) ----

    /// <summary>SDL2 活模块:显式 LoadLibrary 指定路径 + GetExport 函数指针(同路径重复 Load 仅增引用计数,
    /// 不产生新模块)。requireLive 时以 SDL_WasInit(VIDEO) 校验为窗口属主,失败即卸载让位。</summary>
    sealed class SdlApi
    {
        readonly nint _mod;
        internal readonly DStartTextInput StartTextInput;
        internal readonly DStopTextInput StopTextInput;
        internal readonly DSetTextInputRect SetTextInputRect;
        readonly DSetHint _setHint;
        readonly DGetVersion _getVersion;
        readonly DGetWindowWMInfo _getWindowWMInfo;
        readonly DWasInit _wasInit;

        SdlApi(nint mod)
        {
            _mod = mod;
            StartTextInput = Bind<DStartTextInput>("SDL_StartTextInput");
            StopTextInput = Bind<DStopTextInput>("SDL_StopTextInput");
            SetTextInputRect = Bind<DSetTextInputRect>("SDL_SetTextInputRect");
            _setHint = Bind<DSetHint>("SDL_SetHint");
            _getVersion = Bind<DGetVersion>("SDL_GetVersion");
            _getWindowWMInfo = Bind<DGetWindowWMInfo>("SDL_GetWindowWMInfo");
            _wasInit = Bind<DWasInit>("SDL_WasInit");
        }

        internal static SdlApi? TryBind(string path, bool requireLive)
        {
            if (!File.Exists(path)) return null;
            nint mod;
            try { mod = NativeLibrary.Load(path); }
            catch { return null; }
            var api = new SdlApi(mod);
            uint live = api._wasInit(VideoFlag);
            if (Environment.GetEnvironmentVariable("SHARPUI_IME_TRACE") == "1")
                Trace($"TryBind {path} mod=0x{mod:X} wasInit=0x{live:X} requireLive={requireLive}");
            if (requireLive && live == 0)
            {
                NativeLibrary.Free(mod);   // 非属主副本:不留第三份模块
                return null;
            }
            return api;
        }

        T Bind<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_mod, name));

        internal void ApplyImeUiHints()
        {
            _ = _setHint("SDL_IME_SHOW_UI", "1");
            _ = _setHint("SDL_IME_SUPPORT_EXTENDED_BASICS", "1");
        }

        /// <summary>SDL 窗口句柄 → Win32 HWND(客户区坐标即 IMM 坐标系);版本字以 SDL_GetVersion 动态填充。</summary>
        internal IntPtr Win32Hwnd(IntPtr sdlWindow)
        {
            var v = new SdlVersion();
            _getVersion(ref v);
            var info = new SdlSysWmInfo { Maj = v.Major, Min = v.Minor, Pat = v.Patch };
            int r = _getWindowWMInfo(sdlWindow, ref info);
            if (Environment.GetEnvironmentVariable("SHARPUI_IME_TRACE") == "1")
                Trace($"WMInfo r={r} ver={v.Major}.{v.Minor}.{v.Patch} sub={info.Subsystem} hwnd=0x{info.Hwnd:X}");
            return r != 0 && info.Subsystem == SyswmWindows ? info.Hwnd : IntPtr.Zero;
        }
    }

    const uint VideoFlag = 0x00000020;   // SDL_INIT_VIDEO
    const int SyswmWindows = 1;

    static IntPtr HwndOf(SdlApi api, IntPtr sdlWindow) => api.Win32Hwnd(sdlWindow);

    // ---- imm32/user32 直连(系统库无多副本问题) ----
    const uint CfsPoint = 0x0002;            // CFS_POINT:组合窗锚定到指定点(IME 可微调)
    const uint CfsCandidatePos = 0x0040;     // CFS_CANDIDATE_POS:候选窗置于指定点

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hwnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetCompositionWindow(IntPtr himc, ref CompositionForm form);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetCandidateWindow(IntPtr himc, ref CandidateForm form);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr himc);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetOpenStatus(IntPtr himc, bool open);

    [DllImport("imm32.dll")]
    private static extern bool ImmGetOpenStatus(IntPtr himc);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint pt);

    [DllImport("user32.dll")]
    private static extern bool CreateCaret(IntPtr hwnd, IntPtr bitmap, int width, int height);

    [DllImport("user32.dll")]
    private static extern bool SetCaretPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool ShowCaret(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyCaret();

    // ---- SDL 封送类型与委托 ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlRect { public int X, Y, W, H; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion { public byte Major, Minor, Patch; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm { public uint Style; public NativePoint Pos; public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CandidateForm { public uint Index, Style; public NativePoint Pos; public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlSysWmInfo
    {
        public byte Maj, Min, Pat;      // SDL_version {major,minor,patch}
        public int Subsystem;           // SDL_SYSWM_TYPE
        // Windows 分支联合体首成员(Hwnd)。SDL 会写整个联合体(2.24+ win 分支 4 指针:
        // window/hdc/hglrc/helperWindow,至 offset 40),结构必须给足尺寸防原生越界写
        // 冲毁封送暂存区(实证:16 字节版在 WMInfo 成功后即刻段错误)。
        public IntPtr Hwnd, Hdc, Hglrc, HelperWindow;
        public IntPtr Pad1, Pad2, Pad3, Pad4, Pad5, Pad6, Pad7, Pad8;   // 其余平台分支余量,仅占位
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DStartTextInput(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DStopTextInput(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DSetTextInputRect(IntPtr window, ref SdlRect rect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate bool DSetHint([MarshalAs(UnmanagedType.LPStr)] string name,
        [MarshalAs(UnmanagedType.LPStr)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DGetVersion(ref SdlVersion v);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DGetWindowWMInfo(IntPtr window, ref SdlSysWmInfo info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint DWasInit(uint flags);
}

/// <summary>每窗文本输入状态机(IME 热修):Run 每帧以 (窗口句柄, 聚焦 TextField id, 光标像素矩形) 驱动。
/// 聚焦→Start 一次 + 每帧 SetRect 同步(光标移动候选窗跟随);失焦/无矩形→Stop 一次;
/// 句柄切换→先 Stop 旧窗再按新窗 Start。委托注入,测试以纯假体驱动。</summary>
public sealed class TextInputController
{
    readonly Action<IntPtr> _start;
    readonly Action<IntPtr> _stop;
    readonly Action<IntPtr, int, int, int, int> _setRect;

    IntPtr _activeWindow;   // 非 Zero = 该窗文本输入已激活(Stop 收此句柄,不收当帧窗口)
    string? _focusedId;     // 激活时的聚焦 TextField id

    public TextInputController(Action<IntPtr> start, Action<IntPtr> stop, Action<IntPtr, int, int, int, int> setRect)
    {
        _start = start;
        _stop = stop;
        _setRect = setRect;
    }

    /// <summary>激活条件:聚焦 TextField 且光标像素矩形已知,任一缺失即去激活(空帧/失焦/关窗)。</summary>
    public void Update(IntPtr window, string? focusedTextFieldId, (int X, int Y, int W, int H)? cursorRectPixels)
    {
        bool wantActive = focusedTextFieldId is not null && cursorRectPixels is not null;
        if (_activeWindow != IntPtr.Zero && (!wantActive || window != _activeWindow))
        {
            _stop(_activeWindow);
            _activeWindow = IntPtr.Zero;
            _focusedId = null;
        }
        if (!wantActive) return;
        if (_activeWindow == IntPtr.Zero)
        {
            _start(window);
            _activeWindow = window;
            _focusedId = focusedTextFieldId;
        }
        (int x, int y, int w, int h) = cursorRectPixels!.Value;
        _setRect(_activeWindow, x, y, w, h);
    }
}
