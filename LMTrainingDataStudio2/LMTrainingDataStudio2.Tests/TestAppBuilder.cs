using Avalonia;
using Avalonia.Headless;
using LMTrainingDataStudio2;
using LMTrainingDataStudio2.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace LMTrainingDataStudio2.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}