using System.Runtime.InteropServices;

namespace SharpUI.Backends;

/// <summary>SDL 原生最小面(M3d-ii):视频子系统初始化 + 窗口 HWND。裁定#38:不做 NativeLibrary
/// 预载(避免 runtimes 副本成第二模块),DllImport 解析与 Veldrid.Sdl2 同路径(应用基目录)。
/// 裁定#39:WMInfo 结构给足 SDL 2.24+ Windows 联合体尺寸(4 指针)+ 余量,防原生越界写。</summary>
internal static class SdlNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion { public byte Major, Minor, Patch; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlSysWmInfo
    {
        public byte Maj, Min, Pat;
        public int Subsystem;
        public IntPtr Hwnd, Hdc, Hglrc, HelperWindow;   // 2.24+ win 分支 4 指针
        public IntPtr Pad1, Pad2, Pad3, Pad4;           // 其余平台分支余量
    }

    internal static void EnsureVideoInit()
    {
        _ = SDL_Init(0x00000020);   // SDL_INIT_VIDEO;幂等引用计数,失败值忽略(与 Veldrid.StartupUtilities 先例一致)
    }

    internal static IntPtr GetWin32Hwnd(IntPtr sdlWindow)
    {
        var v = new SdlVersion();
        SDL_GetVersion(ref v);
        var info = new SdlSysWmInfo { Maj = v.Major, Min = v.Minor, Pat = v.Patch };
        if (SDL_GetWindowWMInfo(sdlWindow, ref info) == 0 || info.Subsystem != 1 /*SYSWM_WINDOWS*/)
            throw new InvalidOperationException(
                $"SDL_GetWindowWMInfo 失败(SDL {v.Major}.{v.Minor}.{v.Patch}, sub={info.Subsystem})");
        return info.Hwnd;
    }

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_Init(uint flags);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GetVersion(ref SdlVersion v);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetWindowWMInfo(IntPtr window, ref SdlSysWmInfo info);
}
