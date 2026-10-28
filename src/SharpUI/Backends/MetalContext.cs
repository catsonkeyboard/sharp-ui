using System.Runtime.InteropServices;
using System.Text;

namespace SharpUI.Backends;

/// <summary>
/// X-3/X-4 T3:Metal 后端(headless 离屏路径)。headless = 独立 CAMetalLayer(不挂 NSView,
/// 只接 device/drawableSize —— CI 上先行验证 CAMetalLayer 布线,为窗口路径同构前奏)+ 自建
/// MTLTextureDescriptor RGBA8Unorm 离屏目标(每帧 encoder 渲到该 texture,getBytes 读回);
/// 窗口路径 = CreateForWindow(sdlWindow) 预留(SDL macOS NSView.layer 接线归 M3e 后半 CI 落地,
/// 签名已立 + NotImplementedException)。全部 IRenderContext 成员语义与 D3D11/GL 同。
/// 逐步 API 序以 iPhoneOS16.5.sdk Metal.framework Headers 实数核对(与 macOS Metal.h 同源同面;
/// 枚举常量出处见 MetalNative 常量区);**全部消息收发待 CI macOS 实机验证**(本机无 Metal 运行时,
/// ObjectiveC/MetalNative.Ensure 即抛,Windows 上本类仅编译结构验证 + env 门控测试空过)。
/// 纪律:零 unsafe、NativeAOT 兼容、绘制路径零堆分配(调用者数组经 MemoryMarshal.AsBytes +
/// Marshal.Copy(span→contents) 直写 Shared 缓冲,无 D3D11 scratch/GL BufferSubData 之需 ——
/// Shared 缓冲即 CPU 可写存储,上传量恒等于当帧实际字节)。
/// 帧模型(与 GL 的即时模式、D3D11 的即时上下文之差):Metal 是编码-提交模型 —— BeginFrame
/// 起一段 [queue commandBuffer] + renderCommandEncoder,Draw/SetScissor 逐条编码,**编码中的
/// 命令在下一 BeginFrame/ReadBack/Dispose 的 FlushFrame 处 endEncoding+commit 落地**;headless
/// Present 严格 no-op(接口契约;渲染结果的提交同步由读回路径负责,不取 drawable)。
/// Y 轴/行序:Metal NDC 同 +1=上沿(MSL 源与 HLSL/GLSL 同式,无 GL 式窗口原点差异);纹理
/// (0,0)=左上、行序自顶向下,MTLScissorRect 原点同为左上 —— 三处 GL 特有翻转(scissor Y 翻/
/// readPixels 行翻/着色器不翻)在 Metal 全部不存在,直接按目标像素坐标落地。
/// </summary>
internal sealed class MetalContext : IRenderContext
{
    /// <summary>图集边长(IRenderContext 契约:构造期显式上传全零初始内容 —— Shared 图集
    /// replaceRegion 前 GPU 内存为未定义,不得依赖清零;字形 padding 依赖透明零)。</summary>
    public uint AtlasSize => 1024;

    public uint TargetWidth { get; private set; }
    public uint TargetHeight { get; private set; }

    bool _disposed;

    // ---- Metal 对象(new*/C 导出 +1 持有,Dispose 经 SelRelease 配对; autorelease 族 +0 不持) ----
    IntPtr _device;                 // MTLCreateSystemDefaultDevice(C 导出,调用方持有 —— 头文件注)
    IntPtr _layer;                  // CAMetalLayer.new(+1;headless 布线件,不取 drawable)
    IntPtr _queue;                  // newCommandQueue(+1)
    IntPtr _libraryVs;              // newLibraryWithSource:options:error:(+1;顶点源库)
    IntPtr _libraryFs;              // 同上(片段源库 —— 两源各自成库,免拼接手术)
    IntPtr _vsFunction;             // newFunctionWithName:(+1)
    IntPtr _fsFunction;             // 同上
    IntPtr _pipelineState;          // newRenderPipelineStateWithDescriptor:error:(+1)
    IntPtr _sampler;                // newSamplerStateWithDescriptor:(+1)
    IntPtr _atlasTexture;           // newTextureWithDescriptor:(+1;1024² Shared)
    IntPtr _targetTexture;          // 同上(离屏主目标,Resize 重建)
    IntPtr _vertexBuffer;           // newBufferWithLength:options:(+1;容量 ×2 增长即重建)
    IntPtr _indexBuffer;            // 同上
    IntPtr _constantBuffer;         // 同上(16B 屏幕尺寸常量,写前 8B)
    IntPtr _passDescriptor;         // MTLRenderPassDescriptor.new(+1;每帧改写 attachment 0 后建 encoder)

    // ---- 当前帧编码态(commandBuffer/encoder 为 +0 autorelease,字段仅记账不作所有权) ----
    IntPtr _commandBuffer;
    IntPtr _encoder;

    // ---- 上传缓冲容量(首 256KB/512KB,×2 增长,同 GL/D3D11;Metal 缓冲不可重分配,超容重建) ----
    uint _vbCapacityBytes = 256 * 1024, _ibCapacityBytes = 512 * 1024;

    /// <summary>屏幕尺寸常量(MSL constant float2&amp; 槽 0;写 Shared 缓冲前 8 字节,
    /// Marshal.Copy float[] 重载零钉扎)。 </summary>
    readonly float[] _cb = new float[2];

    // ---- 工厂 ----

    /// <summary>headless 离屏上下文(像素验证):独立 CAMetalLayer + 自建 RGBA8Unorm 目标;
    /// 不创建窗口(与 GL headless 的隐藏窗之差:离屏渲染不经窗口系统)。尺寸为 0 抛
    /// ArgumentException(同 D3D11/GL)。</summary>
    public static MetalContext CreateHeadless(uint width, uint height)
    {
        ValidateSize(width, height);
        return new MetalContext(width, height);
    }

    /// <summary>窗口渲染上下文:预留签名(SDL macOS NSView.layer 接 CAMetalLayer +
    /// nextDrawable/present)。归 M3e 后半 CI 落地;headless 路径先行实证本类其余全部成员。</summary>
    public static MetalContext CreateForWindow(IntPtr sdlWindow, uint width, uint height)
    {
        if (sdlWindow == IntPtr.Zero) throw new ArgumentException("sdlWindow 不能为空", nameof(sdlWindow));
        ValidateSize(width, height);
        throw new NotImplementedException(
            "待 M3e 后半:SDL macOS NSView 接 CAMetalLayer(headless 离屏路径已落地,窗口路径 CI macOS 实机接线)");
    }

    static void ValidateSize(uint width, uint height)
    {
        if (width == 0 || height == 0)
            throw new ArgumentException($"目标尺寸必须为正(width={width}, height={height})");
    }

    MetalContext(uint width, uint height)
    {
        TargetWidth = width;
        TargetHeight = height;
        try
        {
            CreatePipeline();
            CreateTarget(width, height);
            // IRenderContext.AtlasSize 契约:构造期显式上传全零(replaceRegion 4MB 一次性;
            // Shared 图集初值未定义,字形 padding 依赖透明零 —— GL/D3D11 同款落点)
            UpdateAtlasRegion(new byte[AtlasSize * AtlasSize * 4], 0, 0, AtlasSize, AtlasSize);
        }
        catch
        {
            Dispose();   // 构造失败路径:释放尽力,不掩盖原始异常(同 GL)
            throw;
        }
    }

    // ---- IRenderContext ----

    /// <summary>目标尺寸变化通知:headless 离屏 texture 无法"长出"像素 —— 尺寸变化即按新尺寸重建
    /// 目标(与 GL headless"仅记录"之差:GL 隐藏窗 viewport 随记录尺寸即时生效,Metal 须换 texture;
    /// 属性语义两后端一致,由镜像测试锁定)。不接受 0(同 D3D11/GL 语义)。下一 BeginFrame 经
    /// renderPassDescriptor setTexture: 重指新目标。</summary>
    public void Resize(uint width, uint height)
    {
        ThrowIfDisposed();
        ValidateSize(width, height);
        if (width != TargetWidth || height != TargetHeight)
            CreateTarget(width, height);   // 旧目标 release;在途已提交命令缓冲自持引用,安全
        TargetWidth = width;
        TargetHeight = height;
    }

    /// <summary>图集区域上传(RGBA 字节,左上原点 —— Metal 纹理行 0=首行,与 UV (0,0) 对应,无翻转):
    /// pin rgba + replaceRegion:mipmapLevel:withBytes:bytesPerRow:(MTLRegion 按值)。
    /// Shared 图集的 CPU 直写语义:与在途 GPU 采样并发属竞态(ImGui-Metal 同款逐帧上传模式,
    /// 帧边界使用安全;CI 实测异常再议 in-flight 栅栏)。</summary>
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
            var region = new MetalNative.Region { X = x, Y = y, Z = 0, Width = width, Height = height, Depth = 1 };
            ObjectiveC.MsgSendVoidRegionNUIntPtrNUInt(_atlasTexture, MetalNative.SelReplaceRegion,
                region, 0, pin.AddrOfPinnedObject(), (nuint)(width * 4));
        }
        finally { pin.Free(); }
    }

    /// <summary>帧起始:FlushFrame 提交上一帧 → 改写 renderPassDescriptor(attachment 0:
    /// setTexture: 重指当前目标[Resize 重建后重落位]+ setClearColor: 4×double HFA)→
    /// [queue commandBuffer](+0)→ renderCommandEncoderWithDescriptor:(+0,encoder 创建即
    /// snapshot pass 状态,clear 按 loadAction=Clear 在此刻生效)→ 管线/图集纹理/采样器/
    /// 常量缓冲(slot 0,BindVertexBuffer 三参统一落位点)落位。
    /// 清屏-scissor 语义:loadAction 清屏不受 scissor 作用(Metal 规约,clear 属 pass 装载),
    /// 每帧新 encoder 的默认 scissor/viewport 即全目标 —— GLContext.BeginFrame 的
    /// Disable(SCISSOR) 前置(残留 scissor 裁 glClear)在 Metal 由"每帧新 encoder"天然达成。
    /// 混合:管线创建期固化(One, OneMinusSourceAlpha ×RGB/Alpha,对位 D3D11/GL);Metal 无
    /// 运行时 Enable(Blend) 对应物。</summary>
    public void BeginFrame(float r, float g, float b, float a)
    {
        ThrowIfDisposed();
        FlushFrame(waitForCompletion: false);
        IntPtr attachments = ObjectiveC.MsgSend(_passDescriptor, MetalNative.SelColorAttachments);
        IntPtr att = ObjectiveC.MsgSendNUInt(attachments, MetalNative.SelObjectAtIndexedSubscript, 0);
        ObjectiveC.MsgSendVoidPtr(att, MetalNative.SelSetTexture, _targetTexture);
        ObjectiveC.MsgSendVoidDouble4(att, MetalNative.SelSetClearColor, r, g, b, a);
        _commandBuffer = ObjectiveC.MsgSend(_queue, MetalNative.SelCommandBuffer);
        if (_commandBuffer == IntPtr.Zero)
            throw new InvalidOperationException("[commandBuffer] 返回 nil(commandQueue 不可用)—— 待 CI macOS 实机验证");
        _encoder = ObjectiveC.MsgSendPtr(_commandBuffer, MetalNative.SelRenderCommandEncoder, _passDescriptor);
        if (_encoder == IntPtr.Zero)
            throw new InvalidOperationException("renderCommandEncoderWithDescriptor: 返回 nil —— 待 CI macOS 实机验证");
        ObjectiveC.MsgSendVoidPtr(_encoder, MetalNative.SelSetRenderPipelineState, _pipelineState);
        ObjectiveC.MsgSendVoidPtrNUInt(_encoder, MetalNative.SelSetFragmentTexture, _atlasTexture, 0);
        ObjectiveC.MsgSendVoidPtrNUInt(_encoder, MetalNative.SelSetFragmentSamplerState, _sampler, 0);
        BindVertexBuffer(_constantBuffer, 0);   // 常量独占 slot 0 —— 三参统一落位点(评审 Critical#1 勘正,见 BindVertexBuffer)
    }

    /// <summary>setVertexBuffer:offset:atIndex: **三实参**统一落位点(评审 Critical#1 勘正):selector
    /// 为 (buffer, offset, index) 三参,须 MsgSendVoidPtrNUIntNUInt —— 常量(slot 0,BeginFrame)与
    /// 几何(slot 1,Draw)两落位收拢至此单点,selector↔包装配对仅此一处,两参包装误用在结构上
    /// 不再可现(原 BeginFrame 两参形态:x3 槽读 0 被当 offset 碰巧无害,x4=index 槽读到未定义
    /// 残留值 → 常量绑随机槽、MSL uSize [[buffer(0)]] 落空,macOS 首帧即校验失败/几何全错)。
    /// offset 恒 0(整缓冲上传,无子区偏移需求)。**静态锚**:本方法是 SelSetVertexBuffer 的唯一
    /// 消费点(与 Draw 的几何落位同型同参三形态);门控测试无法在本机实跑,此收拢即防回归面。</summary>
    void BindVertexBuffer(IntPtr buffer, nuint index) =>
        ObjectiveC.MsgSendVoidPtrNUIntNUInt(_encoder, MetalNative.SelSetVertexBuffer, buffer, 0, index);

    /// <summary>顶点着色器屏幕尺寸常量(逻辑像素):MSL `constant float2&amp; uSize [[buffer(0)]]`
    /// —— 写 constantBuffer(Shared)前 8 字节。缓冲对象帧首已绑 slot 0(见 BeginFrame),
    /// 此处只写 contents(对在途已提交缓冲的写并发属 Shared 缓冲标准竞态面,帧边界使用安全,
    /// CI 实测背书;MTLBuffer.h:didModifyRange 仅 Managed 需要,Shared 写即 CPU 可见)。</summary>
    public void SetScreenSize(float logicalWidth, float logicalHeight)
    {
        ThrowIfDisposed();
        _cb[0] = logicalWidth;
        _cb[1] = logicalHeight;
        IntPtr contents = ObjectiveC.MsgSend(_constantBuffer, MetalNative.SelContents);
        Marshal.Copy(_cb, 0, contents, 2);   // 2×float = 8B,零钉扎(重载内部瞬时固定)
    }

    /// <summary>剪裁矩形(目标像素坐标,左上原点 —— MTLScissorRect 同约定,无 GL 式 Y 翻转):
    /// setScissorRect: 按值结构体。恒显式落位(全开矩形亦然):Metal encoder 默认 scissor=全目标,
    /// 但部分剪裁后回全开须显式复位,统一路径免分支;GLContext 的"全开即 Disable"优化为 GL
    /// 特有(clear-scissor 交互),Metal 无对应面。**时序约束(Metal 校验层)**:须在任何
    /// draw 之前调用 —— Renderer 契约(SetScissor 先于 Draw)天然满足。</summary>
    public void SetScissor(uint x, uint y, uint w, uint h)
    {
        ThrowIfDisposed();
        EnsureEncoder();
        ObjectiveC.MsgSendVoidScissorRect(_encoder, MetalNative.SelSetScissorRect,
            new MetalNative.ScissorRect { X = x, Y = y, Width = w, Height = h });   // uint→nuint 隐式 widening
    }

    /// <summary>绘制一段索引几何(零堆分配):顶点前缀 [0..vertexCount) + 索引前缀
    /// [0..startIndex+indexCount) 钉扎调用者数组 + MetalNative.CopyMemory(libc memcpy,对位
    /// D3D11Native.CopyMemory)直写 Shared 缓冲 contents(上传量=当帧实际字节,无越界面 ——
    /// GL BufferSubData 同款语义;D3D11 的整块 scratch 与重定基在此均无之需)。绘制取
    /// **drawIndexedPrimitives:indexCount:indexType:indexBuffer:indexBufferOffset:** 原生 16 位
    /// 索引 + 字节偏移 startIndex×2 —— 对位 GL DrawElements 原生偏移路径(简报"索引缓冲 32 位
    /// 或 16→32 展开,实现者定":取原生 16 位形免逐帧展开拷贝/顶点重复,索引即接口层
    /// ushort[] 零转换;头文件 5 参便捷形,MetalNative.SelDrawIndexedPrimitives 出处注)。
    /// 缓冲前缀镜像不变量与 GL 同:分段所引索引恒新鲜上传,无陈旧尾段引用。几何缓冲绑
    /// **slot 1**(slot 0=常量,顶点描述符 attributes bufferIndex=1 与之错开 —— MSL
    /// uSize [[buffer(0)]] 与 stage_in 装配共顶点缓冲表,同槽即冲突,见 CreatePipeline 注);
    /// 容量增长重建的缓冲对象每 Draw 重绑,不受影响。</summary>
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
        EnsureEncoder();

        EnsureCapacity(vertexCount, checked((int)(startIndex + indexCount)));
        IntPtr vbContents = ObjectiveC.MsgSend(_vertexBuffer, MetalNative.SelContents);
        IntPtr ibContents = ObjectiveC.MsgSend(_indexBuffer, MetalNative.SelContents);
        GCHandle vp = GCHandle.Alloc(vertices, GCHandleType.Pinned);
        GCHandle ip = GCHandle.Alloc(indices, GCHandleType.Pinned);
        try
        {
            MetalNative.CopyMemory(vbContents, vp.AddrOfPinnedObject(),
                (nuint)((long)vertexCount * UiVertex.SizeInBytes));
            MetalNative.CopyMemory(ibContents, ip.AddrOfPinnedObject(),
                (nuint)((long)(startIndex + indexCount) * sizeof(ushort)));
        }
        finally { ip.Free(); vp.Free(); }   // 句柄表操作,无 GC 堆分配(同 GL/D3D11 Draw 注)

        BindVertexBuffer(_vertexBuffer, 1);   // 几何 slot 1 —— 与常量落位同走三参统一落位点(评审 Critical#1 锚)
        ObjectiveC.MsgSendVoidNUIntNUIntNUIntPtrNUInt(_encoder, MetalNative.SelDrawIndexedPrimitives,
            MetalNative.PrimitiveTypeTriangle, indexCount, MetalNative.IndexTypeUInt16,
            _indexBuffer, (nuint)(startIndex * sizeof(ushort)));
    }

    /// <summary>呈现:headless 严格 no-op(接口契约;不取 CAMetalLayer drawable,离屏 texture 供
    /// 读回;编码中命令的提交时点见类头帧模型 —— 下一 BeginFrame/ReadBack/Dispose 落地)。
    /// 窗口路径 M3e 后半接 [commandBuffer presentDrawable:] + [CAMetalLayer nextDrawable]。</summary>
    public void Present()
    {
        ThrowIfDisposed();
    }

    /// <summary>读回主目标(GPU 测试用):FlushFrame(wait=true)—— endEncoding + commit +
    /// waitUntilCompleted(getBytes 前保证 GPU 完成;+0 commandBuffer 引用在本调用栈存续期内
    /// 无 pool 排空风险)→ getBytes:bytesPerRow:fromRegion:mipmapLevel: 全帧。**行序/字节序**:
    /// Metal 纹理行 0=顶行、原点左上,getBytes 自顶向下逐行 —— 与 GL ReadBack 翻转后同向,
    /// **无需再翻**(简报括注"行序上翻同 GL"指产出同向,非字面翻转);RGBA8Unorm 字节序
    /// R,G,B,A 同 GL 断言形(D3D11 BGRA 异)。测试路径,分配允许。</summary>
    public byte[] ReadBackMainFramebuffer()
    {
        ThrowIfDisposed();
        FlushFrame(waitForCompletion: true);
        int w = (int)TargetWidth, h = (int)TargetHeight;
        byte[] pixels = new byte[w * h * 4];
        GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var region = new MetalNative.Region { X = 0, Y = 0, Z = 0, Width = (nuint)w, Height = (nuint)h, Depth = 1 };
            ObjectiveC.MsgSendVoidGetBytes(_targetTexture, MetalNative.SelGetBytes,
                pin.AddrOfPinnedObject(), (nuint)(w * 4), region, 0);
        }
        finally { pin.Free(); }
        return pixels;
    }

    // ---- 管线构建(一次性;全部 API 序出处见 MetalNative selector/常量注释) ----

    void CreatePipeline()
    {
        MetalNative.Ensure();   // macOS 守卫 + 全量 selector 注册(非 macOS 即抛,失败面前置)

        // 1) 设备:MTLCreateSystemDefaultDevice(C 导出;调用方持有 +1 —— MTLDevice.h 头注)。
        //    无 Metal 能力 GPU 返回 nil → 显式失败(判降级归调用方,本类不静默换后端)。
        _device = MetalNative.MTLCreateSystemDefaultDevice();
        if (_device == IntPtr.Zero)
            throw new InvalidOperationException("MTLCreateSystemDefaultDevice 返回 nil(无 Metal 能力设备)—— 待 CI macOS 实机验证");

        // 2) 离屏 CAMetalLayer(独立实例,不挂 NSView):setDevice: + setDrawableSize:(CGSize=2×double)。
        //    headless 渲染不经 drawable(目标=步 6 自建 texture),layer 仅布线件 —— 窗口路径(M3e 后半)
        //    将同款 layer 接 NSView.layer;此处先行验证 QuartzCore 类可达与属性收发。
        IntPtr layerClass = ObjectiveC.GetClass("CAMetalLayer");
        if (layerClass == IntPtr.Zero)
            throw new InvalidOperationException("objc_getClass(\"CAMetalLayer\") 返回 nil(QuartzCore 未注册该类)—— 待 CI macOS 实机验证");
        _layer = ObjectiveC.MsgSend(layerClass, MetalNative.SelNew);
        ObjectiveC.MsgSendVoidPtr(_layer, MetalNative.SelSetDevice, _device);
        ObjectiveC.MsgSendVoidDouble2(_layer, MetalNative.SelSetDrawableSize,
            (double)TargetWidth, (double)TargetHeight);

        // 3) 命令队列:newCommandQueue(+1)。
        _queue = ObjectiveC.MsgSend(_device, MetalNative.SelNewCommandQueue);
        if (_queue == IntPtr.Zero)
            throw new InvalidOperationException("newCommandQueue 返回 nil —— 待 CI macOS 实机验证");

        // 4) 着色器库:两 MSL 源各自成库(免拼接手术)—— stringWithUTF8String:(类方法,+0 autorelease;
        //    建库期一次性,无 pool 泄漏容忍 —— 类头所有权注)→ newLibraryWithSource:options:nil
        //    error:.MsgSendNSErrorWithOptions 契约:仅返回 nil 时 error 槽有效,失败判定只看返回值
        //    (error 对象描述提取从简 —— 不再引入 localizedDescription 面,CI 落地后按需补)。
        _libraryVs = CompileLibrary(Shaders.MslVertexSource, "vertex");
        _libraryFs = CompileLibrary(Shaders.MslFragmentSource, "fragment");
        _vsFunction = GetFunction(_libraryVs, "vs_main");
        _fsFunction = GetFunction(_libraryFs, "fs_main");

        // 5) 渲染管线:MTLRenderPipelineDescriptor.new(+1,建毕即释放 —— 一次性描述符不驻留)。
        //    顶点描述符(attributes 0/1/2 对位 MSL [[attribute(N)]],**bufferIndex=1**:几何缓冲与
        //    MSL uSize [[buffer(0)]] 共用顶点缓冲表,同槽即冲突 —— 几何让位 slot 1,常量独占 slot 0):
        //      attr 0: Float2   @0  (X,Y)      attr 1: Float2   @8  (U,V)
        //      attr 2: UChar4Normalized @16 (RGBA 归一化,内存直读)
        //      layout 1: stride=20 + stepFunction=PerVertex + stepRate=1(步进显式落定,不赌默认)。
        //    颜色附件 0(colorAttachments 只读 getter + objectAtIndexedSubscript:0 —— setColorAttachments:
        //    不存在,T2 头文件实证):RGBA8Unorm + 混合固化(One/OneMinusSourceAlpha/Add ×RGB/Alpha,
        //    对位 D3D11 BlendDesc 与 GL BlendFunc —— Metal 无运行时开关,随管线)。
        //    newRenderPipelineStateWithDescriptor:error:(+1;MsgSendNSError 契约同上)。
        IntPtr pipelineDesc = ObjectiveC.MsgSend(GetClassOrThrow("MTLRenderPipelineDescriptor"), MetalNative.SelNew);
        try
        {
            ObjectiveC.MsgSendVoidPtr(pipelineDesc, MetalNative.SelSetVertexFunction, _vsFunction);
            ObjectiveC.MsgSendVoidPtr(pipelineDesc, MetalNative.SelSetFragmentFunction, _fsFunction);

            IntPtr vertexDesc = ObjectiveC.MsgSend(GetClassOrThrow("MTLVertexDescriptor"), MetalNative.SelNew);
            try
            {
                IntPtr attrs = ObjectiveC.MsgSend(vertexDesc, MetalNative.SelAttributes);
                SetVertexAttribute(attrs, 0, MetalNative.VertexFormatFloat2, 0);
                SetVertexAttribute(attrs, 1, MetalNative.VertexFormatFloat2, 8);
                SetVertexAttribute(attrs, 2, MetalNative.VertexFormatUChar4Normalized, 16);
                IntPtr layouts = ObjectiveC.MsgSend(vertexDesc, MetalNative.SelLayouts);
                IntPtr layout1 = ObjectiveC.MsgSendNUInt(layouts, MetalNative.SelObjectAtIndexedSubscript, 1);
                ObjectiveC.MsgSendVoidNUInt(layout1, MetalNative.SelSetStride, UiVertex.SizeInBytes);
                ObjectiveC.MsgSendVoidNUInt(layout1, MetalNative.SelSetStepFunction, MetalNative.VertexStepFunctionPerVertex);
                ObjectiveC.MsgSendVoidNUInt(layout1, MetalNative.SelSetStepRate, 1);
                ObjectiveC.MsgSendVoidPtr(pipelineDesc, MetalNative.SelSetVertexDescriptor, vertexDesc);
            }
            finally { Release(ref vertexDesc); }

            IntPtr attachments = ObjectiveC.MsgSend(pipelineDesc, MetalNative.SelColorAttachments);
            IntPtr att = ObjectiveC.MsgSendNUInt(attachments, MetalNative.SelObjectAtIndexedSubscript, 0);
            ObjectiveC.MsgSendVoidNUInt(att, MetalNative.SelSetPixelFormat, MetalNative.PixelFormatRgba8Unorm);
            ObjectiveC.MsgSendVoidNUInt(att, MetalNative.SelSetBlendingEnabled, 1);   // BOOL=_Bool:低字节即位
            ObjectiveC.MsgSendVoidNUInt(att, MetalNative.SelSetSourceRGBBlendFactor, MetalNative.BlendFactorOne);
            ObjectiveC.MsgSendVoidNUInt(att, MetalNative.SelSetDestinationRGBBlendFactor, MetalNative.BlendFactorOneMinusSourceAlpha);
            ObjectiveC.MsgSendVoidNUInt(att, MetalNative.SelSetRgbBlendOperation, MetalNative.BlendOperationAdd);
            ObjectiveC.MsgSendVoidNUInt(att, MetalNative.SelSetSourceAlphaBlendFactor, MetalNative.BlendFactorOne);
            ObjectiveC.MsgSendVoidNUInt(att, MetalNative.SelSetDestinationAlphaBlendFactor, MetalNative.BlendFactorOneMinusSourceAlpha);
            ObjectiveC.MsgSendVoidNUInt(att, MetalNative.SelSetAlphaBlendOperation, MetalNative.BlendOperationAdd);

            _pipelineState = ObjectiveC.MsgSendNSError(_device, MetalNative.SelNewRenderPipelineState,
                pipelineDesc, out IntPtr error);
            if (_pipelineState == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"newRenderPipelineStateWithDescriptor:error: 失败(error 槽 0x{error:X};返回 nil 时槽有效)—— 待 CI macOS 实机验证");
        }
        finally { Release(ref pipelineDesc); }

        // 6) 采样器:MTLSamplerDescriptor(一次性)Nearest×2 + ClampToEdge×2(对位 GL NEAREST/
        //    CLAMP_TO_EDGE、D3D11 POINT/CLAMP)→ newSamplerStateWithDescriptor:(+1)。
        IntPtr samplerDesc = ObjectiveC.MsgSend(GetClassOrThrow("MTLSamplerDescriptor"), MetalNative.SelNew);
        try
        {
            ObjectiveC.MsgSendVoidNUInt(samplerDesc, MetalNative.SelSetMinFilter, MetalNative.SamplerMinMagFilterNearest);
            ObjectiveC.MsgSendVoidNUInt(samplerDesc, MetalNative.SelSetMagFilter, MetalNative.SamplerMinMagFilterNearest);
            ObjectiveC.MsgSendVoidNUInt(samplerDesc, MetalNative.SelSetSAddressMode, MetalNative.SamplerAddressModeClampToEdge);
            ObjectiveC.MsgSendVoidNUInt(samplerDesc, MetalNative.SelSetTAddressMode, MetalNative.SamplerAddressModeClampToEdge);
            _sampler = ObjectiveC.MsgSendPtr(_device, MetalNative.SelNewSamplerStateWithDescriptor, samplerDesc);
            if (_sampler == IntPtr.Zero)
                throw new InvalidOperationException("newSamplerStateWithDescriptor: 返回 nil —— 待 CI macOS 实机验证");
        }
        finally { Release(ref samplerDesc); }

        // 7) 图集:MTLTextureDescriptor RGBA8Unorm 1024² Shared + ShaderRead(+1;初始零上传在构造尾部)。
        _atlasTexture = CreateTexture(AtlasSize, AtlasSize, MetalNative.TextureUsageShaderRead);
        if (_atlasTexture == IntPtr.Zero)
            throw new InvalidOperationException("newTextureWithDescriptor:(atlas) 返回 nil —— 待 CI macOS 实机验证");

        // 8) Shared 缓冲三件(newBufferWithLength:options: 0=Shared+DefaultCache,+1):
        //    顶点/索引(容量 ×2 增长重建)+ 屏幕尺寸常量(16B 槽写前 8B —— MSL 只读 float2)。
        _vertexBuffer = CreateBuffer(_vbCapacityBytes);
        _indexBuffer = CreateBuffer(_ibCapacityBytes);
        _constantBuffer = CreateBuffer(16);
        if (_vertexBuffer == IntPtr.Zero || _indexBuffer == IntPtr.Zero || _constantBuffer == IntPtr.Zero)
            throw new InvalidOperationException("newBufferWithLength:options: 返回 nil —— 待 CI macOS 实机验证");

        // 9) renderPassDescriptor(+1 驻留,每帧改写):attachment 0 = 离屏目标 + LoadAction=Clear +
        //    StoreAction=Store(读回目标必须 Store —— DontCare 会弃内容;T2 注释勘正后常量
        //    StoreActionStore=1,见 MetalNative)。clearColor 每帧于 BeginFrame 落位。
        _passDescriptor = ObjectiveC.MsgSend(GetClassOrThrow("MTLRenderPassDescriptor"), MetalNative.SelNew);
        IntPtr passAtt = ObjectiveC.MsgSendNUInt(
            ObjectiveC.MsgSend(_passDescriptor, MetalNative.SelColorAttachments),
            MetalNative.SelObjectAtIndexedSubscript, 0);
        ObjectiveC.MsgSendVoidNUInt(passAtt, MetalNative.SelSetLoadAction, MetalNative.LoadActionClear);
        ObjectiveC.MsgSendVoidNUInt(passAtt, MetalNative.SelSetStoreAction, MetalNative.StoreActionStore);
        // setTexture: 延后至 BeginFrame —— CreateTarget 在步 10(构造序)才产目标,Resize 亦重建。
    }

    /// <summary>离屏主目标:RGBA8Unorm + RenderTarget + Shared(渲染+getBytes 读回;Shared 取舍与
    /// 离散卡退路见 MetalNative.StorageModeShared 注)。旧目标 release 后换新;已提交命令缓冲
    /// 对旧目标自持引用,release 仅撤本方所有权。</summary>
    void CreateTarget(uint width, uint height)
    {
        IntPtr texture = CreateTexture(width, height, MetalNative.TextureUsageRenderTarget);
        if (texture == IntPtr.Zero)
            throw new InvalidOperationException($"newTextureWithDescriptor:(target {width}x{height}) 返回 nil —— 待 CI macOS 实机验证");
        Release(ref _targetTexture);
        _targetTexture = texture;
    }

    /// <summary>newTextureWithDescriptor:(+1;描述符一次性,建毕释放)。</summary>
    IntPtr CreateTexture(uint width, uint height, nuint usage)
    {
        IntPtr desc = ObjectiveC.MsgSend(GetClassOrThrow("MTLTextureDescriptor"), MetalNative.SelNew);
        try
        {
            ObjectiveC.MsgSendVoidNUInt(desc, MetalNative.SelSetPixelFormat, MetalNative.PixelFormatRgba8Unorm);
            ObjectiveC.MsgSendVoidNUInt(desc, MetalNative.SelSetWidth, width);
            ObjectiveC.MsgSendVoidNUInt(desc, MetalNative.SelSetHeight, height);
            ObjectiveC.MsgSendVoidNUInt(desc, MetalNative.SelSetUsage, usage);
            ObjectiveC.MsgSendVoidNUInt(desc, MetalNative.SelSetStorageMode, MetalNative.StorageModeShared);
            return ObjectiveC.MsgSendPtr(_device, MetalNative.SelNewTextureWithDescriptor, desc);
        }
        finally { Release(ref desc); }
    }

    /// <summary>newBufferWithLength:options:(+1;options=0 = StorageModeShared | CPUCacheModeDefault)。</summary>
    IntPtr CreateBuffer(uint length) =>
        ObjectiveC.MsgSendNUIntNUInt(_device, MetalNative.SelNewBufferWithLengthOptions, length,
            MetalNative.ResourceOptionsShared);

    /// <summary>MSL 源 → 库:stringWithUTF8String:(+0)→ newLibraryWithSource:options:nil error:(+1;
    /// 失败只看返回值,error 槽仅失败时有效 —— MsgSendNSErrorWithOptions 契约注)。</summary>
    IntPtr CompileLibrary(string source, string what)
    {
        IntPtr nsSource = CreateNSString(source);
        IntPtr library = ObjectiveC.MsgSendNSErrorWithOptions(_device, MetalNative.SelNewLibraryWithSource,
            nsSource, IntPtr.Zero, out IntPtr error);
        if (library == IntPtr.Zero)
            throw new InvalidOperationException(
                $"newLibraryWithSource:options:error: 失败({what} 源;error 槽 0x{error:X},nil 返回时槽有效)—— MSL 编译错误细节待 CI macOS 实机提取");
        return library;
    }

    /// <summary>newFunctionWithName:(+1;函数缺席返回 nil → 显式失败,不静默)。</summary>
    IntPtr GetFunction(IntPtr library, string name)
    {
        IntPtr nsName = CreateNSString(name);
        IntPtr function = ObjectiveC.MsgSendPtr(library, MetalNative.SelNewFunctionWithName, nsName);
        if (function == IntPtr.Zero)
            throw new InvalidOperationException($"newFunctionWithName:\"{name}\" 返回 nil(MSL 入口缺席)");
        return function;
    }

    /// <summary>NSString 构造:stringWithUTF8String:(类方法;+0 autorelease —— 建库期三调用
    /// 容忍泄漏,进程级常驻)。源/名 ASCII 域,UTF-8 编码 + 尾 NUL 钉扎传入。</summary>
    static IntPtr CreateNSString(string s)
    {
        byte[] utf8 = new byte[Encoding.UTF8.GetByteCount(s) + 1];   // 尾部 NUL
        Encoding.UTF8.GetBytes(s, 0, s.Length, utf8, 0);
        GCHandle pin = GCHandle.Alloc(utf8, GCHandleType.Pinned);
        try
        {
            return ObjectiveC.MsgSendPtr(ObjectiveC.GetClass("NSString"),
                MetalNative.SelStringWithUTF8String, pin.AddrOfPinnedObject());
        }
        finally { pin.Free(); }
    }

    static IntPtr GetClassOrThrow(string name)
    {
        IntPtr cls = ObjectiveC.GetClass(name);
        if (cls == IntPtr.Zero)
            throw new InvalidOperationException($"objc_getClass(\"{name}\") 返回 nil(框架未注册该类)—— 待 CI macOS 实机验证");
        return cls;
    }

    /// <summary>顶点描述符单属性:objectAtIndexedSubscript: 取槽 → format/offset/bufferIndex=1。
    /// (attributes/layouts 两数组类型选择子同名共享 SelObjectAtIndexedSubscript —— T2 注)</summary>
    static void SetVertexAttribute(IntPtr attrs, nuint index, nuint format, nuint offset)
    {
        IntPtr attr = ObjectiveC.MsgSendNUInt(attrs, MetalNative.SelObjectAtIndexedSubscript, index);
        ObjectiveC.MsgSendVoidNUInt(attr, MetalNative.SelSetFormat, format);
        ObjectiveC.MsgSendVoidNUInt(attr, MetalNative.SelSetOffset, offset);
        ObjectiveC.MsgSendVoidNUInt(attr, MetalNative.SelSetBufferIndex, 1);   // slot 1:让位常量 buffer(0)
    }

    /// <summary>上传容量保障:超容(×2,同 GL/D3D11)时**重建**缓冲(Metal 缓冲不可重分配 ——
    /// 无 GL BufferData 孤儿化/名字不变之便;旧缓冲 release,在途命令缓冲自持引用安全;
    /// Draw 每 call 重绑 slot 1,重建对象下一 Draw 即生效)。</summary>
    void EnsureCapacity(int vertexCount, int totalIndexCount)
    {
        uint vbNeed = (uint)((long)vertexCount * UiVertex.SizeInBytes);
        uint ibNeed = (uint)((long)totalIndexCount * sizeof(ushort));
        if (vbNeed > _vbCapacityBytes)
        {
            _vbCapacityBytes = Math.Max(_vbCapacityBytes * 2, vbNeed);
            IntPtr nb = CreateBuffer(_vbCapacityBytes);
            Release(ref _vertexBuffer);
            _vertexBuffer = nb;
        }
        if (ibNeed > _ibCapacityBytes)
        {
            _ibCapacityBytes = Math.Max(_ibCapacityBytes * 2, ibNeed);
            IntPtr nb = CreateBuffer(_ibCapacityBytes);
            Release(ref _indexBuffer);
            _indexBuffer = nb;
        }
    }

    /// <summary>帧落地:endEncoding(一 commandBuffer 同时至多一活动 encoder —— 新帧前必先封旧)
    /// → commit →(wait=true 时)waitUntilCompleted。BeginFrame/Dispose 取 false(提交即可),
    /// ReadBack 取 true(getBytes 前保证 GPU 完成)。+0 对象的字段仅记账:本方法同步存续期内
    /// 无 autorelease pool 排空,waitUntilCompleted 作用于存活对象,安全。</summary>
    void FlushFrame(bool waitForCompletion)
    {
        if (_encoder != IntPtr.Zero)
        {
            ObjectiveC.MsgSendVoid(_encoder, MetalNative.SelEndEncoding);
            _encoder = IntPtr.Zero;
        }
        if (_commandBuffer != IntPtr.Zero)
        {
            ObjectiveC.MsgSendVoid(_commandBuffer, MetalNative.SelCommit);
            if (waitForCompletion)
                ObjectiveC.MsgSendVoid(_commandBuffer, MetalNative.SelWaitUntilCompleted);
            _commandBuffer = IntPtr.Zero;
        }
    }

    void EnsureEncoder()
    {
        if (_encoder == IntPtr.Zero)
            throw new InvalidOperationException("BeginFrame 未先行(编码器缺席;Renderer 契约:帧以 BeginFrame 起)");
    }

    // ---- 释放 ----

    /// <summary>FlushFrame(尽力)→ 全部 +1 对象经 release 配对释放(new* 族所有权规约,见
    /// ObjectiveC/MetalNative 类头;+0 的 commandBuffer/encoder 不持不放)。autorelease 族
    /// (commandBuffer/encoder/nextDrawable/getter)在无 pool 线程的逐帧累积为 CI 内存观测项
    /// —— 类头所有权注;修法预留 @autoreleasepool 包裹策略(对位 SDL metal 后端)。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { FlushFrame(waitForCompletion: false); } catch { /* 释放链不阻断 */ }
        Release(ref _pipelineState);
        Release(ref _vsFunction);
        Release(ref _fsFunction);
        Release(ref _libraryVs);
        Release(ref _libraryFs);
        Release(ref _sampler);
        Release(ref _atlasTexture);
        Release(ref _targetTexture);
        Release(ref _vertexBuffer);
        Release(ref _indexBuffer);
        Release(ref _constantBuffer);
        Release(ref _passDescriptor);
        Release(ref _queue);
        Release(ref _layer);
        Release(ref _device);   // MTLCreateSystemDefaultDevice:+1 归调用方(头文件注)
    }

    /// <summary>NSObject release(-1 配对;仅施于 +1 持有对象 —— 误施于 +0 对象即过度释放崩溃)。</summary>
    static void Release(ref IntPtr obj)
    {
        if (obj == IntPtr.Zero) return;
        ObjectiveC.MsgSendVoid(obj, MetalNative.SelRelease);
        obj = IntPtr.Zero;
    }

    void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MetalContext));
    }
}
