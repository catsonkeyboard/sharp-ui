using SharpUI;
using static SharpUI.Ui;

internal sealed class CounterState
{
    public int Count;
}

internal static class Program
{
    private static int Main()
    {
        var state = new CounterState();
        return App.Create("SharpUI Counter", 480, 320, () => Column(
                Text("Counter").Size(28).Bold().Color(0xE8E8EC),
                Text(state.Count.ToString()).Size(48).Color(0x7AA2F7),
                Row(
                    new ButtonElement("−", () => state.Count--),
                    Ui.Spacer(new SizeF(8f, 0f)),
                    new ButtonElement("+", () => state.Count++)
                ).Gap(0f),
                Ui.Scroll(
                    Enumerable.Range(1, 12).Select(i => (Element)Ui.Text($"line {i}").Size(14).Color(0x9AA2B3)).ToArray()
                ).Basis(96f).Fill(0x16161A).Radius(8f),
                Ui.VirtualList("ticks", 1000, 22f, static (i, row) =>
                    row.Add(Ui.Text($"tick {i:0000}").Size(13).Color(0x7AA2F7))).Basis(110f).Fill(0x101014).Radius(8f),
                Ui.Text("你好 SharpUI 😀").Size(15).Color(0x9AA2B3),
                Ui.TextField("type here…").Key("edit"),
                Ui.Immediate("debug", ui =>
                {
                    ui.Text($"count = {state.Count}", 14, 0x9AA2B3);
                    if (ui.Button("Reset").Clicked()) state.Count = 0;
                })
            ).Center().Gap(12f).Padding(24f)).Run();
    }
}
