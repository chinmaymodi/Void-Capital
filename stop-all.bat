@echo off
REM ---------------------------------------------------------------------------
REM Stop Void Capital: kills the local API process and stops the docker
REM compose project (postgres, redis, api containers). Leaves Docker Desktop
REM itself running. Does NOT remove volumes, so DB data persists.
REM Usage: stop-all.bat   (or: stop-all.bat --prune  to also remove volumes)
REM ---------------------------------------------------------------------------
setlocal

REM 1. Kill the locally-running API (apphost from start-api.bat / dotnet run)
taskkill /F /IM VoidCapital.Api.exe >nul 2>&1
echo [stop] API process killed (if it was running)

REM 2. Stop + remove the compose project containers
cd /d "%~dp0"
if /i "%~1"=="--prune" (
    docker compose down -v
) else (
    docker compose down
)

REM 3. Confirm nothing from this project is listening on the app ports
for %%p in (5432 6379 5000 5189) do (
    netstat -ano | findstr /R /C:":%%p .*LISTENING" >nul && echo [stop] port %%p still listening || echo [stop] port %%p free
)

echo.
echo [stop] Void Capital stopped. Docker Desktop is still running.
endlocal
