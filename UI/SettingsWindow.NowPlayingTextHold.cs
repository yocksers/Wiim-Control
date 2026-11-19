using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly TimeSpan TextHoldLimit = TimeSpan.FromSeconds(12);
        private (string Title, string Artist, string Detail)? _npHeldText;
        private DateTime _npHeldSince;
        private bool _npTextWaitsForMascot;
        private bool _npTextWaitsForFade;

        private bool NowPlayingTextWaits => _npTextWaitsForMascot || _npTextWaitsForFade;

        private string ShownSong(NowPlayingControls c) =>
            ReferenceEquals(c, _npControls) && _npHeldText is { } held ? $"{held.Title}\n{held.Artist}" : $"{c.Title.Text}\n{c.Artist.Text}";

        private bool HoldNowPlayingText(NowPlayingControls c, (string Title, string Artist, string Detail) texts, string artUrl)
        {
            if (!ReferenceEquals(c, _npControls)) return false;
            bool shown = _npOverlay.IsVisible;
            if (_npHeldText != null)
            {
                _npHeldText = texts;
                bool stale = !NowPlayingTextWaits && DateTime.UtcNow - _npHeldSince > TextHoldLimit;
                bool finished = NowPlayingTextWaits && !MascotBusy && !_npFadeTimer.IsEnabled;
                if (!shown || stale || finished) ReleaseNowPlayingText();
                return true;
            }
            bool newSong = $"{c.Title.Text}\n{c.Artist.Text}" != $"{texts.Title}\n{texts.Artist}";
            if (!shown || !newSong || artUrl == c.Art.ImageUrl || !_npCover.IsVisible || _npCover.Source == null) return false;
            _npHeldText = texts;
            _npHeldSince = DateTime.UtcNow;
            _npTextWaitsForMascot = _npTextWaitsForFade = false;
            return true;
        }

        private void ForgetHeldText()
        {
            _npHeldText = null;
            _npTextWaitsForMascot = _npTextWaitsForFade = false;
        }

        private void ReleaseNowPlayingText()
        {
            if (_npHeldText is not { } held) return;
            ForgetHeldText();
            if (_npControls is { } c) (c.Title.Text, c.Artist.Text, c.Detail.Text) = held;
        }

        private void TextWaitsForArt()
        {
            if (_npHeldText == null) return;
            if (MascotBusy) _npTextWaitsForMascot = true;
            else if (_npFadeTimer.IsEnabled) _npTextWaitsForFade = true;
            else ReleaseNowPlayingText();
        }

        private void CheckMascotTextReveal()
        {
            if (_npHeldText == null || !_npTextWaitsForMascot) return;
            bool painted = _mascotState == MascotState.Paint && _npCoverOld.Clip is RectangleGeometry { Rect.X: >= StageCover / 2 };
            if (!MascotBusy || _npCoverOld.Opacity <= 0 || painted) ReleaseNowPlayingText();
        }

        private void CheckFadeTextReveal(double progress)
        {
            if (_npHeldText == null || !_npTextWaitsForFade || _npHasPendingArt) return;
            bool turned = _npFlip || _npSwap ? _npCoverOld.Opacity <= 0 : progress >= 0.5;
            if (progress >= 1 || turned) ReleaseNowPlayingText();
        }

        private T WithHeldTextShown<T>(Func<T> render)
        {
            if (_npHeldText is not { } held || _npControls is not { } c) return render();
            var shown = (c.Title.Text, c.Artist.Text, c.Detail.Text);
            (c.Title.Text, c.Artist.Text, c.Detail.Text) = held;
            _npCoverContent.UpdateLayout();
            try
            {
                return render();
            }
            finally
            {
                (c.Title.Text, c.Artist.Text, c.Detail.Text) = shown;
            }
        }
    }
}
