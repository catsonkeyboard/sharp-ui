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
}
