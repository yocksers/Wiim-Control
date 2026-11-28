using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private WindowState? _appRestoreState;
        private (Button Button, PathIcon Icon)? _appFullScreen;

        private Button AppFullScreenButton()
        {
            var pill = IconPill(Icons.FullScreen, null);
            _appFullScreen = pill;
            pill.Button.Click += (_, _) => SetAppFullScreen(_appRestoreState == null);
            PropertyChanged += (_, e) =>
            {
                if (e.Property != WindowStateProperty || WindowState == WindowState.FullScreen || _appRestoreState == null) return;
                _appRestoreState = null;
                ApplyAppFullScreenButton();
            };
            ApplyAppFullScreenButton();
            return pill.Button;
        }

        private void SetAppFullScreen(bool on)
        {
            if (on && _appRestoreState == null)
            {
                _appRestoreState = WindowState;
                WindowState = WindowState.FullScreen;
            }
            else if (!on && _appRestoreState is { } restore)
            {
                _appRestoreState = null;
                WindowState = restore;
            }
            ApplyAppFullScreenButton();
        }

        private void ApplyAppFullScreenButton()
        {
            if (_appFullScreen is not var (button, icon)) return;
            bool on = _appRestoreState != null;
            icon.Data = on ? Icons.FullScreenExit : Icons.FullScreen;
            ToolTip.SetTip(button, on ? "Leave full screen (Esc)" : "Show Wiim Control full screen (F11)");
        }
    }
}
