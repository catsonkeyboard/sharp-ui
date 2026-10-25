namespace SharpUI;

public enum Easing { Linear, EaseOutQuad, EaseOutCubic }

public static class Anim
{
    sealed class Tween { public float From, To; public double Start; }
    static readonly Dictionary<string, Tween> _tweens = new();
    const int TweenCapacity = 4096;

    /// <summary>测试注入;null 时用 App.Time(D23)。</summary>
    internal static Func<double>? TimeSource;

    public static double Now => TimeSource?.Invoke() ?? App.Time;

    /// <summary>取补间当前值;目标变化时从当前值重启(D23)。</summary>
    public static float Get(string id, float target, float duration = 0.2f, Easing easing = Easing.EaseOutCubic)
    {
        double now = Now;
        if (!_tweens.TryGetValue(id, out Tween? t))
        {
            _tweens[id] = t = new Tween { From = target, To = target, Start = now };
            if (_tweens.Count > TweenCapacity)
                _tweens.Clear();   // D23 粗粒度逐出:超上限全清(同 shaped 缓存策略,下次访问自然重建)
            return target;
        }
        if (MathF.Abs(t.To - target) > 0.0001f)
        {
            t.From = ValueAt(t, now, duration, easing);
            t.To = target;
            t.Start = now;
        }
        return ValueAt(t, now, duration, easing);
    }

    static float ValueAt(Tween t, double now, float duration, Easing easing)
    {
        if (duration <= 0f) return t.To;
        float p = (float)Math.Clamp((now - t.Start) / duration, 0.0, 1.0);
        return t.From + (t.To - t.From) * Apply(easing, p);
    }

    public static bool Blink(float period = 1.06f, float duty = 0.5f) =>
        (Now % period) / period < duty;

    public static float Apply(Easing e, float t) => e switch
    {
        Easing.Linear => t,
        Easing.EaseOutQuad => 1f - (1f - t) * (1f - t),
        Easing.EaseOutCubic => 1f - (1f - t) * (1f - t) * (1f - t),
        _ => t,
    };
}
