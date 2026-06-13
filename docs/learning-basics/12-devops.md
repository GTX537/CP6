# 12 · Docker / Compose / K8s / cloudflared

## 🌱 你将学到

- 容器到底是什么（不是"很轻的虚拟机"这种敷衍解释）
- 看懂 CP6 的 Dockerfile + docker-compose.yml
- Kubernetes 的 Deployment / Service / Ingress 各干什么
- cloudflared 怎么把内网应用暴露到公网而无需开端口

---

## 🍳 生活类比

### 容器 vs 虚拟机：集装箱 vs 货船

**虚拟机**：买整艘货船，自己开。带燃料、船员、厨房、卧室。重。
**容器**：你的货物装进标准集装箱，放在共用货船上。轻、可移动、互不影响。

容器**共用宿主机的内核**，每个容器只是一组进程被"圈起来"的隔离空间。所以：

- 启动快（不用启操作系统，启进程就行）
- 占用小（不带 OS 内核）
- 但隔离没虚拟机彻底（依赖宿主机内核）

### Docker Compose vs K8s：宿舍 vs 学校

- **docker-compose** = 一个宿舍管理几个室友（几个容器）。一台机器够用。
- **K8s** = 学校管理几百栋宿舍（几百台机器，几千个容器）。复杂但能扩展。

CP6 演示用 Compose，生产部署 K8s。

---

## 🔎 看 CP6 代码

### CP6.WebApi/Dockerfile

```dockerfile
# 阶段 1：用 SDK 镜像编译
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先复制 csproj 文件做 restore（缓存技巧）
COPY CP6.Entity/CP6.Entity.csproj CP6.Entity/
COPY CP6.Core/CP6.Core.csproj CP6.Core/
COPY CP6.WebApi/CP6.WebApi.csproj CP6.WebApi/
RUN dotnet restore CP6.WebApi/CP6.WebApi.csproj

# 复制源码 + publish
COPY . .
RUN dotnet publish CP6.WebApi -c Release -o /app/publish --no-restore

# 阶段 2：只用 Runtime 镜像跑（不带编译器，小很多）
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENTRYPOINT ["dotnet", "CP6.WebApi.dll"]
```

两阶段构建：

- 阶段 1：装备齐全的"编译厂房"（SDK 镜像 ~800MB）。编译完丢弃。
- 阶段 2：极简"运行环境"（Runtime 镜像 ~200MB）。只放编译产物。

最终镜像只有阶段 2 那部分，不带阶段 1 的 SDK。**这就是为什么镜像能从 800MB 缩到 200MB**。

### docker-compose.yml

```yaml
services:
  cp6-db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:?Set MSSQL_SA_PASSWORD in .env}"
    volumes:
      - cp6-db-data:/var/opt/mssql       # 数据持久化
    healthcheck:                            # 健康检查
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd ..."]
      interval: 10s
      retries: 20
    restart: unless-stopped

  cp6-api:
    build:
      context: .
      dockerfile: CP6.WebApi/Dockerfile
    environment:
      ConnectionStrings__DefaultConnection: "Server=cp6-db;Database=CP6DB;..."
      JWT__Secret: "${JWT_SECRET:?Set JWT_SECRET (>=32 chars) in .env}"
    depends_on:
      cp6-db: { condition: service_healthy }   # 等 DB 健康才启动
    ports:
      - "9991:5000"                            # 宿主机 9991 → 容器 5000

  cp6-web:
    build:
      context: ./cp6.web
    ports:
      - "8080:80"
    depends_on:
      - cp6-api

  cp6-cloudflared:
    image: cloudflare/cloudflared:latest
    command: tunnel run
    volumes:
      - ./cloudflared-docker:/etc/cloudflared:ro

volumes:
  cp6-db-data:
  cp6-redis-data:
  cp6-mq-data:
  cp6-kafka-data:
```

要点：

- 5 个服务（db、api、web、redis、mq、kafka、cloudflared）
- `depends_on` + `healthcheck` 控制启动顺序
- `volumes` 持久化数据（重启容器数据还在）
- `${VAR:?msg}` 强制 `.env` 里有这个变量

### K8s 三大对象（最重要的）

**Deployment**：声明"我要 N 个副本的这个容器"

```yaml
apiVersion: apps/v1
kind: Deployment
metadata: { name: cp6-api, namespace: cp6 }
spec:
  replicas: 2                  # 我要 2 个
  template:
    spec:
      initContainers:
        - name: wait-for-db
          image: busybox
          command: ['sh', '-c', 'until nc -z cp6-db 1433; do sleep 3; done']
      containers:
        - name: api
          image: cp6-api:latest
          ports: [{ containerPort: 5000 }]
          envFrom:
            - configMapRef: { name: cp6-config }
          readinessProbe:        # 啥时候算"准备好接流量"
            tcpSocket: { port: 5000 }
            initialDelaySeconds: 15
          livenessProbe:         # 啥时候算"活着"
            tcpSocket: { port: 5000 }
            failureThreshold: 5
```

**Service**：稳定的访问入口（Pod 重启 IP 会变，Service 不变）

```yaml
apiVersion: v1
kind: Service
metadata: { name: cp6-api, namespace: cp6 }
spec:
  selector: { app: cp6-api }
  ports: [{ port: 5000, targetPort: 5000 }]
  type: ClusterIP            # 只集群内访问
```

**Ingress**：L7 路由（一个入口路由到多个 Service）

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: cp6-ingress
  namespace: cp6
  annotations:
    nginx.ingress.kubernetes.io/websocket-services: "cp6-api"   # SignalR 用
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

## 🤔 为什么这样

### Q1: 为什么多阶段构建

如果一阶段直接打包：

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0
COPY . .
RUN dotnet publish
ENTRYPOINT ["dotnet", "CP6.WebApi.dll"]
```

最终镜像 800MB，包含编译器、调试器等运行时不需要的东西。生产白浪费。

多阶段把编译产物拷到极简镜像，最终 200MB。每次部署节省 600MB 传输。

### Q2: healthcheck + depends_on 多此一举？

普通 `depends_on` 只等容器**启动**（进程在跑）。
SQL Server 容器启动后还要 30 秒才能真接受连接（初始化 DB）。

`healthcheck` 让 Compose 知道"容器是否真的可用"，配合 `depends_on: condition: service_healthy` 才正确等待。

### Q3: ${VAR:?msg} 这个是啥

shell 语法。"如果 VAR 没设置，就报这条 msg 并退出"。

CP6 用它强制本地必须配置敏感变量：

```yaml
MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:?Set MSSQL_SA_PASSWORD in .env}"
```

如果你启动时 `.env` 没配 `MSSQL_SA_PASSWORD`，立刻报错而不是用默认密码"docker"被攻击。

### Q4: readinessProbe vs livenessProbe

| 探针 | 目的 | 失败后果 |
|---|---|---|
| **readinessProbe** | 准备好接流量了吗？ | 从 Service 端点摘除 → 不收新流量 |
| **livenessProbe** | 还活着吗？ | 杀掉 Pod 重启 |

CP6 用 `tcpSocket`（端口能连 = 健康）。更精确做法是 HTTP `/health` 端点检查 DB + Redis + MQ 全通。

### Q5: 为什么用 cloudflared

要把 `cp6.uk` 暴露到公网，传统方式：

- 买 VPS + 公网 IP
- 配防火墙 / 端口转发
- 配 HTTPS 证书
- 防 DDoS

cloudflared 隧道：

- 容器从内网主动连 Cloudflare 边缘
- 公网请求 → Cloudflare → 隧道 → 你的容器
- 不开任何入站端口
- 自带 HTTPS / DDoS 防护 / CDN

凭证文件存 `cloudflared-docker/`，被 `.gitignore` 排除，绝不入 Git。

---

## ⚠️ 容易搞错的地方

### 1. Docker 镜像没改但容器不更新

```bash
docker compose up -d   # ❌ 用缓存的旧镜像
docker compose up -d --build   # ✅ 强制重新构建
```

CI 流程：每次提交 build + 用 git commit hash 当镜像 tag（不要只用 `:latest`）。

### 2. volumes 删了数据丢

```bash
docker compose down       # 删容器，volumes 还在
docker compose down -v    # 删容器 + volumes（生产慎用）
```

如果你 down -v 后 SQL Server 容器再起来，DB 是全新的。这是改密码生效的方式（旧 DB 已经存了旧密码 hash）。

### 3. K8s imagePullPolicy 用错

```yaml
imagePullPolicy: Never        # ← minikube 本地镜像
```

minikube 用本地镜像可以。生产集群必须改 `IfNotPresent` 或 `Always`，配合远程 registry。

### 4. HPA 不工作

```bash
kubectl get hpa
# TARGETS: <unknown>/70%
```

`<unknown>` 通常 = Metrics Server 没装。`minikube addons enable metrics-server` 解决。

### 5. cloudflared 凭证误入仓库

```
cloudflared-docker/cert.pem   ← 必须 .gitignore
```

凭证泄露 = 别人接管你的隧道，能假冒你的网站。CP6 README 反复强调。

---

## ✋ 动手试试

### 任务 1：跑通 docker compose

确认 Docker Desktop 装了并启动。CP6 根目录创建 `.env` 文件：

```
MSSQL_SA_PASSWORD=YourStrong!Passw0rd
RABBITMQ_USER=cp6
RABBITMQ_PASSWORD=cp6pwd
JWT_SECRET=this-secret-must-be-at-least-32-chars-long
```

启动：

```bash
cd D:\CP6
docker compose up -d --build
```

看 5 个容器全部 healthy（用 `docker compose ps`）。

访问 `http://localhost:8080` 应该看到 CP6 前端。

### 任务 2：观察容器之间的网络

```bash
docker exec -it cp6-api bash
```

进入 api 容器，里面：

```bash
ping cp6-db    # 能 ping 通（容器名字就是 host name）
```

这是 docker compose 的"容器名 = host name"网络。

容器间用 `cp6-db:1433` 连，宿主机用 `localhost:1433` 连。两套地址。

### 任务 3：看 Dockerfile 缓存生效

第一次构建：

```bash
docker compose build cp6-api   # 很慢，几分钟
```

什么都不改再 build：

```bash
docker compose build cp6-api   # 几秒钟
```

为什么这么快？Docker 看每层有没有变，没变就用缓存。

故意改一行 csproj 再 build → restore 那层失效要重跑。
故意改一行 .cs 源文件再 build → restore 还在缓存（因为 csproj 没变），publish 那层失效。

这是 CP6 Dockerfile **先 COPY csproj 后 COPY 源码**的精妙。

### 任务 4：体验 minikube + K8s（可选，时间充裕时做）

```bash
minikube start
minikube addons enable ingress
minikube addons enable metrics-server

cd D:\CP6\k8s
kubectl apply -f namespace.yaml
kubectl apply -f configmap.yaml
kubectl apply -f secret.yaml
# ... 一个个 apply

kubectl get pods -n cp6   # 看 Pod 状态
kubectl get svc -n cp6
kubectl get ingress -n cp6
```

少有任何环节失败正常，K8s 入门曲线本来就陡。读官方文档配合 CP6 的 `k8s/*.yaml` 慢慢来。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/12-devops.md`](../learning/12-devops.md)
- Docker 官方教程：[Get Started](https://docs.docker.com/get-started/)
- K8s 官方：[概念](https://kubernetes.io/docs/concepts/)
- Cloudflare Tunnel：[文档](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
- 关键词搜索："Docker 多阶段构建"、"K8s Pod Deployment Service"
