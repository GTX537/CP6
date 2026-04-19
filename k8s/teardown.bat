@echo off
echo ===== CP6 Kubernetes Teardown =====
echo.
echo This will delete ALL CP6 resources from the cluster.
echo.
pause

kubectl delete namespace cp6

echo.
echo ===== All CP6 resources deleted =====
pause
