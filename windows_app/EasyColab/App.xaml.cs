using Microsoft.UI.Xaml;

namespace EasyColab;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write(e.Exception); e.SetObserved(); };
    }

    public static MainWindow? MainAppWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        UiDispatcher.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _window = new MainWindow();
        MainAppWindow = _window;
        _window.Activate();
    }
}

internal static class CrashLog
{
    public static void Write(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var path = Path.Combine(LocalRuntimePaths.AppConfigDir, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
