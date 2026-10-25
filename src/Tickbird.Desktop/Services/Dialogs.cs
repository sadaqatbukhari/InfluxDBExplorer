using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tickbird.Core.Navigation;

namespace Tickbird.Desktop.Services;

/// <summary>Small modal dialog helpers (message box, confirmation, simple forms).</summary>
public static class Dialogs
{
    public static Task ShowMessageAsync(Window owner, string title, string message) =>
        new MessageWindow(title, message, ok: "OK", cancel: null).ShowDialog<bool>(owner);

    public static Task ShowErrorAsync(Window owner, string message, string title = "Error") =>
        ShowMessageAsync(owner, title, message);

    public static Task ShowExceptionAsync(Window owner, Exception ex, string title = "Unexpected Error") =>
        ShowMessageAsync(owner, title, ex.Message);

    public static async Task<bool> ConfirmAsync(Window owner, string title, string message) =>
        await new MessageWindow(title, message, ok: "OK", cancel: "Cancel").ShowDialog<bool>(owner);

    /// <summary>
    /// A confirmation with a check box under the message (unchecked at first). <paramref name="option"/> is null to
    /// leave the check box out. Returns whether the user confirmed and whether the box was checked.
    /// </summary>
    public static async Task<(bool Confirmed, bool Checked)> ConfirmWithOptionAsync(
        Window owner, string title, string message, string? option, string ok = "Delete")
    {
        var window = new OptionConfirmWindow(title, message, option, ok);
        var confirmed = await window.ShowDialog<bool>(owner);
        return (confirmed, confirmed && window.OptionBox.IsChecked == true);
    }

    /// <summary>
    /// Confirmation for destructive actions: the user must type <paramref name="name"/> exactly before
    /// the action button is enabled. Returns true only when confirmed.
    /// </summary>
    public static async Task<bool> ConfirmByNameAsync(Window owner, string title, string message, string name, string action = "Drop") =>
        await new ConfirmByNameWindow(title, message, name, action).ShowDialog<bool>(owner);

    /// <summary>The window behind <see cref="ConfirmWithOptionAsync"/>.</summary>
    public sealed class OptionConfirmWindow : Window
    {
        public CheckBox OptionBox { get; } = new();
        public Button ConfirmButton { get; } = new() { MinWidth = 80, IsDefault = true };

        public OptionConfirmWindow(string title, string message, string? option, string ok)
        {
            Title = title;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ConfirmButton.Content = ok;
            ConfirmButton.Click += (_, _) => Close(true);
            var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
                Children = { ConfirmButton, cancel }
            };
            var content = new StackPanel { Margin = new Thickness(4), Spacing = 14 };
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            if (option != null)
            {
                OptionBox.Content = option;
                content.Children.Add(OptionBox);
            }
            content.Children.Add(buttons);
            Content = TickbirdTheme.Card(content);
        }
    }

    private sealed class MessageWindow : Window
    {
        public MessageWindow(string title, string message, string ok, string? cancel)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            var okButton = new Button { Content = ok, MinWidth = 80, IsDefault = true };
            okButton.Click += (_, _) => Close(true);
            buttons.Children.Add(okButton);
            if (cancel != null)
            {
                var cancelButton = new Button { Content = cancel, MinWidth = 80, IsCancel = true };
                cancelButton.Click += (_, _) => Close(false);
                buttons.Children.Add(cancelButton);
            }
            Content = TickbirdTheme.Card(new StackPanel
            {
                Margin = new Thickness(4),
                Spacing = 16,
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons }
            });
        }
    }
}

/// <summary>A label/control form with OK/Cancel and optional validation (shown inline).</summary>
public sealed class FormDialog : Window
{
    private readonly TextBlock errorText = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button okButton;
    private readonly Grid footer;

    /// <summary>Extra buttons shown at the left of the footer (for example Test / Ping).</summary>
    public StackPanel LeftButtons { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 8 };

    public FormDialog(string title, string okText, params (string Label, Control Control)[] rows)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 12 };
        foreach (var (label, control) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var row = grid.RowDefinitions.Count - 1;
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(text);
            grid.Children.Add(control);
        }

        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Grid.SetColumn(rightButtons, 1);
        footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { LeftButtons, rightButtons } };
        okButton = new Button { Content = okText, MinWidth = 80, IsDefault = true };
        okButton.Click += (_, _) =>
        {
            var error = Validate?.Invoke();
            if (error != null) { errorText.Text = error; errorText.IsVisible = true; return; }
            Close(true);
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        rightButtons.Children.Add(okButton);
        rightButtons.Children.Add(cancel);

        Content = TickbirdTheme.Card(new StackPanel
        {
            Margin = new Thickness(4),
            Spacing = 12,
            Children =
            {
                grid,
                errorText,
                footer
            }
        });
    }

    /// <summary>Returns an error message to keep the dialog open, or null when the input is valid.</summary>
    public Func<string?>? Validate { get; set; }

    /// <summary>Replaces only the form presentation, retaining validation and footer actions.</summary>
    internal void SetBody(Control body)
    {
        if (errorText.Parent is Panel errorParent) errorParent.Children.Remove(errorText);
        if (footer.Parent is Panel footerParent) footerParent.Children.Remove(footer);
        Content = new StackPanel
        {
            Children =
            {
                body,
                new Border { Padding = new Thickness(24, 0, 24, 12), Child = errorText },
                new Border
                {
                    BorderBrush = TickbirdTheme.Line, BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(24, 16), Child = footer
                }
            }
        };
    }

    public async Task<bool> ShowAsync(Window owner) => await ShowDialog<bool>(owner);
}

public static class Ui
{
    public static TextBox Text(string? value = null, bool password = false, string? watermark = null) =>
        new() { Text = value, PasswordChar = password ? '•' : '\0', Watermark = watermark };

    public static NumericUpDown Number(decimal value, decimal min, decimal max) =>
        new() { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0" };
}

/// <summary>Type-the-name confirmation used before dropping a database, measurement or series.</summary>
public sealed class ConfirmByNameWindow : Window
{
    public ConfirmByNameWindow(string title, string message, string name, string action)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ConfirmButton = new Button
        {
            Content = action, MinWidth = 80, IsEnabled = false,
            Background = Brush.Parse("#C62828"), Foreground = Brushes.White
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        bool Matches() => Validation.NameMatches(Input.Text, name);
        // React to the Text property directly so the button state is always in step with what was typed.
        Input.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ConfirmButton.IsEnabled = Matches();
        };
        Input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || !Matches()) return;
            e.Handled = true;
            Close(true);
        };
        ConfirmButton.Click += (_, _) => { if (Matches()) Close(true); };
        cancel.Click += (_, _) => Close(false);
        Opened += (_, _) => Input.Focus();

        var prompt = new TextBlock { TextWrapping = TextWrapping.Wrap };
        prompt.Inlines = new InlineCollection
        {
            new Run("To confirm, type "),
            new Run(name) { FontWeight = FontWeight.Bold },
            new Run(" below:")
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Children = { ConfirmButton, cancel }
        };
        Content = TickbirdTheme.Card(new StackPanel
        {
            Margin = new Thickness(4),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "This cannot be undone.", Foreground = Brush.Parse("#C62828"), FontWeight = FontWeight.SemiBold },
                prompt,
                Input,
                buttons
            }
        });
    }

    /// <summary>Where the user types the name.</summary>
    public TextBox Input { get; } = new();

    /// <summary>Enabled only while the typed text matches the name exactly.</summary>
    public Button ConfirmButton { get; }
}
