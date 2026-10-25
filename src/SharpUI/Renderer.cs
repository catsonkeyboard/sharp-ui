using System.Buffers.Binary;
using Veldrid;

namespace SharpUI;

public sealed class Renderer : IDisposable
{
    // M3d-ii 过渡(T4 迁移):IRenderContext 已重写为 v2(由 D3D11Context 实现),旧驱动逻辑暂依赖
    // 具体 GraphicsContext(Veldrid);T4 将本类整体迁移到 IRenderContext v2,本任务不改驱动逻辑。
    readonly GraphicsContext _ctx;
    DeviceBuffer _vertexBuffer;
    DeviceBuffer _indexBuffer;
    uint _vertexCapacityBytes = 256 * 1024;
    uint _indexCapacityBytes = 512 * 1024;

    /// <summary>D27:依赖具体 GraphicsContext(M3d-ii 过渡);Submit 的 Framebuffer 参数保留——窗口路径需每帧现取交换链目标。</summary>
    public Renderer(GraphicsContext ctx)
    {
        _ctx = ctx;
        ResourceFactory f = ctx.Device.ResourceFactory;
        _vertexBuffer = f.CreateBuffer(new BufferDescription(_vertexCapacityBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _indexBuffer = f.CreateBuffer(new BufferDescription(_indexCapacityBytes, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
    }

    void EnsureCapacity(MeshBuilder mesh)
    {
        uint vertexBytes = (uint)(mesh.VertexCount * UiVertex.SizeInBytes);
        uint indexBytes = (uint)(mesh.IndexCount * sizeof(ushort));
        if (vertexBytes > _vertexCapacityBytes)
        {
            _vertexCapacityBytes = Math.Max(_vertexCapacityBytes * 2, vertexBytes);
            _vertexBuffer.Dispose();
            _vertexBuffer = _ctx.Device.ResourceFactory.CreateBuffer(
                new BufferDescription(_vertexCapacityBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }
        if (indexBytes > _indexCapacityBytes)
        {
            _indexCapacityBytes = Math.Max(_indexCapacityBytes * 2, indexBytes);
            _indexBuffer.Dispose();
            _indexBuffer = _ctx.Device.ResourceFactory.CreateBuffer(
                new BufferDescription(_indexCapacityBytes, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        }
    }

    public void Submit(MeshBuilder mesh, SizeF logicalScreen, Framebuffer framebuffer, float scale = 1f)
    {
        bool hasGeometry = mesh.IndexCount > 0;
        if (hasGeometry)
            EnsureCapacity(mesh);

        Span<byte> uniform = stackalloc byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(uniform, logicalScreen.Width);
        BinaryPrimitives.WriteSingleLittleEndian(uniform.Slice(4), logicalScreen.Height);

        CommandList cl = _ctx.Commands;
        cl.Begin();
        if (hasGeometry)
        {
            cl.UpdateBuffer(_ctx.ScreenSizeBuffer, 0, uniform);
            cl.UpdateBuffer(_vertexBuffer, 0, mesh.Vertices);
            cl.UpdateBuffer(_indexBuffer, 0, mesh.Indices);
        }
        cl.SetFramebuffer(framebuffer);
        cl.ClearColorTarget(0, new RgbaFloat(0.07f, 0.07f, 0.08f, 1f));
        cl.SetPipeline(_ctx.Pipeline);
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        cl.SetGraphicsResourceSet(0, _ctx.AtlasSet);
        if (mesh.IndexCount == 0)
        {
            // 无几何:仅清屏,不设 scissor、不绘制
        }
        else
        {
            uint fbW = framebuffer.Width, fbH = framebuffer.Height;
            ReadOnlySpan<MeshBuilder.ClipRun> runs = mesh.ClipRuns;
            if (runs.Length == 0)
            {
                cl.SetScissorRect(0, 0, 0, fbW, fbH);
                cl.DrawIndexed((uint)mesh.IndexCount, 1u, 0u, 0, 0u);
            }
            else
            {
                foreach (ref readonly MeshBuilder.ClipRun run in runs)
                {
                    if (run.Count == 0) continue;
                    if (run.Clip == MeshBuilder.FullClip)
                    {
                        cl.SetScissorRect(0, 0, 0, fbW, fbH);
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
                        cl.SetScissorRect(0, x, y, w, h);
                    }
                    cl.DrawIndexed((uint)run.Count, 1u, (uint)run.Start, 0, 0u);
                }
            }
        }
        cl.End();
        _ctx.Device.SubmitCommands(cl);
    }

    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
    }
}
