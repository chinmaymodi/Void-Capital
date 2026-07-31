@echo off
REM ---------------------------------------------------------------------------
REM Start Void Capital API locally (Development env, port 5189 via
REM launchSettings.json). Kills any stale instance first. Detaches into its
REM own minimized window; stdout/stderr go to Home Base\temp\api-*.log.
REM Usage: start-api.bat
REM ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0src\VoidCapital.Api"

REM Kill any stale API instance (the apphost process name)
taskkill /F /IM VoidCapital.Api.exe >nul 2>&1

REM sleep ~1s without needing console stdin
ping -n 2 127.0.0.1 >nul

set OUT="%~dp0..\..\temp\api-out.log"
set ERR="%~dp0..\..\temp\api-err.log"

start "VoidCapital API" /min cmd /c "dotnet run --no-build 1>>%OUT% 2>>%ERR%"
endlocal
