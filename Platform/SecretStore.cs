using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WiimControl;

static class SecretStore
{
    private const string WindowsPrefix = "dpapi:";
    private const string KeyringPrefix = "keyring:";
    private const string PlainPrefix = "b64:";
    private const string KeyringService = "wiim-control";
    private static readonly Dictionary<string, string> StoredInKeyring = new();
    private static bool _keyringUnavailable;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt,
        int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt,
        int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static string Protect(string secret, string id)
    {
        if (secret.Length == 0)
        {
            Forget(id);
            return string.Empty;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        if (OperatingSystem.IsWindows() && Transform(bytes, protect: true) is { } sealedBytes)
            return WindowsPrefix + Convert.ToBase64String(sealedBytes);
        if (StoreInKeyring(id, secret)) return KeyringPrefix + id;
        return PlainPrefix + Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string stored)
    {
        try
        {
            if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored[PlainPrefix.Length..]));
            if (stored.StartsWith(WindowsPrefix, StringComparison.Ordinal) && OperatingSystem.IsWindows() &&
                Transform(Convert.FromBase64String(stored[WindowsPrefix.Length..]), protect: false) is { } plain)
                return Encoding.UTF8.GetString(plain);
            if (stored.StartsWith(KeyringPrefix, StringComparison.Ordinal))
            {
                string id = stored[KeyringPrefix.Length..];
                string? secret = ReadFromKeyring(id);
                if (secret != null) lock (StoredInKeyring) StoredInKeyring[id] = secret;
                return secret ?? string.Empty;
            }
        }
        catch (FormatException) { }
        return string.Empty;
    }

    public static void Forget(string id)
    {
        bool known;
        lock (StoredInKeyring) known = StoredInKeyring.Remove(id);
        if (!known) return;
        if (OperatingSystem.IsMacOS()) Run("security", ["delete-generic-password", "-s", KeyringService, "-a", id], null);
        else if (OperatingSystem.IsLinux()) Run("secret-tool", ["clear", "service", KeyringService, "account", id], null);
    }

    private static bool StoreInKeyring(string id, string secret)
    {
        lock (StoredInKeyring)
        {
            if (StoredInKeyring.TryGetValue(id, out var stored) && stored == secret) return true;
            if (_keyringUnavailable) return false;
        }
        bool ok = OperatingSystem.IsMacOS()
            ? StoreInMacKeychain(id, secret)
            : OperatingSystem.IsLinux() &&
              Run("secret-tool", ["store", "--label=Wiim Control shared folder", "service", KeyringService, "account", id], secret)?.ExitCode == 0;
        lock (StoredInKeyring)
        {
            if (ok) StoredInKeyring[id] = secret;
            else _keyringUnavailable = true;
        }
        return ok;
    }

    private static string MacQuote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static bool StoreInMacKeychain(string id, string secret)
    {
        if (secret.Contains('\n') || secret.Contains('\r')) return false;
        string command = $"add-generic-password -U -s {MacQuote(KeyringService)} -a {MacQuote(id)} -l {MacQuote("Wiim Control shared folder")} " +
                         $"-w {MacQuote(secret)}\n";
        return Run("security", ["-i"], command) != null && ReadFromKeyring(id) == secret;
    }

    private static string? ReadFromKeyring(string id)
    {
        var result = OperatingSystem.IsMacOS()
            ? Run("security", ["find-generic-password", "-s", KeyringService, "-a", id, "-w"], null)
            : OperatingSystem.IsLinux() ? Run("secret-tool", ["lookup", "service", KeyringService, "account", id], null) : null;
        return result is { ExitCode: 0 } ? result.Output.TrimEnd('\r', '\n') : null;
    }

    private sealed record ToolResult(int ExitCode, string Output);

    private static ToolResult? Run(string tool, IEnumerable<string> arguments, string? input)
    {
        try
        {
            var info = new ProcessStartInfo(tool)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process == null) return null;
            if (input != null) process.StandardInput.Write(input);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); }
                catch (InvalidOperationException) { }
                return null;
            }
            return new ToolResult(process.ExitCode, output.Result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return null; }
    }

    private static byte[]? Transform(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            bool ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output);
            if (!ok) return null;
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { handle.Free(); }
    }
}
