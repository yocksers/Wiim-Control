using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _lastInput = DateTime.UtcNow;
        private Func<string?>? _idlePlayerDevice;

        private void HookIdleBigPlayer()
        {
            AddHandler(PointerMovedEvent, (_, _) => NoteInput(), RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerPressedEvent, (_, _) => NoteInput(), RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerWheelChangedEvent, (_, _) => NoteInput(), RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(KeyDownEvent, (_, _) => NoteInput(), RoutingStrategies.Tunnel, handledEventsToo: true);
            Activated += (_, _) => NoteInput();
            _idleTimer.Tick += (_, _) => CheckIdle();
            Closed += (_, _) => _idleTimer.Stop();
            ApplyIdleTimer();
        }

        private void NoteInput() => _lastInput = DateTime.UtcNow;

        private void ApplyIdleTimer()
        {
            NoteInput();
            if (_owner.IdleBigPlayerSeconds > 0) _idleTimer.Start();
            else _idleTimer.Stop();
        }

        private void CheckIdle()
        {
            int seconds = _owner.IdleBigPlayerSeconds;
            if (seconds <= 0 || _npIp != null || !IsVisible || !IsActive || WindowState == WindowState.Minimized)
            {
                NoteInput();
                return;
            }
            if (DateTime.UtcNow - _lastInput < TimeSpan.FromSeconds(seconds)) return;
            NoteInput();
            if (BigPlayerDevice() is not { } ip) return;
            ShowNowPlayingView(ip);
            SetNowPlayingFullScreen(true);
        }

        private string? BigPlayerDevice() =>
            _idlePlayerDevice?.Invoke() ?? (_owner._knownDevices.Any(d => d.Ip == _owner._deviceIp) ? _owner._deviceIp : null);

        private static string IdleChoiceName(int seconds) => seconds switch
        {
            0 => "Off",
            < 60 => $"{seconds} seconds",
            60 => "1 minute",
            _ => $"{seconds / 60} minutes"
        };

        private static string IdleChoiceShort(int seconds) => seconds switch { 0 => "Off", < 60 => $"{seconds} s", _ => $"{seconds / 60} min" };

        private Button IdleBigPlayerButton()
        {
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { new PathIcon { Data = Icons.Timer, Width = 16, Height = 16 }, text }
                }
            };
            button.Classes.Add("pill");
            void Apply()
            {
                int seconds = _owner.IdleBigPlayerSeconds;
                text.Text = IdleChoiceShort(seconds);
                ToolTip.SetTip(button, seconds == 0
                    ? "Open the big player in full screen when Wiim Control hasn't been touched for a while, like a screensaver"
                    : $"The big player opens in full screen when Wiim Control hasn't been touched for {IdleChoiceName(seconds).ToLowerInvariant()}");
            }
            Apply();
            button.Click += (_, _) =>
            {
                var choices = new List<DropdownChoice> { DropdownHeading("Big player in full screen when unused for") };
                foreach (int seconds in IdleBigPlayerChoices)
                {
                    int choice = seconds;
                    choices.Add(DropdownAction(IdleChoiceName(choice), () =>
                    {
                        _owner.IdleBigPlayerSeconds = choice;
                        ApplyIdleTimer();
                        Apply();
                        return Task.CompletedTask;
                    }, _owner.IdleBigPlayerSeconds == choice));
                }
                ShowDropdown(button, choices);
            };
            return button;
        }
    }
}
