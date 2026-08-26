# CP6 DevOps 与 CI/CD

本目录保存 CP6 的项目级 DevOps 上下文，供开发者、Codex 和发布负责人共同使用。它回答三个问题：当前流水线已经做到了什么、目标发布链是什么、下一步按什么顺序实施。

> 当前状态：Azure DevOps 已接入 CI，但尚未成为 CP6 的生产发布权威。现有 WMS R2 候选与部署链仍由 GitHub Actions 和 [`docs/client/r2`](../client/r2/README.md) 约束。CRM V1 的私有 R00 摘要 `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03` 已 Accepted，公开 [ADR-CRM-R00](./adr/ADR-CRM-R00-RELEASE-AUTHORITY.md) 镜像已同步为 Complete；P09/P10 未完成前不得声称候选对象身份 Gap 已关闭。

## 文档地图

| 文档 | 类型 | 用途 |
| --- | --- | --- |
| [CI/CD 架构](./CI-CD-ARCHITECTURE.md) | Explanation | 解释当前双流水线边界、目标架构和关键取舍 |
| [Azure Pipelines 演进计划](./AZURE-PIPELINES-PLAN.md) | Reference / Roadmap | 记录阶段、任务、完成定义和迁移门禁 |
| [发布权威与 Registry ADR](./adr/ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md) | Accepted ADR | 固定 GitHub R2/GHCR 唯一候选权威和 Azure 只读影子边界 |
| [Azure Release Shadow 设计](./AZURE-DOCKER-RELEASE-SHADOW-DESIGN.md) | Design / Contract | 设计 Phase 3 的只读候选链验证、失败关闭和 Shadow 证据 |
| [Azure Environments 设置](./AZURE-ENVIRONMENTS-SETUP.md) | How-to / Checklist | 创建并验收 DEV、UAT、PROD-LAB 逻辑环境 |
| [部署 Agent Readiness](./DEPLOY-AGENT-READINESS.md) | How-to / Gate | 验证专用部署身份、Docker Desktop 和本机 SQL TCP 能力 |
| [DEV 双模式发布](./DEV-AUTOMATIC-DEPLOYMENT.md) | How-to / Checklist | 配置手动/自动 `CP6 DEV CD`、部署前备份、独立 Tunnel 和安全数据旁路导入 |
| [发布流程](./RELEASE-PROCESS.md) | How-to | 说明从代码到 DEV、审批和 PROD 的标准操作顺序 |
| [环境策略](./ENVIRONMENT-STRATEGY.md) | Explanation / Reference | 定义 DEV、UAT、PROD 的用途、权限、配置和证据边界 |
| [DevOps ADR 索引](./adr/README.md) | Normative mirror / Index | CRM R00 发布权威、候选对象身份、Manifest 与回退工程合同 |
| [WMS R2 生产就绪主规范](../client/r2/README.md) | Normative | 当前生产候选、部署和现场试点的唯一规范源 |

## 当前事实

仓库内可直接验证的 Azure CI 配置位于根目录 [`azure-pipelines.yml`](../../azure-pipelines.yml)：

- `main` 提交触发；`pr: none`，当前不承担 PR 验证。
- 使用 Azure DevOps `Default` self-hosted agent pool；YAML 没有绑定具体 Agent 名称。该 Agent 只执行合同、受认证下载、摘要/清单验证和 Azure Artifact 发布，不再运行 .NET/Node 编译。
- GitHub `.github/workflows/client-contract.yml` 在 GitHub-hosted Runner 完成 .NET、客户端、OpenAPI、Web、Android 与 R2 source 门禁，并生成名称含完整 Git SHA、内部逐文件 SHA-256 的 `cp6-dev-runtime-<sha>`；保留期为 3 天。
- Azure 只接受同一仓库、同一完整 SHA、指定工作流路径、`push`/`workflow_dispatch` 事件且结论为 `success` 的未过期 Artifact；下载归档还必须匹配 GitHub SHA-256，解压后再次验证内部 manifest。
- Azure [`Run #116`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=116) 因错误查询非仓库专属 Checkout extraheader 在下载前失败，Publish 被跳过；修复后分支 [`Run #117`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=117) 与 main [`Run #118`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=118) 均成功下载、验证并发布 Azure `cp6-dev-runtime`。SQL 与公网七容器基线未变。
- 该桥自身不构建/推送生产镜像，也不部署环境；独立 `azure-pipelines-dev.yml` 下载并验证所选成功 `main` Azure Artifact 后只做 runtime-only 镜像封装。Manual DEV [`Run #95`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=95)、[`#120`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=120)、[`#121`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=121) 已完成 3/3。#129 证明 2 GiB + 3 次 SQL 门禁会在备份前失败关闭；#131 证明同 Stage 重试必须按 `System.StageAttempt` 区分证据 Artifact。修复后基础 CI [`#132`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=132) 自动触发 DEV [`#133`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=133)，600 秒 readiness、备份、部署、健康/身份和 `cp6-dev-evidence-attempt-1` 均成功。自动开关保持开启，公网验证仍关闭。

项目上下文确认 self-hosted Agent 已接通并能执行该 CI。具体 Agent 名称、在线状态和历史运行结果属于 Azure DevOps 外部运行证据，不能只靠仓库文件推断。

仓库内同时存在更完整的 GitHub R2 发布链：

- PR/main 合同门禁：`.github/workflows/client-contract.yml`、`wms-production-sql.yml`。
- 版本冻结：`.github/workflows/r2-freeze.yml`。
- 候选制品、GHCR 镜像、SBOM、漏洞扫描和签名：`.github/workflows/r2-candidate.yml`。
- 受保护环境、数据库初始化、digest 部署和运行身份验证：`.github/workflows/r2-deploy.yml`。

Azure Release Shadow S0 已有独立仓库实现：根目录 [`azure-pipelines-release-shadow.yml`](../../azure-pipelines-release-shadow.yml) 固定 `trigger: none`、`pr: none`，只在无 Secret 环境验证仓库内固定 fixture。`scripts/Test-Cp6ReleaseShadowCandidate.ps1` 逐层校验 candidate result、Schema 2 manifest、freeze/spec 哈希、GHCR allowlist/digest、供应链、签名和 ForwardOnly 元数据；输出固定为 `Authority=Shadow`、`Deployable=false`。S0 不访问真实 R2/GHCR，不拉取镜像，也未在 Azure 创建 Pipeline definition 或 Service Connection。

## 当前完成与未完成

| 层次 | 状态 | 准确描述 |
| --- | --- | --- |
| CI 代码验证 | 已配置并已接通 | GitHub-hosted `client-contract` 执行完整编译/测试；Azure self-hosted Agent 只桥接经 SHA/摘要验证的运行包 |
| 发布权威与 Registry | 已决策 | GitHub R2 + GHCR 是唯一候选权威；Schema 2 manifest + candidate result 是唯一候选链；Azure 只允许非权威 Shadow 验证 |
| Azure Release Shadow | S0 仓库合同已实现；S1 未开始 | 手动 YAML、离线 parser/fixture、10 个失败关闭场景和静态能力门禁已建立；尚未读取真实候选、GHCR 或外部证据 |
| 发布制品 | Azure 不重复构建；GitHub R2 已有实现 | Azure 不为同一版本产出 `cp6-api` / `cp6-web` 镜像或第二份清单；ACR 当前未批准 |
| 本机 Lab 运行环境 | 已完成 | DEV/UAT/PROD-LAB Compose project 已实际启动并通过健康/身份验证 |
| Azure 逻辑 Environments | DEV 已有部署历史 | `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 已创建；`cp6-dev` 由 DEV CD Run #95 写入首次成功部署历史，UAT/PROD-LAB 仍未部署 |
| 专用部署 Agent | Readiness 已通过 | `CP6-Deploy` 使用 `cp6_deploy_agent` 服务身份；最新 Readiness [`Run #89`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=89) 验证身份、Docker、Compose、SQL TCP、`sqlcmd` 与备份目录 |
| Azure DEV 双模式发布 | 手动/自动均已验收 | Pipeline/Pool/Variable Group/Environment 均为定向授权，`cp6-dev` 配置 Exclusive lock；#95/#120/#121 Manual 3/3，#129 证明低内存失败关闭，#131 暴露并修复重试证据命名，#132→#133 最终自动发布成功。7 份备份均保留，最新 CHECKSUM/VERIFYONLY 与本机 SHA-256 复核通过；公网验证保持关闭，根环境基线不变 |
| 白天测试公网 | 工具已交付，切换待执行 | `cp6-public-tunnel` 只连接 `cp6-dev_default`；切换前必须显式停止旧 `cp6-cloudflared`，Pipeline 不自动切换 Cloudflare |
| 私人本地 `cp6`/`CP6DB` | 保持独立 | DEV CD 不操作根 Compose、`CP6DB` 或 `cp6_cp6-db-data`；DEV 数据只能手动恢复为新的 `CP6DEV_IMPORT_*` 旁路库 |
| PROD 审批与部署 | Azure 未完成；GitHub R2 有受控实现 | 不得把 Azure CI 成功描述为生产上线 |
| CRM R00 | 私有源 Accepted；公开同步 Complete；P09/P10 Pending | GHCR/GitHub R2 已固定为 V1 唯一权威，但精确对象版本与四仓 Manifest 尚未实现 |

## 核心原则

1. **Build once, deploy many**：同一 API/Web 镜像只构建一次，DEV、UAT、PROD 推广同一 digest。
2. **生产按 digest 部署**：SemVer 和 Git SHA 用于追踪；运行环境只接受 `repository@sha256:digest`。
3. **发布身份可追溯**：至少记录版本、完整 Git SHA、镜像 digest、Pipeline Run ID、批准人、部署时间和验证证据。
4. **数据库只前向迁移**：初始化先于 API/Web；回退应用前先证明 Schema 兼容，数据库故障用更高版本迁移前滚修复。
5. **审批不由 YAML 作者控制**：PROD Approval/Checks 放在 Azure Environment 或其他受保护资源上。
6. **不产生双重真相源**：当前唯一 Registry 是 GHCR，唯一候选权威是 GitHub R2；Azure 只读验证同一 digest。ACR 迁移必须通过新的 ADR 和等价验收。

## Codex 接手顺序

处理 Azure DevOps 或发布任务前，依次阅读：

1. 根目录 [`AGENTS.md`](../../AGENTS.md)；
2. 本页与 [Azure Pipelines 演进计划](./AZURE-PIPELINES-PLAN.md)；
3. [R2 主规范](../client/r2/README.md)、[ADR-CRM-R00](./adr/ADR-CRM-R00-RELEASE-AUTHORITY.md) 和相关生产规范；
4. 实际 `azure-pipelines.yml`、`.github/workflows/`、Dockerfile 与 `deploy/production/`。

任何实现任务都必须从最新 `main` 创建单任务分支。根工作区有未提交改动时使用独立 worktree。

## 外部参考

- [Azure Pipelines Docker@2 任务](https://learn.microsoft.com/zh-cn/azure/devops/pipelines/tasks/reference/docker-v2?view=azure-pipelines)
- [Azure Pipelines Environments 与部署历史](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/environments?view=azure-devops)
- [Azure Pipelines Approvals and checks](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/approvals?view=azure-devops)
