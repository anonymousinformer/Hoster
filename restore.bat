@echo off
rem Restores Hoster's packages and shows what happens. Saves the output to restore.log
pushd "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (echo The .NET 8 SDK isn't installed. Get it from https://dotnet.microsoft.com/download & pause & exit /b 1)
echo Restoring... this can take a minute the first time.
dotnet restore "Hoster.csproj" -p:PublishReadyToRun=false --nologo > restore.log 2>&1
type restore.log
if errorlevel 1 (
  echo.
  echo RESTORE FAILED. Send me restore.log
  pause
  popd
  exit /b 1
)
echo.
echo Restore OK. Now run run.bat
pause
popd
