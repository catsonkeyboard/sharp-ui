using System.Runtime.InteropServices;
using System.Text;
using SharpUI;

namespace SharpUI.Tests;

/// <summary>T2:SdlWindow + SdlPump 全局路由。真窗口测试带 Gpu trait(实触 vendored SDL2.dll,创建隐藏窗
/// 免焦点扰动);事件注入走 SdlWindow.InjectTest —— 与 SdlPump.PumpAll 同一分发入口(Dispatch),注入测试
/// 不泵原生队列(免受全量序下前序测试遗留事件干扰,确定性)。事件合成偏移与 SdlEvents.SdlEvent 显式布局
/// 一致(text@12 起 32B UTF-8)。测试主机禁并行见 AssemblyInfo.cs(SDL 全局态不容忍并行)。</summary>
public class SdlWindowTests
{
    // ---- 事件合成(偏移出处见 SdlEvents.SdlEvent 字段注)----

    static SdlEvents.SdlEvent KeyEv(uint win, bool down, uint scancode, ushort mod = 0) => new()
    {
        Type = down ? SdlEvents.EventTypeKeyDown : SdlEvents.EventTypeKeyUp,
        WindowId = win,
        KeyState = SdlEvents.Pressed,
        KeysymScancode = scancode,
        KeysymSym = scancode,
        KeysymMod = mod,
    };

    static SdlEvents.SdlEvent TextEv(uint win, string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        Assert.True(utf8.Length < 32, "SDL_TextInputEvent.text[32] 容量上限");
        SdlEvents.SdlEvent e = new() { Type = SdlEvents.EventTypeTextInput, WindowId = win };
        Span<byte> raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref e, 1));
        raw.Slice(12, 32).Fill(0);          // 0 终止 + 余量清零
        utf8.CopyTo(raw.Slice(12, 32));     // text@12(char[32] UTF-8)
        return e;
    }

    static SdlEvents.SdlEvent MotionEv(uint win, int x, int y) => new()
    {
        Type = SdlEvents.EventTypeMouseMotion, WindowId = win, MouseX = x, MouseY = y,
    };

    static SdlEvents.SdlEvent ButtonEv(uint win, byte id, bool down) => new()
    {
        Type = down ? SdlEvents.EventTypeMouseButtonDown : SdlEvents.EventTypeMouseButtonUp,
        WindowId = win,
        MouseButtonId = id,
        MouseButtonState = down ? SdlEvents.Pressed : (byte)0,
    };

    static SdlEvents.SdlEvent WheelEv(uint win, int y, uint direction = 0) => new()
    {
        Type = SdlEvents.EventTypeMouseWheel, WindowId = win, WheelY = y, WheelDirection = direction,
    };

    static SdlEvents.SdlEvent WindowEv(uint win, byte code, int d1 = 0, int d2 = 0) => new()
    {
        Type = SdlEvents.EventTypeWindow, WindowId = win, WindowEventCode = code, WindowData1 = d1, WindowData2 = d2,
    };

    // ---- 真窗口生命周期(Gpu:SDL_CreateWindow/GetWindowSize/GetWindowDisplayIndex/GetDisplayDPI/DestroyWindow)----

    [Fact]
    [Trait("Category", "Gpu")]
    public void Window_CreateReadback_And_DisposeIdempotent()
    {
        // 200x150(简报原稿 64x64 实测被 Windows 最小窗宽钳制回读 120 —— SM_CXMIN 系标题栏按钮最小宽,
        // 隐藏窗不免;取安全余量尺寸做精确回读断言,钳制现象记入 T2 报告)
        var w = new SdlWindow("sharpui-t2-create", 60, 60, 200, 150, SdlWindowFlags.Hidden);
        try
        {
            Assert.NotEqual(0u, w.WindowId);
            Assert.True(w.Exists);
            Assert.Equal(200, w.Width);            // SDL_GetWindowSize 现取回读
            Assert.Equal(150, w.Height);
            Assert.InRange(w.ScaleFactor, 0.5f, 4f);   // DPI/96 创建定格,夹紧域内(本机实测 ~1.588)
        }
        finally
        {
            w.Dispose();
            w.Dispose();   // 二次幂等:不抛
        }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void PumpAll_EmptyQueue_EmptySnapshot_And_ResetSemantics()
    {
        var w = new SdlWindow("sharpui-t2-drain", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            SdlPump.PumpAll();   // 排空遗留(未注册 WindowId 安全丢弃,含前序测试窗口残留)
            InputSnapshot s = w.GetSnapshotAndReset();
            Assert.Empty(s.MouseEvents);
            Assert.Empty(s.KeyEvents);
            Assert.Empty(s.KeyCharPresses);
            Assert.Equal(0f, s.WheelDelta);
            // 取后清空:无新事件时再取为空快照
            Assert.Empty(w.GetSnapshotAndReset().KeyEvents);
        }
        finally { w.Dispose(); }
    }

    // ---- 注入路由(经 InjectTest → SdlPump.Dispatch,与 PumpAll 同入口)----

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_Keyboard_Motion_Wheel_Routing()
    {
        var w = new SdlWindow("sharpui-t2-keys", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(KeyEv(w.WindowId, true, 4, SdlEvents.KmodLShift));   // A + LShift(scancode 路径)
            w.InjectTest(KeyEv(w.WindowId, false, 4));
            w.InjectTest(TextEv(w.WindowId, "A"));
            w.InjectTest(MotionEv(w.WindowId, 20, 30));
            w.InjectTest(WheelEv(w.WindowId, 3));
            w.InjectTest(WheelEv(w.WindowId, 2));                              // 同帧累计

            InputSnapshot s = w.GetSnapshotAndReset();
            Assert.Equal(new[] { (Key.A, true, ModifierKeys.Shift), (Key.A, false, ModifierKeys.None) }, s.KeyEvents);
            Assert.Equal(new[] { 'A' }, s.KeyCharPresses);
            Assert.Equal(new PointF(20, 30), s.MousePosition);
            Assert.Equal(5f, s.WheelDelta);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_TextInput_Decodes_MultiByteUtf8()
    {
        var w = new SdlWindow("sharpui-t2-utf8", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(TextEv(w.WindowId, "你好"));   // 各 3B UTF-8,0 终止串解码
            Assert.Equal(new[] { '你', '好' }, w.GetSnapshotAndReset().KeyCharPresses);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_MouseButton_States_And_X1X2_ToButton1Button2()
    {
        var w = new SdlWindow("sharpui-t2-mouse", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(ButtonEv(w.WindowId, 1, true));    // SDL_BUTTON_LEFT
            w.InjectTest(ButtonEv(w.WindowId, 4, true));    // SDL_BUTTON_X1 → Button1(Veldrid 对齐)
            w.InjectTest(ButtonEv(w.WindowId, 5, false));   // SDL_BUTTON_X2 → Button2
            w.InjectTest(ButtonEv(w.WindowId, 1, false));

            InputSnapshot s = w.GetSnapshotAndReset();
            Assert.Equal(new[] { (MouseButton.Left, true), (MouseButton.Button1, true),
                (MouseButton.Button2, false), (MouseButton.Left, false) }, s.MouseEvents);
            Assert.False(s.IsMouseDown(MouseButton.Left));        // up 后清除
            Assert.True(s.IsMouseDown(MouseButton.Button1));      // 持久态跨事件保留
            Assert.False(s.IsMouseDown(MouseButton.Button2));
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void TwoWindows_RouteByWindowId_OnlyOwn()
    {
        var w1 = new SdlWindow("sharpui-t2-r1", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        var w2 = new SdlWindow("sharpui-t2-r2", 120, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w1.InjectTest(KeyEv(w1.WindowId, true, 9));     // F 键(scancode 9)
            w2.InjectTest(KeyEv(w2.WindowId, true, 8));     // E 键(scancode 8)
            w2.InjectTest(MotionEv(w2.WindowId, 5, 6));

            Assert.Equal(new[] { (Key.F, true, ModifierKeys.None) }, w1.GetSnapshotAndReset().KeyEvents);
            InputSnapshot s2 = w2.GetSnapshotAndReset();
            Assert.Equal(new[] { (Key.E, true, ModifierKeys.None) }, s2.KeyEvents);
            Assert.Equal(new PointF(5, 6), s2.MousePosition);
        }
        finally { w1.Dispose(); w2.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void WindowEvents_ResizedGuard_FocusLost_Close_Quit()
    {
        var w1 = new SdlWindow("sharpui-t2-ev1", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        var w2 = new SdlWindow("sharpui-t2-ev2", 120, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            bool resized = false, focusLost = false;
            w1.Resized += () => resized = true;
            w1.FocusLost += () => focusLost = true;

            w1.InjectTest(WindowEv(w1.WindowId, SdlEvents.WindowEventResized, 640, 480));
            Assert.True(resized);
            w1.InjectTest(WindowEv(w1.WindowId, SdlEvents.WindowEventResized, 0, 0));   // 0 尺寸不触发
            w1.InjectTest(WindowEv(w1.WindowId, SdlEvents.WindowEventSizeChanged, 800, 600));
            Assert.True(resized);

            w1.InjectTest(WindowEv(w1.WindowId, SdlEvents.WindowEventFocusLost));
            Assert.True(focusLost);

            w1.InjectTest(WindowEv(w1.WindowId, SdlEvents.WindowEventClose));   // 单窗 Close 事件
            Assert.False(w1.Exists);
            Assert.True(w2.Exists);                                             // 不殃及他窗

            w1.InjectTest(new SdlEvents.SdlEvent { Type = SdlEvents.EventTypeQuit });   // QUIT → 全部 MarkClosed
            Assert.False(w2.Exists);
        }
        finally { w1.Dispose(); w2.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void UnownedMouseMotion_PropagatesTo_LastFocusedWindow()
    {
        var w1 = new SdlWindow("sharpui-t2-p1", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        var w2 = new SdlWindow("sharpui-t2-p2", 120, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w2.InjectTest(WindowEv(w2.WindowId, SdlEvents.WindowEventFocusGained));   // w2 = 最近聚焦
            w2.InjectTest(MotionEv(0xDEADBEEF, 33, 44));   // 无归属 WindowId 的移动 → 默认传播给 w2

            Assert.Equal(new PointF(33, 44), w2.GetSnapshotAndReset().MousePosition);
            Assert.Equal(PointF.Zero, w1.GetSnapshotAndReset().MousePosition);   // w1 未受污染
        }
        finally { w1.Dispose(); w2.Dispose(); }
    }
}
