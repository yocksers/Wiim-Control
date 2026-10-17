using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SMBLibrary;
using SMBLibrary.Client;
using SmbFileAttributes = SMBLibrary.FileAttributes;

namespace WiimControl;

sealed record ShareProblem(string Message, bool LoginRefused);

sealed partial class WiimController
{
    private const string ShareSourceName = "Samba";
    private const string ShareScheme = "smb://";
    private const string ShareRootId = "0";
    private const int MaxCoverBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan ShareTimeout = TimeSpan.FromSeconds(10);

    private static readonly Dictionary<string, string> ShareAudioTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".flac"] = "audio/flac", [".m4a"] = "audio/mp4", [".aac"] = "audio/aac", [".wav"] = "audio/wav",
        [".aif"] = "audio/aiff", [".aiff"] = "audio/aiff", [".ogg"] = "audio/ogg", [".oga"] = "audio/ogg", [".opus"] = "audio/opus",
        [".wma"] = "audio/x-ms-wma", [".ape"] = "audio/ape", [".wv"] = "audio/wavpack", [".dsf"] = "audio/dsf", [".dff"] = "audio/dff"
    };

    private static readonly string[] ShareCoverNames =
        ["folder.jpg", "cover.jpg", "front.jpg", "album.jpg", "folder.png", "cover.png", "front.png", "album.png", "folder.jpeg", "cover.jpeg"];

    private List<SharedFolder> _sharedFolders = [];

    internal IEnumerable<MediaServer> ShareServers =>
        _sharedFolders.Select(f => new MediaServer(f.Name, ShareUdn(f), ShareScheme + ShareRoot(f), string.Empty, IsShare: true));

    internal static string ShareAddress(SharedFolder f) => @"\\" + string.Join('\\', new[] { f.Host, f.Share, f.Folder }.Where(p => p.Length > 0));

    private static string ShareRoot(SharedFolder f) =>
        string.Join('/', new[] { f.Host, f.Share, f.Folder.Replace('\\', '/') }.Where(p => p.Length > 0));

    private static string ShareUdn(SharedFolder f) => "smb:" + ShareRoot(f).ToLowerInvariant();

    private SharedFolder? ShareOf(MediaServer server) => _sharedFolders.FirstOrDefault(f => ShareUdn(f) == server.Udn);

    internal static bool IsShareUrl(string url) => url.StartsWith(ShareScheme, StringComparison.OrdinalIgnoreCase);

    internal static QueueTrack QueueTrackOf(MediaEntry e) =>
        new(e.Url, e.Title, e.Artist, e.Album, IsShareUrl(e.ArtUrl) ? string.Empty : e.ArtUrl, e.Duration, e.ProtocolInfo);

    internal static (string Host, string Share, string Folder)? ParseShareAddress(string text)
    {
        string value = text.Trim();
        if (value.StartsWith(ShareScheme, StringComparison.OrdinalIgnoreCase)) value = value[ShareScheme.Length..];
        var parts = value.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[0].Contains('@') || parts[0].Contains(' ')) return null;
        return (parts[0], parts[1], string.Join('\\', parts[2..]));
    }

    internal MediaServer AddSharedFolder(SharedFolder folder)
    {
        _sharedFolders.RemoveAll(f => ShareUdn(f) == ShareUdn(folder));
        _sharedFolders.Add(folder);
        SaveConfig();
        return ShareServers.First(s => s.Udn == ShareUdn(folder));
    }

    internal void RemoveSharedFolder(MediaServer server)
    {
        _sharedFolders.RemoveAll(f => ShareUdn(f) == server.Udn);
        SecretStore.Forget(server.Udn);
        SaveConfig();
    }

    internal static async Task<string?> TestSharedFolderAsync(SharedFolder folder) => (await CheckShareAsync(folder).ConfigureAwait(false))?.Message;

    private static async Task<ShareProblem?> CheckShareAsync(SharedFolder folder)
    {
        var (listing, error) = await WithShareAsync(folder, store => ListDirectory(store, folder.Folder)).ConfigureAwait(false);
        if (error != null) return error;
        return listing == null ? new ShareProblem($"{ShareAddress(folder)} wasn't found. Check the folder name.", false) : null;
    }

    internal async Task<ShareProblem?> CheckSharedFolderAsync(MediaServer server) =>
        ShareOf(server) is { } folder ? await CheckShareAsync(folder).ConfigureAwait(false) : new ShareProblem("This shared folder isn't in the list any more.", false);

    internal string? SharedFolderUser(MediaServer server) => ShareOf(server)?.User;

    internal void SetSharedFolderLogin(MediaServer server, string user, string password)
    {
        int index = _sharedFolders.FindIndex(f => ShareUdn(f) == server.Udn);
        if (index < 0) return;
        _sharedFolders[index] = _sharedFolders[index] with { User = user, Password = password };
        SaveConfig();
    }

    internal async Task<BrowsePage?> BrowseShareAsync(MediaServer server, string id, int start, int count)
    {
        if (ShareOf(server) is not { } folder) return null;
        string path = id == ShareRootId ? folder.Folder : id;
        var listing = WithShareAsync(folder, store => ListDirectory(store, path));
        var host = DeviceHostAsync(folder.Host);
        await Task.WhenAll(listing, host).ConfigureAwait(false);
        if (listing.Result.Result is not { } entries) return null;

        var visible = entries.Where(e => e.FileName is not ("." or "..") && !e.FileName.StartsWith('.') && !e.FileName.StartsWith('$') &&
                (e.FileAttributes & (SmbFileAttributes.Hidden | SmbFileAttributes.System)) == 0)
            .OrderBy(e => e.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        string album = path.Length > 0 ? path[(path.LastIndexOf('\\') + 1)..] : folder.Share;
        string? cover = ShareCoverNames.FirstOrDefault(name => visible.Any(e => !IsDirectory(e) && e.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)));
        string art = cover != null
            ? ShareUrl(folder.Host, folder.Share, JoinSharePath(path, visible.First(e => e.FileName.Equals(cover, StringComparison.OrdinalIgnoreCase)).FileName))
            : string.Empty;

        var folders = visible.Where(IsDirectory)
            .Select(e => new MediaEntry(JoinSharePath(path, e.FileName), e.FileName, true, null, "object.container.storageFolder",
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, TimeSpan.Zero));
        var tracks = visible.Where(e => !IsDirectory(e) && ShareAudioTypes.ContainsKey(Path.GetExtension(e.FileName)))
            .Select(e => new MediaEntry(JoinSharePath(path, e.FileName), Path.GetFileNameWithoutExtension(e.FileName), false, null,
                "object.item.audioItem.musicTrack", string.Empty, album, art,
                ShareUrl(host.Result, folder.Share, JoinSharePath(path, e.FileName), folder),
                $"smb:*:{ShareAudioTypes[Path.GetExtension(e.FileName)]}:*", TimeSpan.Zero));
        var all = folders.Concat(tracks).ToList();
        return new BrowsePage(all.Skip(start).Take(count).ToList(), all.Count);
    }

    private async Task<byte[]?> ReadShareFileAsync(string url)
    {
        var parts = url[ShareScheme.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length < 3) return null;
        var folder = _sharedFolders.FirstOrDefault(f => f.Host.Equals(parts[0], StringComparison.OrdinalIgnoreCase) &&
                         f.Share.Equals(parts[1], StringComparison.OrdinalIgnoreCase))
                     ?? new SharedFolder(parts[0], parts[0], parts[1], string.Empty, string.Empty, string.Empty);
        var (bytes, _) = await WithShareAsync(folder, store => ReadFile(store, string.Join('\\', parts[2..]))).ConfigureAwait(false);
        return bytes;
    }

    private static string JoinSharePath(string folder, string name) => folder.Length > 0 ? folder + '\\' + name : name;

    private static bool IsDirectory(FileDirectoryInformation e) => (e.FileAttributes & SmbFileAttributes.Directory) != 0;

    private static string ShareUrl(string host, string share, string path, SharedFolder? login = null)
    {
        string auth = string.Empty;
        if (login is { User.Length: > 0 })
        {
            var (domain, user) = SplitShareUser(login.User);
            auth = (domain.Length > 0 ? Uri.EscapeDataString(domain) + ";" : string.Empty) + Uri.EscapeDataString(user) +
                   (login.Password.Length > 0 ? ":" + Uri.EscapeDataString(login.Password) : string.Empty) + "@";
        }
        var segments = new[] { share }.Concat(path.Split('\\', StringSplitOptions.RemoveEmptyEntries)).Select(Uri.EscapeDataString);
        return ShareScheme + auth + host + "/" + string.Join('/', segments);
    }

    private static (string Domain, string User) SplitShareUser(string user) =>
        user.IndexOf('\\') is int slash and > 0 ? (user[..slash], user[(slash + 1)..]) : (string.Empty, user);

    private static async Task<string> DeviceHostAsync(string host)
    {
        if (IPAddress.TryParse(host, out _)) return host;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? host;
        }
        catch (SocketException) { return host; }
    }

    private static async Task<(T? Result, ShareProblem? Error)> WithShareAsync<T>(SharedFolder folder, Func<ISMBFileStore, T?> work) where T : class
    {
        var client = new SMB2Client((int)ShareTimeout.TotalMilliseconds / 2);
        var task = Task.Run(() => UseShare(folder, work, client));
        if (await Task.WhenAny(task, Task.Delay(ShareTimeout)).ConfigureAwait(false) != task)
        {
            try { client.Disconnect(); }
            catch (Exception) { }
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return (null, new ShareProblem($"{folder.Host} didn't answer in time.", false));
        }
        return await task.ConfigureAwait(false);
    }

    private static (T? Result, ShareProblem? Error) UseShare<T>(SharedFolder folder, Func<ISMBFileStore, T?> work, SMB2Client client) where T : class
    {
        bool loggedIn = false;
        try
        {
            var address = IPAddress.TryParse(folder.Host, out var ip) ? ip
                : Dns.GetHostAddresses(folder.Host).OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).FirstOrDefault();
            if (address == null || !client.Connect(address, SMBTransportType.DirectTCPTransport))
                return (null, new ShareProblem($"Couldn't reach {folder.Host}. Check the name or address and that it's switched on.", false));
            var (domain, user) = SplitShareUser(folder.User);
            var status = client.Login(domain, user, folder.Password);
            if (status != NTStatus.STATUS_SUCCESS)
                return (null, status is NTStatus.STATUS_LOGON_FAILURE or NTStatus.STATUS_ACCESS_DENIED or NTStatus.STATUS_ACCOUNT_RESTRICTION
                    ? new ShareProblem(folder.User.Length == 0
                        ? $"{folder.Host} needs a user name and password."
                        : $"{folder.Host} didn't accept the user name or password.", true)
                    : new ShareProblem($"Couldn't sign in to {folder.Host} ({status}).", false));
            loggedIn = true;
            var store = client.TreeConnect(folder.Share, out status);
            if (store == null || status != NTStatus.STATUS_SUCCESS)
                return (null, status == NTStatus.STATUS_BAD_NETWORK_NAME ? new ShareProblem($"{folder.Host} has no shared folder called {folder.Share}.", false)
                    : status == NTStatus.STATUS_ACCESS_DENIED ? new ShareProblem($"This user can't open {folder.Share} on {folder.Host}.", true)
                    : new ShareProblem($"Couldn't open {folder.Share} on {folder.Host} ({status}).", false));
            try { return (work(store), null); }
            finally { store.Disconnect(); }
        }
        catch (Exception ex)
        {
            return (null, new ShareProblem($"Couldn't reach {folder.Host}: {ex.Message}", false));
        }
        finally
        {
            try { if (loggedIn) client.Logoff(); }
            catch (Exception) { }
            client.Disconnect();
        }
    }

    private static List<FileDirectoryInformation>? ListDirectory(ISMBFileStore store, string path)
    {
        var status = store.CreateFile(out var handle, out _, path, AccessMask.GENERIC_READ, SmbFileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
        if (status != NTStatus.STATUS_SUCCESS) return null;
        try
        {
            status = store.QueryDirectory(out var list, handle, "*", FileInformationClass.FileDirectoryInformation);
            return status is NTStatus.STATUS_SUCCESS or NTStatus.STATUS_NO_MORE_FILES ? list.OfType<FileDirectoryInformation>().ToList() : null;
        }
        finally { store.CloseFile(handle); }
    }

    private static byte[]? ReadFile(ISMBFileStore store, string path)
    {
        var status = store.CreateFile(out var handle, out _, path, AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE, SmbFileAttributes.Normal,
            ShareAccess.Read, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (status != NTStatus.STATUS_SUCCESS) return null;
        try
        {
            using var stream = new MemoryStream();
            while (stream.Length < MaxCoverBytes)
            {
                status = store.ReadFile(out var data, handle, stream.Length, (int)store.MaxReadSize);
                if (status == NTStatus.STATUS_END_OF_FILE || data is not { Length: > 0 }) break;
                if (status != NTStatus.STATUS_SUCCESS) return null;
                stream.Write(data);
            }
            return stream.ToArray();
        }
        finally { store.CloseFile(handle); }
    }

    private string SerializeSharedFolders() =>
        JsonSerializer.Serialize(_sharedFolders.Select(f => f with { Password = SecretStore.Protect(f.Password, ShareUdn(f)) }).ToList());

    internal static List<SharedFolder> ParseSharedFolders(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<List<SharedFolder?>>(value)?
                .OfType<SharedFolder>()
                .Where(f => f.Host is { Length: > 0 } && f.Share is { Length: > 0 })
                .Select(f => new SharedFolder(f.Name ?? f.Share, f.Host, f.Share, f.Folder ?? string.Empty, f.User ?? string.Empty,
                    SecretStore.Unprotect(f.Password ?? string.Empty)))
                .ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }
}
