namespace SharpUI;

/// <summary>渲染后端窄接口 v2(M3d-ii,D12 清偿:不再暴露旧窗口栈类型)。Renderer 经此驱动任意后端;
/// 剪裁区段→scissor 的换算留在 Renderer(平台共享逻辑),后端只做命令落地。</summary>
public interface IRenderContext : IDisposable
{
    /// <summary>图集边长(像素,方形)。后端契约:构造期显式上传全零初始内容
    /// (字形 padding 依赖透明零;D3D11/GL 的"初始内容未定义"语义不得依赖驱动隐式清零)。</summary>
    uint AtlasSize { get; }

    /// <summary>当前主目标像素尺寸(scissor 全开与清屏基准;窗口路径随 Resize 更新)。</summary>
    uint TargetWidth { get; }

    /// <summary>当前主目标像素尺寸(scissor 全开与清屏基准;窗口路径随 Resize 更新)。</summary>
    uint TargetHeight { get; }

    /// <summary>窗口交换链 resize;headless 语义由后端取舍:D3D11 抛 NotSupportedException,GL 仅记录尺寸。</summary>
    void Resize(uint width, uint height);

    /// <summary>图集区域上传(RGBA 字节,左上原点)。</summary>
    void UpdateAtlasRegion(byte[] rgba, uint x, uint y, uint width, uint height);

    /// <summary>帧起始:绑主目标 + 设视口 + 清屏(r,g,b,a 线性)。</summary>
    void BeginFrame(float r, float g, float b, float a);

    /// <summary>顶点着色器屏幕尺寸常量(逻辑像素)。</summary>
    void SetScreenSize(float logicalWidth, float logicalHeight);

    /// <summary>剪裁矩形(目标像素坐标;全开=目标尺寸)。</summary>
    void SetScissor(uint x, uint y, uint w, uint h);

    /// <summary>绘制一段索引几何(M3e 修订:上传语义两可,由后端按其缓冲更新 API 取舍并注释)——
    /// D3D11:UpdateSubresource(box=NULL) 整块 ByteWidth 读取语义,须将传入分段拷入自有常驻
    /// scratch(字节容量恒等于 GPU ByteWidth,直接钉调用者数组会在缓冲容量(×2 增长)大于当帧
    /// 数组时越界读,M3d-ii 实证),分段重定基到缓冲头,DrawIndexed 偏移恒 0;
    /// GL:glBufferSubData 显式长度语义,直接钉调用者数组按当帧实际长度上传(无越界面),
    /// 分段经 DrawElements 原生索引字节偏移(startIndex×2)绘制,要素缓冲整段上传 [0..总需求)。
    /// 两路取舍依据与均摊成本分析见各自实现注释;内存布局与 UiVertex 20 字节一致。</summary>
    void Draw(UiVertex[] vertices, int vertexCount, ushort[] indices, uint indexCount, uint startIndex);

    /// <summary>呈现(窗口路径 Present(1,0) VSync;headless no-op)。</summary>
    void Present();
}
