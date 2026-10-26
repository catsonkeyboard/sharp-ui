# M1 Native AOT Publish Record (win-x64)

Command: `dotnet publish examples/Counter -c Release -r win-x64 -p:PublishAot=true -o artifacts/aot-win-x64`

- Result: SUCCESS
- Size convention: decimal MB = 10^6 bytes; MiB = 2^20 bytes, labeled explicitly where used.
- Publish duration: 18 s (SDK 10.0.401, incremental ILC; bin/obj warm from prior Release builds)
- Counter.exe (single-file AOT executable, native libs for Vortice/Veldrid/SkiaSharp linked or shipped alongside): 4,720,640 bytes = 4.72 MB
- Runtime payload — explicit sum: Counter.exe 4,720,640 + SDL2.dll 1,334,272 + libSkiaSharp.dll 12,875,576 + libveldrid-spirv.dll 5,844,480 = **24,774,968 bytes = ~24.8 MB** (23.6 MiB) — meets spec M1 acceptance 3 (<40 MB)
- Raw directory total: ~140 MB decimal (134 MiB as reported by `du -sh`, which uses binary units); the excess is debug symbol files only — Counter.pdb 19,648,512 bytes (~19.6 MB) and libSkiaSharp.pdb 95,985,664 bytes (~96.0 MB). These are not runtime artifacts; excluding all *.pdb the deployable output is 24,774,968 bytes = ~24.8 MB.
- Warnings during publish: CS0618 (SkiaSharp obsolete DrawText overload, pre-existing) ; IL2104 + IL3000 originate from third-party assemblies (Vortice.DXGI, Microsoft.Extensions.DependencyModel), not SharpUI code. No NU1903.

Launch check (manual test, Step 3): Counter.exe started from artifacts/aot-win-x64, process alive at +3 s — MainWindowTitle 'SharpUI Counter' observed (real window created; no cold-start crash) — killed cleanly via Stop-Process. Cold-start <1 s window appearance is per human perception (user acceptance call).

## Artifact listing (`ls -la artifacts/aot-win-x64`)
total 137152
drwxr-xr-x 1 <user> <group>        0 Oct 25 10:00 .
drwxr-xr-x 1 <user> <group>        0 Oct 25 10:00 ..
-rwxr-xr-x 1 <user> <group>  4720640 Oct 25 10:00 Counter.exe
-rw-r--r-- 1 <user> <group> 19648512 Oct 25 10:00 Counter.pdb
-rwxr-xr-x 1 <user> <group>  1334272 Feb  4  2023 SDL2.dll
-rw-r--r-- 1 <user> <group>    23168 Oct 25 10:00 SharpUI.pdb
-rwxr-xr-x 1 <user> <group> 12875576 Sep 30 06:04 libSkiaSharp.dll
-rw-r--r-- 1 <user> <group> 95985664 Sep 30 06:04 libSkiaSharp.pdb
-rwxr-xr-x 1 <user> <group>  5844480 Jun  3  2022 libveldrid-spirv.dll
--- du -sh ---
134M	artifacts/aot-win-x64

## 已知问题(2026-10-25,裁定#35;2026-10-26 已收账,见下节)

NativeAOT 产物在启动时抛 `MissingMethodException: Vortice.DXGI.IDXGIFactory 无参构造`——
SharpGen.Runtime 2.0.0-beta.13 的 `MarshallingHelpers.FromPointer<T>` 走 `Activator` 反射,
当前 ILCompiler(10.0.12)下 AOT 不放行;JIT(开发/Release 运行)不受影响。
rd.xml Dynamic="Required All" 实测无效。处置:M3e 时一并解决(升级 Vortice 3.x 系或自研 D3D11 后端
取代 Veldrid.D3D11 路径——M3d-ii 本就计划之);此前 AOT 验收条目视为失效。

## 裁定#35 收账(2026-10-26,M3d-ii Task 5)

自研 D3D11 直连后端定稿,Vortice 随 Veldrid 图形包(Veldrid/Veldrid.SPIRV/Veldrid.StartupUtilities)移除而消失,反射根因不复存在——M1 验收 3(AOT 发布)恢复有效:

- 依赖树复核:`dotnet list examples/Counter/Counter.csproj package --include-transitive` 输出**无任何 Vortice/SharpGen 条目**(Veldrid.SDL2 独立成包,不引 Veldrid/Vortice,计划期二进制实证复核成立)。
- 发布:`dotnet publish examples/Counter/Counter.csproj -c Release -r win-x64 -p:PublishAot=true -v q` 成功(增量 ILC 约 11s)。警告仅 IL3000 一条(Microsoft.Extensions.DependencyModel,Veldrid.SDL2→NativeLibraryLoader 传递链的第三方代码);旧记录中的 IL2104(Vortice.DXGI)与 CS0618(SkiaSharp 旧 DrawText)均已消失——前者随包移除,后者 M2d D21 改用非废弃重载时清偿。零 NU1903。
- 产物(排除 *.pdb 后可部署部分):Counter.exe 3,315,712 + SDL2.dll 1,702,400(vendored 2.30.8,IME 裁定#34)+ libSkiaSharp.dll 12,875,576 + libHarfBuzzSharp.dll 2,038,112 = **19,931,800 B ≈ 19.9 MB**(M1 Veldrid 时代为 24,774,968 B ≈ 24.8 MB:Counter.exe −1.4 MB(图形设备代码出栈),libveldrid-spirv.dll 5.8 MB 不再需要;新增 libHarfBuzzSharp.dll 为 M2d shaping 功能,非 AOT 回归)。
- 运行实测(桌面锁定环境,采 T4 冒烟降级标准:进程存活 + 无异常退出):Counter.exe 存活 10s+(≥5s 标准),`Responding=True`,MainWindowTitle "SharpUI Counter",CPU 10s 内 1.14s(渲染循环实做工作),stdout/stderr 零输出——**Vortice.DXGI MissingMethodException 症状消失**,无未处理异常退出。客户区目视渲染受锁屏环境限制不可判读(PrintWindow 客户区黑屏,T4 报告已记录同限;窗口路径像素级正确性由 T4 harness 后备缓冲读回断言承证,与本产物同一渲染路径)。
- Newtonsoft.Json 直接引用**保留**(2026-10-26 终审修复波两步裁定):M3d-ii 终审曾判其为孤儿移除,但移除后实证 NU1903 经 `Veldrid.SDL2 → NativeLibraryLoader → Newtonsoft.Json 9.0.1`(GHSA-5crp-9r3c-p9vr 高危)传递链复发,stash 复测基线(直接引用在)无警告——该钉版同时压制已消失的 SPIRV 链与仍在依赖图中的 SDL2 链,包不是孤儿;退出条件=M3e 替换窗口层移除 Veldrid.SDL2 时删除(csproj 行内已注)。Vortice/SharpGen 零条目不受影响。

## D3 收官:文本层 FT/HB 直连,Skia 系移除(2026-10-26 Task 5,裁定#42)

`dotnet publish examples/Counter/Counter.csproj -c Release -r win-x64 -p:PublishAot=true` 成功(发布前清空旧 publish 目录防陈旧残留);警告仅 IL3000 一条(同前,Microsoft.Extensions.DependencyModel 第三方链),零 NU1903、零 CS0618。

- 产物(排除 *.pdb 可部署部分):Counter.exe 3,481,088 + SDL2.dll 1,702,400(vendored 2.30.8)+ freetype.dll 686,592 + harfbuzz.dll 1,075,712 = **6,945,792 B ≈ 6.95 MB(6.62 MiB)**——较裁定#35 收账 19,931,800 B ≈ 19.9 MB **−65.2%**;设计验收 ≤8MB 达成。Skia 系原生库(libSkiaSharp.dll 12,875,576 + libHarfBuzzSharp.dll 2,038,112)自产物消失;Counter.exe +165,376 B(FT/HB P/Invoke 声明层与自研文本栈取代旧栈 interop 代码,3,315,712→3,481,088)。
- 运行实测(桌面锁定,采降级标准:进程存活 + 无未处理异常):AOT Counter.exe 存活 10s(≥5s 标准)后手动终止,Responding=True,MainWindowTitle "SharpUI Counter",CPU 0.88s/10s(渲染循环实做工作),stdout/stderr 零输出——无未处理异常退出。PrintWindow 客户区黑屏(锁屏环境已知限,上文 M1/裁定#35 节同记录);渲染正确性由金样本 6/6(同一 TextAtlas FT/HB 路径,tests/golden/text)承证,捕获帧中 TextField 光标 quad(SharpUI 自绘元素)可见。
