using SharpUI;
using static SharpUI.Ui;

internal sealed class CounterState
{
    public int Count;

    // D4 控件段状态(裁定#44;回调式 C-2:状态存应用侧,控件不藏状态)
    public bool Checked;
    public bool RadioA = true;   // "a"/"b" 同组互斥=应用回调职责
    public float SliderValue = 50f;

    // D5 浮层段状态(裁定#46):Select 选中下标(三项)
    public int SizeIndex = 1;

    // D6 菜单/子菜单演示回显状态(裁定#47)
    public string MenuPicked = "none";

    // 波 3B/B5:文件拖放回显(app.MainWindow.FileDropped 接线演示)
    public string? DroppedFile;
}

internal static class Program
{
    static readonly string[] Sizes = { "small", "medium", "large" };   // D5 Select 三项

    private static int Main()
    {
        var state = new CounterState();
        App app = null!;   // 先声明后赋值:About/Menu 回调捕获 app(CS0165);回调仅在 Run 后的点击中触发
        app = App.Create("SharpUI Counter", 900, 960, () =>
        {
            ButtonElement menuBtn = null!;
            menuBtn = new ButtonElement("Menu", () => app.ShowMenu(menuBtn.Frame,
                new MenuItem("+10", () => { state.Count += 10; state.MenuPicked = "+10"; }),
                new MenuSeparator(),
                new MenuSub("More",
                    new MenuItem("+100", () => { state.Count += 100; state.MenuPicked = "+100"; }),
                    new MenuItem("Zero", () => { state.Count = 0; state.MenuPicked = "Zero"; }))));
            return Column(
                Ui.Text("Counter").Size(28).Bold().Color(0xE8E8EC),
                Ui.Text(state.Count.ToString()).Size(48).Color(0x7AA2F7),
                Row(
                    new ButtonElement("−", () => state.Count--),
                    Ui.Spacer(new SizeF(8f, 0f)),
                    new ButtonElement("+", () => state.Count++)
                ).Gap(0f),
                Row(
                    Ui.Checkbox("check", state.Checked, v => state.Checked = v).Tooltip("提示文本"),
                    Ui.Radio("a", "demo", state.RadioA, () => state.RadioA = true),
                    Ui.Radio("b", "demo", !state.RadioA, () => state.RadioA = false),
                    Ui.Slider(0f, 100f, state.SliderValue, v => state.SliderValue = v).Step(10f),
                    Ui.Text(state.SliderValue.ToString("F0")).Size(13).Color(0x9AA2B3)
                ).Gap(16f),
                Row(
                    new ButtonElement("About", () => app.ShowDialog("About",
                        p => p.Add(Ui.Text("SharpUI D5:Dialog / Tooltip / Menu / Select 浮层全链。").Size(14)),
                        new DialogButton("OK", () => { }) { IsDefault = true, IsCancel = true })),
                    menuBtn,
                    Ui.Select(Sizes, state.SizeIndex, i => state.SizeIndex = i),
                    Ui.Text($"size = {Sizes[state.SizeIndex]} | menu = {state.MenuPicked}").Size(13).Color(0x9AA2B3)
                ).Gap(12f),
                Ui.Scroll(
                    Enumerable.Range(1, 12).Select(i => (Element)Ui.Text($"line {i}").Size(14).Color(0x9AA2B3)).ToArray()
                ).Basis(96f).Fill(0x16161A).Radius(8f),
                Ui.VirtualList("ticks", 1000, 22f, static (i, row) =>
                    row.Add(Ui.Text($"tick {i:0000}").Size(13).Color(0x7AA2F7))).Basis(110f).Fill(0x101014).Radius(8f),
                Ui.Text("你好 SharpUI 😀").Size(15).Color(0x9AA2B3),
                Ui.Text(state.DroppedFile is { } df ? $"drop = {df}" : "drop a file onto the window").Size(13).Color(0x9AA2B3),
                Ui.TextField("type here… (Ctrl+Z undo / Ctrl+Y redo)").Key("edit").FocusOnStart(),
                Ui.Immediate("debug", ui =>
                {
                    ui.Text($"count = {state.Count}", 14, 0x9AA2B3);
                    if (ui.Button("Reset").Clicked()) state.Count = 0;
                })
            ).Center().Gap(12f).Padding(24f);
        });
        // 波 3B/B5:拖放接线演示(主窗应用面)。须在 Create 后订阅一次 —— render lambda 每帧执行,
        // 事件 += 在其中每帧追加新委托(无界泄漏,评审 Important#1)。
        app.MainWindow.FileDropped += path => state.DroppedFile = path;
        return app.Run();
    }
}
