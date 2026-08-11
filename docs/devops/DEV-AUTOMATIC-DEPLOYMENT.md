# DEV 自动部署（本机学习环境）

本指南把 Azure DevOps 中已通过的 `GTX537.CP6` CI 运行自动部署到本机 `cp6-dev` Docker 环境。它用于学习 CI/CD、deployment job、Secret 授权和部署证据，不是生产发布权威，也不替代现有 GitHub R2 发布链。

## 已具备的前提

- Azure Environment：`cp6-dev`。
- Agent Pool：`CP6-Deploy`；Agent `LAPTOP-3QQ44FJS` Online，并以 `cp6_deploy_agent` 服务身份运行。
- Readiness Build ID `10` 已验证该身份可访问 Docker Desktop Linux engine 和 `KOUSQLSERVER` TCP。
- Variable Group：`cp6-dev-secrets`，包含四个锁定的 Secret：
  - `CP6_DEV_DB_MIGRATOR_PASSWORD`
  - `CP6_DEV_DB_RUNTIME_PASSWORD`
  - `CP6_DEV_RABBITMQ_PASSWORD`
  - `CP6_DEV_JWT_SECRET`
- SQL Server 已存在 `cp6_dev_migrator`、`cp6_dev_runtime`，Variable Group 中的两个 SQL 密码必须分别与这两个登录一致。

不要把密码写进 YAML、Git、命令参数、部署证据或截图。Variable Group 中只能看到掩码是正确状态。

## 自动链路

```text
GTX537.CP6 在 main 成功
        ↓ pipeline completion trigger
在 CP6-Deploy 上校验完整 Git SHA 与服务身份
        ↓
用该 SHA 构建 cp6-api / cp6-web 本机镜像（只构建一次）
        ↓
deployment job 进入 Azure Environment cp6-dev
        ↓
db-init → Redis/RabbitMQ/Kafka → API/Web
        ↓
live、ready、API/Web release identity 校验
        ↓
发布 cp6-dev-evidence/deployment.json
```

只有部署任务会把四个 Azure Secret 映射成进程环境变量。构建、身份检查和证据任务都不接收 Secret。部署脚本仍保留人工本地使用的 DPAPI 模式；Azure 模式不会读取个人 Windows 凭据库。

RabbitMQ 的初始密码会写入数据目录，因此 Azure 部署使用独立的 `cp6-dev_rabbitmq-data-azure` volume。原先人工 Lab 的 `cp6-dev_rabbitmq-data` 不会被删除，可以继续用于 DPAPI 模式。

## 一次性创建 Azure Pipeline

先确认本文件和根目录 `azure-pipelines-dev.yml` 已进入远端 `main`，然后：

1. 打开 **Pipelines → Pipelines → New pipeline**。
2. 选择 CP6 当前使用的代码来源和仓库 `GTX537/CP6`。
3. 选择 **Existing Azure Pipelines YAML file**。
4. Branch 选 `main`，Path 选 `/azure-pipelines-dev.yml`。
5. 保存后把 Pipeline 名称改为 `CP6 DEV CD`。

创建时间晚于本次 CI 成功运行时，completion trigger 不会补跑历史事件。第一次应手工运行一次，并在界面提供资源版本选择时选取 `GTX537.CP6` 最新成功的 `main` 运行。YAML 会再次核对分支、完整 SHA 和 checkout；不匹配会失败关闭。

## 只授权这一条 Pipeline

第一次运行可能依次显示 Resource authorization。只批准 `CP6 DEV CD`，不要打开“允许所有 Pipelines”：

1. **CP6-Deploy → Security / Pipeline permissions**：添加 `CP6 DEV CD`；保留现有 Readiness Pipeline。
2. **Library → cp6-dev-secrets → Pipeline permissions**：添加 `CP6 DEV CD`，保持 Open access 关闭。
3. **Environments → cp6-dev**：批准 `CP6 DEV CD` 使用此受保护资源；如果页面提供 Pipeline permissions，只添加该 Pipeline。

若运行停在“需要权限”，进入对应资源完成上述授权后，再次运行同一个 CI 资源版本即可。不要给 `GTX537.CP6` 基础 CI 或 Readiness Pipeline 授予 Variable Group 权限。

## 首次验收

成功运行必须同时满足：

- `BuildCandidate` 和 `DeployDev` 两个 Stage 均为绿色。
- `cp6-dev` 不再显示 `Never deployed`，部署历史能关联本次 Run 与 commit。
- `http://127.0.0.1:19991/health/live` 和 `/health/ready` 均为 `Healthy`。
- API `/health/release` 与 Web `http://127.0.0.1:18080/release.json` 的版本、完整 Git SHA 一致。
- Run 的 Artifacts 中存在 `cp6-dev-evidence/deployment.json`；该文件只包含非敏感部署身份、镜像 ID、迁移和健康结果。

验收完成后保存 Azure Run ID 或截图，再更新项目记忆。仓库中的 YAML 成功合入只代表“自动部署能力已配置”，在 Azure 首次实际运行成功前不能写成“DEV 已自动部署”。

## 范围边界

本链路使用部署 Agent 本机镜像缓存，适合当前单机学习环境；它没有 Registry、SBOM、签名或跨机器制品推广能力。UAT/PROD-LAB 不得复制此方式重新 Build。进入 UAT 前仍需完成 Registry/发布权威决策，并把同一不可变 digest 从 DEV 推广到后续环境。
