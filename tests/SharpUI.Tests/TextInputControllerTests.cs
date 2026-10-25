using SharpUI;

namespace SharpUI.Tests;

public class TextInputControllerTests
{
    sealed class Recorder : IDisposable
    {
        public readonly List<string> Calls = new();
        public IntPtr Win = (IntPtr)1234;
        public TextInputController Make() => new(
            w => Calls.Add($"start:{w}"),
            w => Calls.Add($"stop:{w}"),
            (w, x, y, pw, ph) => Calls.Add($"rect:{x},{y},{pw},{ph}"));
        public void Dispose() { }
    }

    [Fact] public void GainFocus_StartsOnceThenSyncsRectEachFrame()
    {
        using var r = new Recorder();
        var c = r.Make();
        c.Update(r.Win, "tf:/edit", (10, 20, 2, 24));
        c.Update(r.Win, "tf:/edit", (12, 20, 2, 24));
        Assert.Equal(new[] { "start:1234", "rect:10,20,2,24", "rect:12,20,2,24" }, r.Calls);
    }

    [Fact] public void LoseFocus_StopsOnce()
    {
        using var r = new Recorder();
        var c = r.Make();
        c.Update(r.Win, "tf:/edit", (10, 20, 2, 24));
        c.Update(r.Win, null, null);
        c.Update(r.Win, null, null);
        Assert.Equal(new[] { "start:1234", "rect:10,20,2,24", "stop:1234" }, r.Calls);
    }

    [Fact] public void FocusedButNullRect_Stops()
    {
        using var r = new Recorder();
        var c = r.Make();
        c.Update(r.Win, "tf:/edit", (1, 2, 3, 4));
        c.Update(r.Win, "tf:/edit", null);
        Assert.Contains("stop:1234", r.Calls);
    }

    [Fact] public void IdleBeforeGain_NoCalls()
    {
        using var r = new Recorder();
        r.Make().Update(r.Win, null, null);
        Assert.Empty(r.Calls);
    }

    [Fact] public void Regain_AfterStop_StartsAgain()
    {
        using var r = new Recorder();
        var c = r.Make();
        c.Update(r.Win, "a", (0, 0, 1, 1));
        c.Update(r.Win, null, null);
        c.Update(r.Win, "a", (5, 6, 1, 1));
        Assert.Equal(2, r.Calls.Count(s => s.StartsWith("start")));
    }

    [Fact] public void WindowSwitch_StopsOldThenStartsNew()
    {
        using var r = new Recorder();
        var c = r.Make();
        IntPtr w2 = (IntPtr)5678;
        c.Update(r.Win, "a", (0, 0, 1, 1));
        c.Update(w2, "b", (0, 0, 1, 1));
        Assert.Equal(new[] { "start:1234", "rect:0,0,1,1", "stop:1234", "start:5678", "rect:0,0,1,1" }, r.Calls);
    }
}
