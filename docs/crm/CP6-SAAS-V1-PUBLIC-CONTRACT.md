# CP6 SaaS V1 公开工程契约

<!-- public-contract-status: Candidate -->

- 决策 ID：`CP6-SAAS-V1-PUBLIC-CONTRACT`
- 状态：**Candidate**
- `decisionPayloadSha256`：`8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`
- 私有源仓库：`GTX537/CP6.CRM`
- 私有源合并提交：`07a7bb0b50f33b0cb70c18c14f83be77c725626d`
- 私有产品决策：`CP6-SAAS-V1` / `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b` / `Frozen`
- 私有发布决策：`CP6-SAAS-R00` / `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03` / `Accepted`
- 日期：2026-08-14
- 发布镜像：[ADR-CRM-R00](../devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md)
- M0 门禁：[CRM M0 Readiness](./CRM-M0-READINESS.md)
- 状态记录：[cp6-saas-v1-public-contract.json](./approvals/cp6-saas-v1-public-contract.json)

本文件只公开跨仓实现、测试和发布所需的脱敏工程合同。价格、支付供应商、商业 cohort、个人审批身份和内部风险接受保留在私有产品仓库；不得从公开镜像推断或补写。私有受保护决策是产品与 R00 的权威源，本文件是供公开 `GTX537/CP6` 消费的内容寻址镜像。

<!-- public-contract-payload:start -->
## 1. 产品与系统边界

CP6 是面向包装及相邻离散制造企业的模块化 B2B SaaS 平台，CRM 是第一个商业化模块。CRM 支持从公开/人工获客、Lead 协作、Account/Contact、Opportunity 到可审计成交结果；CP6 ERP 继续拥有法定客户、报价、订单、财务和交易主数据。

未来产品作为独立模块接入统一 Portal，不把其他产品的领域代码放入 CRM。V1 使用区域隔离控制面、每组织独立 CRM 数据库、版本化 Entitlement 和四仓协作。

## 2. 四仓职责

| 仓库 | 权威职责 | 禁止边界 |
| --- | --- | --- |
| `GTX537/CP6` | 区域身份发行、PersonIdentity、Organization、Membership、权限/撤销事件；CP6 ERP 的 BP、Quotation、Order 权威 | 不拥有 CRM 漏斗、Commerce 或 Billing 权威 |
| `GTX537/CP6.Platform` | RequestContext、RS256/JWKS、Problem Details、YARP、Dapr/Kafka、CloudEvents/JSON Schema、Outbox/Inbox、System Release Manifest 和跨仓测试夹具 | 不定义业务实体或商业政策 |
| `GTX537/CP6.CRM` | CRM 领域/API、每组织 SQL Server 数据库、Migrator、Next.js 管理台/租户站点和 React Native 移动端 | 不发行身份、不直接记账、不创建法定 ERP 单据 |
| `GTX537/CP6.Portal` | 区域公开模块目录、注册、App Launcher、组织/成员、Commerce、Billing、Subscription 和 Entitlement 权威 | 不写 CRM/ERP 数据库，不建立跨区域 PII 目录 |

服务只通过版本化 API 和 CloudEvents 协作，不跨服务直写数据库。浏览器和移动端只使用 Gateway/BFF；同步跨服务调用只读，业务写入通过权威命令或 `IntegrationProcess + Outbox/Inbox`。

## 3. 区域、身份与租户

- V1 支持北美和中国大陆两个独立数据区域；组织、身份、账单、CRM、媒体、日志和备份留在选择区域。
- 注册显式选择区域；区域域名独立，不建立全球邮箱目录，不按 IP 自动迁移组织。
- 同一自然人在每个区域有独立 `PersonIdentity`；区域内通过 `OrganizationMembership` 加入多个组织。
- 每组织独立 CRM 数据库和恢复边界，可共享区域资源池但不得共享业务表、凭据或连接上下文。
- 外部 Tenant/User/Organization header 在 Gateway 清除并由可信组件重建；没有默认租户，缺失上下文失败关闭。
- Gateway、Portal、CRM 与 CP6 后端分别验证 RS256/OIDC/JWKS、issuer、audience、expiry 和 `kid`。权限、DataScope 与 Entitlement 不进入 JWT，使用区域本地投影和撤销/reconciliation。

## 4. CRM V1 领域合同

- 获客渠道为租户公开站点、人工录入和受控 CSV import；Import 与 Website 指标分开。
- `PublicSubmission` 是独立 Intake 资源。Quarantined 在 release 前不是 Lead；合法边为 ConvertedToLead、Rejected、Expired，随后按 PII 生命周期 Anonymized。
- Lead：`New → Assigned → Contacted → Qualified → Converted`；活动状态可终止为 Disqualified/Merged，终态不可恢复。
- Qualified Lead 原子创建或关联 Account、Contact、Opportunity。
- Opportunity：`Qualification → NeedsAnalysis → Proposal → Negotiation → Accepted → Won`；活动阶段可进入 Lost。
- Proposal 前冻结 `OrderAuthorityMode`。`Cp6Erp` 只有权威 ERP Order 成功结果可 Won；`ExternalEvidence` 只有不可变 `ExternalSaleRecord` 可 Won。
- Offering 是公开营销目录，不是库存、交易价或在线结账商品。
- 现有 20 表 Foundation 是兼容语义和迁移源，不是目标微服务数据库架构。

## 5. API、事件与数据不变量

- 管理 API 使用 `/api/crm/v1`，公开同源 BFF 使用 `/api/public/v1`；不提供冒号命令别名。
- 创建要求 `Idempotency-Key`；并发更新要求 `If-Match`，需要时两者同时使用。相同 key/载荷重放原结果，不同载荷返回 409。
- 缺少前置条件 428、版本冲突 412、语义验证 422；错误统一 RFC 9457 和稳定业务码。
- Merge 同时验证 source/target RowVersion；任一过期整体 412、零写入。412 UI 保留草稿、拉取快照、展示差异并要求显式重试。
- CloudEvents 1.0 是唯一 envelope；扩展字段固定 tenant、correlation、causation、aggregate、version、schema、region，data 由生产者/消费者双向 JSON Schema 验证。
- 业务写入与 Outbox 同事务；Inbox 唯一键 `(ConsumerName, MessageId)` 并验证 payload hash；版本守卫阻止乱序倒退。
- Activity、Collaborator、StageHistory 等弱引用迁为显式同组织复合外键。

## 6. Intake、安全、PII 与体验

- 浏览器只提交到同源 Next.js BFF；BFF 以服务 Token、Dapr mTLS/AppId 和受控网络身份调用 CRM。浏览器到 CRM API 的直接写路由关闭。
- attempt 绑定 site/form version、规范化 payload hash 和 BFF 签名 HttpOnly cookie；CRM 不得凭 attemptId 单独返回 receipt token。
- receipt token 加密保存；同载荷重放原 receipt。到期只返回中性 tombstone，不重发 token、不创建新提交。
- 回执只放入有界、加密、Secure/HttpOnly/SameSite 的 `__Host-` Cookie，并按最终 Set-Cookie 总字节预算逐出；不进入 URL、HTML、JS、日志或分析。
- PII 必须服务端遮罩，不进入日志、Trace、事件、缓存键、错误、公开证据、分析或不受控 HTML。
- CRM 管理台一级 IA：Dashboard、Leads、Accounts/Contacts、Opportunities、Site/CMS；Portal 独立承载组织/成员/Billing/App Launcher。
- Next.js 管理台/站点和 React Native 移动端位于 `CP6.CRM`；移动写操作在线执行，只允许短期加密只读缓存，Push payload 不含 PII。
- 所有页面覆盖 Loading、Empty、Forbidden、Not Found、Conflict、Integration Pending/Failed 和 Partial Report，并满足 WCAG 2.2 AA。

## 7. ERP、Commerce 与迁移

- CP6 ERP 同步调用只读；BP/Order 写入全部使用 IntegrationProcess + Outbox/Inbox。UAT/候选使用真实 C03 handler 与隔离 ERP SQL，Mock 只用于单元测试。
- Portal 是 Subscription/Billing/Entitlement 权威；CRM 只消费版本化 Entitlement Snapshot。降级阻止新超限写入，不删除现有数据。
- 20 表迁移执行 `preflight → migrate → verify → cutover-check`，使用脱敏生产恢复副本验证计数、哈希、时区、租户和引用不变量。
- 第一条新系统业务写入前可恢复旧入口；之后禁止旧库覆盖或双写，只允许前向修复。Schema 和业务数据不回退。

## 8. SLO、测试与发布

- 每区域核心技术可用性目标 99.9%；API p95 `<300ms`、公开 Intake p95 `<500ms` 是门禁负载目标。
- AZ 已提交事务 RPO 0、恢复目标 `≤15min`；逻辑损坏 PITR 恢复点目标 `≤10min`、季度实测恢复门禁 `≤4h`。
- 测试矩阵覆盖 Domain/Application、真实 SQL Server、API/OpenAPI、JSON Schema、Dapr/Kafka、真实隔离 ERP、Next.js、React Native、Playwright、Security、Performance、Resilience、Migration 和 Deployment。
- GitHub R2/GHCR 是 CRM V1 唯一候选权威。Azure 可以执行 CI、DEV 学习、影子验证或消费同一 digest，不为同版本重建候选。
- Build once, deploy many；System Release Manifest 固定四仓 SHA、包/镜像 digest、契约/schema、Dapr、migration、SBOM/签名和不可变证据身份。
- 候选对象使用 content-addressed key 或 first-writer-wins，记录 `bucket + key + VersionId + SHA-256`；签名 `CandidateLocator` 固定 candidate-result 精确对象版本。
- 切换后 Adoption 证据是 append-only 发布记录，引用候选 Manifest digest，不阻止候选生成、不改写 Manifest。

## 9. 治理与开工门禁

- 私有产品决策 `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b` 已 Frozen；私有 R00 决策 `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03` 已 Accepted。
- 批准模型为 `SingleProgramOwner`，唯一人类批准角色是 `ProgramOwner`。Architecture、Security、Privacy/Legal、Finance/Commerce、ERP、Data、SRE、Release、Sales Operations、Design、QA 和工程角色提供评审或证据，但不形成平行人类签字。
- 单一 ProgramOwner 不能豁免自动化、Critical/High 清零、租户/PII、真实环境、迁移、性能、采用、发布完整性、分支保护或必需检查。
- M0 只批准责任、拓扑/连续性合同、Pilot/cohort/evidence 合同；不要求提前创建真实云资源或 Pilot 运行证据。
- `CRM01` 只有在本公开同步达到 Complete、M0 达到 Go、分支保护与必需检查强制执行后才解锁；仓库已存在不等于业务实现获准开始。

## 10. 生命周期与漂移处理

1. 本公开载荷由 ProgramOwner 批准后才可从 Candidate 变为 Complete；批准必须绑定规范化 `decisionPayloadSha256` 和不可变历史证据。
2. 私有产品或 R00 摘要变化时，本公开同步立即 Expired；公开载荷必须重新生成、重审和批准。
3. 公开镜像不得反向修改私有产品决策，也不得把公开仓库中的历史规划文档提升为新权威。
4. 每张实施票只改一个仓库、1–3 天范围，具备 DRI/reviewer、前置、输入输出、验证命令、失败处理、测试和文档。
5. 候选前完成真实 SQL/Dapr/Kafka/C03、迁移、SLO、安全和 Pilot；生产切换后再执行 Adoption、移动 GA 和 Epic closure。
<!-- public-contract-payload:end -->

## 状态镜像（不进入公开工程载荷）

- 当前状态：Candidate
- 当前 `decisionPayloadSha256`：`8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`
- 私有产品源：Frozen
- 私有 R00 源：Accepted
- 公开同步：Pending
- M0：No-Go
