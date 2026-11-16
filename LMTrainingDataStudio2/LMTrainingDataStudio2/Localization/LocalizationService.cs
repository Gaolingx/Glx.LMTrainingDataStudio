using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace LMTrainingDataStudio2.Localization;

/// <summary>
/// Loads embedded zh-CN / en-US dictionaries and raises <see cref="Version"/>
/// changes so XAML bindings created by <see cref="LocExtension"/> refresh in place.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public const string English = "en-US";
    public const string Chinese = "zh-CN";

    private readonly Dictionary<string, string> _english;
    private readonly Dictionary<string, string> _chinese;
    private string _languageCode;
    private int _version;

    public static LocalizationService Instance { get; } = new();

    private LocalizationService()
    {
        _english = LoadDictionary(English);
        _chinese = LoadDictionary(Chinese);
        _languageCode = ResolveLanguage(LanguageCatalog.System);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage => _languageCode;

    /// <summary>Increments on every language change; bound by localized text.</summary>
    public int Version => _version;

    public bool IsChinese => _languageCode == Chinese;

    public string this[string key] => GetString(key);

    public static string L(string key) => Instance.GetString(key);

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var dictionary = _languageCode == Chinese ? _chinese : _english;
        if (dictionary.TryGetValue(key, out var value))
        {
            return value;
        }

        return _english.TryGetValue(key, out value) ? value : key;
    }

    public void SetLanguage(string? language)
    {
        var code = ResolveLanguage(language);
        if (string.Equals(code, _languageCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _languageCode = code;
        _version++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChinese)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resolves a persisted language code; unknown values fall back to the OS UI language.</summary>
    public static string ResolveLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language) &&
            !string.Equals(language, LanguageCatalog.System, StringComparison.OrdinalIgnoreCase))
        {
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return Chinese;
            }

            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return English;
            }
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
            "zh", StringComparison.OrdinalIgnoreCase)
            ? Chinese
            : English;
    }

    private static Dictionary<string, string> LoadDictionary(string language)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Localization.{language}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
