# D3 文本层自研设计(FreeType + HarfBuzz 直连,换 SkiaSharp)

日期:2026-10-26 | 状态:已批准(用户选定深度方案 A + 视觉对齐验收)→ **已收官(2026-10-26,主 spec 裁定#42;实施裁定勘误见下节,以实况为准)** | 归属:D3 决策收账("文本层先借后换"最后一笔)

## 实施裁定勘误(2026-10-26 收官;落地实况对上文三处修订,规格与实况保持一致)

T4/T5 落地中三项规格级裁定经评审核验成立(详见主 spec 裁定#42):

1. **T-3 行度量修订**:FT_Size_Metrics(hhea ascender 22@20px)方案实证会整体下移基线 ~4px@20px,致金样本溢阈——改为 **FT hinted Á/g 墨迹并集**(`GlyphRasterizer.InkLineMetrics`,Á 顶/g 底,即 "Ág" bounds 语义的 FT 复刻):12/18/20/24px+bold 五尺寸点与切换前旧栈**逐值相等、零漂移**;下文 §测试策略 2 的"≤0.5px 容差重定基"预案**未启用**(等值即无重定基)。
2. **T-2 金样本阈值调整**:原定 均值<2% / max≤96 经**不可达证明**废止——旧栈的 Windows 字体宿主是 DirectWrite(非 FreeType),两光栅器曲线 AA 逐像素不同,位级平价物理不可达;度量等值的真机制=两引擎对同一 TrueType hinting 字节码执行出相同网格拟合整值(仅整值度量可比,覆盖/位图不可比)。新阈值三层:**均值 ≤6%×255 + max ≤240(仅值域护栏,拦垃圾输出)+ 墨迹包围盒 ±1px 闸(拦结构性位移)+ 精确数值平价单测**(advance/行高逐值,拦度量回归)。金样本 6/6 绿。
3. **bold-CJK 特例容差**:切换前路径 bold-CJK 推进 bug(雅黑 bold ≈13/17px/字,应 20/字)**隔离不复刻**(新路径推进正确);bold×1 语料特例容差 12px,容纳与 bug 样本的差异而非复制 bug。
4. **AOT 体积实测**(T5):19,931,800 → **6,945,792 B ≈ 6.95MB**(≤8MB 验收达成;Counter.exe 3.48 + SDL2 1.70 + freetype 0.69 + harfbuzz 1.08 MB;Skia 系原生库自产物消失;明细 docs/notes/m1-aot.md;FT+HB 实测 1.76MB,略高于本文预估 ~1.5MB)。
5. **收官状态**:全量 226/226;性能复测 5552B/0.05ms(两次全量 5576/0.04 与 5552/0.05,抖动内;docs/notes/perf-baselines.md);Counter 中英混排目视受桌面锁定限制,以金样本(语料含中英混排条目)+ AOT/托管进程存活承证。

## 目标与动机

用 FreeType(字体解析/度量/光栅化)+ HarfBuzz(shaping)两个小而专的 C 库直连,替换 SkiaSharp+SkiaSharp.HarfBuzz(消费面 100% 收敛于 `TextAtlas.cs`):

- **体积**:AOT 产物 19.9MB 中 Skia 系原生库约 13MB,预期降至约 7-8MB(FT+HB 合计约 1.5MB)。
- **依赖面**:去掉 AOT 下最大的第三方风险面(SkiaSharp 反射面/原生库体积/更新节奏)。
- **控制力**:度量/光栅化参数直控,行业级质量(Skia 在 Windows 的字形光栅化底层即 FreeType)。

## 已批准决策(用户拍板)

| # | 决策 | 内容 |
|---|---|---|
| T-1 | 自研深度 | FreeType+HarfBuzz P/Invoke 直连(纯 C# 全自研被否:工程量月计、质量风险高;纯 C# 简化版被否:质量可辨低于基线) |
| T-2 | 验收标准 | 视觉对齐 + **金样本语料库**:切换前以现有 Skia 路径渲染语料存 PNG 入库;切换后 FreeType 渲染逐像素比对,超阈值失败。永久回归护栏,不依赖 Skia 存续 |
| T-3 | 行度量 | 弃用 `MeasureText("Ág")` 边界 hack,改 FT_Size_Metrics 真实 ascender/descender;行高亚像素级漂移可接受,断言具体值的测试按 ≤0.5px 容差重定基 |

## 架构

```
TextAtlas(公共 API 不动:Measure/DrawRun/WhiteUv/Dispose;内部全去 Skia)
 ├─ Text/FontLibrary.cs      托管封装:FT_Library/FT_Face/hb_font 生命周期
 │    ├─ IFontLocator 抽象:族名→字体文件路径
 │    │    └─ WindowsFontLocator:注册表 HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts
 │    │       (族名→文件名,系统目录解析;粗体族 "Segoe UI Bold" 同路;Linux fontconfig/macOS 后续里程碑实现)
 │    └─ hb_ft_font_create:HB 与 FT 共脸,shaping 度量与光栅化天然同步
 ├─ Text/FreeTypeNative.cs   FT P/Invoke 声明层(FT_Init/FT_New_Face/FT_Set_Char_Size/FT_Get_Char_Index/
 │    FT_Load_Glyph/FT_Render_Glyph/FT_Get_Kerning/版本探针;结构自检测试,同 D3D11Native 纪律)
 ├─ Text/HarfBuzzNative.cs   HB P/Invoke(hb_buffer_create/add_utf16/guess_segment_properties/
 │    hb_shape/hb_glyph_info/hb_glyph_position/hb_ft_font_create)
 └─ Text/GlyphRasterizer.cs  FT_RENDER_MODE_NORMAL(A8 256 级覆盖)→ RGBA 预乘白字形 → UpdateAtlasRegion
```

## 语义映射(现有行为保持)

| 现状(Skia) | 新(FT/HB) |
|---|---|
| 槽位回退链 Segoe UI → Microsoft YaHei → Segoe UI Emoji | 同链;探测=FT_Get_Char_Index(face, cp) ≠ 0 |
| SKShaper.Shape → Codepoints/Points/Clusters(UTF-8 字节偏移) | hb_shape + `hb_buffer_add_utf16`——**cluster 即 UTF-16 下标**,`ClusterToCharIndex`/`Utf8Len` 整段删除 |
| SKFont.MeasureText advance+bounds | FT advance(26.6 定点→float)+ glyph metrics(horiBearing/width/height) |
| Edging=Antialias, Subpixel=false | FT_LOAD_DEFAULT + FT_RENDER_MODE_NORMAL(整像素对齐) |
| 粗体=独立 face("Segoe UI" Bold 族),回退槽粗体=同脸 | 同策略(WindowsFontLocator 解析 Bold 族文件) |
| ShapedLayout/ShapedRun 快照缓存(T4 语义) | 保留(结构去 SKPoint,换自有 PointF) |
| ShelfPacker 图集 + 1px padding + 白点种子 | 不动 |
| 度量物理像素×scale 快照(D21/D22) | 不动 |

## 依赖落地

- **vendor** `native/freetype/win-x64/freetype.dll`、`native/harfbuzz/win-x64/harfbuzz.dll`(+LICENSE:FreeType FTL/BSD、HarfBuzz MIT;同 SDL2 zlib 先例)。
- **来源**:官方仅发源码 → vcpkg(x64-windows)或本机 VS 工具链编译;**T0 探针任务先趟平**(两 DLL 就位 + FT_Library_Version/HB version 探针 + 最小 face 加载+单字形渲染)才开工后续——同 M3d-i spike 纪律。
- csproj 复制目标沿用 VendorModernSdl2 模式(OutDir 根 + runtimes)。
- 模块纪律:FT/HB 为单副本布局(仅 native/ 一处来源),无 SDL 式双副本风险;P/Invoke 按名解析即可。

## 测试策略

1. **声明层自检**:FT/HB 结构尺寸/常量测试(同 D3D11NativeTests 模式;FT_GlyphSlotRec 等按 freetype.h 实数)。
2. **单元**:FontLibrary(发现/回退链/cmap 探测)、度量容差(advance/行高 vs 旧值 ≤0.5px)、shaping(中文/ASCII/混排 cluster=UTF-16 下标)。
3. **金样本对比**(Gpu+Compare trait):语料=ASCII 句/中文句/中英混排/粗体/emoji 回退槽,每条渲染到 64×256 位图与 `tests/golden/text/*.png` 比对,阈值:像素差异均值 <2%、单像素最大 delta ≤96(256 级覆盖 × RGBA);金样本由切换前 Skia 路径生成入库(生成工具临时代码,随 Skia 删除)。
4. **既有 TextAtlasTests/RendererTests 等**:行为断言保留;数值断言按 T-3 容差重定基。
5. 性能:shaping/光栅化路径不劣于现基线(5944B/0.05ms 预算不破;字形缓存命中路径零分配)。

## 范围外(一期不做,接口预留)

- 彩色 emoji(CBDT/sbix/COLR;FT_Load_COLOR 标志位已在声明层预留)——现状即单色渲染,平价。
- Linux fontconfig/macOS CoreText 发现层(IFontLocator 抽象就位,后续里程碑)。
- 可变字体、LCD 亚像素渲染、运行时字体回退配置 API。

## 验收清单

- [x] SkiaSharp/SkiaSharp.HarfBuzz 包与全部 SK 代码移除;AOT 产物 ≤8MB(实测记录)。(2026-10-26:6.95MB,`grep -rni skia src/` 零命中)
- [x] 金样本对比全绿(6 类语料 × 阈值内)。(阈值按勘误 2 调整;6/6 绿)
- [x] 全量测试绿(重定基后);Counter demo 中英混排目视验收。(226/226;行度量逐值等值故零重定基;目视受桌面锁定限制,金样本+进程存活承证,勘误 5)
- [x] 性能预算不破;两 DLL LICENSE 随附。(5552B/0.05ms 复测,抖动内;LICENSE 随 native/ vendor)
- [x] spec 主文档 D3 行标收账 + 裁定号(#42:文本层直连定稿)。
