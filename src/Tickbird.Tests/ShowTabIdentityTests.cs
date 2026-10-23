using Tickbird.Core.Settings;

namespace Tickbird.Tests;

/// <summary>Every "Show ..." view gets one tab per server / database / measurement.</summary>
public class ShowTabIdentityTests
{
    [Fact]
    public void SameTarget_GivesSameId()
    {
        Assert.Equal(TabIdentity.Show("Users", "c1"), TabIdentity.Show("Users", "c1"));
        Assert.Equal(TabIdentity.Show("continuous_queries", "c1", "db"), TabIdentity.Show("continuous_queries", "c1", "db"));
        Assert.Equal(TabIdentity.Show("field_keys", "c1", "db", "cpu"), TabIdentity.Show("field_keys", "c1", "db", "cpu"));
    }

    [Fact]
    public void DifferentServer_View_Database_OrMeasurement_GivesDifferentIds()
    {
        var ids = new[]
        {
            TabIdentity.Show("Users", "c1"),
            TabIdentity.Show("Users", "c2"),
            TabIdentity.Show("Diagnostics", "c1"),
            TabIdentity.Show("RetentionPolicy", "c1"),
            TabIdentity.Show("continuous_queries", "c1", "db1"),
            TabIdentity.Show("continuous_queries", "c1", "db2"),
            TabIdentity.Show("field_keys", "c1", "db1", "cpu"),
            TabIdentity.Show("field_keys", "c1", "db1", "mem"),
            TabIdentity.Show("field_keys", "c1", "db2", "cpu"),
            TabIdentity.Show("tag_keys", "c1", "db1", "cpu"),
            TabIdentity.ShowQueries("c1"),
        };
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void NamesContainingSeparatorsDoNotCollide() =>
        Assert.NotEqual(TabIdentity.Show("field_keys", "c1", "a:b", "c"), TabIdentity.Show("field_keys", "c1", "a", "b:c"));

    [Fact]
    public void RequiresViewConnectionAndDatabaseForMeasurement()
    {
        Assert.Throws<ArgumentException>(() => TabIdentity.Show(" ", "c1"));
        Assert.Throws<ArgumentException>(() => TabIdentity.Show("Users", ""));
        Assert.Throws<ArgumentException>(() => TabIdentity.Show("tag_keys", "c1", null, "cpu"));
    }
}
