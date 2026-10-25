using System.Runtime.InteropServices;
using Veldrid.Sdl2;

namespace SharpUI.Backends;

/// <summary>
/// T11 D3D11 直连探针:零 Veldrid 图形栈、零 unsafe 的纯安全模式 COM vtable 互操作,
/// 最小清屏-呈现闭环:CreateDXGIFactory1 → EnumAdapters(0) → D3D11CreateDevice(adapter, BGRA 0x20,
/// 立即上下文经出参取得以规避 GetImmediateContext 槽位风险) → CreateSwapChain(legacy,
/// 640×480 BGRA8 双缓冲) → GetBuffer(0) → CreateRenderTargetView → OMSetRenderTargets →
/// ClearRenderTargetView(RGBA) → Present(1,0)。清屏采用基接口 ID3D11DeviceContext::ClearRenderTargetView
/// (槽 50)而非 ID3D11DeviceContext1::ClearView:后者槽位取决于基接口(104 方法)精确尾段计数且返回 void、
/// 错槽即进程崩溃无法"显式失败";基接口槽位以 d3d11.h 声明顺序双计数核验,功能等价(无着色器纯色清屏)。
/// 槽位纪律:每个 COM 对象取得后立即做 IUnknown AddRef(槽 1)/Release(槽 2)往返实证,
/// 任何不一致抛异常(带接口名与槽号)——该往返实证的是 IUnknown 槽 1/2 与对象活性;
/// 其余槽位由各调用的 hr 检查与 60 帧呈现闭环间接证实。
/// </summary>
public static class D3D11Spike
{
    // ---- 槽位表(全部 0-based;依据 d3d11.h / dxgi.h 接口方法声明顺序,出处见行尾注释)----
    private const int SlotAddRef = 1;                          // IUnknown: QI(0)/AddRef(1)/Release(2) — unknwn.h
    private const int SlotRelease = 2;
    private const int SlotFactoryEnumAdapters = 7;             // dxgi.h IDXGIObject: SetPrivateData(3)/SetPrivateDataInterface(4)/GetPrivateData(5)/GetParent(6);IDXGIFactory: EnumAdapters(7)/MakeWindowAssociation(8)/GetWindowAssociation(9)/CreateSwapChain(10)/CreateSoftwareAdapter(11)
    private const int SlotFactoryCreateSwapChain = 10;         // 同上
    private const int SlotSwapChainPresent = 8;                // dxgi.h IDXGISwapChain(IDXGIObject 0-6 + IDXGIDeviceSubObject::GetDevice(7)): Present(8)/GetBuffer(9)/SetFullscreenState(10)/... — GetDevice 常被漏数导致整体左移一位(#33)
    private const int SlotSwapChainGetBuffer = 9;              // 同上
    private const int SlotDeviceCreateRenderTargetView = 9;    // d3d11.h ID3D11Device: CreateBuffer(3)/CreateTexture1D(4)/CreateTexture2D(5)/CreateTexture3D(6)/CreateShaderResourceView(7)/CreateUnorderedAccessView(8)/CreateRenderTargetView(9)
    private const int SlotContextOmSetRenderTargets = 33;      // 本机 Windows SDK d3d11.h 实数:ID3D11DeviceContext : ID3D11DeviceChild(IUnknown 0-2 + DeviceChild 3-6),自有方法 VSSetConstantBuffers(7) 起,OMSetRenderTargets 为第 27 个 → 槽 33(DrawIndexed 第 6 个 → 槽 12,与社区钩子表互证)
    private const int SlotContextClearRenderTargetView = 50;   // 同上:第 44 个方法(CopyStructureCount(49) 之后、ClearUnorderedAccessViewUint(51) 之前)→ 槽 50

    private const uint DeviceFlagBgraSupport = 0x20;           // D3D11_CREATE_DEVICE_BGRA_SUPPORT(M1 探针事实)
    private const uint DxgiFormatBgra8Unorm = 28;              // DXGI_FORMAT_B8G8R8A8_UNORM
    private const uint DxgiUsageRenderTargetOutput = 32;       // DXGI_USAGE_RENDER_TARGET_OUTPUT
    private const uint SdkVersion = 7;                         // D3D11_SDK_VERSION
    private const int SdlSyswmWindows = 1;                     // SDL_SYSWM_TYPE: UNKNOWN(0)/WINDOWS(1)/X11(2)/...
    private const string SdlLib = "SDL2.dll";
    private const uint SdlInitVideo = 0x20;

    private static readonly object Gate = new();
    private static SpikeState? _state;
    private static bool _sdlReady;

    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");   // dxgi.h __uuidof(IDXGIFactory1)
    private static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");       // d3d11.h __uuidof(ID3D11Texture2D)

    /// <summary>单帧:惰性建静态状态(hwnd 变更则公平重建),清屏 RGBA(r,g,b,1) 后 Present(1,0);返回呈现成功。</summary>
    public static bool RunFrame(IntPtr hwnd, float r, float g, float b)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("hwnd 不能为空", nameof(hwnd));
        lock (Gate)
        {
            if (_state is null || _state.Hwnd != hwnd)
            {
                _state?.Dispose();
                _state = null;   // 先置空再重建:CreateState 失败重试时不留已 Dispose 的悬置状态(与 Reset 同型)
                _state = CreateState(hwnd);
            }
            return _state.PresentClear(r, g, b);
        }
    }

    /// <summary>释放全部静态状态(引用计数公平:逆序 Release 并留痕)。</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _state?.Dispose();
            _state = null;
        }
    }

    /// <summary>测试观测:是否仍有活动状态(Reset 后应为 false)。</summary>
    internal static bool HasLiveState { get { lock (Gate) return _state != null; } }

    /// <summary>测试观测:对每个存活对象做 AddRef/Release 往返(内部不一致即抛),返回守恒后的当前引用计数。</summary>
    internal static IReadOnlyList<(string Name, uint Count)> Refcounts()
    {
        lock (Gate)
        {
            if (_state is null) throw new InvalidOperationException("D3D11Spike 无活动状态(先 RunFrame)");
            return _state.Refcounts();
        }
    }

    /// <summary>Reset 释放轨迹(名称 → 末次 Release 返回值),供测试/诊断读取。</summary>
    internal static IReadOnlyList<(string Name, uint FinalCount)> LastResetReleases
    {
        get { lock (Gate) return _state is null ? Array.Empty<(string, uint)>() : _state.ReleaseTrace; }
    }

    /// <summary>预载 SDL2 原生库并初始化视频子系统(幂等、引用计数安全)。必须在 Sdl2Window 构造前调用:
    /// Veldrid.Sdl2 的窗口构造不自 Init SDL(实证:未 Init 时窗口无效,WMInfo 返 0;Init 后同路线成功)。</summary>
    internal static void InitSdl()
    {
        PreloadSdl();
        SDL_Init(SdlInitVideo);
    }

    /// <summary>Sdl2Window → Win32 HWND。路线(实证):Sdl2Window.SdlWindowHandle(公开 IntPtr,即 SDL_Window*;
    /// Handle 属性实证为窗口 ID 小整数而非指针,传原生即崩溃)→ 自声明 cdecl P/Invoke SDL_GetWindowWMInfo
    /// (Veldrid.Sdl2 的同名导出为 internal 不可调,与 Veldrid.D3D11 内部做法一致);
    /// 版本字以 SDL_GetVersion 动态填充(Windows 驱动校验 major/minor 相等)。</summary>
    internal static IntPtr GetWin32Hwnd(Sdl2Window window)
    {
        PreloadSdl();
        SDL_GetVersion(out SdlVersion v);
        var info = new SdlSysWmInfo
        {
            Version = (uint)v.Major | ((uint)v.Minor << 8) | ((uint)v.Patch << 16),   // SDL_version 三字节 {major,minor,patch} 原位布局
        };
        if (SDL_GetWindowWMInfo(window.SdlWindowHandle, ref info) == 0)
            throw new InvalidOperationException(
                $"SDL_GetWindowWMInfo 失败(路线 Sdl2Window.SdlWindowHandle → P/Invoke;SDL {v.Major}.{v.Minor}.{v.Patch}, subsystem={info.Subsystem})");
        if (info.Subsystem != SdlSyswmWindows)
            throw new InvalidOperationException($"非 Windows 窗口子系统:subsystem={info.Subsystem}");
        if (info.Window == IntPtr.Zero)
            throw new InvalidOperationException("SDL_GetWindowWMInfo 成功但 HWND 为空");
        return info.Window;
    }

    // ---- 状态构建与执行 ----

    private static SpikeState CreateState(IntPtr hwnd)
    {
        var s = new SpikeState { Hwnd = hwnd };
        try
        {
            // 1) IDXGIFactory1(dxgi.dll 直接导出,无 vtable 风险)
            Guid iidFactory = IidDxgiFactory1;   // REFIID 按值拷贝入栈槽,避免静态只读字段作 ref
            int hr = CreateDXGIFactory1(ref iidFactory, out s._factory);
            if (hr != 0 || s._factory == IntPtr.Zero) ThrowHr("CreateDXGIFactory1", hr, -1);
            s._relFactory = ReleaseFn(s._factory);
            ValidateIUnknown(s._factory, "IDXGIFactory1");

            // 2) 主适配器(工厂槽 7)
            var enumAdapters = Vfn<EnumAdaptersFn>(s._factory, SlotFactoryEnumAdapters, "IDXGIFactory::EnumAdapters");
            hr = enumAdapters(s._factory, 0, out s._adapter);
            if (hr != 0 || s._adapter == IntPtr.Zero) ThrowHr("EnumAdapters(0)", hr, SlotFactoryEnumAdapters);
            s._relAdapter = ReleaseFn(s._adapter);
            ValidateIUnknown(s._adapter, "IDXGIAdapter");

            // 3) 设备 + 立即上下文(直接 P/Invoke;上下文经出参取得,避免依赖 GetImmediateContext 槽位)
            hr = D3D11CreateDevice(s._adapter, 0 /* D3D_DRIVER_TYPE_UNKNOWN: 指定 adapter */, IntPtr.Zero,
                DeviceFlagBgraSupport, IntPtr.Zero, 0, SdkVersion,
                out s._device, out s.FeatureLevel, out s._context);
            if (hr != 0 || s._device == IntPtr.Zero || s._context == IntPtr.Zero)
                ThrowHr($"D3D11CreateDevice(BGRA 0x{DeviceFlagBgraSupport:X})", hr, -1);
            s._relDevice = ReleaseFn(s._device);
            s._relContext = ReleaseFn(s._context);
            ValidateIUnknown(s._device, "ID3D11Device");
            ValidateIUnknown(s._context, "ID3D11DeviceContext");

            // 4) legacy 交换链(工厂槽 10)
            var createSwapChain = Vfn<CreateSwapChainFn>(s._factory, SlotFactoryCreateSwapChain, "IDXGIFactory::CreateSwapChain");
            var desc = SwapChainDesc.Create(hwnd);
            hr = createSwapChain(s._factory, s._device, ref desc, out s._swapChain);
            if (hr != 0 || s._swapChain == IntPtr.Zero) ThrowHr("CreateSwapChain(legacy)", hr, SlotFactoryCreateSwapChain);
            s._relSwapChain = ReleaseFn(s._swapChain);
            ValidateIUnknown(s._swapChain, "IDXGISwapChain");

            // 5) 后备缓冲 + RTV(交换链槽 9;设备槽 9)
            var getBuffer = Vfn<GetBufferFn>(s._swapChain, SlotSwapChainGetBuffer, "IDXGISwapChain::GetBuffer");
            Guid iidTex = IidTexture2D;
            hr = getBuffer(s._swapChain, 0, ref iidTex, out s._backBuffer);
            if (hr != 0 || s._backBuffer == IntPtr.Zero) ThrowHr("GetBuffer(0, ID3D11Texture2D)", hr, SlotSwapChainGetBuffer);
            s._relBackBuffer = ReleaseFn(s._backBuffer);
            ValidateIUnknown(s._backBuffer, "ID3D11Texture2D");

            var createRtv = Vfn<CreateRenderTargetViewFn>(s._device, SlotDeviceCreateRenderTargetView, "ID3D11Device::CreateRenderTargetView");
            hr = createRtv(s._device, s._backBuffer, IntPtr.Zero /* desc: 继承资源格式 */, out s._rtv);
            if (hr != 0 || s._rtv == IntPtr.Zero) ThrowHr("CreateRenderTargetView", hr, SlotDeviceCreateRenderTargetView);
            s._relRtv = ReleaseFn(s._rtv);
            ValidateIUnknown(s._rtv, "ID3D11RenderTargetView");

            // 6) 绑定 OM 槽 0(上下文槽 33;views 数组经 HGlobal 零 unsafe 传递,调用内拷贝后立即释放)
            s._om = Vfn<OmSetRenderTargetsFn>(s._context, SlotContextOmSetRenderTargets, "ID3D11DeviceContext::OMSetRenderTargets");
            s._clear = Vfn<ClearRenderTargetViewFn>(s._context, SlotContextClearRenderTargetView, "ID3D11DeviceContext::ClearRenderTargetView");
            s._present = Vfn<PresentFn>(s._swapChain, SlotSwapChainPresent, "IDXGISwapChain::Present");
            IntPtr views = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(views, s._rtv);
                s._om(s._context, 1, views, IntPtr.Zero);
            }
            finally { Marshal.FreeHGlobal(views); }

            // 7) 建链期临时引用公平退场(设备经 D3D11CreateDevice 内部持有适配器,交换链持设备)
            s.ReleaseOne(ref s._factory, ref s._relFactory, "IDXGIFactory1");
            s.ReleaseOne(ref s._adapter, ref s._relAdapter, "IDXGIAdapter");
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    private sealed class SpikeState
    {
        public IntPtr Hwnd;   // 构造即定,不再变更(非 readonly 以允许对象初始化器赋值)
        public uint FeatureLevel;
        internal IntPtr _factory, _adapter, _device, _context, _swapChain, _backBuffer, _rtv;
        internal ComRelease? _relFactory, _relAdapter, _relDevice, _relContext, _relSwapChain, _relBackBuffer, _relRtv;
        internal OmSetRenderTargetsFn? _om;
        internal ClearRenderTargetViewFn? _clear;
        internal PresentFn? _present;
        public readonly List<(string Name, uint FinalCount)> ReleaseTrace = new();

        public bool PresentClear(float r, float g, float b)
        {
            _clear!(_context, _rtv, new[] { r, g, b, 1f });
            int hr = _present!(_swapChain, 1, 0);
            return hr == 0;
        }

        public IReadOnlyList<(string Name, uint Count)> Refcounts() => new[]
        {
            ("ID3D11Device", ValidateIUnknown(_device, "ID3D11Device")),
            ("ID3D11DeviceContext", ValidateIUnknown(_context, "ID3D11DeviceContext")),
            ("IDXGISwapChain", ValidateIUnknown(_swapChain, "IDXGISwapChain")),
            ("ID3D11Texture2D", ValidateIUnknown(_backBuffer, "ID3D11Texture2D")),
            ("ID3D11RenderTargetView", ValidateIUnknown(_rtv, "ID3D11RenderTargetView")),
        };

        /// <summary>逆序公平释放;每步留痕末次 Release 返回值。</summary>
        public void Dispose()
        {
            ReleaseOne(ref _rtv, ref _relRtv, "ID3D11RenderTargetView");
            ReleaseOne(ref _backBuffer, ref _relBackBuffer, "ID3D11Texture2D");
            ReleaseOne(ref _swapChain, ref _relSwapChain, "IDXGISwapChain");
            ReleaseOne(ref _context, ref _relContext, "ID3D11DeviceContext");
            ReleaseOne(ref _device, ref _relDevice, "ID3D11Device");
            ReleaseOne(ref _adapter, ref _relAdapter, "IDXGIAdapter");
            ReleaseOne(ref _factory, ref _relFactory, "IDXGIFactory1");
            _om = null; _clear = null; _present = null;
        }

        internal void ReleaseOne(ref IntPtr obj, ref ComRelease? rel, string name)
        {
            if (obj == IntPtr.Zero) return;
            uint final = rel is not null ? rel(obj) : 0;   // IUnknown::Release(槽 2)
            ReleaseTrace.Add((name, final));
            obj = IntPtr.Zero;
            rel = null;
        }
    }

    // ---- 互操作底座(SAFE 模式:ReadIntPtr 读 vtable + GetDelegateForFunctionPointer)----

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ComAddRef(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ComRelease(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdaptersFn(IntPtr self, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateSwapChainFn(IntPtr self, IntPtr device, ref SwapChainDesc desc, out IntPtr swapChain);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferFn(IntPtr self, uint buffer, ref Guid riid, out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateRenderTargetViewFn(IntPtr self, IntPtr resource, IntPtr desc, out IntPtr rtv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void OmSetRenderTargetsFn(IntPtr self, uint numViews, IntPtr views, IntPtr depthStencilView);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClearRenderTargetViewFn(IntPtr self, IntPtr rtv, float[] color);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PresentFn(IntPtr self, uint syncInterval, uint flags);

    /// <summary>SAFE 模式 vtable 槽取函数委托(零 unsafe)。</summary>
    private static T Vfn<T>(IntPtr comObj, int slot, string ifaceAndMethod) where T : class
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObj);                       // [self] → vtable
        IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);       // vtable[slot]
        if (fn == IntPtr.Zero)
            throw new InvalidOperationException($"vtable 槽位为空:{ifaceAndMethod}(槽 {slot})");
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    private static ComAddRef AddRefFn(IntPtr obj) => Vfn<ComAddRef>(obj, SlotAddRef, "IUnknown::AddRef");

    private static ComRelease ReleaseFn(IntPtr obj) => Vfn<ComRelease>(obj, SlotRelease, "IUnknown::Release");

    /// <summary>IUnknown 槽位实证:AddRef 返回 n+1、Release 返回 n(差恰为 1 且均 ≥1)即证明两槽位正确(#32:COM 返回值语义)。</summary>
    private static uint ValidateIUnknown(IntPtr obj, string name)
    {
        uint afterAdd = AddRefFn(obj)(obj);
        uint afterRel = ReleaseFn(obj)(obj);
        if (afterAdd == 0 || afterRel == 0 || afterAdd != afterRel + 1)
            throw new InvalidOperationException(
                $"IUnknown 槽位实证失败:{name} — AddRef(槽 {SlotAddRef})={afterAdd}, Release(槽 {SlotRelease})={afterRel}(须差 1 且 ≥1)");
        return afterRel;
    }

    private static void ThrowHr(string what, int hr, int slot)
        => throw new InvalidOperationException($"{what} 失败 hr=0x{hr:X8}" + (slot >= 0 ? $"(槽 {slot})" : ""));

    // ---- 结构体与原生导出 ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SwapChainDesc
    {
        public ModeDesc BufferDesc;
        public SampleDesc SampleDesc;
        public uint BufferUsage;
        public uint BufferCount;
        public IntPtr OutputWindow;
        public uint Windowed;
        public uint SwapEffect;
        public uint Flags;

        public static SwapChainDesc Create(IntPtr hwnd) => new()
        {
            BufferDesc = new ModeDesc { Width = 640, Height = 480, Format = DxgiFormatBgra8Unorm },
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            BufferUsage = DxgiUsageRenderTargetOutput,
            BufferCount = 2,
            OutputWindow = hwnd,
            Windowed = 1,
            SwapEffect = 0,   // DXGI_SWAP_EFFECT_DISCARD
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ModeDesc
    {
        public uint Width;
        public uint Height;
        public Rational RefreshRate;   // 0/0 = 未指定
        public uint Format;
        public uint ScanlineOrdering;  // 0 = 未指定
        public uint Scaling;           // 0 = 未指定
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational { public uint Numerator, Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleDesc { public uint Count, Quality; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion { public byte Major, Minor, Patch; }

    /// <summary>SDL_SysWMinfo(仅 Windows 分支所需首部):version{3字节+1填充} → subsystem(4) → 联合体 win 分支 {HWND, HDC, HGLRC}。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SdlSysWmInfo
    {
        public uint Version;
        public int Subsystem;
        public IntPtr Window;
        public IntPtr Hdc;
        public IntPtr Hglrc;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr adapter, uint driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out uint featureLevel, out IntPtr immediateContext);

    [DllImport(SdlLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GetVersion(out SdlVersion version);

    [DllImport(SdlLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetWindowWMInfo(IntPtr sdlWindow, ref SdlSysWmInfo info);

    /// <summary>SDL2 预载(沿用 Clipboard.cs 路线:先程序集探测,再回退 runtimes/&lt;rid&gt;/native)并初始化视频子系统。</summary>
    private static void PreloadSdl()
    {
        if (_sdlReady) return;
        try
        {
            if (!NativeLibrary.TryLoad(SdlLib, typeof(D3D11Spike).Assembly, null, out _))
            {
                string rid = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "win-x64",
                    Architecture.X86 => "win-x86",
                    Architecture.Arm64 => "win-arm64",
                    _ => ""
                };
                if (rid.Length > 0)
                    NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", SdlLib), out _);
            }
            SDL_Init(SdlInitVideo);   // SDL_INIT_VIDEO 全量式 Init(幂等;实证路线同 probe-sdl2)
        }
        catch (DllNotFoundException)
        {
            throw new InvalidOperationException("SDL2.dll 未加载:探针需可用的 SDL2 原生库(测试宿主 runtimes 布局)");
        }
        _sdlReady = true;
    }

    [DllImport(SdlLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_Init(uint flags);

}
