using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>M3e T4:后端选择(SHARPUI_BACKEND)纯函数归一 + GL 窗口路径 harness 冒烟。
/// harness 与 M3d-ii T4 同法:真窗(可见)+ GLContext.CreateForWindow + 90 帧(清屏+白四边形)读回断言
/// —— 桌面锁定降级标准:进程内无异常 + 读回像素命中即过(像素正确性由读回承证,不依赖 vblank/交互)。
/// 简报样例的 SetGlAttribute 字面量经 SdlNative 常量引用(T2 勘误已入实码:DOUBLEBUFFER=5 非 14、
/// PROFILE_CORE=0x0001 非 0x0002),不重抄数字。
/// 测试主机并行禁用见 AssemblyInfo.cs(T4 实证:GPU 类并行 0xC0000005 崩主机)。</summary>
public class BackendSelectionTests
{
    [Theory]
    [InlineData("gl", "gl")]
    [InlineData("d3d11", "d3d11")]
    [InlineData("GL", "gl")]
    [InlineData("", "default")]
    [InlineData("bogus", "default")]
    [InlineData(null, "default")]
    [InlineData(" OpenGL ", "gl")]
    [InlineData("d3d", "d3d11")]
    [InlineData("metal", "metal")]        // 裁定#48:metal 归一(trim+小写;别名面仅此一形)
    [InlineData("Metal", "metal")]
    [InlineData(" metal ", "metal")]
    public void BackendName_Normalizes(string? raw, string expected) =>
        Assert.Equal(expected, App.NormalizeBackendName(raw));

    /// <summary>裁定#48 纯逻辑:Windows 上选 metal 抛 PlatformNotSupportedException(带指引)。
    /// 守卫前置于 EnableImeUi/EnsureVideoInit 之前的 CreateWindow 首段 —— 无 SDL/GPU 动作即可断言;
    /// 仅 Windows 断言(macOS/Linux 的 metal 分派归 CI 实机;env 经 try/finally 复位防串测)。</summary>
    [Fact]
    public void MetalBackend_OnWindows_ThrowsPlatformNotSupported()
    {
        if (!OperatingSystem.IsWindows()) return;
        Environment.SetEnvironmentVariable("SHARPUI_BACKEND", "metal");
        try
        {
            Assert.Throws<PlatformNotSupportedException>(() => App.CreateWindow("metal-reject", 64, 64, null));
        }
        finally { Environment.SetEnvironmentVariable("SHARPUI_BACKEND", null); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void GlWindowHarness_90FramesClearAndQuad_PixelsStable()
    {
        // 时序 = App 接线同款:EnableImeUi → EnsureVideoInit(全候选,属性才能到达属主副本)→
        // GL 属性(建窗前)→ 带OpenGL flag 建窗 → CreateForWindow。
        SdlTextInput.EnableImeUi();
        SdlNative.EnsureVideoInit();
        GLContext.SetCoreProfileAttributes();
        var win = new SdlWindow("gl-harness", 60, 60, 64, 64,
            SdlWindowFlags.OpenGL | SdlWindowFlags.Shown);
        try
        {
            using var ctx = GLContext.CreateForWindow(win.SdlWindowHandle, 64, 64);
            ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
            float inv = 1f / ctx.AtlasSize;
            var verts = new UiVertex[4];
            for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
                new PointF(i % 2 == 0 ? 16f : 48f, i < 2 ? 16f : 48f),
                new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv), Color32.White);
            ushort[] idx = { 0, 1, 2, 2, 1, 3 };
            for (int f = 0; f < 90; f++)
            {
                ctx.SetScreenSize(64, 64);
                ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
                ctx.SetScissor(0, 0, 64, 64);
                ctx.Draw(verts, 4, idx, 6, 0);
                // 末帧不交换:SwapWindow 后后备内容 GL 语义未定义(discard 合法),读回目标须为本帧绘制结果;
                // 前 89 次 Present 已证 SwapWindow 持续稳定(VSync 节奏构造期已设)。
                if (f < 89) ctx.Present();
            }
            byte[] px = ctx.ReadBackMainFramebuffer();   // 窗口帧缓冲读回(窗口路径;行序已翻转到左上原点)
            // GL RGBA + 行序翻转:白 (255,255,255,255) @中心;清屏 (0.07,0.07,0.08,1) @角落 → [18,18,20,255]
            int i1 = (32 * 64 + 32) * 4, i2 = (4 * 64 + 4) * 4;
            Assert.Equal(255, px[i1]); Assert.Equal(255, px[i1 + 3]);
            Assert.Equal(18, px[i2]); Assert.Equal(20, px[i2 + 2]); Assert.Equal(255, px[i2 + 3]);
        }
        finally { win.Dispose(); }   // T3:自有层 Close 仅置标记,Dispose 回收原生窗+出泵
    }
}
