using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Tickbird.Core.History;
using Tickbird.Core.Settings;
using Tickbird.Desktop.Services;
using Tickbird.Desktop.Views.Requests;

namespace Tickbird.Desktop.Views;

/// <summary>
/// Query History tab: every query run from an editor, newest first. Search by text, filter by connection,
/// and reopen a query in a new tab (double-click or Enter).
/// </summary>
public sealed class QueryHistoryView : UserControl
{
    private const string AllConnections = "All connections";
    private static readonly IBrush Accent = Brush.Parse("#1a65a7");

    private readonly QueryHistory history;
    private readonly Func<QueryHistoryEntry, Task> open;
    private readonly DataGrid grid = GridFactory.Create();
    private readonly TextBox search = new() { Watermark = "Search queries, connections and databases…", MinWidth = 280, FontSize = 12 };
    private readonly ComboBox connectionBox = new() { MinWidth = 180, FontSize = 12 };
    private readonly TextBox preview = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, FontSize = 12,
        FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"), Margin = new Thickness(0, 8, 0, 0)
    };
    private readonly TextBlock status = new() { FontSize = 12, Foreground = Accent, Margin = new Thickness(2, 6, 2, 0) };
    private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool binding;

    public QueryHistoryView(QueryHistory history, Func<QueryHistoryEntry, Task> open)
    {
        this.history = history;
        this.open = open;
        Background = Brush.Parse("#F1F7FD");

        GridFactory.SetColumns(grid, new[] { "Run at", "Connection", "Database", "Status", "Duration", "Rows", "Query" });
        grid.Columns[^1].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        grid.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        grid.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await OpenSelectedAsync(); }
            else if (e.Key == Key.Delete) { e.Handled = true; DeleteSelected(); }
        };
        grid.SelectionChanged += (_, _) =>
        {
            var entry = Selected().FirstOrDefault();
            preview.Text = entry == null ? "" : entry.Query + (entry.Error != null ? "\n\n-- Error: " + entry.Error : "");
        };

        searchDelay.Tick += (_, _) => { searchDelay.Stop(); Refresh(); };
        search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        search.KeyDown += (_, e) => { if (e.Key == Key.Escape) search.Text = ""; };
        connectionBox.SelectionChanged += (_, _) => { if (!binding) Refresh(); };

        var openButton = Action("Open in New Query", "Open the selected query in a new query tab (double-click or Enter)", async () => await OpenSelectedAsync());
        var copy = Action("Copy", "Copy the selected queries", async () => await CopySelectedAsync());
        var delete = Action("Delete", "Remove the selected entries (Delete)", () => { DeleteSelected(); return Task.CompletedTask; });
        var clear = Action("Clear All", "Remove every entry from the history", () => { history.Clear(); return Task.CompletedTask; });

        var export = Action("Export…", "Save the selected queries, or all of them when none are selected, to a file", async () => await ExportAsync());
        var import = Action("Import…", "Add the queries of an exported history file", async () => await ImportAsync());

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var control in new Control[] { search, connectionBox, openButton, copy, delete, clear, export, import })
        {
            control.Margin = new Thickness(0, 2, 8, 2);
            control.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(control);
        }

        var layout = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        layout.Children.Add(toolbar);
        layout.Children.Add(status);
        layout.Children.Add(preview);
        layout.Children.Add(new Border
        {
            Background = Brushes.White, BorderBrush = Brush.Parse("#e5eef5"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = grid
        });
        Content = layout;

        Refresh();
        AttachedToVisualTree += (_, _) => { history.Changed += Refresh; Refresh(); };
        DetachedFromVisualTree += (_, _) => history.Changed -= Refresh;
    }

    /// <summary>The rows shown (exposed for tests).</summary>
    public IReadOnlyList<QueryHistoryEntry> Shown { get; private set; } = Array.Empty<QueryHistoryEntry>();

    /// <summary>Sets the search text and refreshes at once (for tests).</summary>
    public void SearchFor(string text)
    {
        search.Text = text;
        searchDelay.Stop();
        Refresh();
    }

    public void Refresh()
    {
        BindConnections();
        var name = connectionBox.SelectedItem as string;
        var connectionId = name == null || name == AllConnections
            ? null
            : history.Entries.FirstOrDefault(e => e.ConnectionName == name)?.ConnectionId;
        Shown = history.Search(search.Text, connectionId);
        var settings = TickbirdApp.Current.Settings;
        var timeFormat = $"{TickbirdSettings.ResolveDateFormat(settings.DateFormat)} {settings.TimeFormat}";
        GridFactory.SetRows(grid, Shown.Select(e => new GridRow(new[]
        {
            e.RunAtUtc.ToLocalTime().ToString(timeFormat, CultureInfo.CurrentCulture),
            e.ConnectionName,
            e.Database,
            StatusText(e.Status),
            FormatDuration(e.DurationMs),
            e.Status == QueryRunStatus.Succeeded ? e.Rows.ToString("N0", CultureInfo.CurrentCulture) : "",
            e.OneLine.Length > 400 ? e.OneLine[..399] + "…" : e.OneLine
        }, e)));
        status.Text = history.Entries.Count == 0
            ? (settings.KeepQueryHistory ? "No queries yet. Queries you run from a query tab appear here." : "Query history is off (Settings › Keep Query History).")
            : $"{Shown.Count:N0} of {history.Entries.Count:N0} queries";
    }

    private void BindConnections()
    {
        binding = true;
        try
        {
            var selected = connectionBox.SelectedItem as string ?? AllConnections;
            var names = new List<string> { AllConnections };
            names.AddRange(history.Entries.Select(e => e.ConnectionName).OfType<string>().Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            connectionBox.ItemsSource = names;
            connectionBox.SelectedItem = names.Contains(selected) ? selected : AllConnections;
        }
        finally
        {
            binding = false;
        }
    }

    private List<QueryHistoryEntry> Selected() =>
        grid.SelectedItems.OfType<GridRow>().Select(r => r.Tag).OfType<QueryHistoryEntry>().ToList();

    private async Task OpenSelectedAsync()
    {
        foreach (var entry in Selected().Take(10)) await open(entry);
    }

    private async Task CopySelectedAsync()
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, Selected().Select(e => e.Query.TrimEnd()));
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && text.Length > 0) await clipboard.SetTextAsync(text);
    }

    private async Task ExportAsync()
    {
        var selected = Selected();
        var file = await FilePicker.SaveAsync(this, $"tickbird-query-history-{DateTime.Now:yyyyMMdd}.json", "Query history", "*.json");
        if (file == null) return;
        try
        {
            var count = history.Export(file, selected.Count > 0 ? selected : null);
            status.Text = $"Exported {count:N0} {(count == 1 ? "query" : "queries")} to {file}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (TopLevel.GetTopLevel(this) is Window owner) await Dialogs.ShowErrorAsync(owner, ex.Message, "Export Failed");
        }
    }

    private async Task ImportAsync()
    {
        var file = await FilePicker.OpenAsync(this, "Query history", "*.json");
        if (file == null) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        try
        {
            var result = history.Import(file);
            Refresh();
            status.Text = $"Imported {result.Added:N0} new and {result.Updated:N0} updated queries ({result.Skipped:N0} skipped).";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (owner != null) await Dialogs.ShowErrorAsync(owner, ex.Message, "Import Failed");
        }
    }

    private void DeleteSelected() => history.Remove(Selected().Select(e => e.Id));

    private static string StatusText(QueryRunStatus status) => status switch
    {
        QueryRunStatus.Failed => "Error",
        QueryRunStatus.Cancelled => "Cancelled",
        QueryRunStatus.TimedOut => "Timed out",
        _ => "OK"
    };

    private static string FormatDuration(double ms) =>
        ms < 1000 ? $"{ms:0} ms" : ms < 60000 ? $"{ms / 1000:0.0} s" : $"{(int)(ms / 60000)} min {ms % 60000 / 1000:0} s";

    private static Button Action(string text, string tip, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12 };
        ToolTip.SetTip(button, tip);
        button.Click += async (_, _) => await action();
        return button;
    }
}
