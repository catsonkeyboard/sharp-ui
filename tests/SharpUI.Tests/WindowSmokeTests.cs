using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>T4(收官):窗口层自研后双后端窗口冒烟 + 进程内全 App 路径冒烟(T3 评审携带项)。
/// harness 选择论证:像素读回断言要求末帧不 Present(GL SwapWindow 后备内容未定义语义/D3D11 blt-model
/// Present 后 discard 合法),App.Run 单体循环不可注入读回点 —— 故窗口+上下文经 App.CreateWindow
/// (internal 缝,App.Create 同款全路径:EnableImeUi→EnsureVideoInit→SHARPUI_BACKEND 路由→flags→
/// 上下文→Renderer/图集→resize/失焦接线),帧核(90 帧清屏+白四边形,末帧不交换)与读回断言由本类驱动
/// —— 较 T2 GL 直组装 harness(BackendSelectionTests.GlWindowHarness,仅 SdlWindow+GLContext)更全,
/// 与其互补保留;全 App 循环(Run 本体)由 MaxFrames 冒烟承证。测试主机禁并行见 AssemblyInfo.cs。</summary>
public class WindowSmokeTests
{
    /// <summary>双后端各一:App.CreateWindow 路由断言(上下文类型)+ 真窗 90 帧清屏/四边形读回。
    /// 像素字节序随后端:D3D11 读回 [B,G,R,A](清屏 0.07/0.07/0.08/1 → 角落 [20,18,18,255]),
    /// GL 读回 RGBA(同色 → [18,18,20,255]);白四边形中心两后端均 [255,255,255,255]。</summary>
    [Theory]
    [InlineData("d3d11")]
    [InlineData("gl")]
    [Trait("Category", "Gpu")]
    public void AppWindowHarness_BothBackends_RoutingAnd_90FramesClearAndQuad(string backend)
    {
        if (backend == "gl" && !GlSupport.Available) return;   // CI 无 GPU 环境 gl 分支早退,见 GlSupport
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            Environment.SetEnvironmentVariable("SHARPUI_BACKEND", backend);
            AppWindow w = App.CreateWindow("sharpui-t4-harness-" + backend, 200, 150, null);
            try
            {
                // 路由断言(App 全路径,补 NormalizeBackendName 纯函数单测之不足):
                // d3d11 → GetWin32Hwnd + D3D11Context.CreateForWindow;gl → OpenGL flag 窗 + GLContext。
                if (backend == "gl") Assert.IsType<GLContext>(w.Ctx);
                else Assert.IsType<D3D11Context>(w.Ctx);
                Assert.InRange(w.Scale, 0.5f, 4f);   // DPI/96 创建定格(多屏/虚拟屏下 1.0~1.59 浮动)

                // 环境自适应(2026-10-27 裁定):Windows 最小窗宽钳制(SM_CXMIN)与虚拟屏驱动
                // (网易UU远程 GameViewer Virtual Display Adapter 残留,新窗落虚拟屏被最大化钳制)
                // 会改写请求尺寸 —— 期望值一律按实际 TargetWidth/Height 派生,不写死 200×150。
                // 帧核 = GlWindowHarness 同款:白图集像素 + 清屏 + 居中白四边形;末帧不交换(读回目标=本帧结果)。
                IRenderContext ctx = w.Ctx;
                uint W = ctx.TargetWidth, H = ctx.TargetHeight;   // ≥1;中心/角落坐标按实际派生
                ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
                float inv = 1f / ctx.AtlasSize;
                var verts = new UiVertex[4];
                for (int i = 0; i < 4; i++) verts[i] = UiVertex.Create(
                    new PointF(i % 2 == 0 ? W * 0.35f : W * 0.65f, i < 2 ? H * 0.30f : H * 0.70f),
                    new PointF(i % 2 == 0 ? 0f : inv, i < 2 ? 0f : inv), Color32.White);
                ushort[] idx = { 0, 1, 2, 2, 1, 3 };
                for (int f = 0; f < 90; f++)
                {
                    ctx.SetScreenSize(W, H);
                    ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
                    ctx.SetScissor(0, 0, W, H);
                    ctx.Draw(verts, 4, idx, 6, 0);
                    if (f < 89) ctx.Present();   // 前 89 次 Present 证持续稳定(VSync 节奏构造期已设)
                }
                byte[] px = w.Ctx switch   // ReadBackMainFramebuffer 为后端具体面(非 IRenderContext 契约)
                {
                    GLContext gl => gl.ReadBackMainFramebuffer(),
                    D3D11Context d3d => d3d.ReadBackMainFramebuffer(),
                    _ => throw new InvalidOperationException("unreachable: 路由断言已限定两后端"),
                };
                int wc = ((int)(H / 2) * (int)W + (int)(W / 2)) * 4;   // 画面中心 = 四边形
                int cc = (2 * (int)W + 2) * 4;                          // (2,2) = 角落清屏(避开图集白点区)
                Assert.Equal(255, px[wc]); Assert.Equal(255, px[wc + 1]);
                Assert.Equal(255, px[wc + 2]); Assert.Equal(255, px[wc + 3]);
                if (backend == "gl")
                {
                    Assert.Equal(18, px[cc]); Assert.Equal(18, px[cc + 1]);
                    Assert.Equal(20, px[cc + 2]); Assert.Equal(255, px[cc + 3]);
                }
                else
                {
                    Assert.Equal(20, px[cc]); Assert.Equal(18, px[cc + 1]);
                    Assert.Equal(18, px[cc + 2]); Assert.Equal(255, px[cc + 3]);
                }
            }
            finally
            {
                // 释放序 = App.Run 次窗移除路径:Renderer → Ctx → Atlas → Window
                w.Renderer.Dispose();
                w.Ctx.Dispose();
                w.Atlas.Dispose();
                w.Window.Dispose();
            }
        }
        finally { Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old); }
    }

    /// <summary>T3 评审携带项:全 App 路径进程内冒烟 —— App.Create + Run(maxFrames:5) 走建窗/全局泵接线/
    /// 快照轮换(InputMapper+Interaction 空帧路径)/Ui.Text("") 树构建与绘制/渲染帧 Present/State Sweep+Tick/
    /// maxFrames 退出/退出兜底 Stop+Dispose。进程存活返回即过(异常即测试失败);双后端各一遍。</summary>
    [Theory]
    [InlineData("d3d11")]
    [InlineData("gl")]
    [Trait("Category", "Gpu")]
    public void App_CreateRun_MaxFrames5_FullLoop_ExitsClean(string backend)
    {
        if (backend == "gl" && !GlSupport.Available) return;   // CI 无 GPU 环境 gl 分支早退,见 GlSupport
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            Environment.SetEnvironmentVariable("SHARPUI_BACKEND", backend);
            App app = App.Create("sharpui-t4-smoke", 200, 150, () => Ui.Text(""));
            int rc = app.Run(maxFrames: 5);
            Assert.Equal(0, rc);
            AppWindow w = app.Windows[0];
            Assert.False(w.Window.Exists);        // maxFrames 关窗 → Run 循环退出
            Assert.True(w.LastTree is not null);  // 至少一帧全路径树已构建(Ui.Text("") 绘制早退但布局/提交实走)
            if (backend == "gl") Assert.IsType<GLContext>(w.Ctx);
            else Assert.IsType<D3D11Context>(w.Ctx);
            // 审计修复波 I-3 后,Run teardown 已对称释放 Renderer/Ctx/Atlas(见 App.Run 退出兜底);
            // 此处补位调用幂等(_disposed 门),保留作跨测试 GPU 态残留的显式防线。
            w.Renderer.Dispose();
            w.Ctx.Dispose();
            w.Atlas.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old);
            // App.Run 静态残留复位(App.State 新建过/Atlas 悬垂指向已释放图集;隔离纪律同 ScrollTests 惯例)
            App.State = null;
            App.Atlas = null;
        }
    }

    /// <summary>审计修复波 I-4(A-I-1):mesh 顶点溢出(>65536)降级为丢帧 + 诊断,App.Run 进程存活。
    /// 17000 个非空面板 ≈ 17000 quad(68000 顶点)> 16384 quad 上限,每帧绘制必触 Ensure 抛
    /// InvalidOperationException —— 修复前异常从 root.Draw 冒穿 Run 循环整进程崩;修复后该帧丢弃
    /// 绘制(不 Submit/Present,窗口保持上一画面),Run 正常走到 maxFrames 退出。</summary>
    [Theory]
    [InlineData("d3d11")]
    [InlineData("gl")]
    [Trait("Category", "Gpu")]
    public void App_RunMeshOverflow_EveryFrameDropped_RunSurvives(string backend)
    {
        if (backend == "gl" && !GlSupport.Available) return;   // CI 无 GPU 环境 gl 分支早退,见 GlSupport
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            Environment.SetEnvironmentVariable("SHARPUI_BACKEND", backend);
            App app = App.Create("sharpui-i4-overflow", 200, 150, () =>
            {
                PanelElement root = Ui.Column();
                root.Style.CrossAlign = CrossAxisAlignment.Stretch;   // 全宽:子面板 Frame 非空必出 quad
                for (int i = 0; i < 17000; i++)
                    root.Add(new PanelElement().Fill(0x3366CC).Basis(1f));
                return root;
            });
            int rc = app.Run(maxFrames: 3);   // 每帧都溢出:3 帧全程降级,进程存活返回即过
            Assert.Equal(0, rc);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old);
            App.State = null;
            App.Atlas = null;
        }
    }
}
