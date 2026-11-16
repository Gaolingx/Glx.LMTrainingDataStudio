using Avalonia.Controls;
using Avalonia.Interactivity;
using LMTrainingDataStudio2.Localization;

namespace LMTrainingDataStudio2.Views;

public partial class PresetSaveDialog : Window
{
    public bool Confirmed { get; private set; }

    public string PresetName => NameBox.Text?.Trim() ?? string.Empty;

    public string PresetDescription => DescriptionBox.Text?.Trim() ?? string.Empty;

    public PresetSaveDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public static async Task<(string Name, string Description)?> ShowAsync(
        Window owner,
        string initialName,
        string initialDescription)
    {
        var dialog = new PresetSaveDialog { Title = LocalizationService.L("preset.dialog.title") };
        dialog.NameBox.Text = initialName;
        dialog.DescriptionBox.Text = initialDescription;

        await dialog.ShowDialog(owner);
        return dialog.Confirmed ? (dialog.PresetName, dialog.PresetDescription) : null;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameError.Text = LocalizationService.L("preset.dialog.name_required");
            NameError.IsVisible = true;
            NameBox.Focus();
            return;
        }

        Confirmed = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
