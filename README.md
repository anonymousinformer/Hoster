# Hoster (.NET 8 / WPF)

A dock + top bar that replace the Windows taskbar, with a control panel. Windows 10/11, 64-bit.

<!-- Add a screenshot here: ![Hoster](docs/screenshot.png) -->

## Run it (stays running after you close the terminal)
1. Install the **.NET 8 SDK** (https://dotnet.microsoft.com/download).
2. Double-click `run.bat`. It builds `dist\Hoster.exe` and starts it. Close the window if you like, Hoster keeps running.
3. Next time, just double-click `dist\Hoster.exe` (no SDK needed, it is self-contained).
   Turn on Control panel > Desktop > "Start with Windows" to launch it at sign-in.

Don't start it with `dotnet run`: that ties it to the terminal. Use run.bat or the .exe.

## Features
- **Dock:** pin apps, launch or focus them (dot = running), magnify on hover (adjustable), hover an open app for a live preview of its windows and browser tabs.
- **Right-click a dock icon:** Unpin, or **End task** to force-close the app and everything it started.
- **Top bar:** a small pill that slides in when you push the cursor against the top edge. Clock + calendar, now-playing player (Spotify and anything with Windows media controls), CPU/memory, and a **Wi-Fi / Bluetooth menu** (switch networks, toggle radios, see paired devices).
- **Desktop widgets** that sit under your apps.
- **Live wallpaper:** a looping, muted video behind your desktop icons (Control panel > Desktop).
- Dock, bar and widgets get out of the way (and stop drawing) while a full-screen app or a maximised window is in front.

## Controls
- Red dot (dock or top bar) or tray icon: control panel. Tray menu also has Quit.
- Settings: `%APPDATA%\Hoster\settings.json`

## If the taskbar is stuck hidden
Run `restore-taskbar.bat`, or tray > "Show Windows taskbar". Quitting from the tray or panel restores it automatically.

## Build from source
```
dotnet publish -c Release -o dist
```
Requires the .NET 8 SDK on Windows. The only NuGet dependency is [NAudio](https://github.com/naudio/NAudio) (MIT).

## Contributing
Issues and pull requests are welcome. Note that this app hides and replaces the Windows taskbar, so please test with `restore-taskbar.bat` within reach.

## License
[MIT](LICENSE)
