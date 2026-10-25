namespace SharpUI;

/// <summary>M3d-ii:纯后端中立驱动——剪裁区段→scissor 换算留在此处(平台共享),命令经 IRenderContext v2 落地。</summary>
public sealed class Renderer : IDisposable
{
    readonly IRenderContext _ctx;

    public Renderer(IRenderContext ctx) => _ctx = ctx;

    public void Submit(MeshBuilder mesh, SizeF logicalScreen, float scale = 1f)
    {
        _ctx.SetScreenSize(logicalScreen.Width, logicalScreen.Height);
        _ctx.BeginFrame(0.07f, 0.07f, 0.08f, 1f);
        if (mesh.IndexCount == 0) return;   // 空帧:仅清屏
        uint fbW = _ctx.TargetWidth, fbH = _ctx.TargetHeight;
        ReadOnlySpan<MeshBuilder.ClipRun> runs = mesh.ClipRuns;
        if (runs.Length == 0)
        {
            _ctx.SetScissor(0, 0, fbW, fbH);
            _ctx.Draw(mesh.VerticesBuffer, mesh.VertexCount, mesh.IndicesBuffer, (uint)mesh.IndexCount, 0);
            return;
        }
        foreach (ref readonly MeshBuilder.ClipRun run in runs)
        {
            if (run.Count == 0) continue;
            if (run.Clip == MeshBuilder.FullClip)
            {
                _ctx.SetScissor(0, 0, fbW, fbH);
            }
            else
            {
                // 精确包含矩形:floor(左上)、ceil(右下),避免缩放下 1px 小数欠覆盖(T5 续订)
                float left = run.Clip.X * scale, top = run.Clip.Y * scale;
                float right = run.Clip.Right * scale, bottom = run.Clip.Bottom * scale;
                uint x = (uint)Math.Clamp(MathF.Floor(left), 0f, fbW);
                uint y = (uint)Math.Clamp(MathF.Floor(top), 0f, fbH);
                uint w = (uint)Math.Clamp(MathF.Ceiling(right) - x, 0f, fbW - x);
                uint h = (uint)Math.Clamp(MathF.Ceiling(bottom) - y, 0f, fbH - y);
                if (w == 0 || h == 0) continue;
                _ctx.SetScissor(x, y, w, h);
            }
            _ctx.Draw(mesh.VerticesBuffer, mesh.VertexCount, mesh.IndicesBuffer, (uint)run.Count, (uint)run.Start);
        }
    }

    public void Dispose() { }   // 缓冲归 D3D11Context(Draw 内部持有);上下文由 AppWindow 释放
}
