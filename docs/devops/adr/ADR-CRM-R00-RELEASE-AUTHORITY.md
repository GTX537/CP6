# ADR-CRM-R00：CRM V1 发布权威与 Registry

- 决策 ID：`DEC-CRM-001`
- 状态：**Proposed — T1 已锁定决策内容，named approval 尚未完成**
- 日期：2026-08-13
- 适用范围：CRM V1 三仓系统候选、DEV/UAT/PROD 推广与回退
- 规范来源：[CRM V1 可执行工程规格](../../crm/CRM-V1-EXECUTABLE-SPEC.md)
- 就绪状态：[CRM M0 就绪清单](../../crm/CRM-M0-READINESS.md)

## 1. 决策

CRM V1 的唯一 Registry 是 **GHCR**，唯一候选与发布权威是 **GitHub R2**。这是 T1 已批准规范中的锁定项，不是实现者需要重新选择的方案。

- 同一 `systemVersion` 只允许 GitHub R2 签发一份权威 System Release Manifest。
- DEV、UAT、PROD 推广同一份 Manifest 中的 `repository@sha256:digest`，不得按环境重新 Build。
- Azure 可执行 CI、DEV 学习链、非权威影子验证，或消费 GitHub R2 已签发的 digest；不得为同一版本重建候选、使用相同候选身份、签发第二份权威清单或覆盖 R2 证据。
- ACR 迁移不属于 CRM V1。若未来立项，必须由新的 ADR 固定复制而非重建、影子期、等价矩阵、切换/退出门禁和恢复时限；该 ADR Accepted 前，R2 始终保持权威。
- CRM V1 不存在“从 Azure 回退到 R2”的权威切换，因为 Azure 从未取得权威。Azure 非权威链故障时，停止该链并继续使用 R2；应用候选回退仍遵守本 ADR 第 7 节。

## 2. 范围与非目标

本 ADR 固定 Registry、候选身份、系统清单签发者、Azure 允许边界、兼容证据和回退原则。它不执行以下动作：

- 不创建 `GTX537/CP6.CRM` 或 `GTX537/CP6.Platform` 仓库；
- 不创建 GHCR/ACR、Azure SQL、Storage、Key Vault、Dapr、Kafka 或环境资源；
- 不新增流水线、Secret、Service Connection、镜像、Tag、候选或部署；
- 不关闭 M0，也不替代 DEC-CRM-002–007、Owner、cohort 和 Observation 的批准。

## 3. 当前权威链与目标系统链

### 3.1 当前可验证的 CP6 R2 链

当前 CP6 仓库的 R2 链由以下受控入口组成：

1. `.github/workflows/client-contract.yml` 与 `wms-production-sql.yml` 执行代码/客户端/SQL 门禁；
2. `.github/workflows/r2-freeze.yml` 校验 `main` 并创建受保护 `vX.Y.Z` Tag 与不可变源码证据；
3. `.github/workflows/r2-candidate.yml` 构建 GHCR 镜像，生成 provenance/SBOM，执行漏洞扫描并写入 Schema 2 `release-manifest.json`；
4. `.github/workflows/r2-deploy.yml` 验证不可变证据，先执行 `db-init`，再按 digest 部署并核对运行身份。

当前 Schema 2 `release-manifest.json` 是 **CP6/WMS 组件候选清单**。它不能被改名或宣称为 CRM 的三仓 System Release Manifest。

### 3.2 CRM V1 目标链

三个仓库分别在自身 CI 中产生不可变的组件候选和证据：

- `GTX537/CP6.Platform`：包、Gateway/部署组件、合同 bundle、System Manifest schema；
- `GTX537/CP6.CRM`：CRM API/Web/Migrator、独立数据库 migration、OpenAPI/Event Schema；
- `GTX537/CP6`：身份/ERP API、handler、migration 与对应镜像。

`GTX537/CP6.Platform` 的受保护 R2 协调工作流是 **唯一 System Release Manifest 签发者**。它只聚合已经冻结、可验证的三仓组件候选，不检出源码重新 Build。Platform 仓和该协调工作流尚未创建前，任何流水线都不得签发 CRM 系统候选。

候选关系固定为：

```text
受保护三仓 SHA
        -> 各仓生成 GHCR image/package digest 与不可变组件证据
        -> Platform R2 聚合与兼容门禁
        -> 唯一 System Release Manifest + digest/signature
        -> DEV -> UAT -> PROD 推广同一 digest
```

## 4. 版本冻结与候选身份

- 系统版本入口沿用受保护 `vX.Y.Z` 语义；Tag 创建前必须验证对应三仓 SHA 已通过 repo 级必需门禁。
- 同一 `systemVersion`、三仓 SHA 或组件 digest 的变化都必须生成新候选；禁止覆盖、复用或重签旧候选身份。
- SemVer 和完整 Git SHA 只用于追踪；部署身份以 Manifest digest 和 `repository@sha256:digest` 为准。
- 任何 Azure 影子制品必须带非候选命名空间/Run ID，不能使用受保护候选 Tag，不能被 DEV/UAT/PROD 推广，也不能写入权威证据前缀。
- 候选及证据继续使用 S3-compatible、服务端加密、版本控制和 Object Lock 存储。引用必须包含不可变 URI、内容 digest 和保留策略。

## 5. Azure 允许与禁止边界

| 场景 | CRM V1 是否允许 | 约束 |
| --- | --- | --- |
| 源码 CI、单元/合同测试 | 允许 | 结果是验证证据，不是候选 |
| DEV 学习链从源码构建本机镜像 | 允许 | 明确标记非候选，不得推广到 UAT/PROD |
| 影子重跑测试、扫描、Manifest 校验 | 允许 | 只比较 R2 输入/输出，不签发权威清单 |
| 从 GHCR 按 digest 拉取并部署受控环境 | 允许 | digest 必须来自权威 System Manifest；环境审批、身份和证据仍须通过，不得重建或重签 |
| 为同一版本重新 Build 并声称等价候选 | 禁止 | 即使字节相同也不是合法候选 |
| Push CRM V1 权威候选到 ACR | 禁止 | ACR 迁移另立项、另审批 |
| 签发、修改或覆盖 System Release Manifest | 禁止 | 只有 Platform R2 协调工作流可签发 |
| Azure YAML 作者自行取消 PROD 审批 | 禁止 | 审批属于受保护资源/环境 Owner |

## 6. R2 能力与 CRM 目标等价矩阵

`现有` 仅表示本仓 R2 有可核对实现；`Gap` 表示 CRM 系统候选关闭前仍须实现并验证。

| 门禁/能力 | 当前 CP6 R2 | CRM V1 目标与处置 |
| --- | --- | --- |
| PR/main 源码、客户端、SQL 门禁 | 现有 | 三仓各自失败关闭；结果由 System Manifest 引用 |
| 受保护版本冻结与源码快照 | 现有 | 扩展为三仓 SHA/组件候选冻结 |
| GHCR digest、provenance、SBOM | 现有 | 三仓所有可部署镜像均须覆盖 |
| High/Critical 漏洞扫描 | 现有 | 保持零未处理 Critical/High；限时例外遵守规范 |
| 原生客户端制品签名 | 现有 | 保留为 CP6 组件证据 |
| OCI 镜像密码学签名与验证 | **Gap** | P10/CRM12 实现；不得用 provenance 或原生客户端签名冒充 |
| CP6 Schema 2 组件清单 | 现有 | 作为 CP6 组件输入，不是系统清单 |
| 三仓 System Release Manifest | **Gap** | P10 实现唯一 schema、聚合、签名、Object Lock |
| OpenAPI/Event/Dapr/JSON Schema 兼容范围 | **Gap** | 使用机器可判定 major/digest/component version |
| 实际 migration ID 与 DB 兼容范围 | **Gap** | 覆盖所有适用数据库、当前 Schema 和升级后真实形态数据 |
| `previousSystemManifestDigest` 与整套回退证据 | **Gap** | P10/CRM12 生成并验证，数据和 Schema 不回退 |
| 不可变证据 URI/Object Lock | 现有 | System Manifest 只引用 content-free、无 PII/Secret 证据 |
| digest 部署、db-init、运行身份核对 | CP6 现有 | 扩展为三仓/CRM DB，并在 DEV/UAT/PROD 验证相同 digest |
| 真实 Dapr/Kafka/ERP 与 Adoption 证据 | **Gap** | CRM12 与采用门禁签收；Mock 不得作为发布证据 |

任何 Gap 未关闭时，只能形成开发/测试制品，不能形成 CRM 系统候选。

## 7. System Release Manifest 与回退权威

权威 Manifest 至少固定：

- `systemVersion`、`previousSystemManifestDigest`、签发工作流/Run ID、签发时间和 Manifest digest/signature；
- CP6、Platform、CRM 的完整 Git SHA、组件候选 digest、所有 image/package digest；
- 实际数据库 migration IDs、OpenAPI/Event majors 与 schema digests、Dapr component/config versions；
- 当前 Schema 和真实升级后形态数据上的旧/新二进制读、写、事件兼容证据；
- SBOM、漏洞、签名、测试、迁移、SLO、安全、ERP、采用和批准证据的不可变 URI/digest；
- 允许环境、推广记录、保留策略和例外到期日。

默认回退单位是 `previousSystemManifestDigest` 指向的**整套系统组件组合**。组件级回退只有在当前 Manifest 明确列出机器可判定兼容范围，并包含受信签名的当前 Schema/升级后数据读写和事件证据时才允许。

- 数据库 migration 与业务数据只前向，不执行 Down，不恢复旧数据。
- CRM 第一次生产切换的 write fence 前可以恢复旧入口；第一条新系统业务写入后，旧入口不再是合法回退目标，只能前向修复。
- 回退完成必须重新核对 release identity、readiness、管理读写、公开回执、两租户隔离、事件积压和 ERP 幂等。

## 8. 审批与职责

批准记录保存于受控项目系统；Git 只保存不可变记录引用和结果，不在此虚构实名。批准对象用合入后的 commit SHA、文件路径和 Git blob SHA 唯一标识；批准必须同时包含账号身份、角色、决定、UTC 时间、该内容标识和不可变 evidence URI。

| 责任 | 必需角色 | 当前状态 | 受控记录 |
| --- | --- | --- | --- |
| Accountable / 最终决策 | Release Owner | Pending | Required before ADR Accepted |
| 架构评审 | System Architect | Pending | Required before ADR Accepted |
| 供应链/身份/Secret 边界 | Security Owner | Pending | Required before ADR Accepted |
| SLO、证据、恢复和运维 | SRE Owner | Pending | Required before ADR Accepted |
| 系统清单实现承诺 | Platform Owner | Pending | Acknowledgement required before P01/P10 |
| 候选消费与 ERP 边界 | CRM Engineering Owner、ERP Owner | Pending | Acknowledgement required before CRM01/C03 |

任一记录缺失、过期、ADR 内容标识不匹配或 approver 无对应角色时，状态保持 Proposed。

## 9. Accepted 与实施解锁条件

本 ADR 只有同时满足以下条件才可改为 Accepted：

1. Release Owner、System Architect、Security Owner、SRE Owner 对同一 ADR 内容标识完成批准；
2. 第 6 节 Gap 已建立有 DRI、reviewer、前置、验证命令和失败处理的独立任务；
3. [CRM M0 就绪清单](../../crm/CRM-M0-READINESS.md) 中 DEC-CRM-001 的批准引用已填入且可读取；
4. 没有创建 ACR、第二候选清单或 Azure 权威路径。

ADR Accepted 只关闭 `DEC-CRM-001`，不自动关闭 M0。M0 仍要求 DEC-CRM-002–007、named Owner、Observation、Pilot cohort、Azure SQL/Emergency Intake 和其他开工输入全部 Approved。
