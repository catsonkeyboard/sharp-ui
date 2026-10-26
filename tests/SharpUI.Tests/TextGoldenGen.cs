using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>
/// D3 文本层 Task 4 金样本一次性生成器:**时序硬约束**——本测试在 TextAtlas 重写(Skia→FT/HB)
/// **之前**以 SHARPUI_REGEN_GOLDEN=1 运行,产 6 张 PNG 入库 tests/golden/text/;此后重写,
/// 对比由 TextGoldenTests 以新实现 vs PNG 完成。常态全量跑此测试空过(env 门控,非断言路径),
/// 重产金样本须显式:
/// <code>SHARPUI_REGEN_GOLDEN=1 dotnet test --filter TextGoldenGen</code>
/// </summary>
public class TextGoldenGen
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void RegenerateGoldenCorpus()
    {
        if (Environment.GetEnvironmentVariable("SHARPUI_REGEN_GOLDEN") != "1")
            return;   // 门控 Skip:env 未置位时空过(xunit 2.x 无运行时 Skip,早退不产文件)

        string dir = TextGoldenCorpus.GoldenDir();
        Directory.CreateDirectory(dir);
        byte[][] frames = TextGoldenCorpus.RenderCorpus();
        for (int i = 0; i < frames.Length; i++)
        {
            string path = Path.Combine(dir, $"{TextGoldenCorpus.Corpus[i].Name}.png");
            TextGoldenCorpus.SavePng(path, frames[i]);
        }
    }
}
