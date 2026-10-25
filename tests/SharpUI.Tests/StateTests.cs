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
        store.Get<int>("kept");       // 第二帧只触碰 kept
        store.Sweep();
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Sweep_WithNoTouches_ClearsAll()
    {
        var store = new StateStore();
        store.Get<int>("x");
        store.Sweep();
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
}
