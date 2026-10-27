using System.Diagnostics;
using SharpUI;
using SharpUI.Backends;
using Xunit.Abstractions;

namespace SharpUI.Tests;

/// <summary>
/// D4 T4 / D6 M-c: D3D11 动态缓冲部分上传(Map/WRITE_DISCARD + RtlMoveMemory)微基线入库。
/// 复刻 1 / 8 / 32 个 ClipRun 区段的 <see cref="Renderer.Submit"/> 真实上传+绘制路径计时(使 49× 微基线入库可复现)。
/// 常态全量测试默认早退 0ms(同 <see cref="TextGoldenGen"/> 模式);置环境变量 <c>SHARPUI_RUN_BENCH=1</c> 时执行预热与 2000 帧计时并打印 μs/帧。
/// </summary>
public class UploadBenchTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void D3D11PartialUpload_1_8_32_ClipSegments_MicroBenchmark()
    {
        if (Environment.GetEnvironmentVariable("SHARPUI_RUN_BENCH") != "1")
            return;   // 常态早退 0ms(同 TextGoldenGen 环境变量门控模式)

        using var ctx = D3D11Context.CreateHeadless(256, 256);
        using var renderer = new Renderer(ctx);
        var screen = new SizeF(256, 256);
        const int TotalQuads = 256;
        const int WarmupFrames = 100;
        const int MeasureFrames = 2000;

        foreach (int segments in new[] { 1, 8, 32 })
        {
            MeshBuilder mesh = BuildSegmentedMesh(segments, TotalQuads);
            Assert.Equal(segments, mesh.ClipRuns.Length);

            for (int i = 0; i < WarmupFrames; i++)
                renderer.Submit(mesh, screen);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < MeasureFrames; i++)
                renderer.Submit(mesh, screen);
            sw.Stop();

            double usPerFrame = sw.Elapsed.TotalMilliseconds * 1000.0 / MeasureFrames;
            output.WriteLine($"segments={segments,2} (quads={TotalQuads}): {usPerFrame:F1} us/frame ({sw.Elapsed.TotalMilliseconds:F2} ms / {MeasureFrames} frames)");
            Assert.True(usPerFrame < 5000.0, $"segments={segments} 上传+绘制均值 {usPerFrame:F1} us/frame 异常偏高");
        }
    }

    static MeshBuilder BuildSegmentedMesh(int segments, int totalQuads)
    {
        var mesh = new MeshBuilder();
        var uv = new RectF(0f, 0f, 0.01f, 0.01f);
        var color = Color32.FromRgb(0x3B82F6);
        int quadsPerSegment = totalQuads / segments;
        for (int s = 0; s < segments; s++)
        {
            mesh.SetClip(new RectF(0f, s * 4f, 256f, 4f));
            for (int q = 0; q < quadsPerSegment; q++)
            {
                float x = (q % 16) * 12f;
                float y = s * 4f;
                mesh.AddQuad(new RectF(x, y, 8f, 3f), uv, color);
            }
        }
        return mesh;
    }
}
