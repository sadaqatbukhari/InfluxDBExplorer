using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tickbird.Core.Settings;
using Tickbird.Desktop.Services;
using Tickbird.Desktop.Views;

namespace Tickbird.Desktop.Tests;

public sealed class ToolbarLayoutTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "tickbird-toolbar-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static ToolBarPanel Panel(params Control[] children)
    {
        var panel = new ToolBarPanel { Spacing = 0 };
        foreach (var c in children) panel.Children.Add(c);
        return panel;
    }

    private static Button Tool() => new() { Width = 28, Height = 28 };

    [AvaloniaFact]
    public void ResponsiveToolbar_MovesWorkingControlsIntoMenu_AndRestoresOnResize()
    {
        var toolbar = new ResponsiveToolBar();
        var first = new Button { Content = "Colors", Width = 100 };
        var editor = new ComboBox { ItemsSource = new[] { "Hybrid", "None" }, SelectedIndex = 0, Width = 120 };
        var save = new Button { Content = "Save PNG", Width = 90 };
        var clicks = 0;
        save.Click += (_, _) => clicks++;
        toolbar.Add(first);
        toolbar.Add(new TextBlock { Text = "Map" }, editor);
        toolbar.Add(save);
        var window = new Window { Width = 250, Height = 180, Content = toolbar, SizeToContent = SizeToContent.Manual };
        window.Show();
        try
        {
            Layout();
            var more = Assert.Single(toolbar.GetVisualDescendants().OfType<Button>(), b => b.Name == "MoreCommands");
            Assert.True(more.IsVisible);
            Assert.Equal(26, first.Bounds.Height);
            Assert.Equal(26, more.Bounds.Height);
            var flyout = Assert.IsType<Flyout>(more.Flyout);
            flyout.ShowAt(more);
            Layout();
            var menu = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(flyout.Content).Content);
            Assert.Contains(save, menu.Children);
            Assert.Contains(editor, menu.GetVisualDescendants());
            editor.SelectedIndex = 1;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, clicks);
            window.Width = 650;
            Layout();
            Assert.False(flyout.IsOpen);
            Assert.False(more.IsVisible);
            Assert.Empty(menu.Children);
            Assert.Equal(1, editor.SelectedIndex);
            Assert.Contains(editor, toolbar.GetVisualDescendants());
            Assert.Equal(first.Bounds.Height, editor.Bounds.Height);
            Assert.Equal(first.Bounds.Y, save.Bounds.Y);
        }
        finally { window.Close(); }

        void Layout()
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void ToolBarPanel_ShowsEverything_WhenItFits()
    {
        var panel = Panel(Tool(), Tool(), Tool());
        panel.Measure(new Size(200, 30));
        Assert.Equal(3, panel.VisibleCount);
        Assert.False(panel.HasOverflow);
    }

    [AvaloniaFact]
    public void ToolBarPanel_MovesWhatDoesNotFitToOverflow_AndNeverEndsOnSeparator()
    {
        var panel = Panel(Tool(), Tool(), new Separator { Width = 10, Margin = new Thickness(0) }, Tool(), Tool());
        panel.Measure(new Size(70, 30));   // two tools + separator fit, the next tool does not
        Assert.Equal(2, panel.VisibleCount);
        Assert.True(panel.HasOverflow);
        Assert.Equal(56, panel.DesiredSize.Width);
    }

    [AvaloniaFact]
    public void ViewMenu_TogglesToolbarAndStatusBar_AndSaves()
    {
        var path = Path.Combine(dir, "settings.json");
        var app = new TickbirdApp(new SettingsStore(path));
        TickbirdApp.Current = app;
        var window = new MainWindow(app, showConnectionsOnStart: false);
        window.Show();
        var toolbar = window.FindControl<Border>("ToolbarHost")!;
        var status = window.FindControl<Border>("StatusBar")!;
        Assert.True(toolbar.IsVisible);
        Assert.True(status.IsVisible);

        window.FindControl<MenuItem>("MenuViewToolbar")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        window.FindControl<MenuItem>("MenuViewStatusBar")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(toolbar.IsVisible);
        Assert.False(status.IsVisible);
        var saved = new SettingsStore(path).Load();
        Assert.False(saved.ShowToolbar);
        Assert.False(saved.ShowStatusBar);

        window.ResetWindowLayout();
        Assert.True(toolbar.IsVisible);
        Assert.True(status.IsVisible);
        window.Close();
    }
}
