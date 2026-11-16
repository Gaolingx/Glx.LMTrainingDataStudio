namespace LMTrainingDataStudio2.Localization;

public sealed record LanguageOption(string Code, string DisplayName);

public static class LanguageCatalog
{
    public const string System = "system";

    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new(System, "跟随系统 / System"),
        new(LocalizationService.Chinese, "简体中文"),
        new(LocalizationService.English, "English")
    ];

    public static bool IsSupported(string? code) =>
        string.IsNullOrWhiteSpace(code) ||
        code.Equals(System, StringComparison.OrdinalIgnoreCase) ||
        code.Equals(LocalizationService.Chinese, StringComparison.OrdinalIgnoreCase) ||
        code.Equals(LocalizationService.English, StringComparison.OrdinalIgnoreCase);

    public static LanguageOption Resolve(string? code) =>
        All.FirstOrDefault(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? All[0];
}
