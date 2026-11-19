# Wiim Control

A program for **Windows, Linux and macOS** that controls **WiiM devices** (Mini, Pro, Pro Plus, Ultra, Sound, Amp,
Amp Pro, Amp Ultra and other WiiM/LinkPlay devices) from your PC.

<img width="428" height="240" alt="1" src="https://github.com/user-attachments/assets/86492f64-243e-4fd6-9bba-1ab5ad0b6814" /><img width="434" height="239" alt="2" src="https://github.com/user-attachments/assets/11b509d7-79a1-4205-9669-1a03fcd8e283" />
<img width="435" height="236" alt="5" src="https://github.com/user-attachments/assets/532e4b1f-2630-4332-809a-1e5c323fd2fb" /><img width="432" height="238" alt="4" src="https://github.com/user-attachments/assets/fceeb541-1983-4806-8cfe-0b425177837f" />

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

WiiM and the WiiM logo are trademarks of their owner, Linkplay Technology. Wiim Control is an independent project and isn't
made, endorsed or supported by WiiM; the logo in the app only links to [wiimhome.com](https://www.wiimhome.com/). The screen style
previews on the Screen page come from the WiiM Home app and are used with WiiM's permission.
