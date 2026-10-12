using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace WiimControl;

public partial class App : Application
{
    private WiimController? _controller;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _controller = new WiimController(desktop);
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Write("UI", e.Exception);
                e.Handled = true;
                _controller?.ReportError();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write("Fatal", e.ExceptionObject as Exception);
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
