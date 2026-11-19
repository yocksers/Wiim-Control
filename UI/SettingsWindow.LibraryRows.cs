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
        private sealed partial class LibraryPage
        {
            private void AddLibraryEntries(List<MediaEntry> entries)
            {
                int first = _libraryEntries.Count;
                _libraryEntries.AddRange(entries);
                bool search = SelectedLibraryServer is { IsRoon: false } && _libraryPath.Count > 0 && IsLibrarySearch(_libraryPath[^1].Id);
                for (int i = first; i < _libraryEntries.Count; i++)
                {
                    var entry = _libraryEntries[i];
                    if (!entry.IsContainer && !entry.IsAudio) continue;
                    string group = LibrarySearchGroup(entry);
                    if (search && (i == 0 || LibrarySearchGroup(_libraryEntries[i - 1]) != group))
                        _libraryRows.Children.Add(new TextBlock { Text = group, Classes = { "heading" }, Margin = new Thickness(8, i == 0 ? 0 : 18, 0, 6) });
                    var row = LibraryRow(entry, i);
                    _libraryRows.Children.Add(row);
                    _libraryRowsById.TryAdd(entry.Id, row);
                }
                UpdateLibraryButtons();
                UpdateLibraryStatus();
            }

            private void UpdateLibraryStatus()
            {
                int folders = _libraryEntries.Count(e => e.IsContainer), tracks = _libraryEntries.Count(e => e.IsAudio);
                var parts = new List<string>();
                if (folders > 0) parts.Add(folders == 1 ? "1 folder" : $"{folders} folders");
                if (tracks > 0) parts.Add(tracks == 1 ? "1 track" : $"{tracks} tracks");
                if (_libraryEntries.Count < _libraryTotal) parts.Add($"{_libraryEntries.Count} of {_libraryTotal} loaded");
                _lblLibraryStatus.Text = string.Join(" · ", parts);
            }

            private void UpdateLibraryButtons()
            {
                _btnLibraryPlayAll.IsVisible = _libraryEntries.Any(e => e.IsAudio);
                _btnLibraryMore.IsVisible = _libraryEntries.Count < _libraryTotal;
            }

            private void UpdateLibraryCrumbs()
            {
                _libraryCrumbs.Children.Clear();
                for (int i = 0; i < _libraryPath.Count; i++)
                {
                    int level = i;
                    bool last = i == _libraryPath.Count - 1;
                    if (i > 0)
                        _libraryCrumbs.Children.Add(new PathIcon
                        {
                            Data = Icons.ChevronRight, Width = 16, Height = 16, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center
                        });
                    var crumb = new TextBlock
                    {
                        Text = _libraryPath[i].Title, Classes = { last ? "heading" : "muted" }, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 2), MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    if (!last)
                    {
                        crumb.Cursor = new Cursor(StandardCursorType.Hand);
                        crumb.PointerEntered += (_, _) => crumb.Foreground = Palette.Text;
                        crumb.PointerExited += (_, _) => crumb.ClearValue(TextBlock.ForegroundProperty);
                        crumb.Tapped += async (_, _) =>
                        {
                            _libraryPath.RemoveRange(level + 1, _libraryPath.Count - level - 1);
                            await LoadLibraryFolderAsync();
                        };
                    }
                    _libraryCrumbs.Children.Add(crumb);
                }
            }

            private void ClearLibraryRows()
            {
                _libraryFolderVersion++;
                _libraryRows.Children.Clear();
                _libraryRowsById.Clear();
                foreach (var bitmap in _libraryBitmaps) bitmap.Dispose();
                _libraryBitmaps.Clear();
                _libraryArt.Clear();
            }

            private Border LibraryRow(MediaEntry entry, int index)
            {
                var icon = new PathIcon
                {
                    Data = !entry.IsContainer ? Icons.Music : entry.Class.Contains("album", StringComparison.OrdinalIgnoreCase) ? Icons.Album : Icons.Folder,
                    Width = 22, Height = 22, Foreground = Palette.TextMuted,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var art = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
                var picture = new Border
                {
                    Width = 44, Height = 44, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Palette.Input,
                    Child = new Panel { Children = { icon, art } }
                };
                string detail = entry.IsContainer
                    ? entry.ChildCount is { } n ? (n == 1 ? "1 item" : $"{n} items") : string.Empty
                    : string.Join(" · ", new[] { entry.Artist, entry.Album }.Where(s => s.Length > 0));
                var text = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0),
                    Children = { new TextBlock { Text = entry.Title.Length > 0 ? entry.Title : "(untitled)", TextTrimming = TextTrimming.CharacterEllipsis } }
                };
                if (detail.Length > 0)
                    text.Children.Add(new TextBlock { Text = detail, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                grid.Children.Add(picture);
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);
                Control end;
                bool roon = SelectedLibraryServer?.IsRoon == true;
                if (entry.IsContainer && roon && (IsRoonInput(entry) || !HasRoonPlay(entry)))
                {
                    end = new PathIcon { Data = Icons.ChevronRight, Width = 20, Height = 20, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center };
                }
                else if (entry.IsContainer)
                {
                    var (play, _) = IconButton(Icons.Play);
                    ToolTip.SetTip(play, $"Play {entry.Title}");
                    play.Click += async (_, _) => await PlayLibraryContainerAsync(entry);
                    end = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            play,
                            LibraryMoreButton(entry, index),
                            new PathIcon { Data = Icons.ChevronRight, Width = 20, Height = 20, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center }
                        }
                    };
                }
                else
                {
                    end = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = entry.Duration > TimeSpan.Zero ? FormatTime((int)entry.Duration.TotalSeconds) : string.Empty,
                                Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
                            },
                            LibraryMoreButton(entry, index)
                        }
                    };
                }
                Grid.SetColumn(end, 2);
                grid.Children.Add(end);

                var row = new Border
                {
                    Child = grid, Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                ToolTip.SetTip(row, entry.IsContainer ? $"Open {entry.Title}" : $"Play {entry.Title}");
                int depth = _libraryPath.Count;
                row.PointerEntered += (_, _) => row.Background = Palette.Card;
                row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
                row.Tapped += async (_, e) =>
                {
                    if (e.Source is Visual v && (v as Button ?? v.FindAncestorOfType<Button>()) != null) return;
                    if (entry.IsContainer) await OpenLibraryFolderAsync(entry, depth);
                    else await PlayLibraryFromAsync(index);
                };
                if (entry.ArtUrl.Length > 0) _ = LoadLibraryArtAsync(entry.ArtUrl, art, icon, _libraryFolderVersion);
                return row;
            }

            private async Task LoadLibraryArtAsync(string url, Image art, PathIcon icon, int version)
            {
                if (!_libraryArt.TryGetValue(url, out var download))
                {
                    download = window._owner.LoadThumbnailAsync(url, 88, () => version == _libraryFolderVersion);
                    _libraryArt[url] = download;
                    _ = KeepLibraryArtAsync(download, version);
                }
                var bitmap = await download;
                if (bitmap == null || version != _libraryFolderVersion) return;
                art.Source = bitmap;
                art.IsVisible = true;
                icon.IsVisible = false;
            }

            private Button LibraryMoreButton(MediaEntry entry, int index)
            {
                var (more, _) = IconButton(Icons.More);
                ToolTip.SetTip(more, "Play now, play next or add to the queue");
                more.Click += (_, _) => window.ShowDropdown(more, SelectedLibraryServer?.IsRoon == true
                    ?
                    [
                        DropdownAction("Play now", () => RoonPlayAsync(entry, RoonPlayNow)),
                        DropdownAction("Play next", () => RoonPlayAsync(entry, RoonAddNext)),
                        DropdownAction("Add to queue", () => RoonPlayAsync(entry, RoonQueue)),
                        DropdownAction("Start Roon Radio", () => RoonPlayAsync(entry, RoonStartRadio))
                    ]
                    :
                    [
                        DropdownAction("Play now", () => entry.IsContainer ? PlayLibraryContainerAsync(entry) : PlayLibraryFromAsync(index)),
                        DropdownAction("Play next", () => AddLibraryToQueueAsync(entry, index, next: true)),
                        DropdownAction("Add to queue", () => AddLibraryToQueueAsync(entry, index, next: false))
                    ]);
                return more;
            }

            private async Task KeepLibraryArtAsync(Task<Bitmap?> download, int version)
            {
                var bitmap = await download;
                if (bitmap == null) return;
                if (version == _libraryFolderVersion) _libraryBitmaps.Add(bitmap);
                else bitmap.Dispose();
            }
        }
    }
}
