namespace SharpUI;

/// <summary>渲染后端窄接口 v2(M3d-ii,D12 清偿:不再暴露 Veldrid 类型)。Renderer 经此驱动任意后端;
/// 剪裁区段→scissor 的换算留在 Renderer(平台共享逻辑),后端只做命令落地。</summary>
public interface IRenderContext : IDisposable
{
    /// <summary>当前主目标像素尺寸(scissor 全开与清屏基准;窗口路径随 Resize 更新)。</summary>
    uint TargetWidth { get; }

    /// <summary>当前主目标像素尺寸(scissor 全开与清屏基准;窗口路径随 Resize 更新)。</summary>
    uint TargetHeight { get; }

    /// <summary>窗口交换链 resize;headless 抛 NotSupportedException。</summary>
    void Resize(uint width, uint height);

    /// <summary>图集区域上传(RGBA 字节,左上原点)。</summary>
    void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint width, uint height);

    /// <summary>帧起始:绑主目标 + 设视口 + 清屏(r,g,b,a 线性)。</summary>
    void BeginFrame(float r, float g, float b, float a);

    /// <summary>顶点着色器屏幕尺寸常量(逻辑像素)。</summary>
    void SetScreenSize(float logicalWidth, float logicalHeight);

    /// <summary>剪裁矩形(目标像素坐标;全开=目标尺寸)。</summary>
    void SetScissor(uint x, uint y, uint w, uint h);

    /// <summary>绘制一段索引几何(拷贝语义:实现须将传入分段拷入自有常驻 scratch——其字节容量恒等于 GPU 缓冲 ByteWidth——后整块上传;禁止直接钉调用者数组,整块上传语义下越界读,M3d-ii 实证;内存布局与 UiVertex 20 字节一致)。</summary>
    void Draw(UiVertex[] vertices, int vertexCount, ushort[] indices, uint indexCount, uint startIndex);

    /// <summary>呈现(窗口路径 Present(1,0) VSync;headless no-op)。</summary>
    void Present();
}
