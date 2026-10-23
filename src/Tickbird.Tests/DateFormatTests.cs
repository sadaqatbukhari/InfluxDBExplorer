using System.Globalization;
using Tickbird.Core.Query;
using Tickbird.Core.Settings;

namespace Tickbird.Tests;

public class DateFormatTests
{
    private static readonly DateTimeOffset Moment = new(2016, 12, 31, 14, 30, 45, TimeSpan.Zero);

    [Theory]
    [InlineData("M/dd/yyyy", "12/31/2016", "en-US")]
    [InlineData("d/MM/yyyy", "31/12/2016", "en-GB")]
    [InlineData("dd'.'MM'.'yyyy", "31.12.2016", "de-DE")]
    [InlineData("dd'.'MM'.'yyyy", "31.12.2016", "en-US")]      // literal dots, whatever the computer's language
    [InlineData("d'.'M'.'yyyy", "31.12.2016", "cs-CZ")]
    [InlineData("dd'-'MM'-'yyyy", "31-12-2016", "nl-NL")]
    [InlineData("yyyy'-'MM'-'dd", "2016-12-31", "sv-SE")]
    [InlineData("yyyy'-'MM'-'dd", "2016-12-31", "de-DE")]
    public void EachFormat_ShowsTheSameDateTheSameWay(string format, string expectedDate, string culture)
    {
        var formatter = new ResultFormatter(format, TickbirdSettings.TimeFormat24Hour);
        var text = formatter.Format("time", Moment, CultureInfo.GetCultureInfo(culture));
        Assert.StartsWith(expectedDate + " 14:30:45", text);
    }

    [Fact]
    public void ZeroPadding_DiffersBetweenTheDottedFormats()
    {
        var day = new DateTimeOffset(2016, 2, 3, 1, 2, 3, TimeSpan.Zero);
        Assert.StartsWith("03.02.2016", new ResultFormatter(TickbirdSettings.DateFormatGerman, TickbirdSettings.TimeFormat24Hour).Format("time", day));
        Assert.StartsWith("3.2.2016", new ResultFormatter(TickbirdSettings.DateFormatDotted, TickbirdSettings.TimeFormat24Hour).Format("time", day));
    }

    [Fact]
    public void TheMenuList_HasUniqueFormats_IncludingTheOriginalTwo()
    {
        var formats = TickbirdSettings.DateFormats.Select(f => f.Format).ToList();
        Assert.Equal(formats.Count, formats.Distinct().Count());
        Assert.Contains(TickbirdSettings.DateFormatMonth, formats);
        Assert.Contains(TickbirdSettings.DateFormatDay, formats);
        Assert.True(formats.Count >= 6);
        Assert.Equal(TickbirdSettings.DateFormatSystem, new TickbirdSettings().DateFormat);   // the default follows the system
    }

    [Theory]
    [InlineData("de-DE", "31.12.2016")]
    [InlineData("en-US", "12/31/2016")]
    [InlineData("en-GB", "31/12/2016")]
    [InlineData("nl-NL", "31-12-2016")]
    [InlineData("sv-SE", "2016-12-31")]
    public void SystemDefault_FollowsTheRegionalSettings(string culture, string expectedDate)
    {
        var formatter = new ResultFormatter(TickbirdSettings.DateFormatSystem, TickbirdSettings.TimeFormat24Hour);
        Assert.StartsWith(expectedDate, formatter.Format("time", Moment, CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void ResolveDateFormat_KeepsExplicitFormats_AndMapsBlankToTheSystem()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal(TickbirdSettings.DateFormatIso, TickbirdSettings.ResolveDateFormat(TickbirdSettings.DateFormatIso, german));
        Assert.Equal(german.DateTimeFormat.ShortDatePattern, TickbirdSettings.ResolveDateFormat("system", german));
        Assert.Equal(german.DateTimeFormat.ShortDatePattern, TickbirdSettings.ResolveDateFormat(null, german));
        Assert.Equal(german.DateTimeFormat.ShortDatePattern, TickbirdSettings.ResolveDateFormat("  ", german));
    }

    [Fact]
    public void TheSystemOption_IsFirstInTheMenuList() =>
        Assert.Equal(TickbirdSettings.DateFormatSystem, TickbirdSettings.DateFormats[0].Format);
}
