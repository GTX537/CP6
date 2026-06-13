# 12 · DevOps：Docker / Compose / K8s / cloudflared

## 📍 学习目标

1. Docker 多阶段构建为什么镜像能从 800MB 缩到 100MB？
2. `depends_on` + `healthcheck` 怎么保证启动顺序？
3. K8s 的 Deployment / Service / Ingress / HPA 各自解决什么？
4. `readinessProbe` vs `livenessProbe` 的本质区别？
5. cloudflared 是怎么把内网应用暴露到公网而无需开端口？
6. 生产环境密钥管理的几种姿势

---

## 🔎 真实代码切片

### `CP6.WebApi/Dockerfile` 多阶段构建

```dockerfile
# 阶段 1：build (SDK 镜像 ~ 800MB)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制 csproj，先 restore 利用 Docker 层缓存
COPY CP6.Entity/CP6.Entity.csproj CP6.Entity/
COPY CP6.Core/CP6.Core.csproj CP6.Core/
COPY CP6.WebApi/CP6.WebApi.csproj CP6.WebApi/
RUN dotnet restore CP6.WebApi/CP6.WebApi.csproj

# 然后复制源码 + 编译
COPY . .
RUN dotnet publish CP6.WebApi -c Release -o /app/publish --no-restore

# 阶段 2：runtime (Runtime 镜像 ~ 200MB)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENTRYPOINT ["dotnet", "CP6.WebApi.dll"]
```

### `docker-compose.yml` 关键段

```yaml
services:
  cp6-db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:?Set MSSQL_SA_PASSWORD in .env}"
    volumes:
      - cp6-db-data:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -C -Q 'SELECT 1' -b"]
      interval: 10s
      timeout: 5s
      retries: 20
      start_period: 40s
    restart: unless-stopped

  cp6-api:
    build:
      context: .
      dockerfile: CP6.WebApi/Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: "Docker"
      ConnectionStrings__DefaultConnection: "Server=cp6-db;Database=CP6DB;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;MultipleActiveResultSets=True"
      JWT__Secret: "${JWT_SECRET:?Set JWT_SECRET (>=32 chars) in .env}"
    depends_on:
      cp6-db: { condition: service_healthy }
      cp6-redis: { condition: service_healthy }
      cp6-mq: { condition: service_healthy }
      cp6-kafka: { condition: service_healthy }
    ports:
      - "9991:5000"

  cp6-cloudflared:
    image: cloudflare/cloudflared:latest
    command: tunnel --no-autoupdate --config /etc/cloudflared/config.yml run
    volumes:
      - ./cloudflared-docker:/etc/cloudflared:ro

volumes:
  cp6-db-data:
  cp6-redis-data:
  cp6-mq-data:
  cp6-kafka-data:
```

### K8s `api-deployment.yaml`

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: cp6-api
  namespace: cp6
spec:
  replicas: 2
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1
      maxUnavailable: 0   # 零停机
  template:
    spec:
      initContainers:
        - name: wait-for-db
          image: busybox:latest
          command: ['sh', '-c', 'until nc -z cp6-db 1433; do sleep 3; done']
      containers:
        - name: api
          image: cp6-api:latest
          imagePullPolicy: Never
          ports:
            - containerPort: 5000
          envFrom:
            - configMapRef: { name: cp6-config }
          env:
            - name: RabbitMQ__Password
              valueFrom:
                secretKeyRef: { name: cp6-secret, key: RABBITMQ_PASSWORD }
          resources:
            requests: { memory: "128Mi", cpu: "100m" }
            limits:   { memory: "512Mi", cpu: "500m" }
          readinessProbe:
            tcpSocket: { port: 5000 }
            initialDelaySeconds: 15
            periodSeconds: 5
          livenessProbe:
            tcpSocket: { port: 5000 }
            initialDelaySeconds: 30
            periodSeconds: 10
            failureThreshold: 5
---
apiVersion: v1
kind: Service
metadata: { name: cp6-api, namespace: cp6 }
spec:
  selector: { app: cp6-api }
  ports: [{ port: 5000, targetPort: 5000 }]
  type: ClusterIP
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata: { name: cp6-api-hpa, namespace: cp6 }
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: cp6-api
  minReplicas: 2
  maxReplicas: 5
  metrics:
    - type: Resource
      resource:
        name: cpu
        target: { type: Utilization, averageUtilization: 70 }
```

### `ingress.yaml`

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: cp6-ingress
  namespace: cp6
  annotations:
    nginx.ingress.kubernetes.io/proxy-body-size: "10m"
    nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"
    nginx.ingress.kubernetes.io/proxy-send-timeout: "3600"
    nginx.ingress.kubernetes.io/websocket-services: "cp6-api"
spec:
  ingressClassName: nginx
  rules:
    - host: cp6.local
      http:
        paths:
          - path: /api
            pathType: Prefix
            backend: { service: { name: cp6-api, port: { number: 5000 } } }
          - path: /hubs
            pathType: Prefix
            backend: { service: { name: cp6-api, port: { number: 5000 } } }
          - path: /
            pathType: Prefix
            backend: { service: { name: cp6-web, port: { number: 80 } } }
```

---

## 💡 资深视角

### 多阶段构建：800MB → 100MB

**为什么 SDK 镜像那么大**：包含编译器、调试器、各种 SDK 工具。
**为什么 Runtime 小**：只有运行 .NET 应用必需的 CLR + ASP.NET Core。

```dockerfile
FROM ...:sdk AS build    # 临时用一次
# 编译
FROM ...:aspnet          # 最终镜像
COPY --from=build /publish .
```

最终镜像不包含 SDK 阶段的任何东西，所以小。

**生产更小**：用 Alpine 基础镜像（~80MB）或 Chiseled Ubuntu（~50MB）：

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
```

CP6 用标准 aspnet 镜像，生产推荐切 alpine。

### Docker 层缓存的精妙

```dockerfile
# 反例
COPY . .
RUN dotnet restore
RUN dotnet publish

# 正例（CP6 做法）
COPY *.csproj .
RUN dotnet restore   # 这一层只在 csproj 变化时重跑
COPY . .
RUN dotnet publish
```

每次 `git pull` 一行源码改动，CP6 的构建只重跑最后一层（几秒），不重新 restore（几分钟）。

### healthcheck + depends_on

```yaml
cp6-api:
  depends_on:
    cp6-db: { condition: service_healthy }
```

`condition: service_healthy` 让 cp6-api 等到 cp6-db 的 healthcheck 返回成功才启动。否则 API 启动时 DB 还没就绪，迁移失败。

**SQL Server 容器启动慢**（30~60s），CP6 设了 `start_period: 40s` 让前 40s 的 health 失败不计数。

### `restart: unless-stopped`

- `no`：从不重启
- `on-failure`：异常退出才重启
- `always`：总是重启（包括 docker stop 后）
- `unless-stopped`：除非手动 stop，否则总是重启

CP6 用 `unless-stopped`，主机重启会自动起来，手动 down 后不会自启。

### `${VAR:?error message}` 强制变量

```yaml
MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:?Set MSSQL_SA_PASSWORD in .env}"
```

`:?` 是 shell 语法的"如果未设置就报错并退出"。CP6 这样写让 dev 一启动就发现 `.env` 没配，比启动后用默认密码"docker"被打爆好得多。

### K8s 三大对象

#### Deployment

声明期望的"Pod 状态"：

- 镜像版本
- 副本数
- 启动顺序（initContainers）
- 资源限额
- 滚动更新策略

K8s 控制器对比期望 vs 实际，差了就调整（扩缩、重启、重新调度）。

#### Service

抽象一组 Pod 的网络访问入口。Pod IP 会变（重启就换），Service 提供稳定的 ClusterIP（或 NodePort、LoadBalancer）。

CP6 的 `cp6-api` Service 是 ClusterIP（集群内可访问，外网不行）；通过 Ingress 暴露给外网。

#### Ingress

L7 路由层。一个 Ingress 路由到多个 Service：

```
cp6.local/api → cp6-api Service
cp6.local/hubs → cp6-api Service (with WebSocket support)
cp6.local/   → cp6-web Service
```

**对比 Service 类型**：

| 类型 | 暴露范围 | 用法 |
|---|---|---|
| ClusterIP | 集群内 | 默认，内部 Service 互访 |
| NodePort | Node 端口 | 开发测试，端口范围 30000+ |
| LoadBalancer | 云 LB | 生产但每个 Service 一个 LB 太贵 |
| Ingress | L7 路由 | 一个 LB + 路由多个 Service，生产推荐 |

### Readiness vs Liveness probe

| 探针 | 目的 | 失败后果 |
|---|---|---|
| **Readiness** | "我准备好接流量了吗？" | 从 Service 端点摘除，停止收新流量 |
| **Liveness** | "我活着吗？" | 杀掉 Pod 重启 |

**典型坑**：Liveness 设太敏感 → 启动慢的 Pod 还没启动就被杀 → 死循环。

CP6 的设置：

```yaml
readinessProbe:
  initialDelaySeconds: 15      # 给 15s 启动
  periodSeconds: 5             # 每 5s 检一次
livenessProbe:
  initialDelaySeconds: 30      # 30s 后才开始检
  periodSeconds: 10
  failureThreshold: 5          # 连续 5 次失败才杀
```

`failureThreshold: 5` 容忍偶发抖动。

**改进**：CP6 用 `tcpSocket` 探针（端口能连上 = 健康），不够精确。应该加 HTTP `/health` endpoint 检查 DB + 缓存 + MQ 都通：

```yaml
livenessProbe:
  httpGet: { path: /health, port: 5000 }
```

然后在 Program.cs 加：

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CP6Context>()
    .AddRedis(redisConn)
    .AddRabbitMQ(...);
app.MapHealthChecks("/health");
```

### HPA 公式

```
desiredReplicas = ceil(currentReplicas × currentMetric / targetMetric)
```

例：当前 2 副本，CPU 平均 85%，target 70% → `ceil(2 × 85/70) = ceil(2.43) = 3` 副本。

**缺陷**：CPU 只是粗指标，更准的是按"业务指标"扩缩（如请求队列长度）。K8s 支持自定义指标（Prometheus Adapter）。

### cloudflared Tunnel

```yaml
cp6-cloudflared:
  image: cloudflare/cloudflared:latest
  command: tunnel --config /etc/cloudflared/config.yml run
```

cloudflared 是 Cloudflare 的"反向出站隧道"：

- 容器从内网**主动**连 Cloudflare 边缘
- 公网请求 `cp6.uk` 到 Cloudflare → 经隧道 → 到内网容器
- **无需开端口、无需公网 IP、无需折腾 NAT**

代价：依赖 Cloudflare（免费但限速）。

**优点**：

- 比 ngrok 稳定（生产可用）
- 自带 DDoS 防护、CDN
- 自带 HTTPS
- 凭证文件存 `cloudflared-docker/` + `.gitignore`，不入仓库

CP6 把 cp6.uk 暴露给外网就靠这个，不用买 VPS / 配防火墙。

### 密钥管理姿势

| 环境 | 姿势 |
|---|---|
| 本地开发 | `appsettings.Local.json` + `.gitignore` |
| Docker Compose | `.env` 文件 + `${VAR}` 注入 |
| K8s | `Secret` 对象 + `envFrom: secretKeyRef` |
| 云上严肃 | Azure Key Vault / AWS Secrets Manager + DI 集成 |
| 极高安全 | HashiCorp Vault + dynamic credentials |

**CP6 都用了**：本地 `appsettings.Local`，Compose 用 `.env`，K8s 用 `Secret`。

**生产关键**：

- Secret 用 `base64` 编码不是加密，要在 K8s 集群开启 etcd encryption at rest
- 别把 Secret 通过 `kubectl get secret -o yaml` 提交到 Git
- 用 sealed-secrets 或 SOPS 安全提交加密的 Secret 到 Git

---

## ⚠️ 踩坑记录

### 坑 1：Docker 镜像没改但容器不更新

```bash
docker compose up -d   # 不会重新构建，用旧镜像
docker compose up -d --build   # 强制重构
```

CI 流程：每次 commit 都 `--build` + 用 tag 区分版本（不要只用 `:latest`）。

### 坑 2：volume 残留数据

```bash
docker compose down   # 容器删了，volumes 还在
docker compose down -v   # 删 volumes（生产慎用）
```

CP6 把 DB volume 命名 `cp6-db-data`。换密码后 `down -v` 重起，否则 `MSSQL_SA_PASSWORD` 改不生效（DB 已初始化）。

### 坑 3：K8s `imagePullPolicy: Never` 拉远程

```yaml
imagePullPolicy: Never   # minikube 本地镜像
```

在 minikube 里有效（用本地构建的镜像）。生产集群必须改成 `IfNotPresent` 或 `Always`，配合 registry（如 Harbor、ECR）。

### 坑 4：HPA 不工作

```bash
kubectl get hpa
# TARGETS: <unknown>/70%
```

`<unknown>` = Metrics Server 没装或没抓到数据。`minikube addons enable metrics-server` 或 K8s 集群装 Metrics Server。

### 坑 5：Ingress 不通

```bash
kubectl describe ingress cp6-ingress
# 看是否分配了 ADDRESS
```

如果 ADDRESS 是空，Ingress Controller 没装。`minikube addons enable ingress` 或装 nginx-ingress-controller。

### 坑 6：cloudflared 凭证误入仓库

```
cloudflared-docker/cert.pem   ← 必须 .gitignore
```

凭证泄露 = 别人可以接管你的隧道。CP6 在 README 反复强调。

---

## 🧪 自检题

1. **多阶段优化**：你的 .NET 镜像 600MB，怎么再缩 50%？  
   <details><summary>答案</summary>(1) 切 Alpine 基础镜像；(2) 用 <code>--no-restore --self-contained false</code> 减少依赖；(3) PublishTrimmed=true 去掉未引用的 dll；(4) ReadyToRun 编译；(5) 切 Chiseled Ubuntu（微软无 shell 的最小镜像）。极致 .NET 8 可到 30MB。</details>

2. **滚动更新**：`maxSurge: 1, maxUnavailable: 0` 跟 `maxSurge: 0, maxUnavailable: 1` 有什么差别？  
   <details><summary>答案</summary>(1, 0) = 先起新的再停旧的，零停机但短时 N+1 副本占资源；(0, 1) = 先停一个旧的再起新的，有短时 N-1 副本可能容量不足但不超配。生产要零停机选 (1, 0)；资源紧张选 (0, 1)。</details>

3. **健康检查**：你的服务启动要 60s（载入大字典），livenessProbe 该怎么配？  
   <details><summary>答案</summary><code>initialDelaySeconds: 90</code>（给充分启动时间） + <code>failureThreshold: 3</code>（避免抖动误杀）。或者用 <code>startupProbe</code>（K8s 1.20+ 新探针，专门处理慢启动，启动完成才切换到 liveness/readiness）。</details>

4. **HPA 反模式**：HPA 设了 minReplicas=1，业务高峰 CPU 100% 但没扩容，可能什么原因？  
   <details><summary>答案</summary>(1) Metrics Server 没装；(2) Pod 没设 <code>resources.requests.cpu</code>，HPA 没基准算百分比；(3) HPA stabilizationWindow 默认 5 分钟，刚发生的高峰还在窗口里；(4) 已达 maxReplicas；(5) cluster 资源不够，新 Pod 排队 Pending。</details>

5. **设计题**：你的 K8s 里有 50 个 service，每个都要不同的密钥，怎么管？  
   <details><summary>答案</summary>(1) 按业务域划分 namespace + 每个 namespace 一个 Secret；(2) 用 External Secrets Operator + AWS Secrets Manager / Vault，K8s 里只引用，密钥在外面；(3) SOPS + sealed-secrets 让 Secret 加密后可 Git 提交；(4) 严肃 prod 上 HashiCorp Vault + Kubernetes auth + 动态密钥（每次启动 Pod 获取临时 DB 密码）。</details>

---

## 🔗 延伸阅读

- [Docker - Best practices for writing Dockerfiles](https://docs.docker.com/develop/develop-images/dockerfile_best-practices/)
- [Kubernetes - Concepts](https://kubernetes.io/docs/concepts/)
- [Cloudflare Tunnel docs](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
- [The 12-Factor App](https://12factor.net/) — 容器化应用的圣经
- 项目内：`CP6.WebApi/Dockerfile`、`docker-compose.yml`、`k8s/*.yaml`、`DEVELOPMENT-GUIDE.md` §四、五
