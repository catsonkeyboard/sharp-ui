using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>GL 3.3 core 函数加载器(M3e T2):函数指针经 SdlNative.GetGlProcAddress(SDL_GL_GetProcAddress)
/// 于首次 EnsureLoaded 全量解析并缓存字段;任一导出缺失即抛 InvalidOperationException(消息附函数名)。
/// 调用前置:SDL_GL_CreateContext + SDL_GL_MakeCurrent 已在属主模块完成(GL 入口依赖线程当前上下文)。
/// 调用约定:全部 [Winapi] —— GL 入口 WINAPI/stdcall 于 x64 与 C 约定统一(简报约定;GLboolean 1 字节,
/// normalized 以 U1 封送;指针宽度参数(GLintptr/GLsizeiptr/const void*)一律 IntPtr,防栈槽高 4 字节未定义)。
/// 常量出处:OpenGL 注册枚举实数,1.1 世代取本机 Windows SDK 10.0.26100.0 um/gl/GL.h,3.x 世代取 Khronos
/// OpenGL-Registry GLES3/gl3.h(枚举值与桌面 GL 统一),打包格式取 GL/glext.h GL_VERSION_1_2 块;逐行尾注。
/// 零 unsafe、NativeAOT 兼容(委托封送桩为具体类型实例化)。</summary>
internal static class GL
{
    static bool _loaded;

    // ---- 加载器 ----

    /// <summary>惰性全量解析(幂等;须已 MakeCurrent,否则全部指针为 NULL 即抛)。</summary>
    internal static void EnsureLoaded()
    {
        if (_loaded) return;
        _getString = R<GetStringFn>("glGetString");
        _getError = R<GetErrorFn>("glGetError");
        _createShader = R<CreateShaderFn>("glCreateShader");
        _shaderSource = R<ShaderSourceFn>("glShaderSource");
        _compileShader = R<CompileShaderFn>("glCompileShader");
        _getShaderiv = R<GetShaderivFn>("glGetShaderiv");
        _getShaderInfoLog = R<GetShaderInfoLogFn>("glGetShaderInfoLog");
        _deleteShader = R<DeleteShaderFn>("glDeleteShader");
        _createProgram = R<CreateProgramFn>("glCreateProgram");
        _attachShader = R<AttachShaderFn>("glAttachShader");
        _linkProgram = R<LinkProgramFn>("glLinkProgram");
        _getProgramiv = R<GetProgramivFn>("glGetProgramiv");
        _getProgramInfoLog = R<GetProgramInfoLogFn>("glGetProgramInfoLog");
        _useProgram = R<UseProgramFn>("glUseProgram");
        _genVertexArrays = R<GenVertexArraysFn>("glGenVertexArrays");
        _bindVertexArray = R<BindVertexArrayFn>("glBindVertexArray");
        _enableVertexAttribArray = R<EnableVertexAttribArrayFn>("glEnableVertexAttribArray");
        _vertexAttribPointer = R<VertexAttribPointerFn>("glVertexAttribPointer");
        _genBuffers = R<GenBuffersFn>("glGenBuffers");
        _bindBuffer = R<BindBufferFn>("glBindBuffer");
        _bufferData = R<BufferDataFn>("glBufferData");
        _bufferSubData = R<BufferSubDataFn>("glBufferSubData");
        _deleteBuffers = R<DeleteBuffersFn>("glDeleteBuffers");
        _bindBufferBase = R<BindBufferBaseFn>("glBindBufferBase");
        _genTextures = R<GenTexturesFn>("glGenTextures");
        _bindTexture = R<BindTextureFn>("glBindTexture");
        _texImage2D = R<TexImage2DFn>("glTexImage2D");
        _texSubImage2D = R<TexSubImage2DFn>("glTexSubImage2D");
        _deleteTextures = R<DeleteTexturesFn>("glDeleteTextures");
        _texParameteri = R<TexParameteriFn>("glTexParameteri");
        _activeTexture = R<ActiveTextureFn>("glActiveTexture");
        _enable = R<EnableFn>("glEnable");
        _disable = R<DisableFn>("glDisable");
        _blendFunc = R<BlendFuncFn>("glBlendFunc");
        _viewport = R<ViewportFn>("glViewport");
        _scissor = R<ScissorFn>("glScissor");
        _clearColor = R<ClearColorFn>("glClearColor");
        _clear = R<ClearFn>("glClear");
        _drawElements = R<DrawElementsFn>("glDrawElements");
        _getUniformLocation = R<GetUniformLocationFn>("glGetUniformLocation");
        _uniform1i = R<Uniform1iFn>("glUniform1i");
        _readPixels = R<ReadPixelsFn>("glReadPixels");
        _loaded = true;
    }

    static T R<T>(string name) where T : class
    {
        IntPtr p = SdlNative.GetGlProcAddress(name);
        if (p == IntPtr.Zero)
            throw new InvalidOperationException("GL 函数缺失:" + name);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    // ---- 常量(出处逐行尾注;枚举均为 GLenum) ----

    // 1.1 世代:本机 Windows SDK 10.0.26100.0 um/gl/GL.h
    internal const uint Version = 0x1F02;          // GL_VERSION(简报测试原稿 0x1F00 系笔误:0x1F00=GL_VENDOR)
    internal const uint Triangles = 0x0004;        // GL_TRIANGLES
    internal const uint Blend = 0x0BE2;            // GL_BLEND
    internal const uint ScissorTest = 0x0C11;      // GL_SCISSOR_TEST
    internal const uint ColorBufferBit = 0x4000;   // GL_COLOR_BUFFER_BIT
    internal const uint Rgb = 0x1907;              // GL_RGB(简报清单"RGB/RGBA 0x1908"混写:RGB 实为 0x1907)
    internal const uint Rgba = 0x1908;             // GL_RGBA
    internal const uint UnsignedByte = 0x1401;     // GL_UNSIGNED_BYTE
    internal const uint UnsignedShort = 0x1403;    // GL_UNSIGNED_SHORT
    internal const uint Float = 0x1406;            // GL_FLOAT
    internal const uint Texture2D = 0x0DE1;        // GL_TEXTURE_2D
    internal const uint Nearest = 0x2600;          // GL_NEAREST
    internal const uint TextureMagFilter = 0x2800; // GL_TEXTURE_MAG_FILTER
    internal const uint TextureMinFilter = 0x2801; // GL_TEXTURE_MIN_FILTER
    internal const uint TextureWrapS = 0x2802;     // GL_TEXTURE_WRAP_S
    internal const uint TextureWrapT = 0x2803;     // GL_TEXTURE_WRAP_T

    // 3.x 世代:Khronos OpenGL-Registry GLES3/gl3.h(枚举值与桌面 GL 统一)
    internal const uint Texture0 = 0x84C0;              // GL_TEXTURE0
    internal const uint ArrayBuffer = 0x8892;           // GL_ARRAY_BUFFER
    internal const uint ElementArrayBuffer = 0x8893;    // GL_ELEMENT_ARRAY_BUFFER
    internal const uint UniformBuffer = 0x8A11;         // GL_UNIFORM_BUFFER
    internal const uint StaticDraw = 0x88E4;            // GL_STATIC_DRAW
    internal const uint DynamicDraw = 0x88E8;           // GL_DYNAMIC_DRAW
    internal const uint VertexShader = 0x8B31;          // GL_VERTEX_SHADER
    internal const uint FragmentShader = 0x8B30;        // GL_FRAGMENT_SHADER
    internal const uint CompileStatus = 0x8B81;         // GL_COMPILE_STATUS
    internal const uint LinkStatus = 0x8B82;            // GL_LINK_STATUS
    internal const uint ClampToEdge = 0x812F;           // GL_CLAMP_TO_EDGE

    // 打包格式:Khronos GL/glext.h GL_VERSION_1_2 块(桌面 GL)
    internal const uint UnsignedInt8888Rev = 0x8367;    // GL_UNSIGNED_INT_8_8_8_8_REV

    // ---- 函数面(包装方法 → 私有委托字段;签名注释仅记封送要点) ----

    /// <summary>枚举字符串(GL_VERSION/GL_VENDOR 等);返回 ANSI 指针,调用方 PtrToStringAnsi。</summary>
    internal static IntPtr GetString(uint name) => _getString(name);

    internal static uint GetError() => _getError();

    internal static uint CreateShader(uint type) => _createShader(type);

    /// <summary>注入着色器源:count 个源码指针(strings 元素为调用方经 GCHandle 钉住的 UTF-8/ASCII 零终止串,
    /// 或 AllocHGlobal 拷贝),lengths 为对应长度(NULL 长度数组按零终止推)。</summary>
    internal static void ShaderSource(uint shader, int count, IntPtr[] strings, int[]? lengths) =>
        _shaderSource(shader, count, strings, lengths);

    internal static void CompileShader(uint shader) => _compileShader(shader);

    internal static void GetShaderiv(uint shader, uint pname, out int values) => _getShaderiv(shader, pname, out values);

    /// <summary>取编译日志到 infoLog(IntPtr,建议 AllocHGlobal + 1024);实际长度经 length 返回。</summary>
    internal static void GetShaderInfoLog(uint shader, int bufSize, out int length, IntPtr infoLog) =>
        _getShaderInfoLog(shader, bufSize, out length, infoLog);

    internal static void DeleteShader(uint shader) => _deleteShader(shader);

    internal static uint CreateProgram() => _createProgram();

    internal static void AttachShader(uint program, uint shader) => _attachShader(program, shader);

    internal static void LinkProgram(uint program) => _linkProgram(program);

    internal static void GetProgramiv(uint program, uint pname, out int values) => _getProgramiv(program, pname, out values);

    internal static void GetProgramInfoLog(uint program, int bufSize, out int length, IntPtr infoLog) =>
        _getProgramInfoLog(program, bufSize, out length, infoLog);

    internal static void UseProgram(uint program) => _useProgram(program);

    internal static void GenVertexArrays(int n, uint[] arrays) => _genVertexArrays(n, arrays);

    internal static void BindVertexArray(uint array) => _bindVertexArray(array);

    internal static void EnableVertexAttribArray(uint index) => _enableVertexAttribArray(index);

    /// <summary>顶点属性布局(绑定 VBO 后调用,offset 为 VBO 内字节偏移;指针参数按指针宽度传)。</summary>
    internal static void VertexAttribPointer(uint index, int size, uint type, bool normalized, int stride, IntPtr offset) =>
        _vertexAttribPointer(index, size, type, normalized, stride, offset);

    internal static void GenBuffers(int n, uint[] buffers) => _genBuffers(n, buffers);

    internal static void BindBuffer(uint target, uint buffer) => _bindBuffer(target, buffer);

    /// <summary>整块分配(target=ARRAY_BUFFER 等;size 字节;data=Zero 允许,留待 BufferSubData 填充)。</summary>
    internal static void BufferData(uint target, IntPtr size, IntPtr data, uint usage) =>
        _bufferData(target, size, data, usage);

    internal static void BufferSubData(uint target, IntPtr offset, IntPtr size, IntPtr data) =>
        _bufferSubData(target, offset, size, data);

    internal static void DeleteBuffers(int n, uint[] buffers) => _deleteBuffers(n, buffers);

    internal static void BindBufferBase(uint target, uint index, uint buffer) => _bindBufferBase(target, index, buffer);

    internal static void GenTextures(int n, uint[] textures) => _genTextures(n, textures);

    internal static void BindTexture(uint target, uint texture) => _bindTexture(target, texture);

    /// <summary>纹理上传(完整 9 参:target/level/internalFormat/width/height/border/format/type/pixels;
    /// 简报清单"12 参"系计数笔误,pixels=Zero 仅分配)。</summary>
    internal static void TexImage2D(uint target, int level, int internalFormat, int width, int height,
        int border, uint format, uint type, IntPtr pixels) =>
        _texImage2D(target, level, internalFormat, width, height, border, format, type, pixels);

    /// <summary>纹理子区更新(完整 9 参;pixels 为调用方钉住的像素数据)。</summary>
    internal static void TexSubImage2D(uint target, int level, int xoffset, int yoffset, int width, int height,
        uint format, uint type, IntPtr pixels) =>
        _texSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels);

    internal static void DeleteTextures(int n, uint[] textures) => _deleteTextures(n, textures);

    internal static void TexParameteri(uint target, uint pname, int value) => _texParameteri(target, pname, value);

    internal static void ActiveTexture(uint texture) => _activeTexture(texture);

    internal static void Enable(uint cap) => _enable(cap);

    internal static void Disable(uint cap) => _disable(cap);

    internal static void BlendFunc(uint sfactor, uint dfactor) => _blendFunc(sfactor, dfactor);

    internal static void Viewport(int x, int y, int width, int height) => _viewport(x, y, width, height);

    internal static void Scissor(int x, int y, int width, int height) => _scissor(x, y, width, height);

    internal static void ClearColor(float red, float green, float blue, float alpha) =>
        _clearColor(red, green, blue, alpha);

    internal static void Clear(uint mask) => _clear(mask);

    /// <summary>索引绘制(indices 为 ELEMENT_ARRAY_BUFFER 绑定时的字节偏移,或客户端指针)。</summary>
    internal static void DrawElements(uint mode, int count, uint type, IntPtr indices) =>
        _drawElements(mode, count, type, indices);

    /// <summary>取 uniform 槽位(-1=未命中/被编译器裁剪);name 以 ANSI 零终止串封送。</summary>
    internal static int GetUniformLocation(uint program, string name) => _getUniformLocation(program, name);

    internal static void Uniform1i(int location, int v0) => _uniform1i(location, v0);

    /// <summary>读回像素矩形(完整 7 参;data 预分配后经 GCHandle 钉,格式 RGBA/UNSIGNED_BYTE 行序自下而上)。</summary>
    internal static void ReadPixels(int x, int y, int width, int height, uint format, uint type, byte[] data)
    {
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { _readPixels(x, y, width, height, format, type, pin.AddrOfPinnedObject()); }
        finally { pin.Free(); }
    }

    // ---- 委托字段(EnsureLoaded 赋值;类型即原生签名,GLboolean→U1 bool、指针宽度→IntPtr) ----

    static GetStringFn _getString = null!;
    static GetErrorFn _getError = null!;
    static CreateShaderFn _createShader = null!;
    static ShaderSourceFn _shaderSource = null!;
    static CompileShaderFn _compileShader = null!;
    static GetShaderivFn _getShaderiv = null!;
    static GetShaderInfoLogFn _getShaderInfoLog = null!;
    static DeleteShaderFn _deleteShader = null!;
    static CreateProgramFn _createProgram = null!;
    static AttachShaderFn _attachShader = null!;
    static LinkProgramFn _linkProgram = null!;
    static GetProgramivFn _getProgramiv = null!;
    static GetProgramInfoLogFn _getProgramInfoLog = null!;
    static UseProgramFn _useProgram = null!;
    static GenVertexArraysFn _genVertexArrays = null!;
    static BindVertexArrayFn _bindVertexArray = null!;
    static EnableVertexAttribArrayFn _enableVertexAttribArray = null!;
    static VertexAttribPointerFn _vertexAttribPointer = null!;
    static GenBuffersFn _genBuffers = null!;
    static BindBufferFn _bindBuffer = null!;
    static BufferDataFn _bufferData = null!;
    static BufferSubDataFn _bufferSubData = null!;
    static DeleteBuffersFn _deleteBuffers = null!;
    static BindBufferBaseFn _bindBufferBase = null!;
    static GenTexturesFn _genTextures = null!;
    static BindTextureFn _bindTexture = null!;
    static TexImage2DFn _texImage2D = null!;
    static TexSubImage2DFn _texSubImage2D = null!;
    static DeleteTexturesFn _deleteTextures = null!;
    static TexParameteriFn _texParameteri = null!;
    static ActiveTextureFn _activeTexture = null!;
    static EnableFn _enable = null!;
    static DisableFn _disable = null!;
    static BlendFuncFn _blendFunc = null!;
    static ViewportFn _viewport = null!;
    static ScissorFn _scissor = null!;
    static ClearColorFn _clearColor = null!;
    static ClearFn _clear = null!;
    static DrawElementsFn _drawElements = null!;
    static GetUniformLocationFn _getUniformLocation = null!;
    static Uniform1iFn _uniform1i = null!;
    static ReadPixelsFn _readPixels = null!;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetStringFn(uint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetErrorFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint CreateShaderFn(uint type);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ShaderSourceFn(uint shader, int count, IntPtr[] strings, int[]? lengths);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CompileShaderFn(uint shader);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetShaderivFn(uint shader, uint pname, out int values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetShaderInfoLogFn(uint shader, int bufSize, out int length, IntPtr infoLog);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DeleteShaderFn(uint shader);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint CreateProgramFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void AttachShaderFn(uint program, uint shader);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void LinkProgramFn(uint program);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetProgramivFn(uint program, uint pname, out int values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetProgramInfoLogFn(uint program, int bufSize, out int length, IntPtr infoLog);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void UseProgramFn(uint program);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GenVertexArraysFn(int n, uint[] arrays);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BindVertexArrayFn(uint array);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void EnableVertexAttribArrayFn(uint index);
    // offset 为指针宽度参数(GLintptr;原生 const void*):IntPtr 而非 int —— x64 栈传参高 4 字节未定义
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VertexAttribPointerFn(uint index, int size, uint type,
        [MarshalAs(UnmanagedType.U1)] bool normalized, int stride, IntPtr offset);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GenBuffersFn(int n, uint[] buffers);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BindBufferFn(uint target, uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BufferDataFn(uint target, IntPtr size, IntPtr data, uint usage);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BufferSubDataFn(uint target, IntPtr offset, IntPtr size, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DeleteBuffersFn(int n, uint[] buffers);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BindBufferBaseFn(uint target, uint index, uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GenTexturesFn(int n, uint[] textures);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BindTextureFn(uint target, uint texture);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TexImage2DFn(uint target, int level, int internalFormat, int width, int height,
        int border, uint format, uint type, IntPtr pixels);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TexSubImage2DFn(uint target, int level, int xoffset, int yoffset, int width, int height,
        uint format, uint type, IntPtr pixels);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DeleteTexturesFn(int n, uint[] textures);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TexParameteriFn(uint target, uint pname, int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ActiveTextureFn(uint texture);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void EnableFn(uint cap);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DisableFn(uint cap);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BlendFuncFn(uint sfactor, uint dfactor);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ViewportFn(int x, int y, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ScissorFn(int x, int y, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ClearColorFn(float red, float green, float blue, float alpha);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ClearFn(uint mask);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawElementsFn(uint mode, int count, uint type, IntPtr indices);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    private delegate int GetUniformLocationFn(uint program, [MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Uniform1iFn(int location, int v0);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ReadPixelsFn(int x, int y, int width, int height, uint format, uint type, IntPtr data);
}
