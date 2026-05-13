using Avalonia.Controls;
using Avalonia.Input;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnBlockTemplatePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: BlockTemplateViewModel template }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var data = new DataObject();
        data.Set("application/x-lmtds-block-type", template.Type.ToString());
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }
}
