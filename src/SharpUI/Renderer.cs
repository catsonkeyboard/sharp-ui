using System.Buffers.Binary;
using Veldrid;

namespace SharpUI;

public sealed class Renderer : IDisposable
{
    readonly GraphicsContext _ctx;
    DeviceBuffer _vertexBuffer;
    DeviceBuffer _indexBuffer;
    uint _vertexCapacityBytes = 256 * 1024;
    uint _indexCapacityBytes = 512 * 1024;

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
                        uint x = (uint)MathF.Max(0f, run.Clip.X * scale);
                        uint y = (uint)MathF.Max(0f, run.Clip.Y * scale);
                        uint w = (uint)MathF.Min(fbW - MathF.Min(x, fbW), MathF.Ceiling(run.Clip.Width * scale));
                        uint h = (uint)MathF.Min(fbH - MathF.Min(y, fbH), MathF.Ceiling(run.Clip.Height * scale));
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
