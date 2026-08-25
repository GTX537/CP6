# CP6 DevOps 与 CI/CD

本目录保存 CP6 的项目级 DevOps 上下文，供开发者、Codex 和发布负责人共同使用。它回答三个问题：当前流水线已经做到了什么、目标发布链是什么、下一步按什么顺序实施。

> 当前状态：Azure DevOps 已接入 CI，但尚未成为 CP6 的生产发布权威。现有 WMS R2 候选与部署链仍由 GitHub Actions 和 [`docs/client/r2`](../client/r2/README.md) 约束。未经显式迁移验收，不得删除、绕过或弱化现有 R2 门禁。

## 文档地图

| 文档 | 类型 | 用途 |
| --- | --- | --- |
| [CI/CD 架构](./CI-CD-ARCHITECTURE.md) | Explanation | 解释当前双流水线边界、目标架构和关键取舍 |
| [Azure Pipelines 演进计划](./AZURE-PIPELINES-PLAN.md) | Reference / Roadmap | 记录阶段、任务、完成定义和迁移门禁 |
| [Azure Environments 设置](./AZURE-ENVIRONMENTS-SETUP.md) | How-to / Checklist | 创建并验收 DEV、UAT、PROD-LAB 逻辑环境 |
| [部署 Agent Readiness](./DEPLOY-AGENT-READINESS.md) | How-to / Gate | 验证专用部署身份、Docker Desktop 和本机 SQL TCP 能力 |
| [DEV 双模式发布](./DEV-AUTOMATIC-DEPLOYMENT.md) | How-to / Checklist | 配置手动/自动 `CP6 DEV CD`、部署前备份、独立 Tunnel 和安全数据旁路导入 |
| [发布流程](./RELEASE-PROCESS.md) | How-to | 说明从代码到 DEV、审批和 PROD 的标准操作顺序 |
| [环境策略](./ENVIRONMENT-STRATEGY.md) | Explanation / Reference | 定义 DEV、UAT、PROD 的用途、权限、配置和证据边界 |
| [WMS R2 生产就绪主规范](../client/r2/README.md) | Normative | 当前生产候选、部署和现场试点的唯一规范源 |

## 当前事实

仓库内可直接验证的 Azure CI 配置位于根目录 [`azure-pipelines.yml`](../../azure-pipelines.yml)：

- `main` 提交触发；`pr: none`，当前不承担 PR 验证。
- 使用 Azure DevOps `Default` self-hosted agent pool；YAML 没有绑定具体 Agent 名称。
- 安装 .NET 8 SDK 和 Node.js 22。
- 还原、构建并测试 `CP6.WebApi`、`CP6.Tests`、`CP6.Client.Tests`。
- 执行 Vue 类型检查、Vitest 和生产构建。
- 该基础 CI 自身不构建/推送镜像，也不部署环境；独立 `azure-pipelines-dev.yml` 负责本机 DEV 双模式学习链，自动开关初始关闭，外部手动 Run 尚待验收。

项目上下文确认 self-hosted Agent 已接通并能执行该 CI。具体 Agent 名称、在线状态和历史运行结果属于 Azure DevOps 外部运行证据，不能只靠仓库文件推断。

仓库内同时存在更完整的 GitHub R2 发布链：

- PR/main 合同门禁：`.github/workflows/client-contract.yml`、`wms-production-sql.yml`。
- 版本冻结：`.github/workflows/r2-freeze.yml`。
- 候选制品、GHCR 镜像、SBOM、漏洞扫描和签名：`.github/workflows/r2-candidate.yml`。
- 受保护环境、数据库初始化、digest 部署和运行身份验证：`.github/workflows/r2-deploy.yml`。

## 当前完成与未完成

| 层次 | 状态 | 准确描述 |
| --- | --- | --- |
| CI 代码验证 | 已配置并已接通 | Azure self-hosted Agent 可执行后端/客户端测试与 Web 检查 |
| 发布制品 | Azure 未完成；GitHub R2 已有实现 | Azure 尚未产出 `cp6-api` / `cp6-web` 镜像或不可变清单 |
| 本机 Lab 运行环境 | 已完成 | DEV/UAT/PROD-LAB Compose project 已实际启动并通过健康/身份验证 |
| Azure 逻辑 Environments | 已创建 | `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 已由 2026-08-11 外部截图验证，当前均为 `Never deployed` |
| 专用部署 Agent | Readiness 已通过 | `CP6-Deploy` 使用 `cp6_deploy_agent` 服务身份；Azure Build ID `10` 验证身份、Docker、Compose 与 SQL TCP |
| Azure DEV 双模式发布 | 仓库闭环已交付，Azure 运行待验收 | 同一 YAML 支持手动和受开关控制的自动模式，拒绝失败/非 main/过期自动 Run；部署前备份并 VERIFYONLY，停旧应用后前向迁移，发布触发/备份/镜像/健康证据 |
| 白天测试公网 | 工具已交付，切换待执行 | `cp6-public-tunnel` 只连接 `cp6-dev_default`；切换前必须显式停止旧 `cp6-cloudflared`，Pipeline 不自动切换 Cloudflare |
| 私人本地 `cp6`/`CP6DB` | 保持独立 | DEV CD 不操作根 Compose、`CP6DB` 或 `cp6_cp6-db-data`；DEV 数据只能手动恢复为新的 `CP6DEV_IMPORT_*` 旁路库 |
| PROD 审批与部署 | Azure 未完成；GitHub R2 有受控实现 | 不得把 Azure CI 成功描述为生产上线 |

## 核心原则

1. **Build once, deploy many**：同一 API/Web 镜像只构建一次，DEV、UAT、PROD 推广同一 digest。
2. **生产按 digest 部署**：SemVer 和 Git SHA 用于追踪；运行环境只接受 `repository@sha256:digest`。
3. **发布身份可追溯**：至少记录版本、完整 Git SHA、镜像 digest、Pipeline Run ID、批准人、部署时间和验证证据。
4. **数据库只前向迁移**：初始化先于 API/Web；回退应用前先证明 Schema 兼容，数据库故障用更高版本迁移前滚修复。
5. **审批不由 YAML 作者控制**：PROD Approval/Checks 放在 Azure Environment 或其他受保护资源上。
6. **不产生双重真相源**：ACR 是 Azure 路线的候选目标；当前 R2 使用 GHCR。切换前必须明确 registry、候选清单和发布权威的唯一来源。

## Codex 接手顺序

处理 Azure DevOps 或发布任务前，依次阅读：

1. 根目录 [`AGENTS.md`](../../AGENTS.md)；
2. 本页与 [Azure Pipelines 演进计划](./AZURE-PIPELINES-PLAN.md)；
3. [R2 主规范](../client/r2/README.md) 和相关生产规范；
4. 实际 `azure-pipelines.yml`、`.github/workflows/`、Dockerfile 与 `deploy/production/`。

任何实现任务都必须从最新 `main` 创建单任务分支。根工作区有未提交改动时使用独立 worktree。

## 外部参考

- [Azure Pipelines Docker@2 任务](https://learn.microsoft.com/zh-cn/azure/devops/pipelines/tasks/reference/docker-v2?view=azure-pipelines)
- [Azure Pipelines Environments 与部署历史](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/environments?view=azure-devops)
- [Azure Pipelines Approvals and checks](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/approvals?view=azure-devops)
