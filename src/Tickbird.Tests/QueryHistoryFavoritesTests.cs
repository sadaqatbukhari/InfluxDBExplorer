using Tickbird.Core.History;

namespace Tickbird.Tests;

public sealed class QueryHistoryFavoritesTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "tickbird-fav-" + Guid.NewGuid().ToString("N"));
    private string HistoryFile => Path.Combine(dir, "query-history.json");
    private DateTime now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private QueryHistory Create(int capacity = 1000, int retentionDays = 0, string? file = null) =>
        new(file ?? HistoryFile, capacity, retentionDays, () => now);

    private QueryHistoryEntry Entry(string query, int daysAgo = 0) => new()
    {
        RunAtUtc = now.AddDays(-daysAgo), ConnectionId = "c1", ConnectionName = "C1", Database = "telemetry",
        Query = query, Status = QueryRunStatus.Succeeded
    };

    private static string[] Ids(QueryHistory history, params string[] queries) =>
        history.Entries.Where(e => queries.Contains(e.Query)).Select(e => e.Id).ToArray();

    [Fact]
    public void Favorites_AreSaved_AndCounted()
    {
        var history = Create();
        history.Add(Entry("A"));
        history.Add(Entry("B"));
        Assert.Equal(0, history.FavoriteCount);

        Assert.Equal(1, history.SetFavorite(Ids(history, "A"), true));
        Assert.Equal(0, history.SetFavorite(Ids(history, "A"), true));      // already a favorite
        Assert.Equal(1, history.FavoriteCount);
        Assert.True(Create().Entries.Single(e => e.Query == "A").IsFavorite);   // written to disk

        Assert.Equal(1, history.SetFavorite(Ids(history, "A"), false));
        Assert.Equal(0, history.FavoriteCount);
    }

    [Fact]
    public void RunningAFavoriteAgain_KeepsItAFavorite()
    {
        var history = Create();
        history.Add(Entry("A"));
        history.SetFavorite(Ids(history, "A"), true);
        history.Add(Entry("B"));
        history.Add(Entry("A"));

        Assert.Equal(new[] { "A", "B" }, history.Entries.Select(e => e.Query));
        Assert.True(history.Entries[0].IsFavorite);
        Assert.False(history.Entries[1].IsFavorite);
    }

    [Fact]
    public void Favorites_NeverExpire()
    {
        var history = Create();
        history.Add(Entry("old favorite", 100));
        history.Add(Entry("old plain", 100));
        history.Add(Entry("recent", 1));
        history.SetFavorite(Ids(history, "old favorite"), true);

        history.SetRetention(7);

        Assert.Equal(new[] { "recent", "old favorite" }, history.Entries.Select(e => e.Query));
        Assert.Equal(new[] { "recent", "old favorite" }, Create(retentionDays: 7).Entries.Select(e => e.Query));
    }

    [Fact]
    public void WhenFull_TheOldestQueriesThatAreNotFavoritesGoFirst()
    {
        var history = Create(capacity: 3);
        history.Add(Entry("first"));
        history.SetFavorite(Ids(history, "first"), true);
        history.Add(Entry("second"));
        history.Add(Entry("third"));
        history.Add(Entry("fourth"));

        Assert.Equal(new[] { "fourth", "third", "first" }, history.Entries.Select(e => e.Query));
    }

    [Fact]
    public void WhenEverythingIsAFavorite_TheListGrowsInsteadOfDroppingANewQuery()
    {
        var history = Create(capacity: 2);
        foreach (var q in new[] { "a", "b", "c" })
        {
            history.Add(Entry(q));
            history.SetFavorite(Ids(history, q), true);
        }
        Assert.Equal(3, history.Entries.Count);
    }

    [Fact]
    public void Remove_CanKeepFavorites()
    {
        var history = Create();
        history.Add(Entry("A"));
        history.Add(Entry("B"));
        history.SetFavorite(Ids(history, "A"), true);
        var both = history.Entries.Select(e => e.Id).ToList();

        history.Remove(both, includeFavorites: false);
        Assert.Equal(new[] { "A" }, history.Entries.Select(e => e.Query));

        history.Remove(both, includeFavorites: true);
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void Clear_KeepsFavoritesUnlessAsked()
    {
        var history = Create();
        history.Add(Entry("A"));
        history.Add(Entry("B"));
        history.Add(Entry("C"));
        history.SetFavorite(Ids(history, "B"), true);

        history.Clear(includeFavorites: false);
        Assert.Equal(new[] { "B" }, history.Entries.Select(e => e.Query));
        Assert.Equal(new[] { "B" }, Create().Entries.Select(e => e.Query));

        history.Clear();   // the default removes everything, as before
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void Search_CanShowOnlyFavorites()
    {
        var history = Create();
        history.Add(Entry("SELECT alpha"));
        history.Add(Entry("SELECT beta"));
        history.SetFavorite(Ids(history, "SELECT beta"), true);

        Assert.Equal(2, history.Search("select").Count);
        Assert.Equal(new[] { "SELECT beta" }, history.Search("select", favoritesOnly: true).Select(e => e.Query));
        Assert.Empty(history.Search("alpha", favoritesOnly: true));
    }

    [Fact]
    public void Export_AndImport_KeepTheFavoriteMark()
    {
        var source = Create();
        source.Add(Entry("A", 2));
        source.Add(Entry("B", 1));
        source.SetFavorite(Ids(source, "A"), true);
        var file = Path.Combine(dir, "e.json");
        source.Export(file);

        var target = Create(file: Path.Combine(dir, "other.json"));
        target.Import(file);

        Assert.True(target.Entries.Single(e => e.Query == "A").IsFavorite);
        Assert.False(target.Entries.Single(e => e.Query == "B").IsFavorite);
    }

    [Fact]
    public void Import_MarksAnExistingQueryAsFavorite_WhenTheFileHasItAsOne()
    {
        var history = Create();
        history.Add(Entry("A", 0));        // our run is the newer one

        var other = Create(file: Path.Combine(dir, "other.json"));
        other.Add(Entry("A", 5));
        other.SetFavorite(Ids(other, "A"), true);
        var file = Path.Combine(dir, "e.json");
        other.Export(file);

        var result = history.Import(file);

        Assert.Equal(1, result.Updated);
        Assert.True(history.Entries.Single().IsFavorite);
        Assert.Equal(now, history.Entries.Single().RunAtUtc);     // the newer run was kept
    }

    [Fact]
    public void Import_KeepsFavoritesThatWouldAlreadyBeExpired()
    {
        var other = Create(file: Path.Combine(dir, "other.json"));
        other.Add(Entry("old favorite", 90));
        other.Add(Entry("old plain", 90));
        other.SetFavorite(Ids(other, "old favorite"), true);
        var file = Path.Combine(dir, "e.json");
        other.Export(file);

        var history = Create(retentionDays: 30);
        var result = history.Import(file);

        Assert.Equal(1, result.Added);
        Assert.Equal(new[] { "old favorite" }, history.Entries.Select(e => e.Query));
    }

    [Fact]
    public void OldFilesWithoutTheFavoriteField_LoadAsNotFavorite()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(HistoryFile, "[{\"Query\":\"SELECT 1\",\"RunAtUtc\":\"2026-10-24T10:00:00Z\",\"ConnectionId\":\"c1\"}]");
        var history = Create();
        Assert.Single(history.Entries);
        Assert.False(history.Entries[0].IsFavorite);
    }
}
