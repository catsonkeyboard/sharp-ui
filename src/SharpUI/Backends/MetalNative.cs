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
    //   `@property (nonatomic) MTLLoadAction loadAction;`(NS_ENUM:Load=0/Store=1/Clear=2/DontCare=3)
    internal static readonly IntPtr SelSetLoadAction;
    //   `@property (nonatomic) MTLStoreAction storeAction;`(NS_ENUM:Store=0/DontCare=1 ...)
    internal static readonly IntPtr SelSetStoreAction;
    //   `@property (nullable, nonatomic, strong) id&lt;MTLTexture&gt; texture;`
    internal static readonly IntPtr SelSetTexture;
    //   `@property (nonatomic) MTLClearColor clearColor;`(typedef struct{double red,green,blue,alpha}
    //   —— 4×double HFA → ObjectiveC.MsgSendVoidDouble4)
    internal static readonly IntPtr SelSetClearColor;

    // CAMetalLayer(QuartzCore CAMetalLayer.h;简报列 nextDrawable,device/drawableSize 系
    // 布线必需的自然扩展 —— layer 不接 device/尺寸即无 drawable 可取):
    //   `- (nullable id&lt;CAMetalDrawable&gt;)nextDrawable;`
    internal static readonly IntPtr SelNextDrawable;
    //   `@property(nullable, retain) id&lt;MTLDevice&gt; device;` → setDevice:
    internal static readonly IntPtr SelSetDevice;
    //   `@property CGSize drawableSize;` → setDrawableSize:(2×double → MsgSendVoidDouble2)
    internal static readonly IntPtr SelSetDrawableSize;

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
    /// MTLRegion;`(MTLOrIGIN={NSUInteger x,y,z}、MTLSize={NSUInteger width,height,depth})
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
