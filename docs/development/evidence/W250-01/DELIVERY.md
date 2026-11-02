# W250：59张局部交付卡真实剩余核对

本轮已逐项对照原卡scope、正反验收、当前采用记录与源码职责。59张中，37张有明确实现余项，19张需要把合同适用/源码覆盖或已有执行证据进一步核准，3张优先整卡核销（WP02-10、WP09-11、WP12-07）。后两类不等于缺代码，也不等于已通过全卡。没有把历史报告中的“后续任务”重新算作本卡缺口。

W250是本轮审计窗口，不是第376张细项。原375张保持24 DONE、2 IN_PROGRESS、349 PLANNED；59张局部交付和24张整卡交付不重叠，覆盖83张不代表完成率或工时比例。125完整目标、247正式运行验收及41条历史静态记录没有提升。七天一期/W249已经完成，本轮不重开、不重跑。

## 来源与适用

完整接任检查点已读取：`C:/Users/tt/.gstack/projects/GTX537-CP6/checkpoints/20261102-001836Z-cp6-overall-progress-new-chat-resume.md`。原清单来自同机`CP6-planning-reports/20261017-foundation/20261102-001406Z-cp6-overall-task-inventory.csv`；权威状态仍为[roadmap.json](../../roadmap.json)，原卡定义为[task-definitions.json](../../task-definitions.json)。以下原scope和正反验收直接引用原卡，不改合同。各报告链接保留真实历史范围；旧“等真实企业数据”、旧七天ACTIVE与后续未交付叙述按当前采用修正，不回写历史报告。

已fetch并核对Main `91a0eea66c25109141c410831d0030782480923c`、CRM `c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f`、Platform `a89e90eb4f078f37a6f8610882079073185fbcf8`。CRM主目录本地main落后，读取使用已核对origin/main，未把旧工作区代码当最新。Handoff没有main，采用正式入口`codex/t02c-entry-handoff-20260921`，本轮开始为`22875f623e0f87aa854d9ef843398f2f51f1c2b6`。AGENTS及固定席位政策已核对；COORD单人执行，未启动子代理。

允许模拟政策/日历/身份/客户/订单/库存等资料及外部服务，按相同合同在真实产品路径执行生产业务验收。它不替代尚未实现的内部Owner，也不把本地执行变成部署。PostgreSQL唯一新增目标；租户schema不自动替代授权/TenantId。复用未变输入的行为证据，本轮只执行文档/引用/台账一致性检查，不宣称重新运行产品测试。

## 优先顺序和核销规则

1. WP02-10、WP09-11、WP12-07先整理原卡已有正反证据并核销完整范围；原卡明确排除的全部消费者完成、生产Use或版模实际报废，不是提供者新增门槛。
2. WP02-06切组织会话和WP03-03审核到期先核已有实际测试，缺哪条只执行哪条；WP03-01/02按冻结合同核公共服务适用，不另造NUM/FILE调用。
3. 代码工作优先有准确小边界的缺项：WP02-16原command号码分配、WP02-04租户原命令/CAS；跨模块较大项如PRIV导出、Transfer TRANSIT/RSV、Funds真账按原卡独立窗口推进。每个窗口从当时最新main重新确认直接前置，单人占用固定席位。

现有`manage_roadmap.py`的validate_tasks仍要求READY/IN_PROGRESS/VERIFYING/DONE的所有dependencies整卡DONE，而原卡prerequisite_bindings和当前AGENTS允许准确交付合同直接采用。本轮只指出这一台账工具限制，不绕过验证、不批量关闭前置、不增加资格证明包装，也不借此阻止已授权且合同具备的实现。优先核销窗口需一并对齐这项规则和原卡证据；375定义本轮不改。

## Main共同事务writer盘点（WP02-10原scope第二项）

`MainOwnerOperationJournalV1`要求实际Npgsql连接/事务，按Tenant+Owner+Operation读取固定输入/结果；提供原结果与审计的事务参加，不替Owner提交事务、不提供跨库伪原子性。StockMovementService.OriginalOperations及OrderPriceCorrectionOperationsV1是已执行代表，权限/归属取当前事实，原结果读取也经当前披露。现有ERP/PLM、WMS/Transfer/RMA、MES计划和OA Owner分别在自己的事务保存业务与journal，并由各自最终授权门决定提交；消费者覆盖不由本公共提供者一并宣称。

原键恢复只查询已保存结果，未知提交不得重解释为失败或无效果；后台Outbox投递与业务提交分轴。W43/44/50/98和W249实际PG/HTTP/故障/撤权/宿主投递证据复用；原旧writer逐项迁入仍在各业务卡，不能仅因引用journal类便记为采用。下表逐卡源码入口是核查定位，行为通过仍依据实际交付报告。

## 逐卡结果

类别：**明确实现余项**表示至少一项当前产品行为尚未交付；**合同/验收待核**保留明确不确定性，下一窗口先利用已有证据核准；**优先整卡核销**表示当前证据已高度覆盖提供者原scope，尚未执行正式状态变更。所有“剩余”均保留原正反合同，不能据本摘要缩小范围。

<a id="wp01-01"></a>

### WP01-01 替代验收采纳后的WP01差异与清单收口

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：接收WP05有版本替代验收与旧缺件覆盖/不能覆盖声明；修订相应台账规则和125目标的精确采用判断；不可恢复历史原件继续记缺失。
- 当前已交付：有版本替代资产、信任阶段和579项义务的ITER/REL划分已经交付。
- 真实剩余/下一步：逐125目标采用判断仍需对照旧缺件覆盖/不能覆盖；不可恢复的历史原件保持缺失。模拟业务输入不伪造历史原件或签名；不重做已接受替代方案。
- 原正验收：新验收依据被明确采纳后登记其实际来源，WP01按完整门核对。原反验收：仅改NOT_INVENTORIED标签、把后继伪装原30/48清单均拒绝。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[trust-stages](../WP01-01/trust-stages/DELIVERY.md)。
- 当前源码定位：[acceptance_basis.py](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/docs/development/tools/acceptance_basis.py)；[test_acceptance_basis.py](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/docs/development/tools/test_acceptance_basis.py)

<a id="wp02-02"></a>

### WP02-02 Main PostgreSQL租户存储布局与隔离采用

- 判定：**明确实现余项**；原状态`IN_PROGRESS`保持。
- 原scope：按S02-00选定布局实现可信租户到存储映射、EF模型缓存与连接池上下文；独占PG测试库验证运行/迁移权限和逐租户初始化版本；现有数据切换另行任务。
- 当前已交付：S02-00混合布局已定；租户连接组件、ERP八表隔离及目录普通LOGIN/安装/HTTP均有交付。
- 真实剩余/下一步：连接组件报告明确未全宿主接线：落实当前Main运行模型缓存、可信映射/池上下文及逐租户初始化版本和普通写入权限；历史数据切换另立任务，不全库自动转schema。
- 原正验收：租户A/B连接复用、取消和回滚后读写均落准确范围，重建同租户初始化返回原结果。原反验收：显式跨schema越权、缺scope、映射碰撞、旧连接上下文均拒绝。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[directory-installation](../WP02-02/directory-installation/DELIVERY.md)；[identity-priority-tenant-isolation](../WP02-02/identity-priority-tenant-isolation/DELIVERY.md)；[provisioning](../WP02-02/provisioning/DELIVERY.md)；[service-revocation](../WP02-02/service-revocation/DELIVERY.md)；[service-issuance](../WP02-02/service-issuance/DELIVERY.md)；[tenant-eligibility-control](../WP02-02/tenant-eligibility-control/DELIVERY.md)；[identity-directory-isolation](../WP02-02/identity-directory-isolation/DELIVERY.md)；[directory-authorization](../WP02-02/directory-authorization/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[TenantConnectionLease.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Persistence/Tenancy/TenantConnectionLease.cs)；[TenantConnectionManager.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Persistence/Tenancy/TenantConnectionManager.cs)

<a id="wp02-03"></a>

### WP02-03 Main当前授权与撤权最终门

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：用户/部门/角色/委派CAS与权威版本；认证完成和Owner最终写入共用撤权fence；全字段注册表先MIN既有授权再应用默认，query/write/export/download分别判定。
- 当前已交付：IAM CAS、当前权限、撤权共同门、字段和动作权限已有多轮实际Owner采用；浏览器bootstrap也已交付。
- 真实剩余/下一步：按全字段注册表与现存写者清单补query/write/export/download未纳管入口；逐入口复用当前权限，不重新实现公共fence。
- 原正验收：合法主体得到当前授权，原结果披露按当前读权判定。原反验收：未知字段、撤权与写入竞争、过期委派不能产生新效果；不可默认放宽权限。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[field-decisions](../WP02-03/field-decisions/DELIVERY.md)；[W136-01](../W136-01/DELIVERY.md)；[W137-01](../W137-01/DELIVERY.md)；[W138-01](../W138-01/DELIVERY.md)；[W139-01](../W139-01/DELIVERY.md)；[W140-01](../W140-01/DELIVERY.md)；[W141-01](../W141-01/DELIVERY.md)；[W148-01](../W148-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[CurrentPermissionContext.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Sys/CurrentPermissionContext.cs)；[ICurrentPermissionContext.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Sys/ICurrentPermissionContext.cs)；[IPermissionAggregator.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Sys/IPermissionAggregator.cs)；[PermissionAggregator.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Sys/PermissionAggregator.cs)

<a id="wp02-04"></a>

### WP02-04 租户创建、暂停恢复与管理审计

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：租户code唯一、创建模板版本和命令原结果；配置CAS；暂停/恢复单调版本与登录及业务fence一致，保留合法历史只读。
- 当前已交付：租户管理和审计已同SaveChanges提交，暂停/恢复入口已有实际验证。
- 真实剩余/下一步：TenantAdminService创建仍缺固定命令结果与模板版本，配置/生命周期命令CAS及单调版本需补；保合法历史只读和业务/登录共同fence。
- 原正验收：重复创建同命令返回同一租户；暂停后原结果只读按当前权判断。原反验收：并发同code创建只一份，旧暂停版本不能覆盖新恢复；删除绕过hold拒绝。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[current-actor-final-gate](../WP02-04/current-actor-final-gate/DELIVERY.md)；[tenant-management-audit](../WP02-04/tenant-management-audit/DELIVERY.md)。
- 当前源码定位：[ITenantAdminService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Platform/ITenantAdminService.cs)；[TenantAdminService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Platform/TenantAdminService.cs)

<a id="wp02-05"></a>

### WP02-05 Main身份权威快照及完整目录发布

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：完整租户/组织/用户/权限/issuer权威向量，精确版本与原字节；目录、水位、重建与发件原结果固定，session身份分轴。
- 当前已交付：完整身份快照、目录/基线/水位、原字节与撤权链已交付，后续BUG419/543修复已采用。
- 真实剩余/下一步：以当前全部权威写者核完tenant/org/user/permission/issuer向量及原字节；旧维护写者与issuer变更是否全部纳管尚需逐入口映射，不能从旧报告推断CRM未实现。
- 原正验收：同一冻结目录重传保持来源与水位，消费者能证明完整集合。原反验收：漏项、未知版本、不同payload重复键不可被当同快照接受。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[identity-writer-coverage](../WP02-05/identity-writer-coverage/DELIVERY.md)；[baseline-contract](../WP02-05/baseline-contract/DELIVERY.md)；[baseline-raw-http](../WP02-05/baseline-raw-http/DELIVERY.md)；[baseline-collector](../WP02-05/baseline-collector/DELIVERY.md)；[baseline-integration](../WP02-05/baseline-integration/DELIVERY.md)；[BUG-419](../BUG-419/DELIVERY.md)；[BUG-543](../BUG-543/DELIVERY.md)；[W130-01](../W130-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[IdentityPermissionSnapshot.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/CrmIdentity/IdentityPermissionSnapshot.cs)；[IdentitySnapshotWriter.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs)

<a id="wp02-06"></a>

### WP02-06 CRM身份投影、撤权fence与会话恢复

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：重复/旧/同版早返前执行precheckpoint，保留准确权威向量与原报文；按S02-00采用CRM本仓PG存储，CRM业务最终授权与撤权共用本地fence，session不能借重建复活。
- 当前已交付：原生IdentityIngress precheckpoint在重复/旧/同版早返前执行，身份与业务撤权共享本地门；W246两种竞争顺序通过。切组织端点注销持久旧会话，callback也轮换旧会话。
- 真实剩余/下一步：切组织旧会话失效属于执行证据待核，不是缺代码；补原卡正反验收与现有测试映射，仅在没有有效执行证据时运行该场景。不得重复整个身份同步链。
- 原正验收：准确目录重建完成后原结果可按当前权读取，租户切换清旧session。原反验收：旧身份事件或重复事件不能跳过撤权检查；目录不完整不可fail-open。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W130-01](../W130-01/DELIVERY.md)；[W131-01](../W131-01/DELIVERY.md)；[W246-01](../W246-01/DELIVERY.md)。
- 当前源码定位：[CrmAuthenticationService.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Authentication/CrmAuthenticationService.cs)；[CrmEndpoints.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/CrmEndpoints.cs)；[CrmIdentityIngressContextV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Identity/CrmIdentityIngressContextV1.cs)；[CrmIdentityIngressHostV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Identity/CrmIdentityIngressHostV1.cs)；[CrmIdentityIngressOptionsV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Identity/CrmIdentityIngressOptionsV1.cs)

<a id="wp02-09"></a>

### WP02-09 CRM首个Owner真实共同事务与授权门

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：将CRM入站实际Owner图/回执/审计/Inbox-Outbox接入同一本仓PG事务；提交结果未知查原key，已提交结果按当前披露权；拒绝先Inbox后业务。
- 当前已交付：Config/H01实际业务全图、回执/审计/Outbox与原结果在本仓PG事务；未知提交恢复和撤权竞争已执行。
- 真实剩余/下一步：Inquiry模型明确Ignore平台Inbox；身份入站的Inbox不能证明业务入站Inbox。确认原卡适用的真实入站Owner并接完整收件/业务/原结果同事务，不能为公开HTTP凭空造消息或声称所有CRM已具备。
- 原正验收：并发重复同命令只一份真实图；事务失败整组回滚，提交响应丢失查回原结果。原反验收：撤权与新写互斥，旧scope/伪造tenant/同键不同payload无新效果。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W130-01](../W130-01/DELIVERY.md)；[W131-01](../W131-01/DELIVERY.md)；[W246-01](../W246-01/DELIVERY.md)；[W117-01](../W117-01/DELIVERY.md)；[W118-01](../W118-01/DELIVERY.md)。
- 当前源码定位：[CrmInquiryConfigurationOwnerV2.Context.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.Context.cs)；[CrmInquiryConfigurationOwnerV2.Lifecycle.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.Lifecycle.cs)；[CrmInquiryConfigurationOwnerV2.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.cs)；[CrmInquirySubmissionOwnerV1.Graph.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.Graph.cs)；[CrmInquirySubmissionOwnerV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.cs)

<a id="wp02-10"></a>

### WP02-10 Main Owner共同事务、原结果与授权参与

- 判定：**优先整卡核销**；原状态`PLANNED`保持。
- 原scope：提供Main业务Owner使用的PG同事务、固定命令结果、最终授权fence和可测故障点；盘点现有writer接入方式与原结果只读边界，消费者逐项接入另由对应任务证明。
- 当前已交付：共同事务/不可变原命令结果、最终授权门、故障点和实际Stock/PA090代表Owner已交付；W249当前PG/HTTP/Outbox回归通过。本轮盘点writer职责见下文。
- 真实剩余/下一步：原卡明确消费者逐项接入归各域卡，不能因此长期扣留本提供者。优先完成整卡原正反验收登记；旧validator要求所有前置整卡DONE，需按现行准确交付采用规则处理，而非增加产品证明层或批量关闭前置。
- 原正验收：限定真实Main代表Owner事务业务/审计/回执一起提交并支持原键恢复。原反验收：并发同key异payload、撤权竞争和故障回滚不能遗留半组记录。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[main-original-operation](../WP02-10/main-original-operation/DELIVERY.md)；[W137-01](../W137-01/DELIVERY.md)；[W138-01](../W138-01/DELIVERY.md)；[W139-01](../W139-01/DELIVERY.md)；[W140-01](../W140-01/DELIVERY.md)；[W171-01](../W171-01/DELIVERY.md)；[W98-01](../W98-01/DELIVERY.md)；[W249-01](../W249-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[OrderPriceCorrectionOperationsV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/OrderPriceCorrectionOperationsV1.cs)；[MainOwnerOperationJournalV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/MainOwnerOperationJournalV1.cs)；[StockMovementService.OriginalOperations.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/StockMovementService.OriginalOperations.cs)

<a id="wp02-13"></a>

### WP02-13 审计写入、授权查询和保留清理

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：操作/安全/字段审计脱敏、actor/effectiveActor和UTC来源；Owner事务参与；总数/明细/导出一致授权，清理按policy/hold/unknown引用及收据。
- 当前已交付：字段历史、操作审计及嵌套来源最终事务已有交付，多类原生Owner复用。
- 真实剩余/下一步：全域actor/effectiveActor/UTC与脱敏、总数/明细/导出同授权及policy/hold/unknown清理收据仍需落到各权威出口；不能把Machine字段历史视为整个AUD。
- 原正验收：合法业务审计与效果一起提交，授权查询计数与明细同范围。原反验收：敏感值外泄、跨租户计数、hold下清理和未知来源时间冒充UTC均阻止。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W135-01](../W135-01/DELIVERY.md)；[W153-01](../W153-01/DELIVERY.md)；[W64-01](../W64-01/DELIVERY.md)。
- 当前源码定位：[OperLogCleanupService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.WebApi/BackgroundServices/OperLogCleanupService.cs)

<a id="wp02-14"></a>

### WP02-14 文件不可变版本、扫描和Owner授权绑定

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：流式大小限制、扫描/隔离、不可变content版本/digest及草稿转正式原命令；绑定Owner当前读写/download权限，ACT-FILE采用LOCAL_AUTHORITATIVE_GUARD，清理核全部引用/hold。
- 当前已交付：W189大小/扫描/隔离/不可变内容/当前下载和hold已实现，W190真实DOC绑定已采用。
- 真实剩余/下一步：补全Owner引用图与清理决定、通用草稿转正式原操作及ACT-FILE LOCAL_AUTHORITATIVE_GUARD实际适配；不要重复DOC绑定或以未做生产部署否定本地实际扫描。
- 原正验收：合格扫描文件按原命令形成准确正式绑定，重复完成返回同结果。原反验收：隔离文件下载、旧授权重放、缺引用核查清理、改bytes同版本均拒绝。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W135-01](../W135-01/DELIVERY.md)；[W189-01](../W189-01/DELIVERY.md)；[W190-01](../W190-01/DELIVERY.md)。
- 当前源码定位：[AttachmentService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Pub/AttachmentService.cs)；[FileContentOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Pub/FileContentOwnerV1.cs)；[IAttachmentService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Pub/IAttachmentService.cs)

<a id="wp02-15"></a>

### WP02-15 隐私subject目录、导出与擦除编排

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：稳定subject目录与逐Owner导出清单，遗漏/不可用明确；擦除预览/审批/hold与回执；未知结果保留责任，撤销fence/恢复墓碑/下载当前授权；真实各域适配随消费者另接。
- 当前已交付：稳定subject目录身份修复已交付；CRM同意撤回和保留已有独立原生行为。
- 真实剩余/下一步：逐Owner导出清单及遗漏/不可用、擦除预览/批准/hold、UNKNOWN责任与墓碑恢复/下载编排尚未完整实现；真实各域适配仍各自接入。
- 原正验收：已接Owner导出清单可追踪，擦除后恢复墓碑仍生效，例外有凭据。原反验收：hold未解、当前权限失效、未知结果自动重做、漏Owner却宣称完整均拒绝。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W125-01](../W125-01/DELIVERY.md)；[W135-01](../W135-01/DELIVERY.md)；[W127-01](../W127-01/DELIVERY.md)；[W128-01](../W128-01/DELIVERY.md)。
- 当前源码定位：[GdprService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Platform/GdprService.cs)；[IGdprService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Platform/IGdprService.cs)

<a id="wp02-16"></a>

### WP02-16 编号原命令分配和生成器安全预览

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：tenant/法人/业务scope与规则版本、原子分配和唯一键；固定command到号码/结果，不回收重用；生成器模板版本、diff和路径/覆盖白名单。
- 当前已交付：PG序号原子锁、租户/BizKey及调用者事务savepoint已实现，代码生成preview已有交付。
- 真实剩余/下一步：NextAsync只有bizKey/日期，尚无command到原号码持久映射；补法人/业务scope/规则版本、永不回收，以及生成器模板版本/diff/路径覆盖白名单。
- 原正验收：并发分配唯一，同原key重试仍同号码；安全生成预览可复现。原反验收：跨scope重用、同命令换号、路径越界和覆盖Owner合同入口均拒绝。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[codegen-preview](../WP02-16/codegen-preview/DELIVERY.md)；[W135-01](../W135-01/DELIVERY.md)；[W65-01](../W65-01/DELIVERY.md)。
- 当前源码定位：[20260613185625_PubCommonCodeGen.Designer.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Migrations/20260613185625_PubCommonCodeGen.Designer.cs)；[20260613185625_PubCommonCodeGen.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Migrations/20260613185625_PubCommonCodeGen.cs)；[CodeGenPreview.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Pub/CodeGenPreview.cs)；[CodeGenService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Pub/CodeGenService.cs)；[ISeqService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Pub/ISeqService.cs)

<a id="wp02-18"></a>

### WP02-18 工作台菜单CAS与授权KPI查询

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：版本化菜单/个性配置及服务端动作权限；KPI由Owner提供定义；缓存计数含租户/授权，区分零/无权/缺源。
- 当前已交付：首页状态、深链和当前权限bootstrap已接入。
- 真实剩余/下一步：服务端版本化菜单/个性配置CAS、各Owner KPI定义与授权缓存键仍需完整实现/验收；零值、无权和缺源必须分开。
- 原正验收：合法配置CAS与授权数据展示一致，切租户刷新准确数据。原反验收：旧缓存跨租户、撤权深链、缺源显示为业务零均拒绝。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[menu-refresh-recovery](../WP02-18/menu-refresh-recovery/DELIVERY.md)；[dashboard-states](../WP02-18/dashboard-states/DELIVERY.md)；[W146-01](../W146-01/DELIVERY.md)；[W148-01](../W148-01/DELIVERY.md)。
- 当前源码定位：[MesDashboardController.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.WebApi/Controllers/Mes/MesDashboardController.cs)；[DashboardController.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.WebApi/Controllers/Sys/DashboardController.cs)；[WmsDashboardController.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.WebApi/Controllers/Wms/WmsDashboardController.cs)

<a id="wp02-28"></a>

### WP02-28 Main宿主实际加载身份与兼容回退验证

- 判定：**明确实现余项**；原状态`IN_PROGRESS`保持。
- 原scope：Main固定包版本与能力，核实际加载/运行身份；本地隔离验证兼容更新/回退；准确采用清单提交给既定R2/GHCR候选门，不能将本地产物当生产候选。
- 当前已交付：Main固定包/实际加载观察和回退拒绝路径已有交付，相关本地证据可复用。
- 真实剩余/下一步：尚无两份兼容实际制品的正向更新/回退采用证明；需隔离PG schema/驱动兼容验证及准确采用清单。正式R2/GHCR候选和推广另行办理，不能拿本地文件替代。
- 原正验收：启动实际宿主的包/源码/运行身份符合清单，允许回退有schema兼容证据。原反验收：加载未知包、身份不匹配、缺兼容证明回退均拒绝；不触发Actions/部署。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[published-host](../WP02-28/published-host/DELIVERY.md)；[WP02-28](../WP02-28/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp02-29"></a>

### WP02-29 CRM宿主平台合同与观测包采用

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：CRM固定可追溯平台包与准确HTTP/消息/观测配置，实际加载版本核验；与CRM原生Owner边界定向验证，发布与环境推广按原门禁另行办理。
- 当前已交付：CRM平台0.10.3、原生HTTP/消息/操作观测及managed payload启动核验已交付；W246限定D5完成。
- 真实剩余/下一步：对原卡“实际加载版本/配置及Owner边界”逐项核销剩余身份观察项，区分已证实托管payload与未证实进程/native身份；正式发布与环境推广不是此卡额外开工门。
- 原正验收：CRM隔离宿主实际加载清单一致，错误/取消/消息路径可追溯。原反验收：旧包能力不足、native合同改写、未知运行身份不得通过采用门。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W130-01](../W130-01/DELIVERY.md)；[W131-01](../W131-01/DELIVERY.md)；[W132-01](../W132-01/DELIVERY.md)；[W143-01](../W143-01/DELIVERY.md)；[W246-01](../W246-01/DELIVERY.md)。
- 当前源码定位：[CrmPlatformAuthenticationExtensions.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Authentication/CrmPlatformAuthenticationExtensions.cs)；[CrmPlatformDaprDeliveryGate.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Infrastructure/Messaging/CrmPlatformDaprDeliveryGate.cs)；[CrmPlatformEventContractGate.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Infrastructure/Messaging/CrmPlatformEventContractGate.cs)；[CrmPlatformTransactionalMessageValidator.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Infrastructure/Messaging/CrmPlatformTransactionalMessageValidator.cs)

<a id="wp03-01"></a>

### WP03-01 公开表单Bundle与配置生效

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：不可变FormBundle、表单版本及政策/日历绑定；Draft预览、定时生效、替代及退休；数据库时钟选择唯一有效Bundle；配置权限、字段裁剪、React配置页与陈旧预览反馈；在原CRM Owner内交付版本化PrivacyPolicy/BusinessCalendar资料及受权发布/准确读取，复用真实host/tzdb和法定政策来源；完整effectivity与同day/holiday合同保真，未知能力fail closed。。
- 当前已交付：不可变Bundle/日历、Draft/Preview/Activate、替代退休与DB时钟已交付；W247实际日历/DST和旧Attempt冻结验证通过。
- 真实剩余/下一步：核完整effectivity及同day/holiday合法域与原验收映射，不能沿用“等真实企业政策”的旧阻塞。现合同H01仅Asia/Shanghai，任意时区自动转Lead属于另版合同，不能暗增本卡范围。
- 原正验收：相邻生效窗口准确选择Bundle且历史不可修改；配置重放返回原版本；只预览不发公开Attempt。原反验收：重叠窗口/未知政策/过期预览整笔拒绝；停用旧Bundle不修改已发Attempt的原绑定。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W133-01](../W133-01/DELIVERY.md)；[W135-01](../W135-01/DELIVERY.md)；[W234-01](../W234-01/DELIVERY.md)；[W246-01](../W246-01/DELIVERY.md)；[W247-01](../W247-01/DELIVERY.md)。
- 当前源码定位：[CrmInquiryConfigurationOwnerV2.Context.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.Context.cs)；[CrmInquiryConfigurationOwnerV2.Lifecycle.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.Lifecycle.cs)；[CrmInquiryConfigurationOwnerV2.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.cs)；[CrmCalendarStorageCodec.Publication.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Infrastructure/Persistence/CrmCalendarStorageCodec.Publication.cs)；[CrmCalendarStorageCodec.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Infrastructure/Persistence/CrmCalendarStorageCodec.cs)

<a id="wp03-02"></a>

### WP03-02 浏览器绑定公开提交与H01自动受理

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：GET持久IssuedAttempt、浏览器/CSRF/有效期；提交使用精确原Bundle及同意事实；同一外层事务完成Attempt消费、Submission、ReceiptProof、ConsentFact、可选H01 Lead全图、审计/Outbox/原命令；公开五字段回执、刷新恢复与49状态中公开流程；原键未知只查询原效果；本卡负责CRM仓对所列Main共同NUM/FILE/PRIV能力的真实版本适配、可信身份传递与当前输出门；提供者交付不等于CRM已采用，不跨库直写Main主账，按实际调用证明原键、撤权和披露/保留规则。。
- 当前已交付：IssuedAttempt/浏览器CSRF、Submission/H01同事务全图及原回执恢复已采用，W234/247补实际传播、规范化与日历验收。
- 真实剩余/下一步：整卡共同NUM/FILE/PRIV适用需按W135原闭合合同准确核销：LeadNo可未分配、无附件输入，不应新增虚构调用；PRIV当前同意/保留与全域导出分开。完成原公开流程状态验收映射。
- 原正验收：重复同浏览器同意图仅一Submission/一Lead且回执原字节保持；自动受理事务失败无半个Lead或可读未完成图。原反验收：跨浏览器、CSRF、失效Attempt或payload变更拒绝且不泄露内部ID；提交确认丢失不能新建Attempt替代原未知命令。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W115-01](../W115-01/DELIVERY.md)；[W134-01](../W134-01/DELIVERY.md)；[W135-01](../W135-01/DELIVERY.md)；[W234-01](../W234-01/DELIVERY.md)；[W246-01](../W246-01/DELIVERY.md)；[W247-01](../W247-01/DELIVERY.md)；[W117-01](../W117-01/DELIVERY.md)；[W118-01](../W118-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[CrmInquiryPublicBrowserV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryPublicBrowserV1.cs)；[CrmInquirySubmissionOwnerV1.Graph.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.Graph.cs)；[CrmInquirySubmissionOwnerV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.cs)

<a id="wp03-03"></a>

### WP03-03 询盘审核队列、放行拒绝与到期

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：NeedsReview队列/详情按当前范围与字段裁剪；放行复用真实H01同事务；拒绝与到期独立终态；版本竞争和主管撤权重核；React审核动作、原操作恢复、匿名化状态展示。
- 当前已交付：W120–122审核队列/字段裁剪、真实H01放行/拒绝及React/BFF/PG场景已交付；W124保留匿名化、W133发布已采用。
- 真实剩余/下一步：独立审核到期终态的实际过期HTTP/worker效果证据尚未定位；60分钟PII保留与7天审核到期不同。优先核源码/已有测试，再补缺失执行，不重做放行和拒绝。
- 原正验收：两主管放行同条只一个H01赢家；拒绝或到期生成受理终态而Lead写入为零。原反验收：终态不再提供审核动作；放行前撤权、旧ETag、隐私失效全回滚。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W133-01](../W133-01/DELIVERY.md)；[W120-01](../W120-01/DELIVERY.md)；[W121-01](../W121-01/DELIVERY.md)；[W122-01](../W122-01/DELIVERY.md)；[W124-01](../W124-01/DELIVERY.md)。
- 当前源码定位：[CrmInquiryRetentionOwnerV1.Graph.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionOwnerV1.Graph.cs)；[CrmInquiryRetentionOwnerV1.Originals.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionOwnerV1.Originals.cs)；[CrmInquiryRetentionOwnerV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionOwnerV1.cs)；[CrmInquiryRetentionWorkerV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionWorkerV1.cs)

<a id="wp03-04"></a>

### WP03-04 询盘同意、保留匿名化与隐私导出

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：ConsentFact原证据、安全字段读取及版本化保留策略；PII清除、Attempt墓碑和导出20/30/90/99状态采用；禁复活敏感内容；审计/回执/缓存/导出同步裁剪；原后台任务重试可恢复；本卡负责CRM仓对所列Main共同NUM/FILE/PRIV能力的真实版本适配、可信身份传递与当前输出门；提供者交付不等于CRM已采用，不跨库直写Main主账，按实际调用证明原键、撤权和披露/保留规则。。
- 当前已交付：Consent撤回当前权限、60分钟保留/Attempt墓碑和hold worker已经实际验证；导出下载权限来源已提供。
- 真实剩余/下一步：原生导出制品20/30/90/99、单次下载及FILE/PRIV/NUM适用编排未完成；Lead月度保留真实边界仅有投影证据的部分需按合同执行，不偷改期限换通过。
- 原正验收：匿名化后姓名联系方式需求为null且风险/动作集合为空；原导出完成后按终态销毁活动artifact/proof引用。原反验收：回执重放、旧缓存或导出不得恢复PII；未知导出/清除结果不得标成功或重复建业务。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W126-01](../W126-01/DELIVERY.md)；[W127-01](../W127-01/DELIVERY.md)；[W128-01](../W128-01/DELIVERY.md)；[W133-01](../W133-01/DELIVERY.md)；[W124-01](../W124-01/DELIVERY.md)。
- 当前源码定位：[CrmInquiryConsentCapabilitiesV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryConsentCapabilitiesV1.cs)；[CrmInquiryConsentWithdrawalOwnerV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryConsentWithdrawalOwnerV1.cs)；[CrmInquiryRetentionOwnerV1.Graph.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionOwnerV1.Graph.cs)；[CrmInquiryRetentionOwnerV1.Originals.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionOwnerV1.Originals.cs)；[CrmInquiryRetentionOwnerV1.cs](https://github.com/GTX537/CP6.CRM/blob/c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f/src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionOwnerV1.cs)

<a id="wp04-33"></a>

### WP04-33 ERP经营产品物料身份与工程映射读取提供者

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：在既有ProductService主账提供准确经营料号/物料/单位/币种/有效状态和scope版本读取；工程映射由各原Owner同门确认，保存原业务身份/映射版本，先提供工程与估算消费所需底座；不重建平行ERP产品表；未知legacy映射保持未证，旧写者资格改变同步frontier。
- 当前已交付：现有ERP产品主账准确商业身份/单位及工程双Owner映射已有交付，库存身份W204已补。
- 真实剩余/下一步：当前映射明确IDENTITY_SITE_ONLY；法人/制造等完整scope版本适用及所有legacy资格变更frontier继续补，币种/有效状态按原主账读取，禁止另建ERP表。
- 原正验收：同名不同物料不被合并，旧订单继续引用原身份；工程停用或映射变化后新采用由当前门拒绝，历史仍可读。原反验收：不以ENG-PROD身份直接替换ERP主键；缺UOM/profile不能默认为EA。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W144-01](../W144-01/DELIVERY.md)；[W145-01](../W145-01/DELIVERY.md)；[W147-01](../W147-01/DELIVERY.md)；[W149-01](../W149-01/DELIVERY.md)；[W150-01](../W150-01/DELIVERY.md)；[W204-01](../W204-01/DELIVERY.md)。
- 当前源码定位：[IProductService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/IProductService.cs)；[ProductService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/ProductService.cs)；[EngineeringProductContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringProductContractV1.cs)；[EngineeringProductMappingService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringProductMappingService.cs)；[EngineeringProductOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringProductOwnerV1.cs)

<a id="wp04-34"></a>

### WP04-34 原发运商业可退资格与独立退货来源授权

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：向RMA提供准确原商业行/Shipment份额、当前source及允许退货依据，独立准入仅沿原接受Owner能力；与WMS拥有的E/A/P entitlement预算和独立RMA批准分责：不把ERP-CREDIT后续贷项决定前移为物理许可；版本化原授权/撤销/current及原键查询，旧发运/已退范围按完整来源读取。
- 当前已交付：原商业行快照W226、Shipment份额W222/223、政策/Delivery/退货授权W227–229均已采用，W248商业桥接受明确模拟外部输入。
- 真实剩余/下一步：核完整旧发运/已退份额和撤销/current来源覆盖，落实原商业Owner允许退货依据的准确适配；模拟外部服务可验收边界，不能被说成内部ERP所有来源均已实现。
- 原正验收：物理客退先获得原准确商业来源而无需先存在贷项/退款；同SKU多发运行按真实原行/区间返回可退来源。原反验收：批准可退不等可贷/已退款；不从尚未收到的RMA或CommercialWork倒造原退货授权。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W226-01](../W226-01/DELIVERY.md)；[W222-01](../W222-01/DELIVERY.md)；[W223-01](../W223-01/DELIVERY.md)；[W227-01](../W227-01/DELIVERY.md)；[W228-01](../W228-01/DELIVERY.md)；[W229-01](../W229-01/DELIVERY.md)；[W248-01](../W248-01/DELIVERY.md)。
- 当前源码定位：[SalesReturnSourceReaderV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/SalesReturnSourceReaderV1.cs)；[RmaFormalOwnerV1.Authorization.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.Authorization.cs)；[RmaFormalOwnerV1.AuthorizationStore.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.AuthorizationStore.cs)；[RmaFormalOwnerV1.Commercial.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.Commercial.cs)；[RmaFormalOwnerV1.Correction.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.Correction.cs)

<a id="wp05-02"></a>

### WP05-02 工程产品身份与经营料号双Owner映射

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：建立稳定产品身份、类别、来源、Site/法人适用域及映射历史；改造现产品入口的工程引用；草稿、歧义核对与双Owner确认分开；提供按当前权限的产品/映射维护与使用引用入口，停用改型不重键历史。
- 当前已交付：稳定工程产品、原商业身份/Site与双Owner确认及映射历史已交付。
- 真实剩余/下一步：当前只证IDENTITY_SITE_ONLY；类别/来源、法人适用及技术修订/配置范围要按原scope补，停用改型保原身份与历史，不重建产品号。
- 原正验收：同一工程身份分别映射两个工厂料号，准确保留双方Owner确认与适用域；原键重放返回同身份及映射版本。原反验收：同名异规格不自动合并；未知映射不能显示确认；工程角色不能更新ERP价格/库存；跨租户同码不可相互恢复。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W150-01](../W150-01/DELIVERY.md)。
- 当前源码定位：[EngineeringProductContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringProductContractV1.cs)；[EngineeringProductMappingService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringProductMappingService.cs)；[EngineeringProductOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringProductOwnerV1.cs)；[EngineeringProductStoreV1.Mapping.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringProductStoreV1.Mapping.cs)；[EngineeringProductStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringProductStoreV1.cs)

<a id="wp05-03"></a>

### WP05-03 工程修订、工作快照与组成候选

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：新增EngineeringRevision、WorkingSnapshot、ConfigurationSnapshot、Candidate/Member与原命令记录；成员保Owner原Ref/version/digest和occurrence；显式patch追加快照，三方rebase逐项选择并失效旧检查；提供草稿/差异/来源可用性页面和按原键恢复；本卡不冻结或授制造许可。
- 当前已交付：EngineeringRevision/WorkingSnapshot/Candidate成员原Ref及快照/选项已实现；后续ROU/BOM/DOC/QUALITY/关系/基线已有真实采用。
- 真实剩余/下一步：完整三方rebase逐项KEEP/ACCEPT/MANUAL与旧CheckRun失效、父基线/变更和legacy入口覆盖仍需实施/验收，不能把已交付成员类型继续列空缺。
- 原正验收：同产品修订连续保留准确父基线/来源，重复相同候选命令只生成一版；同料两个occurrence分列，重定基线保KEEP/ACCEPT/MANUAL决定及理由。原反验收：latest、缺来源或未知Owner不能变成可冻结完整成员；在审/冻结内容不可原地覆盖；过期CAS不增候选版。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W151-01](../W151-01/DELIVERY.md)；[W152-01](../W152-01/DELIVERY.md)；[W186-01](../W186-01/DELIVERY.md)；[W187-01](../W187-01/DELIVERY.md)；[W190-01](../W190-01/DELIVERY.md)；[W192-01](../W192-01/DELIVERY.md)；[W201-01](../W201-01/DELIVERY.md)；[W202-01](../W202-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.EngineeringRevision.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.EngineeringRevision.cs)；[EngineeringRevisionContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRevisionContractV1.cs)；[EngineeringRevisionOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRevisionOwnerV1.cs)；[EngineeringRevisionStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRevisionStoreV1.cs)；[EngineeringRevisionValuesV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRevisionValuesV1.cs)

<a id="wp05-04"></a>

### WP05-04 旧版模写入口的版本与原事务兼容

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：覆盖/api/plate-molds创建、改版、纠正、删除等全部旧写者；保完整BaseCd/WdPtnNo/WdRev；expectedNativeRowVersion和expectedTechnicalControl共同检查；新修订、有效区间、原回执同事务；改造旧两页的版本必填、冲突保输入、Unknown原键查询，保旧字段合法域和历史摘要。
- 当前已交付：旧版模创建/改版/纠正及版本化原结果、两旧页与Order意图已交付，BaseCd/WdPtnNo/WdRev保留。
- 真实剩余/下一步：按全部旧写者核expectedNativeRowVersion+expectedTechnicalControl、有效区间/删除/导入与Unknown恢复覆盖；技术规格当前DRAFT不等正式控制批准，但不得将后续制造许可扩大成本卡。
- 原正验收：两请求争同前驱只有一个合法后继，重复原命令返回同修订；旧页能读取旧版本与技术/实物独立状态，历史不被批量改键。原反验收：缺版本不能服务端补latest；max+1失败不得换Rev重试；技术退役不能执行实物报废或级联删除已提交订单。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W156-01](../W156-01/DELIVERY.md)；[W158-01](../W158-01/DELIVERY.md)；[W159-01](../W159-01/DELIVERY.md)；[W157-01](../W157-01/DELIVERY.md)。
- 当前源码定位：[20260506103852_AddSheetPriceAndPlateMold.Designer.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Migrations/20260506103852_AddSheetPriceAndPlateMold.Designer.cs)；[20260506103852_AddSheetPriceAndPlateMold.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Migrations/20260506103852_AddSheetPriceAndPlateMold.cs)；[IPlateMoldService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/IPlateMoldService.cs)；[PlateMoldService.WriteOwner.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/PlateMoldService.WriteOwner.cs)；[PlateMoldService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/PlateMoldService.cs)

<a id="wp05-05"></a>

### WP05-05 版模商业联动改为原订单Owner意图

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：把直接改订单/ArrangeNo、删除联动及重建遗失关联改为CREATE_OR_LINK/CHANGE_REQUEST/CANCEL_REQUEST；持久完整No/Rev、原订单Ref、技术身份、请求摘要、原Owner应用回执与恢复记录；接真实ERP/PE适配并检查DI路由；NoOp只能呈STUB_NOT_EXECUTED，不造OWNER_APPLIED。
- 当前已交付：CREATE_OR_LINK真实订单采用、完整订单身份及CHANGE/CANCEL请求原结果已交付。
- 真实剩余/下一步：CHANGE_REQUEST/CANCEL_REQUEST仍仅REQUESTED；接真实ERP/PE决定/应用/回执和恢复，替换旧直接联动/重建关联路径，不能以请求或NoOp算OWNER_APPLIED。
- 原正验收：重复联动原键恢复同真实订单及回执；未知只查询原业务键；已应用后本地回执丢失可恢复关联且零次新增订单。原反验收：NoOp Ok不能显示已执行；不匹配回执隔离；取消请求或技术删除不能直接抹原订单事实。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W157-01](../W157-01/DELIVERY.md)；[W158-01](../W158-01/DELIVERY.md)。
- 当前源码定位：[20260506103852_AddSheetPriceAndPlateMold.Designer.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Migrations/20260506103852_AddSheetPriceAndPlateMold.Designer.cs)；[20260506103852_AddSheetPriceAndPlateMold.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Migrations/20260506103852_AddSheetPriceAndPlateMold.cs)；[IPlateMoldService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/IPlateMoldService.cs)；[PlateMoldService.WriteOwner.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/PlateMoldService.WriteOwner.cs)；[PlateMoldService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Erp/PlateMoldService.cs)

<a id="wp05-06"></a>

### WP05-06 工具规格与实例双Role当前关联

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：建立技术规格/版本与实例关联提案，原生Owner schema和不可变证据Inbox；持久控制版、observationGeneration、round及投影版；完整双Role结果才应用；接各真实Owner HoldExact与当前权限共同提交；相同回执去重、冲突隔离、旧轮只存历史。
- 当前已交付：W165已实现实际双Role当前轮关联和技术/实物HoldExact共同提交；W166–168后续维护/使用也存在，W159不是当前全部范围。
- 真实剩余/下一步：逐原协议核不可变证据Inbox、迟到/冲突/observationGeneration及各Owner准确控制适用；当前技术DRAFT路径不自动获得批准资格。需要外部Owner原件的路径按真实调用补，不重做双Role关联。
- 原正验收：两个真实Role在同当前轮次及最终提交保护下产生CONFIRMED及应用回执；一个Role到达不无故失效另一个slot；同round同body恢复原结果。原反验收：A确认后B变化且迟到A不得恢复确认；未知不得显示撤销或确认；TTL/签名/先GET后保存或缺真实参与者保持UNKNOWN/FENCED。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W159-01](../W159-01/DELIVERY.md)；[W165-01](../W165-01/DELIVERY.md)；[W166-01](../W166-01/DELIVERY.md)；[W167-01](../W167-01/DELIVERY.md)；[W168-01](../W168-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.ToolSpecification.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.ToolSpecification.cs)；[ToolAssociationOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ToolAssociationOwnerV1.cs)；[ToolSpecificationContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ToolSpecificationContractV1.cs)；[ToolSpecificationOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ToolSpecificationOwnerV1.cs)；[ToolSpecificationStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ToolSpecificationStoreV1.cs)

<a id="wp05-08"></a>

### WP05-08 版本化工艺操作与工具规格引用

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：从ProductProcess适配受控路线版、稳定operation occurrence、顺序/依赖、资源参数与替代工艺；保工时/损耗/单位来源和准确工具规格Ref，实例寿命/位置仍外部Owner；实现维护、差异、使用引用及校验输入；变化不覆盖旧工单快照。
- 当前已交付：W186路线稳定operation occurrence/依赖及ProductProcess原源已实现，W203资源能力引用已接入。
- 真实剩余/下一步：正式资源/工具规格与替代工艺的适用及参数来源继续补；当前DRAFT_ONLY和声明能力不能声称完整受控路线资格。生产Claim/Start属资源/执行卡，勿扩大本卡。
- 原正验收：同工序码多次发生保独立occurrence和先后关系；路线新版本与旧执行快照分别可读，准确工具规格可追溯。原反验收：循环工序/未知量纲及未证参数不能校验为PASS；PlateNo字符串或同型号实例不得当HoldExact资格。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W186-01](../W186-01/DELIVERY.md)；[W203-01](../W203-01/DELIVERY.md)。
- 当前源码定位：[EngineeringRoutingOwnerV1.Legacy.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRoutingOwnerV1.Legacy.cs)；[EngineeringRoutingOwnerV1.Sources.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRoutingOwnerV1.Sources.cs)；[EngineeringRoutingOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRoutingOwnerV1.cs)；[OutboundRoutingOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/OutboundRoutingOwnerV1.cs)

<a id="wp05-09"></a>

### WP05-09 版本化BOM、材料替代与有效性

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：从ProductMaterial适配稳定BOM occurrence、版本、数量/损耗、适用范围和替代规则；保存单位转换批准/来源与配置条件，独立经营BOM适配而保旧行历史；维护/比较/where-used入口及当前授权、原命令/CAS。
- 当前已交付：W187 BOM occurrence、超常精度数量、原ProductMaterial来源、CAS/差异/where-used已交付。
- 真实剩余/下一步：补批准单位转换、替代/配置条件与完整适用域；不得以隐含倍率1或缩小合法精度代替，旧行历史继续保持。
- 原正验收：同材料两个位置不聚合；合法精度/单位无损落PG且可往返；新有效性版只影响合格新引用，已固定生产材料仍用原版。原反验收：未知换算不补1；不能以小数截断满足旧列限制；未批准替代或范围外材料不能进入可执行BOM。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W187-01](../W187-01/DELIVERY.md)。
- 当前源码定位：[EngineeringBomOwnerV1.Legacy.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringBomOwnerV1.Legacy.cs)；[EngineeringBomOwnerV1.Sources.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringBomOwnerV1.Sources.cs)；[EngineeringBomOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringBomOwnerV1.cs)

<a id="wp05-10"></a>

### WP05-10 配置与逐输出输入工序关系

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：实现ENG-OUTPUT-RELATIONS/1：逐input/output occurrence、producing operation、完整配置与关系证据；闭包校验输入/操作/输出集合及主PRODUCT对应，原manifest bytes不改；变更输出/配置即使原检查和冻结预览失效；提供多输出差异与准确读接口。
- 当前已交付：W188制造模型后，W201 ENG-OUTPUT-RELATIONS/1闭包/主产品/多输出原关系已实际采用，W202冻结也已消费。
- 真实剩余/下一步：核外部Item/Spec准确来源适用、完整合法配置/关系输入及大合法包验收证据；缺的是原卡完整覆盖映射，不能把REL/WO全部实现列为此卡前置。
- 原正验收：同产品在两工序产生两次仍两输出，所有输入/操作来自同配置闭包；新增关系独立摘要且原单PRODUCT黄金字节/摘要保持。原反验收：缺input/operation、不同配置、孤立输出或无依据无输入操作不能PASS；不得按产品去重、行号推工序或给旧Baseline事后补新关系。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W188-01](../W188-01/DELIVERY.md)；[W201-01](../W201-01/DELIVERY.md)；[W202-01](../W202-01/DELIVERY.md)。
- 当前源码定位：[EngineeringOutputRelationsContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringOutputRelationsContractV1.cs)；[EngineeringOutputRelationsOwnerV1.Sources.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringOutputRelationsOwnerV1.Sources.cs)；[EngineeringOutputRelationsOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringOutputRelationsOwnerV1.cs)；[EngineeringOutputRelationsStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringOutputRelationsStoreV1.cs)；[EngineeringOutputRelationsValuesV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringOutputRelationsValuesV1.cs)

<a id="wp05-11"></a>

### WP05-11 工程源固定证明与完整性检查

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：登记每类真实Owner原生源pin、快照水位、策略版本及HoldExact参加能力；CheckRun保存完整输入/源水位/结果，PASS/BLOCK/UNKNOWN/N/A有准确依据；当前源/权限/配置变化使检查失效；技术检查只给冻结输入，不造许可。
- 当前已交付：W193完整输入/水位/策略CheckRun、真实QUALITY/DOC等pin/current/HoldExact已交付；W201输出关系已纳管。
- 真实剩余/下一步：资源/工具等尚未具备资格的来源参与者必须保持UNKNOWN/BLOCK；补其实际原生资格/current参加和完整内部制造正例，签名或GET不替代共同最终检查。
- 原正验收：准确候选、全部技术来源和工具当前参与可生成可复验CheckRun；原源不可达保旧证据及本次UNKNOWN，能定位具体缺项。原反验收：候选WP0的SQLServer-only冻结原型不能登记成Main已支持；仅缓存/签名/顺序GET不足以证明最终源固定，缺参与者不得给肯定冻结资格。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W193-01](../W193-01/DELIVERY.md)；[W201-01](../W201-01/DELIVERY.md)；[W203-01](../W203-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp05-12"></a>

### WP05-12 工程基线冻结、后继与准确发布委托

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：依据当前技术评审/双候选双摘要重核源、配置、策略与授权；冻结不用等待VER PASS；同PG事务写Baseline/Members、原manifest、新RelationSet/OutputBinding、原回执、audit/outbox；准确baseline读/差异/where-used与后继、原操作查询/取消；REL委托只保存真实Owner关联。
- 当前已交付：W202同PG冻结完整成员/原manifest/关系/输出绑定/回执与恢复已通过；实际正例为PURCHASED_FINISHED。
- 真实剩余/下一步：INTERNAL技术冻结仍受实际资源/工具资格来源缺项限制；补全所需参与者与合法大包/后继覆盖，REL只保真实Owner关联，不以委托字符串授制造许可。
- 原正验收：冻结响应丢失查原命令得同Baseline，原字节与独立关系绑定都可复验；验证前可冻结受测基线；技术冻结、评审、许可和采用各轴独立。原反验收：来源漂移或完整输出绑定不匹配零新Baseline；取消迟到不倒改已冻结结果；无REL许可不能创建WO；历史基线不得补造当时证据。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W202-01](../W202-01/DELIVERY.md)；[W203-01](../W203-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.EngineeringBaseline.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.EngineeringBaseline.cs)；[EngineeringBaselineContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringBaselineContractV1.cs)；[EngineeringBaselineOwnerV1.Successor.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringBaselineOwnerV1.Successor.cs)；[EngineeringBaselineOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringBaselineOwnerV1.cs)；[EngineeringBaselineStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringBaselineStoreV1.cs)

<a id="wp05-13"></a>

### WP05-13 工程需求来源、分解与可评价判据

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：登记真实客户/内部来源及不可变版，保原文、解释、澄清及敏感可见域；需求分解/依赖/冲突关系防环，判据保存上下端点、单位工况、方法需求与必要性；需求工作台支持原件/澄清/差异和当前权限，不预造ITER或VER正式ID。
- 当前已交付：W194不可变来源/需求分解/依赖防环/精确判据与W195专业评估已实现。
- 真实剩余/下一步：接CRM/销售实际原件读取和DOC/FILE澄清来源，补准确方法需求/必要性与可见域覆盖；不预造ITER/VER正式ID，已做UOM判据不重复。
- 原正验收：来源可追且未澄清项可存草稿；定量/定性判据均有可评价规则；同名不同来源保持不同要求及明确关系。原反验收：循环分解不能自证覆盖；未知阈值/单位不得填默认；不可访问客户来源不能在列表/导出泄漏内容。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W194-01](../W194-01/DELIVERY.md)；[W195-01](../W195-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.EngineeringRequirement.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.EngineeringRequirement.cs)；[EngineeringRequirementContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRequirementContractV1.cs)；[EngineeringRequirementService.EvaluationSources.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRequirementService.EvaluationSources.cs)；[EngineeringRequirementService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRequirementService.cs)；[EngineeringRequirementStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringRequirementStoreV1.cs)

<a id="wp05-14"></a>

### WP05-14 需求集批准、冲突裁决与后继影响

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：冻结需求集完整成员/必要性/范围；用受控评审批准准确集合；冲突按真实适用交集和有权规则选择澄清/范围分离/ECM/DEV去向；后继版保逐条差分/采用链，向已有消费者发准确影响意图。
- 当前已交付：W196完整ReqSet受控评审/批准与真实FLOW已实现，W197迭代已消费。
- 真实剩余/下一步：真实适用交集冲突裁决、澄清/范围分离/ECM/DEV去向以及后继差分/已采用消费者影响意图仍需完整闭合，不把普通批准当冲突裁决。
- 原正验收：批准需求集具有不可变成员版与签署依据，原版可回读；局部冲突的有据范围分离保留双方原需求。原反验收：审批通过不直接冻结ENG或给REL许可；不能删必需/不利条目或复制旧签署到改动集合。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W196-01](../W196-01/DELIVERY.md)；[W197-01](../W197-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.RequirementSet.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.RequirementSet.cs)；[RequirementSetApplicationDeliveryV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/RequirementSetApplicationDeliveryV1.cs)；[RequirementSetContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/RequirementSetContractV1.cs)；[RequirementSetReviewApplicationStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/RequirementSetReviewApplicationStoreV1.cs)；[RequirementSetReviewContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/RequirementSetReviewContractV1.cs)

<a id="wp05-16"></a>

### WP05-16 受控技术文档版本与原件派生

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：复用Attachment存储，新增原件/派生/权威语言、准确内容版本/摘要及受控修订Owner；冻结提交内容、修订差异、当前评审结果与批准依据；旧版hold/撤回/可用性独立；精确下载/内容历史与原键恢复，文件生命周期权限贯穿所有出口。
- 当前已交付：W190准确原件/派生/语言/永久绑定/历史下载与W191冻结专业评审/批准、真实FLOW/回执均已交付。
- 真实剩余/下一步：独立旧版hold/撤回/可用性与失权旧URL拒绝逐原验收核销；不能由FILE hold推定DOC独立撤回已有。源中未定位完整DOC撤回动作，需确认职责后只补真实缺项。
- 原正验收：同名不同版本保独立内容身份；批准只绑定本次提交版本；缺v2显示不可用而仍保引用；有权人可下载准确旧版。原反验收：评审中改内容不得沿用旧签署；附件bool/文件名/URL不等受控内容；失权不得旧URL下载。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W190-01](../W190-01/DELIVERY.md)；[W191-01](../W191-01/DELIVERY.md)；[W189-01](../W189-01/DELIVERY.md)。
- 当前源码定位：[ControlledDocumentOwnerV1.Sources.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ControlledDocumentOwnerV1.Sources.cs)；[ControlledDocumentOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ControlledDocumentOwnerV1.cs)；[ControlledDocumentReviewOwnerV1.Application.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ControlledDocumentReviewOwnerV1.Application.cs)；[ControlledDocumentReviewOwnerV1.Evidence.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ControlledDocumentReviewOwnerV1.Evidence.cs)；[ControlledDocumentReviewOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/ControlledDocumentReviewOwnerV1.cs)

<a id="wp05-18"></a>

### WP05-18 工程项目、迭代输入与任务确认

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：项目/里程碑/迭代/输入/计划版/任务建立与修改，来源和工程候选只保存准确Ref；激活核Owner/Site/日期/里程碑；暂停恢复重核输入和未决外部请求；任务Submitted由授权确认人核交付；进度分母固定计划非取消权重，0分母显示未知。
- 当前已交付：项目/里程碑/任务/固定权重与确认角色W197已交付，候选/评审/应用W198–200已后续采用。
- 真实剩余/下一步：按原卡核暂停恢复对未决外部请求和输入变化的当前重核、来源/变更/父基线适用及0分母；CHANGE/LEGACY路径待确认，不能用后续OA未实现的旧说法阻塞。
- 原正验收：同键建迭代只一份，InputVersion与PlanVersion独立；工作100%和技术验证状态分别显示，Blocked任务仍在分母。原反验收：改型缺准确ECO/父基线不得激活；客户端不能改BOM/库存字段；任务完成不推进VER PASS/REL许可；缺自批隔离策略不得送审。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W197-01](../W197-01/DELIVERY.md)；[W198-01](../W198-01/DELIVERY.md)；[W199-01](../W199-01/DELIVERY.md)；[W200-01](../W200-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.EngineeringIteration.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.EngineeringIteration.cs)；[EngineeringIterationContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationContractV1.cs)；[EngineeringIterationService.Candidates.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Candidates.cs)；[EngineeringIterationService.Commands.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Commands.cs)；[EngineeringIterationService.Reviews.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Reviews.cs)

<a id="wp05-19"></a>

### WP05-19 迭代方案全集、评审包与OA持久提交

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：候选分支/技术Refs及差分、ReviewIssue独立版本；冻结完整候选全集与评审包；采用ITER-OA-APPLICATION/1，aggregate/binding/round及UUID别名准确保存，不改原身份摘要；Package/Aggregate/Binding/Round/InReview/Owner Outbox/receipt/audit同事务，浏览器不能选Owner。
- 当前已交付：完整候选/ReviewIssue W198、冻结包与原生OA同事务提交W199已交付，W200决定应用也已接。
- 真实剩余/下一步：核ITER-OA-APPLICATION/1原身份与UUID别名完整合法域，当前native UUID正例不能代替全部外部身份映射；只补真实未覆盖路径，不另造第二种权威摘要。
- 原正验收：多候选全集双向相等且技术/包/aggregate摘要分列；同命令恢复同轮次；OA启动丢响应按Submission原engineStartId查询，不起第二实例。原反验收：只首候选、同ID异版、缺成员或超传输域不得截断发起；本地rejectDisposition不增加OA封闭枚举；旧同步callback不得并行写新路由。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W198-01](../W198-01/DELIVERY.md)；[W199-01](../W199-01/DELIVERY.md)；[W200-01](../W200-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.EngineeringIteration.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.EngineeringIteration.cs)；[EngineeringIterationContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationContractV1.cs)；[EngineeringIterationService.Candidates.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Candidates.cs)；[EngineeringIterationService.Commands.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Commands.cs)；[EngineeringIterationService.Reviews.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Reviews.cs)

<a id="wp05-20"></a>

### WP05-20 迭代决定应用、问题关闭与撤回竞争

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：原bytes Inbox/事件去重与GAP隔离，当前控制头/Round/Binding精确校验；全部Blocking经授权VerifiedClosed后在业务事务应用决定、写ApplicationReceipt/Outbox/audit；撤回与应用同控制CAS；OA只收回执投影；退回和拒绝按冻结本地映射处理。
- 当前已交付：W200真实Inbox/决定、Blocking独立VerifiedClosed、当前控制与撤回竞争及Owner应用回执已交付。
- 真实剩余/下一步：核完整外部身份/alias及GAP/迟到/退回拒绝映射的原正反验收范围；模拟业务角色可执行，不能因没有真实企业数据扣留整卡。
- 原正验收：APPROVED当前有效且无阻塞时才业务Applied，丢回执仅重投原回执；应用先赢/撤回先赢分别只有一个业务结果，历史轮次保持不可变。原反验收：审批Approved不能直接等Owner Applied；问题作者不能自验关闭；旧轮迟到、异body、缺完整绑定或当前授权失效不改变新轮；REJECT不自动Closed。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W200-01](../W200-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.EngineeringIteration.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.EngineeringIteration.cs)；[EngineeringIterationContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationContractV1.cs)；[EngineeringIterationService.Candidates.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Candidates.cs)；[EngineeringIterationService.Commands.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Commands.cs)；[EngineeringIterationService.Reviews.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/EngineeringIterationService.Reviews.cs)

<a id="wp05-39"></a>

### WP05-39 配置替代规则、方向性与有界解析

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：CFG规则明确Required/Provided准确版、occurrence、完整供给组合、单位比例与工程依据；方向/客户/Site/配置/活动/半开窗口/取整损耗及禁止范围版本化；解析返回MATCH/NO_MATCH/CONFLICT/UNKNOWN及依赖，多个备选要求明确选择。
- 当前已交付：W152已有工程配置选项维护/快照。
- 真实剩余/下一步：当前ENGINEERING_OPTIONS_ONLY；完整Required/Provided occurrence组合、方向/比例/工程依据、客户Site活动窗口/取整损耗、MATCH等解析与显式备选选择仍需实现。
- 原正验收：B替A获准不自动A替B；完整组合和每需求单位消耗可解释；存在多个可行但未选择为CHOICE_REQUIRED，原规则/选择历史可追。原反验收：同名同价/默认1:1或字符串批次顺序不能证明替代；MATCH不授REL/商务许可；规则变更不改旧工单材料。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W152-01](../W152-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp08-01"></a>

### WP08-01 设备工作中心版本、归属与当前资源门

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：依WP08-16稳定资源/能力/Assignment原件实现路线相关适用性与完整版本/效力段；RES-WRITE-1签发及实际写者最终门，未来效力切段使旧token失效；SCHED/EXEC实际准入与资源Claim能力；资源身份读取不依赖本卡全部完成。
- 当前已交付：普通设备归属、资源原件/时间UOM/声明能力W136/154/155/203已交付。
- 真实剩余/下一步：路线适用性、完整效力段、RES-WRITE-1 token最终写门、未来切段失效及SCHED/EXEC真实Claim准入仍需实现；身份读取不等资格。
- 原正验收：无权威变化跨分钟token稳定；未来归属到时切段旧token失效。原反验收：缺token428/坏400/变化412；ETag和屏幕asOf不能替代资源token；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W136-01](../W136-01/DELIVERY.md)；[W154-01](../W154-01/DELIVERY.md)；[W155-01](../W155-01/DELIVERY.md)；[W203-01](../W203-01/DELIVERY.md)。
- 当前源码定位：[ResourceCapabilityContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/ResourceCapabilityContractV1.cs)；[ResourceCapabilityOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/ResourceCapabilityOwnerV1.cs)；[ResourceCapabilityStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/ResourceCapabilityStoreV1.cs)；[ResourceCapabilityValuesV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/ResourceCapabilityValuesV1.cs)；[ResourceCapabilityController.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.WebApi/Controllers/Mes/ResourceCapabilityController.cs)

<a id="wp08-10"></a>

### WP08-10 排程候选、完整Root组与资源队列

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：按RootWO/Snapshot/OperationOccurrence读取计划/实际独立轴；建立资源窗、可解释自动排列/改期/优先级候选及队列；冻结完整Root计划、冲突与本地意图，不直接改WO机器。
- 当前已交付：W240 Root TIME、W241自动排列、W242队列及W244/245统一Board与页面实际通过，原D3已经完成。
- 真实剩余/下一步：按完整RootWO/快照/operation源核原卡支持域和冲突/冻结计划的未覆盖情况；部分源由SIM profile登记，需区分允许的模拟外部输入与尚未接入的产品内部来源，不能因模拟本身判失败。
- 原正验收：同capture完整Root组候选可重演；未知实际时间保null及原因。原反验收：默认8点/2小时不得代真实能力；不同UOM产量不合计，候选不授开工；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W240-01](../W240-01/DELIVERY.md)；[W241-01](../W241-01/DELIVERY.md)；[W242-01](../W242-01/DELIVERY.md)；[W243-01](../W243-01/DELIVERY.md)；[W244-01](../W244-01/DELIVERY.md)；[W245-01](../W245-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp08-11"></a>

### WP08-11 排程应用、换机六阶段与恢复

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：采用原六阶段换机及WO/RES/EXEC同当前参与者；完整Root组改期/换机按原WO白名单生效；各阶段原回执、unknown、执行已开始竞争和唯一队列切换。
- 当前已交付：W243六阶段和W245恢复页面、未知结果/唯一队列/WO共同事务已实际执行，不能重做六阶段。
- 真实剩余/下一步：核原协议全部WO/RES/EXEC当前参与者及已开始竞争在当前SIM profile以外的适用边界；仅原卡未覆盖路径追加验证，模拟输入本身不是缺口。
- 原正验收：批准计划按完整Root组应用，换机全过程可查每阶段结果。原反验收：半组成功/旧Snapshot/已变执行状态不得装作完成；不扩WO白名单；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W240-01](../W240-01/DELIVERY.md)；[W241-01](../W241-01/DELIVERY.md)；[W242-01](../W242-01/DELIVERY.md)；[W243-01](../W243-01/DELIVERY.md)；[W244-01](../W244-01/DELIVERY.md)；[W245-01](../W245-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp08-16"></a>

### WP08-16 RES资源身份与独立能力目录提供者

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：从现有Machine/工作中心源建立稳定Resource/Capability/Assignment原件及受控版本；提供ROU所需资源类别、基础能力、工时UOM与当前读取，不要求ROU先发布；登记资源主数据更新当前门、原结果与来源；route-specific适用性及执行许可交WP08-01。
- 当前已交付：W154资源原件/工作中心同事务、W155时间UOM和W203基础声明能力已交付。
- 真实剩余/下一步：稳定Assignment原件、准确受控能力/批准来源与当前版本尚需补全；DECLARED_NOT_QUALIFIED不视为专业资格。路线相关RES token及执行许可交WP08-01，不扩大此提供者。
- 原正验收：工程路线可引用实际资源版本，资源版本更新不会重键旧路线；未配置路线也可合法登记真实设备及其固有能力。原反验收：不能从候选路线反造设备实际能力；未知能力明确UNKNOWN；底座读取或设备绿灯不授Start/WO生产资格。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W154-01](../W154-01/DELIVERY.md)；[W155-01](../W155-01/DELIVERY.md)；[W203-01](../W203-01/DELIVERY.md)。
- 当前源码定位：[ResourceCapabilityContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/ResourceCapabilityContractV1.cs)；[ResourceCapabilityOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/ResourceCapabilityOwnerV1.cs)；[ResourceCapabilityStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/ResourceCapabilityStoreV1.cs)；[ResourceCapabilityValuesV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/ResourceCapabilityValuesV1.cs)；[ResourceCapabilityController.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.WebApi/Controllers/Mes/ResourceCapabilityController.cs)

<a id="wp09-11"></a>

### WP09-11 独立质量规格、Requirements与固定源证明

- 判定：**优先整卡核销**；原状态`PLANNED`保持。
- 原scope：建立专业标准/测量要求/不适用与可豁免属性的独立批准版本和原文来源；提供ENG CheckRun使用的准确规格只读pin/current/HoldExact，不以已冻结技术为前置；受控文档/独立批准/current source更新完整纳管；生产source解析/任务族/最终Use交WP09-01/02。
- 当前已交付：W192独立专业规格完整Requirements/NA/waivable/原文批准、current/撤回/同门HoldExact和页面均已验证；W193工程在冻结前实际采用。
- 真实剩余/下一步：优先整卡核销：原正反验收与既有PG/HTTP/UI证据逐条绑定即可，未发现需要以生产Use为前置的本卡实现缺口。正式生产解析/任务族/Use原文明确交WP09-01/02；模拟专业资料可验收。
- 原正验收：工程候选可在冻结前引用真实已批准质量规格，技术未知不改写规格本身；规格当前版/撤回和工程固定检查在共同门竞争。原反验收：未知技术不能当NA，QualityRequirements缺项不得跳过；无原批准来源的模板或简单AutoJudge不得作专业规格证明。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W192-01](../W192-01/DELIVERY.md)；[W193-01](../W193-01/DELIVERY.md)。
- 当前源码定位：[QualitySpecificationAuthorityService.Sources.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/QualitySpecificationAuthorityService.Sources.cs)；[QualitySpecificationAuthorityService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/QualitySpecificationAuthorityService.cs)；[QualitySpecificationContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Mes/QualitySpecificationContractV1.cs)

<a id="wp10-01"></a>

### WP10-01 仓库库位身份、typed capacity与初始化隔离

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：建立稳定Location/Site/层级与类型化容量规则；手工和SPACE CREATE共用初始化器：DRAFT/QUARANTINED/blocked，容量UNKNOWN；旧读写者初始化Fence纳管，正式READY_TYPED与启用分开；REUSE_EXISTING保持旧量/控制。
- 当前已交付：W206–218位置DRAFT/QUARANTINED、类型化容量/审批/时钟/库存独立控制及出库采用已交付。
- 真实剩余/下一步：SPACE CREATE与手工共享初始化器、REUSE_EXISTING保旧量/控制和所有旧写者初始化Fence尚需完整接入；不能因新位置审批完成声称SPACE路径完成。
- 原正验收：新位置不因容量技术0被当无限，typed数据齐证才进入独立启用流程。原反验收：缺InitializationFenceCapability零正式创建；不能REUSE时清预占或容量；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W206-01](../W206-01/DELIVERY.md)；[W212-01](../W212-01/DELIVERY.md)；[W213-01](../W213-01/DELIVERY.md)；[W214-01](../W214-01/DELIVERY.md)；[W215-01](../W215-01/DELIVERY.md)；[W216-01](../W216-01/DELIVERY.md)；[W217-01](../W217-01/DELIVERY.md)；[W218-01](../W218-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp10-03"></a>

### WP10-03 库存唯一数量过账、移动与原结果恢复

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：实现原WriteKind合法来源RECEIPT/MOVE/CONSUME/SHIP/调整等数量效果与原回执；物理量/来源预算/容量/质量/用途/审计outbox同QuantityUoW；manual/import/job/legacy入同门；未知commit以原command/Stock操作查回。
- 当前已交付：ADJ/MOVE原操作、未知恢复及实际Stock/IN/调拨/退货入口已有多轮共同事务与当前权限验证。
- 真实剩余/下一步：完整WriteKind合法来源、RSV预算/容量/质量/用途同QuantityUoW及manual/import/job/legacy所有写者继续补；不重跑已稳定Stock恢复链或缩小数量合法域。
- 原正验收：合法多腿移动同Root守恒，任一腿失败整体回滚；已提交丢确认只回原receipt。原反验收：同品码但Owner/技术/UOM不同不得混账；不可负量、无源补货、默认QA PASS或截断占用差异；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W204-01](../W204-01/DELIVERY.md)；[W98-01](../W98-01/DELIVERY.md)；[W137-01](../W137-01/DELIVERY.md)；[W140-01](../W140-01/DELIVERY.md)；[W230-01](../W230-01/DELIVERY.md)；[W236-01](../W236-01/DELIVERY.md)；[W239-01](../W239-01/DELIVERY.md)；[W249-01](../W249-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[IStockMovementService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/IStockMovementService.cs)；[StockMovementService.Creation.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/StockMovementService.Creation.cs)；[StockMovementService.Inheritance.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/StockMovementService.Inheritance.cs)；[StockMovementService.MoveMetadata.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/StockMovementService.MoveMetadata.cs)；[StockMovementService.OriginalOperations.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/StockMovementService.OriginalOperations.cs)

<a id="wp10-10"></a>

### WP10-10 调拨Request/Stage、来源预算与本地移动Owner

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：建立SourceDemand、Request/Stage和准确来源/目的/在途三个坐标；签发有权现场指令并绑定完整Selection、全部RSV Use和StockPrepareCall；本地MOVE整组同Stock/来源/容量/质量UoW，提供CP22/SLOT/REP原创建/查询/永久关闭。
- 当前已交付：可信SourceDemand/Request及直接MOVE阶段W142/205/235/236已实现，REP执行W239已消费。
- 真实剩余/下一步：在途TRANSIT坐标、完整Selection/全部RSV Use与StockPrepareCall、现场指令/实际事实及所有来源类型继续接入；现MOVE正例不等全Transfer。
- 原正验收：需求30可事前Stage20+10，首20未知不再复制；单stage多Root作为一原子组。原反验收：已有WO/MAT Issue/Return身份仅MatLink不得走Transfer第二数量入口；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W142-01](../W142-01/DELIVERY.md)；[W215-01](../W215-01/DELIVERY.md)；[W216-01](../W216-01/DELIVERY.md)；[W235-01](../W235-01/DELIVERY.md)；[W236-01](../W236-01/DELIVERY.md)；[W237-01](../W237-01/DELIVERY.md)；[W238-01](../W238-01/DELIVERY.md)；[W239-01](../W239-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[OperationsAccessV1.TransferRequest.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.TransferRequest.cs)；[TransferRequestContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/Transfer/TransferRequestContractV1.cs)；[TransferRequestOwnerV1.Actions.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/Transfer/TransferRequestOwnerV1.Actions.cs)；[TransferRequestOwnerV1.Sources.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/Transfer/TransferRequestOwnerV1.Sources.cs)；[TransferRequestOwnerV1.Stages.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/Transfer/TransferRequestOwnerV1.Stages.cs)

<a id="wp10-19"></a>

### WP10-19 出库授权、拣配暂存包装与真实发运Owner

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：建立准确Source授权、RSV Allocation→Pick→Stage→Package→Shipment链；真实scan/封箱/称重与Stock发运同最终门，原源份额不重用；提供采购退运正式Part的具名来源入口和Shipment原结果，不代商业批准。
- 当前已交付：位置分配守卫W218，准确订单行/Shipment原件W222/223已交付。
- 真实剩余/下一步：完整RSV Allocation→Pick→Stage→Package→Shipment、scan/封箱/称重与Stock同最终门，以及采购退运Part具名来源仍需实施。
- 原正验收：发运按准确Allocation产生真实Shipment行/Stock SHIP，重复一次；拣配不等已发。原反验收：授权unknown留原grantRequestRef不造grant；缺质量/技术/当前权阻新发运；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W218-01](../W218-01/DELIVERY.md)；[W222-01](../W222-01/DELIVERY.md)；[W223-01](../W223-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp10-22"></a>

### WP10-22 客退授权、原发运行预算与分批收货

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：建立RMA Case/Part准确E/A/P entitlement范围与独立授权，消费WP04-34原商业发运行/可退来源；分批实收至正式待检位置，Stock/IN/RMA Physical及H30原source/claim身份同资源计划提交；此处不预签FININV effect成功proof；提供NCR/FININV真实RMA已收source/current，保原Shipment/Delivery及独立退回事实；完整财务关闭留WP10-23。
- 当前已交付：W220–233 RMA来源/申请/entitlement/授权/实收IN/质量/处置/更正和W248独立商业/财务观察已交付。
- 真实剩余/下一步：非空例外批准路径、完整H30原source/claim共同资源计划及NCR/FININV实际消费者尚需闭合；财务全部关闭归WP10-23，不能以观察POSTED冒充内部Finance已实现。
- 原正验收：发100退20仍历史发100/另记退20；并发两退货不能超原可退预算。原反验收：Create不得自动Authorized；不能自动重开Demand或把实收等同质量PASS；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W220-01](../W220-01/DELIVERY.md)；[W228-01](../W228-01/DELIVERY.md)；[W229-01](../W229-01/DELIVERY.md)；[W230-01](../W230-01/DELIVERY.md)；[W231-01](../W231-01/DELIVERY.md)；[W232-01](../W232-01/DELIVERY.md)；[W233-01](../W233-01/DELIVERY.md)；[W248-01](../W248-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[RmaFormalOwnerV1.Authorization.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.Authorization.cs)；[RmaFormalOwnerV1.AuthorizationStore.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.AuthorizationStore.cs)；[RmaFormalOwnerV1.Commercial.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.Commercial.cs)；[RmaFormalOwnerV1.Correction.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.Correction.cs)；[RmaFormalOwnerV1.DeliveryEvidence.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/RmaFormalOwnerV1.DeliveryEvidence.cs)

<a id="wp10-31"></a>

### WP10-31 补货调拨执行、取消和完整Stage恢复

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：唯一Transfer/Stock路线派发补货并存原businessKey；采用完整stageRefs/receiptRefs及covered/remaining状态；取消核全部Stage和已发生，旧任务族显式迁移并保持source-task一致性。
- 当前已交付：W237/238规划和W239原Transfer/Stock直接MOVE执行/完成/停止及原键恢复已交付。
- 真实剩余/下一步：多阶段/在途、部分covered/remaining与已发生后的取消、旧任务族显式迁移及source-task一致性仍需完善；不等待真实补货数据。
- 原正验收：真实移动5后源减5目标加5，原另在途2独立；重复receipt不重复完结。原反验收：派发5不能当已到5；只查首stage/换键补派未知任务拒绝；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W235-01](../W235-01/DELIVERY.md)；[W236-01](../W236-01/DELIVERY.md)；[W237-01](../W237-01/DELIVERY.md)；[W238-01](../W238-01/DELIVERY.md)；[W239-01](../W239-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.Replenish.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.Replenish.cs)；[IReplenishService.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/IReplenishService.cs)；[ReplenishPlanningCalculatorV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/Replenish/ReplenishPlanningCalculatorV1.cs)；[ReplenishPlanningContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/Replenish/ReplenishPlanningContractV1.cs)；[ReplenishPlanningOwnerV1.Execution.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/Replenish/ReplenishPlanningOwnerV1.Execution.cs)

<a id="wp10-36"></a>

### WP10-36 版模实物使用、维护周期与退役

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：消费WP10-48原WMS实例及WP05-06双Role关联，核准确ENG技术身份/当前用途；追加不可变使用/维护事实及当前计量周期，维护不抹累计事实或自动恢复全部资格；实际搬位/报废通过Transfer/Stock/Finance原Owner，保原结果和迟到恢复。
- 当前已交付：W165双角色关联、W166–168使用维护追加事实/周期、W169意图及W176/184审批回执已交付。
- 真实剩余/下一步：真实搬位/报废仍需Transfer/Stock/Finance原Owner应用和迟到恢复，准确技术当前用途与外部使用原件继续补；不得把APPLIED工作流回执当库存/财务业务效果。
- 原正验收：维修新周期计数可重启但累计使用事实不清零；技术撤回阻后续新使用。原反验收：旧PLT2号不自动等工程号；CompleteMaintenance不直接恢复所有技术/质量资格；跨租户/当前无权的新效果被拒；已提交原件不删除，原结果输出重核当前披露权；UNKNOWN不得换键重执。。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W176-01](../W176-01/DELIVERY.md)；[W165-01](../W165-01/DELIVERY.md)；[W166-01](../W166-01/DELIVERY.md)；[W167-01](../W167-01/DELIVERY.md)；[W168-01](../W168-01/DELIVERY.md)；[W169-01](../W169-01/DELIVERY.md)；[W184-01](../W184-01/DELIVERY.md)。
- 当前源码定位：[ToolApplicationDeliveryV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/ToolApplicationDeliveryV2.cs)；[ToolApplicationOwnerV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/ToolApplicationOwnerV2.cs)；[ToolApplicationReceiptStoreV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/ToolApplicationReceiptStoreV2.cs)；[ToolMaintenanceContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/ToolMaintenanceContractV1.cs)；[ToolMaintenanceOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/ToolMaintenanceOwnerV1.cs)

<a id="wp10-47"></a>

### WP10-47 既有物料、UOM与容量维度原Owner目录适配

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：为BOM/CFG/Stock/MASTER提供既有经营物料、Site/位置和UOM稳定身份/原版本读取；固定原Owner批准的单位转换/量纲/容量metric定义与当前控制，不建第二份可写主账；真实原源不可用/歧义保持UNKNOWN，正式源由受信配置固定；旧数据读接口不要求新工程基线已发布。
- 当前已交付：时间UOM、BOM材料及库存身份/单位来源W155/187/204已有交付。
- 真实剩余/下一步：原Owner批准的完整单位转换/量纲/容量metric及当前控制尚需提供；准确物料/Site/位置身份按原主账读取，歧义UNKNOWN，不建第二套可写WMS字典。
- 原正验收：准确经营料号/UOM与批准转换可在工程冻结、Stock新过账之前被BOM引用；同原版本量义无损，查询当前授权不扩大。原反验收：未知来源/转换不得补默认系数；不得把WMS-MASTER声明成所有产品的新Owner；原合法单位与精度不截断，跨租户或无权读取拒绝。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W155-01](../W155-01/DELIVERY.md)；[W187-01](../W187-01/DELIVERY.md)；[W204-01](../W204-01/DELIVERY.md)。
- 当前源码定位：[OperationsAccessV1.InventoryIdentity.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.InventoryIdentity.cs)；[InventoryIdentityContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/InventoryIdentityContractV1.cs)；[InventoryIdentityOwnerV1.Sources.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/InventoryIdentityOwnerV1.Sources.cs)；[InventoryIdentityOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/InventoryIdentityOwnerV1.cs)；[InventoryIdentityStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/InventoryIdentityStoreV1.cs)

<a id="wp10-48"></a>

### WP10-48 WMS版模实例身份、当前头与HoldExact提供者

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：登记原WMS实物版模稳定身份/当前版本/独立计量周期和真实来源；提供ENG双Role所需实例current/provenance/HoldExact原事务参加者；按旧ENG技术号原义读取可用规格候选，正式关联、生产使用和退役仍交WP05-06/WP10-36。
- 当前已交付：W160–164实物实例/合作方/来源原件，W165真实HoldExact/current，W167/168计量周期均已采用。
- 真实剩余/下一步：核完整旧ENG技术号候选读取与provenance适用域、外部来源原件及current版本覆盖；未知历史不能假造。正式关联/生产使用/退役归WP05-06/WP10-36，不能据此扣留本提供者。
- 原正验收：ENG在本提供者和技术Owner同当前轮次可完成双Role关联；实例信息变更与HoldExact竞争有唯一当前结果。原反验收：不因登记实例创建Stock Root或零凭据实物余额；不自签技术发布；全WMS工具使用/退役未交付时不得对外声称已具完整可用资格。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W160-01](../W160-01/DELIVERY.md)；[W166-01](../W166-01/DELIVERY.md)；[W167-01](../W167-01/DELIVERY.md)；[W168-01](../W168-01/DELIVERY.md)；[W169-01](../W169-01/DELIVERY.md)；[W176-01](../W176-01/DELIVERY.md)；[W182-01](../W182-01/DELIVERY.md)；[W184-01](../W184-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[OperationsAccessV1.ToolInstance.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Integration/OperationsAccessV1.ToolInstance.cs)；[ToolInstanceContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/ToolInstanceContractV1.cs)；[ToolInstanceOwnerV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/ToolInstanceOwnerV1.cs)；[ToolInstanceStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wms/ToolInstanceStoreV1.cs)；[ToolInstanceController.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.WebApi/Controllers/Wms/ToolInstanceController.cs)

<a id="wp11-32"></a>

### WP11-32 应收退货贷项与库存价值独立协调

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：原票逐行价税/批准数量、RETURN或PRICE_ALLOWANCE原额度；精确ERP-CREDIT/RMA/QA/INV准入，INV唯一COGS/Inventory source；全当前结果后CreditFact/余额/预算消费，partial保claims与撤权全链闭合；与AR Source目录共用最终事务发布对应不可变事实/当前头/frontier；不能先提交余额后异步声称完整来源。。
- 当前已交付：W248已实现RMA商业请求/不可变财务观察与当前授权，实际PG/HTTP采用持久模拟外部ERP/Finance。
- 真实剩余/下一步：内部AR CreditFact/余额/预算与source/frontier共同最终事务、原票逐行精确额度及INV唯一COGS/Inventory来源仍未由观察桥替代；partial claims与撤销全链继续实现。
- 原正验收：实收20但批准15只贷15；价值160不另减应收200；额度不足且已有核销时要求先正规反核销。原反验收：不能按整票均摊取代原LayerConsumption；待质量/INV未知不自动把全退回变可售。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W248-01](../W248-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp11-33"></a>

### WP11-33 应收退款授权、资金事实与撤销恢复

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：接精确原贷项/RefundAuthorization/current source及合法退款额度；真实Funds退款记录/会计/AR结果分开，原执行key与完整别名预算；已执行退款不可借撤销假删除，未知持原claim与逐原件恢复。
- 当前已交付：W248区分退款授权、现金POSTED观察和商业批准，并保原键/来源。
- 真实剩余/下一步：真实Funds退款执行记录/会计/AR分轴、执行key完整别名预算/原claim与撤销和未知恢复仍需实现；外部模拟结果不是本产品资金账。
- 原正验收：仅真实退款到账/出款原证明推进资金轴；原贷项撤回先闭合全部后继，不擅恢复可退余额。原反验收：新退款提议不是PaymentInstruction；未观察到不能再次退款或释放原额度。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W248-01](../W248-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp12-01"></a>

### WP12-01 不可变流程表单草稿与设计器版本

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：复用FlowDef/Form设计器，建立head与不可变版本，显式新建/克隆/建草稿/保存各原命令+CAS；GET不写草稿；复制固定sourceVersion清身份码，不复制实例历史；往返完整图/schema布局，服务端允许未完草图但拒未知可执行类型、致命结构和越权字段。
- 当前已交付：W170/172不可变FLOW草稿和W173/174发布/激活已经实现，GET不隐式写草稿。
- 真实剩余/下一步：克隆固定sourceVersion并清身份、FORM完整schema/layout及设计器往返/未知可执行类型和致命结构约束尚需全面覆盖；不复制实例历史。
- 原正验收：克隆只产生新disabled定义和明确来源；重复原命令不多草稿；已有已发布版不变，UI保合法布局/表单属性及稳定节点/边ID。原反验收：读取不能暗建draft；缺CAS不覆盖已有head；表单条件脚本不得任意执行，前端可见/disabled不能授写权。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W170-01](../W170-01/DELIVERY.md)；[W172-01](../W172-01/DELIVERY.md)；[W173-01](../W173-01/DELIVERY.md)；[W174-01](../W174-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp12-02"></a>

### WP12-02 定义发布预览、准确依赖pin与独立启停

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：发布预览绑定draft/schemaDigest、表单/子流程完整依赖，提交时同WF_AUTH fence重核；原子写发布版/依赖pin/receipt/audit；activation独立历史/唯一form-flow绑定CAS；停用只阻新根申请，在途实例保原pin；设计器旧publish/enable同门。
- 当前已交付：准确草稿发布预览、当前权限/原结果与激活CAS已有交付。
- 真实剩余/下一步：FORM/子流程完整依赖pin和唯一form-flow绑定CAS仍需补全；OA APP binding不是FORM-FLOW绑定，在途保原pin及旧入口同门继续核。
- 原正验收：发布成功仍不自动enabled；切换head不改历史实例pin；同键响应丢失恢复同发布版；两个激活竞争只有合法唯一binding。原反验收：过时预览、依赖漂移或撤权竞争不得发布；不得暗中停另一个活动flow，停用不假称取消在途。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W174-01](../W174-01/DELIVERY.md)；[W173-01](../W173-01/DELIVERY.md)。
- 当前源码定位：源码职责和准确采用定位见上述交付报告；本轮未据文件名推定实现覆盖。

<a id="wp12-06"></a>

### WP12-06 审批业务绑定与冻结提交TxS/TxE

- 判定：**合同/验收待核**；原状态`PLANNED`保持。
- 原scope：治理可信Owner/subjectKind/服务路由、binding/policy不可变版与activation时间线；冻结Subject/Snapshot/FrozenInput、SubmissionCore/FullRequestHash/Intent和alias；原AppCommand唯一；TxS提交Submission/StartOutbox；TxE用SubmissionId作为engineStartId持久启动与查回，注册真实Owner能力。
- 当前已交付：W175–181可信binding/路由、TxS/TxE原生启动恢复已交付，后续DOC/ReqSet/ITER真实业务也已消费。
- 真实剩余/下一步：核原subjectKind/身份合法域、完整alias与legacy callback冲突采用；不得把所有业务消费者未完成当协议提供者未实现。仅补当前native UUID profile未覆盖的真实合同路径。
- 原正验收：相同提交键+内容返同Submission；引擎启动响应丢失按原键只一实例；无实例业务详情可在当前业务读权下打开，页面不伪造回执。原反验收：浏览器不能换Owner/Tenant/callback；快照/Scope/策略不匹配拒发起；技术配置存在不证明实际Owner已采用；旧同步回调不得旁路新路由。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W175-01](../W175-01/DELIVERY.md)；[W178-01](../W178-01/DELIVERY.md)；[W179-01](../W179-01/DELIVERY.md)；[W180-01](../W180-01/DELIVERY.md)；[W181-01](../W181-01/DELIVERY.md)；[W191-01](../W191-01/DELIVERY.md)；[W196-01](../W196-01/DELIVERY.md)；[W199-01](../W199-01/DELIVERY.md)；其余既有采用引用见本卡roadmap.scope_reconciliation.evidence_refs。
- 当前源码定位：[20260615162917_OaStage2ApprovalBinding.Designer.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Migrations/20260615162917_OaStage2ApprovalBinding.Designer.cs)；[20260615162917_OaStage2ApprovalBinding.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Migrations/20260615162917_OaStage2ApprovalBinding.cs)；[ApprovalBindingContractV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalBindingContractV2.cs)；[ApprovalBindingOwnerV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalBindingOwnerV2.cs)；[ApprovalSubmissionContractV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalSubmissionContractV2.cs)

<a id="wp12-07"></a>

### WP12-07 审批决定TxD、Owner交付与回执连续链

- 判定：**优先整卡核销**；原状态`PLANNED`保持。
- 原scope：提供同引擎终态事务参加的不可变Decision+DecisionOutbox recorder；可信路由投递原body，Owner接收/应用分阶段；应用必须业务+原结果+receipt同提交；TxR仅消费Owner ApplicationReceipt，连续revision/previousReceiptId链、GAP/同序异hash隔离与重放。
- 当前已交付：W181 TxD、W183投递、W184Owner receipt及W185连续链/GAP恢复已交付；W191/196/200已有业务真实应用。
- 真实剩余/下一步：优先整卡核销原Decision/Outbox、Owner应用与TxR正反验收；工具报废/借用等具体业务由各域卡完成，不能成为此协议底座的无限完成门。完整合同身份范围仍按准确已有测试核对。
- 原正验收：决定成功落库同时有可恢复outbox；Owner已应用回执丢失只重送；最高连续回执才推进展示，APPLIED保真实Owner/action/result/time。原反验收：HTTP ACK/Approved/Inbox收件不能显示业务Applied；回执缺前驱或同序异hash不能跨过；OA不能再驱动同一业务效果。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W183-01](../W183-01/DELIVERY.md)；[W185-01](../W185-01/DELIVERY.md)；[W181-01](../W181-01/DELIVERY.md)；[W184-01](../W184-01/DELIVERY.md)；[W191-01](../W191-01/DELIVERY.md)；[W196-01](../W196-01/DELIVERY.md)；[W200-01](../W200-01/DELIVERY.md)。
- 当前源码定位：[ApprovalApplicationReceiptV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalApplicationReceiptV2.cs)；[ApprovalDecisionDeliveryV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalDecisionDeliveryV2.cs)；[ApprovalDecisionSourceV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalDecisionSourceV2.cs)；[ApprovalDecisionWireV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalDecisionWireV2.cs)；[FlowEngine.ApprovalDecisionV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Wf/FlowEngine.ApprovalDecisionV2.cs)

<a id="wp12-19"></a>

### WP12-19 可信业务撤回与阻塞回执恢复

- 判定：**明确实现余项**；原状态`PLANNED`保持。
- 原scope：Owner可信withdraw输入保原actor、subject/binding、scope和原事实，不接受worker任意选人；撤回与Owner apply共享control CAS，OA运输重排不授业务动作权；BLOCKED恢复追加receipt revision/previousReceiptId，终态APPLIED/STALE/CONFLICT不改写。
- 当前已交付：W200真实非委派撤回、Owner apply同控制CAS及两种竞争顺序已交付，终态不可改写。
- 真实剩余/下一步：委派撤回与BLOCKED新actor/新grant的明确再授权和连续回执尚未完整实现；worker不能自选人员替代授权，不重做已通过非委派竞争。
- 原正验收：先应用后撤回给原应用结果，先撤回后迟到批准不生效；原角色/原grant当前仍有效才继续原阻塞应用，换人必须Owner新请求。原反验收：callback.replay不代apply权限；管理员不能强制通过；worker无权代换Actor，缺共同fence不得宣称withdraw/apply互斥。以上未被本次文档审计改称新执行通过。
- 采用和复用来源：[W200-01](../W200-01/DELIVERY.md)。
- 当前源码定位：[ApprovalWithdrawalOwnerV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalWithdrawalOwnerV2.cs)；[ApprovalWithdrawalV2.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Oa/ApprovalWithdrawalV2.cs)；[IterationReviewApplicationStoreV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/IterationReviewApplicationStoreV1.cs)；[IterationReviewCanonicalV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/IterationReviewCanonicalV1.cs)；[IterationReviewContractV1.cs](https://github.com/GTX537/CP6/blob/91a0eea66c25109141c410831d0030782480923c/CP6.Core/Services/Plm/IterationReviewContractV1.cs)

## 本轮验证、审查和交付边界

文档核对包括：59个唯一ID与原375清单对应；原scope/正反验收保持；补读后续关联交付与当前源码；125/247及全部原任务状态不变；本轮报告引用存在；生成页与唯一台账一致；完整分支diff和新增链接检查。一次作者SPEC/QUALITY集中审查包含身份/租户/精度/事务边界、模拟适用、旧证据与本轮执行区别、无关后续范围剔除；没有产品代码变更，因此不编译、不新增镜像测试、不触发Actions。

本报告不把“尚未定位证据”写成已确认代码缺陷。特别纠正：CRM已有切组织注销旧会话；DOC已有专业批准；版模已有双Role及维护/使用事实；质量规格无需等待ENG冻结；OA底座无需等待所有业务实际报废。原375仍需后续工作，不能以本次59张审计称整个项目完成。

固定DEV-1/Main与DEV-2/Handoff由官方start登记，INTEGRATION-1未占用，根目录用户原件/缓存保持。正常PR、远端采用、准确分支清理及官方finish后结束本轮；提交前快照与最终采用状态分开，最终证明为`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave250-final-verification.json`。本轮无Actions取消/运行、触发修改、保护绕过、既有数据切换或生产部署。工作流/依赖内容与已审查基线相同，普通入口保持手动，生产tag门禁保留。


提交前快照：Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/226)已准确采用、分支清理并释放DEV-2；Main待正常PR合并及finish。
