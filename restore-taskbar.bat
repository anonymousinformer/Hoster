@echo off
rem Emergency: stops Hoster and brings the normal Windows taskbar back.
taskkill /f /im Hoster.exe >nul 2>&1
taskkill /f /im DotDesk.exe >nul 2>&1
if exist "%~dp0dist\Hoster.exe" ("%~dp0dist\Hoster.exe" --restore) else if exist "%~dp0dist\DotDesk.exe" ("%~dp0dist\DotDesk.exe" --restore) else (powershell -NoProfile -Command "Stop-Process -Name explorer -Force")
