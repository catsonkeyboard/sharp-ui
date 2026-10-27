using System.Runtime.InteropServices;

namespace SharpUI;

/// <summary>系统剪贴板桥(M3c):SDL2 原生导出,Ansi 字符集(非 ASCII 文本可能被系统代码页改写)。
/// GetText 返回串需 SDL_free;IntPtr.Zero/空串归一为 ""。预载兜底覆盖测试宿主的 runtimes/&lt;rid&gt;/native 布局。</summary>
public static class Clipboard
{
    // DllImport 特性参数须编译期常量 → 取 NativeLib 编译期键(= Windows 现名,零行为差;X-1)
    const string Lib = NativeLib.Sdl2;

    static Clipboard()
    {
        try
        {
            // 主探测走平台解析(Windows = 现名;Linux/macOS 预载系统 SONAME,X-1)
            if (!NativeLibrary.TryLoad(NativeLib.Resolve(NativeLib.Current, "SDL2"), typeof(Clipboard).Assembly, null, out _))
            {
                string rid = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "win-x64",
                    Architecture.X86 => "win-x86",
                    Architecture.Arm64 => "win-arm64",
                    _ => ""
                };
                if (rid.Length > 0)
                    NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native",
                        NativeLib.Resolve(NativeLib.Platform.Windows, "SDL2")), out _);   // win-* 布局仅 Windows 宿主存在
            }
        }
        catch { /* 预载失败交给 DllImport 自身解析路径报错 */ }
        try { SDL_InitSubSystem(SdlInitVideo); } catch { /* 已初始化/无桌面环境:交给调用侧报错 */ }
    }

    const uint SdlInitVideo = 0x20;   // SDL_INIT_VIDEO:Windows 剪贴板依赖视频子系统(隐藏辅助窗口);计数式,重复调用安全

    /// <summary>写入系统剪贴板(SDL 拷贝入参,Ansi 封送);null 归一为空串。</summary>
    public static void SetText(string s) => _ = SDL_SetClipboardText(s ?? "");

    /// <summary>同 SetText 但回传 SDL 是否接受(0=成功);剪贴板被系统策略拒绝时测试据此跳过往返断言。</summary>
    internal static bool TrySetText(string s) => SDL_SetClipboardText(s ?? "") == 0;

    /// <summary>读取系统剪贴板;空/失败返回 ""(IntPtr.Zero/空串归一)。</summary>
    public static string GetText()
    {
        IntPtr p = SDL_GetClipboardText();
        if (p == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringAnsi(p) ?? ""; }
        finally { SDL_free(p); }   // SDL 约定:调用方负责释放返回串
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_InitSubSystem(uint flags);

    [DllImport(Lib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_SetClipboardText(string text);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetClipboardText();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_free(IntPtr ptr);
}
