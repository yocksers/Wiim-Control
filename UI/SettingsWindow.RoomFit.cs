using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly Button _btnRoomFitCurve = Pill("Show curve…");

        private async Task ShowRoomFitCurveAsync()
        {
            var state = await _owner.GetRoomFitStateAsync(_eqDeviceIp);
            if (state == null)
            {
                await Dialogs.MessageAsync(this, "Couldn't read RoomFit from the device.", _scale);
                return;
            }

            string name = state.Active.Length > 0 ? state.Active : "No profile selected";
            var content = new StackPanel { Width = 680, Spacing = 6 };
            content.Children.Add(new TextBlock { Text = name, Classes = { "heading" } });
            content.Children.Add(Muted(state.Enabled
                ? "The correction the device applies now. To see another profile, pick it in RoomFit profile first."
                : "RoomFit is off, so the device doesn't apply this correction now."));
            var channels = state.Left is { } left && state.Right is { } right
                ? new[] { ("Left", left), ("Right", right) }
                : [(string.Empty, state.Stereo)];
            foreach (var (label, bands) in channels)
            {
                if (label.Length > 0)
                    content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
                content.Children.Add(new PeqGraph { Bands = bands, Height = 240, IsHitTestVisible = false });
                content.Children.Add(RoomFitFilterTable(bands));
            }
            await Dialogs.ContentAsync(this, new ScrollViewer { Content = content, MaxHeight = 720 }, _scale);
        }

        private static Control RoomFitFilterTable(PeqBand[] bands)
        {
            var active = bands.Select((b, i) => (Band: b, Number: i + 1)).Where(b => b.Band.Filter != PeqFilter.Off).ToList();
            if (active.Count == 0) return Muted("No filters.");

            var table = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,*,*,*"), Margin = new Thickness(0, 6, 0, 0) };
            void Cell(string text, int row, int column, bool muted = false)
            {
                var cell = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2) };
                if (muted) cell.Classes.Add("muted");
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                table.Children.Add(cell);
            }

            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            string[] titles = ["", "Filter", "Frequency", "Gain", "Q"];
            for (int c = 0; c < titles.Length; c++) Cell(titles[c], 0, c, muted: true);
            for (int r = 0; r < active.Count; r++)
            {
                var (band, number) = active[r];
                table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Cell(number.ToString(System.Globalization.CultureInfo.InvariantCulture), r + 1, 0, muted: true);
                Cell(PeqFilterLabels.FirstOrDefault(f => f.Filter == band.Filter).Label ?? "Peak", r + 1, 1);
                Cell(FormatFrequency(band.Frequency), r + 1, 2);
                Cell(FormatGain(band.Gain), r + 1, 3);
                Cell(FormatQ(band.Q), r + 1, 4);
            }
            return table;
        }
    }
}
