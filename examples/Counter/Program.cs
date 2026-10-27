using SharpUI;
using static SharpUI.Ui;

internal sealed class CounterState
{
    public int Count;

    // D4 控件段状态(裁定#44;回调式 C-2:状态存应用侧,控件不藏状态)
    public bool Checked;
    public bool RadioA = true;   // "a"/"b" 同组互斥=应用回调职责
    public float SliderValue = 50f;
}

internal static class Program
{
    private static int Main()
    {
        var state = new CounterState();
        return App.Create("SharpUI Counter", 900, 960, () => Column(
                Ui.Text("Counter").Size(28).Bold().Color(0xE8E8EC),
                Ui.Text(state.Count.ToString()).Size(48).Color(0x7AA2F7),
                Row(
                    new ButtonElement("−", () => state.Count--),
                    Ui.Spacer(new SizeF(8f, 0f)),
                    new ButtonElement("+", () => state.Count++)
                ).Gap(0f),
                Row(
                    Ui.Checkbox("check", state.Checked, v => state.Checked = v),
                    Ui.Radio("a", "demo", state.RadioA, () => state.RadioA = true),
                    Ui.Radio("b", "demo", !state.RadioA, () => state.RadioA = false),
                    Ui.Slider(0f, 100f, state.SliderValue, v => state.SliderValue = v).Step(10f),
                    Ui.Text(state.SliderValue.ToString("F0")).Size(13).Color(0x9AA2B3)
                ).Gap(16f),
                Ui.Scroll(
                    Enumerable.Range(1, 12).Select(i => (Element)Ui.Text($"line {i}").Size(14).Color(0x9AA2B3)).ToArray()
                ).Basis(96f).Fill(0x16161A).Radius(8f),
                Ui.VirtualList("ticks", 1000, 22f, static (i, row) =>
                    row.Add(Ui.Text($"tick {i:0000}").Size(13).Color(0x7AA2F7))).Basis(110f).Fill(0x101014).Radius(8f),
                Ui.Text("你好 SharpUI 😀").Size(15).Color(0x9AA2B3),
                Ui.TextField("type here…").Key("edit").FocusOnStart(),
                Ui.Immediate("debug", ui =>
                {
                    ui.Text($"count = {state.Count}", 14, 0x9AA2B3);
                    if (ui.Button("Reset").Clicked()) state.Count = 0;
                })
            ).Center().Gap(12f).Padding(24f)).Run();
    }
}
