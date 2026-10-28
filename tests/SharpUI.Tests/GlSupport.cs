using SharpUI;
using SharpUI.Backends;

namespace SharpUI.Tests;

/// <summary>GL 3.3 可用性一次性探测(CI 环境适配):无 GPU 的 runner(如 GitHub windows-latest
/// 的 Microsoft GDI 软渲染)只能建 GL 1.1 上下文,glCreateShader 等核心函数缺失——GL 测试
/// 在此类环境按约定早退(有 GPU 的开发机/LLVMpipe 环境照常全跑)。探测走真实路径:
/// GLContext.CreateHeadless(3.3 core 属性 + GL.EnsureLoaded,核心函数缺失即抛)。</summary>
public static class GlSupport
{
    static readonly Lazy<bool> _available = new(Probe);

    public static bool Available => _available.Value;

    static bool Probe()
    {
        try
        {
            using var ctx = GLContext.CreateHeadless(64, 64);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
