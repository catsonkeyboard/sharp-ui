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
    /// GLSL 源保留供 M3e OpenGL 后端复用(D3 决策)。</summary>
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
            float2 texCoord : TEXCOORD0;
            float4 color : COLOR0;
        };

        float4 PSMain(PSIn i) : SV_Target
        {
            return i.color * atlas.Sample(atlasSampler, i.texCoord);
        }
        """;
}
