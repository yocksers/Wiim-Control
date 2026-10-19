# Wiim Control

A program for **Windows, Linux and macOS** that controls **WiiM devices** (Mini, Pro, Pro Plus, Ultra, Sound, Amp,
Amp Pro, Amp Ultra and other WiiM/LinkPlay devices) from your PC.

## Features

**Keyboard and tray**
- The volume and media keys control the active WiiM device, with an optional on-screen volume overlay.
- Your own hotkeys that work in every program, a sleep timer, and a tray menu with presets, device switching and more.
- Shows the device in the system's media controls (Windows, Linux MPRIS, macOS Now Playing), with optional song
  notifications showing the cover and title when the song changes.
- Windows and Linux: play everything you hear on the computer on a WiiM device with the cast button on its card (or
  the tray menu), in full quality with about a second of delay. On Linux this needs `parec` (the pulseaudio-utils
  package, also used with PipeWire).

**Devices and playback**
- Finds your devices automatically (rename and reorder them as you like) and shows what each one plays, with play/pause, next/previous, position, volume,
  input switching and presets. Click the artwork for a full-window view, optionally as a spinning vinyl record, or open
  a resizable mini player that can pop up by itself when the device switches to Wi-Fi. The cover of what's playing
  can also be shown blurred as the window's background.
- Plays from DLNA/UPnP servers, shared network folders (SMB), Lyrion Music Server, Roon, internet radio and network streams,
  with Queue and Recent pages.
- Multiroom: link devices, choose the group leader, set left/right channels, home theater (center and surround speakers)
  and group audio delay.

**Sound and device settings**
- Graphic and parametric EQ with presets, quick buttons, REW/AutoEQ import, export and RoomFit.
- Volume limit, knob and remote steps, balance, fade, headroom, audio channel and Dynamic Bass (on devices that have it).
- Output switching (including USB DAC, Bluetooth and DLNA out), digital filter, input settings and subwoofer settings,
  including WiiM Sub Pro pairing.
- Network, standby, alarms, lights, prompt sounds, automatic updates, restart, and the Amp Ultra screen with your own
  background pictures.
- Backup and restore of settings and EQ presets, and automatic update checks for Wiim Control itself.

## Install

**Windows:** run `WiimControlSetup.exe`.

**Linux:** download `WiimControl-<version>-x86_64.AppImage`, then:

```
chmod +x WiimControl-<version>-x86_64.AppImage
./WiimControl-<version>-x86_64.AppImage
```

Settings are stored in `~/.config/wiim-control/`. On GNOME the tray icon needs the **AppIndicator and KStatusNotifierItem
Support** extension (Ubuntu includes it). For the volume keys, use the desktop's global shortcuts (KDE Plasma, GNOME 48+),
bind keys to the commands shown on the General page (for example `--volume-up`), or set hotkeys on the Hotkeys page.

**macOS:** download the `macos-arm64` (Apple Silicon) or `macos-x64` (Intel) zip, unzip it and move **Wiim Control** to
Applications. macOS doesn't let apps take over the volume keys, so set hotkeys on the Hotkeys page. If macOS blocks the
first start, open **System Settings → Privacy & Security** and click **Open Anyway** (on older versions, right-click the
app and choose **Open**).

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the
user or the person installing or operating it. It has no telemetry, analytics or advertising, and doesn't collect
or send any personal information.

To do what you ask it to, Wiim Control connects to:

- **Your WiiM devices** on your local network, to control them and read their status and settings.
- **Music servers and shared folders** on your local network that you use (DLNA/UPnP, Lyrion, Roon and SMB). To play
  from a shared folder, its address, user name and password are sent to the WiiM device, which reads the files itself.
- **Internet streams you enter**, which the device plays directly.
- **Your WiiM device, to your computer**, only while you play the computer's sound on it: the device fetches the sound
  from Wiim Control over your local network. Windows may ask once to allow this.
- **[radio-browser.info](https://www.radio-browser.info/)**, to search for radio stations.
- **Websites that host album art and station logos**, to show the pictures.
- **GitHub**, to check for and download new versions (can be switched off on the General page).
- **Buy Me a Coffee**, only when you click the coffee cup.

Settings, backups and the error log are stored only on your computer.

## Credits and third-party software

Wiim Control is built on the work of others. Thank you! These are used under their own licences:

| Project | Used for | Licence |
| --- | --- | --- |
| [Avalonia UI](https://avaloniaui.net/) (with its Fluent theme) | The user interface | MIT |
| [Inter](https://rsms.me/inter/) by Rasmus Andersson | The font | SIL Open Font License 1.1 |
| [SkiaSharp](https://github.com/mono/SkiaSharp) and [Skia](https://skia.org/) | Drawing, artwork and background pictures | MIT / BSD 3-Clause |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp) and [HarfBuzz](https://harfbuzz.github.io/) | Text rendering | MIT |
| [ANGLE](https://chromium.googlesource.com/angle/angle) | Graphics on Windows | BSD 3-Clause |
| [MicroCom.Runtime](https://github.com/kekekeks/MicroCom) | Part of Avalonia on Windows | MIT |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | Media controls and global shortcuts on Linux | MIT |
| [SMBLibrary](https://github.com/TalAloni/SMBLibrary) by Tal Aloni | Shared network folders | [GNU LGPL v3.0 or later](https://www.gnu.org/licenses/lgpl-3.0.html) |
| [.NET](https://dotnet.microsoft.com/) | The runtime the app is built on | MIT |
| [Material Design Icons](https://pictogrammers.com/library/mdi/) by Pictogrammers | The icons | Apache License 2.0 |

SMBLibrary ships as a separate `SMBLibrary.dll` next to the program, so it can be replaced with another build of the
library, as the LGPL allows.

Also with thanks to:

- [python-linkplay](https://github.com/Velleman/python-linkplay), whose list of WiiM and LinkPlay project codes is used to
  show each device's model name.
- [radio-browser.info](https://www.radio-browser.info/), the free community database behind the Radio page.
- [Roon Labs](https://roon.app/)

WiiM and the WiiM logo are trademarks of their owner, Linkplay Technology. Wiim Control is an independent project and isn't
made, endorsed or supported by WiiM; the logo in the app only links to [wiimhome.com](https://www.wiimhome.com/).
