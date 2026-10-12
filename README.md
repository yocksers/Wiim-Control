# Wiim Control

A tray app for **Windows, Linux and macOS** for controlling **WiiM amps** (WiiM Amp, WiiM Amp Ultra and
other WiiM/LinkPlay devices) from your PC.

## Features

**Volume keys**
- The keyboard's volume up/down/mute keys control the active WiiM amp instead of Windows.
- Optional: suppress the Windows volume pop-up and show Wiim Control's own volume overlay instead
  (position, duration and size are configurable; it also shows the current track).
- Adjustable volume step, with an optional logarithmic step that is finer at low volume.
- Respects the amp's maximum volume setting.
- Optional group volume: the volume keys also change the amps linked to the active amp.
- Only take over the volume keys when one of the Windows playback devices you choose is active.
- Optional: forward the media keys (play/pause, next, previous) to the amp.

**Devices**
- Finds WiiM devices on your network automatically, or add one by IP address.
- Keeps a list of your amps; pick which one the volume keys control.
- For every amp: now playing (title, artist, album art, source or streaming service, the connected Bluetooth
  device and audio quality), play/pause, previous and next, shuffle and repeat, a position bar to jump within
  the track, and its own volume slider and mute button.
- Sleep timer for each amp (15 minutes to 2 hours), also in the tray menu.
- Presets page: each amp's saved presets with their artwork, name and source, including routines made in the WiiM Home
  app (for example switching the output and EQ); click one to play or run it.
- Switch each amp's input (Wi-Fi, Bluetooth, Line in, Optical, HDMI…) from its **Input** button. Inputs you
  renamed in the WiiM Home app are shown with your names.
- Remembers each amp's unique ID, so it reconnects automatically when the active amp gets a new IP
  address, and **Discover** updates amps that have moved instead of adding them twice.

**Multiroom**
- Link your amps together so they play in sync. The group leader is the controlled amp by default,
  or any other amp you choose; unlink amps or ungroup a whole group.
- When the controlled amp is linked to another amp, the media keys, presets and group volume go to
  the group leader.

**Equalizer**
- Turn the EQ on or off, pick a preset, or adjust the 10-band graphic EQ (±12 dB). Shows the amp's
  current input by default, and you can pick any other input to set its own EQ.
- Parametric EQ like the WiiM Home app: 10 bands with low shelf, peak or high shelf filters, frequency,
  gain and Q, a response graph with draggable points, and left and right channels set separately if you like.
- Save the current EQ as a new preset on the amp, and rename or delete your own presets.

**Amp settings** (per amp)
- Model, firmware and temperatures.
- Maximum volume, left/right balance and fade in/out.
- Subwoofer: on/off, level, crossover, phase and delay.
- Up to three daily alarms that play an internet radio stream or other web address.
- Digital filter (the list comes from the amp; it differs between models).
- Status light and touch-button lock; WiiM Amp and Amp Pro: volume lights completely off, on when adjusting the volume, or always on.
- Choose which inputs are shown (saved on the amp, so the WiiM Home app shows the same inputs).
- WiiM Amp Ultra screen: on/off, automatic brightness, brightness, 24-hour clock, clock style, standby screen, screen off in standby,
  now playing layout, second page (cover art, EQ visualization or VU meter) and its style, which page shows first, and the
  background (thumbnails are loaded from the amp).

**Tray**
- Right-click menu with the most common settings, the amp's presets 1–12 (with their names when the
  amp reports them), a sleep timer, device switching and a connection test.
- The tray tooltip shows the active amp and the track that is playing.
- Start with Windows (or at login on Linux and macOS).

**Hotkeys**
- Set your own keyboard shortcuts for volume up/down, mute, play/pause, next, previous and opening
  the settings. They work in every program, which is handy if your keyboard has no media keys.

## Windows

Run `WiimControlSetup.exe`.

## Linux

Download `WiimControl-<version>-x86_64.AppImage`, make it executable and run it:

```
chmod +x WiimControl-1.06-x86_64.AppImage
./WiimControl-1.06-x86_64.AppImage
```

Settings are stored in `~/.config/wiim-control/`. The tray icon works out of the box on KDE Plasma
and most desktops; GNOME needs the AppIndicator extension.

**Volume keys on Linux.** There are three ways to control the amp from the keyboard:

1. **Desktop global shortcuts** (KDE Plasma, GNOME 48 and newer): switch on *Use the desktop's
   global shortcuts* and approve the shortcuts when the desktop asks.
2. **Keyboard settings**: bind the keys to the commands shown on the General page, for example
   `WiimControl-1.06-x86_64.AppImage --volume-up`, `--volume-down`, `--mute`, `--play-pause`,
   `--next` and `--previous`. Each command is sent to the running Wiim Control.
3. **Hotkeys page**: set your own shortcuts in Wiim Control. No desktop setup needed on X11 desktops
   such as XFCE, Cinnamon and MATE. Ctrl + Alt + F1 to F12 can't be used; Linux reserves them.

## macOS

Download `WiimControl-<version>-macos-arm64.zip` (Apple Silicon) or `WiimControl-<version>-macos-x64.zip`
(Intel), unzip it and move **Wiim Control** to Applications. It runs in the menu bar.

macOS doesn't let apps take over the volume keys, so set your own shortcuts on the **Hotkeys** page.
The first time Wiim Control connects to your amps, macOS asks for permission to access the local
network. Unless the app is signed and notarized, macOS blocks the first start: right-click the app
and choose **Open**.
