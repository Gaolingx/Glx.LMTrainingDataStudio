using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;
using LMTrainingDataStudio2.Views;

namespace LMTrainingDataStudio2;

public partial class App : Application
{
    private static readonly object PendingGate = new();
    private static readonly Queue<string> PendingExternalOpens = new();
    private static string? _startupRecipePath;
    private static MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLog.Initialize();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Error("UI thread exception", e.Exception);
            var report = CrashReportService.WriteAutomaticCrashReport("UI thread exception", e.Exception);
            if (report is not null)
            {
                AppLog.Error($"Automatic crash report written to '{report}'.");
            }
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            _mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.MainWindow = _mainWindow;
            _ = RunStartupMaintenanceAsync(viewModel);

            lock (PendingGate)
            {
                while (PendingExternalOpens.Count > 0)
                {
                    var path = PendingExternalOpens.Dequeue();
                    _mainWindow.QueueExternalOpen(path);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunStartupMaintenanceAsync(MainWindowViewModel viewModel)
    {
        try
        {
            await viewModel.RunStartupMaintenanceAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Startup maintenance failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stores the recipe passed on the command line; the window opens it after restoring settings.
    /// </summary>
    public static void QueueStartupRecipe(string path) => _startupRecipePath = path;

    public static string? TakeStartupRecipe()
    {
        var path = _startupRecipePath;
        _startupRecipePath = null;
        return path;
    }

    /// <summary>
    /// Called from the single-instance pipe when another launch requests a recipe.
    /// </summary>
    public static void RequestOpenFromExternal(string path)
    {
        if (Current is App && _mainWindow is { } window)
        {
            Dispatcher.UIThread.Post(() => window.QueueExternalOpen(path));
            return;
        }

        lock (PendingGate)
        {
            PendingExternalOpens.Enqueue(path);
        }
    }
}

