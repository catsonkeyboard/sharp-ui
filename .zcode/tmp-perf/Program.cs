// Scratch benchmark (not committed): measures the real D3D11 upload path
// (Renderer.Submit -> IRenderContext.Draw, multiple clip runs = segmented Draw)
// and the GL headless submit path for comparison. Run before/after the
// DYNAMIC+Map change to quantify the upload cost delta.
using System.Diagnostics;
using SharpUI;
using SharpUI.Backends;

const int Frames = 2000;

// Mesh: sizable UI-ish scene (text quads), split into N clip runs to exercise
// the segmented Draw path (each run = one Draw call with startIndex offset).
MeshBuilder BuildMesh(int runs)
{
    var mesh = new MeshBuilder();
    float y = 0;
    for (int r = 0; r < runs; r++)
    {
        mesh.SetClip(new RectF(0, y, 480, 320f / runs));
        for (int i = 0; i < 200; i++)
        {
            float x = (i % 40) * 12f, yy = y + (i / 40) * 12f;
            mesh.AddQuad(new RectF(x, yy, 10, 10), new RectF(0, 0, 1, 1), Color32.White);
        }
        y += 320f / runs;
    }
    return mesh;
}

void Bench(string name, IRenderContext ctx, MeshBuilder mesh)
{
    var renderer = new Renderer(ctx);
    var logical = new SizeF(480, 320);
    for (int i = 0; i < 100; i++) renderer.Submit(mesh, logical);   // warmup
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < Frames; i++) renderer.Submit(mesh, logical);
    sw.Stop();
    double usPerFrame = sw.Elapsed.TotalMilliseconds * 1000 / Frames;
    int draws = mesh.ClipRuns.Length == 0 ? 1 : mesh.ClipRuns.Length;
    Console.WriteLine($"{name}: {usPerFrame:F1} us/frame over {Frames} frames " +
                      $"({mesh.VertexCount} verts, {mesh.IndexCount} idx, {draws} draws/frame)");
}

Console.WriteLine($"== D3D11 upload bench ({(Environment.Is64BitProcess ? "x64" : "x86")}, Release) ==");
foreach (int runs in new[] { 1, 8, 32 })
{
    using var ctx = D3D11Context.CreateHeadless(480, 320);
    Bench($"D3D11 runs={runs,2}", ctx, BuildMesh(runs));
}

try
{
    Console.WriteLine("== GL headless bench ==");
    using var gl = GLContext.CreateHeadless(480, 320);
    foreach (int runs in new[] { 1, 8, 32 })
        Bench($"GL     runs={runs,2}", gl, BuildMesh(runs));
}
catch (Exception ex)
{
    Console.WriteLine($"GL bench unavailable: {ex.Message}");
}
