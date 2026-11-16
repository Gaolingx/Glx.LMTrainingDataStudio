using Avalonia;
using Avalonia.Styling;

namespace LMTrainingDataStudio2.Services;

public static class ThemeManager
{
    public static void Apply(bool dark)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}