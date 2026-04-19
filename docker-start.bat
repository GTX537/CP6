@echo off
echo ===========================
echo   CP6 Docker - Starting...
echo ===========================
echo.
echo [1/4] Building containers...
docker compose up -d --build
echo.
echo [2/4] Waiting for services...
timeout /t 10 /nobreak > nul
echo.
echo [3/4] Checking service status...
docker compose ps
echo.
echo [4/4] Done!
echo.
echo   Frontend:  http://localhost:8080
echo   Backend:   http://localhost:9991
echo   SQL Server: localhost,1433  (sa / Cp6@Docker2024!)
echo   Redis:     localhost:6379
echo.
pause
