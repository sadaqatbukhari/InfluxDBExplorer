using Newtonsoft.Json;

namespace Tickbird.Core.History;

/// <summary>How a query run ended.</summary>
public enum QueryRunStatus
{
    Succeeded,
    Failed,
    Cancelled,
    TimedOut
}

/// <summary>One query run from the editor.</summary>
public sealed class QueryHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime RunAtUtc { get; set; }
    public string? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public string? Database { get; set; }
    public string Query { get; set; } = "";
    public double DurationMs { get; set; }
    public int Rows { get; set; }
    public QueryRunStatus Status { get; set; }
    public string? Error { get; set; }

    /// <summary>The query on one line, for lists.</summary>
    [JsonIgnore]
    public string OneLine => string.Join(" ", Query.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())).Trim();
}

/// <summary>What an import did.</summary>
public sealed record QueryHistoryImportResult(int Added, int Updated, int Skipped);

/// <summary>
/// The most recent queries run from the editor, newest first, saved as JSON next to the settings file.
/// Running the same query again on the same connection and database moves it to the top instead of adding a copy.
/// Entries can expire after a number of days (0 keeps them always), and the history can be exported and imported.
/// </summary>
public sealed class QueryHistory
{
    public const int DefaultCapacity = 1000;
    public const string ExportFormat = "tickbird-query-history";
    private readonly string? path;
    private readonly Func<DateTime> utcNow;
    private readonly List<QueryHistoryEntry> entries;

    /// <param name="retentionDays">Entries older than this many days are removed; 0 keeps them always.</param>
    /// <param name="clock">The current UTC time (replaceable for tests).</param>
    public QueryHistory(string? path, int capacity = DefaultCapacity, int retentionDays = 0, Func<DateTime>? clock = null)
    {
        this.path = path;
        utcNow = clock ?? (() => DateTime.UtcNow);
        Capacity = Math.Max(1, capacity);
        RetentionDays = Math.Max(0, retentionDays);
        entries = Load(path);
        if (RemoveExpired() > 0) Save();
    }

    public int Capacity { get; }

    /// <summary>Entries older than this many days are removed. 0 means they never expire.</summary>
    public int RetentionDays { get; private set; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<QueryHistoryEntry> Entries => entries;

    /// <summary>Raised after the history changes.</summary>
    public event Action? Changed;

    public static string PathNextTo(string settingsFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsFile)) ?? ".", "query-history.json");

    /// <summary>Changes how long entries are kept (0 = always) and removes the ones that are now too old.</summary>
    public void SetRetention(int days)
    {
        days = Math.Max(0, days);
        if (days == RetentionDays) return;
        RetentionDays = days;
        Prune();
    }

    /// <summary>Removes expired entries. Returns how many were removed.</summary>
    public int Prune()
    {
        var removed = RemoveExpired();
        if (removed > 0) SaveAndNotify();
        return removed;
    }

    private int RemoveExpired()
    {
        if (RetentionDays <= 0) return 0;
        var cutoff = utcNow().AddDays(-RetentionDays);
        return entries.RemoveAll(e => e.RunAtUtc < cutoff);
    }

    /// <summary>
    /// Saves entries (all of them, or just the given ones) to a JSON file that <see cref="Import"/> can read
    /// on this or another computer.
    /// </summary>
    public int Export(string file, IEnumerable<QueryHistoryEntry>? selection = null)
    {
        var list = (selection ?? entries).ToList();
        var document = new { format = ExportFormat, version = 1, exportedAtUtc = utcNow(), entries = list };
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(file, JsonConvert.SerializeObject(document, Formatting.Indented));
        return list.Count;
    }

    /// <summary>
    /// Merges the entries of an exported file into the history. A query that is already there (same connection,
    /// database and text) keeps the more recent run. Entries that are already expired are skipped.
    /// Throws <see cref="InvalidDataException"/> when the file is not a query history export.
    /// </summary>
    public QueryHistoryImportResult Import(string file)
    {
        List<QueryHistoryEntry>? incoming;
        try
        {
            var text = File.ReadAllText(file);
            var token = Newtonsoft.Json.Linq.JToken.Parse(text);
            var array = token is Newtonsoft.Json.Linq.JObject document ? document["entries"] : token;
            if (array is not Newtonsoft.Json.Linq.JArray) throw new InvalidDataException("The file does not contain query history entries.");
            incoming = array.ToObject<List<QueryHistoryEntry>>();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The file is not a valid query history export.", ex);
        }
        if (incoming == null) throw new InvalidDataException("The file does not contain query history entries.");

        int added = 0, updated = 0, skipped = 0;
        var cutoff = RetentionDays > 0 ? utcNow().AddDays(-RetentionDays) : DateTime.MinValue;
        foreach (var entry in incoming)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Query) || entry.RunAtUtc < cutoff) { skipped++; continue; }
            entry.RunAtUtc = entry.RunAtUtc.ToUniversalTime();
            if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
            var existing = entries.FirstOrDefault(e => e.Id == entry.Id || SameQuery(e, entry));
            if (existing == null) { entries.Add(entry); added++; }
            else if (existing.RunAtUtc < entry.RunAtUtc) { entries[entries.IndexOf(existing)] = entry; updated++; }
            else skipped++;
        }
        if (added + updated > 0)
        {
            var ordered = entries.OrderByDescending(e => e.RunAtUtc).Take(Capacity).ToList();
            entries.Clear();
            entries.AddRange(ordered);
            SaveAndNotify();
        }
        return new QueryHistoryImportResult(added, updated, skipped);
    }

    private static bool SameQuery(QueryHistoryEntry a, QueryHistoryEntry b) =>
        a.ConnectionId == b.ConnectionId
        && string.Equals(a.Database, b.Database, StringComparison.Ordinal)
        && string.Equals(a.Query.Trim(), b.Query.Trim(), StringComparison.Ordinal);

    public void Add(QueryHistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Query)) return;
        RemoveExpired();
        entries.RemoveAll(e => e.ConnectionId == entry.ConnectionId
                               && string.Equals(e.Database, entry.Database, StringComparison.Ordinal)
                               && string.Equals(e.Query.Trim(), entry.Query.Trim(), StringComparison.Ordinal));
        entries.Insert(0, entry);
        if (entries.Count > Capacity) entries.RemoveRange(Capacity, entries.Count - Capacity);
        SaveAndNotify();
    }

    public void Remove(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        if (entries.RemoveAll(e => set.Contains(e.Id)) > 0) SaveAndNotify();
    }

    public void Clear()
    {
        if (entries.Count == 0) return;
        entries.Clear();
        SaveAndNotify();
    }

    /// <summary>
    /// Entries whose query, connection or database contain every word of <paramref name="text"/> (ignoring case),
    /// optionally only for one connection.
    /// </summary>
    public IReadOnlyList<QueryHistoryEntry> Search(string? text, string? connectionId = null)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return entries.Where(e => connectionId == null || e.ConnectionId == connectionId)
            .Where(e => words.All(w => e.Query.Contains(w, StringComparison.OrdinalIgnoreCase)
                                        || (e.ConnectionName?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)
                                        || (e.Database?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();
    }

    private void SaveAndNotify()
    {
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(entries, Formatting.Indented));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { /* history is a convenience; never fail a query over it */ }
        catch (UnauthorizedAccessException) { }
    }

    private static List<QueryHistoryEntry> Load(string? path)
    {
        try
        {
            if (path == null || !File.Exists(path)) return new List<QueryHistoryEntry>();
            return JsonConvert.DeserializeObject<List<QueryHistoryEntry>>(File.ReadAllText(path))?
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Query)).ToList() ?? new List<QueryHistoryEntry>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<QueryHistoryEntry>();
        }
    }
}
