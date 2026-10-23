using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;

namespace Tickbird.Desktop.Services;

/// <summary>A single compact row; controls that do not fit are available in the More menu.</summary>
public sealed class ResponsiveToolBar : UserControl
{
    private readonly ToolBarPanel row = new() { Spacing = 8 };
    private readonly Button more = new() { Content = "⋯", Width = 30, IsVisible = false };
    private readonly StackPanel overflow = new() { Spacing = 8, Margin = new Thickness(4) };
    private readonly Flyout flyout;
    private readonly List<(int Index, Control Control, Control Placeholder)> moved = new();

    public ResponsiveToolBar()
    {
        Compact(more);
        more.Name = "MoreCommands";
        ToolTip.SetTip(more, "More commands");
        flyout = new Flyout
        {
            Content = new ScrollViewer { Content = overflow, MaxHeight = 420 },
            Placement = PlacementMode.BottomEdgeAlignedRight
        };
        more.Flyout = flyout;
        flyout.Opening += (_, _) => MoveToMenu();
        flyout.Closed += (_, _) => RestoreControls();
        row.OverflowChanged += (_, _) => more.IsVisible = row.HasOverflow;
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(more, 1);
        layout.Children.Add(row);
        layout.Children.Add(more);
        Content = layout;
        Margin = new Thickness(0, 0, 0, 6);
        // Return popup controls before recalculating which commands fit at the new width.
        SizeChanged += (_, e) => { if (e.WidthChanged) flyout.Hide(); };
        DetachedFromVisualTree += (_, _) => { flyout.Hide(); RestoreControls(); };
    }

    public void Add(Control control)
    {
        Compact(control);
        row.Children.Add(control);
    }

    /// <summary>Keep the label and its editor together when they move into the menu.</summary>
    public void Add(Control label, Control editor)
    {
        Compact(editor);
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        group.Children.Add(label);
        group.Children.Add(editor);
        group.Bind(IsVisibleProperty, new Binding(nameof(IsVisible)) { Source = editor });
        row.Children.Add(group);
    }

    private static void Compact(Control control)
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        if (control is TemplatedControl templated)
        {
            templated.FontSize = 12;
            templated.MinHeight = 26;
            templated.Height = 26;
            if (control is Button or ComboBox) templated.Padding = new Thickness(8, 2);
        }
    }

    private void MoveToMenu()
    {
        RestoreControls();
        for (var i = row.VisibleCount; i < row.Children.Count; i++)
        {
            var control = row.Children[i];
            if (!control.IsVisible) continue;
            // Preserve the measured slot so opening the popup does not change the toolbar layout.
            var placeholder = new Border { Width = control.DesiredSize.Width, Height = control.DesiredSize.Height };
            placeholder.Bind(IsVisibleProperty, new Binding(nameof(IsVisible)) { Source = control });
            row.Children.RemoveAt(i);
            row.Children.Insert(i, placeholder);
            overflow.Children.Add(control);
            moved.Add((i, control, placeholder));
        }
    }

    private void RestoreControls()
    {
        foreach (var item in moved)
        {
            overflow.Children.Remove(item.Control);
            row.Children.Remove(item.Placeholder);
            row.Children.Insert(item.Index, item.Control);
        }
        moved.Clear();
    }
}
