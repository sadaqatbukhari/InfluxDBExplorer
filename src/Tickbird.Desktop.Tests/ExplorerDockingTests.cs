using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Tickbird.Core.Settings;
using Tickbird.Desktop.Services;
using Tickbird.Desktop.Views;

namespace Tickbird.Desktop.Tests;

public sealed class ExplorerDockingTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "tickbird-dock-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(dir, "settings.json");

    private MainWindow Create()
    {
        var app = new TickbirdApp(new SettingsStore(SettingsPath));
        TickbirdApp.Current = app;
        var window = new MainWindow(app, showConnectionsOnStart: false);
        window.Show();
        return window;
    }

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static Border Pane(MainWindow w) => w.FindControl<Border>("ExplorerPane")!;
    private static Border Host(MainWindow w, string name) => w.FindControl<Border>(name)!;

    [AvaloniaFact]
    public void Explorer_IsDockedLeftByDefault()
    {
        var window = Create();
        Assert.Same(Pane(window), Host(window, "LeftDockHost").Child);
        Assert.Null(Host(window, "RightDockHost").Child);
        Assert.False(Host(window, "LeftAutoHideStrip").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void DockRight_MovesPane_AndIsSaved()
    {
        var window = Create();
        window.DockExplorer(PaneLayout.Right);
        Assert.Same(Pane(window), Host(window, "RightDockHost").Child);
        Assert.Null(Host(window, "LeftDockHost").Child);
        Assert.Equal(PaneLayout.Right, new SettingsStore(SettingsPath).Load().Explorer.Placement);
        window.Close();
    }

    [AvaloniaFact]
    public void SavedLayout_IsRestoredOnStartup()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new TickbirdSettings();
        settings.Explorer.DockTo(PaneLayout.Right);
        store.Save(settings);

        var window = Create();
        Assert.Same(Pane(window), Host(window, "RightDockHost").Child);
        window.Close();
    }

    [AvaloniaFact]
    public void Float_MovesTreeToOwnedWindow_AndDockBringsItBack()
    {
        var window = Create();
        window.FloatExplorer();
        var floating = window.ExplorerFloatWindow;
        Assert.NotNull(floating);
        Assert.True(floating!.IsVisible);
        Assert.Same(Pane(window), floating.Content);
        Assert.Null(Host(window, "LeftDockHost").Child);

        window.DockExplorer(PaneLayout.Left);
        Assert.Null(window.ExplorerFloatWindow);
        Assert.False(floating.IsVisible);
        Assert.Same(Pane(window), Host(window, "LeftDockHost").Child);
        window.Close();
    }

    [AvaloniaFact]
    public void AutoHide_ShowsEdgeTab_AndShowExplorerSlidesItOut()
    {
        var window = Create();
        window.SetExplorerAutoHide(true);
        Assert.Null(Host(window, "LeftDockHost").Child);
        Assert.True(Host(window, "LeftAutoHideStrip").IsVisible);
        Assert.Null(Pane(window).Parent);

        window.ShowExplorer();
        Assert.True(window.IsExplorerFlyoutOpen);
        Assert.NotNull(Pane(window).Parent);
        Assert.True(window.FindControl<Panel>("FlyoutLayer")!.IsVisible);

        window.SetExplorerAutoHide(false);
        Assert.False(window.IsExplorerFlyoutOpen);
        Assert.Same(Pane(window), Host(window, "LeftDockHost").Child);
        window.Close();
    }

    [AvaloniaFact]
    public void Hide_ThenShowExplorer_RestoresPane()
    {
        var window = Create();
        window.DockExplorer(PaneLayout.Right);
        window.HideExplorer();
        Assert.Null(Pane(window).Parent);
        Assert.False(window.ExplorerLayout.Visible);

        window.ShowExplorer();
        Assert.Same(Pane(window), Host(window, "RightDockHost").Child);
        window.Close();
    }

    [AvaloniaFact]
    public void ViewMenu_ObjectExplorer_UsesF8()
    {
        var window = Create();
        Assert.Equal("F8", window.FindControl<MenuItem>("MenuObjectExplorer")!.HotKey?.ToString());
        window.Close();
    }
}
