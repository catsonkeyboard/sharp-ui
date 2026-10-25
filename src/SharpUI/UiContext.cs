namespace SharpUI;

/// <summary>本帧输入的服务定位(与 App.Atlas/State 同款;UiScope 在用户构建闭包内读取)。</summary>
internal static class UiContext
{
    public static InputState CurrentInput;
}
