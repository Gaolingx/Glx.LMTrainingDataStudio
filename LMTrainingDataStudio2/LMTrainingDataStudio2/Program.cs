using Avalonia;
using System;
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

        var recipePath = Array.Find(args, IsRecipePath);

        using var singleInstance = new SingleInstanceService();
        if (!singleInstance.IsFirstInstance)
        {
            if (recipePath is not null)
            {
                SingleInstanceService.TrySendToPrimary(recipePath);
            }

            return;
        }

        if (recipePath is not null)
        {
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

    private static bool IsRecipePath(string argument) =>
        !string.IsNullOrWhiteSpace(argument) &&
        string.Equals(Path.GetExtension(argument), ".json", StringComparison.OrdinalIgnoreCase);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
