using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;

namespace WiimControl;

sealed record AppUpdate(string Version, string Notes, string PageUrl, string DownloadUrl, long DownloadSize, string Sha256);

sealed partial class WiimController
{
    private const string ReleasesApi = "https://api.github.com/repos/yocksers/Wiim-Control/releases/latest";
    private const string ReleasesPage = "https://github.com/yocksers/Wiim-Control/releases/latest";

    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(24) };
    private bool _checkUpdates = true;
    private string _skippedVersion = string.Empty;
    private bool _updatePromptOpen;
    private string _promptedVersion = string.Empty;

    internal static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate.TrimStart('v', 'V'), out var a) && Version.TryParse(current.TrimStart('v', 'V'), out var b) && a > b;

    private static string AssetName(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (OperatingSystem.IsWindows()) return list.FirstOrDefault(n => n.Equals("WiimControlSetup.exe", StringComparison.OrdinalIgnoreCase)) ?? "";
        if (OperatingSystem.IsLinux()) return list.FirstOrDefault(n => n.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)) ?? "";
        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return list.FirstOrDefault(n => n.EndsWith($"macos-{arch}.zip", StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    internal async Task<AppUpdate?> GetLatestReleaseAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            request.Headers.UserAgent.ParseAdd($"WiimControl/{AppVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await UpdateHttp.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
            string version = (root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "").TrimStart('v', 'V');
            var assets = root.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToList() : [];
            string asset = AssetName(assets.Select(x => x.GetProperty("name").GetString() ?? ""));
            var chosen = assets.FirstOrDefault(x => x.GetProperty("name").GetString() == asset);
            return new AppUpdate(version,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                root.TryGetProperty("html_url", out var page) ? page.GetString() ?? ReleasesPage : ReleasesPage,
                asset.Length > 0 ? chosen.GetProperty("browser_download_url").GetString() ?? "" : "",
                asset.Length > 0 && chosen.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                asset.Length > 0 && chosen.TryGetProperty("digest", out var digest) && digest.GetString() is { } d &&
                d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? d[7..].ToLowerInvariant() : string.Empty);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void StartUpdateChecks()
    {
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync(manual: false);
        _updateTimer.Start();
        DispatcherTimer.RunOnce(() => _ = CheckForUpdateAsync(manual: false), TimeSpan.FromMinutes(1));
    }

    internal async Task CheckForUpdateAsync(bool manual)
    {
        if (!manual && !_checkUpdates) return;
        var release = await GetLatestReleaseAsync();
        if (release == null)
        {
            if (manual) await Dialogs.MessageAsync(_settingsWindow, "Couldn't check for updates. Check the internet connection and try again.", UiScale);
            return;
        }
        if (!IsNewer(release.Version, AppVersion))
        {
            if (manual) await Dialogs.MessageAsync(_settingsWindow, $"Wiim Control {AppVersion} is the newest version.", UiScale);
            return;
        }
        if (!manual && (release.Version == _skippedVersion || release.Version == _promptedVersion)) return;
        await OfferUpdateAsync(release);
    }

    private async Task OfferUpdateAsync(AppUpdate release)
    {
        if (_updatePromptOpen) return;
        _updatePromptOpen = true;
        _promptedVersion = release.Version;
        try
        {
            bool canInstall = OperatingSystem.IsWindows() && release.DownloadUrl.Length > 0;
            string notes = release.Notes.Trim();
            if (notes.Length > 600) notes = notes[..600].TrimEnd() + "…";
            string message = $"Wiim Control {release.Version} is available. You have {AppVersion}." +
                             (notes.Length > 0 ? $"\n\n{notes}" : "") +
                             (canInstall ? "\n\nDownload and install it now? Wiim Control closes during the installation and starts again afterwards."
                                         : "\n\nOpen the download page?");
            int choice = await Dialogs.ButtonsAsync(_settingsWindow, message, UiScale,
                canInstall ? "Download and install" : "Open download page", "Skip this version", "Later");
            if (choice == 1)
            {
                _skippedVersion = release.Version;
                SaveConfig();
                return;
            }
            if (choice != 0) return;
            if (canInstall) await DownloadAndInstallAsync(release);
            else OpenUrl(release.PageUrl);
        }
        finally
        {
            _updatePromptOpen = false;
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private async Task DownloadAndInstallAsync(AppUpdate release)
    {
        string path = Path.Combine(Path.GetTempPath(), $"WiimControlSetup-{release.Version}.exe");
        try
        {
            Notify($"Downloading Wiim Control {release.Version}…");
            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
            request.Headers.UserAgent.ParseAdd($"WiimControl/{AppVersion}");
            using (var response = await UpdateHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? release.DownloadSize;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(path);
                var buffer = new byte[81920];
                long done = 0;
                int shown = 0, read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    int quarter = total > 0 ? (int)(done * 4 / total) : 0;
                    if (quarter > shown && quarter < 4)
                    {
                        shown = quarter;
                        Notify($"Downloading Wiim Control {release.Version}… {quarter * 25}%");
                    }
                }
            }
            if (release.DownloadSize > 0 && new FileInfo(path).Length != release.DownloadSize)
                throw new IOException("The download is incomplete.");
            if (release.Sha256.Length > 0)
            {
                await using var file = File.OpenRead(path);
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The downloaded file doesn't match the release. It may be damaged.");
            }
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            Exit();
        }
        catch (Exception ex)
        {
            try { File.Delete(path); } catch (Exception) { }
            await Dialogs.MessageAsync(_settingsWindow, $"Couldn't download the update: {ex.Message}\n\nYou can download it from {release.PageUrl}", UiScale);
        }
    }

    private void SetCheckUpdates(bool value)
    {
        _checkUpdates = value;
        SaveConfig();
    }
}
