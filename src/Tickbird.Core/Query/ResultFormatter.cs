using System.Globalization;
using Tickbird.Core.Settings;

namespace Tickbird.Core.Query;

/// <summary>Formats result cells using the user's date and time preferences (port of FormatOutputValue).</summary>
public sealed class ResultFormatter
{
    private readonly string dateFormat;
    private readonly string timeFormat;
    private readonly IReadOnlyDictionary<string, EpochUnit>? epochColumns;

    public ResultFormatter(string dateFormat, string timeFormat)
    {
        this.dateFormat = dateFormat;
        this.timeFormat = timeFormat;
    }

    private ResultFormatter(string dateFormat, string timeFormat, IReadOnlyDictionary<string, EpochUnit>? epochColumns)
        : this(dateFormat, timeFormat) => this.epochColumns = epochColumns;

    /// <summary>
    /// A formatter that also shows the named columns (matched ignoring case) as dates, reading their numbers as
    /// Unix epoch time in the given unit. The <c>time</c> column is never converted.
    /// </summary>
    public ResultFormatter WithEpochColumns(IReadOnlyDictionary<string, EpochUnit>? columns) =>
        new(dateFormat, timeFormat, columns is { Count: > 0 } ? new Dictionary<string, EpochUnit>(columns.ToDictionary(c => c.Key, c => c.Value), StringComparer.OrdinalIgnoreCase) : null);

    /// <summary>The unit a column is converted with, or null when it is shown as it came.</summary>
    public EpochUnit? EpochUnitOf(string columnName) =>
        epochColumns != null && epochColumns.TryGetValue(columnName, out var unit) ? unit : null;

    /// <summary>An epoch number as a date in the user's date and time format, or null when it is not a usable epoch value.</summary>
    public string? FormatEpoch(object? value, EpochUnit unit = EpochUnit.Auto, IFormatProvider? culture = null) =>
        EpochTime.TryConvert(value, unit) is { } timestamp ? FormatTimestamp(timestamp, culture ?? CultureInfo.CurrentCulture) : null;

    public string? Format(string columnName, object? value, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (value == null) return null;
        if (epochColumns != null && !string.Equals(columnName, "time", StringComparison.OrdinalIgnoreCase)
            && epochColumns.TryGetValue(columnName, out var epochUnit)
            && EpochTime.TryConvert(value, epochUnit) is { } converted)
            return FormatTimestamp(converted, culture);
        var isTimeColumn = string.Equals(columnName, "time", StringComparison.OrdinalIgnoreCase)
            || string.Equals(columnName, "timestamp", StringComparison.OrdinalIgnoreCase);
        if (!isTimeColumn && value is not DateTime && value is not DateTimeOffset)
            return Convert.ToString(value, culture);

        DateTimeOffset timestamp;
        if (value is DateTimeOffset offset) timestamp = offset;
        else if (value is DateTime dateTime) timestamp = new DateTimeOffset(dateTime);
        else if (!DateTimeOffset.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
            return Convert.ToString(value, culture);

        return FormatTimestamp(timestamp, culture);
    }

    private string FormatTimestamp(DateTimeOffset timestamp, IFormatProvider culture)
    {
        var time = timeFormat;
        if (time.IndexOf('f') < 0) time = time.Replace("ss", "ss.fff");
        return timestamp.ToString(TickbirdSettings.ResolveDateFormat(dateFormat, culture) + " " + time, culture);
    }
}
