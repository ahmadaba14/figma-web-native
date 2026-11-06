using System.Windows;
using FigmaShell.Interop;
using FigmaShell.Services;
using FigmaShell.Web;

namespace FigmaShell;

public partial class App : Application
{
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstance("FigmaShell");
        if (!_singleInstance.IsFirstInstance)
        {
            _singleInstance.TrySendToFirstInstance(e.Args);
            Shutdown();
            return;
        }

        // Kick off browser-process startup immediately; it runs in parallel with window creation.
        var environment = WebViewFactory.CreateEnvironmentAsync();

        var session = SessionStore.Load();
        var startupUrls = e.Args.Where(UrlRouter.IsFigmaUrl).ToList();

        var window = new MainWindow(environment, session, startupUrls);
        MainWindow = window;
        window.Show();

        _singleInstance.ArgumentsReceived += args =>
            Dispatcher.BeginInvoke(() => window.HandleExternalLaunch(args));
        _singleInstance.StartListening();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
