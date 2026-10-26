namespace Tickbird.Desktop.ViewModels;

/// <summary>Tool tabs (query history) host a view that is not run or refreshed.</summary>
public enum TabKind { Query, ShowQueries, Request, Tool }

/// <summary>A workspace tab. The full title is the tooltip; the view caps the displayed width.</summary>
public sealed class TabViewModel : ViewModelBase
{
    public const double MaxTabWidth = 300;

    private string title;

    public TabViewModel(string id, string title, TabKind kind, string? connectionId = null, string? iconName = null, object? content = null)
    {
        Id = id; this.title = title; Kind = kind; ConnectionId = connectionId; IconName = iconName; Content = content;
    }

    public string Id { get; }
    public string Title { get => title; set => Set(ref title, value); }
    public TabKind Kind { get; }
    public string? ConnectionId { get; }
    public string? IconName { get; }

    /// <summary>The view hosted by the tab (a request view); typed as object to keep this layer UI-free.</summary>
    public object? Content { get; }

    /// <summary>Whether Run/Refresh applies to this tab.</summary>
    public bool IsRequest => Content != null && Kind != TabKind.Tool;

    public bool IsQuery => Kind == TabKind.Query;
}
