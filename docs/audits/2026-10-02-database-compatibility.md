# DB-COMPAT-01：SQL Server / PostgreSQL 兼容性源码盘点

- 日期：2026-10-02。
- 用途：记录已接受的双数据库兼容任务基线、源码证据、风险和验收缺口，供实现与集中审查使用。
- 状态：**源码盘点与已接受任务基线；非功能完成、非 PostgreSQL 验收、非生产发布凭据**。
- 主任务：[GitHub Issue #134：DB-COMPAT-01](https://github.com/GTX537/CP6/issues/134)，Open，enhancement。
- 已确认源码基线：`157630594e3371fe181955d2f6227ff3b6962c84`。
- 设计：[数据库兼容设计](../superpowers/specs/2026-10-02-database-compatibility-design.md)。
- 执行计划：[数据库兼容实施计划](../superpowers/plans/2026-10-02-database-compatibility.md)。

## 1. 已确认目标与边界

同一套 CP6 代码兼容 SQL Server 和 PostgreSQL，每次部署选择一个 provider。同一部署中的业务、Space、Identity messaging 和 ERP integration 上下文必须使用该部署选定的数据库 provider；涉及原子业务写入、Inbox/Outbox 和审计时，保留现有同连接、同数据库、同事务的关系。

现有环境已有 PostgreSQL 18 服务运行的只读环境证据；本次未连接该服务，未读取数据库目录、Schema、数据或凭据，也未执行 SQL。服务运行不证明 CP6 已接入 PostgreSQL。当前跟踪的 CP6 项目源码未发现 Npgsql 包接线或 `UseNpgsql` 注册的证据，现有 SQL Server 配置不能作为 PostgreSQL 验收。

本次任务建档仅修改文档。盘点没有执行编译、测试、数据库连接、迁移、数据库覆盖、部署或 GitHub Actions。统计与发现来自静态源码检查，不将代码中的测试类、历史 SQL Server 成功记录或 SQLite/InMemory 结果改称本次 PostgreSQL 通过。

纳入 CP6 主仓库跟踪源码及其测试入口。未跟踪的 `CP6.Platform/` 是本地独立子仓库，旧 checkout 不代表 CP6 实际消费的包版本；实际 `0.10.2` 私有包源码与运行时行为尚待审计。独立私有 CRM 仓库未审计；CP6 中的 CRM 接口、Identity 桥和 ERP 集成证据不能代替该仓库的数据库兼容结论。

## 2. 统计口径与上下文规模

模型数量从四个已跟踪 EF `ModelSnapshot` 中的唯一 `modelBuilder.Entity("类型名")` 计数；rowversion 数量统计对应 Snapshot 的 `HasColumnType("rowversion")`。迁移数量按已跟踪迁移主文件计数，排除 `*.Designer.cs` 与 `*ModelSnapshot.cs`，Core 主链与其 IdentityPriority / ErpIntegration 子目录分别统计。

| 上下文 | Snapshot 中实体映射 | 迁移主文件 | rowversion 列映射 | 源码依据 |
|---|---:|---:|---:|---|
| CP6Context | 232 | 136 | 157 | [Core Snapshot](../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs)、[Core Context](../../CP6.Core/EFDbContext/CP6Context.cs) |
| SpaceContext | 83 | 47 | 41 | [Space Snapshot](../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs)、[Space Context](../../CP6.Space.Infrastructure/SpaceContext.cs) |
| IdentityMessagingContext | 4 | 1 | 4 | [Identity Snapshot](../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs)、[Identity Context](../../CP6.Core/Services/CrmIdentity/IdentityMessagingContext.cs) |
| ErpIntegrationContext | 8 | 3 | 6 | [ERP integration Snapshot](../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs)、[ERP integration Context](../../CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs) |

**327 是四个上下文的实体映射汇总，不是数据库实际表数。** 同一物理对象可能被多个上下文映射，Snapshot 也不是对真实数据库目录的查询。本文不推导实际表数、兼容百分比、工期或验收覆盖率。

四条迁移链合计 187 个迁移主文件，其中 28 个文件调用 `migrationBuilder.Sql`；该 28 是文件数量，不是 SQL 调用次数或独立数据库对象数量。已检查迁移主文件未发现 `ActiveProvider` 分支。现有迁移链包含 SQL Server 生成的类型、注解和 SQL，即使某个文件没有原始 SQL，也不能据此认定可以直接交给 PostgreSQL 执行。

四个上下文模型源码共有 95 次 `HasFilter` 调用，其中 1 次为 `HasFilter(null)`；因此不能将 95 全部称为实际 SQL 过滤谓词或实际数据库索引数。`HasFilter(null)` 的业务语义见 Core Context L691-L698：`Sys_Lang` 的 nullable TenantId 唯一键要覆盖全局资源，不能在 PostgreSQL 默认 NULL 唯一语义下意外允许多条全局同键记录。

运行时统计限定 `git ls-files` 跟踪的 `CP6.Core`、`CP6.WebApi`、`CP6.Space.Infrastructure` C#，排除 Migrations、bin/obj；tools 另计，排除 `*.Tests` 项目。每个类别按不同文件去重，各类别重叠，**不得相加**。

| 搜索类别 | 运行时不同文件 | tools 不同文件 | 口径与限制 |
|---|---:|---:|---|
| EF 原始 SQL API | 28 | 0 | `FromSqlRaw/Interpolated`、`ExecuteSqlRaw/Interpolated`、`SqlQuery/Raw`，含对应 Async 方法；API 本身并不意味着 SQL 不可移植 |
| ADO 连接/命令入口 | 10 | 1 | `GetDbConnection` / `CreateCommand`；含仅用于复用连接的入口 |
| Dapper import | 4 | 0 | 精确 `using Dapper;`；不把业务自定义的 `QueryAsync/ExecuteAsync` 计入 Dapper |
| SqlClient import | 22 | 7 | `using Microsoft.Data.SqlClient;` 或 `using System.Data.SqlClient;` |
| SqlException | 8 | 4 | SQL Server 异常类型依赖，不代表所有文件都含 SQL 字面量 |
| 锁提示/应用锁 | 31 | 2 | `UPDLOCK/HOLDLOCK/ROWLOCK/READPAST/sp_getapplock/sp_releaseapplock` |
| 明确 T-SQL 命令/字面量 | 35 | 5 | 锁、TOP、OUTPUT、专有时间函数、带 SQL 类型的 CONVERT、表变量、SET NOCOUNT、NEWID、joined DELETE、sys 元数据等；人工排除三个 Controller 的 C# `Convert(...)` 方法误报 |

MES 存储过程调用位于另一个运行时文件，未计入最后一行 35 个 SQL 字面量文件。普通 LINQ、`ExecuteUpdateAsync`、`ExecuteDeleteAsync` 与不含专有语法的聚合，应先验证 provider 翻译和业务契约，不能只因使用 EF relational API 就列为 T-SQL 重写。

## 3. 最高风险能力与具体证据

### 3.1 provider 注册、私有包与同事务上下文

- [WebApi Program](../../CP6.WebApi/Program.cs) L155-L157 固定 `UseSqlServer`；L201-L203 为 Dapper 注册 SQL Server 连接。[Space 注册](../../CP6.Space.Infrastructure/SpaceInfrastructureRegistration.cs) L47-L48 固定 SQL Server。
- [ERP integration 注册](../../CP6.WebApi/Configuration/ErpIntegrationConfiguration.cs) L110-L112 的 context factory 固定 `UseSqlServer`。设计时 factory、worker 内部 factory 和复用现有连接的 factory 都在兼容范围内，不能只改启动文件。
- [ErpRequestHandler](../../CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs) L265-L273 从 queue connection 再 `UseSqlServer` 创建 CP6Context，并明确拒绝非 SQL Server。
- [ErpInboxReplayService](../../CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs) L28-L32 拒绝其他 provider，并共享连接与事务；[ErpDeliveryReplayService](../../CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs) L177-L183 同类硬编码。
- [IdentitySnapshotWriter](../../CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs) L141-L145 从业务 connection 创建 IdentityMessagingContext，再加入原事务；L174-L177 要求 caller SQL Server 事务。
- [Cp6SpaceRuntimeMaterializer](../../CP6.Space.Infrastructure/Cp6SpaceRuntimeMaterializer.cs) L129-L140 从 Space connection 创建 CP6Context 并加入同事务，承担 Space 发布与 CP6 投影的原子性。
- [Core 项目](../../CP6.Core/CP6.Core.csproj) L13-L14 精确消费 `CP6.Platform.Messaging` / `CP6.Platform.EntityFramework` `[0.10.2]`；[WebApi 项目](../../CP6.WebApi/CP6.WebApi.csproj) L10 消费 `CP6.Platform.AspNetCore` `[0.10.2]`。实际消费版本由这些声明及 lock 文件约束，不能用本地旧 Platform checkout 代替包审计。包中的模型、租约、Inbox/Outbox、SQL、异常和 provider 接口仍须检查实际 0.10.2 来源。

窄化方向：一个 provider 配置入口，加上从现有 `DbConnection` 构造同 provider context 的工厂。保留原子性、TenantId 和错误行为；不将跨 context 事务拆成多个独立提交。

### 3.2 乐观并发 token 与 Identity baseline 边界

[BaseBizEntity](../../CP6.Entity/BaseBizEntity.cs) L24-L25 使用 `[Timestamp] byte[] RowVersion`。ERP、Workflow、Space 的更新竞争、If-Match / Base64 token、worker 领取失败处理依赖真实并发 token；改变类型或编码形状可能影响公开 API 和重放契约。

[IdentitySnapshotReader](../../CP6.Core/Services/CrmIdentity/IdentitySnapshotReader.cs) L25-L40 在 Serializable 事务中读取 `MAX(CONVERT(bigint,RowVersion))`，分页时要求整个租户当前快照边界一致，边界改变就拒绝并重启 baseline。此处要求能够识别全租户快照变动的边界，不是单行 opaque 并发 token。PostgreSQL `xmin` 不应直接代替此全局单调边界。

应分别设计：单行乐观并发 token；Identity baseline 的租户 generation / 事件序列或持久化一致快照游标。新边界与快照写入在同一事务推进，并保留跨页变动的显式处理。使用数据库 sequence 时也需设计事务可见性，不能仅凭“序号递增”宣称得到提交顺序和一致分页。

[ErpInboxReplayService](../../CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs) L22 要求传入 RowVersion 为 8 字节；[ErpQuotationOrderFactory](../../CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs) L27 比较 quotation RowVersion 的 Base64。迁移需明确保留或版本化这些契约。

### 3.3 应用锁、范围互斥与静默回退

[ErpSqlLock](../../CP6.Core/Services/ErpIntegration/ErpSqlLock.cs) L10-L18 要求 SQL Server 当前事务，执行 `sp_getapplock`，负返回码映射为争用超时。它保护消息处理、重放等业务资源。

[SpaceCadProviderCapabilityService](../../CP6.Space.Infrastructure/SpaceCadProviderCapabilityService.cs) L460-L482 仅检查 IsRelational，随后执行 `sp_getapplock`：PostgreSQL 是关系库，但不支持该命令。另一些路径在非 SQL Server 时直接跳过锁，例如 [SpaceEditLeaseService](../../CP6.Space.Infrastructure/SpaceEditLeaseService.cs) L408-L409、[SpaceAiAtomicApplyService](../../CP6.Space.Infrastructure/SpaceAiAtomicApplyService.cs) L381-L384；类似模式分布于 Design、Underlay、Excel Apply、Cad Parse、Retention、Republish 和 Validation。

需要 provider 各自的事务级业务资源锁，保留现有含 TenantId 的资源键、超时、冲突与事务结束释放行为。`HOLDLOCK` / Serializable 下的首次创建、不存在记录和范围互斥不能机械等同于对已存在行执行 `FOR UPDATE`；应围绕业务唯一约束和锁资源验证。

### 3.4 队列领取、租约、库存与审批

[EfSpaceAiCapacityLedger](../../CP6.Space.Infrastructure/EfSpaceAiCapacityLedger.cs) L58-L117 使用 `UPDLOCK,HOLDLOCK,ROWLOCK` 读现有 slot 和容量，随后 `SELECT TOP (1)` 配合 `READPAST` 领取可用项；L439-L475 的预算预留清理也使用专有 SQL。适配必须验证多 worker、不超容量、跳过已锁项、过期回收、续租和归属检查。

[WfServiceJobService](../../CP6.Core/Services/Wf/WfServiceJobService.cs) L81-L87 与 [EfSpaceJobLedger](../../CP6.Space.Infrastructure/EfSpaceJobLedger.cs) L230-L240 已使用 SaveChanges / `DbUpdateConcurrencyException` 竞争租约。此类实现可保留，但 PostgreSQL 并发 token 必须真实参与 UPDATE 条件，不能用 InMemory 行为测试证明竞争安全。

[WmsBinConsumer](../../CP6.Core/Services/Wms/WmsBinConsumer.cs) L333-L358 的非 SQL Server relational 分支通过同值 `ExecuteUpdateAsync` 获取写锁，再读归属记录；[DeadLetterNotifier](../../CP6.Core/Services/Integration/DeadLetterNotifier.cs) L267-L287 也有同值 UPDATE 实现。这是已有可移植思路，不能统称无锁降级，仍须 PostgreSQL 并发实证。

[IntegrationEventRetryWorker](../../CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs) L280、[WfNotificationDispatchWorker](../../CP6.WebApi/BackgroundServices/WfNotificationDispatchWorker.cs) L50 使用条件 ExecuteUpdate。重点是生成 SQL、影响行数、归属、崩溃重试与副作用幂等，不要求整体改成原始 SQL。

[PurchaseRequestService](../../CP6.Core/Services/Pur/PurchaseRequestService.cs) L231-L250 的审批 callback 仅 SQL Server 读取上锁，其他 provider 普通读取；需保持审批与撤回、重复 callback 的一致性。

### 3.5 授权一次消费、刷新与撤销

[CrmOidcGrantStore](../../CP6.WebApi/Services/CrmOidcGrantStore.cs) L56 起直接创建 SqlConnection；L58 使用 DELETE TOP、DATEADD 和 SYSUTCDATETIME；L69、L90 和 L158 使用修改并 OUTPUT 返回记录，承担 grant / logout ticket 原子消费与撤销。应保留 store 接口，分别实现两库的原子修改并返回，避免退化为读取后再写入。

[IdentitySnapshotWriter](../../CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs) L101-L105 使用 SYSUTCDATETIME / OUTPUT 撤销服务 token；L159-L165 批量撤销 grant，并通过 `COLLATE Latin1_General_100_BIN2` 精确关联 refresh hash。需要保持精确比较、租户范围及同事务撤销事件。

[RefreshTokenService](../../CP6.Core/Services/Sys/RefreshTokenService.cs) L184-L188 的 SQL Server rotation读取有 UPDLOCK/HOLDLOCK，其他 provider走普通集合。需要明确等价互斥及失败行为，不能只移除 provider 判断。

### 3.6 编号、错误分类、时钟与批量复制

- [DocNumber](../../CP6.Core/Services/Common/DocNumber.cs) L27-L38 只有 SQL Server ORD 使用专用原子编号；L53-L59 含表变量、UPDATE锁、OUTPUT、@@ROWCOUNT 和条件 INSERT。PostgreSQL 会退到 EF 读后递增，应实现窄的事务编号分配器，保持首次创建、并发增量、现有事务与间隙约定。
- [EfSpaceAiCapacityLedger](../../CP6.Space.Infrastructure/EfSpaceAiCapacityLedger.cs) L141-L143 仅识别 SQL Server 死锁 1205；[EfSpaceVersionClone](../../CP6.Space.Infrastructure/EfSpaceVersionClone.cs) L485-L494 识别 2601/2627；[ErpCommerceAuthority](../../CP6.Core/Services/ErpIntegration/ErpCommerceAuthority.cs) L34-L36 还要求具体唯一约束名。错误分类接口需分别识别两库唯一约束、死锁、序列化重试，并保留具体约束判定。
- [SpaceEditLeaseService](../../CP6.Space.Infrastructure/SpaceEditLeaseService.cs) L393-L397 的权威时间仅 SQL Server查询数据库 UTC，其他 provider使用应用时钟；Design、Cad Parse、Underlay、Excel Apply 有同类实现。应明确数据库时间或应用时钟的统一契约，避免多节点租约行为随 provider变化。
- [EfSpaceVersionClone](../../CP6.Space.Infrastructure/EfSpaceVersionClone.cs) L937 起使用 SET NOCOUNT、table variable、uniqueidentifier、NEWID及批量源映射复制。它需要独立 provider命令或等价批量实现，保留一次事务与源/目标 ID映射。
- [GdprService](../../CP6.Core/Services/Platform/GdprService.cs) L219 是 joined DELETE；L327-L328 动态 SQL使用 SQL Server方括号标识符。可局部转为 EF ExecuteDelete/Update，或使用模型元数据与 provider标识符服务，保持租户删除范围与引用处理。

## 4. 模型、迁移与业务规则差异

| 风险 | 具体依据 | 实现时需保持的行为 |
|---|---|---|
| nullable唯一键、全局语言资源 | [CP6Context](../../CP6.Core/EFDbContext/CP6Context.cs) L691-L698 | NULL TenantId全局键仍唯一；按业务语义选择 PostgreSQL约束/索引，不按默认NULL语义照搬 |
| SQL过滤索引、租户复合唯一键 | [MultiTenantCompositeUniqueIndexes](../../CP6.Core/Migrations/20260616132134_MultiTenantCompositeUniqueIndexes.cs) | 软删除、状态、来源、租户键、自动凭证与发票防重复条件均保留 |
| JSON、bit、LEN、字符串模式检查 | [SpaceContext](../../CP6.Space.Infrastructure/SpaceContext.cs) L1187-L1190、L1333-L1336 | 约束接受/拒绝相同业务输入，不仅替换函数名称 |
| 财务凭证触发器 | [FinJournalLineNoMutateTrigger](../../CP6.Core/Migrations/20260615153849_FinJournalLineNoMutateTrigger.cs) | 凭证行号不可变与已过账规则仍由可靠机制保护，含直接数据库写入场景 |
| MES存储过程 | [AddMesStoredProcedures](../../CP6.Core/Migrations/20260518115050_AddMesStoredProcedures.cs)、[MesDashboardDapperService](../../CP6.Core/Services/Mes/MesDashboardDapperService.cs) L25-L46 | 三个usp报表可由已有EF版或provider查询实现，保持结果契约 |
| Space发布/草稿治理 | [PublishOrchestration](../../CP6.Space.Infrastructure/Migrations/20260807135544_SpaceE06S03PublishOrchestration.cs)、[UnifiedDraftCreation](../../CP6.Space.Infrastructure/Migrations/20260827053057_SpaceV1UnifiedDraftCreation.cs) | 发布、草稿唯一性、证据链、不可变/状态约束和并发版本均纳入 |
| DateTime / UTC / 本地时间 | [BaseEntity](../../CP6.Entity/BaseEntity.cs) L27、[CP6Context](../../CP6.Core/EFDbContext/CP6Context.cs) L2434-L2436 | 区分已有DateTime.Now业务本地日期与审计UTC；明确列类型、Kind、读取和写入转换 |

现有 SQL Server 迁移历史应保留；PostgreSQL 采用独立 provider迁移产物，并围绕已确认当前模型和业务约束建立可审计初始基线。空库初始化、后续升级以及 SQL Server回归都要验证；从旧 SQL Server生产数据迁往 PostgreSQL的导出、转换、校验与切换必须另行明确数据迁移范围，不能由“新空库可启动”推导完成。

## 5. 各业务模块的兼容范围

| 模块 | 直接热点与关联能力 | 需验证的业务闭环 |
|---|---|---|
| ERP / CRM订单集成 | SQL-only handler/factory、应用锁、报价版本、编号、共享事务、Inbox/Outbox/replay | 同消息重复、同机会重复、首次绑定竞争、报价变更、汇率/金额、失败回滚与重放 |
| WMS / Space发布投影 | 重试归属锁、库存并发、Space→CP6同事务投影、过期租约 | 不重复库存副作用、旧租约不能提交、发布投影原子性、不同租户隔离 |
| 财务 | rowversion、nullable/过滤唯一键、凭证触发器、金额/精度和日期 | 过账一致性、自动凭证与发票防重复、行号不可变、对账并发；Fin服务未发现直接运行时T-SQL不等于财务兼容通过 |
| Workflow / OA /采购审批 | rowversion worker抢占、通知条件UPDATE、审批callback锁 | 并发领取只允许有效持有者推进、撤回与callback竞争、重复通知/任务重试 |
| Auth / SSO / Identity | grant一次消费、刷新轮转、token撤销、精确hash比较、快照游标 | 同ticket并发消费、家庭撤销、租户停用、跨页快照变化、事件与业务同事务 |
| Space CAD / AI /编辑 | 应用锁、容量slot、预算预留、权威时钟、批量clone、JSON约束、版本/发布 | 编辑lease、Apply幂等、容量上限、provider资格修订、clone完整性、草稿/发布互斥 |
| MES /经营报表 | 三个存储过程、Dashboard TOP及Dapper聚合 | 结果一致、分页/排序、日期边界、报表租户范围 |
| 租户 /平台治理 / GDPR | nullable全局键、唯一键前缀、IgnoreQueryFilters路径、删除SQL | 同业务键不同租户、全局资源唯一、跨租户不可见、导出/删除范围正确 |

[DashboardController](../../CP6.WebApi/Controllers/Sys/DashboardController.cs) L43-L71 的 Dapper查询没有显式 TenantId谓词，需明确它是全局还是租户报表。EF全局 query filter不会自动约束Dapper；静态发现不能代替运行时授权验证，也不应在本次文档建档顺带修改报表行为。

## 6. 现有测试基础与未验证范围

| 入口 | 已有源码证据 | 可证明与不能证明的范围 |
|---|---|---|
| [WmsProductionSqlServerTests](../../CP6.Tests/WmsProductionSqlServerTests.cs) | L43 `MigrateAsync`；`SqlServerFact`门控 | 已有真实SQL Server迁移测试入口；本文未执行，不覆盖PostgreSQL |
| [SpaceSqlIntegrationTests](../../CP6.Tests/SpaceSqlIntegrationTests.cs) | L43 `EnsureCreated` | 真SQL Server模型/并发集成入口；**不是正式迁移链验证** |
| [ERP integration SqlDatabaseFixture](../../eng/crm/erp-integration-tests/SqlDatabaseFixture.cs) | L55 `MigrateAsync` | 已有真实ERP迁移/集成基础；需新增PostgreSQL分支与等价业务断言 |
| [OIDC GrantStoreSqlTests](../../CP6.Oidc.IntegrationTests/GrantStoreSqlTests.cs) | L38 CreateSql、L41 GenerateCreateScript、L42-L47筛选DDL批次 | grant SQL行为入口，且仅建所选对象；**不等于全部迁移链验收** |
| [OIDC browser fixture](../../eng/crm/browser-fixture/Program.cs) | L58 EnsureCreated、L59 CreateSql | 浏览器场景模型/专用DDL夹具；不代替完整迁移 |
| [Identity events fixture](../../eng/crm/identity-events-fixture/Program.cs) | L158迁移到指定Core目标、L165 Identity context迁移 | 既有Identity事件夹具；迁移目标与实际测试范围需保留原始记录 |

SQLite / InMemory适合部分领域行为回归，不证明 PostgreSQL的事务隔离、rowversion替代、NULL唯一性、锁、约束或真实迁移。`SqlServerFact`缺少环境配置时可能跳过，发现测试源码或看到测试命令成功退出也不能自动记为真库通过。

实现任务必须保留两库隔离测试数据库、源码SHA、provider/数据库版本、实际执行命令、迁移起止状态和失败/跳过记录。先形成单次部署provider一致的初始化与升级验证，再集中验证高风险业务契约。普通编译/测试按仓库规则本地运行；未经新的明确授权不触发GitHub Actions，不恢复自动事件，不把本地结果冒充生产候选或环境推广证据。

## 7. 运行时35个明确T-SQL文件清单

以下是上述去重文件集合，可作为实现时逐项确认的入口；行号为本文源码基线的定位提示，链接指向仓库文件。部分专有SQL已有SQL Server条件分支，部分在关系库上直接执行；集合大小不是需要新建适配接口的数量。

| 文件 | 定位行 / 特征 |
|---|---|
| [ErpOrderBridgeWorker](../../CP6.WebApi/BackgroundServices/ErpOrderBridgeWorker.cs) | L46 UPDLOCK/HOLDLOCK |
| [SpaceIntegrationEventOccurredAtUtcBackfill](../../CP6.WebApi/BackgroundServices/SpaceIntegrationEventOccurredAtUtcBackfill.cs) | L17/L28 应用锁获取/释放 |
| [CrmOidcGrantStore](../../CP6.WebApi/Services/CrmOidcGrantStore.cs) | L58/L69/L158 TOP、时间函数、OUTPUT |
| [SpaceAuditPermissionSeed](../../CP6.WebApi/Seed/SpaceAuditPermissionSeed.cs) | L24 应用锁 |
| [DashboardController](../../CP6.WebApi/Controllers/Sys/DashboardController.cs) | L59 SELECT TOP |
| [SpaceValidationInfrastructure](../../CP6.Space.Infrastructure/SpaceValidationInfrastructure.cs) | L103 应用锁 |
| [SpaceUnderlayHistory](../../CP6.Space.Infrastructure/SpaceUnderlayHistory.cs) | L310/L336 数据库UTC、应用锁 |
| [SpaceHistoricalRepublishService](../../CP6.Space.Infrastructure/SpaceHistoricalRepublishService.cs) | L77 应用锁 |
| [SpaceGenerationApplyStepExecutor](../../CP6.Space.Infrastructure/SpaceGenerationApplyStepExecutor.cs) | L1610/L1624/L1638 锁提示、bit |
| [SpaceExcelCadApplyService](../../CP6.Space.Infrastructure/SpaceExcelCadApplyService.cs) | L636/L663 数据库UTC、应用锁 |
| [SpaceExcelCadApplyJobStepExecutor](../../CP6.Space.Infrastructure/SpaceExcelCadApplyJobStepExecutor.cs) | L1870/L1898 数据库UTC、应用锁 |
| [SpaceEditLeaseService](../../CP6.Space.Infrastructure/SpaceEditLeaseService.cs) | L395/L422 数据库UTC、应用锁 |
| [SpaceDesignV1Service](../../CP6.Space.Infrastructure/SpaceDesignV1Service.cs) | L6361/L6388/L6423 数据库UTC、应用锁 |
| [SpaceCadProviderCapabilityService](../../CP6.Space.Infrastructure/SpaceCadProviderCapabilityService.cs) | L471 应用锁 |
| [SpaceCadParseService](../../CP6.Space.Infrastructure/SpaceCadParseService.cs) | L1432/L1476/L1848 数据库UTC、应用锁 |
| [SpaceAiRunRecoveryService](../../CP6.Space.Infrastructure/SpaceAiRunRecoveryService.cs) | L542 锁提示、bit |
| [SpaceAiRetentionStore](../../CP6.Space.Infrastructure/SpaceAiRetentionStore.cs) | L208 应用锁 |
| [SpaceAiAtomicApplyService](../../CP6.Space.Infrastructure/SpaceAiAtomicApplyService.cs) | L400 应用锁 |
| [EfSpaceVersionClone](../../CP6.Space.Infrastructure/EfSpaceVersionClone.cs) | L937起 表变量、NEWID、批量复制 |
| [EfSpaceFileSafety](../../CP6.Space.Infrastructure/EfSpaceFileSafety.cs) | L529 锁提示 |
| [EfSpaceAiCapacityLedger](../../CP6.Space.Infrastructure/EfSpaceAiCapacityLedger.cs) | L62/L109/L111 锁提示、TOP、READPAST |
| [WmsBinConsumer](../../CP6.Core/Services/Wms/WmsBinConsumer.cs) | L322 锁提示 |
| [IdentitySnapshotWriter](../../CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs) | L101/L152/L159 锁、时间、OUTPUT、COLLATE |
| [IdentitySnapshotReader](../../CP6.Core/Services/CrmIdentity/IdentitySnapshotReader.cs) | L27 CONVERT(bigint,RowVersion) |
| [IdentityBootstrapService](../../CP6.Core/Services/CrmIdentity/IdentityBootstrapService.cs) | L16 锁提示 |
| [CrmServiceTokenRecordStore](../../CP6.Core/Services/CrmIdentity/CrmServiceTokenRecordStore.cs) | L32 锁提示 |
| [DocNumber](../../CP6.Core/Services/Common/DocNumber.cs) | L53-L59 表变量、OUTPUT、@@ROWCOUNT |
| [DeadLetterNotifier](../../CP6.Core/Services/Integration/DeadLetterNotifier.cs) | L250起 锁提示 |
| [RefreshTokenService](../../CP6.Core/Services/Sys/RefreshTokenService.cs) | L185 锁提示 |
| [PurchaseRequestService](../../CP6.Core/Services/Pur/PurchaseRequestService.cs) | L237起 锁提示 |
| [ErpSqlLock](../../CP6.Core/Services/ErpIntegration/ErpSqlLock.cs) | L14 应用锁 |
| [ErpRequestHandler](../../CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs) | L83 锁提示 |
| [ErpDeliveryReplayService](../../CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs) | L106/L145 原始SQL锁定记录 |
| [ErpQuotationOrderFactory](../../CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs) | L19/L22/L24 锁提示 |
| [GdprService](../../CP6.Core/Services/Platform/GdprService.cs) | L219/L327 joined DELETE、标识符 |

tools专有SQL匹配5个文件：[CadStartAcceptance](../../tools/CP6.Space.CadStartAcceptance/Program.cs) L528-L529 SERVERPROPERTY / CONVERT；[SourceFence](../../tools/CP6.Crm.SourceFence/SourceFence.cs)、[ActualSourceInspector](../../tools/CP6.Crm.SourceFence/ActualSourceInspector.cs)、[ActualSourceFreezer](../../tools/CP6.Crm.SourceFence/ActualSourceFreezer.cs)、[ActualSourceFreezer.Recovery](../../tools/CP6.Crm.SourceFence/ActualSourceFreezer.Recovery.cs)。SourceFence工具服务于SQL Server来源的治理与证据，不应为了应用支持PostgreSQL而更改来源身份或伪造两库通用证明。

## 8. 正式依据与后续审查用途

- [EF Core：多个provider的迁移](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)：不同provider维护独立迁移产物，避免将现有SQL Server迁移直接当作PostgreSQL迁移。
- [Npgsql EF Core provider](https://www.npgsql.org/efcore/)：新增接线与版本选择以正式provider文档为依据；CP6当前EF Core主依赖为8.0.30，不能由PostgreSQL服务器18推导使用EF/Npgsql18或盲目升级全部依赖。
- [Npgsql：并发token](https://www.npgsql.org/efcore/modeling/concurrency.html)：PostgreSQL并发token方案与SQL Server自动rowversion存在差异；本文Identity baseline的全租户边界仍需独立设计。
- [Npgsql：日期与时间类型](https://www.npgsql.org/doc/types/datetime.html)：明确UTC DateTime、DateTimeOffset与timestamp/timestamptz读写规则，逐项审计当前DateTime.Now与审计UTC转换。
- [PostgreSQL 18：约束](https://www.postgresql.org/docs/18/ddl-constraints.html)：依据唯一键、NULL和检查约束规则设计等价业务约束。
- [PostgreSQL 18：显式锁](https://www.postgresql.org/docs/18/explicit-locking.html)：依据行锁、事务锁和advisory lock行为设计业务资源互斥，而不是仅翻译T-SQL语法。

下一阶段围绕provider/同事务工厂、并发token/Identity边界、事务锁/编号/错误分类/时钟形成小范围能力适配，再完成各模块SQL和独立迁移、两库契约验证。纯EF可用实现优先保留。具体实施顺序、阶段验收、数据迁移边界与任务关闭条件以关联设计和执行计划为准；本文不是已完成清单。
