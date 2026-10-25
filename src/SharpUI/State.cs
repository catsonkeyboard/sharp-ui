namespace SharpUI;

/// <summary>按稳定 id 存取的跨帧状态(规格书 D10)。每帧 Get/Set 视为触碰并记录帧号;Tick 每帧 +1(App.Run);
/// Sweep 只清除超过 <see cref="GraceFrames"/> 帧未触碰的键(D25 宽限:虚拟化滚出的控件状态保留约 5 秒,60fps × 300)。
/// 注意:值类型 T 返回的是装箱副本,变更不会持久化;值类型状态请在 M2b 的 Set API 落地前持引用类型包装。</summary>
public sealed class StateStore
{
    internal const int GraceFrames = 300;   // 宽限帧数:严格大于才清除(301 帧未触碰即失联)

    readonly Dictionary<string, (object? Value, int Frame)> _entries = new();
    int _frame;
    List<string>? _stale;

    public int Count => _entries.Count;

    /// <summary>帧计数 +1;App.Run 每帧调用一次(与 Sweep 相邻,先 Sweep 后 Tick:清扫以触碰发生帧为准)。</summary>
    internal void Tick() => _frame++;

    public T Get<T>(string id) where T : new()
    {
        if (_entries.TryGetValue(id, out var e))
        {
            if (e.Value is not null)
            {
                if (e.Frame != _frame) _entries[id] = (e.Value, _frame);
                return (T)e.Value;
            }
        }
        T created = new();
        _entries[id] = (created, _frame);
        return created;
    }

    public T Get<T>(string id, T initial)
    {
        if (_entries.TryGetValue(id, out var e))
        {
            if (e.Value is not null)
            {
                if (e.Frame != _frame) _entries[id] = (e.Value, _frame);
                return (T)e.Value;
            }
        }
        _entries[id] = (initial, _frame);
        return initial;
    }

    /// <summary>写入并触碰(值类型状态的持久化路径;规格书 M2b)。值总是写入,帧戳随写刷新。</summary>
    public void Set<T>(string id, T value) => _entries[id] = (value, _frame);

    public void Sweep()
    {
        _stale?.Clear();
        foreach (KeyValuePair<string, (object? Value, int Frame)> kv in _entries)
            if (_frame - kv.Value.Frame > GraceFrames)
                (_stale ??= new List<string>()).Add(kv.Key);
        if (_stale is { Count: > 0 })
        {
            foreach (string key in _stale) _entries.Remove(key);
            _stale.Clear();
        }
    }
}
