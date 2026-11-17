using Avalonia.Controls;
using Avalonia.Interactivity;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Views;

public partial class SamplePreviewWindow : Window
{
    public SamplePreviewWindow()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(Window owner, SamplePreviewViewModel viewModel)
    {
        var window = new SamplePreviewWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
    }

    private async void OnOpenInDataClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SamplePreviewViewModel viewModel)
        {
            await viewModel.OpenInDataAsync();
            Close();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
