using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tickbird.Desktop.Services;

/// <summary>Shared presentation helpers; never change data or command state.</summary>
internal static class TickbirdTheme
{
    public static readonly IBrush Canvas = Brush.Parse("#F1F7FD");
    public static readonly IBrush Line = Brush.Parse("#e3ecf4");
    public static readonly IBrush Muted = Brush.Parse("#1a65a7");
    private static readonly IBrush AlternateRow = Brush.Parse("#F2F8FE");

    public static Border Card(Control content) => new()
    {
        Background = Brushes.White,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        Margin = new Thickness(10),
        Child = content
    };

    public static void StyleGrid(DataGrid grid)
    {
        grid.Classes.Add("tickbirdGrid");
        grid.Background = Brushes.White;
        grid.Foreground = Brush.Parse("#1a65a7");
        grid.FontSize = 13;
        grid.RowHeight = 30;
        grid.ColumnHeaderHeight = 36;
        grid.HorizontalGridLinesBrush = Brush.Parse("#F0F6FC");
        grid.VerticalGridLinesBrush = Brush.Parse("#e6eef5");
        grid.BorderBrush = Line;
        grid.BorderThickness = new Thickness(1);
        grid.LoadingRow += (_, e) => e.Row.Background = e.Row.Index % 2 == 0 ? Brushes.White : AlternateRow;
    }
}
