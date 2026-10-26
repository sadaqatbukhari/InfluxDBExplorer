using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Tickbird.Desktop.Views.Requests;

namespace Tickbird.Desktop.Tests;

/// <summary>The query editor uses SQL Server Management Studio's default colors.</summary>
public class EditorColorsTests
{
    private static string? Hex(string token) => (QueryView.SyntaxBrush(token) as ISolidColorBrush)?.Color.ToString();

    [AvaloniaTheory]
    [InlineData("SELECT", "#ff0000ff")]
    [InlineData("from", "#ff0000ff")]
    [InlineData("GROUP", "#ff0000ff")]
    [InlineData("LIMIT", "#ff0000ff")]
    [InlineData("mean", "#ffff00ff")]
    [InlineData("NOW", "#ffff00ff")]
    [InlineData("'work'", "#ffff0000")]
    [InlineData("'unterminated", "#ffff0000")]
    [InlineData("-- a comment", "#ff008000")]
    [InlineData("AND", "#ff808080")]
    [InlineData("or", "#ff808080")]
    [InlineData("=", "#ff808080")]
    [InlineData("=~", "#ff808080")]
    [InlineData("*", "#ff808080")]
    public void Tokens_GetSsmsColors(string token, string expected) => Assert.Equal(expected, Hex(token));

    [AvaloniaTheory]
    [InlineData("\"cpu load\"")]
    [InlineData("speed")]
    [InlineData("gps_position")]
    public void NamesStayBlack(string token) => Assert.Null(QueryView.SyntaxBrush(token));

    [AvaloniaFact]
    public void EditorText_IsBlack()
    {
        var view = new QueryView();
        Assert.Equal(Colors.Black, ((ISolidColorBrush)view.Editor.Foreground!).Color);
    }
}
