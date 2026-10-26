using System.Runtime.InteropServices;
using System.Text;

namespace SharpUI.Backends;

/// <summary>
/// M3e Task 3:OpenGL 3.3 core 渲染上下文,实现后端中立 IRenderContext(第二后端,经 SDL 原生 GL 路线)。
/// headless 路径 = 64×64 隐藏 GL 窗口 + 自建上下文(像素验证);窗口路径 = 调用方窗口上自建上下文(T4 接线)。
/// 纪律:零 unsafe、NativeAOT 兼容、绘制路径零新分配(直钉调用者数组 + glBufferSubData 显式长度,
/// 无 D3D11 整块 scratch 之需 —— 上传量恒等于当帧实际长度,无越界面)。
/// Y 轴三处落实(GL 默认帧缓冲原点左下,与 D3D11 的全部差异收敛于此):
///   ① 顶点投影不翻 Y(NDC 两 API 同向,翻 Y 会使帧缓冲内容垂直镜像 → 可见窗 UI 上下颠倒,见
///      Shaders.Glsl330VertexSource 注释);
///   ② glScissor Y 翻转:目标像素坐标(左上原点,接口契约)→ 窗口坐标 glScissor(x, fbH-y-h, w, h);
///   ③ glReadPixels 行序翻转:底行起 → ReadBackMainFramebuffer 翻转到左上原点(与 D3D11 读回一致)。
/// 线程/上下文纪律:GL 当前上下文为线程全局态,MakeCurrent 落在四类"帧入口"(SetScreenSize/BeginFrame/
/// UpdateAtlasRegion/ReadBackMainFramebuffer —— Renderer.Submit 恒以 SetScreenSize 开帧、TextAtlas 恒经
/// UpdateAtlasRegion 上传,故 SetScissor/Draw 骑乘既有绑定,零额外 wgl 调用);GL.EnsureLoaded 前置
/// MakeCurrent(T2 契约:无当前上下文时 GetProcAddress 全 NULL)。
/// </summary>
public sealed class GLContext : IRenderContext
{
    /// <summary>图集边长(IRenderContext 契约:构造期显式上传全零初始内容,见 CreatePipeline 步 6)。</summary>
    public uint AtlasSize => 1024;

    public uint TargetWidth { get; private set; }
    public uint TargetHeight { get; private set; }

    bool _disposed;
    readonly IntPtr _window;                 // SDL 窗口句柄(headless=自建持有;窗口路径=调用方窗口,不持有)
    readonly bool _ownsWindow;               // headless=true:Dispose 关窗;窗口路径=false(T4 持窗)
    Veldrid.Sdl2.Sdl2Window? _windowObj;     // 自建窗口对象(仅 headless;Dispose Close)
    IntPtr _glContext;                       // SDL_GLContext(属主模块创建,Dispose 于同模块销毁)
    bool _pipelineReady;                     // GL 对象已建(Dispose 的 GL 删除门;构造失败路径保护)

    // ---- GL 对象(名字在上下文销毁时自动全量回收,Dispose 的显式删除为卫生性) ----
    uint _program, _vao, _vbo, _ibo, _ubo, _atlasTexture;

    // ---- 上传缓冲容量(首 256KB/512KB,×2 增长,同 D3D11;GL 只需 BufferData 孤儿化重分配,名字不变) ----
    uint _vboCapacityBytes = 256 * 1024, _iboCapacityBytes = 512 * 1024;

    // ---- 常驻钉扎槽(帧频路径零分配) ----
    readonly float[] _cb = new float[2];     // 屏幕尺寸常量(std140 vec2:UBO 16 字节槽,上传前 8 字节)
    GCHandle _cbPin;

    GLContext(IntPtr window, uint width, uint height, bool ownsWindow)
    {
        _window = window;
        _ownsWindow = ownsWindow;
        TargetWidth = width;
        TargetHeight = height;
        try
        {
            _glContext = SdlNative.CreateGlContext(window);
            if (_glContext == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"SDL_GL_CreateContext 失败(窗口 0x{window:X}):3.3 core/双缓冲属性未达属主模块或像素格式不可用");
            MakeCurrent();
            GL.EnsureLoaded();
            _ = SdlNative.SetSwapInterval(1);   // VSync 建上下文后设一次;失败忽略(隐藏窗/驱动不支持)
            CreatePipeline();
        }
        catch
        {
            try { Dispose(); }   // 构造失败路径:释放尽力,不掩盖原始异常
            catch { }
            throw;
        }
    }

    // ---- 工厂 ----

    /// <summary>headless 上下文(像素验证):64×64(参数尺寸)隐藏 GL 窗 + 3.3 core 自建上下文;
    /// 窗口与上下文由本对象持有,Dispose 释放。GL 属性须建窗前落全候选模块
    /// (SdlNative.EnsureVideoInit/SetGlAttribute 已按评审裁定 Important#1 ①② 全候选收)。</summary>
    public static GLContext CreateHeadless(uint width, uint height)
    {
        ValidateSize(width, height);
        SdlNative.EnsureVideoInit();
        SetCoreProfileAttributes();
        var win = new Veldrid.Sdl2.Sdl2Window("sharpui-gl-headless", 50, 50, (int)width, (int)height,
            Veldrid.Sdl2.SDL_WindowFlags.OpenGL | Veldrid.Sdl2.SDL_WindowFlags.Hidden, false);
        try
        {
            var ctx = new GLContext(win.SdlWindowHandle, width, height, ownsWindow: true);
            ctx._windowObj = win;
            return ctx;
        }
        catch { win.Close(); throw; }
    }

    /// <summary>窗口渲染上下文:调用方 SDL 窗口上自建 GL 上下文并持有;窗口本身不持有(Dispose 不关调用方窗)。
    /// 调用方接线时序(App.CreateWindow 同款):EnsureVideoInit → SetCoreProfileAttributes → 带 OpenGL
    /// flag 建窗 → 本工厂 —— 本工厂不自设属性(建窗后 SDL_GL_SetAttribute 已迟,属性须建窗时点就位)。
    /// 上下文独立于调用方可能已有的上下文(wglCreateContextAttribs 无共享参数,两上下文对象命名空间隔离)。</summary>
    public static GLContext CreateForWindow(IntPtr sdlWindow, uint width, uint height)
    {
        if (sdlWindow == IntPtr.Zero) throw new ArgumentException("sdlWindow 不能为空", nameof(sdlWindow));
        ValidateSize(width, height);
        return new GLContext(sdlWindow, width, height, ownsWindow: false);
    }

    static void ValidateSize(uint width, uint height)
    {
        if (width == 0 || height == 0)
            throw new ArgumentException($"目标尺寸必须为正(width={width}, height={height})");
    }

    /// <summary>建窗前设置 3.3 core + 双缓冲 GL 属性(T4 提为 internal:App 接线与 CreateHeadless
    /// 共用,单一真相)。时序硬约束:① 先 SdlNative.EnsureVideoInit(全候选 SDL_Init VIDEO)——
    /// SDL_GL_SetAttribute 须全候选可达,某副本视频未初始化即拒收属性,窗口(Veldrid 建窗落点不定)
    /// 恰落在该副本时上下文即非 3.3 core(T3 已实证该保证,兼容上下文亦过版本下限断言);
    /// ② 建窗前(属性须在建窗时点已就位);③ 建窗须带 OpenGL flag。</summary>
    internal static void SetCoreProfileAttributes()
    {
        SetAttr(SdlNative.GlAttrContextMajorVersion, 3, "CONTEXT_MAJOR_VERSION");
        SetAttr(SdlNative.GlAttrContextMinorVersion, 3, "CONTEXT_MINOR_VERSION");
        SetAttr(SdlNative.GlAttrContextProfileMask, SdlNative.GlProfileCore, "CONTEXT_PROFILE_MASK(core)");
        SetAttr(SdlNative.GlAttrDoubleBuffer, 1, "DOUBLEBUFFER");
    }

    static void SetAttr(int attr, int value, string name)
    {
        int r = SdlNative.SetGlAttribute(attr, value);
        if (r != 0)
            throw new InvalidOperationException($"SDL_GL_SetAttribute({name}={value}) 失败:{r}(全候选拒收,建窗前属性不可靠)");
    }

    // ---- IRenderContext ----

    /// <summary>仅记录新尺寸:GL 窗口由 SDL 管理(交换链尺寸事件在 T4 窗口层处理),viewport 于下次
    /// BeginFrame 按 TargetWidth/Height 刷新。不接受 0(同 D3D11 语义);headless 不抛
    /// (GL headless 无离屏目标重建问题,与 D3D11 headless NotSupportedException 语义之差由实现取舍)。</summary>
    public void Resize(uint width, uint height)
    {
        ThrowIfDisposed();
        ValidateSize(width, height);
        TargetWidth = width;
        TargetHeight = height;
    }

    /// <summary>图集区域上传(RGBA 字节,左上原点 —— GL 纹理行 0 即首个上传字节,与 UV (0,0) 对应,
    /// 两 API 同构无需翻转):pin rgba + TexSubImage2D。纹理上传坐标即图集坐标,不经 Y 翻转。</summary>
    public void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint width, uint height)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rgba);
        if (width == 0 || height == 0) return;
        if (x + width > AtlasSize || y + height > AtlasSize)
            throw new ArgumentOutOfRangeException($"图集区域越界({x}+{width} x {y}+{height} > {AtlasSize})");
        if (rgba.Length < checked(width * height * 4))
            throw new ArgumentException($"rgba 长度不足:需 {width * height * 4},实有 {rgba.Length}", nameof(rgba));
        MakeCurrent();   // TextAtlas 可能在帧外/他窗上下文时上传(MakeCurrent 幂等快路径)
        GCHandle pin = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            GL.BindTexture(GL.Texture2D, _atlasTexture);
            GL.TexSubImage2D(GL.Texture2D, 0, (int)x, (int)y, (int)width, (int)height,
                GL.Rgba, GL.UnsignedByte, pin.AddrOfPinnedObject());
        }
        finally { pin.Free(); }
    }

    /// <summary>帧起始:MakeCurrent(多窗切换)+ Viewport(0,0,w,h)+ Disable(SCISSOR)+ ClearColor+Clear +
    /// Enable(BLEND)+BlendFunc(ONE, ONE_MINUS_SRC_ALPHA)(预乘,对应 D3D11 BlendOne/InvSrcAlpha)。
    /// 帧边界 scissor 复位(评审 Important#1):GL 规范 glClear 受 scissor test 作用,上一帧末残留的
    /// 部分剪裁(滚动面板内被裁文本最后绘制)会把本帧清屏裁成子矩形 → 拖影;D3D11
    /// ClearRenderTargetView 不受 scissor 影响,此处 Disable 对齐镜像后端的无 scissor 清屏语义。
    /// 双缓冲默认读写缓冲均 BACK:清/绘/读同一后备缓冲,headless Present no-op 保读回有效。</summary>
    public void BeginFrame(float r, float g, float b, float a)
    {
        ThrowIfDisposed();
        MakeCurrent();
        GL.Viewport(0, 0, (int)TargetWidth, (int)TargetHeight);
        GL.Disable(GL.ScissorTest);   // 必须先于 Clear:glClear 受 scissor test 作用(GL 规范)
        GL.ClearColor(r, g, b, a);
        GL.Clear(GL.ColorBufferBit);
        GL.Enable(GL.Blend);
        GL.BlendFunc(GL.One, GL.OneMinusSrcAlpha);
    }

    /// <summary>顶点着色器屏幕尺寸常量(逻辑像素):std140 UBO 单 vec2 成员(16 字节槽)前 8 字节
    /// BufferSubData(垫后 8 字节构造期已零,着色器仅读 vec2)。</summary>
    public void SetScreenSize(float logicalWidth, float logicalHeight)
    {
        ThrowIfDisposed();
        MakeCurrent();   // Renderer.Submit 恒以本方法开帧:后续 SetScissor/Draw 骑乘此绑定
        _cb[0] = logicalWidth;
        _cb[1] = logicalHeight;
        GL.BindBuffer(GL.UniformBuffer, _ubo);
        GL.BufferSubData(GL.UniformBuffer, IntPtr.Zero, (IntPtr)(sizeof(float) * 2), _cbPin.AddrOfPinnedObject());
    }

    /// <summary>剪裁矩形(目标像素坐标,左上原点,经换算后的最终值)。与目标全等时 Disable(SCISSOR)
    /// (Renderer 对 FullClip 传全屏矩形,禁用即全屏,等价且省状态);否则 Enable + Y 翻转:
    /// glScissor(x, fbH-y-h, w, h) —— GL 窗口坐标原点左下,目标像素坐标原点左上。</summary>
    public void SetScissor(uint x, uint y, uint w, uint h)
    {
        ThrowIfDisposed();
        if (x == 0 && y == 0 && w == TargetWidth && h == TargetHeight)
        {
            GL.Disable(GL.ScissorTest);
            return;
        }
        GL.Enable(GL.ScissorTest);
        GL.Scissor((int)x, checked((int)(TargetHeight - y - h)), (int)w, (int)h);
    }

    /// <summary>绘制一段索引几何(零分配,上传语义取舍见 IRenderContext.Draw 注释):直钉调用者数组 +
    /// glBufferSubData 显式长度(上传量=当帧实际长度,无 D3D11 整块 ByteWidth 越界面,故无 scratch);
    /// 顶点整段 [0..vertexCount),索引整段 [0..startIndex+indexCount) —— 缓冲恒镜像调用者数组前缀,
    /// 分段经 DrawElements 原生索引字节偏移 (startIndex×2) 取段,所引索引恒新鲜上传,无陈旧尾段引用。
    /// 成本对比(D3D11 重定基,见其 Draw 注释):D3D11 UpdateSubresource 整块语义恒上传全部容量
    /// (256KB+512KB/次),维持 GPU 侧偏移还会使末段重传几乎整个前缀,故重定基;GL 本方案每次上传
    /// 前缀(≤ 当帧数据),已优于 D3D11 按需性,且免分段拷贝;更优解(仅传段内子区:BufferSubData
    /// 偏移 + 指针偏移)从简报取舍舍去 —— 前缀镜像不变量简单,DrawElements 偏移路径由像素测试锁定。
    /// GCHandle.Alloc/Free 为句柄表操作,无 GC 堆分配。</summary>
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

        EnsureCapacity(vertexCount, checked((int)(startIndex + indexCount)));
        GCHandle vp = GCHandle.Alloc(vertices, GCHandleType.Pinned);
        GCHandle ip = GCHandle.Alloc(indices, GCHandleType.Pinned);
        try
        {
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ArrayBuffer, _vbo);
            GL.BufferSubData(GL.ArrayBuffer, IntPtr.Zero,
                (IntPtr)((long)vertexCount * UiVertex.SizeInBytes), vp.AddrOfPinnedObject());
            GL.BindBuffer(GL.ElementArrayBuffer, _ibo);   // VAO 态:要素缓冲绑定(名字未变,幂等)
            GL.BufferSubData(GL.ElementArrayBuffer, IntPtr.Zero,
                (IntPtr)(((long)startIndex + indexCount) * sizeof(ushort)), ip.AddrOfPinnedObject());
        }
        finally { ip.Free(); vp.Free(); }
        GL.DrawElements(GL.Triangles, (int)indexCount, GL.UnsignedShort, (IntPtr)(startIndex * sizeof(ushort)));
    }

    /// <summary>呈现:窗口路径 SwapWindow(VSync 节奏已由构造期 SetSwapInterval(1) 设定);
    /// headless no-op(接口契约 —— 不交换,后备缓冲保持可读回)。</summary>
    public void Present()
    {
        ThrowIfDisposed();
        if (_ownsWindow) return;   // headless:隐藏窗 no-op
        SdlNative.SwapWindow(_window);
    }

    /// <summary>读回主目标(GPU 测试用):glReadPixels 全帧(默认读缓冲 BACK,即本帧绘制目标)+
    /// 行序翻转到左上原点(GL 行序自下而上,翻转后与 D3D11 读回同向;字节序 GL=RGBA,与 D3D11 BGRA 异,
    /// 断言值按 RGBA)。测试路径,分配允许。</summary>
    public byte[] ReadBackMainFramebuffer()
    {
        ThrowIfDisposed();
        MakeCurrent();
        int w = (int)TargetWidth, h = (int)TargetHeight;
        byte[] pixels = new byte[w * h * 4];
        GL.ReadPixels(0, 0, w, h, GL.Rgba, GL.UnsignedByte, pixels);   // 行 0=窗口底行
        int stride = w * 4;
        byte[] row = new byte[stride];
        for (int y = 0; y < h / 2; y++)   // 原地行交换:row r ↔ row h-1-r
        {
            int top = y * stride, bottom = (h - 1 - y) * stride;
            Buffer.BlockCopy(pixels, top, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom, stride);
        }
        return pixels;
    }

    // ---- 管线构建(一次性) ----

    void CreatePipeline()
    {
        // 1) 着色器(GLSL 330 core,Shaders.cs;编译/链接失败显式抛,信息取 InfoLog)
        uint vs = CompileShader(GL.VertexShader, Shaders.Glsl330VertexSource, "vertex");
        uint fs = CompileShader(GL.FragmentShader, Shaders.Glsl330FragmentSource, "fragment");
        try
        {
            _program = GL.CreateProgram();
            GL.AttachShader(_program, vs);
            GL.AttachShader(_program, fs);
            GL.LinkProgram(_program);
            GL.GetProgramiv(_program, GL.LinkStatus, out int linked);
            if (linked == 0)
                throw new InvalidOperationException($"GL 程序链接失败:{ProgramInfoLog(_program)}");
            GL.UseProgram(_program);   // 本上下文唯一程序,一次绑定终身有效(上下文态独立,多窗不串)
        }
        finally
        {
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);   // 链接后着色器对象可删(程序持引用)
        }

        // 2) UBO 绑定点显式落 0(330 core 无 layout(binding);默认值 0 属驱动层状态,不作假设)
        uint block = GL.GetUniformBlockIndex(_program, "ScreenSize");
        if (block == GL.InvalidIndex)
            throw new InvalidOperationException("uniform 块 ScreenSize 未命中(着色器被裁剪或名字不符)");
        GL.UniformBlockBinding(_program, block, 0);

        // 3) 图集采样器 → 纹理单元 0
        int atlasLoc = GL.GetUniformLocation(_program, "uAtlas");
        if (atlasLoc < 0)
            throw new InvalidOperationException("uAtlas uniform 未命中(着色器被裁剪或名字不符)");
        GL.Uniform1i(atlasLoc, 0);
        GL.ActiveTexture(GL.Texture0);

        // 4) VAO + 顶点/索引/常量缓冲(VAO 记录 ArrayBuffer 绑定于 VertexAttribPointer 时点、
        //    ElementArrayBuffer 绑定为 VAO 态;容量增长经 BufferData 孤儿化重分配,名字不变 → VAO 引用恒有效)
        var one = new uint[1];
        GL.GenVertexArrays(1, one);
        _vao = one[0];
        GL.BindVertexArray(_vao);

        var bufs = new uint[3];
        GL.GenBuffers(3, bufs);
        _vbo = bufs[0];
        _ibo = bufs[1];
        _ubo = bufs[2];
        GL.BindBuffer(GL.ArrayBuffer, _vbo);
        GL.BufferData(GL.ArrayBuffer, (IntPtr)_vboCapacityBytes, IntPtr.Zero, GL.DynamicDraw);
        GL.BindBuffer(GL.ElementArrayBuffer, _ibo);
        GL.BufferData(GL.ElementArrayBuffer, (IntPtr)_iboCapacityBytes, IntPtr.Zero, GL.DynamicDraw);
        GL.BindBuffer(GL.UniformBuffer, _ubo);
        _cbPin = GCHandle.Alloc(_cb, GCHandleType.Pinned);
        var cb16 = new byte[16];   // std140:vec2 单成员占 16 字节槽(对齐),垫后 8 字节零
        var cbPin = GCHandle.Alloc(cb16, GCHandleType.Pinned);
        try
        {
            GL.BufferData(GL.UniformBuffer, (IntPtr)16, cbPin.AddrOfPinnedObject(), GL.DynamicDraw);
        }
        finally { cbPin.Free(); }
        GL.BindBufferBase(GL.UniformBuffer, 0, _ubo);

        // 5) 顶点属性布局(与 UiVertex 20 字节一致:X/Y float2@0、U/V float2@8、R/G/B/A unorm byte4@16;
        //    UNSIGNED_BYTE normalized 以内存 R,G,B,A 顺序解释,与 D3D11 R8G8B8A8_UNORM 同结果)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, GL.Float, false, (int)UiVertex.SizeInBytes, (IntPtr)0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, GL.Float, false, (int)UiVertex.SizeInBytes, (IntPtr)8);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, GL.UnsignedByte, true, (int)UiVertex.SizeInBytes, (IntPtr)16);

        // 6) 图集:1024² RGBA8 + NEAREST/CLAMP_TO_EDGE(同 D3D11 Point/Clamp);
        //    TexImage2D 携 4MB 全零像素(GL 语义 null 像素=内容未定义,不得依赖驱动清零 —— 
        //    IRenderContext.AtlasSize 契约:构造期显式全零,字形 padding 依赖透明零)
        GL.GenTextures(1, one);
        _atlasTexture = one[0];
        GL.BindTexture(GL.Texture2D, _atlasTexture);
        GL.TexParameteri(GL.Texture2D, GL.TextureMinFilter, (int)GL.Nearest);
        GL.TexParameteri(GL.Texture2D, GL.TextureMagFilter, (int)GL.Nearest);
        GL.TexParameteri(GL.Texture2D, GL.TextureWrapS, (int)GL.ClampToEdge);
        GL.TexParameteri(GL.Texture2D, GL.TextureWrapT, (int)GL.ClampToEdge);
        var zeros = new byte[AtlasSize * AtlasSize * 4];   // 一次性 4MB(构造期)
        var zPin = GCHandle.Alloc(zeros, GCHandleType.Pinned);
        try
        {
            GL.TexImage2D(GL.Texture2D, 0, (int)GL.Rgba8, (int)AtlasSize, (int)AtlasSize, 0,
                GL.Rgba, GL.UnsignedByte, zPin.AddrOfPinnedObject());
        }
        finally { zPin.Free(); }

        _pipelineReady = true;
        uint err = GL.GetError();   // 构建期收尾错误巡检(sticky 标志,一次性清空读取)
        if (err != 0)
            throw new InvalidOperationException($"GL 管线构建后残留错误标志:0x{err:X8}");
    }

    /// <summary>上传容量保障:超容(×2,同旧 Renderer.EnsureCapacity)时 BufferData 孤儿化重分配
    /// (重新分配同名缓冲存储,VAO 属性绑定按名字引用不受影响;无 D3D11 的 ByteWidth=scratch 不变量,
    /// 故无需圆整到 20 倍数)。</summary>
    void EnsureCapacity(int vertexCount, int totalIndexCount)
    {
        uint vbNeed = (uint)((long)vertexCount * UiVertex.SizeInBytes);
        uint ibNeed = (uint)((long)totalIndexCount * sizeof(ushort));
        if (vbNeed > _vboCapacityBytes)
        {
            _vboCapacityBytes = Math.Max(_vboCapacityBytes * 2, vbNeed);
            GL.BindBuffer(GL.ArrayBuffer, _vbo);
            GL.BufferData(GL.ArrayBuffer, (IntPtr)_vboCapacityBytes, IntPtr.Zero, GL.DynamicDraw);
        }
        if (ibNeed > _iboCapacityBytes)
        {
            _iboCapacityBytes = Math.Max(_iboCapacityBytes * 2, ibNeed);
            GL.BindBuffer(GL.ElementArrayBuffer, _ibo);
            GL.BufferData(GL.ElementArrayBuffer, (IntPtr)_iboCapacityBytes, IntPtr.Zero, GL.DynamicDraw);
        }
    }

    static uint CompileShader(uint type, string source, string what)
    {
        uint shader = GL.CreateShader(type);
        if (shader == 0)
            throw new InvalidOperationException($"glCreateShader({what}) 返回 0");
        byte[] ascii = new byte[source.Length + 1];   // ASCII 源 + 尾部 NUL
        Encoding.ASCII.GetBytes(source, 0, source.Length, ascii, 0);
        GCHandle pin = GCHandle.Alloc(ascii, GCHandleType.Pinned);
        try
        {
            IntPtr[] stringsArr = { pin.AddrOfPinnedObject() };   // lengths=null:按零终止推
            GL.ShaderSource(shader, 1, stringsArr, null);
        }
        finally { pin.Free(); }
        GL.CompileShader(shader);
        GL.GetShaderiv(shader, GL.CompileStatus, out int ok);
        if (ok == 0)
            throw new InvalidOperationException($"{what} 着色器编译失败:{ShaderInfoLog(shader)}");
        return shader;
    }

    static string ShaderInfoLog(uint shader)
    {
        IntPtr buf = Marshal.AllocHGlobal(1024);
        try
        {
            GL.GetShaderInfoLog(shader, 1024, out int len, buf);
            return len > 0 ? Marshal.PtrToStringAnsi(buf, len)! : "(空日志)";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static string ProgramInfoLog(uint program)
    {
        IntPtr buf = Marshal.AllocHGlobal(1024);
        try
        {
            GL.GetProgramInfoLog(program, 1024, out int len, buf);
            return len > 0 ? Marshal.PtrToStringAnsi(buf, len)! : "(空日志)";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    void MakeCurrent()
    {
        int r = SdlNative.MakeCurrent(_window, _glContext);
        if (r != 0)
            throw new InvalidOperationException($"SDL_GL_MakeCurrent 失败:{r}(窗口 0x{_window:X})");
    }

    // ---- 释放 ----

    /// <summary>GL 对象显式删除(须上下文 current;名字 0 被 GL 静默忽略,构造失败路径安全)→
    /// 上下文销毁(属主模块;SDL 自带当前上下文解绑语义)→ 自建隐藏窗 Close。
    /// GL 上下文销毁本身即回收其全部对象,显式删除为卫生性留痕(同 D3D11 ReleaseTrace 纪律)。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pipelineReady)
        {
            try
            {
                MakeCurrent();
                GL.DeleteTextures(1, new uint[] { _atlasTexture });
                GL.DeleteBuffers(3, new uint[] { _vbo, _ibo, _ubo });
                GL.DeleteVertexArrays(1, new uint[] { _vao });
                GL.DeleteProgram(_program);
            }
            catch { /* 对象随上下文销毁自动回收,删除失败不阻断释放链 */ }
        }
        if (_cbPin.IsAllocated) _cbPin.Free();
        if (_glContext != IntPtr.Zero)
        {
            SdlNative.DeleteGlContext(_window, _glContext);   // 属主已缓存于 CreateGlContext 时点
            _glContext = IntPtr.Zero;
        }
        if (_ownsWindow) _windowObj?.Close();
        _windowObj = null;
    }

    void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GLContext));
    }
}
