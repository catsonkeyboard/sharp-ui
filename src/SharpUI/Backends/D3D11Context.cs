using System.Runtime.InteropServices;
using System.Text;

namespace SharpUI.Backends;

/// <summary>
/// M3d-ii Task 3:D3D11 渲染上下文,实现后端中立 IRenderContext v2(替代 Veldrid GraphicsContext)。
/// headless 路径全功能(离屏 RT + 读回);窗口路径(交换链/Resize/Present)已实现,T4 接线。
/// 纪律:零 unsafe、NativeAOT 兼容、绘制路径零新分配(常驻钉扎槽 + span 拷贝,稳态无堆分配)。
/// 槽位纪律(照抄 D3D11Spike 实证模式):每个 COM 对象取得后立即做 IUnknown AddRef/Release 往返实证;
/// Dispose 逆序 Release 全部 COM 对象并留痕(ReleaseTrace)。hr 判定按 COM FAILED 语义(hr&lt;0):
/// CreateInputLayout 在布局与运行时缓存重复时返回 S_FALSE(成功码)且对象有效。
/// 上传语义(D3D11.3 功能规范):UpdateSubresource 对缓冲 pDstBox 必须 NULL 且整块 ByteWidth 自 pSrcData
/// 读出 — 故上下文自持常驻钉扎 scratch(UiVertex[]/ushort[],字节容量恒等于 GPU ByteWidth),Draw 先
/// span 拷入 scratch 再整块上传;尾段陈旧数据不被 DrawIndexed 的索引引用(直接钉调用者数组会在容量
/// (×2 增长)大于当帧数组时越界读取)。级间链接:D3D11 按硬件寄存器配对 VS 输出/PS 输入,PSIn 首成员
/// 必须 SV_Position(见 Shaders.HlslFragmentSource 注释;链接错位时采样/顶点色读 0,真机调试层实证)。
/// </summary>
public sealed class D3D11Context : IRenderContext
{
    /// <summary>图集边长(M3e 归接口:IRenderContext 契约成员,GL 后端同值实现;const→实例属性破坏性变更,消费面一律经接口/实例)。</summary>
    public uint AtlasSize => 1024;

    public uint TargetWidth { get; private set; }
    public uint TargetHeight { get; private set; }

    /// <summary>设备特性等级(诊断;D3D11CreateDevice 出参)。</summary>
    internal uint FeatureLevel => _featureLevel;

    bool _disposed;
    uint _featureLevel;

    // ---- COM 对象(引用计数经 ReleaseTrace 公平退场) ----
    IntPtr _factory, _adapter, _device, _context;
    IntPtr _swapChain, _backBuffer;                       // 窗口路径
    IntPtr _mainTexture;                                  // headless 主目标(离屏 RT 纹理)
    IntPtr _rtv;
    IntPtr _vertexShader, _pixelShader, _inputLayout, _blendState, _rasterizerState, _samplerState;
    IntPtr _atlasTexture, _atlasSrv, _constantBuffer, _vertexBuffer, _indexBuffer, _stagingTexture;

    // ---- 常驻钉扎槽(绘制路径零分配:COM 数组形参的原生落点) ----
    readonly D3D11Native.Viewport[] _viewportArr = new D3D11Native.Viewport[1];
    readonly int[] _scissorRect = new int[4];             // D3D11_RECT{left,top,right,bottom}
    readonly D3D11Native.Box[] _boxArr = new D3D11Native.Box[1];
    readonly IntPtr[] _rtvSlot = new IntPtr[1];
    readonly IntPtr[] _vbSlot = new IntPtr[1];
    readonly IntPtr[] _cbSlot = new IntPtr[1];
    readonly IntPtr[] _srvSlot = new IntPtr[1];
    readonly IntPtr[] _samplerSlot = new IntPtr[1];
    readonly uint[] _stridesArr = new uint[1];
    readonly uint[] _offsetsArr = new uint[1];
    readonly float[] _cb = new float[4];                  // cbuffer 16 字节(屏幕尺寸常量)
    readonly float[] _clearColor = new float[4];
    readonly byte[] _semPosition = AsciiZ("POSITION");    // SemanticName:ASCII+NUL,钉扎后常驻
    readonly byte[] _semTexcoord = AsciiZ("TEXCOORD");
    readonly byte[] _semColor = AsciiZ("COLOR");
    GCHandle _viewportPin, _scissorPin, _boxPin, _rtvPin, _vbSlotPin, _cbSlotPin, _srvSlotPin, _samplerSlotPin,
             _stridesPin, _offsetsPin, _cbPin, _semPosPin, _semTexPin, _semColPin;

    // ---- 上传 scratch(字节容量恒等于 GPU ByteWidth;首容量 256KB/512KB,×2 增长,同旧 Renderer) ----
    UiVertex[]? _vbScratch;
    ushort[]? _ibScratch;
    GCHandle _vbScratchPin, _ibScratchPin;
    uint _vbCapacityBytes = 256 * 1024, _ibCapacityBytes = 512 * 1024;

    // ---- 设备方法委托(构造期解析一次;绘制路径零分配要求禁止逐帧 Vfn) ----
    CreateBufferFn _createBuffer = null!;
    CreateTexture2DFn _createTexture2D = null!;
    CreateShaderResourceViewFn _createSrv = null!;
    CreateRenderTargetViewFn _createRtv = null!;
    CreateInputLayoutFn _createInputLayout = null!;
    CreateShaderFn _createVertexShaderFn = null!;
    CreateShaderFn _createPixelShaderFn = null!;
    CreateBlendStateFn _createBlendState = null!;
    CreateRasterizerStateFn _createRasterizerState = null!;
    CreateSamplerStateFn _createSamplerState = null!;

    // ---- 上下文/交换链方法委托 ----
    VssSetConstantBuffersFn _vssSetConstantBuffers = null!;
    PssSetShaderResourcesFn _pssSetShaderResources = null!;
    PssSetShaderFn _pssSetShader = null!;
    PssSetSamplersFn _pssSetSamplers = null!;
    VssSetShaderFn _vssSetShader = null!;
    DrawIndexedFn _drawIndexed = null!;
    MapFn _map = null!;
    UnmapFn _unmap = null!;
    IaSetInputLayoutFn _iaSetInputLayout = null!;
    IaSetVertexBuffersFn _iaSetVertexBuffers = null!;
    IaSetIndexBufferFn _iaSetIndexBuffer = null!;
    IaSetPrimitiveTopologyFn _iaSetPrimitiveTopology = null!;
    OmSetRenderTargetsFn _omSetRenderTargets = null!;
    OmSetBlendStateFn _omSetBlendState = null!;
    RsSetStateFn _rsSetState = null!;
    RsSetViewportsFn _rsSetViewports = null!;
    RsSetScissorRectsFn _rsSetScissorRects = null!;
    CopyResourceFn _copyResource = null!;
    UpdateSubresourceFn _updateSubresource = null!;
    ClearRenderTargetViewFn _clearRenderTargetView = null!;
    GetBufferFn _getBuffer = null!;
    PresentFn _present = null!;
    ResizeBuffersFn _resizeBuffers = null!;

    readonly List<(string Name, uint FinalCount)> _releaseTrace = new();

    /// <summary>Dispose 释放轨迹(名称 → 末次 Release 返回值),测试/诊断观测(模式照抄 D3D11Spike)。</summary>
    internal IReadOnlyList<(string Name, uint FinalCount)> ReleaseTrace => _releaseTrace;

    // ---- 构造与工厂 ----

    D3D11Context(uint width, uint height, IntPtr hwnd)
    {
        TargetWidth = width;
        TargetHeight = height;
        try
        {
            CreateDeviceChain();
            if (hwnd == IntPtr.Zero) CreateHeadlessTarget(width, height);
            else CreateWindowTarget(hwnd, width, height);
            // 建链期临时引用公平退场(设备内部持适配器;交换链持设备)— 照抄 spike 第 7 步
            ReleaseOne(ref _adapter, "IDXGIAdapter");
            ReleaseOne(ref _factory, "IDXGIFactory1");
            CreatePipeline();
            AllocPinnedSlots();
            // M3e 图集零初始化契约(IRenderContext.AtlasSize):Default 堆图集初始内容"未定义",
            // 构造期显式上传 4MB 全零(一次性;字形 padding 依赖透明零,不得依赖驱动隐式清零)。
            // 落点在 AllocPinnedSlots 后(UpdateAtlasRegion 依赖 _boxPin),先于任何 BeginFrame/Draw。
            UpdateAtlasRegion(new byte[AtlasSize * AtlasSize * 4], 0, 0, AtlasSize, AtlasSize);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>离屏渲染上下文:B8G8R8A8 主目标(读回字节序 [B,G,R,A] 与旧断言兼容),无交换链。</summary>
    public static D3D11Context CreateHeadless(uint width, uint height)
    {
        ValidateSize(width, height);
        return new D3D11Context(width, height, IntPtr.Zero);
    }

    /// <summary>窗口渲染上下文:legacy 交换链(B8G8R8A8 双缓冲 VSync)。T4 与 SdlNative/窗口层接线。</summary>
    public static D3D11Context CreateForWindow(IntPtr hwnd, uint width, uint height)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("hwnd 不能为空", nameof(hwnd));
        ValidateSize(width, height);
        return new D3D11Context(width, height, hwnd);
    }

    static void ValidateSize(uint width, uint height)
    {
        if (width == 0 || height == 0)
            throw new ArgumentException($"目标尺寸必须为正(width={width}, height={height})");
    }

    // ---- IRenderContext v2 ----

    /// <summary>窗口交换链 resize:解绑 RTV(管线仍间接引用后备缓冲,仅 Release 自身引用不够,MSDN 要求
    /// 先 OMSetRenderTargets(0,null,null) 否则 DXGI_ERROR_INVALID_CALL)→ Release 旧 RTV/后备缓冲 →
    /// ResizeBuffers → GetBuffer → 新 RTV;headless 抛。</summary>
    public void Resize(uint width, uint height)
    {
        ThrowIfDisposed();
        ValidateSize(width, height);
        if (_swapChain == IntPtr.Zero)
            throw new NotSupportedException("headless 上下文不支持 Resize(离屏目标尺寸随重建变更)");
        _omSetRenderTargets(_context, 0, IntPtr.Zero, IntPtr.Zero);   // 解绑管线对后备缓冲的输出绑定
        ReleaseOne(ref _rtv, "ID3D11RenderTargetView");
        ReleaseOne(ref _backBuffer, "ID3D11Texture2D(backbuffer)");
        ReleaseOne(ref _stagingTexture, "ID3D11Texture2D(staging)");   // 尺寸随 resize 失配,惰性重建
        int hr = _resizeBuffers(_swapChain, 2 /*同 SwapChainDesc.BufferCount*/, width, height,
            D3D11Native.DxgiFormatB8G8R8A8Unorm, 0);
        if (hr < 0) ThrowHr("IDXGISwapChain::ResizeBuffers", hr, D3D11Native.SlotSwapResizeBuffers);
        AcquireBackBufferAndView();
        TargetWidth = width;
        TargetHeight = height;
    }

    /// <summary>图集区域上传(RGBA 字节,左上原点):pin rgba + UpdateSubresource(Box, RowPitch=w*4)。</summary>
    public void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint width, uint height)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rgba);
        if (width == 0 || height == 0) return;
        if (x + width > AtlasSize || y + height > AtlasSize)
            throw new ArgumentOutOfRangeException($"图集区域越界({x}+{width} x {y}+{height} > {AtlasSize})");
        if (rgba.Length < checked(width * height * 4))
            throw new ArgumentException($"rgba 长度不足:需 {width * height * 4},实有 {rgba.Length}", nameof(rgba));
        GCHandle pin = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            _boxArr[0] = new D3D11Native.Box { Left = x, Top = y, Front = 0, Right = x + width, Bottom = y + height, Back = 1 };
            _updateSubresource(_context, _atlasTexture, 0, _boxPin.AddrOfPinnedObject(),
                pin.AddrOfPinnedObject(), width * 4, 0);
        }
        finally { pin.Free(); }
    }

    /// <summary>帧起始:OMSetRenderTargets(1, rtv, null) + RSSetViewports(全目标) + ClearRenderTargetView。</summary>
    public void BeginFrame(float r, float g, float b, float a)
    {
        ThrowIfDisposed();
        _rtvSlot[0] = _rtv;
        _omSetRenderTargets(_context, 1, _rtvPin.AddrOfPinnedObject(), IntPtr.Zero);
        _viewportArr[0] = new D3D11Native.Viewport
        {
            TopLeftX = 0f, TopLeftY = 0f, Width = TargetWidth, Height = TargetHeight, MinDepth = 0f, MaxDepth = 1f,
        };
        _rsSetViewports(_context, 1, _viewportPin.AddrOfPinnedObject());
        _clearColor[0] = r; _clearColor[1] = g; _clearColor[2] = b; _clearColor[3] = a;
        _clearRenderTargetView(_context, _rtv, _clearColor);
    }

    /// <summary>顶点着色器屏幕尺寸常量(逻辑像素):写 _cb[0]/[1] 并整块上传 16 字节常量缓冲。</summary>
    public void SetScreenSize(float logicalWidth, float logicalHeight)
    {
        ThrowIfDisposed();
        _cb[0] = logicalWidth;
        _cb[1] = logicalHeight;
        _updateSubresource(_context, _constantBuffer, 0, IntPtr.Zero, _cbPin.AddrOfPinnedObject(), 0, 0);
    }

    /// <summary>剪裁矩形(目标像素坐标,经换算后的最终值;全开=目标尺寸由 Renderer 侧负责)。</summary>
    public void SetScissor(uint x, uint y, uint w, uint h)
    {
        ThrowIfDisposed();
        _scissorRect[0] = (int)x;
        _scissorRect[1] = (int)y;
        _scissorRect[2] = checked((int)(x + w));
        _scissorRect[3] = checked((int)(y + h));
        _rsSetScissorRects(_context, 1, _scissorPin.AddrOfPinnedObject());
    }

    /// <summary>绘制一段索引几何(零分配):取 indices[startIndex .. startIndex+indexCount) 分段,重定基拷入
    /// 常驻 scratch 头部(DrawIndexed 偏移 0)→ UpdateSubresource 整块上传 → 全状态落地。取舍:分段重定基的
    /// 每次拷贝量 O(indexCount),一帧 N 个剪裁区段合计 O(总索引数);若上传 [0..startIndex+indexCount) 维持
    /// GPU 侧偏移,末尾区段每次重传几乎整个前缀,均摊 O(N×总数),故选重定基。</summary>
    public void Draw(UiVertex[] vertices, int vertexCount, ushort[] indices, uint indexCount, uint startIndex)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (vertexCount < 0 || vertexCount > vertices.Length)
            throw new ArgumentOutOfRangeException(nameof(vertexCount), $"顶点数越界:{vertexCount}(数组 {vertices.Length})");
        if (indexCount > (uint)indices.Length || startIndex > (uint)indices.Length - indexCount)
            throw new ArgumentOutOfRangeException(nameof(indexCount),
                $"索引分段越界:[{startIndex}, {startIndex + indexCount}) > 数组 {indices.Length}");
        if (indexCount == 0) return;

        EnsureUploadCapacity(vertexCount, (int)indexCount);
        vertices.AsSpan(0, vertexCount).CopyTo(_vbScratch!);
        indices.AsSpan((int)startIndex, (int)indexCount).CopyTo(_ibScratch!);
        // 缓冲更新语义:box=NULL = 整块 ByteWidth;scratch 字节容量恒等于 ByteWidth,读取有界
        _updateSubresource(_context, _vertexBuffer, 0, IntPtr.Zero, _vbScratchPin.AddrOfPinnedObject(), 0, 0);
        _updateSubresource(_context, _indexBuffer, 0, IntPtr.Zero, _ibScratchPin.AddrOfPinnedObject(), 0, 0);

        _iaSetInputLayout(_context, _inputLayout);
        _vbSlot[0] = _vertexBuffer;
        _iaSetVertexBuffers(_context, 0, 1, _vbSlotPin.AddrOfPinnedObject(),
            _stridesPin.AddrOfPinnedObject(), _offsetsPin.AddrOfPinnedObject());
        _iaSetIndexBuffer(_context, _indexBuffer, D3D11Native.DxgiFormatR16Uint, 0);
        _iaSetPrimitiveTopology(_context, D3D11Native.TopologyTriangleList);
        _vssSetShader(_context, _vertexShader, IntPtr.Zero, 0);
        _pssSetShader(_context, _pixelShader, IntPtr.Zero, 0);
        _vssSetConstantBuffers(_context, 0, 1, _cbSlotPin.AddrOfPinnedObject());
        _pssSetShaderResources(_context, 0, 1, _srvSlotPin.AddrOfPinnedObject());
        _pssSetSamplers(_context, 0, 1, _samplerSlotPin.AddrOfPinnedObject());
        _drawIndexed(_context, indexCount, 0, 0);   // 分段已重定基到缓冲头,偏移恒 0
    }

    /// <summary>呈现:窗口 Present(1,0) VSync,hr 失败抛(设备丢失显式失败);headless no-op。</summary>
    public void Present()
    {
        ThrowIfDisposed();
        if (_swapChain == IntPtr.Zero) return;   // headless no-op
        int hr = _present(_swapChain, 1, 0);
        if (hr < 0)
            throw new InvalidOperationException($"IDXGISwapChain::Present(1,0) 失败 hr=0x{hr:X8}(设备丢失显式失败)");
    }

    /// <summary>读回主目标(GPU 测试用):惰性 staging 纹理 + CopyResource + Map READ,按 RowPitch 逐行拷
    /// (解除 M1 的 width*4 对齐 256 限制)。</summary>
    public byte[] ReadBackMainFramebuffer()
    {
        ThrowIfDisposed();
        IntPtr source = _mainTexture != IntPtr.Zero ? _mainTexture : _backBuffer;
        if (source == IntPtr.Zero)
            throw new InvalidOperationException("主目标不可用(headless 与窗口路径二选一)");
        if (_stagingTexture == IntPtr.Zero)
        {
            var desc = new D3D11Native.Texture2DDesc
            {
                Width = TargetWidth, Height = TargetHeight, MipLevels = 1, ArraySize = 1,
                Format = D3D11Native.DxgiFormatB8G8R8A8Unorm,
                Sample = new D3D11Native.SampleDesc { Count = 1, Quality = 0 },
                Usage = D3D11Native.D3D11UsageStaging,
                CPUAccessFlags = D3D11Native.CpuAccessRead,
            };
            int hr = _createTexture2D(_device, ref desc, IntPtr.Zero, out _stagingTexture);
            if (hr < 0 || _stagingTexture == IntPtr.Zero)
                ThrowHr("CreateTexture2D(staging)", hr, D3D11Native.SlotDevCreateTexture2D);
            ValidateIUnknown(_stagingTexture, "ID3D11Texture2D(staging)");
        }
        _copyResource(_context, _stagingTexture, source);
        int hrMap = _map(_context, _stagingTexture, 0, D3D11Native.MapRead, 0, out D3D11Native.MappedSubresource mapped);
        if (hrMap < 0 || mapped.Data == IntPtr.Zero)
            ThrowHr("ID3D11DeviceContext::Map(READ)", hrMap, D3D11Native.SlotCtxMap);
        uint rowBytes = TargetWidth * 4;
        byte[] pixels = new byte[rowBytes * TargetHeight];   // 测试路径,分配允许
        try
        {
            for (uint row = 0; row < TargetHeight; row++)
                Marshal.Copy(IntPtr.Add(mapped.Data, (int)(row * mapped.RowPitch)), pixels,
                    (int)(row * rowBytes), (int)rowBytes);
        }
        finally { _unmap(_context, _stagingTexture, 0); }
        return pixels;
    }

    // ---- 状态构建 ----

    /// <summary>设备链(照抄 D3D11Spike.CreateState 第 1–3 步):factory1 → adapter0 → D3D11CreateDevice(BGRA)。</summary>
    void CreateDeviceChain()
    {
        Guid iidFactory = D3D11Native.IidDxgiFactory1;   // REFIID 按值拷贝入栈槽,避免静态只读字段作 ref
        int hr = D3D11Native.CreateDXGIFactory1(ref iidFactory, out _factory);
        if (hr < 0 || _factory == IntPtr.Zero) ThrowHr("CreateDXGIFactory1", hr, -1);
        ValidateIUnknown(_factory, "IDXGIFactory1");

        var enumAdapters = Vfn<EnumAdaptersFn>(_factory, D3D11Native.SlotFactoryEnumAdapters, "IDXGIFactory::EnumAdapters");
        hr = enumAdapters(_factory, 0, out _adapter);
        if (hr < 0 || _adapter == IntPtr.Zero)
            ThrowHr("EnumAdapters(0)", hr, D3D11Native.SlotFactoryEnumAdapters);
        ValidateIUnknown(_adapter, "IDXGIAdapter");

        hr = D3D11Native.D3D11CreateDevice(_adapter, 0 /* D3D_DRIVER_TYPE_UNKNOWN: 指定 adapter */, IntPtr.Zero,
            D3D11Native.DeviceFlagBgraSupport, IntPtr.Zero, 0, D3D11Native.SdkVersion,
            out _device, out _featureLevel, out _context);
        if (hr < 0 || _device == IntPtr.Zero || _context == IntPtr.Zero)
            ThrowHr($"D3D11CreateDevice(BGRA 0x{D3D11Native.DeviceFlagBgraSupport:X})", hr, -1);
        ValidateIUnknown(_device, "ID3D11Device");
        ValidateIUnknown(_context, "ID3D11DeviceContext");

        // 设备方法委托(槽位出处与校验和见 D3D11Native 注释)
        _createBuffer = Vfn<CreateBufferFn>(_device, D3D11Native.SlotDevCreateBuffer, "ID3D11Device::CreateBuffer");
        _createTexture2D = Vfn<CreateTexture2DFn>(_device, D3D11Native.SlotDevCreateTexture2D, "ID3D11Device::CreateTexture2D");
        _createSrv = Vfn<CreateShaderResourceViewFn>(_device, D3D11Native.SlotDevCreateShaderResourceView, "ID3D11Device::CreateShaderResourceView");
        _createRtv = Vfn<CreateRenderTargetViewFn>(_device, D3D11Native.SlotDevCreateRenderTargetView, "ID3D11Device::CreateRenderTargetView");
        _createInputLayout = Vfn<CreateInputLayoutFn>(_device, D3D11Native.SlotDevCreateInputLayout, "ID3D11Device::CreateInputLayout");
        _createVertexShaderFn = Vfn<CreateShaderFn>(_device, D3D11Native.SlotDevCreateVertexShader, "ID3D11Device::CreateVertexShader");
        _createPixelShaderFn = Vfn<CreateShaderFn>(_device, D3D11Native.SlotDevCreatePixelShader, "ID3D11Device::CreatePixelShader");
        _createBlendState = Vfn<CreateBlendStateFn>(_device, D3D11Native.SlotDevCreateBlendState, "ID3D11Device::CreateBlendState");
        _createRasterizerState = Vfn<CreateRasterizerStateFn>(_device, D3D11Native.SlotDevCreateRasterizerState, "ID3D11Device::CreateRasterizerState");
        _createSamplerState = Vfn<CreateSamplerStateFn>(_device, D3D11Native.SlotDevCreateSamplerState, "ID3D11Device::CreateSamplerState");

        // 上下文方法委托
        _vssSetConstantBuffers = Vfn<VssSetConstantBuffersFn>(_context, D3D11Native.SlotCtxVssSetConstantBuffers, "ID3D11DeviceContext::VSSetConstantBuffers");
        _pssSetShaderResources = Vfn<PssSetShaderResourcesFn>(_context, D3D11Native.SlotCtxPssSetShaderResources, "ID3D11DeviceContext::PSSetShaderResources");
        _pssSetShader = Vfn<PssSetShaderFn>(_context, D3D11Native.SlotCtxPssSetShader, "ID3D11DeviceContext::PSSetShader");
        _pssSetSamplers = Vfn<PssSetSamplersFn>(_context, D3D11Native.SlotCtxPssSetSamplers, "ID3D11DeviceContext::PSSetSamplers");
        _vssSetShader = Vfn<VssSetShaderFn>(_context, D3D11Native.SlotCtxVssSetShader, "ID3D11DeviceContext::VSSetShader");
        _drawIndexed = Vfn<DrawIndexedFn>(_context, D3D11Native.SlotCtxDrawIndexed, "ID3D11DeviceContext::DrawIndexed");
        _map = Vfn<MapFn>(_context, D3D11Native.SlotCtxMap, "ID3D11DeviceContext::Map");
        _unmap = Vfn<UnmapFn>(_context, D3D11Native.SlotCtxUnmap, "ID3D11DeviceContext::Unmap");
        _iaSetInputLayout = Vfn<IaSetInputLayoutFn>(_context, D3D11Native.SlotCtxIaSetInputLayout, "ID3D11DeviceContext::IASetInputLayout");
        _iaSetVertexBuffers = Vfn<IaSetVertexBuffersFn>(_context, D3D11Native.SlotCtxIaSetVertexBuffers, "ID3D11DeviceContext::IASetVertexBuffers");
        _iaSetIndexBuffer = Vfn<IaSetIndexBufferFn>(_context, D3D11Native.SlotCtxIaSetIndexBuffer, "ID3D11DeviceContext::IASetIndexBuffer");
        _iaSetPrimitiveTopology = Vfn<IaSetPrimitiveTopologyFn>(_context, D3D11Native.SlotCtxIaSetPrimitiveTopology, "ID3D11DeviceContext::IASetPrimitiveTopology");
        _omSetRenderTargets = Vfn<OmSetRenderTargetsFn>(_context, D3D11Native.SlotCtxOmSetRenderTargets, "ID3D11DeviceContext::OMSetRenderTargets");
        _omSetBlendState = Vfn<OmSetBlendStateFn>(_context, D3D11Native.SlotCtxOmSetBlendState, "ID3D11DeviceContext::OMSetBlendState");
        _rsSetState = Vfn<RsSetStateFn>(_context, D3D11Native.SlotCtxRsSetState, "ID3D11DeviceContext::RSSetState");
        _rsSetViewports = Vfn<RsSetViewportsFn>(_context, D3D11Native.SlotCtxRsSetViewports, "ID3D11DeviceContext::RSSetViewports");
        _rsSetScissorRects = Vfn<RsSetScissorRectsFn>(_context, D3D11Native.SlotCtxRsSetScissorRects, "ID3D11DeviceContext::RSSetScissorRects");
        _copyResource = Vfn<CopyResourceFn>(_context, D3D11Native.SlotCtxCopyResource, "ID3D11DeviceContext::CopyResource");
        _updateSubresource = Vfn<UpdateSubresourceFn>(_context, D3D11Native.SlotCtxUpdateSubresource, "ID3D11DeviceContext::UpdateSubresource");
        _clearRenderTargetView = Vfn<ClearRenderTargetViewFn>(_context, D3D11Native.SlotCtxClearRenderTargetView, "ID3D11DeviceContext::ClearRenderTargetView");
    }

    /// <summary>headless 主目标:B8G8R8A8(Default,RenderTarget|ShaderResource)+ RTV。</summary>
    void CreateHeadlessTarget(uint width, uint height)
    {
        var desc = new D3D11Native.Texture2DDesc
        {
            Width = width, Height = height, MipLevels = 1, ArraySize = 1,
            Format = D3D11Native.DxgiFormatB8G8R8A8Unorm,   // 读回字节序 [B,G,R,A] 与旧断言兼容
            Sample = new D3D11Native.SampleDesc { Count = 1, Quality = 0 },
            Usage = D3D11Native.D3D11UsageDefault,
            BindFlags = D3D11Native.BindRenderTarget | D3D11Native.BindShaderResource,
        };
        int hr = _createTexture2D(_device, ref desc, IntPtr.Zero, out _mainTexture);
        if (hr < 0 || _mainTexture == IntPtr.Zero) ThrowHr("CreateTexture2D(headless target)", hr, D3D11Native.SlotDevCreateTexture2D);
        ValidateIUnknown(_mainTexture, "ID3D11Texture2D(main)");
        hr = _createRtv(_device, _mainTexture, IntPtr.Zero /* desc: 继承资源格式 */, out _rtv);
        if (hr < 0 || _rtv == IntPtr.Zero) ThrowHr("CreateRenderTargetView(main)", hr, D3D11Native.SlotDevCreateRenderTargetView);
        ValidateIUnknown(_rtv, "ID3D11RenderTargetView");
    }

    /// <summary>窗口目标:legacy 交换链 → 后备缓冲 + RTV(ResizeBuffers 路径见 Resize)。</summary>
    void CreateWindowTarget(IntPtr hwnd, uint width, uint height)
    {
        var createSwapChain = Vfn<CreateSwapChainFn>(_factory, D3D11Native.SlotFactoryCreateSwapChain, "IDXGIFactory::CreateSwapChain");
        var desc = D3D11Native.SwapChainDesc.Create(hwnd, width, height);
        int hr = createSwapChain(_factory, _device, ref desc, out _swapChain);
        if (hr < 0 || _swapChain == IntPtr.Zero) ThrowHr("CreateSwapChain(legacy)", hr, D3D11Native.SlotFactoryCreateSwapChain);
        ValidateIUnknown(_swapChain, "IDXGISwapChain");
        _getBuffer = Vfn<GetBufferFn>(_swapChain, D3D11Native.SlotSwapGetBuffer, "IDXGISwapChain::GetBuffer");
        _present = Vfn<PresentFn>(_swapChain, D3D11Native.SlotSwapPresent, "IDXGISwapChain::Present");
        _resizeBuffers = Vfn<ResizeBuffersFn>(_swapChain, D3D11Native.SlotSwapResizeBuffers, "IDXGISwapChain::ResizeBuffers");
        AcquireBackBufferAndView();
    }

    void AcquireBackBufferAndView()
    {
        Guid iidTex = D3D11Native.IidTexture2D;
        int hr = _getBuffer(_swapChain, 0, ref iidTex, out _backBuffer);
        if (hr < 0 || _backBuffer == IntPtr.Zero) ThrowHr("GetBuffer(0, ID3D11Texture2D)", hr, D3D11Native.SlotSwapGetBuffer);
        ValidateIUnknown(_backBuffer, "ID3D11Texture2D(backbuffer)");
        hr = _createRtv(_device, _backBuffer, IntPtr.Zero, out _rtv);
        if (hr < 0 || _rtv == IntPtr.Zero) ThrowHr("CreateRenderTargetView(backbuffer)", hr, D3D11Native.SlotDevCreateRenderTargetView);
        ValidateIUnknown(_rtv, "ID3D11RenderTargetView");
    }

    /// <summary>管线一次性构建:HLSL 编译 → VS/PS/InputLayout → Blend/Rasterizer/Sampler → 图集 → 常量缓冲。</summary>
    void CreatePipeline()
    {
        // 1) 着色器(d3dcompiler_47 运行时编译;失败显式抛)
        var (vsCode, vsErr) = ShaderCompiler.Compile(Shaders.HlslVertexSource, "VSMain", "vs_5_0");
        if (vsErr is not null) throw new InvalidOperationException($"顶点着色器编译失败:{vsErr}");
        var (psCode, psErr) = ShaderCompiler.Compile(Shaders.HlslFragmentSource, "PSMain", "ps_5_0");
        if (psErr is not null) throw new InvalidOperationException($"像素着色器编译失败:{psErr}");

        // 2) SemanticName 常驻钉扎(构造存续期有效,Dispose 释放)
        _semPosPin = GCHandle.Alloc(_semPosition, GCHandleType.Pinned);
        _semTexPin = GCHandle.Alloc(_semTexcoord, GCHandleType.Pinned);
        _semColPin = GCHandle.Alloc(_semColor, GCHandleType.Pinned);

        // 3) 顶点着色器 + 输入布局(R32G32_FLOAT@0 / R32G32_FLOAT@8 / R8G8B8A8_UNORM@16,与 UiVertex 20 字节一致)
        GCHandle vsPin = GCHandle.Alloc(vsCode, GCHandleType.Pinned);
        try
        {
            IntPtr vsPtr = vsPin.AddrOfPinnedObject();
            int hr = _createVertexShaderFn(_device, vsPtr, (UIntPtr)vsCode.Length, IntPtr.Zero, out _vertexShader);
            if (hr < 0 || _vertexShader == IntPtr.Zero) ThrowHr("CreateVertexShader", hr, D3D11Native.SlotDevCreateVertexShader);
            ValidateIUnknown(_vertexShader, "ID3D11VertexShader");

            var descs = new D3D11Native.InputElementDesc[3]
            {
                new() { SemanticName = _semPosPin.AddrOfPinnedObject(), SemanticIndex = 0, Format = D3D11Native.DxgiFormatR32G32Float, InputSlot = 0, AlignedByteOffset = 0, InputSlotClass = D3D11Native.InputPerVertexData, InstanceDataStepRate = 0 },
                new() { SemanticName = _semTexPin.AddrOfPinnedObject(), SemanticIndex = 0, Format = D3D11Native.DxgiFormatR32G32Float, InputSlot = 0, AlignedByteOffset = 8 /* UiVertex.U */, InputSlotClass = D3D11Native.InputPerVertexData, InstanceDataStepRate = 0 },
                new() { SemanticName = _semColPin.AddrOfPinnedObject(), SemanticIndex = 0, Format = D3D11Native.DxgiFormatR8G8B8A8Unorm, InputSlot = 0, AlignedByteOffset = 16 /* UiVertex.R */, InputSlotClass = D3D11Native.InputPerVertexData, InstanceDataStepRate = 0 },
            };
            int descSize = Marshal.SizeOf<D3D11Native.InputElementDesc>();
            IntPtr pDescs = Marshal.AllocHGlobal(descs.Length * descSize);
            try
            {
                for (int i = 0; i < descs.Length; i++)
                    Marshal.StructureToPtr(descs[i], IntPtr.Add(pDescs, i * descSize), false);
                hr = _createInputLayout(_device, pDescs, (uint)descs.Length, vsPtr, (UIntPtr)vsCode.Length, out _inputLayout);
                if (hr < 0 || _inputLayout == IntPtr.Zero) ThrowHr("CreateInputLayout", hr, D3D11Native.SlotDevCreateInputLayout);
            }
            finally { Marshal.FreeHGlobal(pDescs); }
            ValidateIUnknown(_inputLayout, "ID3D11InputLayout");
        }
        finally { vsPin.Free(); }

        // 4) 像素着色器
        GCHandle psPin = GCHandle.Alloc(psCode, GCHandleType.Pinned);
        try
        {
            int hr = _createPixelShaderFn(_device, psPin.AddrOfPinnedObject(), (UIntPtr)psCode.Length, IntPtr.Zero, out _pixelShader);
            if (hr < 0 || _pixelShader == IntPtr.Zero) ThrowHr("CreatePixelShader", hr, D3D11Native.SlotDevCreatePixelShader);
        }
        finally { psPin.Free(); }
        ValidateIUnknown(_pixelShader, "ID3D11PixelShader");

        // 5) 混合:预乘输出(One, InvSrcAlpha × Add;同旧 SingleOverrideBlend)
        var rt0 = new D3D11Native.RenderTargetBlendDesc
        {
            BlendEnable = 1,
            SrcBlend = D3D11Native.BlendOne, DestBlend = D3D11Native.BlendInvSrcAlpha, BlendOp = D3D11Native.BlendOpAdd,
            SrcBlendAlpha = D3D11Native.BlendOne, DestBlendAlpha = D3D11Native.BlendInvSrcAlpha, BlendOpAlpha = D3D11Native.BlendOpAdd,
            RenderTargetWriteMask = 0x0F,   // D3D11_COLOR_WRITE_ENABLE_ALL
        };
        var blend = new D3D11Native.BlendDesc { AlphaToCoverageEnable = 0, IndependentBlendEnable = 0, R0 = rt0 };
        int hrBlend = _createBlendState(_device, ref blend, out _blendState);
        if (hrBlend < 0 || _blendState == IntPtr.Zero) ThrowHr("CreateBlendState", hrBlend, D3D11Native.SlotDevCreateBlendState);
        ValidateIUnknown(_blendState, "ID3D11BlendState");

        // 6) 光栅:Solid/CullNone/DepthClip=0/Scissor=1
        var raster = new D3D11Native.RasterizerDesc
        {
            FillMode = D3D11Native.FillSolid, CullMode = D3D11Native.CullNone,
            FrontCounterClockwise = 0, DepthBias = 0, DepthBiasClamp = 0f, SlopeScaledDepthBias = 0f,
            DepthClipEnable = 0, ScissorEnable = 1, MultisampleEnable = 0, AntialiasedLineEnable = 0,
        };
        int hrRaster = _createRasterizerState(_device, ref raster, out _rasterizerState);
        if (hrRaster < 0 || _rasterizerState == IntPtr.Zero) ThrowHr("CreateRasterizerState", hrRaster, D3D11Native.SlotDevCreateRasterizerState);
        ValidateIUnknown(_rasterizerState, "ID3D11RasterizerState");

        // 7) 采样器:Point/Clamp×3(图集像素精确采样)
        var sampler = new D3D11Native.SamplerDesc
        {
            Filter = D3D11Native.FilterMinMagMipPoint,
            AddressU = D3D11Native.AddressClamp, AddressV = D3D11Native.AddressClamp, AddressW = D3D11Native.AddressClamp,
            MipLodBias = 0f, MaxAnisotropy = 0, ComparisonFunc = D3D11Native.ComparisonNever,
            BorderR = 0f, BorderG = 0f, BorderB = 0f, BorderA = 0f, MinLod = 0f, MaxLod = float.MaxValue,
        };
        int hrSampler = _createSamplerState(_device, ref sampler, out _samplerState);
        if (hrSampler < 0 || _samplerState == IntPtr.Zero) ThrowHr("CreateSamplerState", hrSampler, D3D11Native.SlotDevCreateSamplerState);
        ValidateIUnknown(_samplerState, "ID3D11SamplerState");

        // 8) 图集:1024² R8G8B8A8 Default + SRV(Tex2D,全部 mip)
        var atlasDesc = new D3D11Native.Texture2DDesc
        {
            Width = AtlasSize, Height = AtlasSize, MipLevels = 1, ArraySize = 1,
            Format = D3D11Native.DxgiFormatR8G8B8A8Unorm,
            Sample = new D3D11Native.SampleDesc { Count = 1, Quality = 0 },
            Usage = D3D11Native.D3D11UsageDefault,
            BindFlags = D3D11Native.BindShaderResource,
        };
        int hrAtlas = _createTexture2D(_device, ref atlasDesc, IntPtr.Zero, out _atlasTexture);
        if (hrAtlas < 0 || _atlasTexture == IntPtr.Zero) ThrowHr("CreateTexture2D(atlas)", hrAtlas, D3D11Native.SlotDevCreateTexture2D);
        ValidateIUnknown(_atlasTexture, "ID3D11Texture2D(atlas)");
        var srvDesc = new D3D11Native.ShaderResourceViewDesc
        {
            Format = D3D11Native.DxgiFormatR8G8B8A8Unorm,
            ViewDimension = D3D11Native.SrvDimensionTexture2D,
            MostDetailedMip = 0, MipLevels = ~0u /* 全部 mip(0xFFFFFFFF)*/,
        };
        int hrSrv = _createSrv(_device, _atlasTexture, ref srvDesc, out _atlasSrv);
        if (hrSrv < 0 || _atlasSrv == IntPtr.Zero) ThrowHr("CreateShaderResourceView(atlas)", hrSrv, D3D11Native.SlotDevCreateShaderResourceView);
        ValidateIUnknown(_atlasSrv, "ID3D11ShaderResourceView(atlas)");

        // 9) 屏幕尺寸常量缓冲(16 字节 Default+BindConstant;float[4] 钉扎直传)
        var cbDesc = new D3D11Native.BufferDesc { ByteWidth = 16, Usage = D3D11Native.D3D11UsageDefault, BindFlags = D3D11Native.BindConstant };
        int hrCb = _createBuffer(_device, ref cbDesc, IntPtr.Zero, out _constantBuffer);
        if (hrCb < 0 || _constantBuffer == IntPtr.Zero) ThrowHr("CreateBuffer(constant)", hrCb, D3D11Native.SlotDevCreateBuffer);
        ValidateIUnknown(_constantBuffer, "ID3D11Buffer(constant)");
    }

    /// <summary>常驻钉扎槽分配 + 一次性状态落地(blend/rasterizer;每绘制状态在 Draw 内)。</summary>
    void AllocPinnedSlots()
    {
        _viewportPin = GCHandle.Alloc(_viewportArr, GCHandleType.Pinned);
        _scissorPin = GCHandle.Alloc(_scissorRect, GCHandleType.Pinned);
        _boxPin = GCHandle.Alloc(_boxArr, GCHandleType.Pinned);
        _rtvPin = GCHandle.Alloc(_rtvSlot, GCHandleType.Pinned);
        _vbSlotPin = GCHandle.Alloc(_vbSlot, GCHandleType.Pinned);
        _cbSlotPin = GCHandle.Alloc(_cbSlot, GCHandleType.Pinned);
        _srvSlotPin = GCHandle.Alloc(_srvSlot, GCHandleType.Pinned);
        _samplerSlotPin = GCHandle.Alloc(_samplerSlot, GCHandleType.Pinned);
        _stridesPin = GCHandle.Alloc(_stridesArr, GCHandleType.Pinned);
        _offsetsPin = GCHandle.Alloc(_offsetsArr, GCHandleType.Pinned);
        _cbPin = GCHandle.Alloc(_cb, GCHandleType.Pinned);
        _cbSlot[0] = _constantBuffer;
        _srvSlot[0] = _atlasSrv;
        _samplerSlot[0] = _samplerState;
        _stridesArr[0] = UiVertex.SizeInBytes;
        _offsetsArr[0] = 0;

        // 持久状态(帧间不变;漏设则 scissor/混合失效)
        _omSetBlendState(_context, _blendState, IntPtr.Zero, 0xFFFFFFFF /*默认全采样掩码*/);
        _rsSetState(_context, _rasterizerState);
    }

    /// <summary>顶点/索引上传缓冲与 scratch 容量保障:首建或超容(×2,同旧 Renderer.EnsureCapacity)时重建。
    /// GPU ByteWidth 与 scratch 字节长度恒等(UpdateSubresource 整块读取的有界性前提);顶点容量向上取整到
    /// 20 的倍数(256KB/20=13107.2,直接截断会短 4 字节)。</summary>
    void EnsureUploadCapacity(int vertexCount, int indexCount)
    {
        uint vbNeed = (uint)vertexCount * UiVertex.SizeInBytes;
        uint ibNeed = (uint)indexCount * sizeof(ushort);
        if (_vertexBuffer == IntPtr.Zero || vbNeed > _vbCapacityBytes)
        {
            if (vbNeed > _vbCapacityBytes) _vbCapacityBytes = Math.Max(_vbCapacityBytes * 2, vbNeed);
            // 无条件圆整到 20 的倍数:首容量 262144/20=13107.2,截断会使 scratch(262140B)< ByteWidth(262144B),
            // 整块上传越界 4 字节(违反类头声明的"scratch 字节容量恒等于 ByteWidth"不变量)
            _vbCapacityBytes = (_vbCapacityBytes + UiVertex.SizeInBytes - 1) / UiVertex.SizeInBytes * UiVertex.SizeInBytes;
            ReleaseOne(ref _vertexBuffer, "ID3D11Buffer(vertex)");
            if (_vbScratchPin.IsAllocated) _vbScratchPin.Free();
            _vbScratch = new UiVertex[_vbCapacityBytes / UiVertex.SizeInBytes];
            _vbScratchPin = GCHandle.Alloc(_vbScratch, GCHandleType.Pinned);
            var desc = new D3D11Native.BufferDesc { ByteWidth = _vbCapacityBytes, Usage = D3D11Native.D3D11UsageDefault, BindFlags = D3D11Native.BindVertex };
            int hr = _createBuffer(_device, ref desc, IntPtr.Zero, out _vertexBuffer);
            if (hr < 0 || _vertexBuffer == IntPtr.Zero) ThrowHr("CreateBuffer(vertex)", hr, D3D11Native.SlotDevCreateBuffer);
            ValidateIUnknown(_vertexBuffer, "ID3D11Buffer(vertex)");
        }
        if (_indexBuffer == IntPtr.Zero || ibNeed > _ibCapacityBytes)
        {
            if (ibNeed > _ibCapacityBytes) _ibCapacityBytes = Math.Max(_ibCapacityBytes * 2, ibNeed);
            ReleaseOne(ref _indexBuffer, "ID3D11Buffer(index)");
            if (_ibScratchPin.IsAllocated) _ibScratchPin.Free();
            _ibScratch = new ushort[_ibCapacityBytes / sizeof(ushort)];
            _ibScratchPin = GCHandle.Alloc(_ibScratch, GCHandleType.Pinned);
            var desc = new D3D11Native.BufferDesc { ByteWidth = _ibCapacityBytes, Usage = D3D11Native.D3D11UsageDefault, BindFlags = D3D11Native.BindIndex };
            int hr = _createBuffer(_device, ref desc, IntPtr.Zero, out _indexBuffer);
            if (hr < 0 || _indexBuffer == IntPtr.Zero) ThrowHr("CreateBuffer(index)", hr, D3D11Native.SlotDevCreateBuffer);
            ValidateIUnknown(_indexBuffer, "ID3D11Buffer(index)");
        }
    }

    // ---- 释放(引用计数公平:逆序 Release 全部 COM 对象,留痕 ReleaseTrace) ----

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FreePin(ref _vbScratchPin); FreePin(ref _ibScratchPin);
        FreePin(ref _viewportPin); FreePin(ref _scissorPin); FreePin(ref _boxPin);
        FreePin(ref _rtvPin); FreePin(ref _vbSlotPin); FreePin(ref _cbSlotPin);
        FreePin(ref _srvSlotPin); FreePin(ref _samplerSlotPin);
        FreePin(ref _stridesPin); FreePin(ref _offsetsPin); FreePin(ref _cbPin);
        FreePin(ref _semPosPin); FreePin(ref _semTexPin); FreePin(ref _semColPin);
        ReleaseOne(ref _stagingTexture, "ID3D11Texture2D(staging)");
        ReleaseOne(ref _indexBuffer, "ID3D11Buffer(index)");
        ReleaseOne(ref _vertexBuffer, "ID3D11Buffer(vertex)");
        ReleaseOne(ref _constantBuffer, "ID3D11Buffer(constant)");
        ReleaseOne(ref _atlasSrv, "ID3D11ShaderResourceView(atlas)");
        ReleaseOne(ref _atlasTexture, "ID3D11Texture2D(atlas)");
        ReleaseOne(ref _samplerState, "ID3D11SamplerState");
        ReleaseOne(ref _rasterizerState, "ID3D11RasterizerState");
        ReleaseOne(ref _blendState, "ID3D11BlendState");
        ReleaseOne(ref _inputLayout, "ID3D11InputLayout");
        ReleaseOne(ref _pixelShader, "ID3D11PixelShader");
        ReleaseOne(ref _vertexShader, "ID3D11VertexShader");
        ReleaseOne(ref _rtv, "ID3D11RenderTargetView");
        ReleaseOne(ref _mainTexture, "ID3D11Texture2D(main)");
        ReleaseOne(ref _backBuffer, "ID3D11Texture2D(backbuffer)");
        ReleaseOne(ref _swapChain, "IDXGISwapChain");
        ReleaseOne(ref _context, "ID3D11DeviceContext");
        ReleaseOne(ref _device, "ID3D11Device");
        ReleaseOne(ref _adapter, "IDXGIAdapter");      // 建链期已释放 → no-op(建链失败路径兜底)
        ReleaseOne(ref _factory, "IDXGIFactory1");    // 同上
    }

    static void FreePin(ref GCHandle pin)
    {
        if (pin.IsAllocated) pin.Free();
        pin = default;
    }

    void ReleaseOne(ref IntPtr obj, string name)
    {
        if (obj == IntPtr.Zero) return;
        uint final = Vfn<ComRelease>(obj, D3D11Native.SlotRelease, $"IUnknown::Release({name})")(obj);
        _releaseTrace.Add((name, final));
        obj = IntPtr.Zero;
    }

    void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(D3D11Context));
    }

    static byte[] AsciiZ(string s)
    {
        byte[] b = new byte[s.Length + 1];   // 尾部 NUL
        Encoding.ASCII.GetBytes(s, 0, s.Length, b, 0);
        return b;
    }

    // ---- 互操作底座(SAFE 模式,照抄 D3D11Spike:ReadIntPtr 读 vtable + GetDelegateForFunctionPointer) ----

    static T Vfn<T>(IntPtr comObj, int slot, string ifaceAndMethod) where T : class
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObj);                       // [self] → vtable
        IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);       // vtable[slot]
        if (fn == IntPtr.Zero)
            throw new InvalidOperationException($"vtable 槽位为空:{ifaceAndMethod}(槽 {slot})");
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    /// <summary>IUnknown 槽位实证:AddRef 返回 n+1、Release 返回 n(差恰为 1 且均 ≥1)即证明两槽位正确。</summary>
    static uint ValidateIUnknown(IntPtr obj, string name)
    {
        uint afterAdd = Vfn<ComAddRef>(obj, D3D11Native.SlotAddRef, "IUnknown::AddRef")(obj);
        uint afterRel = Vfn<ComRelease>(obj, D3D11Native.SlotRelease, "IUnknown::Release")(obj);
        if (afterAdd == 0 || afterRel == 0 || afterAdd != afterRel + 1)
            throw new InvalidOperationException(
                $"IUnknown 槽位实证失败:{name} — AddRef(槽 {D3D11Native.SlotAddRef})={afterAdd}, " +
                $"Release(槽 {D3D11Native.SlotRelease})={afterRel}(须差 1 且 ≥1)");
        return afterRel;
    }

    static void ThrowHr(string what, int hr, int slot)
        => throw new InvalidOperationException($"{what} 失败 hr=0x{hr:X8}" + (slot >= 0 ? $"(槽 {slot})" : ""));

    // ---- COM 委托(StdCall 为 x64 Windows COM 调用约定;数组形参封送为首元素指针) ----

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ComAddRef(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ComRelease(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdaptersFn(IntPtr self, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateSwapChainFn(IntPtr self, IntPtr device, ref D3D11Native.SwapChainDesc desc, out IntPtr swapChain);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferFn(IntPtr self, uint buffer, ref Guid riid, out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ResizeBuffersFn(IntPtr self, uint bufferCount, uint width, uint height, uint format, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PresentFn(IntPtr self, uint syncInterval, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateBufferFn(IntPtr self, ref D3D11Native.BufferDesc desc, IntPtr initialData, out IntPtr buffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DFn(IntPtr self, ref D3D11Native.Texture2DDesc desc, IntPtr initialData, out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateShaderResourceViewFn(IntPtr self, IntPtr resource, ref D3D11Native.ShaderResourceViewDesc desc, out IntPtr srv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateRenderTargetViewFn(IntPtr self, IntPtr resource, IntPtr desc, out IntPtr rtv);

    // 注意:CreateInputLayout 无 pClassLinkage 形参(与 Create*Shader 不同,本机 d3d11.h/MSDN 实证)—
    // 多传一个 IntPtr.Zero 会使原生端把 ppInputLayout 读成 NULL → "仅校验"模式返回 S_FALSE 且不写出对象。
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateInputLayoutFn(IntPtr self, IntPtr elementDescs, uint numElements, IntPtr bytecode, UIntPtr bytecodeLength, out IntPtr layout);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateShaderFn(IntPtr self, IntPtr bytecode, UIntPtr bytecodeLength, IntPtr linkage, out IntPtr shader);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateBlendStateFn(IntPtr self, ref D3D11Native.BlendDesc desc, out IntPtr state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateRasterizerStateFn(IntPtr self, ref D3D11Native.RasterizerDesc desc, out IntPtr state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateSamplerStateFn(IntPtr self, ref D3D11Native.SamplerDesc desc, out IntPtr state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void VssSetConstantBuffersFn(IntPtr self, uint startSlot, uint numBuffers, IntPtr buffers);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PssSetShaderResourcesFn(IntPtr self, uint startSlot, uint numViews, IntPtr views);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PssSetShaderFn(IntPtr self, IntPtr shader, IntPtr classInstances, uint numClassInstances);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PssSetSamplersFn(IntPtr self, uint startSlot, uint numSamplers, IntPtr samplers);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void VssSetShaderFn(IntPtr self, IntPtr shader, IntPtr classInstances, uint numClassInstances);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawIndexedFn(IntPtr self, uint indexCount, uint startIndex, int baseVertex);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapFn(IntPtr self, IntPtr resource, uint subresource, uint mapType, uint mapFlags, out D3D11Native.MappedSubresource mapped);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapFn(IntPtr self, IntPtr resource, uint subresource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IaSetInputLayoutFn(IntPtr self, IntPtr layout);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IaSetVertexBuffersFn(IntPtr self, uint startSlot, uint numBuffers, IntPtr buffers, IntPtr strides, IntPtr offsets);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IaSetIndexBufferFn(IntPtr self, IntPtr buffer, uint format, uint offset);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IaSetPrimitiveTopologyFn(IntPtr self, uint topology);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void OmSetRenderTargetsFn(IntPtr self, uint numViews, IntPtr views, IntPtr depthStencilView);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void OmSetBlendStateFn(IntPtr self, IntPtr state, IntPtr blendFactor, uint sampleMask);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RsSetStateFn(IntPtr self, IntPtr state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RsSetViewportsFn(IntPtr self, uint numViewports, IntPtr viewports);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RsSetScissorRectsFn(IntPtr self, uint numRects, IntPtr rects);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceFn(IntPtr self, IntPtr dstResource, IntPtr srcResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UpdateSubresourceFn(IntPtr self, IntPtr dstResource, uint dstSubresource, IntPtr pDstBox, IntPtr pSrcData, uint srcRowPitch, uint srcDepthPitch);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClearRenderTargetViewFn(IntPtr self, IntPtr rtv, float[] color);
}
