using SharpUI;

namespace SharpUI.Tests;

[Collection("interaction")]   // 新静态写入方(Anim._tweens/TimeSource),与其余静态用户串行(惯例)
public class AnimTests : IDisposable
{
    double _t;
    public AnimTests() => Anim.TimeSource = () => _t;
    public void Dispose() => Anim.TimeSource = null;

    [Fact]
    public void Get_ReturnsTargetImmediatelyOnFirstCall()
    {
        _t = 0;
        Assert.Equal(5f, Anim.Get("a", 5f));
    }

    [Fact]
    public void Get_TweensTowardNewTarget()
    {
        _t = 0; Anim.Get("b", 0f);
        _t = 0.1; Assert.Equal(0f, Anim.Get("b", 10f, 0.2f), 3);   // 改目标瞬间从当前值(0)重启(D23)
        _t = 0.2; float mid = Anim.Get("b", 10f, 0.2f);      // 50% EaseOutCubic = 1-(0.5)³=0.875 → 8.75
        Assert.InRange(mid, 8.5f, 9.0f);
        _t = 0.3; Assert.Equal(10f, Anim.Get("b", 10f, 0.2f), 3);
    }

    [Fact]
    public void TargetChange_RestartsFromCurrent()
    {
        _t = 0; Anim.Get("c", 0f);
        _t = 0.1; Anim.Get("c", 10f, 0.2f);
        _t = 0.2; float mid = Anim.Get("c", 10f, 0.2f);      // ~8.75
        float after = Anim.Get("c", 20f, 0.2f);              // 同时刻改目标:从 8.75 出发 0%(EaseOutCubic 起点即原值)
        Assert.Equal(mid, after, 2);
    }

    [Fact]
    public void Blink_IsStatelessClockMath()
    {
        _t = 0.0; Assert.True(Anim.Blink());
        _t = 0.8; Assert.False(Anim.Blink());
        _t = 1.06; Assert.True(Anim.Blink());
    }

    [Fact]
    public void Easing_Monotonic()
    {
        for (float t = 0; t <= 1f; t += 0.25f)
            Assert.InRange(Anim.Apply(Easing.EaseOutCubic, t), 0f, 1f);
        Assert.True(Anim.Apply(Easing.EaseOutQuad, 0.5f) >= 0.5f);
    }
}
