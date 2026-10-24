using SharpUI;
using static SharpUI.Ui;

internal static class Program
{
    private static int Main() => App.Create("SharpUI Counter", 480, 320, () => Column(
            Text("Counter").Size(28).Bold().Color(0xE8E8EC),
            Text("M1").Size(16).Color(0x9AA2B3)
        ).Center().Gap(8f).Padding(24f)).Run();
}
