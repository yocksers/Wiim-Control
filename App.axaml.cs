using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace WiimControl;

public partial class App : Application
{
    private WiimController? _controller;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeEngine.Register(Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Write("UI", e.Exception);
                e.Handled = true;
                _controller?.ReportError();
            };
            _controller = new WiimController(desktop);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ErrorLog.Write("Background", e.Exception);
                e.SetObserved();
            };
            desktop.Exit += (_, _) => _controller.Dispose();
            Dispatcher.UIThread.Post(async () => await _controller.StartAsync());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
