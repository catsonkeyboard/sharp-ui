using System.Diagnostics;
using SharpUI;
using SharpUI.Backends;
using static SharpUI.Ui;
using Xunit.Abstractions;

namespace SharpUI.Tests;

/// <summary>M3e T4:GL 后端可比测(perf-baselines 采集)。场景/断言与 PerfTests.CounterScene 全同,
/// 仅图集/上下文宿主 D3D11 headless → GL headless —— build→layout→tessellate 管线后端中立,数字
/// 应与 D3D11 持平(后端中立性实证);输出行供 perf-baselines.md 摘录。
/// 测试主机并行禁用见 AssemblyInfo.cs(SDL/GL 原生全局态 + IdResolver 静态双因,T4 实证)。</summary>
public class GlPerfTests
{
    readonly ITestOutputHelper _output;

    public GlPerfTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "Gpu")]
    public void CounterScene_GlHeadless_WithinAllocationAndCpuBudget()
    {
        using var ctx = GLContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        var mesh = new MeshBuilder();
        int state = 0;
        var screen = new SizeF(480, 320);
        var focus = new FocusManager();

        Element Build() => Column(
            Ui.Text("Counter").Size(28).Bold(),
            Ui.Text(state.ToString()).Size(48),
            Row(
                Ui.Button("−", () => state--).Key("minus"),
                Ui.Button("+", () => state++).Key("plus")
            ).Padding(0f),
            Ui.Immediate("debug", ui =>
            {
                ui.Text($"count = {state}", 14, 0x9AA2B3);
                if (ui.Button("Reset").Clicked()) state = 0;
            })
        ).Center().Gap(12f).Padding(24f);

        void Frame()
        {
            Element root = Build();
            LayoutEngine.Measure(root, screen);
            IdResolver.Assign(root);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            focus.BeginFrame();
            TreeWalk.Register(root, focus);
            mesh.Clear();
            root.Draw(new PaintContext(mesh, atlas, InputState.Empty));
        }

        for (int i = 0; i < 50; i++) Frame();   // 预热:字体缓存/委托/JIT

        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int Frames = 300;
        for (int i = 0; i < Frames; i++) Frame();
        sw.Stop();

        double bytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / (double)Frames;
        double msPerFrame = sw.Elapsed.TotalMilliseconds / Frames;
        _output.WriteLine($"GL perf: {bytesPerFrame:F0} B/frame, {msPerFrame:F2} ms/frame");
        Assert.True(bytesPerFrame <= 8192, $"每帧分配 {bytesPerFrame:F0}B 超预算 8192B");
        Assert.True(msPerFrame < 8.0, $"每帧 CPU {msPerFrame:F2}ms 超预算 8ms");
        App.Atlas = null;   // 用后复位:atlas 随 ctx dispose,防悬垂引用泄漏给后续测试(同 PerfTests 惯例)
    }
}
