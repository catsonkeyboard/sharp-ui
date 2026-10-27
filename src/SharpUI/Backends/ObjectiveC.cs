using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>
/// X-3 T2:libobjc 直连(零 unsafe、NativeAOT 兼容)。/usr/lib/libobjc.dylib 为 macOS 系统库,
/// 经 NativeLibrary.Load 显式加载 + GetExport 取 C 导出 + GetDelegateForFunctionPointer 绑定
/// (TextInput.Bind / SdlNative.Fn 同型先例);不走 DllImport —— 库名仅在 macOS 存在,显式句柄
/// + IsMacOS 守卫把失败面收敛到 Ensure(非 Windows 常量陷阱见 NativeLib 头注)。加载/绑定失败
/// 抛 InvalidOperationException,消息含"待 CI macOS 实机验证"指引 —— 本机无 macOS 可跑,
/// 实机行为由 CI 跑道背书。
/// **调用约定论证**(objc/message.h 声明 objc_msgSend(id, SEL, ...) 为变参 C 函数;本层绑定为
/// 定参函数指针 —— 与 Swift unsafeBitCast(@convention(c))/Go cgo 绑定 objc_msgSend 的规范做法
/// 同型,消息首二参 self/SEL 之后实参按平台 C ABI 直排):
///   arm64:objc_msgSend 与 C 调用约定完全兼容(self→x0、SEL→x1、实参→x2+;arm64 无
///   stret/fpret 变体,一切返回均走单入口),Cdecl 即平台唯一约定,定参直调合法;
///   x86_64(SysV):self→rdi、SEL→rsi,同为标准 C 约定。本层全部消息仅 id/void 返回 ——
///   无浮点返回(x86_64 仅 long double 返回需 objc_msgSend_fpret)、无结构体返回(x86_64
///   大于 16 字节结构体返回需 objc_msgSend_stret),故标量定参直调 objc_msgSend 即安全。
///   结构体【实参】按平台 C ABI 落位,两类处置:
///   ① 整型复合体 MTLScissorRect(4×NSUInteger=32B)/MTLRegion(6×NSUInteger=48B)均大于
///     16 字节 → MEMORY 类走栈,不能用标量参数冒充(arm64 标量走寄存器、x86_64 亦然,
///     位型不匹配),经结构体按值参数委托表达 —— 封送器按平台 ABI 生成栈传递(见
///     MetalNative.ScissorRect/Region);
///   ② 同质浮点复合体 MTLClearColor(4×double)/CGSize(2×double):arm64 归 HFA(v0-v3/
///     v0-v1)、x86_64 归 SSE eightbytes(xmm0-3/xmm0-1),与等数量独立 double 标量参数的
///     寄存器落位逐位一致 → 用 double 标量委托表达(MsgSendVoidDouble4/2),免结构体。
///   i386 旧 Mac 的 stret/fpret 陷阱不在支持范围(目标宿主 x86_64/arm64 Mac)。
/// Windows/Linux:Ensure 即抛(守卫在原生加载之前,可被本机测试真断言);测试仅做编译结构
/// 断言(ObjectiveCTests),真实消息收发待 CI macOS 实机验证。
/// </summary>
internal static class ObjectiveC
{
    /// <summary>macOS 系统库绝对路径(/usr/lib/libobjc.dylib; objc/runtime.h、objc/message.h 所属)。</summary>
    const string LibObjc = "/usr/lib/libobjc.dylib";

    static readonly object _gate = new();
    static bool _bound;
    static IntPtr _libobjc;

    // ---- C 入口绑定(objc/runtime.h:objc_getClass/sel_registerName;objc/message.h:objc_msgSend) ----

    static DGetClass _getClass = null!;
    static DRegisterSel _registerSel = null!;
    static DMsgSend _msgSend = null!;
    static DMsgSendPtr _msgSendPtr = null!;
    static DMsgSendPtrPtr _msgSendPtrPtr = null!;
    static DMsgSendVoid _msgSendVoid = null!;
    static DMsgSendFloat4 _msgSendFloat4 = null!;
    static DMsgSendNSError _msgSendNSError = null!;
    static DMsgSendVoidPtr _msgSendVoidPtr = null!;
    static DMsgSendVoidNUInt _msgSendVoidNUInt = null!;
    static DMsgSendPtrNUInt _msgSendPtrNUInt = null!;
    static DMsgSendVoidPtrNUInt _msgSendVoidPtrNUInt = null!;
    static DMsgSendVoidPtrNUIntNUInt _msgSendVoidPtrNUIntNUInt = null!;
    static DMsgSendNUIntNUInt _msgSendNUIntNUInt = null!;
    static DMsgSendVoidNUInt4 _msgSendVoidNUInt4 = null!;
    static DMsgSendNSErrorWithOptions _msgSendNSErrorWithOptions = null!;
    static DMsgSendVoidDouble2 _msgSendVoidDouble2 = null!;
    static DMsgSendVoidDouble4 _msgSendVoidDouble4 = null!;
    static DMsgSendVoidScissorRect _msgSendVoidScissorRect = null!;
    static DMsgSendVoidGetBytes _msgSendVoidGetBytes = null!;

    /// <summary>Selector 注册缓存(sel_registerName 本身廉价且幂等,缓存避免每帧字符串往返;
    /// ConcurrentDictionary:UI 线程为主但无必要假设单线程)。</summary>
    static readonly ConcurrentDictionary<string, IntPtr> _selCache = new();

    /// <summary>加载 libobjc 并绑定全部入口(幂等)。非 macOS 宿主一律抛 InvalidOperationException
    /// —— 本层在本机 Windows 上只做编译结构验证,实机行为待 CI macOS 跑道验证。</summary>
    internal static void Ensure()
    {
        if (_bound) return;
        lock (_gate)
        {
            if (_bound) return;
            if (!OperatingSystem.IsMacOS())
                throw new InvalidOperationException(
                    "Objective-C 运行时(libobjc)仅存在于 macOS 宿主,Ensure 拒绝在当前平台加载。" +
                    "Windows/Linux 上本声明层仅作编译结构验证(测试经反射断言);真实消息收发待 CI macOS 实机验证。");
            try
            {
                _libobjc = NativeLibrary.Load(LibObjc);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"加载 {LibObjc} 失败(系统库,正常不应缺席):{ex.Message}。待 CI macOS 实机验证。", ex);
            }
            _getClass = Bind<DGetClass>("objc_getClass");
            _registerSel = Bind<DRegisterSel>("sel_registerName");
            _msgSend = Bind<DMsgSend>("objc_msgSend");
            _msgSendPtr = Bind<DMsgSendPtr>("objc_msgSend");
            _msgSendPtrPtr = Bind<DMsgSendPtrPtr>("objc_msgSend");
            _msgSendVoid = Bind<DMsgSendVoid>("objc_msgSend");
            _msgSendFloat4 = Bind<DMsgSendFloat4>("objc_msgSend");
            _msgSendNSError = Bind<DMsgSendNSError>("objc_msgSend");
            _msgSendVoidPtr = Bind<DMsgSendVoidPtr>("objc_msgSend");
            _msgSendVoidNUInt = Bind<DMsgSendVoidNUInt>("objc_msgSend");
            _msgSendPtrNUInt = Bind<DMsgSendPtrNUInt>("objc_msgSend");
            _msgSendVoidPtrNUInt = Bind<DMsgSendVoidPtrNUInt>("objc_msgSend");
            _msgSendVoidPtrNUIntNUInt = Bind<DMsgSendVoidPtrNUIntNUInt>("objc_msgSend");
            _msgSendNUIntNUInt = Bind<DMsgSendNUIntNUInt>("objc_msgSend");
            _msgSendVoidNUInt4 = Bind<DMsgSendVoidNUInt4>("objc_msgSend");
            _msgSendNSErrorWithOptions = Bind<DMsgSendNSErrorWithOptions>("objc_msgSend");
            _msgSendVoidDouble2 = Bind<DMsgSendVoidDouble2>("objc_msgSend");
            _msgSendVoidDouble4 = Bind<DMsgSendVoidDouble4>("objc_msgSend");
            _msgSendVoidScissorRect = Bind<DMsgSendVoidScissorRect>("objc_msgSend");
            _msgSendVoidGetBytes = Bind<DMsgSendVoidGetBytes>("objc_msgSend");
            _bound = true;
        }
    }

    static T Bind<T>(string export) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_libobjc, export));

    /// <summary>objc_getClass(name):类名 → Class 指针;Objective-C 链接库未注册该名时返回
    /// nil(Zero),由调用方判定(Metal 类名缺席属安装级异常)。</summary>
    internal static IntPtr GetClass(string name)
    {
        Ensure();
        return _getClass(name);
    }

    /// <summary>sel_registerName(name):selector 名 → SEL(缓存字典;同名恒等返回)。
    /// sel_registerName 对任意字符串都会注册成功(不返回 nil)。</summary>
    internal static IntPtr RegisterSel(string name)
    {
        Ensure();
        return _selCache.GetOrAdd(name, n => _registerSel(n));
    }

    // ---- MsgSend 委托族(命名=返回形态+参数形态;全部先 Ensure 再直调) ----
    // NSUInteger 一律映射 nuint(64 位平台上 8 字节;uint 会留高 32 位垃圾 → 必须满宽)。

    /// <summary>0 参 id 返回:newCommandQueue / commandBuffer / nextDrawable / new / colorAttachments 等。</summary>
    internal static IntPtr MsgSend(IntPtr rcv, IntPtr sel) { Ensure(); return _msgSend(rcv, sel); }

    /// <summary>1×对象参 id 返回:newTextureWithDescriptor: / newFunctionWithName:(NSString 参) /
    /// renderCommandEncoderWithDescriptor: / objectAtIndexedSubscript: 等。</summary>
    internal static IntPtr MsgSendPtr(IntPtr rcv, IntPtr sel, IntPtr a0) { Ensure(); return _msgSendPtr(rcv, sel, a0); }

    /// <summary>2×对象参 id 返回(通用形态;注意不可用于带 error:** 尾参的方法 ——
    /// 该槽位未占即读到垃圾指针,须用 MsgSendNSError/MsgSendNSErrorWithOptions)。</summary>
    internal static IntPtr MsgSendPtrPtr(IntPtr rcv, IntPtr sel, IntPtr a0, IntPtr a1)
    { Ensure(); return _msgSendPtrPtr(rcv, sel, a0, a1); }

    /// <summary>0 参 void:endEncoding / commit 等。</summary>
    internal static void MsgSendVoid(IntPtr rcv, IntPtr sel) { Ensure(); _msgSendVoid(rcv, sel); }

    /// <summary>4×float 参 void(简报契约件;现无 Metal 消费方 —— MTLClearColor 为 4×double,
    /// 须用 MsgSendVoidDouble4。保留契约形态,T3 如无消费可裁)。</summary>
    internal static void MsgSendVoidFloat4(IntPtr rcv, IntPtr sel, float r, float g, float b, float a)
    { Ensure(); _msgSendFloat4(rcv, sel, r, g, b, a); }

    /// <summary>(对象参, NSError** out)→ id:newRenderPipelineStateWithDescriptor:error:
    /// —— MTLDevice.h:`newRenderPipelineStateWithDescriptor:(MTLRenderPipelineDescriptor *)descriptor
    /// error:(__autoreleasing NSError **)error`(error 为 +0 引用,调用方不释放)。</summary>
    internal static IntPtr MsgSendNSError(IntPtr rcv, IntPtr sel, IntPtr a0, out IntPtr error)
    { Ensure(); return _msgSendNSError(rcv, sel, a0, out error); }

    /// <summary>1×对象参 void:setRenderPipelineState: / setVertexFunction: / setFragmentFunction: /
    /// setVertexDescriptor: / setTexture: / setDevice: / setObject:atIndexedSubscript:(与 nuint 参版区分)。</summary>
    internal static void MsgSendVoidPtr(IntPtr rcv, IntPtr sel, IntPtr a0) { Ensure(); _msgSendVoidPtr(rcv, sel, a0); }

    /// <summary>1×NSUInteger 参 void:全部描述符属性 setter —— setPixelFormat:/setWidth:/setHeight:/
    /// setUsage:/setStorageMode:/setLoadAction:/setStoreAction:(Metal.h 属性均 NSUInteger/其枚举)。</summary>
    internal static void MsgSendVoidNUInt(IntPtr rcv, IntPtr sel, nuint a0) { Ensure(); _msgSendVoidNUInt(rcv, sel, a0); }

    /// <summary>(对象参, NSUInteger 参)→ id:objectAtIndexedSubscript:(attachmentIndex)。</summary>
    internal static IntPtr MsgSendPtrNUInt(IntPtr rcv, IntPtr sel, IntPtr a0, nuint a1)
    { Ensure(); return _msgSendPtrNUInt(rcv, sel, a0, a1); }

    /// <summary>(对象参, NSUInteger 参)void:setFragmentTexture:atIndex: / setFragmentSamplerState:atIndex: /
    /// setObject:atIndexedSubscript:(attachment, attachmentIndex)。</summary>
    internal static void MsgSendVoidPtrNUInt(IntPtr rcv, IntPtr sel, IntPtr a0, nuint a1)
    { Ensure(); _msgSendVoidPtrNUInt(rcv, sel, a0, a1); }

    /// <summary>(对象参, NSUInteger, NSUInteger)void:setVertexBuffer:offset:atIndex:。</summary>
    internal static void MsgSendVoidPtrNUIntNUInt(IntPtr rcv, IntPtr sel, IntPtr a0, nuint a1, nuint a2)
    { Ensure(); _msgSendVoidPtrNUIntNUInt(rcv, sel, a0, a1, a2); }

    /// <summary>(NSUInteger, NSUInteger)→ id:newBufferWithLength:options:(MTLDevice.h:
    /// `newBufferWithLength:(NSUInteger)length options:(MTLResourceOptions)options`,枚举基底 NSUInteger)。</summary>
    internal static IntPtr MsgSendNUIntNUInt(IntPtr rcv, IntPtr sel, nuint a0, nuint a1)
    { Ensure(); return _msgSendNUIntNUInt(rcv, sel, a0, a1); }

    /// <summary>4×NSUInteger 参 void:drawPrimitives:vertexStart:vertexCount:instanceCount:
    /// (MTLPrimitiveType 为 NS_ENUM(NSUInteger) —— 满宽 nuint;x86_64 恰尽 rdx/rcx/r8/r9)。</summary>
    internal static void MsgSendVoidNUInt4(IntPtr rcv, IntPtr sel, nuint a0, nuint a1, nuint a2, nuint a3)
    { Ensure(); _msgSendVoidNUInt4(rcv, sel, a0, a1, a2, a3); }

    /// <summary>(NSString 参, MTLCompileOptions 参, NSError** out)→ id:newLibraryWithSource:options:error:
    /// —— MTLDevice.h:`newLibraryWithSource:(NSString *)source options:(nullable MTLCompileOptions
    /// *)options error:(__autoreleasing NSError **)error`。简报原稿 MsgSendNSError 单对象参形态
    /// 与本方法不匹配(options 参夹在 source 与 error 之间,缺槽会把 error 指针错读入 options 槽),
    /// 故立本扩展形态;MsgSendNSError 保留给 newRenderPipelineStateWithDescriptor:error:(恰为单对象参)。</summary>
    internal static IntPtr MsgSendNSErrorWithOptions(IntPtr rcv, IntPtr sel, IntPtr source, IntPtr options, out IntPtr error)
    { Ensure(); return _msgSendNSErrorWithOptions(rcv, sel, source, options, out error); }

    /// <summary>2×double 参 void:setDrawableSize:(CAMetalLayer.h `@property CGSize drawableSize`;
    /// CGSize=2×CGFloat=2×double —— 同质浮点复合体,寄存器位与两个 double 标量逐位一致,免结构体)。</summary>
    internal static void MsgSendVoidDouble2(IntPtr rcv, IntPtr sel, double a0, double a1)
    { Ensure(); _msgSendVoidDouble2(rcv, sel, a0, a1); }

    /// <summary>4×double 参 void:setClearColor:(MTLRenderPass.h `@property MTLClearColor clearColor`;
    /// MTLClearColor=4×double —— arm64 HFA(v0-v3)/x86_64 4×SSE(xmm0-3),与四个 double 标量
    /// 参数寄存器位逐位一致,免结构体)。</summary>
    internal static void MsgSendVoidDouble4(IntPtr rcv, IntPtr sel, double r, double g, double b, double a)
    { Ensure(); _msgSendVoidDouble4(rcv, sel, r, g, b, a); }

    /// <summary>MTLScissorRect 按值参 void:setScissorRect:(32 字节整型复合体,MEMORY 类走栈,
    /// 不可拆标量 —— 见类头调用约定论证②)。</summary>
    internal static void MsgSendVoidScissorRect(IntPtr rcv, IntPtr sel, MetalNative.ScissorRect rect)
    { Ensure(); _msgSendVoidScissorRect(rcv, sel, rect); }

    /// <summary>getBytes:bytesPerRow:fromRegion:mipmapLevel: —— MTLTexture.h:
    /// `getBytes:(void *)pixelBytes bytesPerRow:(NSUInteger)bytesPerRow fromRegion:(MTLRegion)region
    /// mipmapLevel:(NSUInteger)level`(MTLRegion 48 字节按值,同走栈;标量与结构体混排由封送器落位)。</summary>
    internal static void MsgSendVoidGetBytes(IntPtr rcv, IntPtr sel, IntPtr pixelBytes, nuint bytesPerRow,
        MetalNative.Region region, nuint level)
    { Ensure(); _msgSendVoidGetBytes(rcv, sel, pixelBytes, bytesPerRow, region, level); }

    // ---- 委托类型(私有嵌套;Cdecl —— macOS 两 64 位 ABI 上 C 约定即 objc_msgSend 约定,见类头) ----

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr DGetClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr DRegisterSel([MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DMsgSend(IntPtr rcv, IntPtr sel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DMsgSendPtr(IntPtr rcv, IntPtr sel, IntPtr a0);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DMsgSendPtrPtr(IntPtr rcv, IntPtr sel, IntPtr a0, IntPtr a1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoid(IntPtr rcv, IntPtr sel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendFloat4(IntPtr rcv, IntPtr sel, float r, float g, float b, float a);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DMsgSendNSError(IntPtr rcv, IntPtr sel, IntPtr a0, out IntPtr error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidPtr(IntPtr rcv, IntPtr sel, IntPtr a0);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidNUInt(IntPtr rcv, IntPtr sel, nuint a0);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DMsgSendPtrNUInt(IntPtr rcv, IntPtr sel, IntPtr a0, nuint a1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidPtrNUInt(IntPtr rcv, IntPtr sel, IntPtr a0, nuint a1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidPtrNUIntNUInt(IntPtr rcv, IntPtr sel, IntPtr a0, nuint a1, nuint a2);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DMsgSendNUIntNUInt(IntPtr rcv, IntPtr sel, nuint a0, nuint a1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidNUInt4(IntPtr rcv, IntPtr sel, nuint a0, nuint a1, nuint a2, nuint a3);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DMsgSendNSErrorWithOptions(IntPtr rcv, IntPtr sel, IntPtr source, IntPtr options, out IntPtr error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidDouble2(IntPtr rcv, IntPtr sel, double a0, double a1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidDouble4(IntPtr rcv, IntPtr sel, double r, double g, double b, double a);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidScissorRect(IntPtr rcv, IntPtr sel, MetalNative.ScissorRect rect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DMsgSendVoidGetBytes(IntPtr rcv, IntPtr sel, IntPtr pixelBytes, nuint bytesPerRow,
        MetalNative.Region region, nuint level);
}
