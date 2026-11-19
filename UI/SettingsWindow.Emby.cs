using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class LibraryPage
        {
            private async Task AddEmbyServerAsync()
            {
                var address = new TextBox { Watermark = "http://192.168.1.20:8096 or https://music.example.com", Width = 360 };
                var user = new TextBox { Width = 360 };
                var password = new TextBox { PasswordChar = '•', Width = 360 };
                var name = new TextBox { Watermark = "Optional", Width = 360 };
                var content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Add Emby or Jellyfin server", Classes = { "heading" } },
                        new TextBlock
                        {
                            Text = "The WiiM device plays the music straight from the server, so this computer doesn't need to stay on. " +
                                   "The server can be at home or on the internet, as long as the WiiM device can reach it.",
                            TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Classes = { "muted" }
                        },
                        ShareField("Server address", address),
                        ShareField("User name", user),
                        ShareField("Password", password),
                        ShareField("Name in the list", name)
                    }
                };
                ToolTip.SetTip(address, "Without a port, the usual Emby and Jellyfin port 8096 is also tried");

                while (true)
                {
                    (content.Parent as Panel)?.Children.Remove(content);
                    if (!await Dialogs.ChooseAsync(window, content, "Add", window._scale)) return;
                    string userName = user.Text?.Trim() ?? string.Empty;
                    if ((address.Text?.Trim() ?? string.Empty).Length == 0 || userName.Length == 0)
                    {
                        await Dialogs.MessageAsync(window, "Please enter the server address and your user name.", window._scale);
                        continue;
                    }
                    _lblLibraryStatus.Text = "Connecting…";
                    var info = await window._owner.GetEmbyServerInfoAsync(address.Text!);
                    if (info == null)
                    {
                        UpdateLibraryStatus();
                        await Dialogs.MessageAsync(window, "No Emby or Jellyfin server answered at that address. Check the address, including http:// or https:// and the port.", window._scale);
                        continue;
                    }
                    string title = name.Text?.Trim() is { Length: > 0 } given ? given : $"{info.Product} · {info.ServerName}";
                    _lblLibraryStatus.Text = $"Signing in to {info.ServerName}…";
                    var result = await window._owner.SignInEmbyAsync(info, userName, password.Text ?? string.Empty, title);
                    if (result.Server is not { } server)
                    {
                        UpdateLibraryStatus();
                        await Dialogs.MessageAsync(window, result.Problem ?? "The server didn't accept the sign-in.", window._scale);
                        continue;
                    }
                    var added = window._owner.AddEmbyServer(server);
                    window._owner.SetLibraryServer(added.Udn);
                    await RefillLibraryServersAsync();
                    return;
                }
            }

            public async Task<bool> AskEmbySignInAsync(MediaServer server)
            {
                _lblLibraryStatus.Text = $"Checking {server.Name}…";
                if (!await window._owner.EmbySignInExpiredAsync(server)) return false;
                if (await window._owner.RenewSavedEmbySignInAsync(server)) return true;
                var password = new TextBox { PasswordChar = '•', Width = 360 };
                var content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = $"Sign in to {server.Name}", Classes = { "heading" } },
                        new TextBlock
                        {
                            Text = $"The server signed Wiim Control out. Enter the password for {window._owner.EmbyUserOf(server)} again.",
                            TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Classes = { "muted" }
                        },
                        ShareField("Password", password)
                    }
                };
                password.AttachedToVisualTree += (_, _) => password.Focus();
                while (true)
                {
                    (content.Parent as Panel)?.Children.Remove(content);
                    if (!await Dialogs.ChooseAsync(window, content, "Sign in", window._scale)) return false;
                    if (await window._owner.RenewEmbySignInAsync(server, password.Text ?? string.Empty) is not { } problem) return true;
                    await Dialogs.MessageAsync(window, problem, window._scale);
                }
            }

            private async Task RemoveLibraryServerAsync()
            {
                if (SelectedLibraryServer is { IsEmbyJellyfin: true } emby)
                {
                    if (!await Dialogs.ConfirmAsync(window, $"Remove {emby.Name} from the list? The music on the server isn't touched.", window._scale)) return;
                    await window._owner.RemoveEmbyServerAsync(emby);
                    await RefillLibraryServersAsync();
                    return;
                }
                await RemoveSharedFolderAsync();
            }
        }
    }
}
