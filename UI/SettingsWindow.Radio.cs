using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const string RadioFavorites = "favorites", RadioLocal = "local", RadioPopular = "popular",
            RadioParadise = "paradise", RadioCountries = "countries", RadioSearch = "search";

        private readonly ComboBox _cboRadioAmp = new() { Width = 200 };
        private readonly TextBlock _lblRadioStatus = Subtitle();
        private readonly TextBlock _lblRadioResults = new() { Classes = { "heading" }, Margin = new Thickness(0, 0, 0, 8) };
        private readonly StackPanel _radioResults = new();
        private readonly Button _btnRadioMore = Pill("Show more");
        private readonly TextBox _txtRadioSearch = new() { Watermark = "Search stations by name, for example DR P1 or BBC", MinWidth = 340 };
        private readonly Dictionary<string, Button> _radioPills = new();
        private readonly List<Bitmap> _radioBitmaps = [];
        private readonly List<(RadioStation Station, PathIcon Star)> _radioStars = [];
        private readonly List<(RadioStation Station, Control Row)> _radioRows = [];
        private string _radioCategory = string.Empty;
        private string _radioQuery = string.Empty;
        private int _radioOffset;
        private int _radioVersion;
        private bool _radioLoaded;
        private bool _radioPlaying;

        private string RadioCountryName => RadioDirectory.CountryName(_owner.RadioCountryCode);

        private Control BuildRadioPage()
        {
            ToolTip.SetTip(_cboRadioAmp, "The device to play on");
            var btnSearch = Pill("Search", accent: true);
            btnSearch.Click += async (_, _) => await SearchRadioAsync();
            _txtRadioSearch.KeyDown += async (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                await SearchRadioAsync();
            };
            var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _txtRadioSearch, btnSearch } };

            var browse = new WrapPanel();
            foreach (var (key, label) in new[]
                     {
                         (RadioFavorites, "Favourites"), (RadioLocal, "Local"), (RadioPopular, "Popular"),
                         (RadioParadise, "Radio Paradise"), (RadioCountries, "By country")
                     })
                browse.Children.Add(RadioPill(key, label));
            var genres = new WrapPanel();
            foreach (var (label, tag) in RadioDirectory.Genres) genres.Children.Add(RadioPill("tag:" + tag, label));

            _btnRadioMore.Margin = new Thickness(0, 10, 0, 0);
            _btnRadioMore.HorizontalAlignment = HorizontalAlignment.Center;
            _btnRadioMore.IsVisible = false;
            _btnRadioMore.Click += async (_, _) => await LoadRadioPageAsync(append: true);

            var body = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    Card(null, searchRow, RadioSection("Browse"), browse, RadioSection("Genres"), genres),
                    BuildStreamCard(),
                    Card(null, _lblRadioResults, _radioResults, _btnRadioMore,
                        Muted("Stations come from radio-browser.info, a free directory run by the community. " +
                              "Click the star next to a station to keep it in Favourites."))
                }
            };

            var page = Page("Radio", _lblRadioStatus, [_cboRadioAmp], body);
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property != IsVisibleProperty || !page.IsVisible || _radioLoaded) return;
                _radioLoaded = true;
                await ShowRadioCategoryAsync(_owner.RadioFavorites.Count > 0 ? RadioFavorites : RadioLocal);
            };
            return page;
        }

        private static TextBlock RadioSection(string text) => new()
        {
            Text = text, Classes = { "small", "muted" }, Margin = new Thickness(0, 14, 0, 6)
        };

        private Button RadioPill(string key, string label)
        {
            var pill = Pill(label);
            pill.Margin = new Thickness(0, 0, 6, 6);
            pill.Click += async (_, _) => await ShowRadioCategoryAsync(key);
            _radioPills[key] = pill;
            return pill;
        }

        private string RadioTitle(string category) => category switch
        {
            RadioFavorites => "Favourites",
            RadioLocal => $"Local stations in {RadioCountryName}",
            RadioPopular => "Most popular",
            RadioParadise => "Radio Paradise",
            RadioCountries => "Countries",
            RadioSearch => $"Results for “{_radioQuery}”",
            _ when category.StartsWith("tag:", StringComparison.Ordinal) =>
                RadioDirectory.Genres.FirstOrDefault(g => g.Tag == category[4..]).Label ?? category[4..],
            _ when category.StartsWith("country:", StringComparison.Ordinal) => $"Stations in {RadioDirectory.CountryName(category[8..])}",
            _ => string.Empty
        };

        private async Task SearchRadioAsync()
        {
            string query = _txtRadioSearch.Text?.Trim() ?? string.Empty;
            if (query.Length == 0) return;
            _radioQuery = query;
            await ShowRadioCategoryAsync(RadioSearch);
        }

        private async Task ShowRadioCategoryAsync(string category)
        {
            _radioCategory = category;
            string pill = category.StartsWith("country:", StringComparison.Ordinal) ? RadioCountries : category;
            foreach (var (key, button) in _radioPills) button.Classes.Set("accent", key == pill);
            _lblRadioResults.Text = RadioTitle(category);
            _radioOffset = 0;

            switch (category)
            {
                case RadioFavorites:
                    ClearRadioResults();
                    AddStations(_owner.RadioFavorites);
                    if (_owner.RadioFavorites.Count == 0)
                        _radioResults.Children.Add(Muted("No favourites yet. Click the star next to a station to add it here."));
                    _lblRadioStatus.Text = string.Empty;
                    return;
                case RadioParadise:
                    ClearRadioResults();
                    AddStations(RadioDirectory.RadioParadise);
                    _lblRadioStatus.Text = string.Empty;
                    return;
                case RadioCountries:
                    await ShowRadioCountriesAsync();
                    return;
                default:
                    await LoadRadioPageAsync(append: false);
                    return;
            }
        }

        private Task<List<RadioStation>?> QueryRadio(string category, int offset)
        {
            return category switch
            {
                RadioLocal => RadioDirectory.SearchAsync(country: _owner.RadioCountryCode, offset: offset),
                RadioPopular => RadioDirectory.SearchAsync(offset: offset),
                RadioSearch => RadioDirectory.SearchAsync(name: _radioQuery, offset: offset),
                _ when category.StartsWith("tag:", StringComparison.Ordinal) => RadioDirectory.SearchAsync(tag: category[4..], offset: offset),
                _ when category.StartsWith("country:", StringComparison.Ordinal) => RadioDirectory.SearchAsync(country: category[8..], offset: offset),
                _ => Task.FromResult<List<RadioStation>?>([])
            };
        }

        private async Task LoadRadioPageAsync(bool append)
        {
            int version = append ? _radioVersion : ClearRadioResults();
            int offset = append ? _radioOffset + RadioDirectory.PageSize : 0;
            string category = _radioCategory;
            _btnRadioMore.IsEnabled = false;
            _lblRadioStatus.Text = "Loading stations…";
            var stations = await QueryRadio(category, offset);
            _btnRadioMore.IsEnabled = true;
            if (version != _radioVersion) return;
            _lblRadioStatus.Text = string.Empty;
            if (stations == null)
            {
                if (!append) _radioResults.Children.Add(Muted("Couldn't reach the station directory. Check the internet connection and try again."));
                return;
            }
            _radioOffset = offset;
            AddStations(stations);
            _btnRadioMore.IsVisible = stations.Count == RadioDirectory.PageSize;
            if (!append && stations.Count == 0) _radioResults.Children.Add(Muted("No stations found."));
        }

        private async Task ShowRadioCountriesAsync()
        {
            int version = ClearRadioResults();
            _lblRadioStatus.Text = "Loading countries…";
            var countries = await RadioDirectory.GetCountriesAsync();
            if (version != _radioVersion) return;
            _lblRadioStatus.Text = string.Empty;
            if (countries == null)
            {
                _radioResults.Children.Add(Muted("Couldn't reach the station directory. Check the internet connection and try again."));
                return;
            }
            var grid = new WrapPanel();
            foreach (var country in countries)
            {
                var text = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = country.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock
                        {
                            Text = string.Create(CultureInfo.InvariantCulture, $"{country.Stations} stations"), Classes = { "muted", "small" }
                        }
                    }
                };
                var tile = new Border
                {
                    Width = 210, Padding = new Thickness(10, 6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(8),
                    Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = text
                };
                tile.PointerEntered += (_, _) => tile.Background = Palette.Input;
                tile.PointerExited += (_, _) => tile.Background = Brushes.Transparent;
                tile.Tapped += async (_, _) => await ShowRadioCategoryAsync("country:" + country.Code);
                grid.Children.Add(tile);
            }
            _radioResults.Children.Add(grid);
        }

        private int ClearRadioResults()
        {
            _radioResults.Children.Clear();
            foreach (var bitmap in _radioBitmaps) bitmap.Dispose();
            _radioBitmaps.Clear();
            _radioStars.Clear();
            _radioRows.Clear();
            _btnRadioMore.IsVisible = false;
            return ++_radioVersion;
        }

        private void AddStations(IEnumerable<RadioStation> stations)
        {
            foreach (var station in stations)
                if (!_radioRows.Any(r => r.Station.Url == station.Url)) _radioRows.Add((station, StationRow(station)));
            _radioRows.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Station.Name.Trim(), b.Station.Name.Trim()));
            _radioResults.Children.Clear();
            foreach (var (_, row) in _radioRows) _radioResults.Children.Add(row);
        }

        private Control StationRow(RadioStation station)
        {
            var icon = new PathIcon
            {
                Data = Icons.Radio, Width = 20, Height = 20, Foreground = Palette.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var art = new Image { Stretch = Stretch.Uniform, IsVisible = false };
            var picture = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Palette.Input,
                Child = new Panel { Children = { icon, art } }
            };
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0),
                Children = { new TextBlock { Text = station.Name, TextTrimming = TextTrimming.CharacterEllipsis } }
            };
            if (station.Detail.Length > 0)
                text.Children.Add(new TextBlock { Text = station.Detail, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });

            var (star, starIcon) = IconButton(_owner.IsRadioFavorite(station) ? Icons.StarFilled : Icons.Star);
            ToolTip.SetTip(star, "Add to or remove from Favourites");
            _radioStars.Add((station, starIcon));
            star.Click += (_, _) =>
            {
                bool favorite = !_owner.IsRadioFavorite(station);
                _owner.SetRadioFavorite(station, favorite);
                foreach (var (_, i) in _radioStars.Where(s => s.Station.Url == station.Url))
                    i.Data = favorite ? Icons.StarFilled : Icons.Star;
                if (_radioCategory == RadioFavorites) _ = ShowRadioCategoryAsync(RadioFavorites);
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            grid.Children.Add(picture);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Grid.SetColumn(star, 2);
            grid.Children.Add(star);

            var row = new Border
            {
                Child = grid, Padding = new Thickness(6, 5), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(row, $"Play {station.Name}");
            row.PointerEntered += (_, _) => row.Background = Palette.Input;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.Tapped += async (_, e) =>
            {
                if (e.Source is Visual v && (v as Button ?? v.FindAncestorOfType<Button>()) != null) return;
                await PlayStationAsync(station);
            };
            if (station.ArtUrl.Length > 0) _ = LoadStationArtAsync(station.ArtUrl, art, icon, _radioVersion);
            return row;
        }

        private async Task LoadStationArtAsync(string url, Image art, PathIcon icon, int version)
        {
            var bitmap = await _owner.LoadThumbnailAsync(url, 80, () => version == _radioVersion);
            if (bitmap == null) return;
            if (version != _radioVersion)
            {
                bitmap.Dispose();
                return;
            }
            _radioBitmaps.Add(bitmap);
            art.Source = bitmap;
            art.IsVisible = true;
            icon.IsVisible = false;
        }

        private async Task PlayStationAsync(RadioStation station)
        {
            if (AmpOf(_cboRadioAmp) is not { } ip)
            {
                await Dialogs.MessageAsync(this, "Add a WiiM device on the Devices page first.", _scale);
                return;
            }
            if (_radioPlaying) return;
            string target = TargetOf(ip);
            _lblRadioStatus.Text = $"Starting {station.Name}…";
            _radioPlaying = true;
            bool played;
            try { played = await _owner.PlayStationAsync(target, station); }
            finally { _radioPlaying = false; }
            if (!played)
            {
                _lblRadioStatus.Text = string.Empty;
                await Dialogs.MessageAsync(this, "The device didn't start the station. Check that it's switched on and reachable.", _scale);
                return;
            }
            _owner.AddRecent(new RecentItem(station.Name, station.Detail, station.ArtUrl, DateTime.Now, station, null));
            string name = _owner._knownDevices.FirstOrDefault(d => d.Ip == target).Name ?? target;
            _lblRadioStatus.Text = $"Playing {station.Name} on {name}";
            await Task.Delay(2000);
            _ = _owner.RefreshNowPlayingAsync();
            if (IsVisible) await RefreshDeviceStatusesAsync();
        }
    }
}
