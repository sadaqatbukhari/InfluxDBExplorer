using System.Globalization;

namespace Tickbird.Core.Charting;

/// <summary>Converts raw result cells (as returned by the 1.x JSON and 3.x Arrow clients) to chart numbers.</summary>
public static class ChartValues
{
    private static readonly long MinUnixSeconds = (long)(DateTime.MinValue - DateTime.UnixEpoch).TotalSeconds;
    private static readonly long MaxUnixSeconds = (long)(DateTime.MaxValue - DateTime.UnixEpoch).TotalSeconds;

    /// <summary>True for numbers and booleans. Strings are never numeric, even "42": in InfluxDB they are tags or string fields.</summary>
    public static bool IsNumeric(object? value) => value is sbyte or byte or short or ushort or int or uint or long or ulong
        or float or double or decimal or bool;

    /// <summary>Numeric cell to double; null or anything else becomes NaN (a gap). Booleans plot as 0 and 1.</summary>
    public static double ToDouble(object? value) => value switch
    {
        null => double.NaN,
        bool b => b ? 1 : 0,
        double d => d,
        float f => f,
        _ when IsNumeric(value) => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => double.NaN
    };

    /// <summary>
    /// Reads a timestamp cell as UTC. Handles <see cref="DateTime"/> (1.x JSON), <see cref="DateTimeOffset"/>,
    /// RFC 3339 strings, and integer epochs (3.x Arrow timestamps). An epoch's unit is inferred from its size:
    /// nanoseconds, microseconds, milliseconds or seconds.
    /// </summary>
    public static bool TryGetUtc(object? value, out DateTime utc)
    {
        switch (value)
        {
            case DateTime dateTime:
                utc = dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                return true;
            case DateTimeOffset offset:
                utc = offset.UtcDateTime;
                return true;
            case string text:
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    utc = parsed.UtcDateTime;
                    return true;
                }
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochText))
                    return TryFromEpoch(epochText, out utc);
                break;
            case long l: return TryFromEpoch(l, out utc);
            case int i: return TryFromEpoch(i, out utc);
            case ulong ul when ul <= long.MaxValue: return TryFromEpoch((long)ul, out utc);
            case uint ui: return TryFromEpoch(ui, out utc);
            case double d when !double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < long.MaxValue:
                return TryFromEpoch((long)d, out utc);
        }
        utc = default;
        return false;
    }

    /// <summary>Epoch to UTC, inferring the unit: |v| ≥ 1e17 ns, ≥ 1e14 µs, ≥ 1e11 ms, otherwise seconds.</summary>
    public static bool TryFromEpoch(long epoch, out DateTime utc)
    {
        utc = default;
        var magnitude = epoch == long.MinValue ? long.MaxValue : Math.Abs(epoch);
        long ticks;
        if (magnitude >= 100_000_000_000_000_000L) ticks = epoch / 100;                 // nanoseconds
        else if (magnitude >= 100_000_000_000_000L) ticks = epoch * 10;                // microseconds
        else if (magnitude >= 100_000_000_000L) ticks = epoch * TimeSpan.TicksPerMillisecond;
        else
        {
            if (epoch < MinUnixSeconds || epoch > MaxUnixSeconds) return false;
            ticks = epoch * TimeSpan.TicksPerSecond;
        }
        var maxTicks = DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks;
        var minTicks = DateTime.MinValue.Ticks - DateTime.UnixEpoch.Ticks;
        if (ticks > maxTicks || ticks < minTicks) return false;
        utc = new DateTime(DateTime.UnixEpoch.Ticks + ticks, DateTimeKind.Utc);
        return true;
    }

    /// <summary>The chart X coordinate for a UTC time (OLE Automation date, which ScottPlot's date axis expects).</summary>
    public static double ToX(DateTime utc) => utc.ToOADate();

    /// <summary>The UTC time for a chart X coordinate.</summary>
    public static DateTime FromX(double x) => DateTime.SpecifyKind(DateTime.FromOADate(x), DateTimeKind.Utc);

    /// <summary>Short display text for a numeric value in tooltips.</summary>
    public static string FormatNumber(double value, IFormatProvider? culture = null)
    {
        if (double.IsNaN(value)) return "null";
        culture ??= CultureInfo.CurrentCulture;
        var abs = Math.Abs(value);
        if (abs != 0 && (abs >= 1e9 || abs < 1e-4)) return value.ToString("0.###e+0", culture);
        return value.ToString(abs >= 1000 ? "#,0.##" : "0.####", culture);
    }
}
