using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tickbird.Core.Mapping;

namespace Tickbird.Desktop.Views.Requests;

/// <summary>
/// One editable condition (column, operator, value, remove) — shared by the result filter, map rules and map layers.
/// Typed values apply on Enter or when the box loses focus, so nothing is recomputed on every keystroke.
/// </summary>
public static class ConditionEditor
{
    private static readonly IBrush Accent = Brush.Parse("#1a65a7");
    public static readonly string[] OperatorNames = Enum.GetValues<MapConditionOperator>().Select(MapCondition.Symbol).ToArray();

    /// <param name="edited">Called after a change; true when the list itself changed (a condition was removed).</param>
    public static Control Row(List<MapCondition> conditions, int index, IReadOnlyList<string> columns, Action<bool> edited, string firstWord = "If")
    {
        var condition = conditions[index];
        var column = new ComboBox { ItemsSource = columns, MinWidth = 130, FontSize = 12 };
        column.SelectedItem = columns.FirstOrDefault(c => string.Equals(c, condition.Column, StringComparison.OrdinalIgnoreCase));
        var op = new ComboBox { ItemsSource = OperatorNames, SelectedIndex = (int)condition.Operator, MinWidth = 96, FontSize = 12 };
        var value = new TextBox { Text = condition.Value, Watermark = "value", Width = 130, FontSize = 12, IsVisible = MapCondition.NeedsValue(condition.Operator) };
        ToolTip.SetTip(value, "Numbers compare as numbers; text compares ignoring case. Press Enter to apply.");

        column.SelectionChanged += (_, _) =>
        {
            if (column.SelectedItem is not string name || name == condition.Column) return;
            condition.Column = name;
            edited(false);
        };
        op.SelectionChanged += (_, _) =>
        {
            if (op.SelectedIndex < 0 || (MapConditionOperator)op.SelectedIndex == condition.Operator) return;
            condition.Operator = (MapConditionOperator)op.SelectedIndex;
            value.IsVisible = MapCondition.NeedsValue(condition.Operator);
            edited(false);
        };
        OnCommit(value, () =>
        {
            if (value.Text == condition.Value) return;
            condition.Value = value.Text;
            edited(false);
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(new TextBlock { Text = index == 0 ? firstWord : "and", FontSize = 12, Foreground = Accent, Width = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(column);
        row.Children.Add(op);
        row.Children.Add(value);
        row.Children.Add(SmallButton("✕", "Remove condition", true, () =>
        {
            conditions.RemoveAt(index);
            edited(true);
        }));
        return row;
    }

    /// <summary>Runs <paramref name="commit"/> when the user presses Enter or leaves the box (not on every keystroke).</summary>
    public static void OnCommit(TextBox box, Action commit)
    {
        box.LostFocus += (_, _) => commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            commit();
            e.Handled = true;
        };
    }

    public static Button SmallButton(string text, string tip, bool enabled, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(6, 1), FontSize = 11, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
}
