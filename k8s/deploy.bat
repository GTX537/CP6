@echo off
echo ===== CP6 Kubernetes Deployment =====
echo.

echo [1/5] Creating namespace...
kubectl apply -f namespace.yaml

echo [2/5] Creating ConfigMap and Secret...
kubectl apply -f configmap.yaml
kubectl apply -f secret.yaml

echo [3/5] Deploying infrastructure (DB + Redis + MQ)...
kubectl apply -f db-deployment.yaml
kubectl apply -f redis-deployment.yaml
kubectl apply -f mq-deployment.yaml

echo [4/5] Waiting for infrastructure to be ready...
echo        (This may take 1-2 minutes)
kubectl wait --for=condition=ready pod -l app=cp6-db -n cp6 --timeout=120s
kubectl wait --for=condition=ready pod -l app=cp6-redis -n cp6 --timeout=60s
kubectl wait --for=condition=ready pod -l app=cp6-mq -n cp6 --timeout=90s

echo [5/5] Deploying application (API + Web)...
kubectl apply -f api-deployment.yaml
kubectl apply -f web-deployment.yaml
kubectl apply -f ingress.yaml

echo.
echo ===== Deployment complete! =====
echo.
echo Check status:  kubectl get all -n cp6
echo Frontend URL:  minikube service cp6-web -n cp6
echo API logs:      kubectl logs -f deployment/cp6-api -n cp6
pause
