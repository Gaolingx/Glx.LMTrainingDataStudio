using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LMTrainingDataStudio2.Views;

public enum MessageDialogButtons
{
    Ok,
    OkCancel,
    SaveDiscardCancel
}

public enum MessageDialogResult
{
    Ok,
    Save,
    Discard,
    Cancel
}

public partial class MessageDialog : Window
{
    private MessageDialogResult _result = MessageDialogResult.Cancel;

    public MessageDialog()
    {
        InitializeComponent();
    }

    public static async Task<MessageDialogResult> ShowAsync(
        Window owner,
        string heading,
        string message,
        MessageDialogButtons buttons)
    {
        var dialog = new MessageDialog { Title = heading };
        dialog.HeadingText.Text = heading;
        dialog.MessageText.Text = message;

        var confirm = buttons == MessageDialogButtons.SaveDiscardCancel;
        dialog.SaveButton.IsVisible = confirm;
        dialog.DiscardButton.IsVisible = confirm;
        dialog.CancelButton.IsVisible = buttons != MessageDialogButtons.Ok;
        dialog.OkButton.IsVisible = !confirm;

        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _result = MessageDialogResult.Save;
        Close();
    }

    private void OnDiscardClick(object? sender, RoutedEventArgs e)
    {
        _result = MessageDialogResult.Discard;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _result = MessageDialogResult.Cancel;
        Close();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        _result = MessageDialogResult.Ok;
        Close();
    }
}
