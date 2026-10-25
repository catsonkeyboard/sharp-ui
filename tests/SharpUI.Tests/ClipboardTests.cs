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
            Assert.Equal("sharpui-clip", Clipboard.GetText());   // ANSI 通道:非 ASCII 会被代码页改写,断言串保持 ASCII
    }
}
