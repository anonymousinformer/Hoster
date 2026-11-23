@echo off
rem Emergency: stops Hoster and brings the normal Windows taskbar back.
rem Works from any folder, whatever it is called.
taskkill /f /im Hoster.exe >nul 2>&1
taskkill /f /im DotDesk.exe >nul 2>&1
if exist "%~dp0dist\Hoster.exe" goto hoster
if exist "%~dp0Hoster.exe" goto hosterhere
if exist "%~dp0dist\DotDesk.exe" goto dotdesk
goto explorer

:hoster
"%~dp0dist\Hoster.exe" --restore
exit /b

:hosterhere
"%~dp0Hoster.exe" --restore
exit /b

:dotdesk
"%~dp0dist\DotDesk.exe" --restore
exit /b

:explorer
powershell -NoProfile -Command "Stop-Process -Name explorer -Force"
