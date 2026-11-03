namespace SharpUI;

/// <summary>应用窗口(T6 多窗):每窗独占的窗口/设备上下文/渲染器/图集/焦点管理/输入会话与上帧保留树。主窗恒为 App 窗口表 [0]。</summary>
public sealed class AppWindow
{
    internal SdlWindow Window = null!;
    internal IRenderContext Ctx = null!;   // M3e 接口化(spec 背记债清偿):消费面 UpdateAtlasRegion/Present/Resize/Dispose 皆在接口
    internal Renderer Renderer = null!;
    internal TextAtlas Atlas = null!;
    internal FocusManager Focus = new();
    internal InputSession Session = new();
    internal Element? LastTree;

    /// <summary>该窗文本输入状态机(IME 热修):Run 每帧 Arrange 后驱动,聚焦 TextField 时激活 SDL 文本输入。</summary>
    internal readonly TextInputController TextInput = new(SdlTextInput.Start, SdlTextInput.Stop, SdlTextInput.SetRect);

    /// <summary>该窗 DPI 缩放比(0.5–4 夹紧,创建时定格);鼠标映射与屏幕换算基准(D18)。</summary>
    internal float Scale = 1f;

    /// <summary>构建闭包;App.Create/OpenWindow 的 null render 以空 Spacer 兜底(单窗语义保持)。</summary>
    public Func<Element> Render = static () => new Spacer(SizeF.Zero);

    /// <summary>窗口关闭可否决(审计修复波 2B/A3,透传 SdlWindow.Closing —— Window 为 internal 字段,
    /// 本事件为应用侧唯一关闭否决入口):窗口 CLOSE 事件/SDL_QUIT 分发时逐订阅者调用,任一返回 false
    /// 即否决该窗本次关闭(窗口存活);无订阅者/全部 true → 放行。编辑器"未保存更改"场景钩子。</summary>
    public event Func<bool> Closing
    {
        add => Window.Closing += value;
        remove => Window.Closing -= value;
    }

    // ---- 窗口生命周期事件开放(波 3A/B4):SdlWindow 公共事件经本类透传(应用侧唯一入口,同 Closing)。

    /// <summary>窗口尺寸变化(SDL_WINDOWEVENT RESIZED/SIZE_CHANGED 且尺寸&gt;0;回调在泵调用线程同步执行)。</summary>
    public event Action? Resized
    {
        add => Window.Resized += value;
        remove => Window.Resized -= value;
    }

    /// <summary>输入焦点丢失(D15 失焦取消按压的触发源在本类之上由 App 接线;本事件供应用层感知)。</summary>
    public event Action? FocusLost
    {
        add => Window.FocusLost += value;
        remove => Window.FocusLost -= value;
    }

    /// <summary>输入焦点获得(SDL_WINDOWEVENT FOCUS_GAINED)。</summary>
    public event Action? FocusGained
    {
        add => Window.FocusGained += value;
        remove => Window.FocusGained -= value;
    }

    /// <summary>窗口移动(data1/data2 = 新客户区左上坐标;跨显示器 DPI 重算消费留 M3e)。</summary>
    public event Action<int, int>? Moved
    {
        add => Window.Moved += value;
        remove => Window.Moved -= value;
    }

    // ---- 文件/文本拖放(波 3A/B5):载荷为 UTF-8 解码串(SDL 侧缓冲已由泵 SDL_free)。

    /// <summary>文件拖放(SDL_DROPFILE;windowID=0 的全局拖放由泵广播至主窗)。</summary>
    public event Action<string>? FileDropped
    {
        add => Window.FileDropped += value;
        remove => Window.FileDropped -= value;
    }

    /// <summary>文本拖放(SDL_DROPTEXT)。</summary>
    public event Action<string>? TextDropped
    {
        add => Window.TextDropped += value;
        remove => Window.TextDropped -= value;
    }

    /// <summary>拖放开始(SDL_DROPBEGIN,无载荷)。</summary>
    public event Action? DropBegan
    {
        add => Window.DropBegan += value;
        remove => Window.DropBegan -= value;
    }

    /// <summary>拖放结束(SDL_DROPCOMPLETE,无载荷)。</summary>
    public event Action? DropCompleted
    {
        add => Window.DropCompleted += value;
        remove => Window.DropCompleted -= value;
    }

    // ---- 窗口管理 API 开放(波 3B/B3/B4):SdlWindow 公共方法透传(Window 为 internal 字段,
    // 本类是应用侧唯一入口,同事件面)。已 Dispose 的窗由 SdlWindow 侧静默返回(句柄 Zero 门)。
    // 任务稿 "Focus()" 与既有 internal 字段 AppWindow.Focus(FocusManager,多窗焦点管理)同名冲突
    // (CS0102 成员重名),按实码取 SdlWindow 同名方法 FocusInput 透传(报告写明)。

    /// <summary>设置窗口标题(SDL_SetWindowTitle,UTF-8 封送)。</summary>
    public void SetTitle(string title) => Window.SetTitle(title);

    /// <summary>设置客户区左上坐标(SDL_SetWindowPosition;SDL_WINDOWPOS_* 值可透传)。</summary>
    public void SetPosition(int x, int y) => Window.SetPosition(x, y);

    /// <summary>设置客户区尺寸(SDL_SetWindowSize;平台最小窗宽钳制与 resize 回读语义同 Width/Height)。</summary>
    public void SetSize(int width, int height) => Window.SetSize(width, height);

    /// <summary>最小化(SDL_MinimizeWindow)。</summary>
    public void Minimize() => Window.Minimize();

    /// <summary>最大化(SDL_MaximizeWindow)。</summary>
    public void Maximize() => Window.Maximize();

    /// <summary>还原常规尺寸/位置(SDL_RestoreWindow)。</summary>
    public void Restore() => Window.Restore();

    /// <summary>切换全屏(SDL_SetWindowFullscreen):desktop 模式(合成的桌面分辨率全屏),返回 SDL
    /// 语义成败(部分 WM 拒绝,调用方裁量;失败窗口状态不变)。</summary>
    public bool SetFullscreen(bool enable) => Window.SetFullscreen(enable);

    /// <summary>请求输入焦点(SDL_SetWindowInputFocus;返回 SDL 语义成败 —— 部分 WM 拒绝跨窗抢焦点)。</summary>
    public bool FocusInput() => Window.FocusInput();

    /// <summary>置顶并激活(SDL_RaiseWindow)。</summary>
    public void Raise() => Window.Raise();
}
