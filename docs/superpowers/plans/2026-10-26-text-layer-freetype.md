# D3 文本层自研(FreeType+HarfBuzz 直连)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用 vendor 的 FreeType+HarfBuzz 直连替换 TextAtlas 内部的全部 SkiaSharp(公共 API 不变),金样本验收,移除 Skia 包,AOT 产物 ≤8MB。

**Architecture:** 四个新文件分层(FT/HB 声明层→FontLibrary 托管封装→GlyphRasterizer→TextAtlas 内部重写);金样本语料在切换前由现有 Skia 路径生成入库,切换后永久比对;T0 探针先趟平 DLL 依赖风险。

**Tech Stack:** .NET 10、FreeType 2.13+(freetype.dll)、HarfBuzz 8+(harfbuzz.dll,含 hb-ft)、System.Drawing(仅金样本工具解码/编码 PNG,测试宿主用)。

**Spec:** `docs/superpowers/specs/2026-10-26-text-layer-design.md`(用户已批准:深度方案 A、金样本验收、真实行度量 T-3)

## Global Constraints

- 零 `unsafe`;NativeAOT 兼容(IsAotCompatible=true;金样本工具与 System.Drawing 仅测试宿主可用,**不得进入 src/SharpUI**)。
- 每帧稳态分配 ≤8192B 不破;字形缓存命中路径零分配。
- 声明层纪律(同 D3D11Native/GL 先例):FT/HB 结构与常量以本机头文件实数推导(freetetype.h/hb-buffer.h 等,头文件随源码包或 SDK),每个值注释出处;结构尺寸 Marshal.SizeOf 自检测试锁定。
- `TextAtlas` 公共 API(`Measure`/`DrawRun`/`WhiteUv`/`Dispose`)与 `ShapedLayout`/`ShapedRun` 快照缓存语义(T4/D21/D22)不变;`ClusterToCharIndex`/`Utf8Len` 删除(hb utf16 cluster 即下标)。
- 行度量换 FT_Size_Metrics(T-3):既有数值断言按 ≤0.5px 容差重定基。
- vendor 许可随附:`native/freetype/win-x64/LICENSE.txt`(FTL/BSD)、`native/harfbuzz/win-x64/LICENSE.txt`(MIT),同 SDL2 zlib 先例。
- FT/HB 单副本布局(native/ 一处),P/Invoke 按名解析即可,无 SDL 式双副本探测。
- 全量测试门槛:基线 189/189;金样本阈值:像素差异均值 <2%、单像素最大 delta ≤96。
- git 每任务至少一提交。

---

### Task 0: 依赖探针——vendor 两 DLL + 版本/最小渲染探针

**Files:**
- Create: `native/freetype/win-x64/`(freetype.dll + LICENSE.txt + VERSIONS.md 记录来源与哈希)
- Create: `native/harfbuzz/win-x64/`(harfbuzz.dll + LICENSE.txt)
- Create: `src/SharpUI/Text/NativeProbe.cs`(internal,探针用最小 P/Invoke,后续任务声明层可吸收)
- Modify: `src/SharpUI/SharpUI.csproj`(VendorNativeText 复制目标,模式抄 Counter.csproj 的 VendorModernSdl2:Build/Publish 后 Copy 到 OutDir 根+OutDir runtimes/win-x64/native)
- Test: `tests/SharpUI.Tests/TextNativeProbeTests.cs`

**Interfaces(Produces):**
- 两 DLL 就位且可加载;`NativeProbe` 暴露 internal 探针:`FreeTypeVersion() → (int major,int minor,int patch)`、`HarfBuzzVersion() → string`。
- Consumes: 无。

- [ ] **Step 1: 获取 DLL(来源按序尝试,VERSIONS.md 记录实际路线与 sha256)**

获取路线(按序):
1. `vcpkg install freetype harfbuzz triplet=x64-windows`(需 VS/vcpkg;产物 `vcpkg_dir/installed/x64-windows/bin/*.dll`);
2. 本机 VS 开发者环境编译两库源码(官方 tarball: freetype 2.13.x / harfbuzz 8.x,cmake 出 DLL);
3. 既有可信预编译(如 GnuWin 不够新时弃用;harfbuzz 需含 hb-ft 导出)。
验收:`dumpbin /exports harfbuzz.dll | findstr hb_ft_font_create` 非空;两 DLL x64。

- [ ] **Step 2: 写失败测试**

`tests/SharpUI.Tests/TextNativeProbeTests.cs`:

```csharp
using SharpUI.Text;

namespace SharpUI.Tests;

public class TextNativeProbeTests
{
    [Fact]
    [Trait("Category", "Gpu")]   // 依赖本机 native 布局(与 SDL/GPU 测试同宿主约定)
    public void FreeType_Loads_VersionAtLeast2_12()
    {
        (int major, int minor, _) = NativeProbe.FreeTypeVersion();
        Assert.True(major > 2 || (major == 2 && minor >= 12), $"FreeType {major}.{minor}");
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void HarfBuzz_Loads_VersionAtLeast6()
    {
        string v = NativeProbe.HarfBuzzVersion();
        Assert.Matches(@"^\d+\.\d+", v);
        int major = int.Parse(v.Split('.')[0]);
        Assert.True(major >= 6, v);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void MinimalFaceLoad_And_GlyphRender_Succeeds()
    {
        // segoeui.ttf 是系统字体,直接按路径加载(C:\Windows\Fonts\segoeui.ttf);探针内部:
        // FT_New_Face → FT_Set_Pixel_Sizes(0,16) → FT_Get_Char_Index('A')≠0 →
        // FT_Load_Glyph(FT_LOAD_DEFAULT) → FT_Render_Glyph(NORMAL) → bitmap.rows>0 且 buffer 非空
        Assert.True(NativeProbe.RenderProbe("C:\\Windows\\Fonts\\segoeui.ttf", 'A'));
    }
}
```

- [ ] **Step 3: 跑确认失败(DLL/类型不存在)** → **Step 4: 实现**(vendor 布局+csproj 复制目标+NativeProbe 最小 P/Invoke:FT_Init_FreeType/FT_Library_Version/FT_New_Face/FT_Set_Pixel_Sizes/FT_Get_Char_Index/FT_Load_Glyph/FT_Render_Glyph/FT_Done_Face/FT_Done_FreeType/hb_version/hb_ft_font_create 探针经 dumpbin 已证;结构仅 FT_GlyphSlotRec 部分字段读 bitmap,按 freetype.h 偏移注释) → **Step 5: 全量绿+提交**

```bash
git add native/ src/SharpUI/SharpUI.csproj src/SharpUI/Text/NativeProbe.cs tests/SharpUI.Tests/TextNativeProbeTests.cs
git commit -m "feat(text): vendor freetype+harfbuzz, native probe green"
```

---

### Task 1: FreeType/HarfBuzz 完整声明层

**Files:**
- Create: `src/SharpUI/Text/FreeTypeNative.cs`、`src/SharpUI/Text/HarfBuzzNative.cs`
- Modify: `src/SharpUI/Text/NativeProbe.cs`(改为薄壳调用声明层,或直接删除并入)
- Test: `tests/SharpUI.Tests/TextNativeTests.cs`

**Interfaces(Produces):**

```csharp
// FreeTypeNative(internal static class)——结构按 freetype.h 实数,Marshal.SizeOf 自检锁定:
internal static class FreeTypeNative
{
    // 函数(全部 StdCall? 不——FT/HB 为 cdecl):[UnmanagedFunctionPointer(CallingConvention.Cdecl)] 委托经
    // NativeLibrary.Load("freetype.dll") + GetExport,或直接 DllImport cdecl(单副本布局允许):
    // FT_Init_FreeType(out FT_Library)、FT_Done_FreeType、FT_New_Face(string path,int index,out FT_Face)→int err、
    // FT_Done_Face、FT_Set_Pixel_Sizes(FT_Face,0,int px)→int、FT_Get_Char_Index(FT_Face,uint cp)→uint、
    // FT_Load_Glyph(FT_Face,uint gi,uint flags)→int、FT_Render_Glyph(ref GlyphSlotRec,FT_RENDER_MODE_NORMAL=0)→int、
    // FT_Get_Kerning(FT_Face,uint l,uint r,uint mode=0,ref Vector)→int、FT_Library_Version(FT_Library,out int,out int,out int)
    // 常量:FT_LOAD_DEFAULT=0x0、FT_LOAD_NO_HINTING=0x2、FT_LOAD_NO_BITMAP=0x8、FT_LOAD_COLOR=0x100000(预留,spec 范围外)
    // 结构(布局以 freetype.h 推导,自检断言尺寸):
    //   GlyphSlotRec:仅读 metrics(GlyphMetrics 128B)+format(uint)+bitmap(BitmapRec)+bitmap_left(int)+bitmap_top(int)
    //     ——完整字段序:library/face/next/glyph_index/generic(0..40)+metrics(40..168)+linearHoriAdvance(168)
    //     +linearVertAdvance(176)+advance(Vector 184..200)+format(200)+bitmap(204..244)+bitmap_left(244)+bitmap_top(248)…
    //     (x64 指针宽推导,实现时对 freetype.h 逐字段核数并在注释留推导;自检 SizeOf==256+)
    //   GlyphMetrics{Width,Height,HoriBearingX,HoriBearingY,HoriAdvance,VertBearingX,VertBearingY,VertAdvance}:8×long=64
    //   BitmapRec{Rows,Width uint; Pitch int; Buffer IntPtr; NumGrays ushort; PixelMode byte; PaletteMode byte; Palette IntPtr}
    //   Vector{X,Y long}、SizeMetricsRec{XPpem,YPpem ushort(各含 2 填充);XScale,YScale,Ascender,Descender,Height long; MaxAdvance long}
    //   FaceRec 部分读取:num_glyphs(long @24 前)、units_per_EM(ushort @96)、ascender/descender/height(short @98/100/102,字面单位 1/64 em)
    //     glyph(GlyphSlotRec* @112)、size(SizeRec* @120)——偏移以 freetype.h 逐字段推导注释;SizeRec{Face,Generic,Metrics}
    internal const uint LoadDefault = 0x0; /* …全部常量按头文件实数补全并注出处 */
}

// HarfBuzzNative(internal static class):
//   hb_version(out int major,out int minor,out int micro)、hb_buffer_create/destroy、
//   hb_buffer_add_utf16(buf,ushort[] text,int len,int item_start,int item_end)、hb_buffer_guess_segment_properties、
//   hb_shape(font,buf,IntPtr features,0)→bool、hb_buffer_get_length→uint、
//   hb_buffer_get_glyph_infos/positions→IntPtr(手动 Marshal.PtrToStructure 逐条读)、
//   hb_ft_font_create(FT_Face)→hb_font、hb_font_destroy、hb_buffer_reset
//   结构:GlyphInfo{Codepoint,Mask,Cluster,Var1} 4×uint=16;GlyphPosition{XAdvance,YAdvance,XOffset,YOffset} 4×int=16
```

(实现者按上面规格逐项落码:函数清单完整,结构以头文件逐字段核数并注释推导——**同 D3D11Native 槽位纪律**;本计划不内联全部结构体代码,以"清单+出处+自检断言"为规格。)

- [ ] **Step 1: 写失败测试(结构尺寸自检)**

`tests/SharpUI.Tests/TextNativeTests.cs`:

```csharp
using System.Runtime.InteropServices;
using SharpUI.Text;

namespace SharpUI.Tests;

public class TextNativeTests
{
    [Fact]
    public void NativeStructSizes_MatchHeaderLayouts()
    {
        Assert.Equal(64, Marshal.SizeOf<FreeTypeNative.GlyphMetrics>());
        Assert.Equal(40, Marshal.SizeOf<FreeTypeNative.BitmapRec>());   // rows4+width4+pitch4+buf8+gray2+pm1+pal1pad2+pal8 → 核头文件
        Assert.Equal(16, Marshal.SizeOf<FreeTypeNative.Vector>());
        Assert.Equal(16, Marshal.SizeOf<HarfBuzzNative.GlyphInfo>());
        Assert.Equal(16, Marshal.SizeOf<HarfBuzzNative.GlyphPosition>());
    }
    // GlyphSlotRec/SizeMetricsRec/FaceRec 部分字段的读取由 T0 探针测试继续覆盖(单源不重复)
}
```

尺寸断言值以头文件推导为准——实现时对不上就改断言并注释依据(同 D3D11NativeTests 先例:断言锁定的是"实现与推导一致",推导错则两处同改+注释)。

- [ ] **Step 2-5: 红灯→实现→全量绿→提交**(NativeProbe 改薄壳;全量基线 189+3=192±)

```bash
git commit -m "feat(text): complete freetype/harfbuzz declaration layer with struct self-checks"
```

---

### Task 2: FontLibrary——字体发现 + 脸/字体对象生命周期

**Files:**
- Create: `src/SharpUI/Text/FontLibrary.cs`(含 `IFontLocator`/`WindowsFontLocator`)
- Test: `tests/SharpUI.Tests/FontLibraryTests.cs`

**Interfaces(Produces):**

```csharp
namespace SharpUI.Text;

/// <summary>族名→字体文件路径(spec:Windows 注册表实现,Linux/macOS 后续里程碑)。</summary>
internal interface IFontLocator
{
    /// <summary>解析族名(含粗体族如 "Segoe UI Bold");失败返回 null。</summary>
    string? Locate(string familyName, bool bold);
}

internal sealed class WindowsFontLocator : IFontLocator
{
    // HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts:
    // 值名 "<family> (TrueType)" → 文件名(相对 C:\Windows\Fonts 或绝对路径);
    // 粗体先查 "<family> Bold (TrueType)",回退 "<family> (TrueType)"(与现状 BoldOf 回退语义一致)
}

/// <summary>FT_Library + 槽位脸表生命周期;每脸配 hb_ft_font(度量同步)。</summary>
internal sealed class FontLibrary : IDisposable
{
    public FontLibrary(string[] fallbackFamilies);   // 槽 0 = "Segoe UI",1.. = fallback(解析失败并入槽 0,同现状)
    public int SlotCount { get; }
    public bool ContainsGlyph(int slot, uint codepoint);              // FT_Get_Char_Index ≠ 0
    public FaceHandle Face(int slot, bool bold);                      // 脸句柄(缓存,粗体=BoldOf 同现状语义)
    public (float Ascender, float Descender) LineMetrics(int slot, int pixelSize, bool bold);  // SizeMetrics,px
}

internal sealed class FaceHandle : IDisposable
{
    public IntPtr FtFace { get; }          // FT_Face(FT_Set_Pixel_Sizes 已按请求尺寸设置;尺寸变化重设)
    public IntPtr HbFont { get; }          // hb_ft_font_create(FtFace)
    public void SetPixelSize(int px);      // 缓存当前尺寸,尺寸变才调 FT_Set_Pixel_Sizes + 重建? hb_ft 随脸走,无需重建
}
```

- [ ] **Step 1: 写失败测试**(发现:Segoe UI/YaHei 解析出存在文件、Bold 族存在;回退链 ContainsGlyph:'A'→槽0、'中'→槽1(微软雅黑)、'🙂'→槽2(Emoji);LineMetrics:16px 的 ascender 在 [10,20] 区间且 ascender>0、descender<0)——**测试代码按上述语义逐条写全**(模式同 D3D11ContextTests:每断言含注释依据)。
- [ ] **Step 2-5: 红灯→实现→全量绿→提交**

```bash
git commit -m "feat(text): font library - registry locator, face/hb-font lifecycle, fallback chain"
```

---

### Task 3: shaping + 光栅化核心

**Files:**
- Create: `src/SharpUI/Text/Shaper.cs`(hb utf16 shaping → 托管快照)、`src/SharpUI/Text/GlyphRasterizer.cs`
- Test: `tests/SharpUI.Tests/ShaperTests.cs`、`tests/SharpUI.Tests/GlyphRasterizerTests.cs`

**Interfaces(Produces):**

```csharp
namespace SharpUI.Text;

/// <summary>单 run shaping(hb utf16,cluster=UTF-16 下标)。</summary>
internal sealed class Shaper : IDisposable
{
    public Shaper(FontLibrary fonts);
    public ShapedRun Shape(int slot, string run, int pixelSize, bool bold, float scale);
    // 实现:FaceHandle.SetPixelSize → hb_buffer_reset+add_utf16+guess_segment_properties → hb_shape(hb_font) →
    //   逐条读 infos/positions → 托管数组:x=advance/64/scale(26.6→px→逻辑),y=-y_offset/64/scale
    //   总宽=Σ x_advance/64/scale
}

/// <summary>字形位图光栅化:FT_LOAD_DEFAULT+RENDER_NORMAL → A8 覆盖 → RGBA 预乘白 → byte[]。</summary>
internal sealed class GlyphRasterizer : IDisposable
{
    public GlyphRasterizer(FontLibrary fonts);
    public RasterGlyph Rasterize(int slot, uint codepoint, int pixelSize, bool bold);
    // 实现:FT_Get_Char_Index → FT_Load_Glyph(DEFAULT) → FT_Render_Glyph(NORMAL) →
    //   bitmap(A8:pixelMode=2? FT_PIXEL_MODE_GRAY=2 核头文件)逐行展开 RGBA(255,255,255,cov;预乘=cov)→
    //   返回 { byte[] Rgba, int Width, int Height, float BearingTop(bitmap_top), float Advance(26.6→px) }
    //   空字形(空格类 codepoint 有 advance 无位图)返回 Width=Height=0 与 Advance>0
}
```

- [ ] **Step 1: 写失败测试**:
  - ShaperTests:"AV" 总宽 ∈ [1.0×,2.0×] px@16 单字宽(区间断言+注释依据);中文字符串每字 advance 相等(全角);cluster 断言:"中a" shape 后 infos[i].Cluster ∈ {0,1}(UTF-16 下标,**不是** UTF-8 字节偏移);粗体 "A" 宽 ≥ 常规。
  - GlyphRasterizerTests:'A'@16 → Width∈[8,20] Height∈[8,24] 且 RGBA 非全零、alpha 通道有中间值(抗锯齿)且 R=G=B=255(白);' '@16 → W=H=0 且 Advance>0;'中'@16 光栅化非空。
- [ ] **Step 2-5: 红灯→实现→全量绿→提交**(两个提交:`feat(text): hb shaper` / `feat(text): glyph rasterizer`)

---

### Task 4: TextAtlas 内部重写 + 金样本生成与对比

**Files:**
- Modify: `src/SharpUI/Elements/TextAtlas.cs`(全内部重写,公共 API 不变;`using SkiaSharp*` 全删)
- Create: `tests/SharpUI.Tests/TextGoldenTests.cs`(对比)与 `tests/golden/text/*.png`(语料金样本)
- Create: `tests/SharpUI.Tests/TextGoldenGen.cs`(一次性生成器,Skip 除非 env SHARPUI_REGEN_GOLDEN=1)
- Modify: 既有数值断言测试(重定基,≤0.5px 容差)

**Interfaces:**
- Consumes: T1 声明层、T2 FontLibrary、T3 Shaper/GlyphRasterizer。
- Produces: TextAtlas 同公共 API 的新内部;金样本 6 类语料(见 Step 1)+ 对比测试。

**语料(固定 6 条,64×256 RGBA 每条):**

```csharp
static readonly (string Name, string Text, float Size, bool Bold)[] Corpus = new[]
{
    ("ascii",   "The quick brown fox jumps", 20f, false),
    ("cjk",     "你好世界,文本渲染测试。",     20f, false),
    ("mixed",   "Hello 你好 mixed 混排 end",   18f, false),
    ("bold",    "Bold text 123 粗体",         20f, true),
    ("fallback","emoji 🙂 fallback",           20f, false),   // emoji 走回退槽单色(现状平价)
    ("sizes",   "S12 S18 S24",                12f, false),   // 同图多字号段(12/18/24 分段绘制)
};
```

- [ ] **Step 1: 金样本生成(Skia 现路径,先于重写)**——生成器:建 TextAtlas(现 Skia 实现)+ MeshBuilder,逐语料 DrawRun 到 mesh,经 headless D3D11Context 渲染 64×256 → `ReadBackMainFramebuffer()` → System.Drawing Bitmap 存 PNG 到 `tests/golden/text/{name}.png`(git add 入库)。跑 `SHARPUI_REGEN_GOLDEN=1 dotnet test --filter TextGoldenGen` 产 6 张。
- [ ] **Step 2: 重写 TextAtlas 内部**(SplitRuns 用 FontLibrary.ContainsGlyph;GetShaped 用 Shaper;Rasterize 用 GlyphRasterizer(空格跳过位图,由 advance 推进);GetLineMetrics 用 FontLibrary.LineMetrics;ShapedRun.Points 换 `PointF[]`;删 ClusterToCharIndex/Utf8Len——`ci = r.Clusters[gi]` 直接用)。
- [ ] **Step 3: 写对比测试**(TextGoldenTests:同生成器渲染路径,新 TextAtlas 渲染 → 与 PNG 逐像素:diffSum/像素数 <2%、maxDelta ≤96;System.Drawing 仅测试宿主)。
- [ ] **Step 4: 既有测试重定基**(跑全量,数值漂移断言按 ≤0.5px 容差改;逐处注释"FT metrics,T-3")。
- [ ] **Step 5: 全量绿+提交**

```bash
git commit -m "feat(text): textatlas on freetype/harfbuzz, golden corpus verified"
```

---

### Task 5: 移除 Skia + AOT 实测 + 收官

**Files:**
- Modify: `src/SharpUI/SharpUI.csproj`(删 SkiaSharp/SkiaSharp.HarfBuzz 两行)
- Delete: TextAtlas 中残留 Skia 引用复查(应已在 T4 清零,`grep -r "SK\|Skia" src/` 0 命中)
- Modify: `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(D3 行收账 + 裁定#42:文本层 FT/HB 直连定稿)、`docs/notes/perf-baselines.md`(文本路径实测)
- Test: 全量 + AOT 发布实测

- [ ] **Step 1**: 删两包引用 → 全量绿(golden 测试证明不依赖 Skia 存续)。
- [ ] **Step 2**: `dotnet publish examples/Counter/Counter.csproj -c Release -r win-x64 /p:PublishAot=true` → 产物体积实测(验收 ≤8MB,记录到 spec 收账行;Skia 原生库从产物消失)→ 启动冒烟(进程存活 ≥5s,桌面锁定降级标准)。
- [ ] **Step 3**: Counter 中英混排目视(桌面锁定则以 golden 测试+进程存活承证并记录)。
- [ ] **Step 4**: 文档 + 提交

```bash
git commit -m "feat(text): drop skiasharp, aot under 8mb, d3 closed"
```

---

## Self-Review 结论(已执行)

- **Spec 覆盖**:架构四文件→T1-T4;依赖落地+T0→T0;金样本→T4;行度量 T-3→T4 Step4;范围外(彩色 emoji 常量 FT_LOAD_COLOR 预留→T1;IFontLocator 抽象→T2);验收清单→T4/T5;许可随附→T0。
- **占位符扫描**:T1 声明层以"函数/结构/常量清单+出处推导纪律+自检断言"为规格(同已验证的 GL 计划模式),非 TBD;其余任务含完整测试代码。
- **类型一致性**:FontLibrary/FaceHandle/Shaper/GlyphRasterizer 签名在 T2/T3 定义、T4 消费一致;ShapedRun.Points 类型变更(PointF[])在 T4 内闭环。
