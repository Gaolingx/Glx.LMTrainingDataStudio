using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace LMTrainingDataStudio2.Localization;

/// <summary>
/// XAML markup extension: <c>{loc:Loc key}</c>.
/// Binds to <see cref="LocalizationService.Version"/> so switching language
/// re-evaluates every localized attribute without reloading the view.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return string.Empty;
        }

        return new Binding(nameof(LocalizationService.Version))
        {
            Source = LocalizationService.Instance,
            Converter = new LocalizeKeyConverter(Key),
            Mode = BindingMode.OneWay
        };
    }
}

public sealed class LocalizeKeyConverter : IValueConverter
{
    private readonly string _key;

    public LocalizeKeyConverter(string key) => _key = key;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        LocalizationService.Instance[_key];

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
