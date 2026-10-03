# WP3 调用方矩阵与后续业务边界

日期：2026-10-03。任务基线为已确认远端 main `605246ca83cbb8e1a89d3ff7209ba9df65176e98`，分支 `codex/db-compat-wp3-20261003`。下表按最终 WP3 实现整理；资源表来源为 2026-10-02 的只读 `wp3-runtime-inventory.md`（原观察 HEAD `d507f389fdcadd8e2fc5dcb9e40f752cde613703`），其旧行号、PG 跳过分支描述仅属于历史盘点。完整实际结果、原始失败及复用范围见 [WP3 实施记录](WP3-IMPLEMENTATION.md)；主任务 [Issue #134](https://github.com/GTX537/CP6/issues/134) 仍 Open，普通 PG 应用启动门禁保留。

## 本阶段覆盖

| 范围 | 已实现与真实验证边界 | 后续业务归属 |
| --- | --- | --- |
| 16 个原应用锁入口 | 统一 Transaction/Session 能力；SQL 保留资源身份、等待值，PG 使用稳定 advisory key；两库锁/时钟原生报告各 5/5。启动筛选实际 30P/2Skip 后，另用显式 SQL 连接执行两个原生启动测试 2P/0Skip。 | WP4 身份/ERP；WP5 Space 租约、幂等、完整事务竞争。primitive 通过不等于所有消费者已通过。 |
| 6 个嵌套 Context factory | 明确 provider/profile、同物理连接/事务、non-owning；两库共享门禁各 3/3（含 owner/history），七种子 Context 写入、失败/正常 dispose 与全回滚。 | 原身份/ERP/OIDC 业务 SQL 与 guard 保留；完整业务由 WP4/WP5 验收。 |
| 6 个 Space 数据库 UTC helper | SQL `SYSUTCDATETIME()`、PG 实际推进的数据库 UTC；测试 provider 保留原 clock fallback。 | 完整 lease fence/业务日期语义属 WP5；未改 GETDATE/wall-clock 字段。 |
| ORD 与非 ORD | 保留全局 FuncCode、即刻独立 reservation/加入调用方事务、手工 pending 拒绝；两库原子编号原生各 7/7，补充各 5/5。非 ORD 同 Local 延迟保存不变。 | ORD 的完整业务订单链及其他编号器竞争属 WP4；不扩大为所有编号服务已兼容。 |
| Identity generation/v2 cursor | SQL 生命周期 3/3、PG 生命周期及同页快照 4/4；真实子进程续读/变更拒绝，PG 两会话提交后同页旧 generation/版本，下页拒绝。 | 身份写入、bootstrap、撤销与消息投递完整事务属 WP4。 |
| AI work-slot Acquire | 生产 ledger 的首次创建、max1/max2 双会话、同 run、到期与 owner/token/run fence、租户独立、有限预算 skip-locked；两库实际各 10/10。 | 原 Renew/Release、budget 与完整生成/恢复属 WP5；仅 Acquire 适配不外推为整个容量 ledger 兼容。 |

上述计数是各份独立实际报告的总项数，包含报告自身 owner/history 等前置项，不拼成新的执行总数。四个新增错误分类调用方只做下述 predicate 替换；通用 classifier 的实际原生约束/事务门禁作为基础覆盖。另有14条真实service SaveChanges异常注入回归，随Core54/54、Space22/22相关类零跳过通过，验证错误映射与透传；调用方完整真库业务验收仍在WP4/WP5。其余Provider条件路径逐一登记于[Provider分支清单](WP3-PROVIDER-BRANCHES.md)。

## 锁资源与生命周期（原 16 处）

表内 `Space*.cs` 均在 `CP6.Space.Infrastructure/`。原资源大小写、Guid 格式、global/tenant 范围、参数 hash 与锁顺序保留；同一 floor-edit 键由六个入口共享。

| Owner/helper | 精确资源模板 | 等待与所有权 |
| --- | --- | --- |
| `CP6.Core/Services/ErpIntegration/ErpSqlLock.cs` | 调用方传入下表的完整原字符串 | 10000 ms；Transaction |
| `SpaceEditLeaseService.cs` | `cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N}` | 15000 ms；Transaction |
| `SpaceDesignV1Service.cs` floor edit | `cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N}` | 15000 ms；Transaction |
| `SpaceDesignV1Service.cs` floor initialization | `cp6:space:version-floor-init:{tenant:N}:{version:N}` | 15000 ms；Transaction |
| `SpaceUnderlayHistory.cs` | `cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N}` | 15000 ms；Transaction |
| `SpaceExcelCadApplyService.cs` | `cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N}` | 15000 ms；Transaction |
| `SpaceExcelCadApplyJobStepExecutor.cs` | `cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N}` | 15000 ms；Transaction |
| `SpaceCadParseService.cs` floor edit | `cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N}` | 15000 ms；Transaction |
| `SpaceCadParseService.cs` parse | `cp6:space:cad-parse:{tenant:N}:{source:N}` | 15000 ms；Transaction |
| `SpaceAiAtomicApplyService.cs` | `cp6:space:ai-apply:{tenant:N}:{run:N}` | 15000 ms；Transaction |
| `SpaceAiRetentionStore.cs` | `cp6:space:ai-retention:{tenant:N}` | **0 ms**；Transaction，保留立即尝试 |
| `SpaceCadProviderCapabilityService.cs` | `space:cad-provider:{tenant:N}:{site:N}` | 15000 ms；Transaction |
| `SpaceValidationInfrastructure.cs` | `CP6:Space:Validation:{tenant:D}:{version:D}` | 15000 ms；Transaction |
| `SpaceHistoricalRepublishService.cs` | `CP6:Space:Republish:{tenant:D}:{Hash(normalizedKey)}` | 15000 ms；Transaction |
| `CP6.WebApi/Seed/SpaceAuditPermissionSeed.cs` | `CP6:Seed:SpaceAuditPermission:v1` | 15000 ms；Transaction，全局 seed |
| `CP6.WebApi/BackgroundServices/SpaceIntegrationEventOccurredAtUtcBackfill.cs` | `CP6:SpaceIntegrationEvent:OccurredAtUtc:v1` | 30000 ms；**Session**，跨批次持有并 finally 释放 |

原有超时域错误保留；Validation/Republish 原 SQL THROW 51000/51021 改为带同一 `SPACE_*_LOCK_UNAVAILABLE` 原因码的 `InvalidOperationException`。ERP 原 `C03_SQL_CONTENTION` 原因码保留。typed deadlock 与取消由统一能力表达，未知完整业务失败不自动重试。

ERP helper 的八处直接调用（Core 路径均为 `CP6.Core/Services/ErpIntegration/`）：

| 调用方 | 原精确资源与顺序 |
| --- | --- |
| `ErpRequestHandler.cs` message | `c03:message:` + SHA256(MessageId)，原全局 MessageId 范围，先于 aggregate |
| `ErpRequestHandler.cs` aggregate | `c03:aggregate:{tenant:D}:{kind:int}:{aggregate:D}`，在可缺失 Aggregate 的首次读取之前 |
| `ErpInboxReplayService.cs` operation | `c03:replay:{tenant:D}:{OperationId:D}`，先于 message |
| `ErpInboxReplayService.cs` message | 与 command processor 相同的 `c03:message:` + SHA256(MessageId) |
| `ErpDeliveryReplayService.cs` tenant | `c03:delivery-replay:{tenant:D}`，在可缺失 operation/audit 读取之前 |
| `ErpDeliveryReplayService.cs` result | `c03:delivery-result:{tenant:D}:{outboxId:D}`，在 Outbox 读取之前 |
| `ErpDeliveryReplayService.cs` bridge | `c03:bridge:{tenant:D}:{orderKey}`，在 dispatch 读取之前，tenant 锁先于 bridge |
| `CP6.WebApi/BackgroundServices/ErpOrderBridgeWorker.cs` | 相同 `c03:bridge:` 键；保留 ReadCommitted 外层事务及独立提交的 WMS/MES hooks，不能盲重试外部效果 |

## 共享 Context 与时钟调用方

| 六个 factory | source → target 与边界 |
| --- | --- |
| `CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs` | Core → IdentityMessagingContext，priority profile；原业务事务与快照/入队绑定 |
| `CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs` | ERP → Core；同连接/事务，原租户 scope |
| `CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs` | ERP → Core；同连接/事务，原 replay/audit scope |
| `CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs` | ERP → Core；有事务时加入，否则共享调用方连接；原 replay/result/bridge scope |
| `CP6.WebApi/Services/CrmOidcGrantStore.cs` | 显式 `DatabaseOptions(SqlServer)` + 现有 DbConnection/DbTransaction → Core；此 store 的业务连接/SQL 仍属 SQL 实现 |
| `CP6.Space.Infrastructure/Cp6SpaceRuntimeMaterializer.cs` | Space → Core；同连接/事务，原租户 scope 与 publishing/runtime 投影 |

六个数据库 UTC helper 位于 `SpaceEditLeaseService`、`SpaceDesignV1Service`、`SpaceUnderlayHistory`、`SpaceExcelCadApplyService`、`SpaceExcelCadApplyJobStepExecutor`、`SpaceCadParseService` 的 `ReadAuthoritativeUtcNowAsync`。具体 lease 入口与并发 fence 属后续业务验收；没有把进程内 clock 证明等同于数据库时钟证明。

## SQL 错误分类调用方：四处最小替换、四处后续迁移

| 调用方 | WP3 当前处理 | 完整业务边界 |
| --- | --- | --- |
| `ErpCommerceAuthority.cs` account binding | 原 `DbUpdateException` catch，要求 `UniqueConstraint` 且精确 ordinal 名称 `IX_T_WebBusinessPartner_TenantId_CrmAccountId`，仍抛 `C03_ACCOUNT_ALREADY_BOUND`；未知名称、其他 unique/FK/check 不改译。 | WP4 binding/并发与业务事务验收。 |
| `SpaceFieldPolicyService.cs` Save | 原 `DbUpdateException` catch，仅 `UniqueConstraint`；保留原 all-unique 语义、FieldPolicyConflict/409 与 recoveryAction；不增加回查/重试。 | WP5 policy 唯一约束与业务事务。 |
| `SpaceExternalOrganizationService.cs` Save | 同上，保留调用方传入 conflictCode/title、409、reload-current-resource 及独立状态错误 catch。 | WP5 organization/membership 业务。 |
| `SpaceExternalGrantService.cs` Save | 同上，保留 ExternalGrantConflict/409、reload-current-grants。 | WP5 grant 业务。 |
| `EfSpaceVersionClone.cs` | **未改 classifier/retry**；原 1205 最多三次与 unique 回查保留。 | WP5 整个 owned Serializable 事务 rollback/dispose、tracker clear、重读/幂等与 40001 策略；不能以 `CanRetryTransaction` 直接扩大原重试范围。 |
| `SpaceExcelMappingService.cs` | **未改**原 unique → rollback → clear → ReadReplay。 | WP5 同 key/same input replay、不同 input conflict 与 rollback 后可用 Context。 |
| `SpaceCadMappingProfileService.cs` | **未改**原 unique → rollback → clear → ReadReplay。 | 同 Excel；需要完整原生幂等事务验收。 |
| `EfSpaceAiCapacityLedger.cs` budget/Renew/Release | Acquire 单独按上述范围适配；原 budget/classifier 与其他方法保持。 | WP5 预算、领取/续租/释放/生成恢复完整流程；禁止失败 PG 事务内重试单条语句。 |

分类器实际 SQL 英文/简体中文固定约束模板与 PG SQLSTATE 已验证；未知 SQL locale 保留未知名称。没有用任意 message substring、全部 `DbUpdateException` 或 optimistic concurrency 作为自动安全重试条件。WMS/DeadLetter 原 conditional UPDATE fence、其他编号器、身份写入/OIDC/ERP 的原生业务 SQL、Space generation/file-reference/query-byte 契约均按原计划交给 WP4/WP5，不因本阶段能力可用而删除其 guard 或声明完整兼容。
