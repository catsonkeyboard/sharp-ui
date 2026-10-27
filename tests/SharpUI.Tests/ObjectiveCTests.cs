using System.Reflection;
using System.Runtime.InteropServices;
using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>
/// 跨平台里程碑 Task 2:X-3 ObjectiveC 声明层 + Metal 框架声明层 + MSL 着色器的编译结构自检。
/// 纯托管、无原生门控:Windows 测试宿主不触碰 libobjc/Metal.framework(Ensure 即抛),
/// 全部断言走反射元数据(字段集合/命名规约/委托签名/DllImport 特性)与 MSL 源码文本;
/// 真实消息收发与 Metal 调用待 CI macOS 跑道实证(类头注释同指)。
/// 契约锁:Selector 字段集 = 本测试 ExpectedSelectors 精确集合 —— T3 扩展 selector 表时
/// 须同步更新本清单(字段集漂移即红,防静默漏注册/误删)。
/// </summary>
public class ObjectiveCTests
{
    // ---- MetalNative selector 常量契约(出处逐条见 MetalNative.cs;此处锁字段名集合) ----

    static readonly string[] ExpectedSelectors =
    {
        // MTLDevice(MTLDevice.h)
        "SelNewCommandQueue", "SelNewBufferWithLengthOptions", "SelNewTextureWithDescriptor",
        "SelNewLibraryWithSource", "SelNewRenderPipelineState",
        // MTLLibrary(MTLLibrary.h)
        "SelNewFunctionWithName",
        // MTLCommandQueue / MTLCommandBuffer(MTLCommandQueue.h / MTLCommandBuffer.h)
        "SelCommandBuffer", "SelRenderCommandEncoder", "SelCommit",
        // MTLRenderCommandEncoder(MTLRenderCommandEncoder.h)
        "SelSetVertexBuffer", "SelSetFragmentTexture", "SelSetFragmentSamplerState",
        "SelSetRenderPipelineState", "SelSetScissorRect", "SelDrawPrimitives", "SelEndEncoding",
        // MTLTexture(MTLTexture.h)
        "SelGetBytes",
        // MTLTextureDescriptor 属性(MTLTexture.h)+ NSObject 类方法 new(简报:MTLTextureDescriptor.new)
        "SelNew", "SelSetPixelFormat", "SelSetWidth", "SelSetHeight", "SelSetUsage", "SelSetStorageMode",
        // MTLRenderPipelineDescriptor(MTLRenderPipeline.h;colorAttachments 只读 getter —— setColorAttachments: 不存在,头文件实证)
        "SelSetVertexFunction", "SelSetFragmentFunction", "SelSetVertexDescriptor", "SelColorAttachments",
        "SelObjectAtIndexedSubscript", "SelSetObjectAtIndexedSubscript",
        // MTLRenderPassDescriptor / ColorAttachmentDescriptor(MTLRenderPass.h)
        "SelSetLoadAction", "SelSetStoreAction", "SelSetTexture", "SelSetClearColor",
        // CAMetalLayer(QuartzCore CAMetalLayer.h;nextDrawable 简报列,device/drawableSize 自然扩展)
        "SelNextDrawable", "SelSetDevice", "SelSetDrawableSize",
    };

    [Fact]
    public void MetalNative_SelectorFields_MatchContractSet()
    {
        var fields = typeof(MetalNative).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(f => f.Name).Where(n => n.StartsWith("Sel")).OrderBy(n => n).ToList();
        Assert.Equal(ExpectedSelectors.OrderBy(n => n).ToList(), fields);
    }

    [Fact]
    public void MetalNative_SelectorFields_AreReadOnlyIntPtr()
    {
        foreach (var f in typeof(MetalNative).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(f => f.Name.StartsWith("Sel")))
        {
            Assert.True(f.IsStatic, f.Name);
            Assert.True(f.IsInitOnly, f.Name);           // static readonly:契约形态
            Assert.Equal(typeof(IntPtr), f.FieldType);    // SEL 即指针
        }
    }

    [Fact]
    public void MetalNative_StructLayouts_MatchHeaderSizes()
    {
        // MTLScissorRect(MTLRenderCommandEncoder.h):4×NSUInteger = 32(x64);
        // MTLRegion(MTLTypes.h):origin{x,y,z}+size{w,h,d} 共 6×NSUInteger = 48(x64)。
        // 均 >16 字节 → MEMORY 类按值传参(arm64/x86_64 走栈),故委托用结构体按值参数。
        Assert.Equal(32, Marshal.SizeOf<MetalNative.ScissorRect>());
        Assert.Equal(48, Marshal.SizeOf<MetalNative.Region>());
    }

    [Fact]
    public void MetalNative_SystemDefaultDevice_BindsMetalFrameworkPath()
    {
        var m = typeof(MetalNative).GetMethod("MTLCreateSystemDefaultDevice",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(m);
        Assert.Equal(typeof(IntPtr), m!.ReturnType);   // id<MTLDevice>
        Assert.Empty(m.GetParameters());               // (void)
        var attr = m.GetCustomAttribute<DllImportAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("/System/Library/Frameworks/Metal.framework/Metal", attr!.Value);
        Assert.Equal(CallingConvention.Cdecl, attr.CallingConvention);
    }

    // ---- ObjectiveC 入口与 MsgSend 委托族签名存在性(T3 消费契约) ----

    static MethodInfo Method(string name) => typeof(ObjectiveC).GetMethod(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"ObjectiveC.{name} 缺失");

    static void AssertSig(string name, Type[] parameters, Type ret)
    {
        var m = Method(name);
        Assert.Equal(parameters, m.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(ret, m.ReturnType);
    }

    [Fact]
    public void ObjectiveC_LibobjcEntryPoints_Exist()
    {
        AssertSig("GetClass", new[] { typeof(string) }, typeof(IntPtr));        // objc_getClass
        AssertSig("RegisterSel", new[] { typeof(string) }, typeof(IntPtr));     // sel_registerName(缓存字典)
        AssertSig("Ensure", Type.EmptyTypes, typeof(void));                     // IsMacOS 守卫加载
    }

    [Fact]
    public void ObjectiveC_MsgSendFamily_SignaturesMatchContract()
    {
        // 简报契约六件套
        AssertSig("MsgSend", new[] { typeof(IntPtr), typeof(IntPtr) }, typeof(IntPtr));
        AssertSig("MsgSendPtr", new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr) }, typeof(IntPtr));
        AssertSig("MsgSendPtrPtr",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr) }, typeof(IntPtr));
        AssertSig("MsgSendVoid", new[] { typeof(IntPtr), typeof(IntPtr) }, typeof(void));
        AssertSig("MsgSendVoidFloat4",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(float), typeof(float), typeof(float), typeof(float) },
            typeof(void));
        AssertSig("MsgSendNSError",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr).MakeByRefType() },
            typeof(IntPtr));   // out IntPtr error**
        // 自然扩展(按 Metal.h 签名逐参对位;命名=返回+参数形态)
        AssertSig("MsgSendVoidPtr", new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr) }, typeof(void));
        AssertSig("MsgSendVoidNUInt", new[] { typeof(IntPtr), typeof(IntPtr), typeof(UIntPtr) }, typeof(void));
        AssertSig("MsgSendPtrNUInt", new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(UIntPtr) }, typeof(IntPtr));
        AssertSig("MsgSendVoidPtrNUInt",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(UIntPtr) }, typeof(void));
        AssertSig("MsgSendVoidPtrNUIntNUInt",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(UIntPtr), typeof(UIntPtr) }, typeof(void));
        AssertSig("MsgSendNUIntNUInt",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(UIntPtr), typeof(UIntPtr) }, typeof(IntPtr));
        AssertSig("MsgSendVoidNUInt4",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(UIntPtr), typeof(UIntPtr), typeof(UIntPtr), typeof(UIntPtr) },
            typeof(void));
        AssertSig("MsgSendNSErrorWithOptions",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr).MakeByRefType() },
            typeof(IntPtr));   // newLibraryWithSource:options:error:(source, options, error**)
        AssertSig("MsgSendVoidDouble2",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(double), typeof(double) }, typeof(void));   // setDrawableSize:(CGSize)
        AssertSig("MsgSendVoidDouble4",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(double), typeof(double), typeof(double), typeof(double) },
            typeof(void));   // setClearColor:(MTLClearColor HFA ≡ 4×double 标量寄存器位)
        AssertSig("MsgSendVoidScissorRect",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(MetalNative.ScissorRect) }, typeof(void));
        AssertSig("MsgSendVoidGetBytes",
            new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(UIntPtr), typeof(MetalNative.Region), typeof(UIntPtr) },
            typeof(void));   // getBytes:bytesPerRow:fromRegion:mipmapLevel:
    }

    // ---- 平台守卫行为(Windows 宿主可真断言:守卫在原生加载之前) ----

    [Fact]
    public void ObjectiveC_Ensure_OnNonMacHost_ThrowsWithGuidance()
    {
        if (OperatingSystem.IsMacOS()) return;   // macOS 宿主:Ensure 走真加载,CI 实机验证
        var ex = Assert.Throws<InvalidOperationException>(() => ObjectiveC.Ensure());
        Assert.Contains("macOS", ex.Message);
    }

    [Fact]
    public void MetalNative_Ensure_OnNonMacHost_ThrowsWithGuidance()
    {
        if (OperatingSystem.IsMacOS()) return;
        var ex = Assert.Throws<InvalidOperationException>(() => MetalNative.Ensure());
        Assert.Contains("macOS", ex.Message);
    }

    // ---- MSL 着色器(编译验证待 CI macOS;本机做源码结构级断言) ----

    [Fact]
    public void Shaders_MslVertexSource_ContainsStructuralMarkers()
    {
        var src = Shaders.MslVertexSource;
        Assert.False(string.IsNullOrWhiteSpace(src));
        foreach (var marker in new[]
                 {
                     "#include <metal_stdlib>", "using namespace metal;",
                     "vertex ", "[[stage_in]]", "[[buffer(0)]]",
                     "[[attribute(0)]]", "[[attribute(1)]]", "[[attribute(2)]]", "[[position]]",
                     "vs_main",
                 })
            Assert.Contains(marker, src);
    }

    [Fact]
    public void Shaders_MslFragmentSource_ContainsStructuralMarkers()
    {
        var src = Shaders.MslFragmentSource;
        Assert.False(string.IsNullOrWhiteSpace(src));
        foreach (var marker in new[]
                 {
                     "#include <metal_stdlib>", "using namespace metal;",
                     "fragment ", "[[stage_in]]", "[[texture(0)]]", "[[sampler(0)]]",
                     "fs_main", "atlas.sample",
                 })
            Assert.Contains(marker, src);
    }

    [Fact]
    public void Shaders_MslSources_AttributeBracketsAreBalanced()
    {
        // 简报原稿 VsIn.texCoord 行 "[[attribute(1);" 缺 "]" 系笔误(同类先例:M3e T3 GLSL Y 方向笔误),
        // 实现按 MSL 语法修正;本测试锁住 "[[" 与 "]]" 计数配平,防该类笔误回归。
        foreach (var src in new[] { Shaders.MslVertexSource, Shaders.MslFragmentSource })
        {
            Assert.Equal(CountOf(src, "[["), CountOf(src, "]]"));
        }
    }

    static int CountOf(string s, string token)
    {
        int count = 0;
        for (int i = s.IndexOf(token, StringComparison.Ordinal); i >= 0; i = s.IndexOf(token, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void Shaders_MslVertexSource_YMappingMatchesD3DAndGlSemantics()
    {
        // Y 上行 NDC(1.0 - y/h*2)与 HlslVertexSource/Glsl330VertexSource 逐字符同式:
        // 三后端 NDC 同为 +1=窗口上沿,Metal 无 GL 式窗口原点差异吸收需求(待 CI 像素级复验)。
        Assert.Contains("1.0 - in.position.y / uSize.y * 2.0", Shaders.MslVertexSource);
        Assert.Contains("1.0 - i.position.y / u_screenSize.y * 2.0", Shaders.HlslVertexSource);
        Assert.Contains("1.0 - aPosition.y / uSize.y * 2.0", Shaders.Glsl330VertexSource);
    }
}
