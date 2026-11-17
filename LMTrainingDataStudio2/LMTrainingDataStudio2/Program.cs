using System;
using Avalonia;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            AppLog.Error("Unhandled exception", exception);
            var report = CrashReportService.WriteAutomaticCrashReport("Unhandled exception", exception);
            if (report is not null)
            {
                AppLog.Error($"Automatic crash report written to '{report}'.");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        var recipePaths = RecipeFileExtensions.FilterRecipePaths(args);
        var recipePath = recipePaths.Length > 0 ? recipePaths[0] : null;

        using var singleInstance = new SingleInstanceService();
        if (!singleInstance.IsFirstInstance)
        {
            if (recipePath is null)
            {
                return;
            }

            if (SingleInstanceService.TrySendToPrimary(recipePath))
            {
                return;
            }

            AppLog.Error(
                $"Could not forward '{recipePath}' to the running instance; starting a separate instance.");
        }

        if (recipePath is not null)
        {
            if (recipePaths.Length > 1)
            {
                AppLog.Error(
                    $"Only the first recipe path is opened: '{recipePath}'. " +
                    $"Ignored: {string.Join(", ", recipePaths.Skip(1).Select(path => $"'{path}'"))}.");
            }

            App.QueueStartupRecipe(recipePath);
        }

        singleInstance.StartServer(App.RequestOpenFromExternal);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Error("Fatal startup exception", ex);
            CrashReportService.WriteAutomaticCrashReport("Fatal startup exception", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
