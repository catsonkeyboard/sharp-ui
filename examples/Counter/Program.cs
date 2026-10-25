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
                ).Gap(0f)
            ).Center().Gap(12f).Padding(24f)).Run();
    }
}
