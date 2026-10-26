using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>SDL 原生最小面(M3d-ii,T4 冒烟实证修订;M3e T2 增 GL 面):视频子系统初始化 + 窗口 HWND + GL 六函数。
/// 裁定#38(修订):应用布局"基目录 + runtimes/win-x64/native 双份 SDL2.dll"下,本程序集裸 DllImport
/// 按名解析与 Veldrid.Sdl2 的解析会分裂为两个已加载模块(同基名不同全路径可并存,TextInput 热修已实证
/// 双 base 地址;T4 冒烟复现:Init 落模块 A 成功、窗口经 Veldrid 落模块 B,WMInfo 报 "Invalid window"/sub=0)。
/// 模块落点不可假设:T4 取证明证 Init 落基目录副本而 Veldrid 建窗落 runtimes 副本,双模块并存、属主不定
/// ——"Init 落点=Veldrid 将绑定的模块"式假设均已被证伪,不得复述。故不用裸 DllImport:
/// 1) Init:按候选序(基目录 → runtimes/&lt;rid&gt;/native → 程序集探测)显式 NativeLibrary.Load 首个命中副本,
///    SDL_Init 经该句柄函数指针执行——这只是确保"某"副本视频就绪的尽力初始化(失败值忽略,同 StartupUtilities
///    先例),其落点不保证是窗口属主模块;
/// 2) WMInfo:多候选探测(Init 句柄 → 按名解析模块 → runtimes 副本句柄),窗口属主模块应答
///    (非属主报 Invalid window),全败才抛并附各候选诊断。此探测是正确性的依赖(同 TextInput 活模块判定,
///    裁定#38 二次实证),不可"简化"为单句柄调用——去掉即复现 T4 冒烟崩溃。
/// 裁定#39:WMInfo 结构给足 SDL 2.24+ Windows 联合体尺寸(4 指针)+ 余量,防原生越界写。
/// GL 面(M3e T2)模块纪律:SDL_GL_* 窗口相关调用(CreateGlContext/MakeCurrent/SwapWindow)必须落在
/// 窗口属主模块(= WMInfo 应答模块,经 OwnerModule 探测);无窗口参的调用(SetGlAttribute 建窗前 /
/// SetSwapInterval / GetGlProcAddress 上下文级)属主未知,全候选设置/解析、任一成功即止。</summary>
internal static class SdlNative
{
    const string SdlLib = "SDL2.dll";
    const uint SdlInitVideo = 0x00000020;

    // ---- SDL_GLattr / SDL_GLprofile 实数(出处:SDL_video.h typedef enum SDL_GLattr 无显式值按声明序递增;
    // 取自 SDL release-2.0.8(= 测试宿主 nuget Veldrid.SDL2 所带 DLL 版本)与 release-2.30.6 头两处互证,全 2.x 同序) ----

    internal const int GlAttrBufferSize = 4;          // SDL_GL_BUFFER_SIZE
    internal const int GlAttrDoubleBuffer = 5;        // SDL_GL_DOUBLEBUFFER(简报原稿 14 系笔误:14=MULTISAMPLESAMPLES)
    internal const int GlAttrContextMajorVersion = 17; // SDL_GL_CONTEXT_MAJOR_VERSION
    internal const int GlAttrContextMinorVersion = 18; // SDL_GL_CONTEXT_MINOR_VERSION
    internal const int GlAttrContextFlags = 20;       // SDL_GL_CONTEXT_FLAGS
    internal const int GlAttrContextProfileMask = 21; // SDL_GL_CONTEXT_PROFILE_MASK

    // SDL_GLprofile(SDL_video.h 显式十六进制值,两版本一致)
    internal const int GlProfileCore = 0x0001;          // SDL_GL_CONTEXT_PROFILE_CORE(简报原稿 0x0002 系笔误:0x0002=COMPATIBILITY)
    internal const int GlProfileCompatibility = 0x0002; // SDL_GL_CONTEXT_PROFILE_COMPATIBILITY
    internal const int GlProfileEs = 0x0004;            // SDL_GL_CONTEXT_PROFILE_ES

    static IntPtr _primary;                  // 显式加载句柄(Init 目标;候选序首个命中)
    static IntPtr _byName;                   // 按名解析模块(NativeLibrary.Load(简单名) 与 DllImport 同解析)
    static IntPtr _runtimes;                 // runtimes/<rid>/native 副本句柄(仅探测用;加载幂等于既有模块)
    static bool _byNameProbed, _runtimesProbed, _primaryProbed;

    internal static void EnsureVideoInit()
    {
        IntPtr h = Primary();
        if (h == IntPtr.Zero)
            throw new DllNotFoundException(
                $"{SdlLib} 不可用:基目录、runtimes/<rid>/native 与程序集探测均未命中");
        _ = Fn<SdlInitFn>(h, "SDL_Init")(SdlInitVideo);   // 幂等引用计数;失败值忽略(与 Veldrid.StartupUtilities 先例一致)
    }

    internal static IntPtr GetWin32Hwnd(IntPtr sdlWindow)
    {
        foreach (IntPtr h in Candidates())
            if (TryWmInfo(h, sdlWindow, out IntPtr hwnd))
                return hwnd;
        throw OwnerFailure();
    }

    /// <summary>窗口属主 SDL 模块句柄(WMInfo 应答候选;窗口级缓存,窗口关闭不强求清理——句柄仅作字典键)。
    /// 裁定#38 纪律:任何窗口相关 SDL_GL_*/WMInfo 调用前经此定位属主,勿直接用 Primary()。</summary>
    internal static IntPtr OwnerModule(IntPtr sdlWindow)
    {
        lock (_ownerLock)
            if (_ownerByWindow.TryGetValue(sdlWindow, out IntPtr owner))
                return owner;
        foreach (IntPtr h in Candidates())
            if (TryWmInfo(h, sdlWindow, out _))
            {
                lock (_ownerLock) _ownerByWindow[sdlWindow] = h;
                return h;
            }
        throw OwnerFailure();
    }

    // ---- GL 面(M3e T2):属主模块纪律见类头 ----

    /// <summary>设置 GL 建窗/建上下文属性(必须建窗前调用)。属主未知(窗口尚不存在)→ 全候选设置,
    /// 任一副本接受即成(属主必在候选中;非属主副本视频未初始化时报 -1,忽略)。</summary>
    internal static int SetGlAttribute(int attr, int value)
    {
        int last = -1;
        foreach (IntPtr h in Candidates())
        {
            last = Fn<GlSetAttributeFn>(h, "SDL_GL_SetAttribute")(attr, value);
            if (last == 0) return 0;
        }
        return last;
    }

    /// <summary>在属主模块创建 GL 上下文(失败返回 Zero,SDL 语义)。</summary>
    internal static IntPtr CreateGlContext(IntPtr sdlWindow) =>
        Fn<GlCreateContextFn>(OwnerModule(sdlWindow), "SDL_GL_CreateContext")(sdlWindow);

    /// <summary>绑定当前线程 GL 上下文(属主模块;0=成功)。</summary>
    internal static int MakeCurrent(IntPtr sdlWindow, IntPtr ctx) =>
        Fn<GlMakeCurrentFn>(OwnerModule(sdlWindow), "SDL_GL_MakeCurrent")(sdlWindow, ctx);

    /// <summary>交换属主窗口前后缓冲(垂直同步节奏由 SetSwapInterval 控制)。</summary>
    internal static void SwapWindow(IntPtr sdlWindow) =>
        Fn<GlSwapWindowFn>(OwnerModule(sdlWindow), "SDL_GL_SwapWindow")(sdlWindow);

    /// <summary>设置交换间隔(0=立即/1=垂直同步/-1=自适应);作用域=各模块"当前上下文",
    /// 无窗口参属主未知 → 全候选,任一接受即成(无当前上下文的副本报 -1,忽略)。</summary>
    internal static int SetSwapInterval(int interval)
    {
        int last = -1;
        foreach (IntPtr h in Candidates())
        {
            last = Fn<GlSetSwapIntervalFn>(h, "SDL_GL_SetSwapInterval")(interval);
            if (last == 0) return 0;
        }
        return last;
    }

    /// <summary>解析 GL 函数指针(须已有 MakeCurrent 的上下文):有当前上下文的副本(即属主)应答,
    /// 其余副本返回 NULL;候选序逐一尝试,任一命中即返回。全败返回 Zero(调用方决定是否报缺失)。</summary>
    internal static IntPtr GetGlProcAddress(string proc)
    {
        foreach (IntPtr h in Candidates())
        {
            IntPtr p = Fn<GlGetProcAddressFn>(h, "SDL_GL_GetProcAddress")(proc);
            if (p != IntPtr.Zero) return p;
        }
        return IntPtr.Zero;
    }

    // ---- 候选探测辅助 ----

    // 候选去重后逐一探测:窗口属主模块应答 WMInfo,非属主 CHECK_WINDOW_MAGIC 失败(Invalid window)
    static bool TryWmInfo(IntPtr module, IntPtr sdlWindow, out IntPtr hwnd)
    {
        GetVersion(module, out SdlVersion v);
        var info = new SdlSysWmInfo { Maj = v.Major, Min = v.Minor, Pat = v.Patch };
        if (Fn<SdlWmInfoFn>(module, "SDL_GetWindowWMInfo")(sdlWindow, ref info) != 0
            && info.Subsystem == 1 /*SYSWM_WINDOWS*/ && info.Hwnd != IntPtr.Zero)
        {
            hwnd = info.Hwnd;
            return true;
        }
        hwnd = IntPtr.Zero;
        return false;
    }

    static InvalidOperationException OwnerFailure()
    {
        string candidates = string.Join(", ", System.Linq.Enumerable.Select(Candidates(), c => $"0x{c:X}"));
        return new InvalidOperationException(
            $"SDL_GetWindowWMInfo 失败:窗口不属于任何已加载 SDL2 模块(候选 {candidates};" +
            "双副本分裂见类注释裁定#38修订)");
    }

    static readonly Dictionary<IntPtr, IntPtr> _ownerByWindow = new();
    static readonly object _ownerLock = new();

    static IEnumerable<IntPtr> Candidates()
    {
        IntPtr p = Primary(), n = ByName(), r = Runtimes();
        if (p != IntPtr.Zero) yield return p;
        if (n != IntPtr.Zero && n != p) yield return n;
        if (r != IntPtr.Zero && r != p && r != n) yield return r;
    }

    // ---- 候选句柄(探测各一次,缓存) ----

    static IntPtr Primary()
    {
        if (_primaryProbed) return _primary;
        _primaryProbed = true;
        string rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "",
        };
        string baseDir = AppContext.BaseDirectory;
        // 候选序为本类自定(基目录 → runtimes/<rid>/native),非 Veldrid 加载序的复刻——T4 实证两者落点
        // 可分裂(Init 落基目录副本、Veldrid 建窗落 runtimes 副本);落点不可假设,属主由 GetWin32Hwnd 多候选探测兜底
        if (NativeLibrary.TryLoad(Path.Combine(baseDir, SdlLib), out _primary)) return _primary;
        if (rid.Length > 0
            && NativeLibrary.TryLoad(Path.Combine(baseDir, "runtimes", rid, "native", SdlLib), out _primary))
            return _primary;
        NativeLibrary.TryLoad(SdlLib, typeof(SdlNative).Assembly, null, out _primary);   // 程序集探测兜底(测试宿主布局)
        return _primary;
    }

    static IntPtr ByName()
    {
        if (_byNameProbed) return _byName;
        _byNameProbed = true;
        NativeLibrary.TryLoad(SdlLib, out _byName);   // 简单名 → ALC 默认解析(与 DllImport 按名同路径)
        return _byName;
    }

    static IntPtr Runtimes()
    {
        if (_runtimesProbed) return _runtimes;
        _runtimesProbed = true;
        string rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "",
        };
        if (rid.Length > 0)   // 同路径幂等:Veldrid 已加载该副本则返回其句柄,不新增模块
            NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", SdlLib), out _runtimes);
        return _runtimes;
    }

    // ---- 函数指针解析(每候选按名 GetExport;失败抛,候选句柄非零时导出必在) ----

    static T Fn<T>(IntPtr module, string export) where T : class
    {
        IntPtr p = NativeLibrary.GetExport(module, export);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    static void GetVersion(IntPtr module, out SdlVersion v)
    {
        v = default;
        Fn<SdlVersionFn>(module, "SDL_GetVersion")(ref v);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlInitFn(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlWmInfoFn(IntPtr window, ref SdlSysWmInfo info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlVersionFn(ref SdlVersion version);

    // SDL_GL 六函数(SDLCALL = cdecl;int 参数对应 SDL_GLattr 枚举宽度)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GlSetAttributeFn(int attr, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GlCreateContextFn(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GlMakeCurrentFn(IntPtr window, IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlSwapWindowFn(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GlSetSwapIntervalFn(int interval);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr GlGetProcAddressFn([MarshalAs(UnmanagedType.LPStr)] string proc);

    // ---- 原生结构 ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion { public byte Major, Minor, Patch; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlSysWmInfo
    {
        public byte Maj, Min, Pat;
        public int Subsystem;
        public IntPtr Hwnd, Hdc, Hglrc, HelperWindow;   // 2.24+ win 分支 4 指针
        public IntPtr Pad1, Pad2, Pad3, Pad4;           // 其余平台分支余量
    }
}
