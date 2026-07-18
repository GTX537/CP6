# 部署与换机恢复

## 新主机恢复源码

```powershell
git lfs install
git clone https://github.com/GTX537/CP6.git C:\CP6
Set-Location C:\CP6
git checkout feat/general-role-vperm
git lfs pull
```

若要固定到迁移时刻，可 checkout `migration-2026-07-18-ready`，再创建工作分支。

## 环境准备

1. 安装 Git/Git LFS、.NET 10 SDK、Node 20.19+ 或 22.12+、Docker/WSL2。
2. 复制 `.env.example` 为 `.env`，填写新的强密码与密钥。
3. 轮换 SQL、JWT、RabbitMQ、Cloudflare Tunnel、SSH 等旧凭证。
4. `npm install`/`npm ci` 重建依赖；不要迁移 `node_modules`。

## 数据库恢复

三库备份和哈希位于 `migration/database`。严格按 `migration/README.md` 与 `deploy/runbook.md` 操作：校验 SHA-256，先起 DB，停止 API，copy backup，FILELISTONLY，restore，最后启动 API/Web。

## 本地启动与验证

```powershell
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --no-build
Set-Location cp6.web
npm install
npm run type-check
npm test
npm run build
```

Compose 服务包括 SQL Server、API、Web、Redis、RabbitMQ、Kafka 和 cloudflared。新机首次启动前审核 `.env` 与端口，不直接复用旧生产 token。

## 发布红线

- 不把 Local/Development 配置打进 API 镜像。
- 前端有变化必须重建 Web 镜像。
- 不带 `-v` 停 Compose。
- 部署后检查容器健康、API 日志、迁移、权限种子、i18n 和关键 401/403/业务路径。
- 数据库恢复和生产覆盖操作必须明确目标库，先留可回滚备份。
