using System.Diagnostics;
using SharpUI;
using static SharpUI.Ui;

namespace SharpUI.Tests;

[Collection("interaction")]   // Frame() 调 IdResolver.Assign,与 IdResolver 静态检测共享串行集
public class PerfTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void CounterScene_WithinAllocationAndCpuBudget()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        var mesh = new MeshBuilder();
        int state = 0;
        var screen = new SizeF(480, 320);
        var focus = new FocusManager();   // 帧外单例:避免每帧分配污染计量

        Element Build() => Column(
            Text("Counter").Size(28).Bold(),
            Text(state.ToString()).Size(48),
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
        Assert.True(bytesPerFrame <= 64 * 1024, $"每帧分配 {bytesPerFrame:F0}B 超预算 65536B");
        Assert.True(msPerFrame < 8.0, $"每帧 CPU {msPerFrame:F2}ms 超预算 8ms");

        // testhost 工作目录在 tests/SharpUI.Tests/bin/{cfg}/net10.0,上溯 5 级到仓库根
        // 仅 Release 写盘:Debug 数字不能覆盖验收记录(SDK 项目仅在 Debug 定义 DEBUG)
#if !DEBUG
        string baselinePath = Path.GetFullPath("../../../../../docs/notes/perf-baselines.md");
        Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
        File.WriteAllText(baselinePath,
            $"# 性能基线(自动生成)\n\n- 每帧分配:{bytesPerFrame:F0} B(预算 65536)\n- 每帧 CPU:{msPerFrame:F2} ms(预算 8)\n- 场景:Counter,480×320,Release 测得(含 id+焦点+即时面板管线)\n");
#endif
        App.Atlas = null;
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void VirtualList10k_StaysBounded()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        App.State = new StateStore();
        var mesh = new MeshBuilder();
        var screen = new SizeF(480, 320);
        VirtualListElement list = Ui.VirtualList("big", 10000, 24f, static (i, row) =>
            row.Add(new TextElement($"item {i}")));
        void Frame()
        {
            LayoutEngine.Arrange(list, new RectF(0, 0, screen.Width, screen.Height));
            mesh.Clear();
            list.Draw(new PaintContext(mesh, atlas, InputState.Empty));
        }
        for (int i = 0; i < 30; i++) Frame();                       // 预热
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int Frames = 200;
        for (int i = 0; i < Frames; i++) Frame();
        sw.Stop();
        double bpf = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Frames;
        Assert.True(mesh.VertexCount < 4000, $"visible mesh {mesh.VertexCount} verts unbounded");
        Assert.True(bpf <= 64 * 1024, $"每帧分配 {bpf:F0}B 超预算");
        Assert.True(sw.Elapsed.TotalMilliseconds / Frames < 8.0);
        App.State = null;
        App.Atlas = null;
    }
}
