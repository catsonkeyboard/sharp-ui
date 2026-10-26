using SharpUI;
using SharpUI.Backends;
using Xunit.Abstractions;

namespace SharpUI.Tests;

/// <summary>
/// D3 文本层 Task 4 金样本一次性生成器:**时序硬约束**——金样本只能由含 Skia 实现的树
/// (e67fe48 或其 worktree)生成,本仓库主线自 T4 起 TextAtlas 已是 FT/HB,**在此跑生成会用新实现
/// 重写 PNG → 对比恒零差、护栏无声死亡(自指死锁,T4 评审 Important #3)**。故写盘前要求双环境变量
/// 同时在场:`SHARPUI_REGEN_GOLDEN=1` + `SHARPUI_REGEN_GOLDEN_CONFIRM=SKIA`(显式确认值);
/// 仅前者在场时不写盘并打印指引。配方(在含 Skia 的树上):
/// <code>git worktree add %TEMP%/sharpui-golden e67fe48
/// # 拷入本文件+TextGoldenTests.cs,测试 csproj 加 System.Drawing.Common
/// SHARPUI_REGEN_GOLDEN=1 SHARPUI_REGEN_GOLDEN_CONFIRM=SKIA dotnet test --filter TextGoldenGen</code>
/// 常态全量跑此测试空过(env 门控,非断言路径)。
/// </summary>
public class TextGoldenGen(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void RegenerateGoldenCorpus()
    {
        if (Environment.GetEnvironmentVariable("SHARPUI_REGEN_GOLDEN") != "1")
            return;   // 门控 Skip:env 未置位时空过(xunit 2.x 无运行时 Skip,早退不产文件)

        // 硬闸(T4 评审 Important #3):本树若无 Skia 实现,重产即自指(PNG 出自被测实现自身)。
        // CONFIRM=SKIA 由操作者显式确认"当前树确实含 Skia 路径"——主线 FT/HB 树不应置此值。
        if (Environment.GetEnvironmentVariable("SHARPUI_REGEN_GOLDEN_CONFIRM") != "SKIA")
        {
            output.WriteLine("SHARPUI_REGEN_GOLDEN=1 已置位但缺少 SHARPUI_REGEN_GOLDEN_CONFIRM=SKIA,未写盘。");
            output.WriteLine("金样本只能由含 Skia 实现的树(如 e67fe48 worktree)生成;主线 FT/HB 树上重产会使对比测试自指失效。");
            output.WriteLine("配方见本类头注(TextGoldenGen.cs)。");
            return;
        }

        string dir = TextGoldenCorpus.GoldenDir();
        Directory.CreateDirectory(dir);
        byte[][] frames = TextGoldenCorpus.RenderCorpus();
        for (int i = 0; i < frames.Length; i++)
        {
            string path = Path.Combine(dir, $"{TextGoldenCorpus.Corpus[i].Name}.png");
            TextGoldenCorpus.SavePng(path, frames[i]);
            output.WriteLine($"regen: {path}");
        }
    }
}
