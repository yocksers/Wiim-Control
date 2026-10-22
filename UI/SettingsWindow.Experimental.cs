using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const string SectionExperimental = "Experimental";
        private const int ExperimentalClicks = 5;
        private static readonly TimeSpan ExperimentalClickWindow = TimeSpan.FromSeconds(1.5);
        private int _deviceInfoClicks;
        private bool _experimentalUnlocked;
        private DateTime _lastDeviceInfoClick;

        private void CountDeviceInfoClick(Control ampPage)
        {
            var now = DateTime.UtcNow;
            _deviceInfoClicks = now - _lastDeviceInfoClick < ExperimentalClickWindow ? _deviceInfoClicks + 1 : 1;
            _lastDeviceInfoClick = now;
            if (_deviceInfoClicks < ExperimentalClicks) return;
            _deviceInfoClicks = 0;
            ShowPage(ampPage, SectionExperimental);
        }

        private static string OptionName(string value)
        {
            string text = value.Replace('_', ' ').Trim();
            return text.Length == 0 ? value : char.ToUpperInvariant(text[0]) + text[1..];
        }

        private Grid TextChoice(string text, List<string> values, string? current, Func<string, string> name, Func<string, Task<bool>> apply)
        {
            int index = current != null ? values.FindIndex(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)) : -1;
            var items = values.Select((v, i) => (i, name(v))).ToList();
            if (index < 0) items.Insert(0, (-1, "Unknown"));
            return AmpChoice(text, items, index, i => i >= 0 && i < values.Count ? apply(values[i]) : Task.FromResult(false));
        }

        private List<Control> BuildExperimentalCards(string ip, ExperimentalSettings? x)
        {
            var settingCards = new List<Control>();
            var allow = new ToggleSwitch { IsChecked = _experimentalUnlocked };
            allow.IsCheckedChanged += (_, _) =>
            {
                _experimentalUnlocked = allow.IsChecked == true;
                foreach (var card in settingCards) card.IsEnabled = _experimentalUnlocked;
            };
            var cards = new List<Control>
            {
                Card("Experimental settings",
                    Muted("These settings are highly experimental and used for testing only, only use if you know what you are doing!"),
                    ToggleRow("Allow changes", allow))
            };
            if (x == null)
            {
                cards.Add(Muted("Loading…"));
                return cards;
            }
            void AddSetting(Border card)
            {
                card.IsEnabled = _experimentalUnlocked;
                settingCards.Add(card);
                cards.Add(card);
            }

            var trigger = new List<Control>();
            if (x.TriggerOn is bool triggerOn)
            {
                var on = Pill("Turn on once");
                var off = Pill("Turn off once");
                on.Click += async (_, _) => await ChangeAmpAsync(() => _owner.TriggerOnceAsync(ip, true));
                off.Click += async (_, _) => await ChangeAmpAsync(() => _owner.TriggerOnceAsync(ip, false));
                trigger.Add(LabeledRow("Trigger output", Info(triggerOn ? "On" : "Off")));
                trigger.Add(LabeledRow(string.Empty, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { on, off } }));
            }
            if (x.TriggerOnReboot is bool onReboot)
            {
                var row = AmpToggle("Put the amplifier in standby after a restart", onReboot, value => _owner.SetTriggerOnRebootAsync(ip, value));
                ToolTip.SetTip(row, "Turn this on if the amplifier stays on after the device restarts. It then goes to standby once the restart " +
                                    "is complete. The amplifier may switch on briefly while the device restarts.");
                trigger.Add(row);
            }
            if (trigger.Count > 0)
                AddSetting(Card("12V trigger", [Muted("Switches external amplifiers on and off together with the device."), .. trigger]));

            var sound = new List<Control>();
            if (x.SoundModes.Count > 0)
                sound.Add(TextChoice("Sound mode", x.SoundModes, x.SoundMode, OptionName, mode => _owner.SetSoundModeAsync(ip, mode)));
            if (x.DolbyEffects.Count > 0)
                sound.Add(TextChoice("Dolby sound effect", x.DolbyEffects, x.DolbyEffect, OptionName, effect => _owner.SetDolbyEffectAsync(ip, effect)));
            if (x.VoiceLevels.Count > 0)
            {
                var row = TextChoice("Voice level", x.VoiceLevels, x.VoiceLevel, v => v, level => _owner.SetVoiceLevelAsync(ip, level));
                ToolTip.SetTip(row, "How much speech and dialogue are lifted above the rest of the sound");
                sound.Add(row);
            }
            if (sound.Count > 0) AddSetting(Card("Sound", sound.ToArray()));

            if (x.HiddenHighPass is { } highPass)
            {
                var frequency = AmpSlider(HighPassMin, HighPassMax, Math.Clamp(highPass.Frequency, HighPassMin, HighPassMax), v => $"{v} Hz", false,
                    out var frequencyRow);
                frequency.ValueCommitted += async (_, _) =>
                {
                    if (frequency.Value != highPass.Frequency) await ChangeAmpAsync(() => _owner.SetHighPassFrequencyAsync(ip, frequency.Value));
                };
                AddSetting(Card("High-pass filter",
                    Muted("This device answers the high-pass filter commands but doesn't list the filter among its features, and the WiiM Home " +
                          "app doesn't show it. It may do nothing, or change how the device protects its own speakers."),
                    AmpToggle("High-pass filter", highPass.Enabled, on => _owner.SetHighPassEnabledAsync(ip, on)),
                    LabeledRow("Cut-off frequency", frequencyRow)));
            }

            var streaming = new List<Control>();
            if (x.AmazonHd is bool amazonHd)
                streaming.Add(AmpToggle("Amazon Music HD", amazonHd, on => _owner.SetAmazonHdAsync(ip, on)));
            if (x.AlexaMusicHd is bool alexaHd)
                streaming.Add(AmpToggle("Play music asked for with Alexa in HD", alexaHd, on => _owner.SetAlexaMusicHdAsync(ip, on)));
            if (x.TidalMqa is bool tidalMqa)
                streaming.Add(AmpToggle("TIDAL MQA", tidalMqa, on => _owner.SetTidalMqaAsync(ip, on)));
            if (streaming.Count > 0) AddSetting(Card("Streaming services", streaming.ToArray()));

            var network = new List<Control>();
            if (x.HideSsid is bool hideSsid)
            {
                var row = AmpToggle("Hide the device's own Wi-Fi network", hideSsid, on => _owner.SetHideSsidAsync(ip, on));
                ToolTip.SetTip(row, "Hides the device's setup network name (SSID) from other devices");
                network.Add(row);
            }
            if (x.SmartDns is bool smartDns)
                network.Add(AmpToggle("Smart DNS", smartDns, on => _owner.SetSmartDnsAsync(ip, on)));
            if (x.DnsSpeedCheck is bool speedCheck)
                network.Add(AmpToggle("DNS speed check", speedCheck, on => _owner.SetDnsSpeedCheckAsync(ip, on)));
            if (network.Count > 0) AddSetting(Card("Network", network.ToArray()));

            if (x.Languages.Count > 1)
            {
                var codes = x.Languages.Select(l => l.Code).ToList();
                var row = TextChoice("Voice prompt language", codes, x.Language,
                    code => x.Languages.First(l => l.Code == code).Name, code => _owner.SetPromptLanguageAsync(ip, code));
                ToolTip.SetTip(row, "The language of the spoken messages the device plays");
                AddSetting(Card("Voice prompts", row));
            }

            if (x.IsEmpty) cards.Add(Muted("This device doesn't answer any of the experimental settings."));
            return cards;
        }
    }
}
