using System.Text.RegularExpressions;
using Tickbird.Core.Data;

namespace Tickbird.Core.Query;

public static class QueryText
{
    /// <summary>SSMS-style: run the selection when present, otherwise the whole editor text.</summary>
    public static string TextToExecute(string? selectedText, string? fullText) =>
        (string.IsNullOrEmpty(selectedText) ? fullText : selectedText)?.Trim() ?? string.Empty;

    public static string Comment(string text) =>
        Regex.Replace(text, @"(?m)^(?<indent>[ \t]*)", "${indent}-- ");

    public static string Uncomment(string text) =>
        Regex.Replace(text, @"(?m)^(?<indent>[ \t]*)--[ \t]?", "${indent}");

    public static bool IsAggregate(string query) =>
        query.IndexOf("group by", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>The starter query placed in a new query tab.</summary>
    public static string SampleQuery(InfluxDbServerVersion version, string measurement = "measurement") =>
        version == InfluxDbServerVersion.InfluxDb3
            ? $"SELECT * FROM \"{measurement}\" WHERE time > now() - INTERVAL '5 minutes' LIMIT 100"
            : $"SELECT * FROM \"{measurement}\" WHERE time > now() - 5m LIMIT 100";
}
