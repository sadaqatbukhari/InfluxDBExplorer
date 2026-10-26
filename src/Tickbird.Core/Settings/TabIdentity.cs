namespace Tickbird.Core.Settings;

/// <summary>
/// Tab identity rules: New Query is always unique; every "Show ..." view has one tab per target
/// (server, database or measurement), keyed by connection ID rather than display name.
/// </summary>
public static class TabIdentity
{
    public static string NewQuery() => "query:" + Guid.NewGuid().ToString("N");

    public static string ShowQueries(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentException("Connection ID is required.", nameof(connectionId));
        return "show-queries:" + connectionId;
    }

    /// <summary>
    /// ID for a "Show ..." tab. <paramref name="view"/> names the view (for example "Users" or "tag_keys");
    /// pass the database for database-level views and the database plus measurement for measurement-level views.
    /// </summary>
    public static string Show(string view, string connectionId, string? database = null, string? measurement = null)
    {
        if (string.IsNullOrWhiteSpace(view)) throw new ArgumentException("View name is required.", nameof(view));
        if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentException("Connection ID is required.", nameof(connectionId));
        if (measurement != null && database == null) throw new ArgumentException("A measurement needs its database.", nameof(database));
        // Database and measurement names may contain any character, so join with a separator they cannot hold.
        var parts = new List<string> { "show", view, connectionId };
        if (database != null) parts.Add(database);
        if (measurement != null) parts.Add(measurement);
        return string.Join('\u001f', parts);
    }
}
