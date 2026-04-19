@echo off
echo ===========================
echo   CP6 Docker - Stopping...
echo ===========================
docker compose down
echo.
echo All containers stopped.
echo (Data is preserved in Docker volumes)
echo.
echo To remove data: docker compose down -v
pause
