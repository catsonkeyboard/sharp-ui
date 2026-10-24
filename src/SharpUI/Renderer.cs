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

    public void Submit(MeshBuilder mesh, SizeF screenSize, Framebuffer framebuffer)
    {
        bool hasGeometry = mesh.IndexCount > 0;
        if (hasGeometry)
            EnsureCapacity(mesh);

        Span<byte> uniform = stackalloc byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(uniform, screenSize.Width);
        BinaryPrimitives.WriteSingleLittleEndian(uniform.Slice(4), screenSize.Height);

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
        if (hasGeometry)
            cl.DrawIndexed((uint)mesh.IndexCount);
        cl.End();
        _ctx.Device.SubmitCommands(cl);
    }

    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
    }
}
