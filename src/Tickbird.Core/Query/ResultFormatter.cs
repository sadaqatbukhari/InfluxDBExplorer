using System.Globalization;
using Tickbird.Core.Settings;

namespace Tickbird.Core.Query;

/// <summary>Formats result cells using the user's date and time preferences (port of FormatOutputValue).</summary>
public sealed class ResultFormatter
{
    private readonly string dateFormat;
    private readonly string timeFormat;

    public ResultFormatter(string dateFormat, string timeFormat)
    {
        this.dateFormat = dateFormat;
        this.timeFormat = timeFormat;
    }

    public string? Format(string columnName, object? value, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (value == null) return null;
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

        var time = timeFormat;
        if (time.IndexOf('f') < 0) time = time.Replace("ss", "ss.fff");
        return timestamp.ToString(TickbirdSettings.ResolveDateFormat(dateFormat, culture) + " " + time, culture);
    }
}
