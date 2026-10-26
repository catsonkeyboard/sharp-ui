using SharpUI;
using SharpUI.Backends;
using Xunit.Abstractions;

namespace SharpUI.Tests;

/// <summary>
/// D3 文本层 Task 4 金样本一次性生成器:**时序硬约束**——金样本只能由含 Skia 实现的树
/// (e67fe48 或其 worktree)生成,本仓库主线自 T4 起 TextAtlas 已是 FT/HB,**在此跑生成会用新实现
/// 重写 PNG → 对比恒零差、护栏无声死亡(自指死锁,T4 评审 Important #3)**。故写盘前要求双环境变量
/// 同时在场:`SHARPUI_REGEN_GOLDEN=1` + `SHARPUI_REGEN_GOLDEN_CONFIRM=<令牌>`(显式确认值);
/// 仅前者在场时不写盘并打印指引。**两令牌语义(终审 Minor#5)**:
/// <list type="bullet">
/// <item><c>SKIA</c>(历史令牌,Skia 树专用):操作者显式确认"当前树确实含 Skia 路径"——
/// 主线 FT/HB 树置此值仍是自指;配方(在含 Skia 的树上):
/// <code>git worktree add %TEMP%/sharpui-golden e67fe48
/// # 拷入本文件+TextGoldenTests.cs,测试 csproj 加 System.Drawing.Common
/// SHARPUI_REGEN_GOLDEN=1 SHARPUI_REGEN_GOLDEN_CONFIRM=SKIA dotnet test --filter TextGoldenGen</code></item>
/// <item><c>FT_REBASELINE</c>(有意变更再基线,FT 树合法路径):Skia 已永删,未来**有意的**布局/
/// 光栅变更需要重新锚定金样本时用此令牌——要求同时设 <c>SHARPUI_REBASELINE_REASON</c> 为非空
/// 变更说明,写盘后向 PNG 同目录 <c>REBASELINE.log</c> 追加一行"时间+原因"留痕审计。</item>
/// </list>
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

        string confirm = Environment.GetEnvironmentVariable("SHARPUI_REGEN_GOLDEN_CONFIRM");
        string? reason = Environment.GetEnvironmentVariable("SHARPUI_REBASELINE_REASON");

        if (confirm == "FT_REBASELINE")
        {
            // 有意变更再基线令牌(终审 Minor#5):无说明留痕即拒写,防"顺手再基线"绕过护栏
            if (string.IsNullOrWhiteSpace(reason))
            {
                output.WriteLine("SHARPUI_REGEN_GOLDEN_CONFIRM=FT_REBASELINE 但 SHARPUI_REBASELINE_REASON 为空,未写盘。");
                output.WriteLine("有意变更再基线须附非空变更说明(写入 PNG 同目录 REBASELINE.log 审计)。");
                return;
            }
        }
        else
        {
            // 硬闸(T4 评审 Important #3):本树若无 Skia 实现,重产即自指(PNG 出自被测实现自身)。
            // CONFIRM=SKIA 由操作者显式确认"当前树确实含 Skia 路径"——主线 FT/HB 树不应置此值。
            if (confirm != "SKIA")
            {
                output.WriteLine($"SHARPUI_REGEN_GOLDEN=1 已置位但 SHARPUI_REGEN_GOLDEN_CONFIRM 非法('{confirm}'),未写盘。");
                output.WriteLine("合法令牌:SKIA(历史,仅限含 Skia 实现的树,如 e67fe48 worktree)/ FT_REBASELINE(有意变更再基线,须设 SHARPUI_REBASELINE_REASON)。");
                output.WriteLine("两令牌语义见本类头注(TextGoldenGen.cs)。");
                return;
            }
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

        if (confirm == "FT_REBASELINE")
        {
            // 留痕审计(终审 Minor#5):时间+原因追加至 PNG 同目录,再基线历史可查
            string logPath = Path.Combine(dir, "REBASELINE.log");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tFT_REBASELINE\t{reason.Trim()}{Environment.NewLine}");
            output.WriteLine($"rebaseline logged: {logPath}");
        }
    }
}
