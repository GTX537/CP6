@echo off
REM CP6 WMS Phase 1 - cmd wrapper for start-wms-phase1.ps1
REM Usage: start-wms-phase1.bat [-SkipMigration] [-SkipSeed] ...
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-wms-phase1.ps1" %*
endlocal
