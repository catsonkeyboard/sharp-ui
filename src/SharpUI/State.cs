namespace SharpUI;

/// <summary>按稳定 id 存取的跨帧状态(规格书 D10)。每帧 Get 视为触碰;Sweep 移除失联键。
/// 注意:值类型 T 返回的是装箱副本,变更不会持久化;值类型状态请在 M2b 的 Set API 落地前持引用类型包装。</summary>
public sealed class StateStore
{
    readonly Dictionary<string, object?> _values = new();
    readonly HashSet<string> _touched = new();
    List<string>? _stale;

    public int Count => _values.Count;

    public T Get<T>(string id) where T : new()
    {
        _touched.Add(id);
        if (_values.TryGetValue(id, out object? v) && v is not null) return (T)v;
        T created = new();
        _values[id] = created;
        return created;
    }

    public T Get<T>(string id, T initial)
    {
        _touched.Add(id);
        if (_values.TryGetValue(id, out object? v) && v is not null) return (T)v;
        _values[id] = initial;
        return initial;
    }

    public void Sweep()
    {
        _stale?.Clear();
        foreach (KeyValuePair<string, object?> kv in _values)
            if (!_touched.Contains(kv.Key))
                (_stale ??= new List<string>()).Add(kv.Key);
        if (_stale is { Count: > 0 })
        {
            foreach (string key in _stale) _values.Remove(key);
            _stale.Clear();
        }
        _touched.Clear();
    }
}
