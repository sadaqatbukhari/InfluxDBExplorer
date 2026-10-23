using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Tickbird.Desktop.Services;

/// <summary>
/// A horizontal toolbar that resizes with the window, like the Visual Studio toolbars: buttons that do not
/// fit are left out (clipped) and reported through <see cref="VisibleCount"/> so they can be shown in an
/// overflow menu. Never ends on a separator.
/// </summary>
public sealed class ToolBarPanel : StackPanel
{
    private int fitCount = int.MaxValue;

    public ToolBarPanel()
    {
        Orientation = Orientation.Horizontal;
        ClipToBounds = true;
    }

    /// <summary>Number of leading children currently shown; the rest belong in the overflow menu.</summary>
    public int VisibleCount => Math.Min(fitCount, Children.Count);

    public bool HasOverflow => Children.Skip(VisibleCount).Any(c => c.IsVisible && c is not Separator);

    /// <summary>Raised (after layout) when the number of shown children changes.</summary>
    public event EventHandler? OverflowChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children;
        var height = 0.0;
        foreach (var child in children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var fit = children.Count;
        var width = 0.0;
        if (!double.IsInfinity(availableSize.Width))
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (!children[i].IsVisible) continue;
                var next = width + (width > 0 ? Spacing : 0) + children[i].DesiredSize.Width;
                if (next > availableSize.Width + 0.5) { fit = i; break; }
                width = next;
            }
        }
        while (fit > 0 && (children[fit - 1] is Separator || !children[fit - 1].IsVisible)) fit--;

        width = 0;
        for (var i = 0; i < fit; i++)
            if (children[i].IsVisible) width += (width > 0 ? Spacing : 0) + children[i].DesiredSize.Width;

        SetFitCount(fit);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var children = Children;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var w = child.DesiredSize.Width;
            if (i < fitCount && child.IsVisible)
            {
                child.Arrange(new Rect(x, 0, w, finalSize.Height));
                x += w + Spacing;
            }
            else
            {
                // Parked outside the clipped area: not drawn and not clickable.
                child.Arrange(new Rect(finalSize.Width + 10000, 0, w, finalSize.Height));
            }
        }
        return finalSize;
    }

    private void SetFitCount(int fit)
    {
        if (fit == fitCount) return;
        fitCount = fit;
        Dispatcher.UIThread.Post(() => OverflowChanged?.Invoke(this, EventArgs.Empty));
    }
}
