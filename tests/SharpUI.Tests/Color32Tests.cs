namespace SharpUI.Tests;

public class Color32Tests
{
    [Fact]
    public void FromRgb_SplitsChannels()
    {
        Color32 c = Color32.FromRgb(0x7AA2F7);
        Assert.Equal(0x7A, c.R);
        Assert.Equal(0xA2, c.G);
        Assert.Equal(0xF7, c.B);
        Assert.Equal(255, c.A);
    }

    [Fact]
    public void Premultiplied_ScalesByAlpha()
    {
        Color32 c = new(255, 100, 0, 128);
        (byte r, byte g, byte b) = c.Premultiplied;
        Assert.Equal(128, r);
        Assert.Equal(50, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void WithAlpha_KeepsChannels()
    {
        Color32 c = Color32.FromRgb(0x112233).WithAlpha(10);
        Assert.Equal((17, 34, 51, 10), (c.R, c.G, c.B, c.A));
    }
}
