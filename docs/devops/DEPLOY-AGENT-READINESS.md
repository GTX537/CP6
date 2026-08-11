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
| Azure Pipeline | `GTX537.CP6 (3)`（Definition ID `3`；待重命名） |
| 首次成功 Run | [`#20260811.1` / Build ID `10`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=10) |
| Run 结果 | Succeeded |

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
4. 通过显式 named pipe 连接 Docker Desktop Linux engine；
5. Docker Compose 可用；
6. 能从注册表发现 `KOUSQLSERVER` TCP 端口并建立 TCP 连接。

Readiness 不验证 SQL 登录密码。SQL migrator/runtime、RabbitMQ 和 JWT Secret 将在后续受限
Variable Group 任务中接入。

2026-08-11 的首次 Azure Run 由 Agent `LAPTOP-3QQ44FJS` 执行。Azure 截图显示完整 Job 和
`Verify identity, Docker, and SQL endpoint` 为绿色；本机 Worker 日志进一步确认 Build ID `10`、
Build Number `20260811.1`，该验证 Step 与最终 Job 结果均为 `Succeeded`。由于任一身份、管理员、
Docker、Compose 或 SQL TCP 断言失败都会使 PowerShell Step 失败，本次绿色结果关闭宿主机 Readiness 门禁。

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
- [ ] `CP6-Deploy` 只授权给 Readiness/后续 DEV CD Pipeline，而不是所有 Pipelines。
- [x] Readiness Run 成功，身份、Docker、Compose 和 SQL TCP 断言全部通过。（Build ID `10`）
- [x] 成功 Run URL/Run ID 已记录。

这些验收项全部完成后，才创建读取 DEV Variable Group 并指向 `cp6-dev` 的 deployment job。
