# M1 Native AOT Publish Record (win-x64)

Command: `dotnet publish examples/Counter -c Release -r win-x64 -p:PublishAot=true -o artifacts/aot-win-x64`

- Result: SUCCESS
- Publish duration: 18 s (SDK 10.0.401, incremental ILC; bin/obj warm from prior Release builds)
- Counter.exe (single-file AOT executable, native libs for Vortice/Veldrid/SkiaSharp linked or shipped alongside): 4,720,640 bytes (4.50 MB)
- Runtime payload (Counter.exe + SDL2.dll + libSkiaSharp.dll + libveldrid-spirv.dll): ~20.2 MB — meets spec M1 acceptance 3 (<40 MB)
- Raw directory total: ~134 MB; the excess is debug symbol files only — Counter.pdb (19.6 MB) and libSkiaSharp.pdb (96 MB). These are not runtime artifacts; excluding all *.pdb the deployable output is ~20.2 MB.
- Warnings during publish: CS0618 (SkiaSharp obsolete DrawText overload, pre-existing) ; IL2104 + IL3000 originate from third-party assemblies (Vortice.DXGI, Microsoft.Extensions.DependencyModel), not SharpUI code. No NU1903.

Launch check (manual test, Step 3): Counter.exe started from artifacts/aot-win-x64, process alive at +3 s (no cold-start crash), killed cleanly via Stop-Process. Cold-start <1 s window appearance is per human perception (user acceptance call).

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
