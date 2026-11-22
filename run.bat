@echo off
rem Builds Hoster.exe and starts it detached. You can close this window afterwards.
cd /d "%~dp0"
taskkill /f /im Hoster.exe >nul 2>&1
taskkill /f /im DotDesk.exe >nul 2>&1
rem give Windows a moment to release the old exe before it gets overwritten
timeout /t 2 /nobreak >nul
rem leftovers from before the rename
if exist DotDesk.csproj del DotDesk.csproj
if exist dist\DotDesk.exe del dist\DotDesk.exe
dotnet publish -c Release -o dist
if errorlevel 1 (echo Build failed. & pause & exit /b 1)
start "" "%~dp0dist\Hoster.exe"
