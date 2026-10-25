using System.Runtime.InteropServices;

namespace SharpUI;

/// <summary>SDL 文本输入(IME)原生桥(IME 热修):Veldrid.Sdl2 4.9 未封装 Start/Stop/SetTextInputRect,
/// 随包 native SDL2.dll 2.0.8 本身导出三者(探针已证),自补 P/Invoke(同 M3c 剪贴板先例)。
/// SetTextInputRect 矩形经 imm32 驱动候选窗定位;预载兜底同 Clipboard(测试宿主 runtimes/&lt;rid&gt;/native 布局)。</summary>
public static class SdlTextInput
{
    const string Lib = "SDL2.dll";

    static SdlTextInput()
    {
        try
        {
            if (!NativeLibrary.TryLoad(Lib, typeof(SdlTextInput).Assembly, null, out _))
            {
                string rid = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "win-x64",
                    Architecture.X86 => "win-x86",
                    Architecture.Arm64 => "win-arm64",
                    _ => ""
                };
                if (rid.Length > 0)
                    NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", Lib), out _);
            }
        }
        catch { /* 预载失败交给 DllImport 自身解析路径报错 */ }
    }

    /// <summary>开启窗口文本输入模式(IME 组合/候选启动门);返回值 SDL_bool,窗口无效仅 0,忽略。</summary>
    public static void Start(IntPtr sdlWindow) => _ = SDL_StartTextInput(sdlWindow);

    /// <summary>放行原生 IME UI(裁定#34:SDL ≤2.0.17 无条件清零 WM_IME_SETCONTEXT 的 ISC_SHOWUI* 压制组合/候选窗;
    /// 2.0.18+ 经本 hint 放行。须在窗口创建前调用;旧版 SDL 上为无害 no-op 返回 false)。</summary>
    public static void EnableImeUi()
    {
        _ = SDL_SetHint("SDL_IME_SHOW_UI", "1");
        _ = SDL_SetHint("SDL_IME_SUPPORT_EXTENDED_BASICS", "1");   // 2.0.22+:支持更多 IME 消息(无害 no-op 于旧版)
    }

    /// <summary>关闭窗口文本输入模式(系统 IME 恢复普通状态)。</summary>
    public static void Stop(IntPtr sdlWindow) => SDL_StopTextInput(sdlWindow);

    /// <summary>锚定组合/候选窗矩形(调用方负责逻辑→屏幕像素换算);驱动 imm32 ImmSetCandidateWindow。</summary>
    public static void SetRect(IntPtr sdlWindow, int x, int y, int w, int h)
    {
        SdlRect r = new() { X = x, Y = y, W = w, H = h };
        SDL_SetTextInputRect(sdlWindow, ref r);
    }

    /// <summary>SDL_Rect(顺序 int x,y,w,h);仅 SetTextInputRect 封送用。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SdlRect
    {
        public int X, Y, W, H;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_StartTextInput(IntPtr window);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_StopTextInput(IntPtr window);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_SetTextInputRect(IntPtr window, ref SdlRect rect);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPStr)] string name,
        [MarshalAs(UnmanagedType.LPStr)] string value);
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
