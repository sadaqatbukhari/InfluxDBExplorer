using Tickbird.Core.History;
using Tickbird.Core.Settings;

namespace Tickbird.Tests;

public sealed class QueryHistoryExpiryAndTransferTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "tickbird-history-x-" + Guid.NewGuid().ToString("N"));
    private string HistoryFile => Path.Combine(dir, "query-history.json");
    private DateTime now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private QueryHistory Create(int retentionDays = 0, string? file = null) =>
        new(file ?? HistoryFile, retentionDays: retentionDays, clock: () => now);

    private QueryHistoryEntry Entry(string query, int daysAgo, string connection = "c1", string database = "telemetry") => new()
    {
        RunAtUtc = now.AddDays(-daysAgo), ConnectionId = connection, ConnectionName = connection.ToUpperInvariant(),
        Database = database, Query = query, Status = QueryRunStatus.Succeeded, Rows = 3, DurationMs = 12
    };

    [Fact]
    public void ByDefault_EntriesNeverExpire()
    {
        Assert.Equal(0, new TickbirdSettings().QueryHistoryRetentionDays);
        var history = Create();
        history.Add(Entry("old", daysAgo: 4000));
        history.Add(Entry("new", daysAgo: 0));
        Assert.Equal(0, history.Prune());
        Assert.Equal(2, history.Entries.Count);
    }

    [Fact]
    public void Prune_RemovesEntriesOlderThanTheRetention()
    {
        var history = Create();
        history.Add(Entry("ancient", 40));
        history.Add(Entry("last week", 6));
        history.Add(Entry("today", 0));
        history.SetRetention(7);
        Assert.Equal(new[] { "today", "last week" }, history.Entries.Select(e => e.Query));

        now = now.AddDays(2);   // "last week" is now 8 days old
        Assert.Equal(1, history.Prune());
        Assert.Equal(new[] { "today" }, history.Entries.Select(e => e.Query));
    }

    [Fact]
    public void Expiry_IsAppliedOnLoad_AndSaved()
    {
        var first = Create();
        first.Add(Entry("old", 30));
        first.Add(Entry("recent", 1));

        var reloaded = Create(retentionDays: 7);
        Assert.Equal(new[] { "recent" }, reloaded.Entries.Select(e => e.Query));
        Assert.Equal(new[] { "recent" }, Create().Entries.Select(e => e.Query));   // the removal was written to disk
    }

    [Fact]
    public void SettingRetentionBackToAlways_DoesNotBringEntriesBack_ButKeepsTheRest()
    {
        var history = Create(retentionDays: 7);
        history.Add(Entry("recent", 1));
        history.SetRetention(0);
        now = now.AddDays(500);
        Assert.Equal(0, history.Prune());
        Assert.Single(history.Entries);
    }

    [Fact]
    public void Export_AndImport_RoundTripBetweenHistories()
    {
        var source = Create();
        source.Add(Entry("SELECT 1", 3));
        source.Add(Entry("SELECT 2", 1));
        var exported = Path.Combine(dir, "export.json");
        Assert.Equal(2, source.Export(exported));

        var target = Create(file: Path.Combine(dir, "other", "query-history.json"));
        var result = target.Import(exported);

        Assert.Equal(new QueryHistoryImportResult(2, 0, 0), result);
        Assert.Equal(new[] { "SELECT 2", "SELECT 1" }, target.Entries.Select(e => e.Query));
        Assert.Equal(source.Entries[0].Id, target.Entries[0].Id);
        Assert.Equal(new[] { "SELECT 2", "SELECT 1" }, Create(file: Path.Combine(dir, "other", "query-history.json")).Entries.Select(e => e.Query));
    }

    [Fact]
    public void Export_CanSaveJustTheSelection()
    {
        var history = Create();
        history.Add(Entry("A", 2));
        history.Add(Entry("B", 1));
        var file = Path.Combine(dir, "some.json");
        Assert.Equal(1, history.Export(file, history.Entries.Where(e => e.Query == "A")));

        var other = Create(file: Path.Combine(dir, "o.json"));
        other.Import(file);
        Assert.Equal(new[] { "A" }, other.Entries.Select(e => e.Query));
    }

    [Fact]
    public void Import_MergesWithoutDuplicates_KeepingTheNewerRun()
    {
        var history = Create();
        history.Add(Entry("SELECT 1", 5));
        history.Add(Entry("SELECT 2", 1));

        var other = Create(file: Path.Combine(dir, "other.json"));
        other.Add(Entry("SELECT 1", 2));    // same query, more recent than ours
        other.Add(Entry("SELECT 2", 9));    // same query, older than ours
        other.Add(Entry("SELECT 3", 4));    // new
        var file = Path.Combine(dir, "e.json");
        other.Export(file);

        var result = history.Import(file);

        Assert.Equal(new QueryHistoryImportResult(1, 1, 1), result);
        Assert.Equal(new[] { "SELECT 2", "SELECT 1", "SELECT 3" }, history.Entries.Select(e => e.Query));
        Assert.Equal(now.AddDays(-2), history.Entries.Single(e => e.Query == "SELECT 1").RunAtUtc);
        Assert.Equal(0, history.Import(file).Added);   // importing twice changes nothing
    }

    [Fact]
    public void Import_SkipsEntriesThatWouldAlreadyBeExpired()
    {
        var other = Create(file: Path.Combine(dir, "other.json"));
        other.Add(Entry("old", 60));
        other.Add(Entry("fresh", 2));
        var file = Path.Combine(dir, "e.json");
        other.Export(file);

        var history = Create(retentionDays: 30);
        var result = history.Import(file);

        Assert.Equal(new QueryHistoryImportResult(1, 0, 1), result);
        Assert.Equal(new[] { "fresh" }, history.Entries.Select(e => e.Query));
    }

    [Fact]
    public void Import_AcceptsABareArray_AndRejectsOtherFiles()
    {
        var bare = Path.Combine(dir, "bare.json");
        Directory.CreateDirectory(dir);
        File.WriteAllText(bare, "[{\"Query\":\"SELECT 9\",\"RunAtUtc\":\"2026-10-22T10:00:00Z\",\"ConnectionId\":\"c1\"}]");
        var history = Create();
        Assert.Equal(1, history.Import(bare).Added);
        Assert.Equal("SELECT 9", history.Entries[0].Query);

        var notHistory = Path.Combine(dir, "bad.json");
        File.WriteAllText(notHistory, "this is not json");
        Assert.Throws<InvalidDataException>(() => history.Import(notHistory));
        File.WriteAllText(notHistory, "{\"hello\":1}");
        Assert.Throws<InvalidDataException>(() => history.Import(notHistory));
        Assert.Single(history.Entries);
    }

    [Fact]
    public void Import_HonoursTheCapacity_KeepingTheNewest()
    {
        var other = new QueryHistory(Path.Combine(dir, "big.json"), capacity: 50, clock: () => now);
        for (var i = 0; i < 5; i++) other.Add(Entry("q" + i, i));
        var file = Path.Combine(dir, "e.json");
        other.Export(file);

        var small = new QueryHistory(Path.Combine(dir, "small.json"), capacity: 3, clock: () => now);
        small.Import(file);
        Assert.Equal(new[] { "q0", "q1", "q2" }, small.Entries.Select(e => e.Query));
    }
}
