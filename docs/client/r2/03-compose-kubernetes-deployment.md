# 03 Compose 与 Kubernetes 部署

## 1. 拓扑

试点使用 `deploy/production/compose/compose.yaml`，只包含一次性 `db-init`、
API 和 Web；SQL Server、Redis、消息服务、S3 均为外部服务。试点允许单
API/Web 实例。

多仓使用 `deploy/production/kubernetes/`：API/Web 至少两个副本，配置
滚动更新、PDB、资源 requests/limits、拓扑分散、探针；API Service 保留
会话亲和，Ingress 使用 TLS 与 cookie affinity。SignalR 使用 TLS Redis
backplane，权限上下文也通过 Redis 跨实例共享和失效。语言包及运行期共享
文件使用 S3 兼容对象存储，Pod 不保存持久状态。

## 2. 配置与密钥

环境 runner 调用受控 `CP6_VAULT_RENDERER`，把密钥库值临时渲染到 runner
临时目录。`scripts/deploy-r2.ps1` 用该文件创建 Compose 环境或 Kubernetes
Secret，完成后删除。日志、Git、GitHub Artifact 和证据禁止包含明文。

生产配置由 `ProductionConfigurationValidator` 在监听端口前校验，至少覆盖
TLS、SQL Server、Redis、JWT、CORS/OIDC、原生制品地址/哈希、S3 和启动模式。

## 3. 数据库初始化

生产 API 必须使用 `Startup:Mode=Api` 且
`DatabaseInitialization:SkipOnStartup=true`，不得由多个副本启动时迁移。

- Compose：先单独运行 `db-init`，成功退出后再启动 API/Web。
- Kubernetes：先运行固定名部署前 Job，成功后才 rollout API/Web。

初始化容器使用同一 API 镜像 digest，但设置 `Startup:Mode=DatabaseInit`。
数据库迁移只前向执行。应用回滚前必须证明旧应用可读取新 Schema；禁止执行
数据库降级迁移。

## 4. 部署与核对

部署工作流 `.github/workflows/r2-deploy.yml` 必须在受保护 Environment 的
`[self-hosted, Windows, X64, cp6-deploy]` runner 上：

1. 从批准的 `s3://` URI 下载清单并核对批准 SHA-256；
2. 从对应 `vX.Y.Z` Tag checkout；
3. 临时渲染 Secret；
4. 调用 `scripts/deploy-r2.ps1` 执行初始化和 digest 固定 rollout；
5. 调用 `scripts/test-r2-deployment.ps1` 核对 live/ready/release、
   `__EFMigrationsHistory` 最新值、运行镜像摘要、bootstrap 与远程原生制品；
6. 生成并 Object Lock 归档 `deployment-evidence.json`。

`GET /health/release` 与 Web `release.json` 为只读发布身份；响应必须
`no-store` 且不含 Secret、连接串、主机内部地址或异常详情。

## 5. 回滚

应用回滚只能选择已有 Schema 兼容证明的旧镜像 digest。若数据库初始化失败，
不启动新 API；若 rollout 或身份核对失败，停止流量切换并恢复上一兼容应用
digest。数据库问题通过新迁移前滚修复，不执行 Down。每次处理产生新的部署
证据，不修改原证据。
