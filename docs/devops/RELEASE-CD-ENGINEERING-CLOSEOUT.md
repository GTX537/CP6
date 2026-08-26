# CP6 Release/CD 工程结案

状态：`Complete`（仓库与平台工程范围）

结案日期：2026-08-26

生产发行状态：`No-Go`（首个 R2 候选尚未冻结）

## 1. 结案口径

本结案关闭的是 Release/CD 的工程建设任务：CI 责任边界、唯一发布权威、候选与部署工作流、生产模板、DEV 自动链、Azure 非权威 Shadow S0，以及相应失败关闭合同已经交付并取得可复现证据。

本结案不把尚未发生的生产发行伪装成完成。`v1.0.0` 的真实候选、GitHub 受保护 Environments、签名与基础设施 Secret、UAT/PROD 部署、R2A/R2B 现场试点和灾备演练继续由 `docs/client/r2` 的发行执行门禁管理。它们只有在真实 Owner、环境和证据到位后才启动，不再作为一个长期开放的“继续建设 Release/CD”工程任务。

## 2. 已关闭的工程范围

| 范围 | 结论 | 权威实现或证据 |
| --- | --- | --- |
| PR 验证责任 | GitHub 是唯一 PR 验证入口；Azure 保持 `pr: none` | `client-contract.yml`、`wms-production-sql.yml` 与 `main` required checks |
| CI 构建 | GitHub-hosted 完成 .NET、Web、Android、OpenAPI/SDK 与 R2 source gate | `client-contract.yml` |
| Azure CI | Self-hosted 只桥接同 SHA、双层哈希验证的 Runtime Artifact | `azure-pipelines.yml` 与已成功的 main Runs |
| 发布权威 | GitHub R2 是唯一候选/部署权威；GHCR 是唯一 Registry | `ADR-DEVOPS-001` |
| 候选与供应链 | 受保护 Tag、Schema 2 manifest、镜像 digest、SBOM、漏洞扫描、签名和不可变证据 | `r2-freeze.yml`、`r2-candidate.yml` |
| 生产部署 | `db-init` 先行、digest 部署、运行身份/迁移核对、Compose/Kubernetes 受控输入 | `r2-deploy.yml`、`deploy/production` |
| DEV 自动链 | 手动 3/3、低内存失败关闭、Stage 重试、自动发布和零根环境漂移 | Azure #95/#120/#121/#129/#131/#133 |
| Azure Shadow S0 | 手动、无 Secret、无 Build/Push/Tag/Deploy 权限的离线候选合同 | PR #32、`main@9009abe6`、Azure Definition #5 / Run #145 |

## 3. CI 门禁责任矩阵

| 门禁 | PR/main | R2 候选 | 说明 |
| --- | --- | --- | --- |
| .NET API/主测试 | GitHub `windows-and-web` | R2 `source-gate` | Azure 不重复编译 |
| Web 类型、单测、生产构建 | GitHub `windows-and-web` | R2 `source-gate` | 候选另跑 WMS Playwright E2E |
| 原生客户端/OpenAPI/SDK | GitHub `windows-and-web` + `android` | R2 `source-gate` + 签名阶段 | Android 正式签名仍需发行输入 |
| WMS SQL Server | GitHub `sql-integration` | R2 `sql-integration` | 使用真实 SQL Server 容器，不以 InMemory 代替 |
| Space | Space GA 独立门禁 | 不属于 WMS R2 候选放行面 | Space GA 仍按自己的真库、Provider 与 Pilot 证据失败关闭 |
| SBOM/漏洞/镜像身份 | Source dependency audit | R2 image/supply-chain 阶段 | Azure Shadow 不生成第二份权威制品 |
| 部署/恢复/Pilot | 不在 PR 执行 | 受保护部署与现场证据 | 只消费同一 manifest/digest |

## 4. Self-hosted Agent 运维边界

- `CP6-Windows` / `Default` 只运行轻量 Artifact 桥与无 Secret Shadow S0，最大并发保持 1；不得回退为与本机 SQL/Docker 争抢内存的完整编译 Agent。
- CI Agent 使用 `Start-Cp6CiAgent.ps1` 前台隔离启动，并清空继承的 PowerShell 7 `PSModulePath`；离线时任务允许排队，不得自动改用 `CP6-Deploy`。
- `CP6-Deploy` 继续由独立服务身份运行，只授权 Readiness 与 DEV CD；通用 CI/Shadow 不读取 `cp6-dev-secrets`，也不获得 PROD Secret 或环境管理权限。
- Agent 升级必须在队列清空后执行，先跑 ValidateOnly/Readiness，再跑一个无副作用合同任务；失败即停用新版本并恢复已验证 Agent。
- 工作区使用 clean checkout，Pipeline 不清理数据库备份、Docker 卷或用户目录。Agent 缓存、诊断日志和 Artifact 保留策略必须作为独立维护任务，先验证绝对路径、最小保留期和磁盘阈值。
- Agent 离线、磁盘不足或宿主低内存必须失败关闭并留下 Run 证据；不允许临时放宽 Pool、跨身份执行或绕过 required checks。

## 5. 结案时的外部事实

- GitHub `main` required checks 固定为 `windows-and-web`、`android`、`sql-integration`、`crm-saas-public-contract`、`crm-v1-prd`，管理员同样受保护规则约束。
- GitHub 当前没有 R2 Release、受保护 `vX.Y.Z` Tag 或 R2 workflow Run；仓库级 GitHub Environments 和 Actions Secrets 均为 0。
- `docs/client/r2/releases/v1.0.0/candidate.yaml` 为 `Draft`，20 项发行输入均为 `Pending`；Freeze gate 按预期拒绝它。
- Azure 已存在 `cp6-dev`、`cp6-uat`、`cp6-prod-lab`，以及 CI、DEV、Deploy Readiness 和 Release Shadow Pipelines；这不等同于 GitHub R2 生产环境已配置。
- Azure Release Shadow S0 Run #145 绑定 `main@9009abe687c693fdcbd650261f39b56cf8ccf8fb`，最终结果：`Succeeded`；Artifact 为 `cp6-release-shadow-s0-145`。

## 6. 后续只按事件重新打开

以下事件出现时建立新的单任务卡，不重新打开一个笼统的 Release/CD 建设项目：

1. `v1.0.0` 输入由真实 Owner 批准并进入 Freeze；
2. 第一个权威 R2 candidate result/manifest 可供 Azure S1 只读验证；
3. UAT/PROD 获得真实外部服务、受保护环境和独立批准人；
4. ACR、AKS、Registry 或发布权威发生变化；
5. 需要执行真实回退、灾备或多仓推广演练。

任何事件都继续遵守 Build once、digest 部署、前向迁移、环境侧审批和不可变证据规则。
