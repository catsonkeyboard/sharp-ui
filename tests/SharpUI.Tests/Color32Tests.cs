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

    [Fact]
    public void Lerp_EndpointsExact()
    {
        Color32 a = new(10, 20, 30, 40), b = new(110, 220, 230, 240);
        Assert.Equal(a, Color32.Lerp(a, b, 0f));
        Assert.Equal(b, Color32.Lerp(a, b, 1f));
    }

    [Fact]
    public void Lerp_MidpointAveragesChannels()
    {
        Color32 m = Color32.Lerp(new Color32(0, 100, 200, 0), new Color32(100, 200, 100, 200), 0.5f);
        Assert.Equal((50, 150, 150, 100), (m.R, m.G, m.B, m.A));
    }
}
