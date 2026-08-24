@echo off
setlocal EnableExtensions

set "CP6_PAUSE=0"
if "%~1"=="" goto menu
goto dispatch

:menu
set "CP6_PAUSE=1"
echo.
echo CP6 daytime home test server
echo   1. Start with existing images
echo   2. Build and start
echo   3. Check status
echo   4. Close public access only
echo   5. Stop all services and preserve data
echo   Q. Quit
choice /C 12345Q /N /M "Select: "
if errorlevel 6 exit /b 0
if errorlevel 5 goto choose-stop
if errorlevel 4 goto choose-close
if errorlevel 3 goto choose-status
if errorlevel 2 goto choose-start-build
if errorlevel 1 goto choose-start

:choose-stop
set "CP6_COMMAND=stop"
goto run

:choose-close
set "CP6_COMMAND=close"
goto run

:choose-status
set "CP6_COMMAND=status"
goto run

:choose-start-build
set "CP6_COMMAND=start-build"
goto run

:choose-start
set "CP6_COMMAND=start"
goto run

:dispatch
set "CP6_COMMAND=%~1"

:run
set "CP6_ACTION="
set "CP6_EXTRA="
if /I "%CP6_COMMAND%"=="start" set "CP6_ACTION=Start"
if /I "%CP6_COMMAND%"=="start-build" (
    set "CP6_ACTION=Start"
    set "CP6_EXTRA=-Build"
)
if /I "%CP6_COMMAND%"=="status" set "CP6_ACTION=Status"
if /I "%CP6_COMMAND%"=="close" set "CP6_ACTION=ClosePublic"
if /I "%CP6_COMMAND%"=="stop" set "CP6_ACTION=StopAll"

if not defined CP6_ACTION (
    echo Unknown command: %CP6_COMMAND%
    echo Usage: %~nx0 start^|start-build^|status^|close^|stop
    exit /b 2
)

where pwsh.exe >nul 2>nul
if not errorlevel 1 (
    pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Invoke-Cp6DaytimeServer.ps1" -Action %CP6_ACTION% %CP6_EXTRA% -ProjectRoot "%~dp0."
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Invoke-Cp6DaytimeServer.ps1" -Action %CP6_ACTION% %CP6_EXTRA% -ProjectRoot "%~dp0."
)
set "CP6_EXIT=%ERRORLEVEL%"

if "%CP6_PAUSE%"=="1" pause
exit /b %CP6_EXIT%
