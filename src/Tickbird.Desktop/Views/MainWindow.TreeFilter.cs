using Avalonia.Input;
using Avalonia.Threading;
using Tickbird.Core.Navigation;
using Tickbird.Desktop.ViewModels;

namespace Tickbird.Desktop.Views;

/// <summary>
/// Object Explorer filter box: filters connections, databases and measurements as you type (Ctrl+E to focus,
/// Esc to clear). Databases whose measurements were not loaded yet are loaded in the background so they can match.
/// </summary>
public partial class MainWindow
{
    private const int MaxNodesLoadedForFilter = 200;
    private readonly DispatcherTimer treeFilterDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private int treeFilterVersion;

    private void InitTreeFilter()
    {
        treeFilterDelay.Tick += async (_, _) =>
        {
            treeFilterDelay.Stop();
            await ApplyTreeFilterAsync(TreeFilter.Text);
        };
        TreeFilter.TextChanged += (_, _) =>
        {
            TreeFilterClear.IsVisible = !string.IsNullOrEmpty(TreeFilter.Text);
            treeFilterDelay.Stop();
            treeFilterDelay.Start();
        };
        TreeFilter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !string.IsNullOrEmpty(TreeFilter.Text))
            {
                e.Handled = true;
                TreeFilter.Text = "";
            }
            else if (e.Key == Key.Down || e.Key == Key.Enter)
            {
                // Move into the tree, on the first match.
                e.Handled = true;
                var first = FirstShownMatch(Vm.Nodes);
                if (first != null) Vm.SelectedNode = first;
                Tree.Focus();
            }
        };
        TreeFilterClear.Click += (_, _) =>
        {
            TreeFilter.Text = "";
            TreeFilter.Focus();
        };
    }

    /// <summary>Applies the filter now (also used by tests), then loads unloaded databases so their measurements can match.</summary>
    public async Task ApplyTreeFilterAsync(string? text)
    {
        var version = ++treeFilterVersion;
        Vm.ApplyTreeFilter(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            Vm.Status = "";
            return;
        }
        ReportTreeMatches();

        // Load what is not loaded yet: databases of connected servers, then their measurements.
        var pending = Unloaded(Vm.Nodes).Take(MaxNodesLoadedForFilter).ToList();
        if (pending.Count == 0) return;
        Vm.Status = $"Searching {pending.Count} unloaded item(s) for \"{text!.Trim()}\"…";
        foreach (var node in pending)
        {
            if (version != treeFilterVersion) return;          // the user typed something else meanwhile
            try { await ExpandNodeChildren(node); }
            catch { /* unreachable server or no permission: that node simply has no matches */ }
            foreach (var child in Unloaded(new[] { node }).Where(c => c != node).Take(MaxNodesLoadedForFilter))
            {
                if (version != treeFilterVersion) return;
                try { await ExpandNodeChildren(child); }
                catch { }
            }
        }
        if (version != treeFilterVersion) return;
        Vm.ReapplyTreeFilter();
        ReportTreeMatches();
    }

    private void ReportTreeMatches()
    {
        var matches = Vm.CountTreeMatches();
        Vm.Status = matches == 0 ? $"No connections, databases or measurements match \"{Vm.TreeFilter}\"." : $"{matches} match(es) for \"{Vm.TreeFilter}\".";
    }

    /// <summary>Connection and database nodes still showing their "loading…" placeholder.</summary>
    private static IEnumerable<TreeNodeViewModel> Unloaded(IEnumerable<TreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Kind is NodeKind.Connection or NodeKind.Database && node.NeedsLoad) yield return node;
            foreach (var child in Unloaded(node.Children)) yield return child;
        }
    }

    private TreeNodeViewModel? FirstShownMatch(IEnumerable<TreeNodeViewModel> nodes)
    {
        foreach (var node in nodes.Where(n => n.IsShown && n.Kind != NodeKind.Loading))
        {
            if (node.Kind != NodeKind.Group && node.Name.Contains(Vm.TreeFilter, StringComparison.OrdinalIgnoreCase)) return node;
            var below = FirstShownMatch(node.Children);
            if (below != null) return below;
        }
        return null;
    }
}
