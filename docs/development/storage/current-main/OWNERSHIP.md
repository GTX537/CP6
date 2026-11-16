# 当前 Main PostgreSQL 存储归属

本清单按 [S02-00 §3](../../../design-handbook/architecture/02-postgresql-tenancy.md#3-数据分类与物理边界)核对 Main `8dfe0ef0b53852fa6797e986639133d3708f022c`。冷初始化目录来自独立 PostgreSQL 18.6，复用已采用的 W673 制品；其产品源码与此 Main 基线相同。[W675 交付及实际验证](../../evidence/W675-01/DELIVERY.md)

## 对象及证据边界

- [summary.json](summary.json)：140 个 schema，891 张表、40 个序列、1 个视图、567 个函数/过程；2,317 个索引中 1,662 个唯一索引，859 个外键，1,713 个用户触发器。数量用于解释覆盖范围，不作为业务交付目标。
- [objects.jsonl](objects.jsonl)：逐对象记录实际 schema、数据库 Owner/ACL、列类型和精度、全部键/索引/外键、触发器、RLS/policy、序列依赖、函数权限及源码引用。第一行说明空字段和本地管理员别名；`LOCAL_MIGRATOR` 只是隔离库建库身份，不是业务 Owner，也不证明普通运行角色有这些权限。
- [sources.jsonl](sources.jsonl)：对应 2,876 个当前 Main 源文件，记录写入、事务、后台和泛型入口的位置。对象的 `source_references` 引用此文件内的 ID；它是保守导航，可能同时包含读取者、被关闭的兼容路径或同文件其他写入，不是调用图或新的授权证明。实际写者与授权采用复用 [PGT-017](../../evidence/W645-01/DELIVERY.md)及原 Owner 交付。
- [tenant-models.jsonl](tenant-models.jsonl)：同一实际制品的 RMA v1/v2 模型。示例 `t_000000000000000000000000000000a1` 只用于离线提取；运行时仍从可信 TenantId 映射，不从示例或请求体建立绑定。
- [optional-installations.jsonl](optional-installations.jsonl)：85 处显式安装 DDL 的源位置和模板。包括 `cp6_tenancy_v1` 目录、RMA 控制/租户对象、身份目录、Stock 安装回执等；这些安装器不由冷 `DatabaseInit` 自动执行。此文件是源模板索引，不把模板当成本轮已创建的对象。实际采用范围沿原 PGT 和各安装器合同。

四个原迁移域的 328 张 EF 表，加上 Stock Outbox 的 13 张表，构成 341 张固定 EF 表。另有 550 张迁移原生表。反射发现的 9 个实际 DbContext 类型均已处理：五个独立共享模型、Stock 写/读及 Identity Directory 三个固定派生模型，以及 RMA 的两套模型。固定派生模型保留原共享位置；RMA v2 是两张安全调用者视图，不是第二份业务原件。

## 当前归属、写入和恢复边界

以下按实际采用范围分类。`RETAIN_CURRENT_SHARED` 表示保留当前物理对象和原唯一域，不表示它是全局公开数据，也不授权普通租户读写全部列。未在租户安装/模型中明确采用的对象，新的 `t_*` 路由继续关闭。当前任务不搬表、不改变 Owner、不移除 TenantId、组织映射、软删或复合引用。

| 对象组 | 当前 Owner / 写入边界 | 保留与恢复依赖 |
| --- | --- | --- |
| `public.Sys_Tenants`、`cp6_tenant_creation_v1`、`cp6_tenant_management_v1` | Main 租户控制；TenantAdminService 的原创建/暂停/到期事务和原回执。普通业务不能枚举目录。 | 与原创建身份、当前资格和 IAM 代次一致；不随业务旧快照回滚当前暂停。 |
| `Sys_User/Role/RoleMenu/Dept` 及授权关系、`CrmIdentityTenantGenerations`、`cp6_authentication_v1`、`cp6_scope_authorization_v1` | Main IAM；实际入口见 W645。共享身份数据仍属于租户，数据库触发器/原生权威根参与最终决定。 | 当前安全状态、封存原件、凭据与恢复操作按原协议；归档不能复制主密钥或复活旧授权。 |
| `CrmOidcGrant`、`CrmOidcLogout`、`DataProtectionKeys` | 共享安全存储。前两者按 `OrganizationId`、`SubjectId`、`GrantId` 关联，不能因没有字面 `TenantId` 就判为全局业务数据。 | 期限、已消费/撤销、当前会话及加密密钥由认证 Owner 处理；不作为普通租户文件切片。 |
| `Sys_Menus`、`Sys_DictTypes`、`Sys_DictDatas` | 当前明确的共享定义；结论来自实际模型与调用。`Sys_Langs` 等带租户的表不因此改为全局。 | 保持原全局键；共享定义与租户覆盖、权限引用分开处置。 |
| `T_DocSequence`、`T_MesSequence`、四个 `cp6_storage_*` token 序列、身份/serial 序列 | 原编号 Owner / PostgreSQL token 生成器；序列记录链接实际所属列或 `PostgreSqlTokenV1.Install`。 | 保持全局编号/并发 token 语义；恢复不得独立回退编号或重新生成已承诺业务身份。 |
| `public` ERP/采购/计划/MES/WMS/FIN/OA/Space 表及其他 `cp6_*` 业务 schema | 仍属其原业务 Owner；逐对象源码与原生写函数见机器清单。实际 raw SQL、批量/导入、HTTP 和后台采用沿 W645。现有 schema 是共享业务命名空间，不能仅按含租户列就复制。 | 原业务、审计、原 operation/receipt 及消息在原事务域闭合。保留软删、原有 hold/处置和精度；未登记租户切片不宣称可单租户恢复。 |
| `Space_Asset/AssetVersion/RackGenerationProfile/RackGenerationProfileVersion` | Space 明确使用 `Scope` 与 `OwnerTenantId`；共享定义和租户自有对象混合存放，按原服务区分。 | 不把 OwnerTenantId 丢弃；不可变资产/历史文件与元数据的恢复依赖保留。 |
| `erp_integration` 八表 | 原 ERP Inbox、请求、业务版本、Outbox、桥接和重放 Owner；[八表明细](../../evidence/WP02-02/erp-tenant-isolation/OBJECTS.md)。 | 全表 MessageId 唯一域、请求复合键及业务写集同连接同事务；恢复/重放按原 evidence，普通角色不新增 DELETE。 |
| `crm_identity`、`crm_identity_priority` | Main 身份业务及独立优先消息域；Platform 0.10.4 是消息实现，不成为 Main 数据 Owner。 | 不合并历史 queueDomain；原 IAM 修改、Outbox 与代次同事务，恢复与当前安全状态协调。 |
| `cp6_stock_outbox_v1` 十三表 | `StockOutboxContextV1`、`StockOutboxEnqueueV1`、`StockOutboxWorkerV1` 采用 Platform Outbox v2。源 Owner 为 `Main.Wms.StockMovement`，投递 Owner 为 `Main.Wms.InventoryNotification`。 | 原 route/root、lease、epoch、claim/send/receipt 与业务原件共同对账；已有归档 v10/v11 切片沿 W526/W527。 |
| `cp6_main_owner_v1.operations` 及各原生 append-only/hold/处置表 | 各 Owner 的原操作与结果；数据库 trigger、FK 和检查约束在逐对象目录保留。 | 保留原始字节和当前 hold/处置策略；本清单不制定新的 TTL、不授予 purge，也不把删除策略改成永久保留。 |
| RMA `{trusted tenant schema}.heads/versions/rma_migration_history_v1` | 已采用租户布局。Owner 仍为 `Main.Wms.RmaApplication.v1`，共享原回执、出库依赖、身份和消息域仍共享。佣用后原 `cp6_rma_application_v1.heads/versions` 写者由安装器关闭，原物理表不会被本清单删除。 | v1 + 完整共享切片的实际归档恢复沿 [W526](../../evidence/W526-01/DELIVERY.md)、[W527](../../evidence/W527-01/DELIVERY.md)。 |
| RMA `application_heads_v2/application_versions_v2`、`cp6_rma_model_v2` 及普通租户 v2 writer | 两视图指向原租户表；读模型、写者登记和迁移原件见 [W528](../../evidence/W528-01/DELIVERY.md)。 | [W676](../../evidence/W676-01/DELIVERY.md) 已验证显式 v12 完整归档、关闭候选和同源缺失行恢复；默认旧格式仍拒绝 v2。当前 IAM、原迁移与普通角色权限保持，完整 schema 毁损恢复另行处理。 |
| Stock / Identity / ERP 的普通角色安装覆盖层 | 保留固定共享表和全局键，通过原租户 LOGIN 目录、RLS、列/函数授权及原 Owner 事务进入。具体模板在 optional 索引。 | 冷目录不代表这些覆盖层已经安装；普通角色与跨租拒绝证据复用相应 PGT。本轮没有安装新环境。 |

## 非直接表名写者的核对

1. PostgreSQL 自动产生的 29 个 identity/serial 序列通过实际依赖回到所属表/列；Space token 序列及函数通过迁移调用回到共用生成器。所有冷目录对象都有直接或生成来源，未把自动名称误报为产品缺口。
2. `federated_keys`、文档 `bindings`、旧表单 source revision、产品身份及 transfer evidence 等由原生函数/触发器写入；`native_writer_routines` 记录显式 DML 的真实函数名。函数内部动态 SQL 另标记，源定义仍是权威。
3. 文档、迭代、位置规则、需求集的 `delivery/attempts/results` 使用 `ApprovalBusinessProfileV2.SubmissionSql/SubmissionText` 的封闭映射，由原 `ToolApprovalDeliveryV1` 执行；不是遗漏写者，也不接受任意 schema 参数。
4. Platform 的共享消息表通过包模型、调用者事务与宿主 Worker 写入，按各自 queueDomain 登记；不能只搜索 Main 中的 `INSERT INTO Cp6_*`。
5. `cp6_warehouse_draft_v1.consumer_fences` 是迁移填充的控制目录，运行守卫只读取。泛型 `RepositoryBase<T>` 在 DI 注册，但当前生产源码没有具体 `IRepository<T>` 消费者；保留其入口记录，不据此虚构活动业务写者。

保守源码索引与以上闭合映射用于存储归属；不能把“某文件出现表名”作为新的权限、原子性或任意未来入口的验收。当前已登记实际写者的事实直接引用 W645，后继改变调用链或存储合同再做对应定向验证。
