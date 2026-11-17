using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Views;

public partial class ExecutionsView : UserControl
{
    public ExecutionsView()
    {
        InitializeComponent();
    }

    private void OnOpenOutputFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.Executions.SelectedRun?.OutputDirectory is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or InvalidOperationException)
        {
            vm.LogMessages.Add($"[Warning] Could not open output folder '{path}': {ex.Message}");
        }
    }
}
