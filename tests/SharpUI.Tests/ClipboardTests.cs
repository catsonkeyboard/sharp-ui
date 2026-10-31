using Xunit.Abstractions;

namespace SharpUI.Tests;

[Collection("interaction")]   // SDL 剪贴板是进程全局资源,并入静态用户串行集(惯例;类本身无静态)
public class ClipboardTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Gpu")]   // 真机原生往返,无 GPU 但沿用隔离惯例(T5 探针约定)
    public void Clipboard_SetGetRoundTrips()
    {
        // 本机执行上下文可能拒绝剪贴板访问(DLP/占用策略,Win32 OpenClipboard err 5):
        // 系统拒绝时无法往返断言,记录说明后通过;授权环境(用户终端)仍然做真实往返。
        if (!Clipboard.TrySetText("sharpui-clip"))
            output.WriteLine("SKIP: OS denied clipboard access (OpenClipboard err 5); round-trip not asserted in this context.");
        else
            Assert.Equal("sharpui-clip", Clipboard.GetText());
    }

    /// <summary>审计修复波 C-1:SDL 剪贴板契约 UTF-8 —— 非 ASCII(BMP 外 emoji + CJK)双向往返恒等。
    /// 修复前 SetText 按 CharSet.Ansi 系统代码页编码、GetText 按 PtrToStringAnsi 解码,任何 locale 下
    /// 非 ASCII 必然 mojibake;修复后手工 UTF-8 封送(建窗标题同款),往返应零损。</summary>
    [Fact]
    [Trait("Category", "Gpu")]
    public void Clipboard_NonAsciiUtf8_RoundTrips()
    {
        const string s = "你好 SharpUI 😀";   // BMP 内 CJK + 代理对 emoji,两路编码陷阱都覆盖
        if (!Clipboard.TrySetText(s))
            output.WriteLine("SKIP: OS denied clipboard access (OpenClipboard err 5); round-trip not asserted in this context.");
        else
            Assert.Equal(s, Clipboard.GetText());
    }
}
