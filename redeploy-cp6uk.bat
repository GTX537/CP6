@echo off
REM CP6 cp6.uk redeploy wrapper for cmd.exe
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0redeploy-cp6uk.ps1" %*
endlocal
