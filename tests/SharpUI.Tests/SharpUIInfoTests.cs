namespace SharpUI.Tests;

public class SharpUIInfoTests
{
    [Fact]
    public void AssemblyNameIsSharpUI()
    {
        Assert.Equal("SharpUI", typeof(SharpUIInfo).Assembly.GetName().Name);
        Assert.Equal("SharpUI", SharpUIInfo.Name);
        Assert.StartsWith("0.", SharpUIInfo.Version);
    }
}
