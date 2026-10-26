# Native Vendored Binaries

Provenance record for third-party native DLLs vendored under `native/`.
Layout follows the `native/SDL2/` precedent (`<lib>/win-x64/<lib>.dll` + `<lib>/LICENSE.txt`).

## freetype.dll (win-x64)

| field | value |
|---|---|
| library | FreeType |
| version | 2.13.3 |
| architecture | x64 (PE32+, machine 8664) |
| acquisition route | 本机 VS 编译(计划路线 2;vcpkg 未安装,无预编译含 hb-ft 配对) |
| source tarball | `freetype-2.13.3.tar.gz` |
| source URL | https://sourceforge.net/projects/freetype/files/freetype2/2.13.3/freetype-2.13.3.tar.gz/download (official SourceForge FreeType mirror; savannah was unreachable) |
| tarball sha256 | `5c3a8e78f7b24c20b25b54ee575d6daa40007a5f4eea2845861c3409b3021747` |
| built DLL sha256 | `f679662948be46b5b57fa73c26be3cb167485bf80afdb04460d7dd19a4e43207` |
| toolchain | VS2022 17.14 MSVC 19.44.35228 (x64), CMake 4.4.4, "Visual Studio 17 2022" generator, Release |
| cmake flags | `BUILD_SHARED_LIBS=ON`, `FT_DISABLE_ZLIB/BZIP2/PNG/HARFBUZZ/BROTLI=TRUE` (零第三方 DLL 依赖,仅 VC runtime/UCRT) |
| built | 2026-10-26 |
| license | FTL (FreeType License) / GPLv2 dual — see `freetype/LICENSE.txt` |

FTL.TXT/GPLv2.TXT 许可正文随附(终审裁定):`freetype/FTL.TXT` 与 `freetype/GPLv2.TXT`
逐字节取自上方 sha256 核验过的官方 tarball(`docs/FTL.TXT` `08c13575…9b86baa1`、
`docs/GPLv2.TXT` `c4120c67…5656c18`);`native/.gitattributes` 标记 `-text` 防换行改写。

## harfbuzz.dll (win-x64)

| field | value |
|---|---|
| library | HarfBuzz |
| version | 8.5.0 |
| architecture | x64 (PE32+, machine 8664) |
| acquisition route | 本机 VS 编译(计划路线 2;官方 win64 预编译 zip 为 MinGW 构建且 libharfbuzz-0.dll 不导出 hb_ft_*、依赖 glib,不达标) |
| source tarball | `harfbuzz-8.5.0.tar.xz` (last 8.x release tag) |
| source URL | https://github.com/harfbuzz/harfbuzz/releases/download/8.5.0/harfbuzz-8.5.0.tar.xz |
| tarball sha256 | `77e4f7f98f3d86bf8788b53e6832fb96279956e1c3961988ea3d4b7ca41ddc27` |
| built DLL sha256 | `0302dd82dedd5603973008ac810908f524dfd339c7206ccd62de2d47deca5632` |
| toolchain | VS2022 17.14 MSVC 19.44.35228 (x64), CMake 4.4.4, "Visual Studio 17 2022" generator, Release |
| cmake flags | `BUILD_SHARED_LIBS=ON`, `HB_HAVE_FREETYPE=ON` (linked against the freetype.dll above — paired ABI), `HB_BUILD_TESTS/UTILS/SUBSET=OFF`, `HB_HAVE_GLIB/ICU=OFF` |
| hb-ft evidence | `dumpbin /exports`: ordinal 239 `hb_ft_font_create`, 240 `hb_ft_font_create_referenced`; `dumpbin /dependents` lists `freetype.dll` |
| built | 2026-10-26 |
| license | Old MIT — see `harfbuzz/LICENSE.txt` |

## Rebuild recipe

```bat
:: prerequisites: VS2022 (vcvars64), CMake >= 3.12, tarballs above
cmake -S freetype-2.13.3 -B build-ft -G "Visual Studio 17 2022" -A x64 ^
  -DBUILD_SHARED_LIBS=ON -DFT_DISABLE_ZLIB=TRUE -DFT_DISABLE_BZIP2=TRUE ^
  -DFT_DISABLE_PNG=TRUE -DFT_DISABLE_HARFBUZZ=TRUE -DFT_DISABLE_BROTLI=TRUE ^
  -DCMAKE_INSTALL_PREFIX=%CD%\install-ft
cmake --build build-ft --config Release --parallel && cmake --install build-ft --config Release
cmake -S harfbuzz-8.5.0 -B build-hb -G "Visual Studio 17 2022" -A x64 ^
  -DBUILD_SHARED_LIBS=ON -DHB_HAVE_FREETYPE=ON ^
  -DFREETYPE_INCLUDE_DIR_ft2build=%CD%\install-ft\include\freetype2 ^
  -DFREETYPE_INCLUDE_DIR_freetype2=%CD%\install-ft\include\freetype2 ^
  -DFREETYPE_LIBRARY=%CD%\install-ft\lib\freetype.lib ^
  -DHB_BUILD_TESTS=OFF -DHB_BUILD_UTILS=OFF -DHB_BUILD_SUBSET=OFF ^
  -DHB_HAVE_GLIB=OFF -DHB_HAVE_ICU=OFF -DCMAKE_INSTALL_PREFIX=%CD%\install-hb
cmake --build build-hb --config Release --parallel && cmake --install build-hb --config Release
```

Note: built DLLs are not byte-reproducible (PDB path/timestamps embedded); the
sha256 values above identify the vendored copies, while tarball sha256 values
verify the sources against their official release URLs.
