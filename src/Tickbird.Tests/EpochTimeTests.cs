using System.Globalization;
using Tickbird.Core.Query;
using Tickbird.Core.Settings;

namespace Tickbird.Tests;

public class EpochTimeTests
{
    // 2026-10-21 07:30:15 UTC
    private static readonly DateTimeOffset Moment = new(2026, 10, 21, 7, 30, 15, TimeSpan.Zero);
    private const long Seconds = 1_792_567_815;

    [Fact]
    public void KnownMoment_IsTheSameInEveryUnit()
    {
        Assert.Equal(Moment, EpochTime.TryConvert(Seconds, EpochUnit.Seconds));
        Assert.Equal(Moment, EpochTime.TryConvert(Seconds * 1_000L, EpochUnit.Milliseconds));
        Assert.Equal(Moment, EpochTime.TryConvert(Seconds * 1_000_000L, EpochUnit.Microseconds));
        Assert.Equal(Moment, EpochTime.TryConvert(Seconds * 1_000_000_000L, EpochUnit.Nanoseconds));
    }

    [Theory]
    [InlineData(1_792_567_815L, EpochUnit.Seconds)]
    [InlineData(1_792_567_815_000L, EpochUnit.Milliseconds)]
    [InlineData(1_792_567_815_000_000L, EpochUnit.Microseconds)]
    [InlineData(1_792_567_815_000_000_000L, EpochUnit.Nanoseconds)]
    [InlineData(0L, EpochUnit.Seconds)]
    [InlineData(-86_400L, EpochUnit.Seconds)]
    public void Detect_PicksTheUnitFromTheSizeOfTheNumber(long value, EpochUnit expected) =>
        Assert.Equal(expected, EpochTime.Detect(value));

    [Fact]
    public void Auto_ConvertsEachUnitToTheSameMoment()
    {
        foreach (var value in new object[] { Seconds, Seconds * 1_000L, Seconds * 1_000_000L, Seconds * 1_000_000_000L })
            Assert.Equal(Moment, EpochTime.TryConvert(value, EpochUnit.Auto));
    }

    [Fact]
    public void Accepts_Doubles_Strings_AndKeepsFractions()
    {
        Assert.Equal(Moment.AddMilliseconds(500), EpochTime.TryConvert(Seconds + 0.5, EpochUnit.Seconds));
        Assert.Equal(Moment, EpochTime.TryConvert(Seconds.ToString(CultureInfo.InvariantCulture), EpochUnit.Seconds));
        Assert.Equal(Moment, EpochTime.TryConvert(" " + (Seconds * 1000L) + " ", EpochUnit.Milliseconds));
        Assert.Equal(Moment, EpochTime.TryConvert((double)Seconds * 1000, EpochUnit.Auto));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(-1), EpochTime.TryConvert(-86_400L, EpochUnit.Seconds));
    }

    [Fact]
    public void NonNumbersAndAbsurdValues_AreNotConverted()
    {
        Assert.Null(EpochTime.TryConvert(null, EpochUnit.Auto));
        Assert.Null(EpochTime.TryConvert("web-1", EpochUnit.Auto));
        Assert.Null(EpochTime.TryConvert(true, EpochUnit.Auto));
        Assert.Null(EpochTime.TryConvert(double.NaN, EpochUnit.Auto));
        Assert.Null(EpochTime.TryConvert(long.MaxValue, EpochUnit.Seconds));          // year far beyond 9999
        Assert.Null(EpochTime.TryConvert(-9_999_999_999_999L, EpochUnit.Seconds));    // before year 1
        Assert.Null(EpochTime.TryConvert(new DateTime(2026, 1, 1), EpochUnit.Auto));  // already a date
    }

    private static ResultFormatter Formatter(IReadOnlyDictionary<string, EpochUnit>? columns) =>
        new ResultFormatter("yyyy'-'MM'-'dd", TickbirdSettings.TimeFormat24Hour).WithEpochColumns(columns);

    [Fact]
    public void Formatter_ShowsOnlyTheChosenColumnsAsDates()
    {
        var formatter = Formatter(new Dictionary<string, EpochUnit> { ["ts"] = EpochUnit.Milliseconds });
        var culture = CultureInfo.InvariantCulture;

        Assert.StartsWith("2026-10-21 07:30:15", formatter.Format("ts", Seconds * 1000L, culture));
        Assert.Equal(Seconds.ToString(culture), formatter.Format("other", Seconds, culture));   // untouched
        Assert.Equal(EpochUnit.Milliseconds, formatter.EpochUnitOf("TS"));                      // names ignore case
        Assert.Null(formatter.EpochUnitOf("other"));
    }

    [Fact]
    public void Formatter_KeepsThePlainText_WhenACellIsNotAnEpochNumber()
    {
        var formatter = Formatter(new Dictionary<string, EpochUnit> { ["ts"] = EpochUnit.Auto });
        Assert.Equal("n/a", formatter.Format("ts", "n/a", CultureInfo.InvariantCulture));
        Assert.Null(formatter.Format("ts", null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Formatter_NeverConvertsTheTimeColumn()
    {
        var formatter = Formatter(new Dictionary<string, EpochUnit> { ["time"] = EpochUnit.Seconds });
        // time already holds a date; it must still format like before
        var text = formatter.Format("time", "2026-10-21T07:30:15Z", CultureInfo.InvariantCulture);
        Assert.StartsWith("2026-10-21 07:30:15", text);
    }

    [Fact]
    public void FormatEpoch_UsesTheUsersDateAndTimeFormats()
    {
        var german = new ResultFormatter(TickbirdSettings.DateFormatGerman, TickbirdSettings.TimeFormat24Hour);
        Assert.StartsWith("21.10.2026 07:30:15", german.FormatEpoch(Seconds * 1000L, EpochUnit.Auto, CultureInfo.InvariantCulture));
        Assert.Null(german.FormatEpoch("abc"));
    }
}
