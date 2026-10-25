using SharpUI;

namespace SharpUI.Tests;

public class StateTests
{
    [Fact]
    public void Get_CreatesThenPersistsAcrossSweeps()
    {
        var store = new StateStore();
        store.Get<int>("a");          // 触碰帧 1
        store.Sweep();
        store.Get<int>("a");          // 触碰帧 2
        store.Sweep();
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Get_InitialValueUsedOnFirstAccessOnly()
    {
        var store = new StateStore();
        Assert.Equal(5, store.Get("n", 5));
        store.Sweep();
        store.Get("n", 100);          // 已存在,初值被忽略
        store.Sweep();
        Assert.Equal(5, store.Get("n", 0));
    }

    [Fact]
    public void ReferenceTypeState_MutatesAcrossFrames()
    {
        var store = new StateStore();
        store.Get("list", new List<int> { 1 });
        store.Sweep();
        store.Get<List<int>>("list").Add(2);
        store.Sweep();
        Assert.Equal(new[] { 1, 2 }, store.Get<List<int>>("list"));
    }

    [Fact]
    public void Sweep_RemovesUntouched_KeepsTouched()
    {
        var store = new StateStore();
        store.Get<int>("kept");
        store.Get<int>("dropped");
        store.Sweep();
        for (int i = 0; i < 301; i++) store.Tick();   // D25 宽限:清扫只清 >300 帧未触碰的键,推进帧越过宽限
        store.Get<int>("kept");       // 第二帧只触碰 kept(帧戳刷新)
        store.Sweep();                // dropped 距上次触碰 301 帧 → 清除
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Sweep_WithNoTouches_ClearsAll()
    {
        var store = new StateStore();
        store.Get<int>("x");
        store.Sweep();
        for (int i = 0; i < 301; i++) store.Tick();   // 越过宽限期后清扫,x 已 301 帧未触碰
        store.Sweep();                // 本帧零触碰
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Set_PersistsValueTypesAcrossSweeps()
    {
        var store = new StateStore();
        store.Set("count", 41);
        store.Sweep();
        Assert.Equal(41, store.Get("count", 0));
        store.Set("count", store.Get("count", 0) + 1);
        store.Sweep();
        Assert.Equal(42, store.Get("count", 0));
    }

    [Fact]
    public void Set_OverwritesAndTouchesExisting()
    {
        var store = new StateStore();
        store.Get<int>("n");
        store.Sweep();
        store.Set("n", 7);
        store.Sweep();
        Assert.Equal(7, store.Get("n", 0));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Sweep_WithinGrace_KeepsUntouchedEntries()   // D25 宽限:未触碰 ≤300 帧,Sweep 保留
    {
        var store = new StateStore();
        store.Get<int>("x");          // stamp @ frame 0
        store.Sweep();                // 宽限期内的清扫不再立即清除
        for (int i = 0; i < 300; i++) store.Tick();
        store.Sweep();                // 300 - 0 = 300,不 > 300 → 仍保留
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Sweep_AfterGrace_EvictsUntouchedEntries()   // 301 帧未触碰 → 清除(边界 = 严格大于)
    {
        var store = new StateStore();
        store.Get<int>("x");          // stamp @ frame 0
        store.Sweep();
        for (int i = 0; i < 301; i++) store.Tick();
        store.Sweep();                // 301 - 0 = 301 > 300 → 清除
        Assert.Equal(0, store.Count);
    }
}
