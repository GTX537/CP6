# DEVELOPMENT ONLY

本目录是本地学习、Minikube 与开发联调用的示例清单，包含本地 SQL Server、
Redis、RabbitMQ 等有状态服务，也可能使用本地镜像或占位 Secret。它不是 R2
生产部署输入，不得复制到试点或正式集群。

R2 正式部署只能使用：

- `deploy/production/kubernetes/` 中的平台中立模板；
- Schema 2 `release-manifest.json` 中固定到 digest 的 API/Web 镜像；
- 受保护环境自托管 runner 调用 `scripts/deploy-r2.ps1`；
- 外部生产 SQL Server、Redis、消息服务和 S3 兼容证据存储。

如需修改生产拓扑，应修改 `deploy/production/kubernetes/` 与相应部署契约，
不要在本目录维护第二套生产事实源。
