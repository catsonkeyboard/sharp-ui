# 窗口/输入层自研设计(去 Veldrid.SDL2,依赖清零收官)

日期:2026-10-26 | 状态:已批准 | 归属:依赖清零第三战(渲染✅D3D11/GL、文本✅FT/HB、窗口/输入←本篇)

## 目标与动机

用自有 SDL2 绑定层替换 Veldrid.SDL2 包(窗口创建/事件泵/输入快照/三枚举),完成后:

- **删 Veldrid.SDL2 + Newtonsoft.Json 钉版**(其唯一存在理由是 Veldrid 链),全仓 Veldrid 零残留,NativeLibraryLoader 等传递依赖同退。
- **SDL2.dll 原生库保留**(vendored 2.30.8,与 FT/HB 同地位)——SDL 即跨平台窗口/输入核心,自研原生窗口层=三平台三套代码,非本项目目标。
- AOT 产物预期 6.95MB → 约 6MB。
- 架构顺手改进:全局单泵分发替代 Veldrid 每窗各泵(多窗事件归属隐患)。

## 已批准决策

| # | 决策 | 内容 |
|---|---|---|
| W-1 | 替换深度 | 仅 C# 绑定层;SDL2.dll 保留 |
| W-2 | Key 枚举 | 自有 `SharpUI.Key`/`MouseButton`/`ModifierKeys`,成员名逐一对齐 Veldrid 现名(KeyEventData 签名与 switch case 零改动迁移;公共面后端中立,未来 GLFW/SDL3 不动公共 API) |
| W-3 | 事件泵架构 | App.Run 每帧全局泵一次(SDL_PollEvent 循环),按 windowID 分发到各窗快照;弃每窗各泵 |
| W-4 | 模块纪律 | 复用 SdlNative 活模块路径(裁定#38);TextInput/SdlTextInput 不重写 |

## 架构

```
src/SharpUI/Sdl/
 ├─ SdlEvents.cs     SDL_Event(56B union)声明层:SDL_PollEvent/事件类型常量(QUIT/WINDOWEVENT/KEYDOWN/UP/
 │    TEXTINPUT/MOUSEMOTION/BUTTONDOWN/UP/WHEEL)/键码与 KMOD_*/窗口事件子类型(RESIZED/FOCUS/CLOSE)/
 │    SDL_GetWindowID/SDL_GetWindowSize/SDL_GetDisplayDPI/SDL_GetWindowDisplayIndex;结构自检测试
 ├─ Key.cs           Key(~120 成员,名对齐 Veldrid)/MouseButton/ModifierKeys
 ├─ InputSnapshot.cs 自有快照:MousePosition(PointF)/MouseEvents/KeyEvents/IsMouseDown/WheelDelta/
 │    KeyCharPresses;成员名与消费面(InputMapper)对齐,迁移零形变
 └─ SdlWindow.cs     创建(SDL_CreateWindow 经活模块,flags 自有枚举含 OpenGL/Hidden/Shown/Resizable)/
      Exists/Close/Width/Height/SdlWindowHandle/ScaleFactor(SDL_GetDisplayDPI/96,对齐现观察 ~1.588)/
      Resized+FocusLost+Closed 事件(经全局泵分发触发)/每窗 ID
```

- **App.Run 改造**:`SdlEvents.PumpAll()` 一帧一次 → 按 windowID 路由:各窗 `BeginFrameSnapshot`/累积;窗口事件直接触发 Resized/FocusLost/Close 标记。
- **InputMapper** 改吃自有 `InputSnapshot`(签名 `Map(InputSnapshot, float)`,逻辑零改动)。
- **键码映射**:SDL keycode/scancode → Key 单一一处(SdlEvents),含 Ctrl/Shift/Alt 修饰位 → ModifierKeys;SDL_TEXTINPUT 的 text 字段(32 字节 UTF-8)→ KeyCharPresses(UTF8→UTF16 解码)。
- **IME 复验要求**:泵重构后 SDL_TEXTINPUT/TEXTEDITING 链路必须实测回归(拼音组合+提交)——TextInput 定位通道不动,但事件到达路径换了。

## 语义保持清单

- ScaleFactor 数值行为:与 Veldrid.SDL2 观察值一致(本机 ~1.588=DPI/96);`Math.Clamp(0.5,4)` 不变。
- 窗口 flags:Resizable|Shown(+GL 路径 OpenGL)对齐现状;DPI unaware 进程行为不变。
- 多窗:OpenWindow 语义不变;全局泵后各窗事件不再互相竞争。
- 剪贴板(M3c 自研)不动。

## 测试策略

1. 声明层:SDL_Event union 各分支结构自检 + 事件类型常量对 SDL 头文件实数(纪律同前)。
2. 键码映射:逐键单测(字母/数字/方向/BackSpace/Enter/Escape/修饰组合)。
3. 快照映射:合成 SDL_Event 字节(手动填 struct)→ PumpAll 分发 → 各窗快照断言(含多窗路由、TEXTINPUT 解码、WHEEL)。
4. 窗口生命周期冒烟:建/改尺寸/关;Counter 交互手测(点击/键盘/**IME 拼音回归**)。
5. 迁移平移:InputMapperTests/FocusTests/TextEditingTests 等改引用自有类型,断言零漂移。

## 范围外

- SDL3/GLFW 迁移选型(M3e 后半平台化再议)。
- 游戏手柄/触摸/拖放事件(现无消费方)。
- 运行时 DPI 变更监听(现状亦无)。

## 验收清单

- [ ] `grep -rni veldrid src/ examples/` 零命中;Veldrid.SDL2+Newtonsoft 包删除;传递依赖(NativeLibraryLoader 等)退场。
- [ ] 全量测试绿(迁移平移+新增);Counter 交互+IME 实测通过。
- [ ] AOT 产物 ≤6.5MB(实测记录);两后端(D3D11/GL)窗口路径均冒烟。
- [ ] spec 主文档裁定#43(窗口层自研定稿+全局泵架构+依赖清零收官)。
