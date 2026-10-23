using Tickbird.Core.History;

namespace Tickbird.Tests;

public sealed class QueryHistoryTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "tickbird-history-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(dir, "query-history.json");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static QueryHistoryEntry Entry(string query, string connection = "c1", string database = "telemetry",
        QueryRunStatus status = QueryRunStatus.Succeeded) => new()
    {
        RunAtUtc = DateTime.UtcNow, ConnectionId = connection, ConnectionName = connection.ToUpperInvariant(),
        Database = database, Query = query, Status = status, Rows = 3, DurationMs = 12
    };

    [Fact]
    public void NewestFirst_AndSavedToDisk()
    {
        var history = new QueryHistory(FilePath);
        history.Add(Entry("SELECT 1"));
        history.Add(Entry("SELECT 2", status: QueryRunStatus.Cancelled));
        Assert.Equal(new[] { "SELECT 2", "SELECT 1" }, history.Entries.Select(e => e.Query));

        var reloaded = new QueryHistory(FilePath);
        Assert.Equal(new[] { "SELECT 2", "SELECT 1" }, reloaded.Entries.Select(e => e.Query));
        Assert.Equal(QueryRunStatus.Cancelled, reloaded.Entries[0].Status);
    }

    [Fact]
    public void RunningTheSameQueryAgain_MovesItToTheTop()
    {
        var history = new QueryHistory(FilePath);
        history.Add(Entry("SELECT 1"));
        history.Add(Entry("SELECT 2"));
        history.Add(Entry("SELECT 1 "));
        Assert.Equal(new[] { "SELECT 1 ", "SELECT 2" }, history.Entries.Select(e => e.Query));
        // ...but the same text on another database is a different entry.
        history.Add(Entry("SELECT 2", database: "other"));
        Assert.Equal(3, history.Entries.Count);
    }

    [Fact]
    public void KeepsOnlyTheMostRecent()
    {
        var history = new QueryHistory(null, capacity: 3);
        for (var i = 0; i < 5; i++) history.Add(Entry($"SELECT {i}"));
        Assert.Equal(new[] { "SELECT 4", "SELECT 3", "SELECT 2" }, history.Entries.Select(e => e.Query));
    }

    [Fact]
    public void Search_MatchesEveryWord_InQueryConnectionOrDatabase()
    {
        var history = new QueryHistory(null);
        history.Add(Entry("SELECT lat, lon FROM gps", "c1"));
        history.Add(Entry("SELECT mean(speed) FROM gps", "c2", "fleet"));
        history.Add(Entry("SHOW MEASUREMENTS", "c2"));
        Assert.Equal(2, history.Search("gps").Count);
        Assert.Single(history.Search("gps fleet"));
        Assert.Single(history.Search("FROM gps", connectionId: "c1"));
        Assert.Equal(3, history.Search("").Count);
        Assert.Equal(2, history.Search("C2").Count);
    }

    [Fact]
    public void RemoveAndClear_RaiseChanged()
    {
        var history = new QueryHistory(FilePath);
        var changes = 0;
        history.Changed += () => changes++;
        history.Add(Entry("SELECT 1"));
        history.Add(Entry("SELECT 2"));
        history.Remove(new[] { history.Entries[0].Id });
        Assert.Equal(new[] { "SELECT 1" }, history.Entries.Select(e => e.Query));
        history.Clear();
        Assert.Empty(history.Entries);
        Assert.Equal(4, changes);
        Assert.Empty(new QueryHistory(FilePath).Entries);
    }

    [Fact]
    public void BlankQueries_AndCorruptFiles_AreIgnored()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, "{ not json");
        var history = new QueryHistory(FilePath);
        Assert.Empty(history.Entries);
        history.Add(Entry("   "));
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void OneLine_FlattensMultiLineQueries() =>
        Assert.Equal("SELECT * FROM gps WHERE time > now() - 1h", Entry("SELECT *\r\n  FROM gps\n\tWHERE time > now() - 1h").OneLine);
}
