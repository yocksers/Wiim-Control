using Avalonia;
using Avalonia.Media;

namespace WiimControl;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (CommandChannel.ParseCommand(args) is { } command)
        {
            if (CommandChannel.Send(command)) return 0;
            Console.Error.WriteLine("Wiim Control isn't running.");
            return 1;
        }

        using var mutex = new Mutex(false, "Local\\WiimControl.SingleInstance");
        bool owned;
        try { owned = mutex.WaitOne(args.Contains("--restart") ? TimeSpan.FromSeconds(15) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            CommandChannel.Send("show");
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .LogToTrace();
}
