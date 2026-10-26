using Tickbird.Core.Data;
using Tickbird.Core.Settings;

namespace Tickbird.Tests;

public class SharedModelTests
{
    [Theory]
    [InlineData("1h", true)]
    [InlineData("30m", true)]
    [InlineData("15s", true)]
    [InlineData("2w", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("h", false)]
    [InlineData("10x", false)]
    public void IsTimeIntervalValid(string? value, bool expected) =>
        Assert.Equal(expected, InfluxDbHelper.IsTimeIntervalValid(value!));

    [Fact]
    public void NewQuery_AlwaysProducesUniqueIds() =>
        Assert.NotEqual(TabIdentity.NewQuery(), TabIdentity.NewQuery());

    [Fact]
    public void ShowQueries_IsKeyedByConnectionId_NotName()
    {
        Assert.Equal(TabIdentity.ShowQueries("id-1"), TabIdentity.ShowQueries("id-1"));
        Assert.NotEqual(TabIdentity.ShowQueries("id-1"), TabIdentity.ShowQueries("id-2"));
    }

    [Fact]
    public void ShowQueries_RequiresConnectionId() =>
        Assert.Throws<ArgumentException>(() => TabIdentity.ShowQueries(" "));
}
