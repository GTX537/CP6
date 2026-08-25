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

## 白天临时家庭测试服务器

当前方案由这台 Windows 电脑上的 Docker Desktop 运行 CP6，Compose 内的 `cp6-cloudflared` 把 Web/API 暴露为：

- 同事访问：`https://cp6.uk`
- 公网 API 就绪检查：`https://api.cp6.uk/health/ready`
- 本机访问：`http://127.0.0.1:8080`
- 本机 API 就绪检查：`http://127.0.0.1:9991/health/ready`

双击根目录 `cp6-daytime-server.bat` 可使用操作菜单，也可在终端运行：

```powershell
.\cp6-daytime-server.bat start        # 复用现有镜像，日常启动
.\cp6-daytime-server.bat start-build  # 代码/依赖有变化时显式重建
.\cp6-daytime-server.bat status       # 检查 7 个容器及本机/公网地址
.\cp6-daytime-server.bat close        # 只停公网 Tunnel，本机服务继续运行
.\cp6-daytime-server.bat stop         # 安全停止全栈，保留容器和命名卷
```

运行边界：

- 此流程不会禁止 Windows 自动睡眠，也不会修改电源计划或计划任务。电脑睡眠、关机、Docker Desktop 退出或网络中断后，`cp6.uk` 暂时不可访问是预期行为；电脑恢复后手动执行 `start` 和 `status`。
- 同事只使用 `https://cp6.uk` 和分配给他们的应用账号。不要共享 `.env`、Tunnel JSON、数据库密码、JWT 密钥、RabbitMQ/Kafka 管理入口或本机基础设施端口。
- `close` 只停止 Docker 中的 CP6 Tunnel。若检测到 Windows 主机另有 cloudflared 进程，脚本只告警而不自动结束，避免误停其他项目。
- `stop` 使用 `docker compose stop`，不会删除数据库、Redis、RabbitMQ、Kafka 或 i18n 数据卷。不得使用 `docker compose down -v`，除非已明确接受不可恢复的数据卷删除风险并完成备份。
- 这是白天临时测试环境，不提供夜间可用性、高可用、SLA 或生产级运维承诺；需要持续在线时应迁移到正式云主机或托管平台。
- Cloudflare Workers 的 `estimate` Git 集成与这条 Docker + Tunnel 链无关，必须在 Cloudflare 控制台作为独立事项处理。

## 发布红线

- 不把 Local/Development 配置打进 API 镜像。
- 前端有变化必须重建 Web 镜像。
- 不带 `-v` 停 Compose。
- 部署后检查容器健康、API 日志、迁移、权限种子、i18n 和关键 401/403/业务路径。
- 数据库恢复和生产覆盖操作必须明确目标库，先留可回滚备份。
