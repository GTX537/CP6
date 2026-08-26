# CP6 专用部署 Agent Readiness

本门禁验证 `CP6-Deploy` Pool 中的专用 Windows Agent 是否具备运行 DEV CD 的最小宿主机能力。
它不读取 Secret、不 Checkout 仓库、不构建镜像，也不写入 Azure Environment 部署历史。

## 已确认的外部状态

2026-08-11 用户提供的 Azure DevOps 截图和本机服务状态确认：

| 项目 | 值 |
| --- | --- |
| Pool | `CP6-Deploy` |
| Agent | `LAPTOP-3QQ44FJS` |
| Azure 状态 | `Online` / `Idle` |
| Agent 版本 | `5.277.0` |
| Windows 服务身份 | `LAPTOP-3QQ44FJS\cp6_deploy_agent` |
| Windows 服务启动 | Automatic（Delayed Start） |
| Azure Pipeline | `CP6 Deploy Agent Readiness`（Definition ID `3`） |
| 首次成功 Run | [`#20260811.1` / Build ID `10`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=10) |
| 最新成功 Run | [`#20260825.1` / Build ID `89`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=89) |
| Run 结果 | Succeeded；更新后的 `sqlcmd` 与备份目录检查由服务身份通过 |

账号是 `docker-users` 成员，不是本机管理员。现有通用 CI Agent `CP6-Windows` 继续保留在
`Default` Pool，部署 Agent 不复用开发者的交互式 CI 身份。

## Pipeline 合同

[`azure-pipelines-deploy-agent-readiness.yml`](../../azure-pipelines-deploy-agent-readiness.yml)
只允许手工运行，并绑定：

```text
Pool: CP6-Deploy
Agent.Name: LAPTOP-3QQ44FJS
```

运行时验证：

1. Job 身份严格等于 `LAPTOP-3QQ44FJS\cp6_deploy_agent`；
2. 该身份不是本机管理员；
3. Git 和 Docker CLI 可用；
4. 由 Azure tool task 取得并实际执行 .NET SDK 8.x；
5. 由 Azure tool task 取得并实际执行 Node.js 22.x 与 npm；
6. 通过显式 named pipe 连接 Docker Desktop Linux engine；
7. Docker Compose 可用；
8. 能从注册表发现 `KOUSQLSERVER` TCP 端口并建立 TCP 连接；
9. 能从 PATH、Go sqlcmd、ODBC 18 或 ODBC 17 标准目录定位并实际执行 `sqlcmd.exe`。

Readiness 不验证 SQL 登录密码。SQL migrator/runtime/backup、RabbitMQ 和 JWT Secret 只在受限
Variable Group 的部署任务中接入。DEV CD 的外部首次运行前还要确认 Agent 可执行 `sqlcmd`，且 SQL
Server 服务账号和部署 Agent 都能按各自职责访问 `C:\CP6Backups\CP6_DEV`；该项已由 2026-08-25
Build ID `89` 的服务身份 Run 补齐。

2026-08-25 本机审计确认 `sqlcmd.exe` 位于
`C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE`，但该目录不在机器级
PATH。Readiness YAML 和备份脚本已改为显式探测标准目录，避免依赖交互用户 PATH。当天还创建了
`C:\CP6Backups\CP6_DEV`：`NT SERVICE\MSSQL$KOUSQLSERVER` 具有 Modify，
`LAPTOP-3QQ44FJS\cp6_deploy_agent` 只有 Read/Execute，宽泛继承修改权限已移除。修复合入后已由服务
身份通过 Readiness Run #89；仓库内 7 场景行为测试继续覆盖 resolver 与失败恢复。
最小权限 `cp6_dev_backup` 已创建并通过实际 TCP 权限核对，真实 CHECKSUM/VERIFYONLY 备份已由 DEV CD
Run #94/#95 生成和验证。

2026-08-11 的首次 Azure Run 由 Agent `LAPTOP-3QQ44FJS` 执行。Azure 截图显示完整 Job 和
`Verify identity, Docker, and SQL endpoint` 为绿色；本机 Worker 日志进一步确认 Build ID `10`、
Build Number `20260811.1`，该验证 Step 与最终 Job 结果均为 `Succeeded`。由于任一身份、管理员、
Docker、Compose 或 SQL TCP 断言失败都会使 PowerShell Step 失败，本次绿色结果关闭当时的宿主机
Readiness 门禁。2026-08-25 候选一度改为宿主机构建后，门禁新增 .NET 8、Node.js 22 与 npm 版本断言；
更新后的 Readiness Run #105 已通过。DEV Pipeline 现复用 CI Runtime Artifact，不再调用这些编译工具，
但 Readiness 继续保留版本断言，用于本机 Lab 回退构建和 Agent 能力漂移诊断。

## 在 Azure DevOps 创建 Pipeline

代码合入 `main` 后：

1. 打开 `Pipelines` → `New pipeline`。
2. 选择 GitHub 和 `GTX537/CP6`。
3. 选择 `Existing Azure Pipelines YAML file`。
4. Branch 选择 `main`。
5. Path 选择 `/azure-pipelines-deploy-agent-readiness.yml`。
6. 保存后将 Pipeline 重命名为 `CP6 Deploy Agent Readiness`。
7. 第一次运行若提示 Pool 未授权，选择 `View` → `Permit`，只授权本 Pipeline 使用 `CP6-Deploy`。
8. 重新运行并保存成功 Run URL/Run ID。

## 验收清单

- [x] `CP6-Deploy` Pool 已创建。
- [x] 专用 Agent 已使用独立服务身份并显示 Online。
- [x] Readiness YAML 通过仓库合同测试。
- [x] Azure Pipeline 已从 `main` YAML 创建。（Definition ID `3`）
- [x] `CP6-Deploy` 只授权给 Readiness 与 `CP6 DEV CD`，没有对所有 Pipelines 开放。
- [x] Readiness Run 成功，身份、Docker、Compose 和 SQL TCP 断言全部通过。（Build ID `10`）
- [x] 成功 Run URL/Run ID 已记录。
- [x] 宿主机 `sqlcmd` 安装位置已确认，仓库门禁不再依赖用户 PATH，并已由 Run #89 以服务身份重跑。
- [x] `C:\CP6Backups\CP6_DEV` 已创建并配置 SQL Server 写入/部署 Agent 读取的显式 ACL。
- [x] 创建并验收最小权限 `cp6_dev_backup`，同一密码保存为锁定 Azure Secret。
- [x] 使用更新后的 `main` 重跑 Readiness，确认服务身份实际发现 `sqlcmd` 与备份目录。（Build ID `89`）

上述验收现已全部完成；读取定向 DEV Variable Group、指向 `cp6-dev` 的 deployment job 已由 `CP6 DEV CD` Run #95 成功执行。后续仍须保持资源定向授权，并在三次手动验收完成前关闭自动部署。
