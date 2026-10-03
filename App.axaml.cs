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
            desktop.Exit += (_, _) => _controller.Dispose();
            Dispatcher.UIThread.Post(async () => await _controller.StartAsync());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
