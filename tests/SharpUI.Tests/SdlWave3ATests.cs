using System.Runtime.InteropServices;
using System.Text;
using SharpUI;

namespace SharpUI.Tests;

/// <summary>波 3A:SDL 绑定层与事件泵缺失面补齐(审计 §A6/§B B2/B3/B4/B5/B6 部分/B10/B11/B12/B13/B21 部分)。
/// 常量/偏移全部按 SDL 2.30.8 官方头文件实数核数(tarball sha256 380c295e… 与 native/SDL2/VERSIONS.md
/// 同源;逐行出处见 SdlEvents.cs 注释)。分两层:
/// ① 纯逻辑/常量(无 Gpu trait,不触原生模块):事件常量、联合体偏移、枚举实数、映射透传;
/// ② 泵注入/真模块(Gpu trait):经 SdlWindow.InjectTest → SdlPump.Dispatch 单一入口合成事件,
///    驱动新通道(全按钮+clicks/repeat/Alt-Gui/滚轮精度/拖放/生命周期);窗口管理方法与游标/VSync
///    走真模块冒烟(真窗行为细节留 CI)。拖放合成用 SDL_malloc 分配 file 缓冲 —— SDL_free 必须与
///    SDL 分配器同堆(Marshal.AllocHGlobal 混堆属 UB,Windows LocalAlloc 与 ucrt malloc 非同一分配器)。
/// 测试主机禁并行见 AssemblyInfo.cs。</summary>
public class SdlWave3ATests
{
    // ---- ① 常量与联合体核数(纯托管,无 Gpu)----

    [Fact]
    public void DropEvent_Constants_MatchSdlHeaders()
        // SDL_events.h L151-154:SDL_DROPFILE=0x1000,DROPTEXT/DROPBEGIN/DROPCOMPLETE 顺延
        => Assert.Equal(new[] { 0x1000u, 0x1001u, 0x1002u, 0x1003u },
            new[] { SdlEvents.EventTypeDropFile, SdlEvents.EventTypeDropText,
                    SdlEvents.EventTypeDropBegin, SdlEvents.EventTypeDropComplete });

    [Fact]
    public void WindowEvent_Moved_DisplayChanged_MatchSdlHeaders()
    {
        // SDL_video.h L155-178 SDL_WindowEventID 声明序递增(NONE=0 起):MOVED=4(L161)、
        // DISPLAY_CHANGED=18(L178);既有成员顺手回归(防后续误改)。
        Assert.Equal(4, SdlEvents.WindowEventMoved);
        Assert.Equal(18, SdlEvents.WindowEventDisplayChanged);
        Assert.Equal(5, SdlEvents.WindowEventResized);
        Assert.Equal(12, SdlEvents.WindowEventFocusGained);
        Assert.Equal(13, SdlEvents.WindowEventFocusLost);
        Assert.Equal(14, SdlEvents.WindowEventClose);
    }

    [Fact]
    public void SdlEvent_Union_DropAndWheelFieldOffsets_MatchSdlHeaders()
    {
        // SDL_DropEvent(SDL_events.h L555-561):file@8(char*)在 windowID@12 **之前**
        // —— 与公共头 windowID@8 相反,简报"windowID@8"系记忆值,按头文件实数校正。
        // SDL_MouseWheelEvent(L312-325):direction@24、preciseX@28/preciseY@32(2.0.18+)、
        // mouseX@36/mouseY@40(2.26.0+)。
        Assert.Equal(56, Marshal.SizeOf<SdlEvents.SdlEvent>());   // 联合体尾部 padding 恒 56B(L617-644)
        // DropEvent 为顺序布局:C 结构体 file 指针占 8..15,windowID 顺延 @16(非分支内联合互叠;
        // C 声明序 type@0/timestamp@4/file@8/windowID@16,SDL_DropEvent 自身 24B 对齐 8)。
        // 初版误标 windowID@12 系把 C 结构体字段当分支内联合处理 —— 指针高 4B 被截断实证 AV,已按实数校正。
        Assert.Equal(new IntPtr(8), Marshal.OffsetOf<SdlEvents.SdlEvent>("DropFile"));
        Assert.Equal(new IntPtr(16), Marshal.OffsetOf<SdlEvents.SdlEvent>("DropWindowId"));
        Assert.Equal(new IntPtr(28), Marshal.OffsetOf<SdlEvents.SdlEvent>("WheelPreciseX"));
        Assert.Equal(new IntPtr(32), Marshal.OffsetOf<SdlEvents.SdlEvent>("WheelPreciseY"));
        Assert.Equal(new IntPtr(36), Marshal.OffsetOf<SdlEvents.SdlEvent>("WheelMouseX"));
        Assert.Equal(new IntPtr(40), Marshal.OffsetOf<SdlEvents.SdlEvent>("WheelMouseY"));
    }

    [Fact]
    public void SdlWindowFlags_AllowHighDpi_Is0x2000()
        // SDL_WINDOW_ALLOW_HIGHDPI(SDL_video.h L117);SdlEvents.WindowFlagAllowHighDpi 常量同值恒等
        => Assert.Equal(SdlEvents.WindowFlagAllowHighDpi, (uint)SdlWindowFlags.AllowHighDpi);

    [Fact]
    public void KeyEventData_AltGuiRepeat_DefaultsFalse_ZeroShapeChange()
    {
        // 既有三参调用零形变:Alt/Gui/IsRepeat 走默认参数(B11/B12)
        KeyEventData legacy = new(Key.A, true, false);
        Assert.False(legacy.Alt);
        Assert.False(legacy.Gui);
        Assert.False(legacy.IsRepeat);
        Assert.True(new KeyEventData(Key.A, true, false, Ctrl: true, Alt: true, Gui: true, IsRepeat: true)
            is { Ctrl: true, Alt: true, Gui: true, IsRepeat: true });
    }

    [Fact]
    public void CursorShape_Values_IdentityMapToSdlSystemCursor()
        // SDL_SystemCursor(SDL_mouse.h L47-61)声明序:ARROW=0..HAND=11(12 项,NUM_SYSTEM_CURSORS=12 不入枚举)。
        // CursorShape 按同序声明 → (int)shape 即 SDL_SystemCursor id,映射零转换。
        => Assert.Equal(new[] {
            CursorShape.Arrow, CursorShape.IBeam, CursorShape.Wait, CursorShape.Crosshair,
            CursorShape.WaitArrow, CursorShape.SizeNwse, CursorShape.SizeNesw, CursorShape.SizeWe,
            CursorShape.SizeNs, CursorShape.SizeAll, CursorShape.No, CursorShape.Hand },
            new[] { (CursorShape)0, (CursorShape)1, (CursorShape)2, (CursorShape)3, (CursorShape)4,
                    (CursorShape)5, (CursorShape)6, (CursorShape)7, (CursorShape)8, (CursorShape)9,
                    (CursorShape)10, (CursorShape)11 });

    [Fact]
    public void InputMapper_MapKeys_CarriesAltGui_From3Tuple()
        // B11 透传修复:旧 MapKeys 只映 Shift/Ctrl,Alt/Gui 被丢弃 —— 现经 KeyEventData 新成员透传
    {
        KeyEventData[] keys = InputMapper.MapKeys(new (Key, bool, ModifierKeys)[]
        {
            (Key.A, true, ModifierKeys.Alt | ModifierKeys.Gui),
            (Key.B, true, ModifierKeys.Control | ModifierKeys.Alt),
        });
        Assert.True(keys[0].Alt && keys[0].Gui);
        Assert.True(keys[1].Ctrl && keys[1].Alt && !keys[1].Gui);
        // 零形变:Shift/Ctrl 语义不变
        Assert.False(keys[0].Shift && keys[0].Ctrl);
    }

    // ---- ② 泵注入(Gpu:真窗注册 + InjectTest → SdlPump.Dispatch)----

    static SdlEvents.SdlEvent KeyEv(uint win, bool down, uint scancode, ushort mod = 0, byte repeat = 0) => new()
    {
        Type = down ? SdlEvents.EventTypeKeyDown : SdlEvents.EventTypeKeyUp,
        WindowId = win,
        KeyState = SdlEvents.Pressed,
        KeyRepeat = repeat,
        KeysymScancode = scancode,
        KeysymSym = scancode,
        KeysymMod = mod,
    };

    static SdlEvents.SdlEvent ButtonEv(uint win, byte id, bool down, byte clicks = 1) => new()
    {
        Type = down ? SdlEvents.EventTypeMouseButtonDown : SdlEvents.EventTypeMouseButtonUp,
        WindowId = win,
        MouseButtonId = id,
        MouseButtonState = down ? SdlEvents.Pressed : (byte)0,
        MouseClicks = clicks,
    };

    static SdlEvents.SdlEvent WheelEv(uint win, int y, uint direction = 0, float preciseY = 0f, int mouseX = 0, int mouseY = 0) => new()
    {
        Type = SdlEvents.EventTypeMouseWheel, WindowId = win, WheelY = y, WheelDirection = direction,
        WheelPreciseY = preciseY, WheelMouseX = mouseX, WheelMouseY = mouseY,
    };

    static SdlEvents.SdlEvent WindowEv(uint win, byte code, int d1 = 0, int d2 = 0) => new()
    {
        Type = SdlEvents.EventTypeWindow, WindowId = win, WindowEventCode = code, WindowData1 = d1, WindowData2 = d2,
    };

    /// <summary>合成 DROPFILE/DROPTEXT 事件:file 缓冲经 SDL_malloc(同模块分配器,泵侧 SDL_free 释放合法)。</summary>
    static SdlEvents.SdlEvent DropEv(uint type, uint win, string payload)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(payload);
        IntPtr p = SdlEvents.SdlMalloc((nuint)(utf8.Length + 1));
        Marshal.Copy(utf8, 0, p, utf8.Length);
        Marshal.WriteByte(p, utf8.Length, 0);
        return new SdlEvents.SdlEvent { Type = type, DropFile = p, DropWindowId = win };
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_MouseButton_FullChannel_CarriesClicks_TupleCompatUnchanged()
    {
        var w = new SdlWindow("sharpui-3a-mouse", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(ButtonEv(w.WindowId, SdlEvents.ButtonRight, true));
            w.InjectTest(ButtonEv(w.WindowId, SdlEvents.ButtonMiddle, true));
            w.InjectTest(ButtonEv(w.WindowId, SdlEvents.ButtonRight, false));
            w.InjectTest(ButtonEv(w.WindowId, SdlEvents.ButtonX1, true));

            InputSnapshot s = w.GetSnapshotAndReset();
            // 全按钮新通道(含 clicks;B2/B10)
            Assert.Equal(new[]
            {
                new MouseButtonEventData(MouseButton.Right, true, 1),
                new MouseButtonEventData(MouseButton.Middle, true, 1),
                new MouseButtonEventData(MouseButton.Right, false, 1),
                new MouseButtonEventData(MouseButton.Button1, true, 1),
            }, s.ButtonEvents);
            // 既有 2 元组通道与 IsMouseDown 持久态零形变
            Assert.Equal(new[] { (MouseButton.Right, true), (MouseButton.Middle, true),
                (MouseButton.Right, false), (MouseButton.Button1, true) }, s.MouseEvents);
            Assert.True(s.IsMouseDown(MouseButton.Middle));
            Assert.True(s.IsMouseDown(MouseButton.Button1));
            Assert.False(s.IsMouseDown(MouseButton.Right));
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_MouseButton_DoubleClick_ClicksEquals2()
    {
        var w = new SdlWindow("sharpui-3a-dbl", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(ButtonEv(w.WindowId, SdlEvents.ButtonLeft, true, clicks: 1));
            w.InjectTest(ButtonEv(w.WindowId, SdlEvents.ButtonLeft, false, clicks: 1));
            w.InjectTest(ButtonEv(w.WindowId, SdlEvents.ButtonLeft, true, clicks: 2));   // 双击(词选/标题栏场景)
            InputSnapshot s = w.GetSnapshotAndReset();
            Assert.Equal(new byte[] { 1, 1, 2 }, s.ButtonEvents.Select(e => (byte)e.Clicks).ToArray());
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_KeyRepeat_MarksIsRepeat_KeyUpNeverMarked()
    {
        var w = new SdlWindow("sharpui-3a-repeat", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(KeyEv(w.WindowId, true, 44, repeat: 0));    // Space 首按
            w.InjectTest(KeyEv(w.WindowId, true, 44, repeat: 1));    // 长按自动重复(A7/B12:文本受益、激活可跳过)
            w.InjectTest(KeyEv(w.WindowId, false, 44, repeat: 1));   // KeyUp 即便 repeat 位为 1 也不标(SDL 重复只发 Down)
            InputSnapshot s = w.GetSnapshotAndReset();
            Assert.Equal(new[] { false, true, false }, s.KeyEventsFull.Select(k => k.IsRepeat).ToArray());
            // 既有 3 元组通道零形变
            Assert.Equal(3, s.KeyEvents.Count);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_KeyModifiers_AltGui_PassthroughToFullRecords()
    {
        var w = new SdlWindow("sharpui-3a-mod", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(KeyEv(w.WindowId, true, 4,
                (ushort)(SdlEvents.KmodLAlt | SdlEvents.KmodRGui)));   // Alt+A + Cmd 位(B11:macOS Cmd 快捷键前置)
            InputSnapshot s = w.GetSnapshotAndReset();
            KeyEventData full = s.KeyEventsFull.Single();
            Assert.True(full.Alt);
            Assert.True(full.Gui);
            Assert.Equal(Key.A, full.Key);
            // 既有通道:ModifierKeys 聚合位照旧
            Assert.Equal(ModifierKeys.Alt | ModifierKeys.Gui, s.KeyEvents[0].Modifiers);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_Wheel_PreciseFloat_IntegerFallback_FlippedNormalization()
    {
        var w = new SdlWindow("sharpui-3a-wheel", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(WheelEv(w.WindowId, 0, preciseY: 0.5f));    // 触控板小数增量(A6:修复前截断为 0)
            w.InjectTest(WheelEv(w.WindowId, 0, preciseY: 0.75f));
            w.InjectTest(WheelEv(w.WindowId, 3));                    // 无 precise(旧模块/合成)回落整型 3
            w.InjectTest(WheelEv(w.WindowId, 2, direction: SdlEvents.WheelFlipped, preciseY: 2f));
            // FLIPPED 归一(任务项 8:保持"上=+1"语义 —— SDL 头文件 L320:FLIPPED 时值已反向,乘 -1 还原)
            InputSnapshot s = w.GetSnapshotAndReset();
            Assert.Equal(0.5f + 0.75f + 3f - 2f, s.WheelDelta, 4);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_Wheel_CarriesMousePosition_Updates_StalePosition()
    {
        var w = new SdlWindow("sharpui-3a-wpos", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            w.InjectTest(WheelEv(w.WindowId, 1, mouseX: 17, mouseY: 23));   // 无 motion 的滚轮:免滞后命中(A6)
            Assert.Equal(new PointF(17, 23), w.GetSnapshotAndReset().MousePosition);
            // (0,0) 视为"未携带"(SDL 后端先 SDL_zero 再填充;2.26 前该字段不存在):不清位、免误落原点
            w.InjectTest(WheelEv(w.WindowId, 1, mouseX: 0, mouseY: 0));
            w.InjectTest(new SdlEvents.SdlEvent { Type = SdlEvents.EventTypeMouseMotion, WindowId = w.WindowId, MouseX = 5, MouseY = 6 });
            w.InjectTest(WheelEv(w.WindowId, 1, mouseX: 0, mouseY: 0));
            Assert.Equal(new PointF(5, 6), w.GetSnapshotAndReset().MousePosition);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_DropFile_DecodesUtf8_RaisesFileDropped()
    {
        var w = new SdlWindow("sharpui-3a-drop", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            string? dropped = null;
            w.FileDropped += s => dropped = s;
            w.InjectTest(DropEv(SdlEvents.EventTypeDropFile, w.WindowId, "你好.txt"));
            Assert.Equal("你好.txt", dropped);   // UTF-8 解码 + SDL_free(泵侧,同模块)
            Assert.False(w.Exists is false);     // 窗口不受影响(纯事件通道)
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_DropText_RaisesTextDropped()
    {
        var w = new SdlWindow("sharpui-3a-droptext", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            string? text = null;
            w.TextDropped += s => text = s;
            w.InjectTest(DropEv(SdlEvents.EventTypeDropText, w.WindowId, "drag text"));
            Assert.Equal("drag text", text);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_DropBeginComplete_RaiseLifecycle_NoPayload()
    {
        var w = new SdlWindow("sharpui-3a-droplife", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            bool began = false, completed = false;
            w.DropBegan += () => began = true;
            w.DropCompleted += () => completed = true;
            // BEGIN/COMPLETE 的 file 恒 NULL(SDL_events.h L559)
            w.InjectTest(new SdlEvents.SdlEvent { Type = SdlEvents.EventTypeDropBegin, DropWindowId = w.WindowId });
            w.InjectTest(new SdlEvents.SdlEvent { Type = SdlEvents.EventTypeDropComplete, DropWindowId = w.WindowId });
            Assert.True(began && completed);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_Drop_WindowIdZero_BroadcastsToPrimaryWindow()
    {
        var w1 = new SdlWindow("sharpui-3a-dp1", 60, 60, 64, 64, SdlWindowFlags.Hidden);   // 首注册 = 主窗
        var w2 = new SdlWindow("sharpui-3a-dp2", 120, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            string? toPrimary = null;
            int toSecondary = 0;
            w1.FileDropped += s => toPrimary = s;
            w2.FileDropped += _ => toSecondary++;
            w1.InjectTest(DropEv(SdlEvents.EventTypeDropFile, 0, "global-drop"));   // windowID=0(全局拖放)
            Assert.Equal("global-drop", toPrimary);
            Assert.Equal(0, toSecondary);
        }
        finally { w1.Dispose(); w2.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Inject_WindowEvents_FocusGained_And_Moved_Raised()
    {
        var w = new SdlWindow("sharpui-3a-life", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            bool gained = false;
            (int X, int Y)? moved = null;
            w.FocusGained += () => gained = true;
            w.Moved += (x, y) => moved = (x, y);
            w.InjectTest(WindowEv(w.WindowId, SdlEvents.WindowEventFocusGained));
            w.InjectTest(WindowEv(w.WindowId, SdlEvents.WindowEventMoved, 128, 64));
            Assert.True(gained);
            Assert.Equal((128, 64), moved);   // MOVED data1/data2 = 新位置(SDL_video.h L161)
        }
        finally { w.Dispose(); }
    }

    // ---- ②' 窗口管理方法(B3)与游标/VSync/生命周期开放:真模块冒烟 ----

    [Fact]
    [Trait("Category", "Gpu")]
    public void Window_ManagementMethods_SetTitleAndSizeReadback_NoThrow()
    {
        var w = new SdlWindow("sharpui-3a-mgmt", 60, 60, 200, 150, SdlWindowFlags.Hidden);
        try
        {
            w.SetTitle("sharpui-3a-titled");          // SDL_SetWindowTitle(L866,UTF-8)
            Assert.Equal("sharpui-3a-titled", w.Title);   // SDL_GetWindowTitle(L880)读回
            w.SetSize(180, 140);                       // SDL_SetWindowSize(L984)
            Assert.Equal((180, 140), (w.Width, w.Height));   // GetWindowSize 读回(200x150 起步避开最小宽钳制)
            w.SetPosition(120, 80);                    // SDL_SetWindowPosition(L940)
            w.SetMinimumSize(80, 60);                  // SDL_SetWindowMinimumSize(L1083)
            w.SetMaximumSize(800, 600);                // SDL_SetWindowMaximumSize(L1115)
            w.SetBordered(false);                      // SDL_SetWindowBordered(L1151)
            w.SetBordered(true);
            w.Maximize();                              // SDL_MaximizeWindow(L1232)
            w.Restore();                               // SDL_RestoreWindow(L1256)
            w.Minimize();                              // SDL_MinimizeWindow(L1244)
            w.Restore();
            w.Raise();                                 // SDL_RaiseWindow(L1220)
            w.FocusInput();                            // SDL_SetWindowInputFocus(L1644;返回值 WM 相关,不断言)
            w.SetFullscreen(true);                     // SDL_SetWindowFullscreen(L1275,desktop 模式)
            w.SetFullscreen(false);
            w.Maximize(); w.Restore(); w.Minimize(); w.Restore();   // 恢复常规态
            Assert.True(w.Exists);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Window_SizeInPixels_AtLeastLogicalSize()
    {
        var w = new SdlWindow("sharpui-3a-px", 60, 60, 200, 150, SdlWindowFlags.Hidden);
        try
        {
            (int pw, int ph) = w.SizeInPixels;   // SDL_GetWindowSizeInPixels(L1068,2.26.0+)
            // Windows SDL 坐标即物理像素(SdlNative.GetDrawableSize 注)→ 恒等;macOS 无 ALLOW_HIGHDPI 时 1x 恒等;
            // 统一按 ≥ 断言(未来建窗路径传 AllowHighDpi 时 Retina 下 >)
            Assert.True(pw >= w.Width && ph >= w.Height);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void UiCursor_SetAllSystemShapes_AndDefault_NoThrow()
    {
        var w = new SdlWindow("sharpui-3a-cursor", 60, 60, 64, 64, SdlWindowFlags.Hidden);   // 真模块就绪
        try
        {
            foreach (CursorShape shape in Enum.GetValues<CursorShape>())
                UiCursor.Set(shape);     // 12 系统游标惰性创建 + SDL_SetCursor(缓存,重复 Set 不再 CreateSystemCursor)
            UiCursor.UseDefault();       // SDL_GetDefaultCursor 复原
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void UiCursor_SystemCursors_CachedPerShape()
        // 缓存语义:同一形状重复 Set 只创建一次(12 项惰性;句柄非零)
    {
        var w = new SdlWindow("sharpui-3a-curcache", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            UiCursor.Set(CursorShape.IBeam);
            UiCursor.Set(CursorShape.IBeam);
            IntPtr first = SdlCursor.HandleForTest(CursorShape.IBeam);
            Assert.NotEqual(IntPtr.Zero, first);
            Assert.Equal(first, SdlCursor.HandleForTest(CursorShape.IBeam));   // 二次取同句柄(缓存命中)
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void AppWindow_LifecycleAndDropEvents_PassthroughFromSdlWindow()
        // B4/B5 对应用开放:AppWindow 公共事件透传(Window 为 internal 字段,本类是应用侧唯一入口)
    {
        if (!OperatingSystem.IsWindows() && !GlSupport.Available) return;   // Linux CI 无 GPU;macOS metal 窗路径未接(B16)
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            if (!OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("SHARPUI_BACKEND", "gl");
            AppWindow app = App.CreateWindow("sharpui-3a-appwin", 200, 150, null);
            try
            {
                bool gained = false, lost = false, resized = false, vetoed = false, dropped = false;
                (int X, int Y)? moved = null;
                app.FocusGained += () => gained = true;
                app.FocusLost += () => lost = true;
                app.Resized += () => resized = true;
                app.Moved += (x, y) => moved = (x, y);
                app.Closing += () => { vetoed = true; return true; };
                app.FileDropped += _ => dropped = true;

                SdlWindow w = app.Window;
                w.InjectTest(WindowEv(w.WindowId, SdlEvents.WindowEventFocusGained));
                w.InjectTest(WindowEv(w.WindowId, SdlEvents.WindowEventFocusLost));
                w.InjectTest(WindowEv(w.WindowId, SdlEvents.WindowEventResized, 180, 140));
                w.InjectTest(WindowEv(w.WindowId, SdlEvents.WindowEventMoved, 96, 48));
                w.InjectTest(WindowEv(w.WindowId, SdlEvents.WindowEventClose));
                w.InjectTest(DropEv(SdlEvents.EventTypeDropFile, w.WindowId, "via-appwindow"));

                Assert.True(gained && lost && resized && vetoed && dropped);
                Assert.Equal((96, 48), moved);
                Assert.False(w.Exists);   // Closing 透传返回 true → CLOSE 放行(透传管道语义验证)
            }
            finally { app.Window.Dispose(); }
        }
        finally { Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Window_CreationFlags_RecordAllowHighDpiDecision()
        // B6 部分:Windows 不传 ALLOW_HIGHDPI(DPI-aware 路径已覆盖;传 flag 反而变像素单位破坏现语义),
        // macOS/Linux 传 —— Windows 主机断言未传;非 Windows 分支待 CI 实机(注释记账)。
    {
        var w = new SdlWindow("sharpui-3a-flags", 60, 60, 64, 64, SdlWindowFlags.Hidden);
        try
        {
            if (OperatingSystem.IsWindows())
                Assert.Equal(SdlWindowFlags.None, w.CreationFlags & SdlWindowFlags.AllowHighDpi);
            else
                Assert.Equal(SdlWindowFlags.AllowHighDpi, w.CreationFlags & SdlWindowFlags.AllowHighDpi);
        }
        finally { w.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void App_Run_ExitCode_And_VSync_NoThrow()
        // B21 部分:Run 返回 exit code(默认 0 兼容);VSync(bool) 静态 API 应用到已存在窗口,后端各自落地
    {
        if (!OperatingSystem.IsWindows() && !GlSupport.Available) return;
        string? old = Environment.GetEnvironmentVariable("SHARPUI_BACKEND");
        try
        {
            if (!OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("SHARPUI_BACKEND", "gl");
            App app = App.Create("sharpui-3a-run", 200, 150, () => Ui.Text(""));
            try
            {
                App.VSync(false);   // D3D11:Present syncInterval 0;GL:SetSwapInterval(0);no-throw
                App.VSync(true);    // 复原
                Assert.Equal(42, app.Run(1, 42));   // maxFrames=1 后正常退出 → 返回 exitCode
            }
            finally { }
        }
        finally { Environment.SetEnvironmentVariable("SHARPUI_BACKEND", old); }
    }
}
