namespace SharpUI;

public static class Shaders
{
    public const string VertexSource = """
        #version 450
        layout(location = 0) in vec2 in_position;
        layout(location = 1) in vec2 in_texCoord;
        layout(location = 2) in vec4 in_color;
        layout(set = 0, binding = 0) uniform t_Uniforms { vec2 u_screenSize; };
        layout(location = 0) out vec2 f_texCoord;
        layout(location = 1) out vec4 f_color;
        void main()
        {
            vec2 clip = vec2(in_position.x / u_screenSize.x * 2.0 - 1.0,
                             1.0 - in_position.y / u_screenSize.y * 2.0);
            gl_Position = vec4(clip, 0.0, 1.0);
            f_texCoord = in_texCoord;
            f_color = in_color;
        }
        """;

    public const string FragmentSource = """
        #version 450
        layout(set = 0, binding = 1) uniform texture2D t_atlas;
        layout(set = 0, binding = 2) uniform sampler t_atlasSampler;
        layout(location = 0) in vec2 f_texCoord;
        layout(location = 1) in vec4 f_color;
        layout(location = 0) out vec4 o_color;
        void main()
        {
            vec4 texel = texture(sampler2D(t_atlas, t_atlasSampler), f_texCoord);
            o_color = f_color * texel;
        }
        """;

    /// <summary>M3d-ii:D3D11 HLSL(vs_5_0/ps_5_0 运行时编译)。与上方 GLSL 语义等价;
    /// GLSL 源保留供 M3e OpenGL 后端复用(D3 决策)。
    /// PSIn 首成员必须为 SV_Position:D3D11 级间链接按硬件寄存器配对(VS 输出 r0=SV_Position,
    /// r1/r2=TEXCOORD/COLOR);PS 不声明 SV_Position 会使 TEXCOORD/COLOR 落在 r0/r1 →
    /// "mismatched hardware registers" 链接错误,采样/顶点色读 0(Task 3 真机调试层实证)。</summary>
    public const string HlslVertexSource = """
        cbuffer ScreenSize : register(b0)
        {
            float2 u_screenSize;
        };

        struct VSIn
        {
            float2 position : POSITION;
            float2 texCoord : TEXCOORD0;
            float4 color : COLOR0;
        };

        struct VSOut
        {
            float4 position : SV_Position;
            float2 texCoord : TEXCOORD0;
            float4 color : COLOR0;
        };

        VSOut VSMain(VSIn i)
        {
            VSOut o;
            o.position = float4(i.position.x / u_screenSize.x * 2.0 - 1.0,
                                1.0 - i.position.y / u_screenSize.y * 2.0, 0.0, 1.0);
            o.texCoord = i.texCoord;
            o.color = i.color;
            return o;
        }
        """;

    public const string HlslFragmentSource = """
        Texture2D atlas : register(t0);
        SamplerState atlasSampler : register(s0);

        struct PSIn
        {
            float4 position : SV_Position;   // 寄存器对齐必需,见 HlslVertexSource 注释
            float2 texCoord : TEXCOORD0;
            float4 color : COLOR0;
        };

        float4 PSMain(PSIn i) : SV_Target
        {
            return i.color * atlas.Sample(atlasSampler, i.texCoord);
        }
        """;

    /// <summary>M3e T3:OpenGL 3.3 core 顶点源。Y 映射与 D3D11 HLSL 版完全一致(1.0 - y/h*2),不做
    /// "相对 D3D 翻转"——简报原稿片段(y/h*2-1)系方向性笔误:两 API 的 NDC 同为 +1=窗口上沿,
    /// GL "原点左下"只影响窗口坐标系(viewport/scissor/readpixels 行序),不影响 NDC→窗口映射的方向;
    /// 若在着色器里翻转 Y,内容在帧缓冲内垂直镜像,可见窗口上整个 UI 上下颠倒,且与读回行序翻转叠加
    /// 后双翻转。GL 的自底向上差异统一吸收在三处:本源不翻 Y、glScissor Y 翻转(fbH-y-h)、
    /// glReadPixels 行序翻转(见 GLContext)——像素测试(两段 startIndex 的左上/右下就位)锁定方向。
    /// UBO 未加 layout(binding=)(420pack 特性,330 core 无):绑定点经 glGetUniformBlockIndex+
    /// glUniformBlockBinding 显式落 0(GLContext 构造期)。</summary>
    public const string Glsl330VertexSource = """
        #version 330 core
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec2 aTexCoord;
        layout(location = 2) in vec4 aColor;   // UNSIGNED_BYTE normalized:内存 R,G,B,A 顺序直读
        layout(std140) uniform ScreenSize { vec2 uSize; };   // 单 vec2 成员占 16 字节槽(std140)
        out vec2 vTexCoord;
        out vec4 vColor;
        void main()
        {
            gl_Position = vec4(aPosition.x / uSize.x * 2.0 - 1.0,
                               1.0 - aPosition.y / uSize.y * 2.0, 0.0, 1.0);
            vTexCoord = aTexCoord;
            vColor = aColor;
        }
        """;

    /// <summary>M3e T3:OpenGL 3.3 core 片段源。采样器绑定纹理单元 0(GLContext 构造期
    /// glGetUniformLocation("uAtlas")+glUniform1i(loc,0)+ActiveTexture(TEXTURE0));预乘顶点色 × 纹理,
    /// 混合 (ONE, ONE_MINUS_SRC_ALPHA) 由 GLContext.BeginFrame 落地。</summary>
    public const string Glsl330FragmentSource = """
        #version 330 core
        in vec2 vTexCoord;
        in vec4 vColor;
        uniform sampler2D uAtlas;   // 纹理单元 0
        out vec4 outColor;
        void main()
        {
            outColor = vColor * texture(uAtlas, vTexCoord);
        }
        """;

    /// <summary>跨平台 T2:Apple Metal(MSL)顶点源。与 GLSL330/HLSL 版语义等价:Y 上行 NDC
    /// (1.0 - y/h*2,Metal NDC 同 +1=窗口上沿,无 GL 式窗口原点差异吸收需求)、预乘白×顶点色。
    /// MSL 形态:metal_stdlib + using namespace metal;vertex 函数修饰;[[stage_in]] 顶点描述符
    /// 装配(T3 的 MTLVertexDescriptor attributes 0/1/2 对位 [[attribute(N)]]);uniform 走
    /// constant 地址空间引用 [[buffer(0)]](T3 setVertexBuffer:offset:atIndex: 槽 0,上载 ≥8 字节
    /// float2)。简报原稿 VsIn.texCoord 行 "[[attribute(1);" 缺 "]" 系笔误(同类先例:M3e T3
    /// GLSL Y 方向笔误),按 MSL 语法修正为 [[attribute(1)]];结构与括号配平由 ObjectiveCTests
    /// 锁定。源码字符串经 newLibraryWithSource:options:error: 运行时编译 —— 本机无 Metal
    /// 编译器,编译/链接验证待 CI macOS(标注:待 CI)。</summary>
    public const string MslVertexSource = """
        #include <metal_stdlib>
        using namespace metal;

        struct VsIn
        {
            float2 position [[attribute(0)]];
            float2 texCoord [[attribute(1)]];
            float4 color    [[attribute(2)]];
        };

        struct VsOut
        {
            float4 position [[position]];
            float2 texCoord;
            float4 color;
        };

        vertex VsOut vs_main(VsIn in [[stage_in]], constant float2& uSize [[buffer(0)]])
        {
            VsOut o;
            o.position = float4(in.position.x / uSize.x * 2.0 - 1.0,
                                1.0 - in.position.y / uSize.y * 2.0, 0.0, 1.0);
            o.texCoord = in.texCoord;
            o.color = in.color;
            return o;
        }
        """;

    /// <summary>跨平台 T2:Apple Metal(MSL)片段源。atlas [[texture(0)]] / 采样器
    /// [[sampler(0)]](T3 setFragmentTexture:atIndex:0 + setFragmentSamplerState:atIndex:0);
    /// 预乘顶点色 × 纹理,混合 (One, OneMinusSourceAlpha) 由 Metal 后端管线状态落地(对位
    /// D3D11/GL 两后端)。attribute(2) 顶点色在 T3 侧以 UChar4 normalized 顶点格式供入
    /// (HLSL COLOR0 先例:内存 RGBA 直读)。编译验证待 CI macOS。</summary>
    public const string MslFragmentSource = """
        #include <metal_stdlib>
        using namespace metal;

        struct VsOut
        {
            float4 position [[position]];
            float2 texCoord;
            float4 color;
        };

        fragment float4 fs_main(VsOut in [[stage_in]],
                                texture2d<float> atlas [[texture(0)]],
                                sampler atlasSampler [[sampler(0)]])
        {
            return in.color * atlas.sample(atlasSampler, in.texCoord);
        }
        """;
}
