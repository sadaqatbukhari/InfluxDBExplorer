using System.Globalization;

namespace Tickbird.Core.Query;

/// <summary>The unit of a Unix epoch number (time since 1970-01-01 UTC).</summary>
public enum EpochUnit
{
    /// <summary>Pick the unit from the size of the number (see <see cref="EpochTime.Detect"/>).</summary>
    Auto,
    Seconds,
    Milliseconds,
    Microseconds,
    Nanoseconds
}

/// <summary>Turns Unix epoch numbers into dates.</summary>
public static class EpochTime
{
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;

    /// <summary>
    /// Guesses the unit from the magnitude: below 1e11 is seconds (up to the year 5138), below 1e14 milliseconds,
    /// below 1e17 microseconds, otherwise nanoseconds (InfluxDB's native precision).
    /// </summary>
    public static EpochUnit Detect(decimal value)
    {
        var magnitude = Math.Abs(value);
        if (magnitude < 100_000_000_000m) return EpochUnit.Seconds;
        if (magnitude < 100_000_000_000_000m) return EpochUnit.Milliseconds;
        if (magnitude < 100_000_000_000_000_000m) return EpochUnit.Microseconds;
        return EpochUnit.Nanoseconds;
    }

    /// <summary>A number (or text holding one) as a decimal, or null when the value is not numeric.</summary>
    public static decimal? ToNumber(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                bool => null,
                long l => l,
                int i => i,
                short s => s,
                byte b => b,
                decimal d => d,
                double d => double.IsFinite(d) ? (decimal)d : null,
                float f => float.IsFinite(f) ? (decimal)f : null,
                string text when decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                IConvertible c when value is not string and not DateTime and not DateTimeOffset => c.ToDecimal(CultureInfo.InvariantCulture),
                _ => null
            };
        }
        catch (Exception ex) when (ex is OverflowException or FormatException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// The UTC date for an epoch value in the given unit, or null when the value is not a number or is too far
    /// from 1970 to be a date (so the grid keeps showing the number).
    /// </summary>
    public static DateTimeOffset? TryConvert(object? value, EpochUnit unit)
    {
        var number = ToNumber(value);
        if (number == null) return null;
        var n = number.Value;
        if (unit == EpochUnit.Auto) unit = Detect(n);

        decimal ticks = unit switch
        {
            EpochUnit.Seconds => n * TicksPerSecond,
            EpochUnit.Milliseconds => n * (TicksPerSecond / 1000),
            EpochUnit.Microseconds => n * (TicksPerSecond / 1_000_000),
            _ => n / 100m                       // nanoseconds: one tick is 100 ns
        };
        var epoch = DateTimeOffset.UnixEpoch.UtcTicks;
        if (ticks < -epoch || ticks > DateTime.MaxValue.Ticks - epoch) return null;
        return new DateTimeOffset(epoch + (long)Math.Round(ticks, MidpointRounding.AwayFromZero), TimeSpan.Zero);
    }

    public static string Describe(EpochUnit unit) => unit switch
    {
        EpochUnit.Seconds => "seconds",
        EpochUnit.Milliseconds => "milliseconds",
        EpochUnit.Microseconds => "microseconds",
        EpochUnit.Nanoseconds => "nanoseconds",
        _ => "auto-detected unit"
    };
}
