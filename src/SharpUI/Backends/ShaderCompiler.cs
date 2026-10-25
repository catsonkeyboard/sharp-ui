using System.Runtime.InteropServices;
using System.Text;

namespace SharpUI.Backends;

/// <summary>HLSL 运行时编译(d3dcompiler_47;M3d-ii)。blob 经 ID3D10Blob vtable(槽 3/4)取出字节后即释放。</summary>
internal static class ShaderCompiler
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetBufferPointerFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate UIntPtr GetBufferSizeFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseFn(IntPtr self);

    public static (byte[] Bytecode, string? Error) Compile(string source, string entryPoint, string target)
    {
        byte[] src = Encoding.UTF8.GetBytes(source);
        IntPtr pSrc = Marshal.AllocHGlobal(src.Length);
        IntPtr code = IntPtr.Zero, errors = IntPtr.Zero;
        try
        {
            Marshal.Copy(src, 0, pSrc, src.Length);
            int hr = D3D11Native.D3DCompile(pSrc, (uint)src.Length, null!, IntPtr.Zero, IntPtr.Zero,
                entryPoint, target, 0, 0, out code, out errors);
            if (hr != 0)
            {
                string msg = ReadBlobString(errors);
                return (Array.Empty<byte>(), msg.Length > 0 ? msg : $"D3DCompile hr=0x{hr:X8}({entryPoint})");
            }
            return (ReadBlobBytes(code), null);
        }
        finally
        {
            ReleaseBlob(errors);
            ReleaseBlob(code);
            Marshal.FreeHGlobal(pSrc);
        }
    }

    static byte[] ReadBlobBytes(IntPtr blob)
    {
        if (blob == IntPtr.Zero) return Array.Empty<byte>();
        var getPtr = Vfn<GetBufferPointerFn>(blob, D3D11Native.SlotBlobGetBufferPointer);
        var getSize = Vfn<GetBufferSizeFn>(blob, D3D11Native.SlotBlobGetBufferSize);
        IntPtr data = getPtr(blob);
        int size = checked((int)getSize(blob).ToUInt64());
        var bytes = new byte[size];
        Marshal.Copy(data, bytes, 0, size);
        return bytes;
    }

    static string ReadBlobString(IntPtr blob)
    {
        byte[] bytes = ReadBlobBytes(blob);
        return bytes.Length == 0 ? "" : Encoding.UTF8.GetString(bytes);
    }

    static void ReleaseBlob(IntPtr blob)
    {
        if (blob == IntPtr.Zero) return;
        Vfn<ReleaseFn>(blob, D3D11Native.SlotRelease)(blob);
    }

    static T Vfn<T>(IntPtr comObj, int slot) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObj);
        IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        if (fn == IntPtr.Zero) throw new InvalidOperationException($"ID3D10Blob vtable 槽 {slot} 为空");
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }
}
