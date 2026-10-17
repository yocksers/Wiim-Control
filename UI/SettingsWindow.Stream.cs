using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly string[] StreamSchemes = ["http", "https", "rtsp", "mms", "rtmp"];
        private readonly TextBox _txtStreamUrl = new() { Watermark = "Stream address, for example https://example.com/live.mp3", MinWidth = 340 };
        private readonly TextBox _txtStreamName = new() { Watermark = "Name (optional)", Width = 200 };

        private Border BuildStreamCard()
        {
            var play = Pill("Play", accent: true);
            var save = Pill("☆  Save to Favourites");
            ToolTip.SetTip(_txtStreamUrl, "A web address of an internet radio stream, a playlist (.m3u, .pls) or an audio file");
            ToolTip.SetTip(save, "Keep this stream in Favourites with the name you entered");
            play.Click += async (_, _) => await PlayStreamAsync();
            save.Click += async (_, _) => await SaveStreamAsync();
            _txtStreamUrl.KeyDown += async (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                await PlayStreamAsync();
            };
            var row = new WrapPanel { Children = { _txtStreamUrl, _txtStreamName, play, save } };
            foreach (var child in row.Children.OfType<Control>()) child.Margin = new Thickness(0, 0, 8, 8);
            return Card("Network stream", Muted("Play any internet stream on the device by its address, like Open Network Stream in the WiiM Home app."), row);
        }

        private async Task<RadioStation?> StreamFromFieldsAsync()
        {
            string url = _txtStreamUrl.Text?.Trim() ?? string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !StreamSchemes.Contains(uri.Scheme.ToLowerInvariant()))
            {
                await Dialogs.MessageAsync(this, "Please enter a full stream address that starts with http:// or https://.", _scale);
                return null;
            }
            string name = _txtStreamName.Text?.Trim() is { Length: > 0 } given ? given : uri.Host;
            return new RadioStation(name, uri.ToString(), string.Empty, "Network stream");
        }

        private async Task PlayStreamAsync()
        {
            if (await StreamFromFieldsAsync() is { } station) await PlayStationAsync(station);
        }

        private async Task SaveStreamAsync()
        {
            if (await StreamFromFieldsAsync() is not { } station) return;
            if (_owner.IsRadioFavorite(station))
            {
                await Dialogs.MessageAsync(this, "This stream is already in Favourites.", _scale);
                return;
            }
            _owner.SetRadioFavorite(station, true);
            _lblRadioStatus.Text = $"Saved {station.Name} to Favourites";
            if (_radioCategory == RadioFavorites) await ShowRadioCategoryAsync(RadioFavorites);
        }
    }
}
