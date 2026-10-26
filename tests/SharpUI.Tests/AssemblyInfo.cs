using Xunit;

// M3e T4 实证裁定:测试主机禁并行(xunit 默认类间并行)。
// 本测试程序集重度触碰两类进程级全局态,均不容忍并行:
//  ① SDL/GL 原生全局态(模块加载/SDL_Init/GL 当前上下文线程绑定)——并行 GPU 类 0xC0000005
//     崩测试主机实测(GLContextTests.EnsureVideoInit × GL 窗口 harness 并行,桌面锁定态稳定复现);
//  ② IdResolver 进程级静态(_duplicates,见 IdResolverTests 头注)。
// 全串行总时长实测 ~6s(纯逻辑毫秒级),正确性优先;新增原生/静态触碰测试零挂集心智负担。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
