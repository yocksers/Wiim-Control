using System.IO.Pipes;

namespace WiimControl;

static class CommandChannel
{
    private static readonly string PipeName = $"WiimControl.{Environment.UserName}";

    private static readonly Dictionary<string, string> Arguments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--volume-up"] = "vol++",
        ["--volume-down"] = "vol--",
        ["--mute"] = "mute",
        ["--play-pause"] = "playpause",
        ["--next"] = "next",
        ["--previous"] = "prev",
        ["--stop"] = "stop",
        ["--settings"] = "show"
    };

    public static IReadOnlyDictionary<string, string> SupportedArguments => Arguments;

    public static string? ParseCommand(string[] args) =>
        args.Select(a => Arguments.TryGetValue(a, out var command) ? command : null).FirstOrDefault(c => c != null);

    public static bool Send(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000);
            using var writer = new StreamWriter(client);
            writer.WriteLine(command);
            writer.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void StartServer(Action<string> onCommand, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    while (await reader.ReadLineAsync(token) is { } line)
                        if (line.Length > 0) onCommand(line.Trim());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    await Task.Delay(500, CancellationToken.None);
                }
            }
        }, token);
    }
}
