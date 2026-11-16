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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLog.Initialize();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
            AppLog.Error("UI thread exception", e.Exception);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
