@echo off
setlocal

rem Starts every part of OnePondy for local development: the API, the Worker, the web console,
rem the customer mobile app and the driver mobile app. Each opens in its own window so you can
rem see its logs and stop it on its own (Ctrl+C in that window, or just close it).
rem
rem Prerequisites (one-time): npm install and dotnet tool restore at the repo root, PostgreSQL
rem running, user secrets set for the API (see README.md). This script does not do first-time
rem setup — it only launches what is already set up.

cd /d "%~dp0"

echo ============================================================
echo  OnePondy - starting API, Worker, web console and mobile
echo ============================================================

echo.
echo Checking PostgreSQL...
sc query postgresql-x64-18 | find "RUNNING" >nul
if errorlevel 1 (
    echo   WARNING: PostgreSQL service "postgresql-x64-18" is not running.
    echo   Start it from Services, or the API will fail to connect.
) else (
    echo   PostgreSQL is running.
)

echo.
echo Starting API (http://localhost:5080)...
start "OnePondy API" cmd /k "cd /d "%~dp0backend" && dotnet run --project src\OnePondy.Api"

echo Starting Worker...
start "OnePondy Worker" cmd /k "cd /d "%~dp0backend" && dotnet run --project src\OnePondy.Worker"

echo Starting web console (http://localhost:3100)...
start "OnePondy Web Console" cmd /k "cd /d "%~dp0apps\web-console" && npm run dev"

echo Starting customer mobile (Metro bundler, port 8081)...
start "OnePondy Customer Mobile" cmd /k "cd /d "%~dp0apps\customer-mobile" && npm start"

echo Starting driver mobile (Metro bundler, port 8082)...
start "OnePondy Driver Mobile" cmd /k "cd /d "%~dp0apps\driver-mobile" && npx expo start --dev-client --port 8082"

echo.
echo All five have been started in their own windows.
echo   API             - http://localhost:5080/health/live
echo   Web console     - http://localhost:3100
echo   Customer mobile - scan the QR code in the Customer Mobile window with the dev client
echo   Driver mobile   - scan the QR code in the Driver Mobile window with the dev client
echo.
echo Close this window any time; it is not needed once the others have started.
pause
