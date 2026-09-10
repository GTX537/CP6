# 本地验证与 GitHub Actions 触发策略

2026-09-10 用户明确要求暂停普通编译、测试的自动 GitHub Actions。后续是否恢复额度不改变授权边界：远程验证必须先说明原因、范围与预计分钟数，并取得新的明确授权。根目录 [AGENTS.md](../../AGENTS.md) 保存长期执行规则。

## 本次触发变更

| 工作流 | 关闭的触发 | 保留的能力 |
| --- | --- | --- |
| `client-contract.yml` | `push(main)`、`pull_request` | 手动执行原有 .NET/Web/Android、OpenAPI、R2 source 检查及 DEV runtime artifact 生成 |
| `wms-production-sql.yml` | `push(main)`、`pull_request` | 手动执行原有 WMS 与 OIDC SQL 集成测试 |
| `space-ga-evidence.yml` | 带路径过滤的 `push(main)`、`pull_request` | 手动执行原有证据完整性及负向检查 |
| `crm-saas-public-contract.yml` | 带路径过滤的 `push(main)`、`pull_request` | 手动执行原有公开契约与审批证据验证 |
| `crm-v1-prd-head.yml` | `pull_request` | 手动诊断；仍不冒充受保护的 `crm-v1-prd` 检查 |
| `crm-v1-prd.yml` | 带路径过滤的 `push(main)`、`pull_request_target` | 只从当前受保护 main 手动启动，按明确的 `candidate_sha` 用可信规则验证候选数据 |
| `p10-platform-preflight.yml` | `push(codex/p10-native-scan-preflight)` | 仍在原限定分支手动执行；原环境、凭证范围及 source 检查保留 |

上述工作流只保留 `workflow_dispatch`，原来没有定时触发。本次保留所有测试脚本和作业，既有 `crm-v1-prd` 的受保护规则来源仍为 main；候选目录只作为数据读取，不运行其验证器。手动 PRD 运行的结果绑定其真实 dispatch 源和输入，不伪造候选提交的成功状态。

R2 的受保护 Tag 候选、release freeze、受保护环境部署，以及原有手动 P10 validation/publication/audit 均保持原文件与触发条件。这些属于正式候选、发布或部署链，不因包含编译而整体停用。本次也未调整分支保护或 Azure/其他仓库/全局配置。

## 依赖与当前集成阻塞

`client-contract` 同时生成 GitHub `cp6-dev-runtime-<sha>`。Azure CI 是该同源成功 Artifact 的消费者，不自行编译、也不会代替本次禁用的 GitHub 运行。改为手动后，新的 main 提交不再自动获得此 Artifact；没有实际成功 Artifact 时，Azure bridge 与后续 DEV 推广不能宣称就绪。需要这条链时，须另外说明范围和成本并取得授权；发布、部署和迁移权限仍由各自门禁控制。

只读检查确认 main 仍要求 GitHub App 的 `windows-and-web`、`android`、`sql-integration`、`crm-saas-public-contract`、`crm-v1-prd` 五个成功检查。暂停自动运行不会让这些要求消失。

当前远端 main 还包含旧 `pull_request_target`，创建 PR 会读取那里的配置并启动运行；候选分支改为手动不足以阻止它。因此本次配置先保存在独立任务分支，不能在没有新授权的情况下创建 PR、重跑 CI、强行合并或宣称 main 配置已生效。后续 push 前必须再次核对实际事件、目标分支和既有 PR；受保护 Tag 或关联 PR 的推送不能沿用“新普通分支不会触发”的结论。

## 检查与证据边界

- 检查本任务涉及的 Core、CRM、Platform 仓库运行状态时，`queued`、`in_progress`、`waiting`、`pending`、`requested` 均为空；实际取消数为 0。没有触碰发布、部署或数据迁移运行。
- 此配置改动只进行本地 YAML/触发语义、可信来源、保留工作流与 diff 检查，不编译、不执行全量测试、不构建镜像、不调用远程 runner。GitHub 实际执行及保护分支集成保持未验证。
- 原 C02 本地结果单独复用：123 项定向单元测试、23 项真实 SQL/HTTPS 夹具场景全部通过，零失败/跳过；其本地 Docker 固定签名包恢复及发布构建在本策略请求之前已完成。这些不是新策略改动的验证，也不代表 CRM 投影、Dapr/Kafka 或 30 秒传播验收已经完成。
