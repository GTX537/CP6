# 本地验证与 GitHub Actions 触发策略

2026-09-10 用户明确要求暂停普通编译、测试的自动 GitHub Actions。后续是否恢复额度不改变授权边界：远程验证必须先说明原因、范围与预计分钟数，并取得新的明确授权。根目录 [AGENTS.md](../../AGENTS.md) 保存长期执行规则。

同日用户进一步明确授权：从门禁移除必需的 Actions 检查，并在本地发布检查。已通过 GitHub 的专用接口仅移除 Core `main` 的五项 required status checks，其他分支保护保持不变；[变更前后记录](evidence/local-gates-20260910/)保留原始设置及普通工作流暂停状态。这里没有 GitHub Actions 成功结果，本地验证通过后使用正常 PR 合并。

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

R2 的受保护 Tag 候选、release freeze、受保护环境部署，以及原有手动 P10 validation/publication/audit 均保持原文件与触发条件。这些属于正式候选、发布或部署链，不因包含编译而整体停用。新的用户授权只调整下述五项 Actions 门禁及本仓库的相关自动入口，不调整其他分支保护、生产审批或全局配置。

## 依赖与本地交付方式

`client-contract` 同时生成 GitHub `cp6-dev-runtime-<sha>`。Azure CI 是该同源成功 Artifact 的消费者，不自行编译、也不会代替被停用的 GitHub 运行。新的 main 提交不再自动获得此 Artifact，因此 `azure-pipelines.yml` 同步改为 `trigger: none`，避免自动等待不存在的上游产物；其手动入口、受认证下载、摘要校验和原有 DEV 推广合同保持不变。没有实际成功 Artifact 时，Azure bridge 与后续 DEV 推广不能宣称就绪。本地发布证据不能冒充这一 Artifact。

按用户后续授权，已移除 GitHub App 的 `windows-and-web`、`android`、`sql-integration`、`crm-saas-public-contract`、`crm-v1-prd` 五个必需检查。PR 要求、过期审查撤销、会话解决、管理员约束、禁止强推和禁止删除等设置逐字段核对保持不变。测试文件和既有失败记录仍保留；本地必须验证受影响范围并诚实记录未覆盖项，未变化的成功结果直接复用。

迁移时先在 GitHub 暂停表中的七个普通工作流，再正常合并仅手动配置，并回读远端 main 的实际触发条件，最后恢复七个工作流的手动入口。这样旧 `pull_request_target` 不会因创建配置 PR 消耗额度。每次 push/PR 前仍须核对实际事件和工作流状态；不得从该一次性迁移推断受保护 Tag、正式候选或后续其他分支操作已获运行授权。

本地发布检查使用隔离的发布目录、端口和数据库，记录源码 SHA、发布文件哈希、程序实际启动及 HTTP 行为。本地结果只用于开发交付，不替代 R2 签名候选、生产扫描、受保护环境审批或实际部署身份验证。当前任务不会覆盖既有 `cp6`/`cp6-dev` 数据或发起生产部署。

## 检查与证据边界

- 检查本任务涉及的 Core、CRM、Platform 仓库运行状态时，`queued`、`in_progress`、`waiting`、`pending`、`requested` 均为空；实际取消数为 0。没有触碰发布、部署或数据迁移运行。
- 此配置改动只进行本地 YAML/触发语义、可信来源、保留工作流与 diff 检查，不编译、不执行全量测试、不构建镜像、不调用远程 runner。GitHub 实际执行及保护分支集成保持未验证。
- 原 C02 本地结果单独复用：123 项定向单元测试、23 项真实 SQL/HTTPS 夹具场景全部通过，零失败/跳过；其本地 Docker 固定签名包恢复及发布构建在本策略请求之前已完成。这些不是新策略改动的验证，也不代表 CRM 投影、Dapr/Kafka 或 30 秒传播验收已经完成。
