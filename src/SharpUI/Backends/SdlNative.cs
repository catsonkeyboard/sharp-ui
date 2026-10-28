using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>SDL 原生最小面(M3d-ii,T4 冒烟实证修订;M3e T2 增 GL 面):视频子系统初始化 + 窗口 HWND + GL 六函数。
/// 裁定#38(修订):应用布局"基目录 + runtimes/win-x64/native 双份 SDL2.dll"下,本程序集裸 DllImport
/// 按名解析与 旧窗口封装包 的解析会分裂为两个已加载模块(同基名不同全路径可并存,TextInput 热修已实证
/// 双 base 地址;T4 冒烟复现:Init 落模块 A 成功、窗口经旧窗口层落模块 B,WMInfo 报 "Invalid window"/sub=0)。
/// 模块落点不可假设:T4 取证明证 Init 落基目录副本而旧窗口层建窗落 runtimes 副本,双模块并存、属主不定
/// ——"Init 落点=旧窗口层将绑定的模块"式假设均已被证伪,不得复述。故不用裸 DllImport:
/// 1) Init:按候选序(基目录 → runtimes/&lt;rid&gt;/native → 程序集探测)显式 NativeLibrary.Load 首个命中副本,
///    SDL_Init 经该句柄函数指针执行——这只是确保"某"副本视频就绪的尽力初始化(失败值忽略,同 StartupUtilities
///    先例),其落点不保证是窗口属主模块;
/// 2) WMInfo:多候选探测(Init 句柄 → 按名解析模块 → runtimes 副本句柄),窗口属主模块应答
///    (非属主报 Invalid window),全败才抛并附各候选诊断。此探测是正确性的依赖(同 TextInput 活模块判定,
///    裁定#38 二次实证),不可"简化"为单句柄调用——去掉即复现 T4 冒烟崩溃。
/// 裁定#39:WMInfo 结构给足 SDL 2.24+ Windows 联合体尺寸(4 指针)+ 余量,防原生越界写。
/// GL 面(M3e T2)模块纪律:SDL_GL_* 窗口相关调用(CreateGlContext/MakeCurrent/SwapWindow)必须落在
/// 窗口属主模块(= WMInfo 应答模块,经 OwnerModule 探测);无窗口参的调用属主未知 —— SetGlAttribute
/// (时序:EnsureVideoInit 之后、建窗之前,须先全候选 SDL_Init,属性才能到达属主副本)/ SetSwapInterval /
/// GetGlProcAddress(上下文级)全候选设置/解析、任一成功即止。</summary>
internal static class SdlNative
{
    // DllImport/预载解析键:X-1 起常量化经 NativeLib(= Windows 现名,零行为差;非 Windows 映射见 Resolve)
    const string SdlLib = NativeLib.Sdl2;
    const uint SdlInitVideo = 0x00000020;

    // ---- SDL_GLattr / SDL_GLprofile 实数(出处:SDL_video.h typedef enum SDL_GLattr 无显式值按声明序递增;
    // 取自 SDL release-2.0.8(= 测试宿主 旧 nuget 窗口封装包(T3 已移除)所带 DLL 版本)与 release-2.30.6 头两处互证,全 2.x 同序) ----

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
        // 评审裁定(T2 Important#1 ②,M3e T3 落地):全部候选句柄逐一 SDL_Init(VIDEO) ——
        // SDL_GL_SetAttribute 须全候选可达(建窗前属主未知);某副本视频未初始化则其拒收 GL 属性,
        // 窗口(旧窗口层建窗落点不定)恰落在该副本时上下文即非 3.3 core(兼容上下文亦过版本下限断言)。
        // 各候选独立尽力(幂等引用计数,失败值忽略,同 StartupUtilities 先例);全无候选才抛。
        bool any = false;
        foreach (IntPtr h in Candidates())
        {
            _ = Fn<SdlInitFn>(h, "SDL_Init")(SdlInitVideo);
            any = true;
        }
        if (!any)
            throw new DllNotFoundException(
                $"{SdlLib} 不可用:基目录、runtimes/<rid>/native 与程序集探测均未命中");
    }

    internal static IntPtr GetWin32Hwnd(IntPtr sdlWindow)
    {
        foreach (IntPtr h in Candidates())
            if (TryWmInfo(h, sdlWindow, out IntPtr hwnd))
                return hwnd;
        throw OwnerFailure();
    }

    /// <summary>窗口属主 SDL 模块句柄(WMInfo 应答候选;窗口级缓存,窗口关闭不强求清理——句柄仅作字典键)。
    /// 裁定#38 纪律:任何窗口相关 SDL_GL_*/WMInfo 调用前经此定位属主,勿直接用 Primary()。
    /// 多窗/多线程评估②(D4 Task4,结论=维持缓存,不加失效钩子):关窗残留键事实——SdlPump.Unregister
    /// 只清泵窗表,本字典键(窗口地址)留存至进程结束,单键成本一 IntPtr(量级=历史窗数,非增长型)。
    /// 失效风险链="SDL_Window* 堆地址被分配器复用 且 新旧窗属主模块不同":两事件同时发生才出错,而应用建窗恒走
    /// 同一 SdlWindow 层(属主模块选择策略进程级一致),属主翻转不可达,残留键复用即命中正确属主。
    /// 精确失效(缓存命中亦 TryWmInfo 复验)会重引入每次调用的多候选探测,缓存归零收益,故不做;
    /// 若未来多窗口创建路径(不同层/不同模块建窗)出现,需先行给出失效策略再接入。</summary>
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

    /// <summary>设置 GL 建窗/建上下文属性。时序:调用方须已 EnsureVideoInit(全候选 SDL_Init VIDEO)且
    /// 尚未建窗 —— 某副本视频未初始化即拒收属性,窗口(旧窗口层建窗落点不定)恰落在该副本时上下文即
    /// 非 3.3 core(T3 实证:修复前属主副本未收属性,建的是兼容上下文,亦过版本下限断言)。属主未知
    /// (窗口尚不存在)→ 全候选设置(评审裁定 T2 Important#1 ①:不去 early-return)。返回聚合结果:
    /// 任一候选拒收即非 0(向调用方暴露;上述时序下 SDL_GL_SetAttribute 对合法 attr 恒 0,拒收属异常
    /// 布局,宁可显式失败——评审 Minor#2:聚合而非末候选遮蔽)。</summary>
    internal static int SetGlAttribute(int attr, int value)
    {
        int result = 0;
        foreach (IntPtr h in Candidates())
        {
            int r = Fn<GlSetAttributeFn>(h, "SDL_GL_SetAttribute")(attr, value);
            if (r != 0) result = r;   // 聚合任意非零
        }
        return result;
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

    /// <summary>查询窗口 GL drawable 像素尺寸(SDL_GL_GetDrawableSize;D4 Task4 增)。HiDPI 下
    /// drawable 可大于逻辑窗口尺寸(macOS Retina/Wayland 复合器缩放);Windows SDL 坐标即物理像素,
    /// 两者恒等(DPI=96 本机断言不漂移的前提,亦是窗口路径 TargetWidth/Height 实取 drawable 的
    /// 本机等价性依据)。窗口级调用 → 属主模块(裁定#38 纪律)。SDL 允许任一出参 NULL,本绑定恒传
    /// 有效出参(out 语义)。</summary>
    internal static void GetDrawableSize(IntPtr sdlWindow, out int w, out int h) =>
        Fn<GlGetDrawableSizeFn>(OwnerModule(sdlWindow), "SDL_GL_GetDrawableSize")(sdlWindow, out w, out h);

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

    /// <summary>解析 GL 函数指针(须已有 MakeCurrent 的上下文):WGL 当前上下文是进程/线程全局态,
    /// 经任一副本的 SDL_GL_GetProcAddress(内部 wglGetProcAddress)都能应答——非"仅属主应答,其余
    /// 返回 NULL";核心 1.1 函数 wglGetProcAddress 不应答,SDL 另有 opengl32 GetProcAddress 回退解析。
    /// 解得的 WGL 指针进程内恒有效、与解析副本无关(跨副本使用安全)。候选序逐一尝试,任一命中即返回,
    /// 全败返回 Zero(调用方决定是否报缺失)。</summary>
    internal static IntPtr GetGlProcAddress(string proc)
    {
        foreach (IntPtr h in Candidates())
        {
            IntPtr p = Fn<GlGetProcAddressFn>(h, "SDL_GL_GetProcAddress")(proc);
            if (p != IntPtr.Zero) return p;
        }
        return IntPtr.Zero;
    }

    /// <summary>销毁 GL 上下文(M3e T3:属主模块 —— 上下文须落其创建模块销毁;sdlWindow 仅作属主
    /// 解析键,OwnerModule 已于 CreateGlContext 时缓存,窗口此后关闭亦不失效)。SDL 2.x 的
    /// WIN_GL_DeleteContext 自带"当前上下文解绑再删"语义,删除当前上下文安全。</summary>
    internal static void DeleteGlContext(IntPtr sdlWindow, IntPtr ctx) =>
        Fn<GlDeleteContextFn>(OwnerModule(sdlWindow), "SDL_GL_DeleteContext")(ctx);

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

    // T1:SdlEvents 声明层复用(活模块候选序 + 按名取导出),可见性升 internal
    internal static IEnumerable<IntPtr> Candidates()
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
        // 候选序为本类自定(基目录 → runtimes/<rid>/native),非旧窗口层加载序的复刻——T4 实证两者落点
        // 可分裂(Init 落基目录副本、旧窗口层建窗落 runtimes 副本);落点不可假设,属主由 GetWin32Hwnd 多候选探测兜底。
        // X-1:基目录候选走平台解析(Windows = 现名逐字节同前;Linux/macOS 亦按 SONAME 找 vendored 副本)。
        // 前两候选为 baseDir+SONAME 的路径限定探测,只覆盖应用侧 vendored 副本、不触系统 SONAME
        // (/usr/lib 等搜索路径)——路径③(终审修复波项 c)改走 Current 解析对齐 Clipboard/FT/HB 预载
        // 模式:程序集探测 TryLoad 未命中应用目录后回落 OS 默认搜索,系统 SONAME 自此可达
        // (Linux CI 无 vendored 副本时 EnsureVideoInit 不再注定抛);Windows 上 Resolve 恒等现名,零差。
        if (NativeLibrary.TryLoad(Path.Combine(baseDir, NativeLib.Resolve(NativeLib.Current, "SDL2")), out _primary)) return _primary;
        if (rid.Length > 0
            && NativeLibrary.TryLoad(Path.Combine(baseDir, "runtimes", rid, "native",
                NativeLib.Resolve(NativeLib.Platform.Windows, "SDL2")), out _primary))   // win-* 布局仅 Windows 宿主存在
            return _primary;
        NativeLibrary.TryLoad(NativeLib.Resolve(NativeLib.Current, "SDL2"), typeof(SdlNative).Assembly, null, out _primary);   // 程序集探测兜底(测试宿主布局;路径③)
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
        if (rid.Length > 0)   // 同路径幂等:该副本已加载则返回其句柄,不新增模块;win-* 布局仅 Windows 宿主存在
            NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native",
                NativeLib.Resolve(NativeLib.Platform.Windows, "SDL2")), out _runtimes);
        return _runtimes;
    }

    // ---- 函数指针解析(每候选按名 GetExport;失败抛,候选句柄非零时导出必在;T1 起 SdlEvents 亦用) ----
    // T2(携带项③):(模块) → 委托 缓存(每委托类型一份静态字典)。Marshal.GetDelegateForFunctionPointer
    // 每调用新分配委托,SDL_PollEvent(每帧×每事件)、GetWindowSize(每帧×2)、GL SwapWindow(每帧)等热路径
    // 不缓存即持续分配。缓存以泛型静态类按 T 分桶:键=(模块, 委托类型),隐含"一委托类型恒解析
    // 同一导出名"的不变式由调用点约定维持(当前 18 处 1:1,核对见终审;新增同型二用调用前必须
    // 复核),且全程走 AOT 友好的泛型重载 GetDelegateForFunctionPointer&lt;T&gt;(非泛型 (IntPtr,Type)
    // 重载带 RequiresDynamicCode,AOT 实证 IL3050,弃用)。锁粒度粗(全 Fn 串行)但调用为纯字典
    // 查,纳秒级;UI 单线程,无争用。

    static readonly object _fnLock = new();

    static class FnCache<T> where T : class
    {
        internal static readonly Dictionary<IntPtr, T> ByModule = new();
    }

    internal static T Fn<T>(IntPtr module, string export) where T : class
    {
        lock (_fnLock)
        {
            if (!FnCache<T>.ByModule.TryGetValue(module, out T? cached))
                FnCache<T>.ByModule[module] = cached =
                    Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, export));
            return cached;
        }
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
    private delegate void GlDeleteContextFn(IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GlMakeCurrentFn(IntPtr window, IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlSwapWindowFn(IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGetDrawableSizeFn(IntPtr window, out int w, out int h);

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
