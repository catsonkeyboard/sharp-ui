# SharpUI M1 基础里程碑 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 打通 SharpUI 最小闭环——纯 C# 声明式元素树,GPU(本机 D3D11)自绘窗口应用 Counter demo,Native AOT 发布可跑。

**Architecture:** 每帧重建元素树 → LayoutEngine 测量/排列 → 光栅化为单网格(图集 UV)→ Veldrid 单 draw call 提交。窗口/设备由 Veldrid.StartupUtilities(SDL2)创建;文本借 SkiaSharp 光栅化进自管图集。详见规格书 §4。

**Tech Stack:** .NET 10(net10.0)、Veldrid 4.9.0 + Veldrid.SPIRV 1.0.15 + Veldrid.StartupUtilities 4.9.0、SkiaSharp 4.153.1、xUnit。

**Spec:** `docs/superpowers/specs/2026-10-25-sharp-ui-spec.md`(本计划从规格书论证,执行者须同时阅读两者;规格书附录 A 是已在本机编译验证过的 Veldrid 4.9 真实 API,遇到 API 疑问以它为准,**不要**相信对新版 Veldrid 的记忆)

## Global Constraints

- 所有代码 `namespace SharpUI`;测试 `namespace SharpUI.Tests`;禁止 `unsafe`。
- `src/SharpUI/SharpUI.csproj` 恒有 `<IsAotCompatible>true</IsAotCompatible>`,构建零 AOT 分析器警告。
- 包版本锁定:Veldrid 4.9.0 / Veldrid.StartupUtilities 4.9.0 / Veldrid.SPIRV 1.0.15 / SkiaSharp 4.153.1 / Newtonsoft.Json 13.0.3(直接引用覆盖传递依赖,见规格书 R2)。
- 需要真实 GPU 的测试打 `[Trait("Category", "Gpu")]`;本机 `dotnet test` 必须全绿(含 Gpu)。
- 性能预算(Release):Counter 场景稳态每帧分配 ≤64KB、单帧 CPU <8ms(规格书 §9)。
- 提交信息用英文 Conventional Commits(`feat: ...` / `test: ...`);main 分支;每个任务至少一次提交。
- 每个任务的手测步骤在本机 Windows 执行;命令均在仓库根 `<repo-root>` 运行。

---

### Task 1: 仓库脚手架与构建基线

**Files:**
- Create: `.gitignore`、`SharpUI.sln`、`src/SharpUI/SharpUI.csproj`、`src/SharpUI/SharpUIInfo.cs`、`tests/SharpUI.Tests/SharpUI.Tests.csproj`、`tests/SharpUI.Tests/SharpUIInfoTests.cs`、`examples/Counter/Counter.csproj`、`examples/Counter/Program.cs`

**Interfaces:**
- Produces: `SharpUIInfo.Name : string`、`SharpUIInfo.Version : string`;解决方案结构(src/tests/examples 三项目),后续所有任务沿用。

- [ ] **Step 1: 初始化仓库与解决方案**

```bash
cd <repo-root>
git init -b main
printf 'bin/\nobj/\nartifacts/\n*.user\n.vs/\n' > .gitignore
dotnet new sln -n SharpUI
dotnet new classlib -n SharpUI -o src/SharpUI
rm src/SharpUI/Class1.cs
dotnet new xunit -n SharpUI.Tests -o tests/SharpUI.Tests
rm tests/SharpUI.Tests/UnitTest1.cs
dotnet new console -n Counter -o examples/Counter
dotnet sln add src/SharpUI tests/SharpUI.Tests examples/Counter
dotnet add tests/SharpUI.Tests reference src/SharpUI
dotnet add examples/Counter reference src/SharpUI
```

- [ ] **Step 2: 写 src/SharpUI/SharpUI.csproj(AOT 分析器常开)**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: 写 src/SharpUI/SharpUIInfo.cs**

```csharp
namespace SharpUI;

public static class SharpUIInfo
{
    public const string Name = "SharpUI";
    public const string Version = "0.1.0-m1";
}
```

- [ ] **Step 4: 写失败测试 tests/SharpUI.Tests/SharpUIInfoTests.cs**

```csharp
namespace SharpUI.Tests;

public class SharpUIInfoTests
{
    [Fact]
    public void AssemblyNameIsSharpUI()
    {
        Assert.Equal("SharpUI", typeof(SharpUIInfo).Assembly.GetName().Name);
        Assert.Equal("SharpUI", SharpUIInfo.Name);
        Assert.StartsWith("0.", SharpUIInfo.Version);
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test`
Expected: PASS(1 test)。若 examples/Counter 的 Program.cs 报 CS5001 之外的错,确认模板默认 `Console.WriteLine("Hello, World!")` 未被改动。

- [ ] **Step 6: 提交**

```bash
git add -A && git commit -m "chore: scaffold solution with AOT-compatible SharpUI library"
```

---

### Task 2: 几何与颜色原语

**Files:**
- Create: `src/SharpUI/Geometry.cs`、`src/SharpUI/Color32.cs`
- Test: `tests/SharpUI.Tests/GeometryTests.cs`、`tests/SharpUI.Tests/Color32Tests.cs`

**Interfaces:**
- Produces(后续任务大量使用,签名精确一致):
  - `readonly record struct SizeF(float Width, float Height)`(静态 `Zero`)
  - `readonly record struct PointF(float X, float Y)`(静态 `Zero`,运算符 `+`)
  - `readonly record struct RectF(float X, float Y, float Width, float Height)`(成员 `Left/Top/Right/Bottom/TopLeft/Size/IsEmpty/Contains(PointF)/Intersects(RectF)/Inflate(Thickness)/Deflate(Thickness)`)
  - `readonly record struct Thickness(float Left, float Top, float Right, float Bottom)`(构造 `(float all)`、`(float horizontal, float vertical)`,成员 `Horizontal/Vertical`)
  - `readonly record struct Color32(byte R, byte G, byte B, byte A = 255)`(`White/Black/Transparent` 静态、`FromRgb(uint)`、`WithAlpha(byte)`、`Premultiplied` 元组属性)

- [ ] **Step 1: 写失败测试 GeometryTests.cs**

```csharp
namespace SharpUI.Tests;

public class GeometryTests
{
    [Fact]
    public void Contains_IsInclusiveExclusive()
    {
        var r = new RectF(10, 10, 20, 20);
        Assert.True(r.Contains(new PointF(10, 10)));
        Assert.True(r.Contains(new PointF(29.9f, 29.9f)));
        Assert.False(r.Contains(new PointF(30, 10)));
        Assert.False(r.Contains(new PointF(9.9f, 10)));
    }

    [Fact]
    public void InflateDeflate_AreInverse()
    {
        var r = new RectF(10, 20, 30, 40);
        var t = new Thickness(2, 3, 4, 5);
        Assert.Equal(r, r.Inflate(t).Deflate(t));
    }

    [Fact]
    public void Deflate_ShrinksRect()
    {
        var r = new RectF(0, 0, 30, 30);
        var d = r.Deflate(new Thickness(5));
        Assert.Equal(new RectF(5, 5, 20, 20), d);
    }

    [Fact]
    public void Intersects_BasicCases()
    {
        var a = new RectF(0, 0, 10, 10);
        Assert.True(a.Intersects(new RectF(5, 5, 10, 10)));
        Assert.False(a.Intersects(new RectF(10, 10, 10, 10)));
        Assert.False(new RectF(0, 0, 0, 10).Intersects(a));
    }

    [Fact]
    public void Thickness_Aggregates()
    {
        var t = new Thickness(3, 7);
        Assert.Equal(6f, t.Horizontal);
        Assert.Equal(14f, t.Vertical);
    }
}
```

- [ ] **Step 2: 写失败测试 Color32Tests.cs**

```csharp
namespace SharpUI.Tests;

public class Color32Tests
{
    [Fact]
    public void FromRgb_SplitsChannels()
    {
        Color32 c = Color32.FromRgb(0x7AA2F7);
        Assert.Equal(0x7A, c.R);
        Assert.Equal(0xA2, c.G);
        Assert.Equal(0xF7, c.B);
        Assert.Equal(255, c.A);
    }

    [Fact]
    public void Premultiplied_ScalesByAlpha()
    {
        Color32 c = new(255, 100, 0, 128);
        (byte r, byte g, byte b) = c.Premultiplied;
        Assert.Equal(128, r);
        Assert.Equal(50, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void WithAlpha_KeepsChannels()
    {
        Color32 c = Color32.FromRgb(0x112233).WithAlpha(10);
        Assert.Equal((17, 34, 51, 10), (c.R, c.G, c.B, c.A));
    }
}
```

- [ ] **Step 3: 运行测试确认失败**

Run: `dotnet test --filter "FullyQualifiedName~Geometry|FullyQualifiedName~Color32"`
Expected: 编译 FAIL(`SizeF`/`RectF`/`Color32` 未定义)。

- [ ] **Step 4: 写实现 src/SharpUI/Geometry.cs**

```csharp
namespace SharpUI;

public readonly record struct SizeF(float Width, float Height)
{
    public static readonly SizeF Zero = new(0, 0);
    public static SizeF operator +(SizeF a, SizeF b) => new(a.Width + b.Width, a.Height + b.Height);
}

public readonly record struct PointF(float X, float Y)
{
    public static readonly PointF Zero = new(0, 0);
    public static PointF operator +(PointF a, PointF b) => new(a.X + b.X, a.Y + b.Y);
}

public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public PointF TopLeft => new(X, Y);
    public SizeF Size => new(Width, Height);
    public bool IsEmpty => Width <= 0f || Height <= 0f;

    public bool Contains(PointF p) =>
        p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public bool Intersects(RectF o) =>
        !IsEmpty && !o.IsEmpty && X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    public RectF Inflate(Thickness t) =>
        new(X - t.Left, Y - t.Top, Width + t.Horizontal, Height + t.Vertical);

    public RectF Deflate(Thickness t) =>
        new(X + t.Left, Y + t.Top, Width - t.Horizontal, Height - t.Vertical);
}

public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    public Thickness(float all) : this(all, all, all, all) { }
    public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}
```

- [ ] **Step 5: 写实现 src/SharpUI/Color32.cs**

```csharp
namespace SharpUI;

public readonly record struct Color32(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Color32 White = new(255, 255, 255);
    public static readonly Color32 Black = new(0, 0, 0);
    public static readonly Color32 Transparent = new(0, 0, 0, 0);

    public static Color32 FromRgb(uint rgb) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public Color32 WithAlpha(byte a) => new(R, G, B, a);

    /// 预乘 alpha:与 Byte4_Norm 顶点色、预乘图集、SingleOverrideBlend 配套。
    public (byte R, byte G, byte B) Premultiplied =>
        ((byte)(R * A / 255), (byte)(G * A / 255), (byte)(B * A / 255));
}
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test --filter "FullyQualifiedName~Geometry|FullyQualifiedName~Color32"`
Expected: PASS(8 tests)。

- [ ] **Step 7: 提交**

```bash
git add -A && git commit -m "feat: geometry primitives and premultiplied color"
```

---

### Task 3: 网格构建器(UiVertex / MeshBuilder)

**Files:**
- Create: `src/SharpUI/Mesh.cs`
- Test: `tests/SharpUI.Tests/MeshTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `PointF/RectF/Color32`。
- Produces:
  - `struct UiVertex`(顺序布局 `float X,Y,U,V; byte R,G,B,A;`,常量 `SizeInBytes = 20`,静态 `Create(PointF pos, PointF uv, Color32 color)`)
  - `sealed class MeshBuilder`:`Clear()`、`AddQuad(RectF rect, RectF uv, Color32 color)`、`AddTriangle(PointF a, PointF b, PointF c, Color32 color, PointF uv)`、`VertexCount`、`IndexCount`、`Vertices : ReadOnlySpan<UiVertex>`、`Indices : ReadOnlySpan<ushort>`(内部数组按需翻倍扩容,零稳态分配)

- [ ] **Step 1: 写失败测试 MeshTests.cs**

```csharp
using System.Runtime.InteropServices;
using SharpUI;

namespace SharpUI.Tests;

public class MeshTests
{
    [Fact]
    public void Vertex_Is20BytesBlittable()
    {
        Assert.Equal(UiVertex.SizeInBytes, (uint)Marshal.SizeOf<UiVertex>());
    }

    [Fact]
    public void AddQuad_AddsFourVertsSixIndices()
    {
        var mesh = new MeshBuilder();
        mesh.AddQuad(new RectF(0, 0, 10, 10), new RectF(0, 0, 1, 1), Color32.White);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(6, mesh.IndexCount);
        Assert.Equal(0, mesh.Indices[0]);
        Assert.Equal(1, mesh.Indices[1]);
        Assert.Equal(2, mesh.Indices[2]);
        Assert.Equal(0, mesh.Indices[3]);
        Assert.Equal(2, mesh.Indices[4]);
        Assert.Equal(3, mesh.Indices[5]);
    }

    [Fact]
    public void AddQuad_PremultipliesColor()
    {
        var mesh = new MeshBuilder();
        mesh.AddQuad(new RectF(0, 0, 1, 1), new RectF(0, 0, 0, 0), new Color32(255, 0, 0, 128));
        Assert.Equal(128, mesh.Vertices[0].R);
        Assert.Equal(0, mesh.Vertices[0].G);
        Assert.Equal(128, mesh.Vertices[0].A);
    }

    [Fact]
    public void AddTriangle_AddsThreeOfEach()
    {
        var mesh = new MeshBuilder();
        mesh.AddTriangle(new PointF(0, 0), new PointF(1, 0), new PointF(0, 1), Color32.White, new PointF(0.5f, 0.5f));
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(3, mesh.IndexCount);
    }

    [Fact]
    public void GrowsBeyondInitialCapacity_AndClears()
    {
        var mesh = new MeshBuilder();
        for (int i = 0; i < 400; i++)             // 初始容量 1024 顶点,400 quads = 1600 顶点
            mesh.AddQuad(new RectF(i, 0, 1, 1), new RectF(0, 0, 1, 1), Color32.White);
        Assert.Equal(1600, mesh.VertexCount);
        Assert.Equal(2400, mesh.IndexCount);
        mesh.Clear();
        Assert.Equal(0, mesh.VertexCount);
        Assert.Equal(0, mesh.IndexCount);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test --filter FullyQualifiedName~MeshTests`
Expected: 编译 FAIL(`UiVertex`/`MeshBuilder` 未定义)。

- [ ] **Step 3: 写实现 src/SharpUI/Mesh.cs**

```csharp
using System.Runtime.InteropServices;

namespace SharpUI;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UiVertex
{
    public float X, Y;      // 屏幕像素,左上原点
    public float U, V;      // 图集 UV,归一化
    public byte R, G, B, A; // 预乘颜色
    public const uint SizeInBytes = 20;

    public static UiVertex Create(PointF pos, PointF uv, Color32 color)
    {
        (byte r, byte g, byte b) = color.Premultiplied;
        return new UiVertex
        {
            X = pos.X, Y = pos.Y, U = uv.X, V = uv.Y,
            R = r, G = g, B = b, A = color.A,
        };
    }
}

public sealed class MeshBuilder
{
    UiVertex[] _vertices = new UiVertex[1024];
    ushort[] _indices = new ushort[2048];
    int _vertexCount, _indexCount;

    public int VertexCount => _vertexCount;
    public int IndexCount => _indexCount;
    public ReadOnlySpan<UiVertex> Vertices => _vertices.AsSpan(0, _vertexCount);
    public ReadOnlySpan<ushort> Indices => _indices.AsSpan(0, _indexCount);

    public void Clear()
    {
        _vertexCount = 0;
        _indexCount = 0;
    }

    void Ensure(int extraVerts, int extraIndices)
    {
        if (_vertexCount + extraVerts > _vertices.Length)
            Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _vertexCount + extraVerts));
        if (_indexCount + extraIndices > _indices.Length)
            Array.Resize(ref _indices, Math.Max(_indices.Length * 2, _indexCount + extraIndices));
    }

    public void AddQuad(RectF rect, RectF uv, Color32 color)
    {
        Ensure(4, 6);
        ushort b = (ushort)_vertexCount;
        _vertices[_vertexCount++] = UiVertex.Create(new PointF(rect.X, rect.Y), new PointF(uv.X, uv.Y), color);
        _vertices[_vertexCount++] = UiVertex.Create(new PointF(rect.Right, rect.Y), new PointF(uv.Right, uv.Y), color);
        _vertices[_vertexCount++] = UiVertex.Create(new PointF(rect.Right, rect.Bottom), new PointF(uv.Right, uv.Bottom), color);
        _vertices[_vertexCount++] = UiVertex.Create(new PointF(rect.X, rect.Bottom), new PointF(uv.X, uv.Bottom), color);
        _indices[_indexCount++] = b;
        _indices[_indexCount++] = (ushort)(b + 1);
        _indices[_indexCount++] = (ushort)(b + 2);
        _indices[_indexCount++] = b;
        _indices[_indexCount++] = (ushort)(b + 2);
        _indices[_indexCount++] = (ushort)(b + 3);
    }

    public void AddTriangle(PointF a, PointF b, PointF c, Color32 color, PointF uv)
    {
        Ensure(3, 3);
        ushort i = (ushort)_vertexCount;
        _vertices[_vertexCount++] = UiVertex.Create(a, uv, color);
        _vertices[_vertexCount++] = UiVertex.Create(b, uv, color);
        _vertices[_vertexCount++] = UiVertex.Create(c, uv, color);
        _indices[_indexCount++] = i;
        _indices[_indexCount++] = (ushort)(i + 1);
        _indices[_indexCount++] = (ushort)(i + 2);
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test --filter FullyQualifiedName~MeshTests`
Expected: PASS(5 tests)。

- [ ] **Step 5: 提交**

```bash
git add -A && git commit -m "feat: mesh builder with premultiplied 20-byte vertex"
```

---

### Task 4: 圆角矩形细分

**Files:**
- Create: `src/SharpUI/Tessellation.cs`
- Test: `tests/SharpUI.Tests/TessellationTests.cs`

**Interfaces:**
- Consumes: `MeshBuilder/Color32/RectF/PointF`。
- Produces: `static class Tessellator`:`const int CornerSegments = 8;`、`AddRoundedRect(MeshBuilder mesh, RectF rect, float radius, Color32 color, PointF whiteUv)`。半径 ≥ 半宽高时钳制;半径 <0.5 时退化为单 quad。

- [ ] **Step 1: 写失败测试 TessellationTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class TessellationTests
{
    static readonly PointF White = new(0.0005f, 0.0005f);

    [Fact]
    public void ZeroRadius_FastPathSingleQuad()
    {
        var mesh = new MeshBuilder();
        Tessellator.AddRoundedRect(mesh, new RectF(0, 0, 100, 50), 0f, Color32.White, White);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(6, mesh.IndexCount);
    }

    [Fact]
    public void Rounded_ExactVertexIndexCounts()
    {
        // 3 个矩形条带:12 顶点/18 索引;4 角 × 8 扇形三角:96 顶点/96 索引
        var mesh = new MeshBuilder();
        Tessellator.AddRoundedRect(mesh, new RectF(0, 0, 100, 50), 8f, Color32.White, White);
        Assert.Equal(108, mesh.VertexCount);
        Assert.Equal(114, mesh.IndexCount);
    }

    [Fact]
    public void Radius_ClampedToHalfOfSmallerSide()
    {
        var mesh = new MeshBuilder();
        Tessellator.AddRoundedRect(mesh, new RectF(0, 0, 100, 50), 500f, Color32.White, White);
        Assert.Equal(108, mesh.VertexCount);   // 未爆掉、未 NaN,与正常圆角路径相同的计数
        bool anyNaN = false;
        for (int i = 0; i < mesh.VertexCount; i++)
            if (float.IsNaN(mesh.Vertices[i].X) || float.IsNaN(mesh.Vertices[i].Y)) anyNaN = true;
        Assert.False(anyNaN);
    }

    [Fact]
    public void CornerTriangles_StayWithinRectBounds()
    {
        var mesh = new MeshBuilder();
        Tessellator.AddRoundedRect(mesh, new RectF(10, 10, 100, 50), 10f, Color32.White, White);
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            Assert.InRange(mesh.Vertices[i].X, 9.9f, 110.1f);
            Assert.InRange(mesh.Vertices[i].Y, 9.9f, 60.1f);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test --filter FullyQualifiedName~TessellationTests`
Expected: 编译 FAIL(`Tessellator` 未定义)。

- [ ] **Step 3: 写实现 src/SharpUI/Tessellation.cs**

```csharp
namespace SharpUI;

public static class Tessellator
{
    public const int CornerSegments = 8;

    public static void AddRoundedRect(MeshBuilder mesh, RectF rect, float radius, Color32 color, PointF whiteUv)
    {
        radius = MathF.Min(radius, MathF.Min(rect.Width, rect.Height) / 2f);
        if (radius < 0.5f)
        {
            mesh.AddQuad(rect, new RectF(whiteUv.X, whiteUv.Y, 0f, 0f), color);
            return;
        }

        var solidUv = new RectF(whiteUv.X, whiteUv.Y, 0f, 0f);   // 退化 UV = 纯色
        mesh.AddQuad(new RectF(rect.X + radius, rect.Y, rect.Width - 2f * radius, rect.Height), solidUv, color);
        mesh.AddQuad(new RectF(rect.X, rect.Y + radius, radius, rect.Height - 2f * radius), solidUv, color);
        mesh.AddQuad(new RectF(rect.Right - radius, rect.Y + radius, radius, rect.Height - 2f * radius), solidUv, color);

        AddCorner(mesh, new PointF(rect.X + radius, rect.Y + radius), radius, MathF.PI, MathF.PI * 1.5f, color, whiteUv);
        AddCorner(mesh, new PointF(rect.Right - radius, rect.Y + radius), radius, MathF.PI * 1.5f, MathF.PI * 2f, color, whiteUv);
        AddCorner(mesh, new PointF(rect.Right - radius, rect.Bottom - radius), radius, 0f, MathF.PI * 0.5f, color, whiteUv);
        AddCorner(mesh, new PointF(rect.X + radius, rect.Bottom - radius), radius, MathF.PI * 0.5f, MathF.PI, color, whiteUv);
    }

    static void AddCorner(MeshBuilder mesh, PointF center, float radius, float a0, float a1, Color32 color, PointF uv)
    {
        PointF prev = PointOnArc(center, radius, a0);
        for (int i = 1; i <= CornerSegments; i++)
        {
            PointF next = PointOnArc(center, radius, a0 + (a1 - a0) * i / CornerSegments);
            mesh.AddTriangle(center, prev, next, color, uv);
            prev = next;
        }
    }

    static PointF PointOnArc(PointF center, float radius, float angle) =>
        new(center.X + radius * MathF.Cos(angle), center.Y + radius * MathF.Sin(angle));
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test --filter FullyQualifiedName~TessellationTests`
Expected: PASS(4 tests)。

- [ ] **Step 5: 提交**

```bash
git add -A && git commit -m "feat: rounded rectangle tessellation with center fans"
```

---

### Task 5: 着色器与 GraphicsContext(Veldrid 封装)

**Files:**
- Create: `src/SharpUI/Shaders.cs`、`src/SharpUI/GraphicsContext.cs`
- Modify: `src/SharpUI/SharpUI.csproj`(加包引用)
- Test: `tests/SharpUI.Tests/GraphicsContextTests.cs`(Gpu)

**Interfaces:**
- Consumes: `Shaders` 由本任务定义。
- Produces:
  - `static class Shaders`:`VertexSource`、`FragmentSource`(GLSL 450 字符串)
  - `sealed class GraphicsContext : IDisposable`:
    - `const int AtlasSize = 1024`
    - `GraphicsDevice Device`、`CommandList Commands`、`Pipeline Pipeline`、`DeviceBuffer ScreenSizeBuffer`、`Texture AtlasTexture`、`TextureView AtlasView`、`Sampler AtlasSampler`、`ResourceSet AtlasSet`、`Framebuffer MainFramebuffer`
    - `static CreateForWindow(Sdl2Window window, GraphicsDevice device)`、`static CreateHeadless(uint width, uint height)`
    - `UpdateAtlasRegion(byte[] rgbaPixels, uint x, uint y, uint width, uint height)`、`ReadBackMainFramebuffer() → byte[]`
  - 后续任务只允许触碰以上成员(渲染器窄接口,规格书 D2)。

- [ ] **Step 1: 加包引用(含 Newtonsoft 安全覆盖,规格书 R2)**

在 `src/SharpUI/SharpUI.csproj` 的 `</PropertyGroup>` 之后追加:

```xml
  <ItemGroup>
    <PackageReference Include="Veldrid" Version="4.9.0" />
    <PackageReference Include="Veldrid.SPIRV" Version="1.0.15" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
```

Run: `dotnet restore` — Expected: 无 NU1903(若仍出现,检查 tests/examples 未直接引 Veldrid.SPIRV 旧链)。

- [ ] **Step 2: 写失败测试 GraphicsContextTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class GraphicsContextTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void HeadlessDevice_CompilesShadersAndBuildsPipeline()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        Assert.Equal(1024, GraphicsContext.AtlasSize);
        Assert.Equal(64u, ctx.MainFramebuffer.Width);
        Assert.NotNull(ctx.Pipeline);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void AtlasRegion_UpdateAndReadBack()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 0, 0, 255 }, 0, 0, 1, 1);  // 上传本身不抛即通过,读回断言在 Task 6 的渲染链路验证
        byte[] pixels = ctx.ReadBackMainFramebuffer();
        Assert.Equal(64u * 64u * 4u, (uint)pixels.Length);
    }
}
```

- [ ] **Step 3: 运行测试确认失败**

Run: `dotnet test --filter FullyQualifiedName~GraphicsContextTests`
Expected: 编译 FAIL(`GraphicsContext` 未定义)。

- [ ] **Step 4: 写实现 src/SharpUI/Shaders.cs**

```csharp
namespace SharpUI;

public static class Shaders
{
    public const string VertexSource = """
        #version 450
        layout(location = 0) in vec2 in_position;
        layout(location = 1) in vec2 in_texCoord;
        layout(location = 2) in vec4 in_color;
        layout(set = 0, binding = 0) uniform t_Uniforms { vec2 u_screenSize; };
        layout(location = 0) out vec2 f_texCoord;
        layout(location = 1) out vec4 f_color;
        void main()
        {
            vec2 clip = vec2(in_position.x / u_screenSize.x * 2.0 - 1.0,
                             1.0 - in_position.y / u_screenSize.y * 2.0);
            gl_Position = vec4(clip, 0.0, 1.0);
            f_texCoord = in_texCoord;
            f_color = in_color;
        }
        """;

    public const string FragmentSource = """
        #version 450
        layout(set = 0, binding = 1) uniform texture2D t_atlas;
        layout(set = 0, binding = 2) uniform sampler t_atlasSampler;
        layout(location = 0) in vec2 f_texCoord;
        layout(location = 1) in vec4 f_color;
        layout(location = 0) out vec4 o_color;
        void main()
        {
            vec4 texel = texture(sampler2D(t_atlas, t_atlasSampler), f_texCoord);
            o_color = f_color * texel;
        }
        """;
}
```

- [ ] **Step 5: 写实现 src/SharpUI/GraphicsContext.cs**

注意:所有 API 按 4.9 真实签名书写(规格书附录 A),与新版记忆的差异处已注释。

```csharp
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;

namespace SharpUI;

public sealed class GraphicsContext : IDisposable
{
    public const int AtlasSize = 1024;

    public GraphicsDevice Device { get; }
    public CommandList Commands { get; }
    public Pipeline Pipeline { get; }
    public DeviceBuffer ScreenSizeBuffer { get; }
    public Texture AtlasTexture { get; }
    public TextureView AtlasView { get; }
    public Sampler AtlasSampler { get; }
    public ResourceSet AtlasSet { get; }
    public Framebuffer MainFramebuffer { get; }

    Texture? _readbackStaging;

    GraphicsContext(GraphicsDevice device, Framebuffer mainFramebuffer)
    {
        Device = device;
        MainFramebuffer = mainFramebuffer;
        ResourceFactory factory = device.ResourceFactory;
        Commands = factory.CreateCommandList();

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm));

        ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ScreenSize", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("Atlas", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("AtlasSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        // GLSL → SPIR-V → 按后端交叉编译(HLSL/MSL/GLSL),三后端一份源码
        Shader[] shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(Shaders.VertexSource), "main"),
            new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(Shaders.FragmentSource), "main"));

        Pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: false, depthWriteEnabled: false, comparisonKind: ComparisonKind.Always),
            RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise, depthClipEnabled: false, scissorTestEnabled: false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { layout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, shaders),
            Outputs = mainFramebuffer.OutputDescription,
        });

        ScreenSizeBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        AtlasTexture = factory.CreateTexture(TextureDescription.Texture2D(
            AtlasSize, AtlasSize, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        AtlasView = factory.CreateTextureView(AtlasTexture);
        AtlasSampler = factory.CreateSampler(SamplerDescription.Linear);
        AtlasSet = factory.CreateResourceSet(new ResourceSetDescription(layout, ScreenSizeBuffer, AtlasView, AtlasSampler));
    }

    public static GraphicsContext CreateForWindow(Sdl2Window window, GraphicsDevice device) =>
        new(device, device.MainSwapchain.Framebuffer);

    public static GraphicsContext CreateHeadless(uint width, uint height)
    {
        GraphicsDevice device = GraphicsDevice.CreateD3D11(new GraphicsDeviceOptions { Debug = false });
        Texture colorTarget = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer framebuffer = device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, colorTarget));
        return new GraphicsContext(device, framebuffer);
    }

    public void UpdateAtlasRegion(byte[] rgbaPixels, uint x, uint y, uint width, uint height) =>
        Device.UpdateTexture(AtlasTexture, rgbaPixels, x, y, 0, width, height, 1, 0, 0);

    /// <summary>读回 MainFramebuffer 颜色目标(GPU 测试用)。M1 限制:仅支持行字节数 256 对齐的宽度(64/128/256…)。</summary>
    public byte[] ReadBackMainFramebuffer()
    {
        Texture source = MainFramebuffer.ColorTargets[0].Target;
        uint rowBytes = source.Width * 4;
        if (rowBytes % 256 != 0)
            throw new NotSupportedException($"M1 读回要求 width*4 对齐 256,当前 {rowBytes}");
        _readbackStaging ??= Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            source.Width, source.Height, 1, 1, source.Format, TextureUsage.Staging));
        CommandList cl = Commands;
        cl.Begin();
        cl.CopyTexture(source, _readbackStaging);
        cl.End();
        Device.SubmitCommands(cl);
        MappedResource mapped = Device.Map(_readbackStaging, MapMode.Read);
        byte[] pixels = new byte[mapped.SizeInBytes];
        Marshal.Copy(mapped.Data, pixels, 0, pixels.Length);
        Device.Unmap(_readbackStaging);
        return pixels;
    }

    public void Dispose()
    {
        _readbackStaging?.Dispose();
        AtlasSet.Dispose();
        AtlasSampler.Dispose();
        AtlasView.Dispose();
        AtlasTexture.Dispose();
        ScreenSizeBuffer.Dispose();
        Pipeline.Dispose();
        Commands.Dispose();
        Device.Dispose();
    }
}
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test --filter FullyQualifiedName~GraphicsContextTests`
Expected: PASS(2 tests,Gpu;首次运行会编译着色器,稍慢)。若 SPIR-V 编译报 GLSL 语法错,优先检查字符串是否完整(heredoc)。

- [ ] **Step 7: 提交**

```bash
git add -A && git commit -m "feat: veldrid graphics context with cross-compiled shaders and atlas"
```

---

### Task 6: Renderer(帧上传与提交)+ 像素级 GPU 测试

**Files:**
- Create: `src/SharpUI/Renderer.cs`
- Test: `tests/SharpUI.Tests/RendererTests.cs`(Gpu)

**Interfaces:**
- Consumes: `GraphicsContext`(Task 5)、`MeshBuilder/UiVertex/SizeF`(Task 3)。
- Produces: `sealed class Renderer : IDisposable`:`ctor(GraphicsContext ctx)`、`Submit(MeshBuilder mesh, SizeF screenSize, Framebuffer framebuffer)`(上传 + 清屏 `0.07,0.07,0.08,1` + 单 draw call;`IndexCount==0` 时直接返回)、`Dispose()`(动态缓冲按需翻倍重建)。

- [ ] **Step 1: 写失败测试 RendererTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class RendererTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void RendersWhiteQuadOverClearColor()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, 0, 0, 1, 1);
        var mesh = new MeshBuilder();
        float inv = 1f / GraphicsContext.AtlasSize;
        mesh.AddQuad(new RectF(16, 16, 32, 32), new RectF(0, 0, inv, inv), Color32.White);

        using var renderer = new Renderer(ctx);
        renderer.Submit(mesh, new SizeF(64, 64), ctx.MainFramebuffer);

        byte[] pixels = ctx.ReadBackMainFramebuffer();
        // B8G8R8A8:中心点应为白色
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(pixels, 32, 32));
        // 角落为清屏色 0.07,0.07,0.08 → B=18 G=18 R=20 A=255
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(pixels, 4, 4));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void EmptyMesh_SubmitsNothing()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var renderer = new Renderer(ctx);
        renderer.Submit(new MeshBuilder(), new SizeF(64, 64), ctx.MainFramebuffer);
        byte[] pixels = ctx.ReadBackMainFramebuffer();
        Assert.Equal(new byte[] { 18, 18, 20, 255 }, Pixel(pixels, 32, 32));
    }

    static byte[] Pixel(byte[] rgba, int x, int y) => rgba.AsSpan((y * 64 + x) * 4, 4).ToArray();
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test --filter FullyQualifiedName~RendererTests`
Expected: 编译 FAIL(`Renderer` 未定义)。

- [ ] **Step 3: 写实现 src/SharpUI/Renderer.cs**

```csharp
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
        if (mesh.IndexCount == 0) return;
        EnsureCapacity(mesh);

        Span<byte> uniform = stackalloc byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(uniform, screenSize.Width);
        BinaryPrimitives.WriteSingleLittleEndian(uniform.Slice(4), screenSize.Height);

        CommandList cl = _ctx.Commands;
        cl.Begin();
        cl.UpdateBuffer(_ctx.ScreenSizeBuffer, 0, uniform);
        cl.UpdateBuffer(_vertexBuffer, 0, mesh.Vertices);
        cl.UpdateBuffer(_indexBuffer, 0, mesh.Indices);
        cl.SetFramebuffer(framebuffer);
        cl.ClearColorTarget(0, new RgbaFloat(0.07f, 0.07f, 0.08f, 1f));
        cl.SetPipeline(_ctx.Pipeline);
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        cl.SetGraphicsResourceSet(0, _ctx.AtlasSet);
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
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test --filter FullyQualifiedName~RendererTests`
Expected: PASS(2 tests)。这是 M1 最关键的一环:像素断言过了,说明 顶点布局/着色器/混合/坐标变换/图集 全链路正确。

- [ ] **Step 5: 提交**

```bash
git add -A && git commit -m "feat: renderer with single-drawcall frame submit, gpu pixel tests green"
```

---

### Task 7: App 窗口与清屏事件循环

**Files:**
- Create: `src/SharpUI/App.cs`
- Modify: `src/SharpUI/SharpUI.csproj`(加 Veldrid.StartupUtilities)、`examples/Counter/Program.cs`
- Test: 手测(本任务无自动化测试;窗口属人工验收)

**Interfaces:**
- Consumes: `GraphicsContext.CreateForWindow`、`Renderer.Submit`。
- Produces: `sealed class App`:`static Create(string title, int width, int height)`、`int Run(int maxFrames = 0)`(M1 版本:仅清屏循环;Task 10/11 扩展为元素树渲染)。后续任务直接修改 `Run` 内部。

- [ ] **Step 1: 加包引用**

`src/SharpUI/SharpUI.csproj` 的 ItemGroup 追加一行:

```xml
    <PackageReference Include="Veldrid.StartupUtilities" Version="4.9.0" />
```

- [ ] **Step 2: 写实现 src/SharpUI/App.cs(M1 清屏版)**

```csharp
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace SharpUI;

public sealed class App
{
    readonly Sdl2Window _window;
    readonly GraphicsContext _ctx;
    readonly Renderer _renderer;

    App(Sdl2Window window, GraphicsDevice device)
    {
        _window = window;
        _ctx = GraphicsContext.CreateForWindow(window, device);
        _renderer = new Renderer(_ctx);
    }

    public static App Create(string title, int width, int height)
    {
        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo { WindowTitle = title, WindowWidth = width, WindowHeight = height },
            new GraphicsDeviceOptions { Debug = false, PreferStandardClipSpaceYDirection = true },
            VeldridStartup.GetPlatformDefaultBackend(),
            out Sdl2Window window,
            out GraphicsDevice device);
        window.Resized += () => device.ResizeMainWindow((uint)window.Width, (uint)window.Height);
        return new App(window, device);
    }

    public int Run(int maxFrames = 0)
    {
        var mesh = new MeshBuilder();
        int frames = 0;
        while (_window.Exists)
        {
            _window.PumpEvents();
            SizeF screen = new(_window.Width, _window.Height);
            _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer);
            _ctx.Device.SwapBuffers();
            if (maxFrames > 0 && ++frames >= maxFrames)
                _window.Close();
        }
        return 0;
    }
}
```

- [ ] **Step 3: 写 examples/Counter/Program.cs(临时清屏版)**

```csharp
using SharpUI;

internal static class Program
{
    private static int Main() => App.Create("SharpUI", 480, 320).Run();
}
```

- [ ] **Step 4: 构建并全量测试**

Run: `dotnet build && dotnet test`
Expected: BUILD OK,全部 PASS(此前所有测试不回归)。

- [ ] **Step 5: 手测**

Run: `dotnet run -c Debug --project examples/Counter`
Expected: 弹出 480×320 深灰蓝(0.07,0.07,0.08)窗口,拖拽边缘可调整大小且内容跟随、关闭按钮退出、无崩溃无闪烁。手测完成后关闭窗口。

- [ ] **Step 6: 提交**

```bash
git add -A && git commit -m "feat: sdl2 window app with vsync clear loop"
```

---

### Task 8: 布局引擎与 Element 基类

**Files:**
- Create: `src/SharpUI/Layout.cs`、`src/SharpUI/Element.cs`
- Test: `tests/SharpUI.Tests/LayoutTests.cs`

**Interfaces:**
- Produces(Task 9/10/11 全部依赖,签名勿改):
  - `enum Axis { Vertical, Horizontal }`、`enum MainAxisAlignment { Start, Center, End }`、`enum CrossAxisAlignment { Start, Center, End, Stretch }`
  - `sealed class Style { Axis Axis = Vertical; MainAxisAlignment MainAlign = Start; CrossAxisAlignment CrossAlign = Start; float Gap; Thickness Padding; }`
  - `abstract class Element`:`Style Style`(get)、`List<Element>? Children`(get)、`RectF Frame`、`SizeF Measured`、`bool IsHitTarget`、`Add(Element)`、`protected internal virtual SizeF MeasureSelf(SizeF available)`、`public virtual void Draw(in PaintContext pc)`、`HitTest(PointF) → Element?`
  - `sealed class Spacer : Element`:`ctor(SizeF size)`
  - `readonly record struct PaintContext(MeshBuilder Mesh)`(Task 10 扩展为带 Atlas,Task 11 再加 Input)
  - `static class LayoutEngine`:`Measure(Element, SizeF) → SizeF`、`Arrange(Element, RectF absoluteFrame)`

- [ ] **Step 1: 写失败测试 LayoutTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class LayoutTests
{
    [Fact]
    public void Column_StacksVerticallyWithGapAndPadding()
    {
        Element root = new ColumnShim(
            new Spacer(new SizeF(10, 20)),
            new Spacer(new SizeF(10, 30)))
        { Style = { Gap = 5, Padding = new Thickness(2) } };
        SizeF m = LayoutEngine.Measure(root, new SizeF(100, 100));
        Assert.Equal(14f, m.Width);      // 10 + padding 4
        Assert.Equal(61f, m.Height);     // 20 + 30 + gap 5 + padding 4
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 100));
        Assert.Equal(new RectF(2, 2, 10, 20), root.Children![0].Frame);
        Assert.Equal(new RectF(2, 27, 10, 30), root.Children![1].Frame);
    }

    [Fact]
    public void Row_ArrangesHorizontally()
    {
        Element root = new ColumnShim(new Spacer(new SizeF(20, 10)), new Spacer(new SizeF(30, 10)))
        { Style = { Axis = Axis.Horizontal } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 100));
        Assert.Equal(new RectF(0, 0, 20, 10), root.Children![0].Frame);
        Assert.Equal(new RectF(20, 0, 30, 10), root.Children![1].Frame);
    }

    [Fact]
    public void MainCenter_CentersContent()
    {
        Element root = new ColumnShim(new Spacer(new SizeF(10, 10)))
        { Style = { MainAlign = MainAxisAlignment.Center } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 100, 60));
        Assert.Equal(25f, root.Children![0].Frame.Y);   // (60-10)/2
    }

    [Fact]
    public void CrossCenter_AndStretch()
    {
        Element center = new ColumnShim(new Spacer(new SizeF(10, 10)))
            { Style = { CrossAlign = CrossAxisAlignment.Center } };
        LayoutEngine.Arrange(center, new RectF(0, 0, 100, 60));
        Assert.Equal(45f, center.Children![0].Frame.X); // (100-10)/2

        Element stretch = new ColumnShim(new Spacer(new SizeF(10, 10)))
            { Style = { CrossAlign = CrossAxisAlignment.Stretch } };
        LayoutEngine.Arrange(stretch, new RectF(0, 0, 100, 60));
        Assert.Equal(100f, stretch.Children![0].Frame.Width);
    }

    [Fact]
    public void NestedContainers_UseAbsoluteCoordinates()
    {
        Element root = new ColumnShim(
            new ColumnShim(new Spacer(new SizeF(5, 5))) { Style = { Padding = new Thickness(10) } })
        { Style = { Padding = new Thickness(1) } };
        LayoutEngine.Arrange(root, new RectF(0, 0, 500, 500));
        Assert.Equal(new RectF(1, 1, 25, 25), root.Children![0].Frame);
        Assert.Equal(new RectF(11, 11, 5, 5), root.Children![0].Children![0].Frame);
    }

    [Fact]
    public void Measure_LeavesDecideOwnSize_SpacerDoesNotShrink()
    {
        // M1 语义:叶子自报固定尺寸,不因可用空间截断;溢出处理(裁剪/滚动)是 M2 的事
        Element root = new ColumnShim(new Spacer(new SizeF(10, 60)), new Spacer(new SizeF(10, 60)))
            { Style = { Gap = 5 } };
        SizeF m = LayoutEngine.Measure(root, new SizeF(100, 80));
        Assert.Equal(125f, m.Height);  // 60 + 5 + 60,Spacer 不收缩
        Assert.Equal(10f, m.Width);
    }

    /// 测试用容器:无叶子自测尺寸,纯靠子元素。
    sealed class ColumnShim : Element
    {
        public ColumnShim(params Element[] children)
        {
            foreach (Element c in children) Add(c);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test --filter FullyQualifiedName~LayoutTests`
Expected: 编译 FAIL(`Element/LayoutEngine` 未定义)。

- [ ] **Step 3: 写实现 src/SharpUI/Element.cs**

```csharp
namespace SharpUI;

/// <summary>绘制上下文。M1 最小形态:仅网格;Task 10 增 Atlas,Task 11 增 Input。</summary>
public readonly record struct PaintContext(MeshBuilder Mesh);

public abstract class Element
{
    public Style Style { get; } = new();
    public List<Element>? Children { get; private set; }
    public RectF Frame;      // Arrange 后的绝对坐标
    public SizeF Measured;   // Arrange 期间的度量缓存
    public bool IsHitTarget; // 命中测试终点(Button = true)

    public Element Add(Element child)
    {
        Children ??= new List<Element>(4);
        Children.Add(child);
        return this;
    }

    /// <summary>叶子自测尺寸;容器尺寸由 LayoutEngine 依据子元素推导。</summary>
    protected internal virtual SizeF MeasureSelf(SizeF available) => SizeF.Zero;

    public virtual void Draw(in PaintContext pc)
    {
        if (Children is null) return;
        foreach (Element child in Children)
            child.Draw(in pc);
    }

    public Element? HitTest(PointF p)
    {
        if (!Frame.Contains(p)) return null;
        if (Children is not null)
            for (int i = Children.Count - 1; i >= 0; i--)
                if (Children[i].HitTest(p) is { } hit)
                    return hit;
        return IsHitTarget ? this : null;
    }
}

public sealed class Spacer : Element
{
    readonly SizeF _size;
    public Spacer(SizeF size) => _size = size;
    protected internal override SizeF MeasureSelf(SizeF available) => _size;
}
```

- [ ] **Step 4: 写实现 src/SharpUI/Layout.cs**

```csharp
namespace SharpUI;

public enum Axis { Vertical, Horizontal }
public enum MainAxisAlignment { Start, Center, End }
public enum CrossAxisAlignment { Start, Center, End, Stretch }

public sealed class Style
{
    public Axis Axis = Axis.Vertical;
    public MainAxisAlignment MainAlign = MainAxisAlignment.Start;
    public CrossAxisAlignment CrossAlign = CrossAxisAlignment.Start;
    public float Gap;
    public Thickness Padding;
}

public static class LayoutEngine
{
    public static SizeF Measure(Element e, SizeF available)
    {
        if (e.Children is not { Count: > 0 })
            return e.MeasureSelf(available);

        bool vertical = e.Style.Axis == Axis.Vertical;
        Thickness pad = e.Style.Padding;
        SizeF inner = new(
            MathF.Max(0f, available.Width - pad.Horizontal),
            MathF.Max(0f, available.Height - pad.Vertical));
        float main = 0f, cross = 0f;
        int i = 0;
        foreach (Element child in e.Children)
        {
            if (i++ > 0) main += e.Style.Gap;
            SizeF avail = vertical
                ? new SizeF(inner.Width, MathF.Max(0f, inner.Height - main))
                : new SizeF(MathF.Max(0f, inner.Width - main), inner.Height);
            SizeF m = Measure(child, avail);
            main += vertical ? m.Height : m.Width;
            cross = MathF.Max(cross, vertical ? m.Width : m.Height);
        }
        SizeF content = vertical ? new SizeF(cross, main) : new SizeF(main, cross);
        return new SizeF(content.Width + pad.Horizontal, content.Height + pad.Vertical);
    }

    public static void Arrange(Element e, RectF frame)
    {
        e.Frame = frame;
        if (e.Children is not { Count: > 0 }) return;

        bool vertical = e.Style.Axis == Axis.Vertical;
        RectF inner = frame.Deflate(e.Style.Padding);
        SizeF innerAvail = vertical
            ? new SizeF(inner.Width, float.PositiveInfinity)
            : new SizeF(float.PositiveInfinity, inner.Height);

        float used = 0f;
        int i = 0;
        foreach (Element child in e.Children)
        {
            if (i++ > 0) used += e.Style.Gap;
            child.Measured = Measure(child, innerAvail);
            used += vertical ? child.Measured.Height : child.Measured.Width;
        }

        float mainSize = vertical ? inner.Height : inner.Width;
        float pos = e.Style.MainAlign switch
        {
            MainAxisAlignment.Center => (vertical ? inner.Top : inner.Left) + MathF.Max(0f, (mainSize - used) / 2f),
            MainAxisAlignment.End => (vertical ? inner.Bottom : inner.Right) - used,
            _ => vertical ? inner.Top : inner.Left,
        };

        i = 0;
        foreach (Element child in e.Children)
        {
            if (i++ > 0) pos += e.Style.Gap;
            SizeF m = child.Measured;
            if (vertical)
            {
                float w = e.Style.CrossAlign == CrossAxisAlignment.Stretch ? inner.Width : m.Width;
                float x = e.Style.CrossAlign switch
                {
                    CrossAxisAlignment.Center => inner.X + (inner.Width - m.Width) / 2f,
                    CrossAxisAlignment.End => inner.Right - m.Width,
                    _ => inner.X,
                };
                Arrange(child, new RectF(x, pos, w, m.Height));
                pos += m.Height;
            }
            else
            {
                float h = e.Style.CrossAlign == CrossAxisAlignment.Stretch ? inner.Height : m.Height;
                float y = e.Style.CrossAlign switch
                {
                    CrossAxisAlignment.Center => inner.Y + (inner.Height - m.Height) / 2f,
                    CrossAxisAlignment.End => inner.Bottom - m.Height,
                    _ => inner.Y,
                };
                Arrange(child, new RectF(pos, y, m.Width, h));
                pos += m.Width;
            }
        }
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test --filter FullyQualifiedName~LayoutTests`
Expected: PASS(6 tests)。

- [ ] **Step 6: 提交**

```bash
git add -A && git commit -m "feat: element tree base and single-axis flex layout engine"
```

---

### Task 9: 文本图集(ShelfPacker + SkiaSharp 光栅化)

**Files:**
- Create: `src/SharpUI/Elements/ShelfPacker.cs`、`src/SharpUI/Elements/TextAtlas.cs`
- Modify: `src/SharpUI/SharpUI.csproj`(加 SkiaSharp)
- Test: `tests/SharpUI.Tests/ShelfPackerTests.cs`、`tests/SharpUI.Tests/TextAtlasTests.cs`

**Interfaces:**
- Consumes: `GraphicsContext.UpdateAtlasRegion`、`MeshBuilder.AddQuad`。
- Produces(Task 10 依赖):
  - `sealed class ShelfPacker`:`ctor(int width, int height)`、`TryAlloc(int width, int height) → (int X, int Y)?`、`Reset()`
  - `sealed class TextAtlas`:`ctor(GraphicsContext ctx)`、`RectF WhiteUv { get }`、`SizeF Measure(string text, float fontSize, bool bold = false)`、`DrawRun(MeshBuilder mesh, string text, PointF topLeft, float fontSize, bool bold, Color32 color)`(topLeft 为行盒左上角,内部自行加 ascent)

- [ ] **Step 1: 加包引用**

`src/SharpUI/SharpUI.csproj` 的 ItemGroup 追加:

```xml
    <PackageReference Include="SkiaSharp" Version="4.153.1" />
```

- [ ] **Step 2: 写失败测试 ShelfPackerTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class ShelfPackerTests
{
    [Fact]
    public void Allocates_SequentiallyInARow()
    {
        var p = new ShelfPacker(10, 10);
        Assert.Equal((0, 0), p.TryAlloc(4, 2));
        Assert.Equal((4, 0), p.TryAlloc(4, 2));
        Assert.Equal((8, 0), p.TryAlloc(2, 2));
    }

    [Fact]
    public void WrapsToNewRow_UsingTallestRowHeight()
    {
        var p = new ShelfPacker(10, 10);
        p.TryAlloc(4, 5);        // 行高 5
        Assert.Equal((4, 0), p.TryAlloc(4, 2));
        Assert.Equal((0, 5), p.TryAlloc(4, 2));   // 放不下 → 换行
    }

    [Fact]
    public void ReturnsNull_WhenExhausted()
    {
        var p = new ShelfPacker(4, 4);
        Assert.NotNull(p.TryAlloc(4, 2));
        Assert.NotNull(p.TryAlloc(4, 2));
        Assert.Null(p.TryAlloc(1, 1));
    }

    [Fact]
    public void TooWide_AlwaysNull()
    {
        var p = new ShelfPacker(4, 10);
        Assert.Null(p.TryAlloc(5, 1));
    }

    [Fact]
    public void Reset_AllowsReallocation()
    {
        var p = new ShelfPacker(4, 4);
        p.TryAlloc(4, 4);
        Assert.Null(p.TryAlloc(1, 1));
        p.Reset();
        Assert.Equal((0, 0), p.TryAlloc(1, 1));
    }
}
```

- [ ] **Step 3: 写失败测试 TextAtlasTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class TextAtlasTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void WhiteUv_PointsIntoFirstPixel()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        Assert.Equal(0f, atlas.WhiteUv.X, 5);
        Assert.Equal(0f, atlas.WhiteUv.Y, 5);
        Assert.True(atlas.WhiteUv.Width > 0f && atlas.WhiteUv.Height > 0f);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Measure_NonEmptyLatinText()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        SizeF m = atlas.Measure("Hello", 16f);
        Assert.True(m.Width > 10f, $"width={m.Width}");
        Assert.True(m.Height > 10f, $"height={m.Height}");
        Assert.True(atlas.Measure("Hello", 16f).Width < atlas.Measure("Hello World", 16f).Width);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void DrawRun_AddsOneQuadPerVisibleGlyph()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "Hi!", new PointF(0, 0), 16f, bold: false, Color32.White);
        Assert.Equal(3, mesh.VertexCount / 4);   // 3 个可见字形
        Assert.Equal(18, mesh.IndexCount);
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void SameGlyphTwice_DoesNotReallocate()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        var mesh = new MeshBuilder();
        atlas.DrawRun(mesh, "AA", new PointF(0, 0), 16f, bold: false, Color32.White);
        atlas.DrawRun(mesh, "AA", new PointF(0, 20), 16f, bold: false, Color32.White);
        // 重复调用不抛错、两次四边形数一致(缓存命中)
        Assert.Equal(4, mesh.VertexCount / 4);
    }
}
```

- [ ] **Step 4: 运行测试确认失败**

Run: `dotnet test --filter "FullyQualifiedName~ShelfPacker|FullyQualifiedName~TextAtlas"`
Expected: 编译 FAIL。

- [ ] **Step 5: 写实现 src/SharpUI/Elements/ShelfPacker.cs**

```csharp
namespace SharpUI;

public sealed class ShelfPacker
{
    readonly int _width, _height;
    int _cursorX, _cursorY, _rowHeight;

    public ShelfPacker(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public (int X, int Y)? TryAlloc(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > _width) return null;
        if (_cursorX + width > _width)
        {
            _cursorY += _rowHeight;
            _cursorX = 0;
            _rowHeight = 0;
        }
        if (_cursorY + height > _height) return null;
        (int X, int Y) result = (_cursorX, _cursorY);
        _cursorX += width;
        _rowHeight = Math.Max(_rowHeight, height);
        return result;
    }

    public void Reset() => (_cursorX, _cursorY, _rowHeight) = (0, 0, 0);
}
```

- [ ] **Step 6: 写实现 src/SharpUI/Elements/TextAtlas.cs**

```csharp
using SkiaSharp;

namespace SharpUI;

public sealed class TextAtlas : IDisposable
{
    readonly GraphicsContext _ctx;
    readonly SKPaint _paint = new() { Color = SKColors.White, IsAntialias = true };
    readonly SKTypeface _regular;
    readonly SKTypeface _bold;
    readonly Dictionary<int, SKFont> _fonts = new();                 // size << 1 | bold
    readonly Dictionary<(char Char, int Size, bool Bold), Glyph> _glyphs = new();
    readonly Dictionary<int, (float Ascent, float Descent)> _lineMetrics = new();
    readonly ShelfPacker _packer;
    readonly SKBitmap _bitmap;
    readonly SKCanvas _canvas;

    public RectF WhiteUv { get; private set; }

    public TextAtlas(GraphicsContext ctx)
    {
        _ctx = ctx;
        _regular = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        _bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? _regular;
        _packer = new ShelfPacker(GraphicsContext.AtlasSize, GraphicsContext.AtlasSize);
        _bitmap = new SKBitmap(new SKImageInfo(
            GraphicsContext.AtlasSize, GraphicsContext.AtlasSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        _canvas = new SKCanvas(_bitmap);
        _canvas.Clear(SKColors.Transparent);
        _canvas.Flush();
        SeedWhitePixel();
    }

    void SeedWhitePixel()
    {
        (int x, int y) = _packer.TryAlloc(1, 1)!.Value;
        _ctx.UpdateAtlasRegion(new byte[] { 255, 255, 255, 255 }, (uint)x, (uint)y, 1, 1);
        WhiteUv = new RectF(
            x / (float)GraphicsContext.AtlasSize, y / (float)GraphicsContext.AtlasSize,
            1f / GraphicsContext.AtlasSize, 1f / GraphicsContext.AtlasSize);
    }

    SKFont GetFont(int size, bool bold)
    {
        int key = size << 1 | (bold ? 1 : 0);
        if (!_fonts.TryGetValue(key, out SKFont? font))
        {
            font = new SKFont(bold ? _bold : _regular, size)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = false,
            };
            _fonts[key] = font;
        }
        return font;
    }

    (float Ascent, float Descent) GetLineMetrics(int size, bool bold)
    {
        int key = size << 1 | (bold ? 1 : 0);
        if (!_lineMetrics.TryGetValue(key, out var lm))
        {
            GetFont(size, bold).MeasureText("Ág", out SKRect bounds, _paint);
            lm = (-bounds.Top, bounds.Bottom);
            _lineMetrics[key] = lm;
        }
        return lm;
    }

    public SizeF Measure(string text, float fontSize, bool bold = false)
    {
        if (text.Length == 0) return SizeF.Zero;
        int size = SizeKey(fontSize);
        (float ascent, float descent) = GetLineMetrics(size, bold);
        return new SizeF(GetFont(size, bold).MeasureText(text, out _, _paint), ascent + descent);
    }

    public void DrawRun(MeshBuilder mesh, string text, PointF topLeft, float fontSize, bool bold, Color32 color)
    {
        int size = SizeKey(fontSize);
        (float ascent, _) = GetLineMetrics(size, bold);
        float baselineY = topLeft.Y + ascent;
        float x = topLeft.X;
        foreach (char c in text)
        {
            Glyph g = GetGlyph(c, size, bold);
            if (c != ' ')
                mesh.AddQuad(new RectF(x, baselineY - g.BearingY, g.Width, g.Height), g.Uv, color);
            x += g.Advance;
        }
    }

    static int SizeKey(float fontSize) => Math.Max(4, (int)MathF.Ceiling(fontSize));

    Glyph GetGlyph(char c, int size, bool bold)
    {
        if (_glyphs.TryGetValue((c, size, bold), out Glyph glyph)) return glyph;
        glyph = Rasterize(c, size, bold);
        _glyphs[(c, size, bold)] = glyph;
        return glyph;
    }

    Glyph Rasterize(char c, int size, bool bold)
    {
        SKFont font = GetFont(size, bold);
        string s = c.ToString();
        float advance = font.MeasureText(s, out SKRect bounds, _paint);
        int width = Math.Max(1, (int)MathF.Ceiling(bounds.Right - bounds.Left));
        int height = Math.Max(1, (int)MathF.Ceiling(bounds.Bottom - bounds.Top));

        (int x, int y)? slot = _packer.TryAlloc(width + 1, height + 1);   // +1 防渗色
        if (slot is null)
        {
            _packer.Reset();
            _glyphs.Clear();
            SeedWhitePixel();                                             // (0,0) 重播白像素
            slot = _packer.TryAlloc(width + 1, height + 1)
                ?? throw new InvalidOperationException($"字形 {c}@{size} 放不进图集");
        }

        using var glyphBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var glyphCanvas = new SKCanvas(glyphBitmap);
        glyphCanvas.Clear(SKColors.Transparent);
        glyphCanvas.DrawText(s, -bounds.Left, -bounds.Top, font, _paint);
        glyphCanvas.Flush();
        _ctx.UpdateAtlasRegion(glyphBitmap.GetPixelSpan().ToArray(), (uint)slot.Value.X, (uint)slot.Value.Y, (uint)width, (uint)height);

        return new Glyph(
            new RectF(slot.Value.X / (float)GraphicsContext.AtlasSize,
                      slot.Value.Y / (float)GraphicsContext.AtlasSize,
                      width / (float)GraphicsContext.AtlasSize,
                      height / (float)GraphicsContext.AtlasSize),
            width, height, -bounds.Top, advance);
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _bitmap.Dispose();
        foreach (SKFont f in _fonts.Values) f.Dispose();
        _paint.Dispose();
        if (!ReferenceEquals(_bold, _regular)) _bold.Dispose();
        _regular.Dispose();
    }

    readonly record struct Glyph(RectF Uv, float Width, float Height, float BearingY, float Advance);
}
```

- [ ] **Step 7: 运行测试确认通过**

Run: `dotnet test --filter "FullyQualifiedName~ShelfPacker|FullyQualifiedName~TextAtlas"`
Expected: PASS(9 tests)。

- [ ] **Step 8: 提交**

```bash
git add -A && git commit -m "feat: skia-backed text atlas with shelf packer"
```

---

### Task 10: Panel/Text 元素与 Ui DSL(声明式核心)

**Files:**
- Create: `src/SharpUI/Elements/PanelElement.cs`、`src/SharpUI/Elements/TextElement.cs`、`src/SharpUI/Elements/Ui.cs`
- Modify: `src/SharpUI/Element.cs`(PaintContext 加 Atlas、Element 加字符串隐式转换)、`src/SharpUI/App.cs`(Run 接入元素树渲染,暴露 `Atlas` 静态)、`examples/Counter/Program.cs`(静态版 demo)
- Test: `tests/SharpUI.Tests/UiDslTests.cs`

**Interfaces:**
- Consumes: Task 8 `Element/LayoutEngine/Style`、Task 9 `TextAtlas`、Task 4 `Tessellator`。
- Produces(Task 11 与 demo 依赖):
  - `PaintContext` 扩展为 `(MeshBuilder Mesh, TextAtlas Atlas)`,并新增属性 `RectF WhiteUv => Atlas.WhiteUv;`
  - `sealed class PanelElement : Element`:`Background : Color32`、`CornerRadius : float`、`Fill(Color32)`、`Fill(uint)`、`Radius(float)`(容器自绘子元素)
  - `sealed class TextElement : Element`:`Text/FontSize/IsBold/Color` 字段、`Size(float)/Bold()/Color(Color32)/Color(uint)` 链式
  - `static class Ui`:`Column(params Element?[])`、`Row(params Element?[])`、`Text(string)`、`Spacer(SizeF)`、`Panel()`
  - `static class ElementExtensions`:`Gap/Padding/Padding(all)/Center/CrossCenter` 泛型链式(`where T : Element`)
  - `Element` 新增 `public static implicit operator Element(string text)`
  - `App`:`static TextAtlas? Atlas { get; internal set; }`;`Create(title,w,h, Func<Element>? render = null)`;Run 渲染元素树

- [ ] **Step 1: 写失败测试 UiDslTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class UiDslTests
{
    [Fact]
    public void String_ImplicitlyConvertsToTextElement()
    {
        Element e = "hello";
        Assert.IsType<TextElement>(e);
        Assert.Equal("hello", ((TextElement)e).Text);
    }

    [Fact]
    public void Column_FiltersNullsAndSetsAxis()
    {
        PanelElement column = Ui.Column("a", null, Ui.Text("b"));
        Assert.Equal(Axis.Vertical, column.Style.Axis);
        Assert.Equal(2, column.Children!.Count);
        PanelElement row = Ui.Row("a", "b");
        Assert.Equal(Axis.Horizontal, row.Style.Axis);
    }

    [Fact]
    public void ModifierChain_SetsStyleAndVisualFields()
    {
        TextElement t = Ui.Text("x").Size(28).Bold().Color(0xE8E8EC);
        Assert.Equal(28f, t.FontSize);
        Assert.True(t.IsBold);
        Assert.Equal(0xE8, t.Color.R);

        PanelElement p = Ui.Panel().Fill(0x0C63E4).Radius(6f).Center().Gap(4f).Padding(24f);
        Assert.Equal(0x0C, p.Background.R);
        Assert.Equal(6f, p.CornerRadius);
        Assert.Equal(MainAxisAlignment.Center, p.Style.MainAlign);
        Assert.Equal(4f, p.Style.Gap);
        Assert.Equal(new Thickness(24f), p.Style.Padding);
    }

    [Fact]
    public void Measure_WiresTextThroughAtlas()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        TextElement t = Ui.Text("Hello").Size(20);
        SizeF m = LayoutEngine.Measure(t, new SizeF(200, 100));
        Assert.True(m.Width > 10f);
        App.Atlas = null;   // 不影响后续测试
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void PanelDraw_ProducesRoundedRectMesh()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        PanelElement panel = Ui.Panel().Fill(Color32.White).Radius(8f);
        LayoutEngine.Arrange(panel, new RectF(0, 0, 100, 50));
        var mesh = new MeshBuilder();
        panel.Draw(in new PaintContext(mesh, atlas));
        Assert.Equal(108, mesh.VertexCount);   // 与 TessellationTests 计数一致
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test --filter FullyQualifiedName~UiDslTests`
Expected: 编译 FAIL。

- [ ] **Step 3: 修改 src/SharpUI/Element.cs(PaintContext 扩展 + 字符串隐式转换)**

`PaintContext` 整体替换为:

```csharp
/// <summary>绘制上下文(Task 11 将再加 InputState)。</summary>
public readonly record struct PaintContext(MeshBuilder Mesh, TextAtlas Atlas)
{
    public RectF WhiteUv => Atlas.WhiteUv;
}
```

`Element` 类内追加(放在 `HitTest` 之后):

```csharp
    public static implicit operator Element(string text) => new TextElement(text);
```

同时把基类 `Draw` 的默认实现改为**不再**遍历子元素(容器自绘,PanelElement 接管):

```csharp
    public virtual void Draw(in PaintContext pc) { }
```

- [ ] **Step 4: 写实现 src/SharpUI/Elements/PanelElement.cs**

```csharp
namespace SharpUI;

public sealed class PanelElement : Element
{
    public Color32 Background = Color32.Transparent;
    public float CornerRadius;

    public PanelElement Fill(Color32 color) { Background = color; return this; }
    public PanelElement Fill(uint rgb) { Background = Color32.FromRgb(rgb); return this; }
    public PanelElement Radius(float radius) { CornerRadius = radius; return this; }

    public override void Draw(in PaintContext pc)
    {
        if (!Frame.IsEmpty && Background.A != 0)
            Tessellator.AddRoundedRect(pc.Mesh, Frame, CornerRadius, Background,
                new PointF(pc.WhiteUv.X + pc.WhiteUv.Width / 2f, pc.WhiteUv.Y + pc.WhiteUv.Height / 2f));
        if (Children is null) return;
        foreach (Element child in Children)
            child.Draw(in pc);
    }
}
```

- [ ] **Step 5: 写实现 src/SharpUI/Elements/TextElement.cs**

```csharp
namespace SharpUI;

public sealed class TextElement : Element
{
    public string Text = "";
    public float FontSize = 16f;
    public bool IsBold;
    public Color32 Color = Color32.White;

    public TextElement(string text) => Text = text;

    public TextElement Size(float size) { FontSize = size; return this; }
    public TextElement Bold() { IsBold = true; return this; }
    public TextElement Color(Color32 c) { Color = c; return this; }
    public TextElement Color(uint rgb) { Color = Color32.FromRgb(rgb); return this; }

    protected internal override SizeF MeasureSelf(SizeF available) =>
        App.Atlas is null ? SizeF.Zero : App.Atlas.Measure(Text, FontSize, IsBold);

    public override void Draw(in PaintContext pc)
    {
        if (Frame.IsEmpty || Text.Length == 0) return;
        pc.Atlas.DrawRun(pc.Mesh, Text, new PointF(Frame.X, Frame.Y), FontSize, IsBold, Color);
    }
}
```

- [ ] **Step 6: 写实现 src/SharpUI/Elements/Ui.cs**

```csharp
namespace SharpUI;

public static class Ui
{
    public static PanelElement Column(params Element?[] children) => Container(Axis.Vertical, children);
    public static PanelElement Row(params Element?[] children) => Container(Axis.Horizontal, children);
    public static TextElement Text(string text) => new(text);
    public static Spacer Spacer(SizeF size) => new(size);
    public static PanelElement Panel() => new();

    static PanelElement Container(Axis axis, Element?[] children)
    {
        var panel = new PanelElement();
        panel.Style.Axis = axis;
        foreach (Element? child in children)
            if (child is not null)
                panel.Add(child);
        return panel;
    }
}

public static class ElementExtensions
{
    public static T Gap<T>(this T e, float gap) where T : Element { e.Style.Gap = gap; return e; }
    public static T Padding<T>(this T e, Thickness padding) where T : Element { e.Style.Padding = padding; return e; }
    public static T Padding<T>(this T e, float all) where T : Element { e.Style.Padding = new Thickness(all); return e; }
    public static T Center<T>(this T e) where T : Element { e.Style.MainAlign = MainAxisAlignment.Center; return e; }
    public static T CrossCenter<T>(this T e) where T : Element { e.Style.CrossAlign = CrossAxisAlignment.Center; return e; }
}
```

- [ ] **Step 7: 修改 src/SharpUI/App.cs(元素树渲染循环)**

整体替换为:

```csharp
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace SharpUI;

public sealed class App
{
    /// <summary>TextElement 度量用(M1 服务定位,M2 换度量上下文)。</summary>
    public static TextAtlas? Atlas { get; internal set; }

    readonly Sdl2Window _window;
    readonly GraphicsContext _ctx;
    readonly Renderer _renderer;
    readonly Func<Element>? _render;

    App(Sdl2Window window, GraphicsDevice device, Func<Element>? render)
    {
        _window = window;
        _ctx = GraphicsContext.CreateForWindow(window, device);
        _renderer = new Renderer(_ctx);
        _render = render;
    }

    public static App Create(string title, int width, int height, Func<Element>? render = null)
    {
        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo { WindowTitle = title, WindowWidth = width, WindowHeight = height },
            new GraphicsDeviceOptions { Debug = false, PreferStandardClipSpaceYDirection = true },
            VeldridStartup.GetPlatformDefaultBackend(),
            out Sdl2Window window,
            out GraphicsDevice device);
        window.Resized += () => device.ResizeMainWindow((uint)window.Width, (uint)window.Height);
        return new App(window, device, render);
    }

    public int Run(int maxFrames = 0)
    {
        Atlas = new TextAtlas(_ctx);
        var mesh = new MeshBuilder();
        int frames = 0;
        while (_window.Exists)
        {
            _window.PumpEvents();
            Element root = _render is null ? new Spacer(SizeF.Zero) : _render();
            SizeF screen = new(_window.Width, _window.Height);
            LayoutEngine.Measure(root, screen);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            mesh.Clear();
            root.Draw(in new PaintContext(mesh, Atlas));
            _renderer.Submit(mesh, screen, _ctx.Device.MainSwapchain.Framebuffer);
            _ctx.Device.SwapBuffers();
            if (maxFrames > 0 && ++frames >= maxFrames)
                _window.Close();
        }
        return 0;
    }
}
```

- [ ] **Step 8: 更新 examples/Counter/Program.cs(静态版,按钮 Task 11 再来)**

```csharp
using SharpUI;
using static SharpUI.Ui;

internal static class Program
{
    private static int Main() => App.Create("SharpUI Counter", 480, 320, () => Column(
            Text("Counter").Size(28).Bold().Color(0xE8E8EC),
            Text("M1").Size(16).Color(0x9AA2B3)
        ).Center().Gap(8f).Padding(24f)).Run();
}
```

- [ ] **Step 9: 运行全部测试确认通过**

Run: `dotnet test`
Expected: 全部 PASS(含此前所有;`LayoutTests` 等不回归)。

- [ ] **Step 10: 手测**

Run: `dotnet run -c Debug --project examples/Counter`
Expected: 窗口中垂直居中显示白色粗体 "Counter" 与灰色 "M1",抗锯齿正常、无花屏;调整窗口大小时布局跟随。

- [ ] **Step 11: 提交**

```bash
git add -A && git commit -m "feat: declarative panel/text elements with ui dsl and app render loop"
```

---

### Task 11: 交互(命中/悬停/点击)与 Button,Counter 完整版

**Files:**
- Create: `src/SharpUI/Interaction.cs`、`src/SharpUI/Elements/ButtonElement.cs`
- Modify: `src/SharpUI/Element.cs`(PaintContext 加 Input)、`src/SharpUI/App.cs`(Run 接交互)、`examples/Counter/Program.cs`(完整 counter)
- Test: `tests/SharpUI.Tests/InteractionTests.cs`

**Interfaces:**
- Consumes: `Element.HitTest/Frame/IsHitTarget`、`PanelElement`。
- Produces:
  - `readonly record struct RawInput(PointF Mouse, bool[] LeftEvents)`(App 从 InputSnapshot 映射;测试可构造)
  - `readonly record struct InputState(PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased, Element? Hovered)`,静态 `Empty`
  - `static class Interaction`:`Update(Element root, in RawInput raw) → InputState`(静态维护 `_leftHeld` 与按下目标 `_pressedOn`;**点击 = 按下与松开命中同一 Button**)
  - `PaintContext` 最终形态 `(MeshBuilder Mesh, TextAtlas Atlas, InputState Input)`
  - `sealed class ButtonElement : PanelElement`:`ctor(string label, Action onClick)`、`InvokeClick()`

- [ ] **Step 1: 写失败测试 InteractionTests.cs**

```csharp
using SharpUI;

namespace SharpUI.Tests;

public class InteractionTests
{
    static (Element Root, ButtonElement Button) ButtonAt(RectF frame)
    {
        var button = new ButtonElement("OK", () => { });
        LayoutEngine.Arrange(button, frame);
        return (button, button);
    }

    [Fact]
    public void Hover_DetectsHitTarget()
    {
        (Element root, _) = ButtonAt(new RectF(0, 0, 100, 40));
        InputState s = Interaction.Update(root, new RawInput(new PointF(50, 20), Array.Empty<bool>()));
        Assert.NotNull(s.Hovered);
        Assert.False(s.LeftDown);
        Assert.False(s.LeftPressed);
        Assert.False(s.LeftReleased);
    }

    [Fact]
    public void PressThenRelease_FiresClick()
    {
        var clicks = 0;
        var button = new ButtonElement("OK", () => clicks++);
        LayoutEngine.Arrange(button, new RectF(0, 0, 100, 40));

        Interaction.Update(button, new RawInput(new PointF(50, 20), new[] { true }));
        InputState released = Interaction.Update(button, new RawInput(new PointF(50, 20), new[] { false }));

        Assert.Equal(1, clicks);
        Assert.True(released.LeftReleased);
        Assert.False(released.LeftDown);
    }

    [Fact]
    public void Miss_DoesNotHit()
    {
        (Element root, _) = ButtonAt(new RectF(0, 0, 100, 40));
        InputState s = Interaction.Update(root, new RawInput(new PointF(150, 20), Array.Empty<bool>()));
        Assert.Null(s.Hovered);
    }

    [Fact]
    public void ClickOutsideHoverInside_ReleaseFiresNothing()
    {
        var clicks = 0;
        var button = new ButtonElement("OK", () => clicks++);
        LayoutEngine.Arrange(button, new RectF(0, 0, 100, 40));
        Interaction.Update(button, new RawInput(new PointF(500, 500), new[] { true }));    // 外面按下
        Interaction.Update(button, new RawInput(new PointF(50, 20), new[] { false }));    // 移进来松开
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void DeepestChildWins_AndNonTargetIgnored()
    {
        PanelElement panel = Ui.Panel().Fill(0x202020);
        panel.Style.Axis = Axis.Vertical;
        var inner = new ButtonElement("x", () => { });
        panel.Add(inner);
        LayoutEngine.Arrange(panel, new RectF(0, 0, 200, 100));
        InputState s = Interaction.Update(panel, new RawInput(
            new PointF(inner.Frame.X + 1, inner.Frame.Y + 1), Array.Empty<bool>()));
        Assert.Same(inner, s.Hovered);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test --filter FullyQualifiedName~InteractionTests`
Expected: 编译 FAIL。

- [ ] **Step 3: 写实现 src/SharpUI/Interaction.cs**

```csharp
namespace SharpUI;

/// <summary>App 从 SDL InputSnapshot 映射;LeftEvents 按时间序记录左键按下(true)/抬起(false)。</summary>
public readonly record struct RawInput(PointF Mouse, bool[] LeftEvents);

public readonly record struct InputState(PointF Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased, Element? Hovered)
{
    public static readonly InputState Empty = new(new PointF(-1f, -1f), false, false, false, null);
}

public static class Interaction
{
    static bool _leftHeld;
    static Element? _pressedOn;   // 按下瞬间的命中目标;点击 = 按下与松开命中同一目标

    public static InputState Update(Element root, in RawInput raw)
    {
        bool pressed = false, released = false;
        Element? hovered = root.HitTest(raw.Mouse);
        foreach (bool down in raw.LeftEvents)
        {
            if (down)
            {
                _leftHeld = true;
                pressed = true;
                _pressedOn = hovered;
            }
            else
            {
                _leftHeld = false;
                released = true;
                if (_pressedOn is not null && ReferenceEquals(_pressedOn, hovered)
                    && hovered is ButtonElement button)
                {
                    button.InvokeClick();
                }
                _pressedOn = null;
            }
        }
        return new InputState(raw.Mouse, _leftHeld, pressed, released, hovered);
    }
}
```

- [ ] **Step 4: 写实现 src/SharpUI/Elements/ButtonElement.cs**

```csharp
namespace SharpUI;

public sealed class ButtonElement : PanelElement
{
    readonly Action _onClick;

    public ButtonElement(string label, Action onClick)
    {
        _onClick = onClick;
        Add(new TextElement(label).Size(16));
        Style.MainAlign = MainAxisAlignment.Center;
        Style.CrossAlign = CrossAxisAlignment.Center;
        Style.Padding = new Thickness(horizontal: 16f, vertical: 8f);
        CornerRadius = 6f;
        IsHitTarget = true;
    }

    public void InvokeClick() => _onClick();

    public override void Draw(in PaintContext pc)
    {
        Background = pc.Input.Hovered == this
            ? (pc.Input.LeftDown ? Color32.FromRgb(0x1D4ED8) : Color32.FromRgb(0x3B82F6))
            : Color32.FromRgb(0x0C63E4);
        base.Draw(pc);
    }
}
```

- [ ] **Step 5: 修改 src/SharpUI/Element.cs(PaintContext 最终形态)**

`PaintContext` 整体替换为:

```csharp
/// <summary>绘制上下文(M1 最终形态)。</summary>
public readonly record struct PaintContext(MeshBuilder Mesh, TextAtlas Atlas, InputState Input)
{
    public RectF WhiteUv => Atlas.WhiteUv;
}
```

- [ ] **Step 6: 修改 src/SharpUI/App.cs(Run 接交互)**

在文件顶部追加 `using Veldrid;`(若已有则跳过),并把 `Run` 中

```csharp
            _window.PumpEvents();
            Element root = _render is null ? new Spacer(SizeF.Zero) : _render();
            SizeF screen = new(_window.Width, _window.Height);
            LayoutEngine.Measure(root, screen);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            mesh.Clear();
            root.Draw(in new PaintContext(mesh, Atlas));
```

替换为:

```csharp
            InputSnapshot snapshot = _window.PumpEvents();
            Element root = _render is null ? new Spacer(SizeF.Zero) : _render();
            SizeF screen = new(_window.Width, _window.Height);
            LayoutEngine.Measure(root, screen);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            RawInput raw = new(
                new PointF(snapshot.MousePosition.X, snapshot.MousePosition.Y),
                LeftEvents(snapshot));
            InputState input = Interaction.Update(root, in raw);
            mesh.Clear();
            root.Draw(in new PaintContext(mesh, Atlas, input));
```

并在 `App` 类内追加静态辅助:

```csharp
    static bool[] LeftEvents(InputSnapshot snapshot)
    {
        int n = 0;
        foreach (MouseEvent me in snapshot.MouseEvents)
            if (me.MouseButton == MouseButton.Left) n++;
        if (n == 0) return Array.Empty<bool>();
        var events = new bool[n];
        int i = 0;
        foreach (MouseEvent me in snapshot.MouseEvents)
            if (me.MouseButton == MouseButton.Left)
                events[i++] = me.Down;
        return events;
    }
```

- [ ] **Step 7: 更新 examples/Counter/Program.cs(完整版)**

```csharp
using SharpUI;
using static SharpUI.Ui;

internal sealed class CounterState
{
    public int Count;
}

internal static class Program
{
    private static int Main()
    {
        var state = new CounterState();
        return App.Create("SharpUI Counter", 480, 320, () => Column(
                Text("Counter").Size(28).Bold().Color(0xE8E8EC),
                Text(state.Count.ToString()).Size(48).Color(0x7AA2F7),
                Row(
                    new ButtonElement("−", () => state.Count--),
                    Ui.Spacer(new SizeF(8f, 0f)),
                    new ButtonElement("+", () => state.Count++)
                ).Gap(0f)
            ).Center().Gap(12f).Padding(24f)).Run();
    }
}
```

- [ ] **Step 8: 运行全部测试确认通过**

Run: `dotnet test`
Expected: 全部 PASS(Task 10 的 `UiDslTests.PanelDraw_...` 若因 PaintContext 变形而编译失败,把其中 `new PaintContext(mesh, atlas)` 改为 `new PaintContext(mesh, atlas, InputState.Empty)`——本步骤直接包含此修改)。

- [ ] **Step 9: 手测(M1 核心验收)**

Run: `dotnet run -c Debug --project examples/Counter`
Expected: 居中显示标题、蓝色大数字、两个圆角按钮;点击 +/− 数字即时增减;悬停按钮变亮蓝、按住变深蓝;窗口拉伸布局跟随;60fps 无闪烁。

- [ ] **Step 10: 提交**

```bash
git add -A && git commit -m "feat: hit-testing interaction and button element, counter demo complete"
```

---

### Task 12: 性能基线测试

**Files:**
- Create: `tests/SharpUI.Tests/PerfTests.cs`、`docs/notes/perf-baselines.md`
- Test: PerfTests.cs 本身

**Interfaces:**
- Consumes: 全部前序公开面(`App.Atlas`、`Ui.*`、`LayoutEngine`、`PaintContext`)。
- Produces: 性能回归门禁(规格书 §9 预算):Counter 场景 ≤64KB/帧、<8ms CPU/帧;实测数字记入 `docs/notes/perf-baselines.md`。

- [ ] **Step 1: 写测试 PerfTests.cs(先跑出真实数字,预算断言即门禁)**

```csharp
using System.Diagnostics;
using SharpUI;
using static SharpUI.Ui;

namespace SharpUI.Tests;

public class PerfTests
{
    [Fact]
    [Trait("Category", "Gpu")]
    public void CounterScene_WithinAllocationAndCpuBudget()
    {
        using var ctx = GraphicsContext.CreateHeadless(64, 64);
        using var atlas = new TextAtlas(ctx);
        App.Atlas = atlas;
        var mesh = new MeshBuilder();
        int state = 0;
        var screen = new SizeF(480, 320);

        Element Build() => Column(
            Text("Counter").Size(28).Bold(),
            Text(state.ToString()).Size(48),
            Row(
                new ButtonElement("−", () => state--),
                new ButtonElement("+", () => state++)
            ).Padding(0f)
        ).Center().Gap(12f).Padding(24f);

        void Frame()
        {
            Element root = Build();
            LayoutEngine.Measure(root, screen);
            LayoutEngine.Arrange(root, new RectF(0, 0, screen.Width, screen.Height));
            mesh.Clear();
            root.Draw(in new PaintContext(mesh, atlas, InputState.Empty));
        }

        for (int i = 0; i < 50; i++) Frame();   // 预热:字体缓存/委托/JIT

        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int Frames = 300;
        for (int i = 0; i < Frames; i++) Frame();
        sw.Stop();

        double bytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / (double)Frames;
        double msPerFrame = sw.Elapsed.TotalMilliseconds / Frames;
        Assert.True(bytesPerFrame <= 64 * 1024, $"每帧分配 {bytesPerFrame:F0}B 超预算 65536B");
        Assert.True(msPerFrame < 8.0, $"每帧 CPU {msPerFrame:F2}ms 超预算 8ms");

        // testhost 工作目录在 tests/SharpUI.Tests/bin/{cfg}/net10.0,上溯 5 级到仓库根
        string baselinePath = Path.GetFullPath("../../../../../docs/notes/perf-baselines.md");
        Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
        File.WriteAllText(baselinePath,
            $"# M1 性能基线(自动生成)\n\n- 每帧分配:{bytesPerFrame:F0} B(预算 65536)\n- 每帧 CPU:{msPerFrame:F2} ms(预算 8)\n- 场景:Counter,480×320,Release 测得\n");
        App.Atlas = null;
    }
}
```

- [ ] **Step 2: 运行测试(Release 才是有效基线)**

Run: `dotnet test -c Release --filter FullyQualifiedName~PerfTests`
Expected: PASS,且控制台/`docs/notes/perf-baselines.md` 记录实测值。若断言失败,先看数字:分配超标 → 检查是否每帧 `ToString()`/闭包异常放大;CPU 超标 → 检查是否 Debug 语义泄漏(必须 -c Release)。

- [ ] **Step 3: 运行全量测试**

Run: `dotnet test`
Expected: 全部 PASS。

- [ ] **Step 4: 提交**

```bash
git add -A && git commit -m "test: per-frame allocation and cpu budget gate with recorded baseline"
```

---

### Task 13: Native AOT 发布验证

**Files:**
- Create: `docs/notes/m1-aot.md`
- Test: 手测 + 构建产物存在性脚本

**Interfaces:**
- Consumes: 完整 SharpUI。
- Produces: M1 验收第 3 条——win-x64 AOT 单文件发布可运行;产物大小记录在案。

- [ ] **Step 1: 发布**

```bash
dotnet publish examples/Counter -c Release -r win-x64 -p:PublishAot=true -o artifacts/aot-win-x64
```

Expected: 成功。常见故障:若报 `link.exe` / C++ 工具链缺失 → 安装 "Visual Studio Build Tools(C++ 桌面开发)" 后重试;若报 NU1903 → 确认 Newtonsoft.Json 13.0.3 直接引用存在。

- [ ] **Step 2: 记录产物**

```bash
ls -la artifacts/aot-win-x64 | tee docs/notes/m1-aot.md
```

Expected: `Counter.exe` 存在,总目录体积(含 exe 内嵌原生库)<40MB(规格书 M1 验收 3)。

- [ ] **Step 3: 手测 AOT 产物**

双击或 `./artifacts/aot-win-x64/Counter.exe`
Expected: 冷启动 <1s 出窗口,功能与调试版一致(计数/悬停/按压/resize)。

- [ ] **Step 4: 全量回归 + 提交**

```bash
dotnet test && git add -A && git commit -m "chore: verify native aot win-x64 publish and record artifact size"
```

---

## 任务依赖图

```
Task1 → Task2 → Task3 → Task4 ─┐
          Task2 ────────────────┤
Task5 → Task6 ─────────────────┤→ Task7 → Task8 → Task9 → Task10 → Task11 → Task12 → Task13
(Task5 依赖 Task3 的 UiVertex.SizeInBytes;Task8 依赖 Task2)
```

串行执行即可;唯一可并行的是 Task 8(纯逻辑)与 Task 5/6(GPU),但串行更稳。

## M1 完成定义(对照规格书 §7)

- [ ] Counter demo 手测通过(Task 11 Step 9)
- [ ] `dotnet test` 与 `dotnet test -c Release` 全绿
- [ ] 性能基线记录在 `docs/notes/perf-baselines.md` 且门禁绿
- [ ] AOT 产物可运行,大小记录在 `docs/notes/m1-aot.md`
- [ ] 全程零 `unsafe`、零 AOT 分析器警告
