# AirPodsWin

A small tray app that lets you use your AirPods on Windows with an interface inspired by iOS/macOS.
It lives in the system tray and opens with a single click.

## Features

- **Battery level** for the left earbud, the right earbud and the case, with a charging indicator.
- **Low battery alert** (< 20%) using a Windows notification.
- **iOS-style volume slider**, kept in sync with the Windows volume.
- **Audio output selector** right from the panel.
- "Start with Windows" option in the tray icon menu.

## How it works

- Battery levels are read from the Bluetooth LE advertisements that AirPods broadcast (Apple
  manufacturer data, `0x004C`, type `0x07`). No drivers required.
- Volume uses Core Audio (NAudio), and output switching uses the `IPolicyConfig` COM interface.

## Limitations

- Apple only broadcasts battery levels in **10% steps**.
- AirPods only broadcast their battery while the **case lid is open** or the earbuds are in use;
  otherwise the case shows as "—".
- **Noise control (ANC / Transparency) is not possible**: it relies on a proprietary Apple L2CAP
  channel, which the Windows Bluetooth stack does not expose to applications (`WSAENETDOWN`).
- With **Smart App Control** turned on, Windows blocks the executable because it is not signed with a
  recognized certificate. You need to turn it off, or build and sign with a valid code-signing certificate.

## Build

Requirements: Windows 10 (build 19041) or later and the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
.\publish\AirPodsWin.exe
```

`AirPodsWin.exe --demo` opens the panel with simulated data, which is handy for checking the UI.

## License

[MIT](LICENSE)
