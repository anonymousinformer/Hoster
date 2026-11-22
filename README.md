# Hoster (.NET 8 / WPF)

Dock + top bar that replace the Windows taskbar, with a control panel. Windows 10/11, 64-bit.

## Run it (stays running after you close the terminal)
1. Install the **.NET 8 SDK** (https://dotnet.microsoft.com/download).
2. Double-click `run.bat`. It builds `dist\Hoster.exe` and starts it. Close the window if you like, Hoster keeps running.
3. Next time, just double-click `dist\Hoster.exe` (no SDK needed, it is self-contained).
   Turn on Control panel > Desktop > "Start with Windows" to launch it at sign-in.

Don't start it with `dotnet run`: that ties it to the terminal. Use run.bat or the .exe.

## Top bar and widgets
- The top bar is a small pill that stays out of the way. Push the cursor against the very top edge of the screen and it slides in; move away and it slides out.
- Click the **clock** to open the calendar (live clock, month view, arrows to change month, click the month name to jump back to today).
- Click the **now playing** pill for the player (artwork, progress, previous / play-pause / next). It works with Spotify desktop and picks up any app that shows Windows media controls. Spotify is preferred when it is playing.
- The dock and top bar hide automatically while a full-screen app is in front (games, full-screen video, F11 browser, slideshows). Turn it off in Control panel > Top bar.

## Controls
- Click a dock icon: launch, or focus the app if it's running (dot underneath = running). Right-click a pinned icon > Unpin.
- Red dot (dock or top bar) or tray icon: control panel. Tray menu also has Quit.
- Settings: `%APPDATA%\Hoster\settings.json`

## If the taskbar is stuck hidden
Run `restore-taskbar.bat`, or tray > "Show Windows taskbar". Quitting from the tray or panel restores it automatically.
