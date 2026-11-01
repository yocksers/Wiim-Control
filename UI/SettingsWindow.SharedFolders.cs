using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class LibraryPage
        {
            private readonly Button _btnRemoveShare = Pill("Remove");

            private async Task AddSharedFolderAsync()
            {
                var address = new TextBox { Watermark = @"\\nas\music or \\192.168.1.20\Music\Albums", Width = 360 };
                var user = new TextBox { Watermark = "Leave empty for guest access", Width = 360 };
                var password = new TextBox { PasswordChar = '•', Width = 360 };
                var name = new TextBox { Watermark = "Optional", Width = 360 };
                var content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Add shared folder", Classes = { "heading" } },
                        new TextBlock
                        {
                            Text = "The WiiM device plays the music straight from the shared folder, so this computer doesn't need to stay on. " +
                                   "This needs recent WiiM firmware.",
                            TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Classes = { "muted" }
                        },
                        ShareField("Folder address", address),
                        ShareField("User name", user),
                        ShareField("Password", password),
                        ShareField("Name in the list", name)
                    }
                };
                ToolTip.SetTip(user, @"For a domain account, use DOMAIN\user");

                while (true)
                {
                    (content.Parent as Panel)?.Children.Remove(content);
                    if (!await Dialogs.ChooseAsync(window, content, "Add", window._scale)) return;
                    if (ParseShareAddress(address.Text ?? string.Empty) is not { } parsed)
                    {
                        await Dialogs.MessageAsync(window, @"Please enter the folder address like \\server\share or \\192.168.1.20\Music.", window._scale);
                        continue;
                    }
                    string title = name.Text?.Trim() is { Length: > 0 } given ? given
                        : parsed.Folder.Length > 0 ? parsed.Folder[(parsed.Folder.LastIndexOf('\\') + 1)..] : parsed.Share;
                    var folder = new SharedFolder(title, parsed.Host, parsed.Share, parsed.Folder, user.Text?.Trim() ?? string.Empty, password.Text ?? string.Empty);
                    _lblLibraryStatus.Text = $"Connecting to {parsed.Host}…";
                    string? error = await TestSharedFolderAsync(folder);
                    if (error != null)
                    {
                        UpdateLibraryStatus();
                        await Dialogs.MessageAsync(window, error, window._scale);
                        continue;
                    }
                    var server = window._owner.AddSharedFolder(folder);
                    window._owner.SetLibraryServer(server.Udn);
                    await RefillLibraryServersAsync();
                    return;
                }
            }

            public async Task<bool> AskSharedFolderLoginAsync(MediaServer server)
            {
                _lblLibraryStatus.Text = $"Checking {server.Name}…";
                var problem = await window._owner.CheckSharedFolderAsync(server);
                if (problem is not { LoginRefused: true }) return false;
                var user = new TextBox { Text = window._owner.SharedFolderUser(server) ?? string.Empty, Width = 360 };
                var password = new TextBox { PasswordChar = '•', Width = 360 };
                var content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = $"Sign in to {server.Name}", Classes = { "heading" } },
                        new TextBlock
                        {
                            Text = problem.Message + " Enter the user name and password again, for example after restoring a backup or changing the password.",
                            TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Classes = { "muted" }
                        },
                        ShareField("User name", user),
                        ShareField("Password", password)
                    }
                };
                password.AttachedToVisualTree += (_, _) => password.Focus();
                if (!await Dialogs.ChooseAsync(window, content, "Sign in", window._scale)) return false;
                window._owner.SetSharedFolderLogin(server, user.Text?.Trim() ?? string.Empty, password.Text ?? string.Empty);
                return true;
            }

            private async Task RemoveSharedFolderAsync()
            {
                if (SelectedLibraryServer is not { IsShare: true } server) return;
                if (!await Dialogs.ConfirmAsync(window, $"Remove {server.Name} from the list? The music in the shared folder isn't touched.", window._scale)) return;
                window._owner.RemoveSharedFolder(server);
                await RefillLibraryServersAsync();
            }

            private async Task RefillLibraryServersAsync()
            {
                _libraryFilling = true;
                _cboLibraryServer.SelectedIndex = -1;
                _libraryFilling = false;
                await FindLibraryServersAsync(force: false);
            }
        }
    }
}
