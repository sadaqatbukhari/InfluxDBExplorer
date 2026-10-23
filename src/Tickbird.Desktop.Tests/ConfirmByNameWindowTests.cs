using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Tickbird.Desktop.Services;

namespace Tickbird.Desktop.Tests;

public class ConfirmByNameWindowTests
{
    private static (Window Owner, ConfirmByNameWindow Dialog, Task<bool> Result) Open(string name)
    {
        var owner = new Window();
        owner.Show();
        var dialog = new ConfirmByNameWindow("Drop Database", "Drop database?", name, "Drop");
        var result = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        return (owner, dialog, result);
    }

    [AvaloniaFact]
    public void DropButton_EnabledOnlyForExactName()
    {
        var (owner, dialog, _) = Open("telegraf");
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.Input.Text = "Telegraf";
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.Input.Text = "telegraf";
        Assert.True(dialog.ConfirmButton.IsEnabled);
        dialog.Input.Text = "telegraf2";
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.Close(false);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Confirm_ReturnsTrue_OnlyAfterMatchingName()
    {
        var (owner, dialog, result) = Open("cpu");
        dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // still disabled: ignored
        Dispatcher.UIThread.RunJobs();
        Assert.False(result.IsCompleted);
        dialog.Input.Text = "cpu";
        dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(await result.WaitAsync(TimeSpan.FromSeconds(5)));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Enter_ConfirmsOnlyWhenNameMatches()
    {
        var (owner, dialog, result) = Open("cpu");
        dialog.Input.Text = "cp";
        dialog.Input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.False(result.IsCompleted);
        dialog.Input.Text = "cpu";
        dialog.Input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.True(await result.WaitAsync(TimeSpan.FromSeconds(5)));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Cancel_ReturnsFalse()
    {
        var (owner, dialog, result) = Open("cpu");
        dialog.Input.Text = "cpu";
        dialog.Close(false);
        Dispatcher.UIThread.RunJobs();
        Assert.False(await result.WaitAsync(TimeSpan.FromSeconds(5)));
        owner.Close();
    }
}
