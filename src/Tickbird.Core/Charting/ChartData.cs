namespace Tickbird.Core.Charting;

/// <summary>What the chart's horizontal axis represents.</summary>
public enum ChartAxisKind
{
    /// <summary>Nothing can be plotted; see <see cref="ChartData.Message"/>.</summary>
    None,

    /// <summary>X values are UTC timestamps stored as OLE Automation dates.</summary>
    Time,

    /// <summary>X values are 0..n-1 positions with labels in <see cref="ChartData.Categories"/>.</summary>
    Category
}

/// <summary>One plotted line (or bar group): a numeric column, optionally for one tag set or split value.</summary>
/// <param name="Label">Legend text, for example <c>usage {host=web-1}</c>.</param>
/// <param name="Field">The result column the values come from.</param>
/// <param name="Xs">X positions, sorted ascending. Time charts use OLE Automation dates in UTC.</param>
/// <param name="Ys">Y values. Null cells are <see cref="double.NaN"/>, which charts draw as gaps.</param>
/// <param name="SourcePointCount">Rows before downsampling.</param>
public sealed record ChartSeries(string Label, string Field, double[] Xs, double[] Ys, int SourcePointCount)
{
    public bool IsDownsampled => Xs.Length < SourcePointCount;
}

/// <summary>Settings the user picks in the chart toolbar.</summary>
public sealed class ChartOptions
{
    /// <summary>A text column whose values split each field into separate lines (useful for InfluxDB 3 SQL results).</summary>
    public string? SplitBy { get; set; }

    /// <summary>Numeric columns the user has unticked.</summary>
    public ISet<string> HiddenFields { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Time series longer than this are reduced with LTTB, which keeps the visual shape.</summary>
    public int MaxPointsPerSeries { get; set; } = 5000;

    /// <summary>Upper limit on lines, so a high-cardinality split does not draw thousands of lines.</summary>
    public int MaxSeries { get; set; } = 50;

    /// <summary>Upper limit on bars in a category chart.</summary>
    public int MaxCategories { get; set; } = 500;
}

/// <summary>Chart-ready data built from query results by <see cref="ChartDataBuilder"/>.</summary>
public sealed class ChartData
{
    public ChartAxisKind AxisKind { get; init; }

    /// <summary>Lines to draw (hidden fields excluded).</summary>
    public IReadOnlyList<ChartSeries> Series { get; init; } = Array.Empty<ChartSeries>();

    /// <summary>Every numeric column found, including hidden ones, for the field picker.</summary>
    public IReadOnlyList<string> Fields { get; init; } = Array.Empty<string>();

    /// <summary>Text columns that can be used with <see cref="ChartOptions.SplitBy"/>.</summary>
    public IReadOnlyList<string> SplitCandidates { get; init; } = Array.Empty<string>();

    /// <summary>Columns that hold text or other non-numeric values and are not drawn.</summary>
    public IReadOnlyList<string> SkippedColumns { get; init; } = Array.Empty<string>();

    /// <summary>Bar labels when <see cref="AxisKind"/> is <see cref="ChartAxisKind.Category"/>.</summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    /// <summary>The time or category column used for the X axis.</summary>
    public string? XColumn { get; init; }

    /// <summary>A note for the user: why nothing is drawn, or what was trimmed.</summary>
    public string? Message { get; init; }

    public int TotalPoints => Series.Sum(s => s.Xs.Length);

    public int SourcePoints => Series.Sum(s => s.SourcePointCount);

    public bool IsDownsampled => Series.Any(s => s.IsDownsampled);

    public static ChartData Empty(string message, IReadOnlyList<string>? skipped = null) =>
        new() { AxisKind = ChartAxisKind.None, Message = message, SkippedColumns = skipped ?? Array.Empty<string>() };
}
