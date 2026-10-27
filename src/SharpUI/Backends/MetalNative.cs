using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>
/// X-3 T2:Metal 框架声明层(零 unsafe、NativeAOT 兼容)。Metal.framework 经绝对路径
/// DllImport(/System/Library/Frameworks/Metal.framework/Metal —— 特性参数须编译期常量,
/// macOS 系统框架路径稳定;Windows 宿主上 DllImport 惰性绑定,不调用即不加载,无解析陷阱)。
/// **头文件纪律**:全部 Selector 名/方法签名以 Metal.framework 头文件核对(出处:
/// iPhoneOS16.5.sdk System/Library/Frameworks/Metal.framework/Headers/(theos/sdks 镜像,
/// 与 macOS Metal.h 同源同面)+ QuartzCore CAMetalLayer.h + Apple 文档站互证;逐条
/// 注释见各字段)。两处简报原稿偏差按头文件实数修正:
///   ① MTLRenderPipelineDescriptor.colorAttachments / MTLRenderPassDescriptor.colorAttachments
///     均为 @property(readonly) —— 不存在 setColorAttachments: selector(调用即
///     unrecognized selector),T3 须走 getter + objectAtIndexedSubscript:;
///   ② newLibraryWithSource:options:error: 的 options 参夹在 source 与 error 之间
///     (见 ObjectiveC.MsgSendNSErrorWithOptions 注)。
/// Selector 常量为 static readonly IntPtr、显式静态构造器内赋值:类型初始化器仅在首次
/// 静态成员访问时运行(precise 语义),非 macOS 宿主直接返回、字段保持 Zero —— Windows
/// 测试经反射断言字段集合/命名规约而不触发注册(ObjectiveCTests);macOS 宿主上 Ensure()
/// 触发全量 sel_registerName 注册并抽查非 Zero。注册路径经 ObjectiveC.RegisterSel(缓存字典)。
/// **对象所有权交接(T3 注意,评审 Minor#3)**:new* 族(newCommandQueue/newBuffer…/
/// newLibraryWithSource:/newRenderPipelineStateWithDescriptor:/newFunctionWithName:/new)
/// +1 返回,调用方持有须 release(见 SelRelease)——描述符等建库期一次性对象可容忍泄漏
/// (进程级常驻),但属言明而非默认;commandBuffer/nextDrawable/属性 getter 族 +0
/// autorelease,无本地 NSAutoreleasePool 的线程上逐帧调用会累积待释放对象 —— CI macOS
/// 实测若内存随帧增长,后续修法对位 SDL metal 后端的 @autoreleasepool 包裹策略。
/// 全部消息收发待 CI macOS 实机验证(本机无 Metal 运行时)。
/// </summary>
internal static class MetalNative
{
    /// <summary>Metal.framework 绝对路径(MTLCreateSystemDefaultDevice 的 C 导出宿主;
    ///DllImport 常量特例:该库仅 macOS 存在,但惰性绑定保证 Windows 宿主零解析)。</summary>
    internal const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";

    /// <summary>MTLDevice.h C 导出:`MTL_EXTERN id&lt;MTLDevice&gt; _Nullable
    /// MTLCreateSystemDefaultDevice(void)`(macOS 10.11+;无默认设备返回 nil/Zero,
    /// T3 判定降级)。Cdecl:C 导出即平台 C 约定。</summary>
    [DllImport(MetalFramework, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr MTLCreateSystemDefaultDevice();

    /// <summary>libc memcpy(T3:macOS 宿主 /usr/lib/libSystem.dylib;DllImport 惰性绑定同上 ——
    /// Windows 宿主不调用即不解析)。Shared 缓冲直写原语(顶点/索引前缀上传):Marshal.Copy 无
    /// span→指针重载(.NET 10 实面核对),对位 D3D11Native.CopyMemory(kernel32 RtlMoveMemory)
    /// 的 Metal 版;签名全 blittable,AOT 兼容。string.h:`void *memcpy(void *restrict dst,
    /// const void *restrict src, size_t n);`</summary>
    [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl, EntryPoint = "memcpy")]
    internal static extern void CopyMemory(IntPtr destination, IntPtr source, nuint byteCount);

    /// <summary>macOS libc 绝对路径(memcpy 宿主)。</summary>
    internal const string LibSystem = "/usr/lib/libSystem.dylib";

    // ---- Selector 常量(SEL 指针;macOS 类型初始化器内经 ObjectiveC.RegisterSel 注册) ----

    // MTLDevice(MTLDevice.h):
    //   `- (nullable id&lt;MTLCommandQueue&gt;)newCommandQueue;`
    internal static readonly IntPtr SelNewCommandQueue;
    //   `- (nullable id&lt;MTLBuffer&gt;)newBufferWithLength:(NSUInteger)length options:(MTLResourceOptions)options;`
    internal static readonly IntPtr SelNewBufferWithLengthOptions;
    //   `- (nullable id&lt;MTLTexture&gt;)newTextureWithDescriptor:(MTLTextureDescriptor *)descriptor;`
    internal static readonly IntPtr SelNewTextureWithDescriptor;
    //   `- (nullable id&lt;MTLLibrary&gt;)newLibraryWithSource:(NSString *)source
    //      options:(nullable MTLCompileOptions *)options error:(__autoreleasing NSError **)error;`
    internal static readonly IntPtr SelNewLibraryWithSource;
    //   `- (nullable id&lt;MTLRenderPipelineState&gt;)newRenderPipelineStateWithDescriptor:
    //      (MTLRenderPipelineDescriptor *)descriptor error:(__autoreleasing NSError **)error;`
    internal static readonly IntPtr SelNewRenderPipelineState;

    // MTLLibrary(MTLLibrary.h):
    //   `- (nullable id&lt;MTLFunction&gt;)newFunctionWithName:(NSString *)functionName;`
    internal static readonly IntPtr SelNewFunctionWithName;

    // MTLCommandQueue(MTLCommandQueue.h):
    //   `- (nullable id&lt;MTLCommandBuffer&gt;)commandBuffer;`
    internal static readonly IntPtr SelCommandBuffer;

    // MTLCommandBuffer(MTLCommandBuffer.h):
    //   `- (nullable id&lt;MTLRenderCommandEncoder&gt;)renderCommandEncoderWithDescriptor:
    //      (MTLRenderPassDescriptor *)renderPassDescriptor;`
    internal static readonly IntPtr SelRenderCommandEncoder;
    //   `- (void)commit;`
    internal static readonly IntPtr SelCommit;

    // MTLRenderCommandEncoder(MTLRenderCommandEncoder.h):
    //   `- (void)setVertexBuffer:(nullable id&lt;MTLBuffer&gt;)buffer offset:(NSUInteger)offset atIndex:(NSUInteger)index;`
    internal static readonly IntPtr SelSetVertexBuffer;
    //   `- (void)setFragmentTexture:(nullable id&lt;MTLTexture&gt;)texture atIndex:(NSUInteger)index;`
    internal static readonly IntPtr SelSetFragmentTexture;
    //   `- (void)setFragmentSamplerState:(nullable id&lt;MTLSamplerState&gt;)sampler atIndex:(NSUInteger)index;`
    internal static readonly IntPtr SelSetFragmentSamplerState;
    //   `- (void)setRenderPipelineState:(id&lt;MTLRenderPipelineState&gt;)pipelineState;`
    internal static readonly IntPtr SelSetRenderPipelineState;
    //   `- (void)setScissorRect:(MTLScissorRect)rect;`(按值结构体 → ObjectiveC.MsgSendVoidScissorRect)
    internal static readonly IntPtr SelSetScissorRect;
    //   `- (void)drawPrimitives:(MTLPrimitiveType)primitiveType vertexStart:(NSUInteger)vertexStart
    //      vertexCount:(NSUInteger)vertexCount instanceCount:(NSUInteger)instanceCount;`
    //   (MTLPrimitiveType 为 NS_ENUM(NSUInteger) → 满宽 nuint,见 ObjectiveC.MsgSendVoidNUInt4)
    internal static readonly IntPtr SelDrawPrimitives;

    // MTLCommandEncoder(MTLCommandEncoder.h,render encoder 继承):
    //   `- (void)endEncoding;`
    internal static readonly IntPtr SelEndEncoding;

    // MTLTexture(MTLTexture.h):
    //   `- (void)getBytes:(void *)pixelBytes bytesPerRow:(NSUInteger)bytesPerRow
    //      fromRegion:(MTLRegion)region mipmapLevel:(NSUInteger)level;`
    internal static readonly IntPtr SelGetBytes;

    // NSObject(objc/runtime.h 类方法;简报:MTLTextureDescriptor.new —— 描述符三件套
    // Texture/Pipeline/Pass 均经此类方法实例化,alloc/init 语义等价):
    //   `+ (instancetype)new;`
    internal static readonly IntPtr SelNew;
    // NSObject 协议 retain/release/autorelease 三件套之 release(Foundation/NSObject.h:
    //   `- (void)release;`):new* 族 +1 返回对象的配对释放(见类头所有权交接注)
    internal static readonly IntPtr SelRelease;

    // MTLTextureDescriptor 属性(MTLTexture.h;NSUInteger 及其枚举基底,setter 单 nuint):
    //   `@property(nonatomic) MTLPixelFormat pixelFormat;` —— setPixelFormat: 共享三处:
    //   MTLTextureDescriptor / MTLRenderPipelineColorAttachmentDescriptor(MTLRenderPipeline.h
    //   `@property (nonatomic) MTLPixelFormat pixelFormat;`)/ CAMetalLayer(CAMetalLayer.h
    //   `@property MTLPixelFormat pixelFormat;`,仅 BGRA8Unorm 族合法)
    internal static readonly IntPtr SelSetPixelFormat;
    //   `@property(nonatomic) NSUInteger width;` / `@property(nonatomic) NSUInteger height;`
    internal static readonly IntPtr SelSetWidth;
    internal static readonly IntPtr SelSetHeight;
    //   `@property(nonatomic) MTLTextureUsage usage;`(NS_OPTIONS(NSUInteger):ShaderRead=0x1/RenderTarget=0x4)
    internal static readonly IntPtr SelSetUsage;
    //   `@property(nonatomic) MTLStorageMode storageMode;`(NS_ENUM;macOS 10.11+)
    internal static readonly IntPtr SelSetStorageMode;

    // MTLRenderPipelineDescriptor(MTLRenderPipeline.h):
    //   `@property (nullable, readwrite, nonatomic, strong) id&lt;MTLFunction&gt; vertexFunction;`
    internal static readonly IntPtr SelSetVertexFunction;
    //   `@property (nullable, readwrite, nonatomic, strong) id&lt;MTLFunction&gt; fragmentFunction;`
    internal static readonly IntPtr SelSetFragmentFunction;
    //   `@property (nullable, readwrite, nonatomic, copy) MTLVertexDescriptor *vertexDescriptor;`
    internal static readonly IntPtr SelSetVertexDescriptor;
    //   `@property (readonly) MTLRenderPipelineColorAttachmentDescriptorArray *colorAttachments;`
    //   ★简报原稿 setColorAttachments: 系笔误 —— 头文件 readonly,无 setter selector;
    //   T3 取附件经 getter + objectAtIndexedSubscript:(下两条,两数组类型选择子同名共享)
    internal static readonly IntPtr SelColorAttachments;
    //   MTLRenderPipelineColorAttachmentDescriptorArray / MTLRenderPassColorAttachmentDescriptorArray:
    //   `- (MTLRenderPipelineColorAttachmentDescriptor *)objectAtIndexedSubscript:(NSUInteger)attachmentIndex;`
    //   `- (MTLRenderPassColorAttachmentDescriptor *)objectAtIndexedSubscript:(NSUInteger)attachmentIndex;`
    internal static readonly IntPtr SelObjectAtIndexedSubscript;
    //   `- (void)setObject:(nullable ...)attachment atIndexedSubscript:(NSUInteger)attachmentIndex;`(两数组同签名)
    internal static readonly IntPtr SelSetObjectAtIndexedSubscript;

    // MTLRenderPassColorAttachmentDescriptor(MTLRenderPass.h):
    //   `@property (nonatomic) MTLLoadAction loadAction;`(NS_ENUM 实数:DontCare=0/Load=1/**Clear=2**
    //      —— T3 按 iPhoneOS16.5.sdk MTLRenderPass.h 逐行核得,勘正 T2 原注"Load=0/…/DontCare=3"
    //      的记忆笔误;消费方常量见 LoadActionClear)
    internal static readonly IntPtr SelSetLoadAction;
    //   `@property (nonatomic) MTLStoreAction storeAction;`(NS_ENUM 实数:**DontCare=0/Store=1**
    //      —— 同上勘正 T2 原注"Store=0/DontCare=1";消费方常量见 StoreActionStore)
    internal static readonly IntPtr SelSetStoreAction;
    //   `@property (nullable, nonatomic, strong) id&lt;MTLTexture&gt; texture;`
    internal static readonly IntPtr SelSetTexture;
    //   `@property (nonatomic) MTLClearColor clearColor;`(typedef struct{double red,green,blue,alpha}
    //   —— 4×double HFA → ObjectiveC.MsgSendVoidDouble4)
    internal static readonly IntPtr SelSetClearColor;

    // CAMetalLayer(QuartzCore CAMetalLayer.h;简报列 nextDrawable,device/drawableSize 系
    // 布线必需的自然扩展 —— layer 不接 device/尺寸即无 drawable 可取):
    //   `- (nullable id<CAMetalDrawable>)nextDrawable;`
    internal static readonly IntPtr SelNextDrawable;
    //   `@property(nullable, retain) id<MTLDevice> device;` → setDevice:
    internal static readonly IntPtr SelSetDevice;
    //   `@property CGSize drawableSize;` → setDrawableSize:(2×double → MsgSendVoidDouble2)
    internal static readonly IntPtr SelSetDrawableSize;

    // ---- T3 扩展(MetalContext headless 消费;出处逐条以 iPhoneOS16.5.sdk Metal.framework
    //      Headers 实数核得,与 macOS Metal.h 同源同面) ----

    // NSString(Foundation/NSString.h):
    //   `+ (instancetype)stringWithUTF8String:(const char *)nullTerminatedCString;`
    //   (类方法:接收者=Class 指针;+0 autorelease 返回 —— 建库期三调用容忍泄漏,见类头所有权注)
    internal static readonly IntPtr SelStringWithUTF8String;

    // MTLBuffer(MTLBuffer.h):
    //   `@property (readonly) void *contents;` —— getter(selector 名=属性名),
    //   NS_RETURNS_INNER_POINTER:内指有效性随接收者存活(本层 +1 持有 buffer,contents 随持活)
    internal static readonly IntPtr SelContents;

    // MTLTexture(MTLTexture.h)2D 便捷形:
    //   `- (void)replaceRegion:(MTLRegion)region mipmapLevel:(NSUInteger)level
    //      withBytes:(const void *)pixelBytes bytesPerRow:(NSUInteger)bytesPerRow;`
    //   (MTLRegion 48B 按值 → 标量与结构体混排,见 ObjectiveC.MsgSendVoidRegionNUIntPtrNUInt)
    internal static readonly IntPtr SelReplaceRegion;

    // MTLDevice(MTLDevice.h):
    //   `- (nullable id<MTLSamplerState>)newSamplerStateWithDescriptor:(MTLSamplerDescriptor *)descriptor;`(+1)
    internal static readonly IntPtr SelNewSamplerStateWithDescriptor;

    // MTLSamplerDescriptor(MTLSampler.h;属性 setter 单 NSUInteger/枚举基底):
    //   `@property (nonatomic) MTLSamplerMinMagFilter minFilter;` / magFilter
    internal static readonly IntPtr SelSetMinFilter;
    internal static readonly IntPtr SelSetMagFilter;
    //   `@property (nonatomic) MTLSamplerAddressMode sAddressMode;` / tAddressMode
    internal static readonly IntPtr SelSetSAddressMode;
    internal static readonly IntPtr SelSetTAddressMode;

    // MTLVertexDescriptor(MTLVertexDescriptor.h):
    //   `@property (readonly) MTLVertexAttributeDescriptorArray *attributes;` —— getter
    internal static readonly IntPtr SelAttributes;
    //   `@property (readonly) MTLVertexBufferLayoutDescriptorArray *layouts;` —— getter
    internal static readonly IntPtr SelLayouts;

    // MTLVertexAttributeDescriptor(MTLVertexDescriptor.h):
    //   `@property (assign, nonatomic) MTLVertexFormat format;` → setFormat:
    internal static readonly IntPtr SelSetFormat;
    //   `@property (assign, nonatomic) NSUInteger offset;` → setOffset:
    internal static readonly IntPtr SelSetOffset;
    //   `@property (assign, nonatomic) NSUInteger bufferIndex;` → setBufferIndex:
    internal static readonly IntPtr SelSetBufferIndex;

    // MTLVertexBufferLayoutDescriptor(MTLVertexDescriptor.h):
    //   `@property (assign, nonatomic) NSUInteger stride;` → setStride:
    internal static readonly IntPtr SelSetStride;
    //   `@property (assign, nonatomic) MTLVertexStepFunction stepFunction;` → setStepFunction:
    //      (默认值 Constant=0 头文件未言明 —— 显式落 PerVertex,不赌驱动默认,见类头记忆值纪律)
    internal static readonly IntPtr SelSetStepFunction;
    //   `@property (assign, nonatomic) NSUInteger stepRate;` → setRate: 同上显式落 1
    internal static readonly IntPtr SelSetStepRate;

    // MTLRenderPipelineColorAttachmentDescriptor(MTLRenderPipeline.h;混合状态随管线固化,
    // 无 GL 运行时 Enable(Blend) 对应物):
    //   `@property (nonatomic, getter = isBlendingEnabled) BOOL blendingEnabled;` → setter 仍为
    //      合成 setBlendingEnabled:(BOOL=_Bool 1 字节;MsgSendVoidNUInt 传 0/1 低字节即位,
    //      arm64/x86_64 实参均取寄存器低 8 位 —— 见 MsgSendVoidNUInt 注)
    internal static readonly IntPtr SelSetBlendingEnabled;
    //   `@property (nonatomic) MTLBlendFactor sourceRGBBlendFactor;` 等 4 因子 + 2 操作:
    internal static readonly IntPtr SelSetSourceRGBBlendFactor;
    internal static readonly IntPtr SelSetDestinationRGBBlendFactor;
    internal static readonly IntPtr SelSetSourceAlphaBlendFactor;
    internal static readonly IntPtr SelSetDestinationAlphaBlendFactor;
    //   `@property (nonatomic) MTLBlendOperation rgbBlendOperation;` / alphaBlendOperation
    internal static readonly IntPtr SelSetRgbBlendOperation;
    internal static readonly IntPtr SelSetAlphaBlendOperation;

    // MTLRenderCommandEncoder(MTLRenderCommandEncoder.h)5 参便捷形(简报"索引位宽实现者定":
    // 取 16 位索引原生形 + 字节偏移,对位 GLContext Draw 的 DrawElements 偏移路径,免 16→32 展开
    // 的逐帧 CPU 拷贝/顶点重复 —— 理由见 MetalContext.Draw 注):
    //   `- (void)drawIndexedPrimitives:(MTLPrimitiveType)primitiveType indexCount:(NSUInteger)indexCount
    //      indexType:(MTLIndexType)indexType indexBuffer:(id<MTLBuffer>)indexBuffer
    //      indexBufferOffset:(NSUInteger)indexBufferOffset;`
    internal static readonly IntPtr SelDrawIndexedPrimitives;

    // MTLCommandBuffer(MTLCommandBuffer.h):
    //   `- (void)waitUntilCompleted;`(读回同步:getBytes 前保证 GPU 完成)
    internal static readonly IntPtr SelWaitUntilCompleted;

    // ---- 枚举/选项常量(T3 消费;逐条以 iPhoneOS16.5.sdk Metal.framework Headers 实数核得,
    //      与 macOS Metal.h 同源同面 —— 记忆值不可信纪律,出处随行注明) ----

    /// <summary>MTLPixelFormat(MTLPixelFormat.h):RGBA8Unorm = 70(BGRA8Unorm = 80 不用 ——
    /// 读回字节序 R,G,B,A 与 GL 后端断言形一致,BGRA 为 CAMetalLayer 窗口路径专属)。</summary>
    internal const nuint PixelFormatRgba8Unorm = 70;

    /// <summary>MTLTextureUsage(MTLTexture.h,NS_OPTIONS):ShaderRead = 0x1(图集采样)。</summary>
    internal const nuint TextureUsageShaderRead = 0x1;

    /// <summary>MTLTextureUsage:RenderTarget = 0x4(离屏主目标)。</summary>
    internal const nuint TextureUsageRenderTarget = 0x4;

    /// <summary>MTLStorageMode(MTLResource.h):Shared = 0。图集(replaceRegion 上传+采样)与
    /// 离屏目标(渲染+getBytes 读回)均取 Shared:macOS 10.15+ 全 GPU 形态支持(Managed 已弃用,
    /// 离散卡经自动迁移);若 CI 实测离散卡拒建 Shared 渲染目标,退路=Private 目标 + blit 编码器
    /// 拷贝(后续修法,非本轮)。</summary>
    internal const nuint StorageModeShared = 0;

    /// <summary>MTLResourceOptions(MTLResource.h):0 = CPUCacheModeDefaultCache(0&lt;&lt;0) |
    /// StorageModeShared(0&lt;&lt;4) —— newBufferWithLength:options: 的 Shared 缓冲形态。</summary>
    internal const nuint ResourceOptionsShared = 0;

    /// <summary>MTLVertexFormat(MTLVertexDescriptor.h):Float2 = 29(顶点 X/Y、U/V 两组)。</summary>
    internal const nuint VertexFormatFloat2 = 29;

    /// <summary>MTLVertexFormat:UChar4Normalized = 9(顶点色 RGBA 逐字节归一化,内存直读 ——
    /// 对位 GL UNSIGNED_BYTE normalized / D3D11 R8G8B8A8_UNORM)。</summary>
    internal const nuint VertexFormatUChar4Normalized = 9;

    /// <summary>MTLVertexStepFunction(MTLVertexDescriptor.h):PerVertex = 1(布局步进显式落定,
    /// 不赌默认值 —— 头文件未言明默认,Constant=0 语义错:全顶点共取缓冲原点一份)。</summary>
    internal const nuint VertexStepFunctionPerVertex = 1;

    /// <summary>MTLBlendFactor(MTLRenderPipeline.h):One = 1。</summary>
    internal const nuint BlendFactorOne = 1;

    /// <summary>MTLBlendFactor:OneMinusSourceAlpha = 5(预乘混合,对位 D3D11 BlendOne/InvSrcAlpha
    /// 与 GL BlendFunc(ONE, ONE_MINUS_SRC_ALPHA),RGB/Alpha 四因子同式)。</summary>
    internal const nuint BlendFactorOneMinusSourceAlpha = 5;

    /// <summary>MTLBlendOperation(MTLRenderPipeline.h):Add = 0(RGB/Alpha 两操作同值)。</summary>
    internal const nuint BlendOperationAdd = 0;

    /// <summary>MTLPrimitiveType(MTLRenderCommandEncoder.h):Triangle = 3。</summary>
    internal const nuint PrimitiveTypeTriangle = 3;

    /// <summary>MTLIndexType(MTLStageInputOutputDescriptor.h):UInt16 = 0(接口层索引即 ushort[])。</summary>
    internal const nuint IndexTypeUInt16 = 0;

    /// <summary>MTLSamplerMinMagFilter(MTLSampler.h):Nearest = 0(对位 GL NEAREST / D3D11 POINT)。</summary>
    internal const nuint SamplerMinMagFilterNearest = 0;

    /// <summary>MTLSamplerAddressMode(MTLSampler.h):ClampToEdge = 0(对位 GL CLAMP_TO_EDGE /
    /// D3D11 CLAMP)。</summary>
    internal const nuint SamplerAddressModeClampToEdge = 0;

    /// <summary>MTLLoadAction(MTLRenderPass.h):Clear = 2(DontCare=0/Load=1 —— T2 原注勘正,见
    /// SelSetLoadAction)。</summary>
    internal const nuint LoadActionClear = 2;

    /// <summary>MTLStoreAction(MTLRenderPass.h):Store = 1(DontCare=0 —— T2 原注勘正,见
    /// SelSetStoreAction;读回目标必须 Store,DontCare 会弃内容)。</summary>
    internal const nuint StoreActionStore = 1;

    // ---- 按值结构体(头文件实数;x64 原生 sizeof 经 ObjectiveCTests 锁定) ----

    /// <summary>MTLScissorRect(MTLRenderCommandEncoder.h):`typedef struct { NSUInteger x, y,
    /// width, height; } MTLScissorRect;` = 4×8 = 32(x64)。大于 16 字节整型复合体 → MEMORY 类
    /// 按值走栈,委托参数须整结构体(见 ObjectiveC 类头调用约定论证①)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScissorRect
    {
        public nuint X, Y, Width, Height;
    }

    /// <summary>MTLRegion(MTLTypes.h):`typedef struct { MTLOrigin origin; MTLSize size; }
    /// MTLRegion;`(MTLOrigin={NSUInteger x,y,z}、MTLSize={NSUInteger width,height,depth})
    /// 展平为 6×8 = 48(x64),布局与 origin+size 嵌套等价(Sequential 同序)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Region
    {
        public nuint X, Y, Z;          // MTLOrigin origin
        public nuint Width, Height, Depth;   // MTLSize size
    }

    // ---- 注册(显式静态构造器:precise 语义 —— 首次静态成员访问才运行;非 macOS 早退) ----

    static MetalNative()
    {
        if (!OperatingSystem.IsMacOS())
            return;   // Windows/Linux:字段保持 Zero,测试仅编译结构(不触发本构造器的值访问)
        SelNewCommandQueue = ObjectiveC.RegisterSel("newCommandQueue");
        SelNewBufferWithLengthOptions = ObjectiveC.RegisterSel("newBufferWithLength:options:");
        SelNewTextureWithDescriptor = ObjectiveC.RegisterSel("newTextureWithDescriptor:");
        SelNewLibraryWithSource = ObjectiveC.RegisterSel("newLibraryWithSource:options:error:");
        SelNewRenderPipelineState = ObjectiveC.RegisterSel("newRenderPipelineStateWithDescriptor:error:");
        SelNewFunctionWithName = ObjectiveC.RegisterSel("newFunctionWithName:");
        SelCommandBuffer = ObjectiveC.RegisterSel("commandBuffer");
        SelRenderCommandEncoder = ObjectiveC.RegisterSel("renderCommandEncoderWithDescriptor:");
        SelCommit = ObjectiveC.RegisterSel("commit");
        SelSetVertexBuffer = ObjectiveC.RegisterSel("setVertexBuffer:offset:atIndex:");
        SelSetFragmentTexture = ObjectiveC.RegisterSel("setFragmentTexture:atIndex:");
        SelSetFragmentSamplerState = ObjectiveC.RegisterSel("setFragmentSamplerState:atIndex:");
        SelSetRenderPipelineState = ObjectiveC.RegisterSel("setRenderPipelineState:");
        SelSetScissorRect = ObjectiveC.RegisterSel("setScissorRect:");
        SelDrawPrimitives = ObjectiveC.RegisterSel("drawPrimitives:vertexStart:vertexCount:instanceCount:");
        SelEndEncoding = ObjectiveC.RegisterSel("endEncoding");
        SelGetBytes = ObjectiveC.RegisterSel("getBytes:bytesPerRow:fromRegion:mipmapLevel:");
        SelNew = ObjectiveC.RegisterSel("new");
        SelRelease = ObjectiveC.RegisterSel("release");
        SelSetPixelFormat = ObjectiveC.RegisterSel("setPixelFormat:");
        SelSetWidth = ObjectiveC.RegisterSel("setWidth:");
        SelSetHeight = ObjectiveC.RegisterSel("setHeight:");
        SelSetUsage = ObjectiveC.RegisterSel("setUsage:");
        SelSetStorageMode = ObjectiveC.RegisterSel("setStorageMode:");
        SelSetVertexFunction = ObjectiveC.RegisterSel("setVertexFunction:");
        SelSetFragmentFunction = ObjectiveC.RegisterSel("setFragmentFunction:");
        SelSetVertexDescriptor = ObjectiveC.RegisterSel("setVertexDescriptor:");
        SelColorAttachments = ObjectiveC.RegisterSel("colorAttachments");
        SelObjectAtIndexedSubscript = ObjectiveC.RegisterSel("objectAtIndexedSubscript:");
        SelSetObjectAtIndexedSubscript = ObjectiveC.RegisterSel("setObject:atIndexedSubscript:");
        SelSetLoadAction = ObjectiveC.RegisterSel("setLoadAction:");
        SelSetStoreAction = ObjectiveC.RegisterSel("setStoreAction:");
        SelSetTexture = ObjectiveC.RegisterSel("setTexture:");
        SelSetClearColor = ObjectiveC.RegisterSel("setClearColor:");
        SelNextDrawable = ObjectiveC.RegisterSel("nextDrawable");
        SelSetDevice = ObjectiveC.RegisterSel("setDevice:");
        SelSetDrawableSize = ObjectiveC.RegisterSel("setDrawableSize:");
        // T3 扩展(MetalContext headless 消费;契约锁同步 ObjectiveCTests.ExpectedSelectors)
        SelStringWithUTF8String = ObjectiveC.RegisterSel("stringWithUTF8String:");
        SelContents = ObjectiveC.RegisterSel("contents");
        SelReplaceRegion = ObjectiveC.RegisterSel("replaceRegion:mipmapLevel:withBytes:bytesPerRow:");
        SelNewSamplerStateWithDescriptor = ObjectiveC.RegisterSel("newSamplerStateWithDescriptor:");
        SelSetMinFilter = ObjectiveC.RegisterSel("setMinFilter:");
        SelSetMagFilter = ObjectiveC.RegisterSel("setMagFilter:");
        SelSetSAddressMode = ObjectiveC.RegisterSel("setSAddressMode:");
        SelSetTAddressMode = ObjectiveC.RegisterSel("setTAddressMode:");
        SelAttributes = ObjectiveC.RegisterSel("attributes");
        SelLayouts = ObjectiveC.RegisterSel("layouts");
        SelSetFormat = ObjectiveC.RegisterSel("setFormat:");
        SelSetOffset = ObjectiveC.RegisterSel("setOffset:");
        SelSetBufferIndex = ObjectiveC.RegisterSel("setBufferIndex:");
        SelSetStride = ObjectiveC.RegisterSel("setStride:");
        SelSetStepFunction = ObjectiveC.RegisterSel("setStepFunction:");
        SelSetStepRate = ObjectiveC.RegisterSel("setStepRate:");
        SelSetBlendingEnabled = ObjectiveC.RegisterSel("setBlendingEnabled:");
        SelSetSourceRGBBlendFactor = ObjectiveC.RegisterSel("setSourceRGBBlendFactor:");
        SelSetDestinationRGBBlendFactor = ObjectiveC.RegisterSel("setDestinationRGBBlendFactor:");
        SelSetSourceAlphaBlendFactor = ObjectiveC.RegisterSel("setSourceAlphaBlendFactor:");
        SelSetDestinationAlphaBlendFactor = ObjectiveC.RegisterSel("setDestinationAlphaBlendFactor:");
        SelSetRgbBlendOperation = ObjectiveC.RegisterSel("setRgbBlendOperation:");
        SelSetAlphaBlendOperation = ObjectiveC.RegisterSel("setAlphaBlendOperation:");
        SelDrawIndexedPrimitives = ObjectiveC.RegisterSel(
            "drawIndexedPrimitives:indexCount:indexType:indexBuffer:indexBufferOffset:");
        SelWaitUntilCompleted = ObjectiveC.RegisterSel("waitUntilCompleted");
    }

    /// <summary>IsMacOS 守卫 + 触发全量 selector 注册(触碰任一字段即运行类型初始化器)
    /// + 抽查注册产物非 Zero。非 macOS 抛 InvalidOperationException(带指引);
    /// Metal.framework 本体(非 libobjc)由 MTLCreateSystemDefaultDevice 首调经 DllImport
    /// 惰性加载 —— 守卫职责与 libobjc 一致,失败面收敛到本方法。</summary>
    internal static void Ensure()
    {
        if (!OperatingSystem.IsMacOS())
            throw new InvalidOperationException(
                "Metal 后端仅限 macOS 宿主(Metal.framework/libobjc 均系统库)。当前平台上本声明层" +
                "仅作编译结构验证(测试经反射断言);设备创建与消息收发待 CI macOS 实机验证。");
        _ = SelNewCommandQueue;   // 触发静态构造器完成全表注册
        if (SelNextDrawable == IntPtr.Zero || SelSetVertexBuffer == IntPtr.Zero)
            throw new InvalidOperationException(
                "Metal selector 注册产物异常(SEL 为空):待 CI macOS 实机验证。");
    }
}
