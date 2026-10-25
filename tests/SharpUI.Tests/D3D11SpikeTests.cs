using SharpUI.Backends;
using Veldrid.Sdl2;

namespace SharpUI.Tests;

[Collection("interaction")]   // SDL2 进程全局 + D3D11Spike 静态状态,并入静态用户串行集(惯例)
public class D3D11SpikeTests
{


    [Fact]
    [Trait("Category", "Gpu")]
    public void ClearPresentLoop_SixtyColorRotatingFrames_RefcountsConserved()
    {
        // 真窗 640×480 小窗闪现(简报:无隐藏 API;60 帧 vsync ≈1s)
        D3D11Spike.InitSdl();   // Veldrid 窗口构造不自 Init SDL(实证),构造前先初始化视频子系统
        var window = new Sdl2Window("sharpui d3d11 spike", 80, 80, 640, 480, SDL_WindowFlags.Shown, false);
        try
        {
            IntPtr hwnd = D3D11Spike.GetWin32Hwnd(window);
            Assert.NotEqual(IntPtr.Zero, hwnd);

            for (int i = 0; i < 60; i++)
            {
                float t = i / 60f;   // 三相正弦颜色轮转
                float r = 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * t);
                float g = 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * (t + 1f / 3f));
                float b = 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * (t + 2f / 3f));
                Assert.True(D3D11Spike.RunFrame(hwnd, r, g, b), $"第 {i} 帧 Present 失败");
                if (i % 15 == 0) _ = window.PumpEvents();   // 喂事件泵,避免"未响应"
            }

            // 引用计数守恒:AddRef/Release 往返在 Refcounts() 内部执行,不一致即抛;此处再断言当前计数均 ≥1
            foreach ((string name, uint count) in D3D11Spike.Refcounts())
                Assert.True(count >= 1, $"{name} 引用计数 {count} 异常(应 ≥1)");

            // 公平 Reset:全释放后无残留状态,且可立即重建再呈一帧(证明 Release 平衡、无僵死引用)
            D3D11Spike.Reset();
            Assert.False(D3D11Spike.HasLiveState);
            Assert.True(D3D11Spike.RunFrame(hwnd, 0.2f, 0.3f, 0.4f));
            D3D11Spike.Reset();
            Assert.False(D3D11Spike.HasLiveState);
        }
        finally { window.Close(); }
    }
}
