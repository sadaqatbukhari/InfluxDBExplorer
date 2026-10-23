using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tickbird.Core.Settings;
using Tickbird.Desktop.Services;
using Tickbird.Desktop.ViewModels;
using Tickbird.Desktop.Views;
using Tickbird.Desktop.Views.Requests;

namespace Tickbird.Desktop.Tests;

public sealed class TableAndTabLayoutTests
{
    [AvaloniaFact]
    public void TableSelection_KeepsReadableColors_WhenFocusMovesAway()
    {
        var grid = GridFactory.Create();
        GridFactory.SetColumns(grid, new[] { "fieldKey", "fieldType" });
        GridFactory.SetRows(grid, new[]
        {
            new GridRow(new[] { "totaldistancefieldcm", "float" }),
            new GridRow(new[] { "totaldistancefieldmeter", "float" })
        });
        var other = new TextBox();
        var panel = new DockPanel();
        DockPanel.SetDock(other, Dock.Top);
        panel.Children.Add(other);
        panel.Children.Add(grid);
        var window = new Window { Width = 700, Height = 350, Content = panel };
        window.Show();
        try
        {
            grid.SelectedIndex = 0;
            grid.Focus();
            CheckSelection();
            other.Focus();
            CheckSelection();
            grid.SelectedIndex = 1;
            CheckSelection();
            SaveFrame(window, "table-selection.png");
        }
        finally { window.Close(); }

        void CheckSelection()
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var row = Assert.Single(grid.GetVisualDescendants().OfType<DataGridRow>(), r => r.IsSelected);
            var background = row.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "BackgroundRectangle");
            Assert.Equal(Color.Parse("#cbdceb"), Assert.IsAssignableFrom<ISolidColorBrush>(background.Fill).Color);
            Assert.All(row.GetVisualDescendants().OfType<DataGridCell>(), cell =>
                Assert.Equal(Color.Parse("#1a65a7"), Assert.IsAssignableFrom<ISolidColorBrush>(cell.Foreground).Color));
        }
    }

    [AvaloniaFact]
    public void OpenTabs_StayOnOneRow_AndScrollToSelectedTab()
    {
        var app = new TickbirdApp(new SettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json")));
        TickbirdApp.Current = app;
        var window = new MainWindow(app, showConnectionsOnStart: false) { Width = 900, Height = 600 };
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        try
        {
            for (var i = 0; i < 12; i++)
                vm.AddTab(new TabViewModel($"t{i}", $"New Connection.dev.measurement_{i}", TabKind.Request, "1", "FieldKeys", new TextBlock { Text = $"Table {i}" }));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var tabs = window.FindControl<TabControl>("Tabs")!;
            var scroll = tabs.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "DocumentTabScroll");
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            var headers = Enumerable.Range(0, tabs.ItemCount).Select(i => (TabItem)tabs.ContainerFromIndex(i)!).ToArray();
            Assert.All(headers, h => Assert.Equal(headers[0].Bounds.Y, h.Bounds.Y));
            Assert.True(scroll.Offset.X > 0, "The newly selected last tab should scroll into view.");
            SaveFrame(window, "scrollable-tabs-last.png");
            vm.SelectedTab = vm.Tabs[0];
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "scrollable-tabs.png");
            Assert.Equal(0, scroll.Offset.X);
            Assert.True(headers[0].IsSelected);
        }
        finally { window.Close(); }
    }

    private static void SaveFrame(Window window, string name)
    {
        var target = Environment.GetEnvironmentVariable("INFLUX_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(target)) return;
        Directory.CreateDirectory(target);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(target, name));
    }
}
