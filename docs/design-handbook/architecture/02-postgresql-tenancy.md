# PostgreSQL 多租户存储与事务布局

任务 `S02-00`；设计版本 `cp6-pg-tenancy-v1`；日期 2026-10-12。本文选定后续实现的设计方案，运行采用仍为 `UNPROVEN`，所有[验收场景](../../development/acceptance/postgresql-tenancy.json)均为 `NOT_RUN`。协调交付、任务状态及远端 main 包含性见[任务报告](../../development/evidence/S02-00/README.md)，本文不是迁移或部署批准。

## 1. 决定与适用范围

选用**每个业务宿主库内的共享 schema + 租户业务 schema**，保留 TenantId/OrganizationId、原授权和跨租户复合约束。Main 与 CRM 仍分别拥有自己的数据库和业务权威；Platform 提供库能力，不成为第三个租户或消息主账。

采用条件是：租户专用数据库登录角色、共享租户行的 RLS、可信存储绑定、同连接事务、逐租户迁移/恢复均通过所属宿主的真实 PostgreSQL 验收。当前代码不会因本文自动得到这些能力。

| 方案 | 收益 | 代价与结论 |
|---|---|---|
| 共享表 + TenantId + 应用过滤/RLS | 模型和迁移数量少，全局唯一键自然保留 | 保留为现有布局的兼容阶段；不提供租户业务对象的独立 schema 管理边界 |
| 共享控制/消息域 + 租户业务 schema | 租户对象边界明确，可逐租户升级，单库跨 schema 可保持本地事务 | **选定**；角色、连接池、EF 模型、共享切片恢复需要额外治理，不能把每张表机械复制 N 份 |
| 每租户独立数据库 | 更强的数据库级访问和独立恢复边界 | 共享身份/全局消息唯一性和本地原子写集需要另设计；本版不采用，也不默认为所有模块准入要求 |

本版是有界存储决定，不修改原 wire/schema、金额数量精度、业务 Owner、认证/授权合同。前置为[数据库决定](../../development/08-postgresql-baseline.md)；原设计与修订按[版本规则](../remediation/01-version-and-authority.md)组合。SYS-TENANT/SYS-IAM/PLT-CTX/PLT-DEL 的来源摘要和源码范围在 [source-inventory.json](../../development/evidence/S02-00/source-inventory.json)。

## 2. 当前源码事实与差距

| 仓库与已核对远端 main | 当前事实 | 后续实现不能假定的能力 |
|---|---|---|
| Main `df5553fd04ec4fc516602a4e2843650c38ae5006` | CP6Context 按实例 CurrentTenantId 注册 BaseTenantEntity 查询过滤、部分特殊实体过滤和新增盖章；TenantScope 还原可变上下文；TenantMiddleware 缺有效 claim 保默认租户 | 新入口不得用默认 A1 证明可信绑定；盖章与 EF filter 不覆盖所有 raw SQL、bulk、导入和显式 TenantId 写入 |
| Main 同上 | Sys_Tenant 是共享目录；Sys_User 属租户，Sys_Role 使用租户复合键；Sys_Menu 是共享定义；DataProtectionKeys 为宿主共享安全数据 | “非 BaseTenantEntity”不能等同“可向所有租户公开”；角色、审计等特殊表须逐一映射 |
| Main 同上 | ErpIntegrationContext 的 CommandInbox 以 MessageId 为主键；保留独立 ERP 原请求/原结果和 Platform Outbox；IdentityMessagingContext 使用 crm_identity_priority | 不得用通用 Inbox checkpoint 替换 ERP command journal，不得合并身份优先与普通队列预算 |
| CRM `c778a3052a4416b82facb07b1244fc98fb6ad8a1` | CrmDbContext 使用 crm，CrmRuntimeDbContext 含 crm_v1/保留 crm 表及全局 PublicRoute 排除；IdentityProjectionContext 使用 crm_identity；CrmBusinessOutbox 要求 SqlTransaction 并使用 sp_getapplock | 这些入口不是已实现的 PostgreSQL 租户 schema 适配；原生 PG 锁、并发 token、映射和真实事务由 CRM 单独交付 |
| Platform `30bd23af6808d217a23878bd9437513043c52834` | RequestContextMiddleware 在 finally 清空上下文；Cp6InboxProcessor 自建 context/Serializable 事务；消息模型 Outbox MessageId、Inbox(ConsumerName,MessageId) 全表唯一 | 新调用方事务/前置内容门不能通过引用旧 ProcessAsync 获得；缺能力的路由不开放 |

Main 当前依赖 Npgsql `8.0.8`、Npgsql EF provider `8.0.11`、EF Core `8.0.30`；Main/CRM 引用 Platform EF/Messaging `[0.10.2]`。这些是项目文件声明，**不是本轮恢复包、运行版本或制品哈希证明**。官方语义参考 PostgreSQL 18；本轮未连接数据库，也未决定升级现有实例版本。落地证据须记录实际服务器/驱动/EF/包版本，验证其兼容性。

## 3. 数据分类与物理边界

`platform_shared` 是各宿主库内拟定的共享控制命名空间，不表示 Platform 库拥有这些业务数据。现有共享队列域名称保持独立；迁移它们必须另有兼容方案。

| 数据组/现有入口 | 逻辑归属与目标位置 | 访问与约束 |
|---|---|---|
| Main Sys_Tenant、存储绑定、服务/issuer/region 注册引用 | Main 控制面；platform_shared | 平台受控服务维护；普通 tenant 不枚举花名册，不从 schema 名反推新 TenantId |
| Main Sys_User、Sys_Role、Sys_RoleMenu、部门及授权关系；认证 session/refresh/SSO | 仍是**租户数据**，本版留共享身份存储域 platform_shared | 租户行 RLS，原 TenantId/复合键/全局 token hash 唯一性不变；跨租户 token 定位仅受控认证入口，不能给普通业务角色全表查询权 |
| Sys_Menu、菜单操作定义、具名全局模板/类型目录 | 宿主共享定义；platform_shared | 只授所需只读列/视图；租户覆盖项仍带 TenantId，不把 Sys_Lang、Sys_Dict 等仅因名称视作全局 |
| DataProtectionKeys、宿主密钥引用、平台管理审计 | 共享安全域；platform_shared | 安全宿主/控制身份专用，不给租户运行角色 SELECT；不在租户导出中复制主密钥 |
| Main ERP/采购/计划/MES/质量/WMS/FIN/OA/Space 等业务聚合与租户配置 | 由原领域 Owner 持有；t_<tenant-guid-N> | 保留 TenantId、完整精度、软删/保留与跨租户复合 FK；依赖共享 IAM 的 FK 使用限定 schema，仍核 TenantId |
| IntegrationEvent、业务审计、附件元数据、业务原operation/receipt | 与其 Owner 写集绑定；租户 schema，或保留原合同要求的共享域 | 以实际唯一键/事务归属决定；不是把所有含 TenantId 的表一律搬走。对象文件另按 tenant+不可变版本授权 |
| Main erp_integration 的 CommandInbox/Request/Aggregate/Outbox/恢复证据 | 同 Main 物理库内共享消息域 erp_integration | 保原 MessageId 全表唯一与请求复合键；租户行 RLS；和租户业务账共用同一连接事务 |
| Main 身份优先 Outbox 与 lease/route 伴随表 | 同 Main 物理库内共享域 crm_identity_priority | 保独立 queueDomain/预算/锁根；与 IAM 变更同事务；不迁入普通业务消息域 |
| CRM PublicRoute/宿主公共路由目录 | CRM 共享控制数据 | Public 解析只返回受控登记目标；不能复制到每个 organization 成为多份权威 |
| CRM identity Projection/Published/Reconciliation/撤权 origin、Inbox/Checkpoint | 同 CRM 物理库的共享 crm_identity 域 | 每 tenant 行隔离；保前置内容判定、原始来源和本地最终门；不反写 Main 用户主账 |
| CRM Leads/Activities/Commands/业务审计/保留数据 | CRM 租户 schema | OrganizationId 必须经受控映射等于 Main TenantId；原 crm/crm_v1 混合映射需准确迁移，不能只替换 HasDefaultSchema |
| Platform 通用消息四表及 v2 companion | **所属宿主**的具名共享 queueDomain | 一张原表的全租户唯一域保持一张共享表，不按 tenant 复制；不把不同历史 queueDomain 合成全系统唯一域 |

以上是明确的数据组分配，不冒充全库逐表迁移清单。实施时由 WP02-02/CRM 对实际 EF 模型及 raw SQL/视图/触发器/函数/sequence 生成逐对象清单：`Owner、位置、tenant列、唯一域、FK、writer、事务域、保留策略、恢复依赖`。未分类对象保持原位置且不得开放新 schema 路由；缺完整清单是采用门未满足。

逐对象清单必须覆盖**所有**主键和唯一键，不能只检查消息。若原表的 Id、业务编号或其他键必须跨 tenant 唯一，将表复制到 N 个 schema 会削弱原约束；这类表本版继续放共享域。只有证明该对象的合法唯一域原本就是 tenant 内、且所有引用保持无损时才迁入租户 schema。若确需共享全局身份注册表与租户实体同事务的适配，应作为具名后继设计另审，不能偷偷改成 `(TenantId, Id)`。

特别保持：Outbox `MessageId` 全表唯一，Inbox `(ConsumerName,MessageId)` 全表唯一，不擅加 TenantId 缩小唯一域；ERP CommandInbox 同样保持。共享表 RLS 不改变这些唯一索引。同键他租户冲突返回安全的身份冲突，不返回其原结果，也不换 messageId 重试。只有受控接收/恢复入口能在原合同授权下读取原绑定、隔离可信原 aggregate；普通请求不能借冲突触发他租户写入。

## 4. 可信租户与内部存储绑定

新增内部设计对象 `TenantStorageBindingV1`，由 Main/CRM 各自配置 Owner 持有，不往已冻结 RequestContextSnapshot 或业务报文中加必填字段：

| 字段 | 含义与限制 |
|---|---|
| tenantId、hostId、databaseRef | tenantId 为 Main 稳定非空 GUID；CRM organizationId 显式等值；databaseRef 是内部不透明键，无连接串 |
| schemaName、layoutVersion、modelVersion | 受控配置；layoutVersion=cp6-pg-tenancy-v1；仅通过对应模型版本的路由可用 |
| bindingRevision、credentialVersion | 分别标识路由配置与数据库凭据版本，不充当权限 epoch、事件版本或 messageId |
| runtimePrincipalRef、queueDomainRefs | 指向该宿主/数据库/租户专用角色及已登记共享消息域；不得外部自由填写 |
| storageState、migrationVersion | 存储就绪轴；Provisioning/Ready/Draining/Unavailable/Retired。不替代 Sys_Tenant.Enable/ExpireDate |

认证验证 → 可信服务/用户注册与 issuer/region 映射 → 非空 tenant → 当前授权 → 存储绑定 → 专用 DataSource/模型 → 事务内重新验证 bindingRevision 和最终门。绑定不可用、冲突、版本未知均拒绝相应新效果，不回退默认租户/public/其他库。租户无关健康探针使用独立受限路径，不打开业务库。客户端 header/body/slug 只可能作为已授权解析流程的输入，不是 schema、databaseRef 或最终身份。

租户 schema 固定为 `t_` 加小写 GUID `N` 格式（32 个 ASCII 十六进制字符），共 34 字节。仅 Main 已登记 GUID 的规范输出才可入映射；不使用 TenantCode、组织名、邮箱、外部字符串截断或哈希缩短。改名不改 schema；非空 GUID 一一对应，目录对 `(hostId,databaseRef,tenantId)` 与 `(databaseRef,schemaName)` 分别唯一。冲突/已存在且 Owner 不符时中止，不接管旧 schema。

PG 默认标识符上限是 63 **字节**，未引用名折为小写；不得依靠静默截断。表/列沿当前 PG 映射和正确引用，生成索引名沿现有确定映射规则并检测碰撞，不擅改业务文本比较语义。参数只绑定值，标识符经内部 allowlist 与 Npgsql 引用 API 生成。[PG 标识符](https://www.postgresql.org/docs/18/sql-syntax-lexical.html)、[Npgsql 参数](https://www.npgsql.org/doc/basic-usage.html)。

## 5. 数据库权限及共享行隔离

本版选择**每宿主/数据库/租户的专用 LOGIN 角色和专用连接池**。普通 tenant 角色不拥有其他 tenant 的 USAGE/DML，也不是其角色成员。一个具备全部租户成员资格的公共 LOGIN 再 SET ROLE 不能作为此隔离证明；角色切换权限取决于登录角色及成员链。[SET ROLE](https://www.postgresql.org/docs/18/sql-set-role.html)。

| 身份 | 允许 | 禁止 |
|---|---|---|
| tenant runtime | 本 schema 必要表的逐动作 DML、必要 sequence USAGE；共享 tenant 行的受限 DML；获准只读公共视图 | schema/database CREATE、TEMP、对象所有权、TRUNCATE、角色管理、其他租户权限、BYPASSRLS、超级用户、迁移与备份凭据 |
| host authentication/control | 精确目录/认证/平台注册职责，受控当前权限与审计 | 自动借给业务请求；任意跨域业务更新；把平台管理员 claim 当作数据库所有者凭据 |
| tenant delivery worker | 该 tenant/queueDomain 的 claim/发送状态和受控恢复能力 | 由原 requester 冒充当前 service；普通 worker 持有全租户业务账权限 |
| migration owner（NOLOGIN）+独立 migrator | 创建授权对象、前向迁移、准确目标的迁移记录 | 被 runtime 继承；把其查询结果作为运行角色隔离证据 |
| backup/restore operator | 获准快照/恢复、完整性核验，活动均审计 | 常驻应用进程；把绕过 RLS 的身份当普通租户测试主体 |

新布局安装时审计并收紧数据库及 public schema 的 PUBLIC 默认权限、TEMP、函数 EXECUTE 和所有者默认 ACL。扩展/函数放受控 schema；runtime 不创建对象。只通过可审查的对象清单授权，新增表默认不开放，迁移显式补所需表/列/sequence 权限。schema 是命名空间，跨 schema 是否能访问仍取决于实际权限；`search_path` 不阻止显式限定访问。[PG schema](https://www.postgresql.org/docs/18/ddl-schemas.html)。

共享的 tenant 数据表强制 ENABLE/FORCE ROW LEVEL SECURITY：SELECT/DELETE 用 USING 限制旧行，INSERT 用 WITH CHECK 限制新行，UPDATE 必须同时具备 USING（旧行属于当前租户）与 WITH CHECK（新行仍属于当前租户）。四类操作的有效策略都必须保留这些边界；缺少明确适用策略时不开放。UPDATE 的旧行保护不能依赖 SELECT 策略：不读取旧列、没有 WHERE/RETURNING 的常量赋值也要排除他租户行，不能将 B 行改属 A 后仅凭新行检查通过。复核实际生效的 ALL/专属及 permissive/restrictive 策略组合，不允许额外 permissive 策略经 OR 放宽边界。[PG CREATE POLICY](https://www.postgresql.org/docs/18/sql-createpolicy.html)。

过滤依据是**受保护目录中 session_user 所绑定的 TenantId**，不是可由调用方改写的 `app.tenant_id`。运行角色不能修改该绑定或获得其他角色资格。目录查询由极小的无参数 SECURITY DEFINER helper 返回本连接 tenant 或拒绝：固定受控 search_path（pg_catalog、受控 schema、pg_temp 最后）、完全限定对象、撤销 PUBLIC EXECUTE、Owner 只可读取映射、无动态 SQL、无任意 tenant 参数；不将 helper 标为 IMMUTABLE。缺绑定必须拒绝，不能回默认值。[函数安全](https://www.postgresql.org/docs/18/sql-createfunction.html)。

同租户表另保 TenantId NOT NULL 及与绑定租户相等的 CHECK/写入守卫、原复合 FK/唯一键。RLS 不覆盖 TRUNCATE，超级用户/BYPASSRLS 可绕过；唯一/FK 检查可能暴露冲突存在性，API 统一安全错误，不返回数据库 DETAIL 或他租户键值。[PG RLS](https://www.postgresql.org/docs/18/ddl-rowsecurity.html)。

信任边界：数据库约束限制单个 tenant 凭据的权限；可信宿主仍决定取哪个凭据。此方案不承诺防御已完全控制宿主进程、密钥库或数据库超级用户的攻击者。若将来为减少池数使用全租户运行账号，那是另一隔离等级/设计版本，不能沿用本版“数据库拒绝显式跨 schema”验收结论。

## 6. 连接、模型与事务生命周期

```mermaid
flowchart LR
    R[可信请求或独立任务 scope] --> B[已登记 tenant 与存储绑定]
    B --> P[该 tenant 的 DataSource]
    P --> T[单连接 PostgreSQL 事务]
    T --> A[Owner 当前授权和原operation]
    A --> W[租户业务账 + 共享 Inbox/Outbox + 必需审计]
    W --> C[确认 commit 后返回结果]
```

1. DataSource 键至少包含 host/databaseRef/tenantId/runtimePrincipalRef/credentialVersion；绑定不可变地进入新 DbContext，禁止在已跟踪实体的 context 中切 TenantScope。每任务 scope 独立，结束清理 AsyncLocal/请求证据。
2. 连接打开后核数据库身份与 session_user；A 的物理连接不能出借给 B。所有业务命令使用限定 schema；会话默认 search_path 仅 pg_catalog，事务内如确需旧未限定 SQL，只由登记 adapter 设置 `SET LOCAL` 的受控路径，且这些 schema 无 runtime CREATE。共享表始终完全限定。
3. 使用显式本地事务，包括需要上下文的只读操作；可用 `set_config(..., true)` 写入仅诊断用 tenant/binding，不把 GUC 当凭据。SET LOCAL 在事务结束或回滚到更早保存点时会复原，故初始化先于后续业务保存点，回滚后重新核上下文。[SET](https://www.postgresql.org/docs/18/sql-set.html)。
4. commit/rollback/cancel 均 finally 释放事务、连接和 scope。保留 Npgsql 默认状态重置，禁止 `No Reset On Close=true`；不启用 multiplexing 来承载此事务模式。清理失败的连接不得继续使用。数据库池与 EF DbContext 池是两件事；本版不跨租户池化 DbContext。[连接配置](https://www.npgsql.org/doc/connection-string-parameters.html)。
5. `40001`/`40P01` 以原 operation/message 和原输入重建整个事务，重新解析当前授权/绑定，有限重试并保留失败记录；不能仅重放最后一句 SQL。取消并不证明 commit 未发生；提交结果 UNKNOWN 先查原位置的 receipt/Inbox/Outbox，不造新业务键，也不将 NOT_FOUND 副本当未执行证据。
6. 同一连接池归属 A 的物理连接允许 A→A 复用；同一进程 A→B→A 应路由到各自池。验收仍必须**强制尝试把 A 连接给 B**并断言在业务 SQL 前拒绝，不能把这个场景改成跳过。公共 role+SET ROLE 的同物理连接方案不属本版。

EF Core 不提供 schema-per-tenant 的完整迁移支持，本方案属于应用自管适配。[EF 多租户](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)。采用明确模型映射：租户表逐一 ToTable(table,schema)，共享表固定 schema；`HasDefaultSchema` 不会覆盖现有显式 crm/crm_v1/queue 映射。自定义模型缓存键至少含 context 类型、provider、layout/model version、schema、designTime；不得缓存首租户模型供其他租户使用，查询过滤引用 context 实例值。[EF 动态模型](https://learn.microsoft.com/en-us/ef/core/modeling/dynamic-model)。

EF 模型数和 DataSource 数随活跃租户增长，须有有界缓存、空闲池释放、凭据轮换后 drain、最大连接总预算。初期禁用未证明能正确区分 schema 的 compiled model/compiled query 共享；不以替换任意 SQL 字符串实现 schema 绑定。后续模板映射优化另经同样验收。

## 7. Owner、消息和撤权的共同提交边界

单次 Owner 新效果的业务行、原 operation/receipt、必需审计、Inbox/Outbox/内容观察必须位于同一**物理数据库、连接及 DbTransaction**。不同 schema 没有破坏该条件；两个池、两个连接或 Main 与 CRM 两库不满足它。多 DbContext 必须明确共享连接并 `UseTransaction`，事务生命周期由 Owner 持有；Platform 不再创建或提前提交另一事务。[EF 跨 context 事务](https://learn.microsoft.com/en-us/ef/core/saving/transactions)。

先按原合同的恢复/读权定位已有终态；新效果才进入当前授权最终门。绑定版本检查、租户暂停/撤权写者与业务写者使用同一真实 fence 至 commit。共享控制行需要锁而不该给 runtime 修改权时，使用窄的受控锁函数，仅锁本 tenant/登记 scope，无任意更新参数；不可为获得 FOR UPDATE 顺便授予修改租户状态的权力。具体 PostgreSQL 行锁/事务级 advisory lock 由 Owner 登记统一顺序，不能把 SQL Server sp_getapplock/锁提示原样复制。[PG 锁](https://www.postgresql.org/docs/18/explicit-locking.html)。

- CRM 身份沿已接受顺序：ReconciliationState → PublishedState → Projection → Inbox → Checkpoint → Observation/Conflict；新鲜、重复、旧版、同版、唯一冲突后重读及重试均先经内容门。commit 未确认不得 ACK SUCCESS。
- Main ERP 保持原 CommandInbox/Request/业务版本路径；通用消息 checkpoint 不能吞掉 command。Main identity priority 保独立预算，与身份变更一起提交。
- 共享消息表的跨 tenant 同键冲突不允许读不到旧行就当新消息。受控接收者只在真实注册和原绑定范围作 TransportIdentityConflict 隔离；普通 runtime 没有权限完成该隔离时交给受控恢复入口，当前请求不成功、不推进 checkpoint。
- broker/HTTP/邮件在 commit 后；已提交结果技术重投、可信事实接收、当前历史读取、新业务写四类权限分别判断。暂停 tenant 不清 Inbox/Outbox，不把旧 requester 的权限作为当前服务身份。
- Main 权威与 CRM 投影跨库传播不宣称一个 ACID 事务。按原 S6/Owner 最终门合同采用；缺真实共同门/健康投影的相应新效果继续关闭，不能用 TTL 或 schema 隔离代替。

## 8. 迁移、生命周期和失败恢复

每宿主两级版本：共享模型/队列域迁移历史独立；每个租户 schema 内有其自身迁移历史。共享目录存迁移运行记录（tenant、model/version、migrationId、script hash、状态、原operation、实际结果引用），只能是索引，不能掩盖 schema 内真实历史。Main 的 Core、ERP、身份优先等不同迁移 lineage 保留，不能只建一个同名历史表互相覆盖。

迁移器以发布包内固定的 PG 模板/manifest 为源，为可信 schema 生成有 hash 的实际脚本；不能通过对旧 SQL 文本全局替换租户名完成迁移。锁使用数据库内按 host/tenant/lineage 唯一的迁移控制行或已登记无歧义锁键，跨进程生效；同租户同 lineage 串行，共享阶段必须先完成。设置有界等待/语句时间，锁拿不到则保留未开始，不删除历史“修复”。

事务型 DDL 的对象变更与该步骤成功历史同事务提交；中断后先核真实目录与脚本 hash，已成功不重复。不能事务执行的操作（例如并发建索引）单独步骤留开始/完成/目录有效性证据，恢复时识别无效对象并按具名修复方案处理，不把无效索引计为完成。共享失败关闭依赖新版本的全部路由；单租户失败仅关闭该 tenant 的不兼容路径，其他租户继续使用已验证版本。

| 动作 | 有效流程 |
|---|---|
| 新租户 | Main 按原合同一次创建 Tenant/首用户/角色/关系/原receipt/强审计；存储另处 Provisioning。受控 provisioning 幂等建立 schema/角色/模板/历史，验证后置 Ready；Enable=true 本身不能开放尚未就绪存储 |
| 暂停 | 当前授权共同门内推进状态与代次并发布事实；数据和原结果保留；取消或拒绝后续新效果，技术重投和历史读按各自门 |
| 恢复 | 新权威版本、当前政策、健康映射/投影/模型满足后开放；不复活旧 token/旧授权，不自动重放待办 |
| 升级 | expand → 逐租户迁移/核对 → 切绑定版本 → 排空旧写者 → 后续具名 contract；不同版本的 reader/writer 允许矩阵随发布包提供 |
| 部分失败 | 保原 operation、失败日志、真实对象状态、未迁租户清单；修复后只继续明确步骤；不能重造另一份权威布局 |
| 应用回退 | 只回到与当前 schema/原结果/消息合同兼容的应用版本；数据库只前向修复，不能默认 Down migration |
| 退役 | Retired 停新写，保审计/保留/恢复责任；删除 schema/数据/角色为另行授权任务 |

## 9. 单租户导出、恢复和全局恢复

单 tenant 备份单元是**业务 schema + 同快照的共享 tenant 行切片 + 不可变外部资产/密钥引用 + 权威版本/路由/消息清单**。仅 `pg_dump --schema` 不会自动包含所有依赖，也不导出共享表中该 tenant 的行；不能声称单 schema 文件就是完整恢复包。[pg_dump](https://www.postgresql.org/docs/18/app-pgdump.html)。

导出由受控工具在一致快照下读取：暂停/排空该 tenant 的可变写集和 dispatcher，或证明所有导出读取共享同一数据库快照；导出业务、IAM/认证关联、原 operation/receipt、全域 Inbox/Outbox/companion/route epoch、审计及 legal hold，记录对象数量/摘要、共享定义版本、资产 manifest 和加密引用。跨库 Main/CRM 不能伪称同一个快照，须保来源水位及恢复对账窗口。全局目录、公共定义、主密钥只留版本/依赖，不通过单 tenant 恢复覆盖其他租户。

恢复先在隔离 staging 库进行：核 manifest/版本/外键/精度/原字节/租户恒等、角色 ACL/RLS 与引用资产。回原 tenant 时使用原 TenantId，不把历史改属新组织；演示克隆另有脱敏与新身份合同。生产切换前必须关闭目标 tenant 新写和发布，核受影响共享切片及全局唯一键碰撞，按批准迁移窗口原子切换或在关闭窗口完成可恢复的多步导入；其他 tenant 行/队列/目录摘要保持不变。

恢复旧备份不能简单回退当前 IAM 撤权代次、有效凭据、消息已发送事实或 route epoch。当前权威与备份之间的差量、旧 lease、已发送但备份中 Pending、当前原结果均需对账；保持 UNKNOWN/隔离直到原 Owner 证据足够，禁止恢复后自动重放。共享 token/消息全局键若碰撞则关闭目标恢复，不能随机换键。无法取得完整共享切片/水位时保持恢复不就绪。

全库 PITR/物理恢复会影响所有 tenant，不作为“原地回退单租户”的手段；可先在独立实例恢复到指定点，再受控提取目标切片。全局灾备另含角色/ACL、共享目录、密钥、外部资产和 Main/CRM 对账。RPO/RTO、恢复吞吐和数据量本轮均未测，不能承诺生产恢复时限。

## 10. 容量假设与可复现测量门

本版没有生产租户数、吞吐或升级时长实测。采用前在独占 PG 测试库按 `N=10/100/1000` 的**候选测量档位**运行；这些数不是已批准容量。使用真实宿主模型的对象数 T、迁移包和标注为合成的数据规模，分别记录空闲/活跃 tenant 数、每 tenant QPS、每进程 P、每池上限 C、max_connections 保留额、CPU/RAM/磁盘/PG版本。

- 对象规模约为共享对象 + N×T；分别采集 pg_class/目录体积、冷/热查询、模型构建时间及进程内存。
- 最坏连接预算近似 P×活跃池数×C，再加控制/迁移/worker/运维预留；记录峰值与等待分位，不照搬 Npgsql 默认每池上限作为全局预算。
- 记录单 tenant 迁移时长 t、并发迁移数 K、共享阶段耗时及锁等待；`ceil(N/K)×t + shared` 仅为估算，不是结果。记录失败注入后的重试/恢复时长。
- 测同快照导出、staging 恢复、共享切片对账、对象清单/金额极值恢复；确定可用容量与 RPO/RTO 后才能给运行配置设上线限额。

若角色/池/模型/目录成本超限，先缩小部署单元或提出下一设计版本并保持合同；不能默默换成全租户高权限账号、丢 TenantId 或取消原子写集。

## 11. 兼容阶段及任务交接

| 阶段/责任任务 | 可消费内容 | 开放条件 |
|---|---|---|
| C0：本文 S02-00 | cp6-pg-tenancy-v1 设计、准确来源、验收矩阵 | 只证明文档；协调登记/合并前仍不是已交付前置 |
| C1：WP02-01/02/03/04 | 可信上下文、Main 存储映射/权限/EF/迁移、最终门、租户生命周期 | 独占新库真实 PG 正反验收，完整 writer/object 清单；不迁既有业务数据 |
| C1：WP02-06/07/08/09/10 | CRM PG 身份存储、调用方 Inbox、Outbox v2、两端各自 Owner 原子写集 | 每仓准确包/源码/运行证据；CRM 当前 SQL Server 实现不能记为 PG 已采用 |
| C2：既有数据迁移与切换独立任务 | 源→目标无损映射、旧写者/旧路由清单、对账/恢复方案 | 单独领取和授权、相应真实验收与正式发布门；不包含在 S02-00 |

S01 纯类型/decoder/精度测试可以继续；真实 Owner 存储采用必须消费本设计和所属任务的已交付能力。精度按 S01 各原生 profile，不压入 .NET decimal 或改原 JSON；旧 SQL Server 历史记录保留，后续验收只要求 PostgreSQL。

## 12. 验收与文档验证

[postgresql-tenancy.json](../../development/acceptance/postgresql-tenancy.json)定义 actor、请求、前置、步骤、预期读写/拒绝、证据及负责后续任务。包含普通角色直接 SQL、A/B 连接错误出借、EF 缓存串租、共享消息唯一冲突、提交 UNKNOWN、撤权/后台恢复、迁移中断与完整单租户恢复。用管理员查询观察数据时，不能替代普通角色的拒绝断言。

文档阶段仅检查链接、来源字节身份、源码引用、JSON 结构及集中语义审查。不会创建数据库/schema/角色、运行业务测试、启动 Actions 或部署。原模块验收与本新增矩阵都保持原状态；静态检查 PASS 只归本任务文档证据，不提升任何运行 case。
