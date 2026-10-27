namespace SharpUI;

/// <summary>
/// 三平台原生库名解析(X-1):基名 → 平台文件名的纯函数映射表,供 DllImport 常量化与
/// 非 Windows 宿主的预载/解析消费。命名依据:Windows vendored DLL 按基名+.dll;
/// Linux 发行版包 SONAME(libsdl2-2.0-0 → libSDL2-2.0.so.0、libfreetype6 → libfreetype.so.6、
/// libharfbuzz0b → libharfbuzz.so.0);macOS Homebrew install_name(版本号置于 .dylib 前:
/// libSDL2-2.0.dylib / libfreetype.6.dylib / libharfbuzz.0.dylib)。
/// **DllImport 特性参数须编译期常量** → Sdl2/FreeType/HarfBuzz 为 const,值取 Windows 现名
/// (零行为差门槛:Windows 宿主解析结果与改造前逐字节一致,由 NativeLibTests 契约锁死);
/// 非 Windows 的名称映射经 Resolve(Current, baseName) 消费(预载路径;DllImportResolver 的
/// 接线属后续任务)。Resolve 为纯函数(注入 Platform),单测全矩阵覆盖;AOT 兼容,零 unsafe。
/// </summary>
internal static class NativeLib
{
    /// <summary>目标平台三态(枚举即可作特性实参,NativeLibTests Theory 直接内联)。</summary>
    internal enum Platform { Windows, Linux, Mac }

    /// <summary>当前宿主平台:Windows/Linux 显式判定,其余(macOS/MacCatalyst 等)并入 Mac——
    /// 与 FontLibrary 发现器分派的 else 分支一致(未知平台走 MacFontLocator 回退扫描)。</summary>
    internal static Platform Current { get; } =
        OperatingSystem.IsWindows() ? Platform.Windows
        : OperatingSystem.IsLinux() ? Platform.Linux
        : Platform.Mac;

    // ---- DllImport 解析键(Windows 现名;与 Resolve(Windows, 基名) 恒等,测试锁定) ----

    internal const string Sdl2 = "SDL2.dll";
    internal const string FreeType = "freetype.dll";
    internal const string HarfBuzz = "harfbuzz.dll";

    /// <summary>基名 → 平台文件名。已知 SONAME 特例精确匹配;未知基名按平台通用约定:
    /// Windows 追 .dll、Linux 加 lib 前缀 + .so、Mac 加 lib 前缀 + .dylib(如 T2 objc 层的
    /// libobjc.dylib 即走通用式,无需特例)。</summary>
    internal static string Resolve(Platform platform, string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        return (platform, baseName) switch
        {
            (Platform.Linux, "SDL2") => "libSDL2-2.0.so.0",
            (Platform.Linux, "freetype") => "libfreetype.so.6",
            (Platform.Linux, "harfbuzz") => "libharfbuzz.so.0",
            (Platform.Mac, "SDL2") => "libSDL2-2.0.dylib",
            (Platform.Mac, "freetype") => "libfreetype.6.dylib",
            (Platform.Mac, "harfbuzz") => "libharfbuzz.0.dylib",
            (Platform.Windows, _) => baseName + ".dll",
            (Platform.Linux, _) => "lib" + baseName + ".so",
            (Platform.Mac, _) => "lib" + baseName + ".dylib",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null),
        };
    }

    /// <summary>X-4 T3(裁定#48):DllImport 名(Windows 形态 const)→ 基名 —— Resolve 的反函数,
    /// 供 App 静态构造的 SetDllImportResolver 把 DllImport 惰性解析名重定向到平台 SONAME
    /// (Linux/macOS 上 SDL/FT/HB 的 DllImport 实际生效路径)。已知三库精确映射;未知名返回
    /// null(调用方回落 IntPtr.Zero = .NET 默认解析,零面扩展)。纯函数,与 Resolve 同矩阵单测面。</summary>
    internal static string? BaseNameOf(string dllImportName) => dllImportName switch
    {
        Sdl2 => "SDL2",
        FreeType => "freetype",
        HarfBuzz => "harfbuzz",
        _ => null,
    };
}
