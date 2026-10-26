using System.Runtime.InteropServices;
using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>GL 基座(M3e T2):隐藏窗 GL 3.3 core 上下文 + GL 函数加载器。
/// 简报样例三处属性/枚举笔误已按头文件实数修正(同 D3D11Native 简报笔误修正先例,出处见 SdlNative/GL 注释):
/// SDL_GL_DOUBLEBUFFER=5(非 14,14=MULTISAMPLESAMPLES)、PROFILE_CORE=0x0001(非 0x0002,后者=COMPATIBILITY)、
/// GL_VERSION=0x1F02(非 0x1F00,0x1F00=GL_VENDOR)。版本断言按简报自注用解析式(4.x 驱动不给 "3.3" 字面)。
/// 测试主机并行禁用见 AssemblyInfo.cs(T4 实证:GPU 类并行 0xC0000005 崩主机)。</summary>
public class GLBaseTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void GlContext_33Core_CreatesAndClearsOnHiddenWindow()
    {
        SdlTextInput.EnableImeUi();
        SdlNative.EnsureVideoInit();
        // 时序:EnsureVideoInit(全候选)→ SetGlAttribute → 建窗(App.CreateWindow 同款;建窗前属性须就位)
        SdlNative.SetGlAttribute(SdlNative.GlAttrContextMajorVersion, 3);
        SdlNative.SetGlAttribute(SdlNative.GlAttrContextMinorVersion, 3);
        SdlNative.SetGlAttribute(SdlNative.GlAttrContextProfileMask, SdlNative.GlProfileCore);
        SdlNative.SetGlAttribute(SdlNative.GlAttrDoubleBuffer, 1);
        var win = new Veldrid.Sdl2.Sdl2Window("gl-base-test", 50, 50, 64, 64,
            Veldrid.Sdl2.SDL_WindowFlags.OpenGL | Veldrid.Sdl2.SDL_WindowFlags.Hidden, false);
        IntPtr ctx = SdlNative.CreateGlContext(win.SdlWindowHandle);
        Assert.NotEqual(IntPtr.Zero, ctx);
        try
        {
            Assert.Equal(0, SdlNative.MakeCurrent(win.SdlWindowHandle, ctx));
            GL.EnsureLoaded();
            // 版本解析式断言(简报自注):提取前两段数字,major>3 || (major==3 && minor>=3)
            string ver = Marshal.PtrToStringAnsi(GL.GetString(GL.Version))!;
            string[] nums = ver.Split(' ')[0].Split('.');
            Assert.True(nums.Length >= 2 && int.TryParse(nums[0], out int major) && int.TryParse(nums[1], out int minor)
                && (major > 3 || (major == 3 && minor >= 3)), $"GL 版本不足 3.3:{ver}");
            GL.ClearColor(0.07f, 0.07f, 0.08f, 1f);
            GL.Clear(GL.ColorBufferBit);
            byte[] px = new byte[64 * 64 * 4];
            GL.ReadPixels(0, 0, 64, 64, GL.Rgba, GL.UnsignedByte, px);
            // GL 行序自下而上:(0,0)=屏幕左下;清屏色一致无方向性,任取两点。
            // GL RGBA 字节序:R=0.07*255≈17.85 四舍五入 18;A=255
            Assert.Equal(18, px[0]);
            Assert.Equal(255, px[3]);
        }
        finally { win.Close(); }
    }
}
