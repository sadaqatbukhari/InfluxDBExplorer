using Tickbird.Core.Navigation;

namespace Tickbird.Tests;

public class ConfirmByNameTests
{
    [Theory]
    [InlineData("telegraf", "telegraf", true)]
    [InlineData("Telegraf", "telegraf", false)]   // case-sensitive, like InfluxDB names
    [InlineData("telegra", "telegraf", false)]
    [InlineData("telegraf ", "telegraf", false)]
    [InlineData("", "telegraf", false)]
    [InlineData(null, "telegraf", false)]
    [InlineData("", "", false)]                    // never confirm against an empty name
    [InlineData("my db", "my db", true)]
    public void NameMatches(string? typed, string? expected, bool result) =>
        Assert.Equal(result, Validation.NameMatches(typed, expected));
}
