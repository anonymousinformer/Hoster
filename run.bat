@echo off
rem Builds Hoster.exe and starts it detached. You can close this window afterwards.
rem To just START Hoster (no rebuild, much faster), double-click dist\Hoster.exe instead.
rem Works from any folder, whatever it is called (spaces, brackets, OneDrive, network drives).
pushd "%~dp0"
if errorlevel 1 (echo Couldn't open the folder this script is in. & pause & exit /b 1)
taskkill /f /im Hoster.exe >nul 2>&1
taskkill /f /im DotDesk.exe >nul 2>&1
rem give Windows a moment to release the old exe before it gets overwritten
ping -n 2 127.0.0.1 >nul
rem leftovers from before the rename
if exist "DotDesk.csproj" del "DotDesk.csproj"
if exist "dist\DotDesk.exe" del "dist\DotDesk.exe"
where dotnet >nul 2>&1
if errorlevel 1 goto nosdk
dotnet publish "Hoster.csproj" -c Release -o "dist"
if errorlevel 1 goto failed
start "" "%~dp0dist\Hoster.exe"
popd
exit /b 0

:nosdk
echo The .NET 8 SDK isn't installed. Get it from https://dotnet.microsoft.com/download
pause
exit /b 1

:failed
echo Build failed.
pause
exit /b 1
