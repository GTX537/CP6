# W531-01 · PGT-017 事务栅栏核对与工作流写入修复

2026-11-13 UTC，Main 基线 `f370917c4425dea32fe77f72437eb2010cca4794`，本地实测源码 `0afcfd6b1c80a2dfa480c980589177f9c02e48c1`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，剩余 **PGT-017**。原询盘 **60/60**。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1000](https://github.com/GTX537/CP6/issues/1000)

## 实际 PostgreSQL 验证

Main Kestrel、PostgreSQL 18.6、.NET SDK 8.0.424、EF Core 8.0.30、Npgsql 8.0.8、Platform 0.10.4。隔离实例 55593，使用模拟租户、真实登录 Cookie 与 CSRF、独立数据库阻塞/观察连接。订单和工作流场景使用正常 HTTP 用户与测试宿主的 Core 数据库身份；普通库存连接另用受限租户 PostgreSQL 登录。实际等待通过 `pg_blocking_pids` 和查询文本判定。

| 竞争 | 业务先取得事务根 | 暂停或撤权先取得事务根 |
| --- | --- | --- |
| 版本化租户暂停与普通库存 V1 ADJ | 普通库存事务持根等待库存表，暂停等该事务。库存、交易、原回执、来源和通知全部提交并保留。 | 暂停持根等待租户行，库存等暂停。库存请求及后续旧会话均 401。认证政策和租户管理版本、身份快照、优先 Outbox、审计及回执与暂停一起推进。 |
| IAM 动作撤销与订单单价更正 V1 | 更正持根等待订单行，IAM 等更正。精确单价、工作流意图、原回执和审计均提交。保留 `query` 权限的旧会话仍可按原键读取和重放相同结果，新键返回 403。 | IAM 持根等待 `Sys_RoleAction`，更正等 IAM。新写 403，订单明细、意图、回执和审计写集前后相等。两个顺序中，IAM 角色版本更新、身份快照和代次各加 1，同版本 Outbox、审计和原回执各一条。 |
| IAM 动作撤销与工作日历写入 | 日历写持根等待日历行，IAM 等日历。合法修改提交，旧会话后续请求返回 403。 | IAM 持根等待授权表，日历、委派及审批人映射的 9 个写入口均在根上等待，随后 403，目标写集不变。9 个入口均执行过正常写入。 |

旧 9 个入口只在事务外鉴权。部分写表触发器在 DML 时才取锁。修复后，9 个已核对入口沿用原动作键，在请求的 Core 事务中获取 IAM 根并读取当前会话与权限，持锁到提交。提交前再确认会话。取消及异常走原 IAM 回滚和未知结果语义。原业务输入、响应和合法模拟会话沿用既有认证判定。 [事务接入](../../../../CP6.WebApi/Filters/WorkflowWriteAuthorityAttribute.cs) · [真实 IAM 与改价](../../../../CP6.Tests/Erp/Pgt017IamRevocationPostgreSqlTests.cs) · [九个入口回归](../../../../CP6.Tests/Erp/Pgt017LegacyAuthorityPostgreSqlTests.cs) · [库存与暂停](../W435-01/DELIVERY.md)

- `baseline.trx` 在旧产品上执行 11 项，订单两种顺序通过，9 个旧入口均返回 200 并使回归失败。`fix-01.trx` 验证修复后 15 项 PostgreSQL 全部通过，另外 2 项静态路由测试通过、2 项失败；旧 W529 同源码制品实测相同失败（控制器计数及旧权限约定）。
- 最终 `final.trx` 40 通过、4 失败、零跳过。其中 **16 项 PostgreSQL 全部通过**，包括 12 项本任务用例及 Stock、WF_AUTH 原生覆盖。其余 24 项通过为 ApproverMap、AdvancedFlow、RequirePermissionFilter 的既有组件测试。4 项旧 SQLite 日历夹具在建表时报缺少 `Latin1_General_100_BIN2`，W529 同相关源码的已存制品重现同 4 项失败。正式日历行为采用上述 PostgreSQL HTTP 结果。
- 构建 0 错误，保留原依赖审计警告。实际测试宿主的 WebApi SHA-256 为 `771bbd8d4358f8bda8a686646e720105434c8b68313e594024b3a24034ace811`。本地 publish 与该宿主程序集一致，310 个发布文件摘要保存于 `D:/CP6-test-artifacts/W531-01/publish-sha256.json`。发布制品初次启动缺模拟 Space 文件目录配置，按现有夹具补齐后在独占实例与端口重新启动，存活 200、匿名访问受保护入口 401。原始 TRX、等待与写集、发布冒烟和停止记录均在任务工件目录。隔离实例及宿主已停止。

## 已核对的共同栅栏

Main 的 `cp6_authentication_v1.acquire()` 最终更新 `cp6_platform_admin_guard_v1.root` 单例行。事务根的 revision 用于串行化，各权限代次由 IAM、租户或 `WF_AUTH` 对应权威数据提供。持锁到提交，等待后的 Read Committed 查询读取当时已提交权限。 [根实现](../../../../CP6.Persistence.PostgreSql/PlatformAdminGuardPostgreSqlV1.cs) · [PostgreSQL 行锁](https://www.postgresql.org/docs/18/explicit-locking.html) · [Read Committed](https://www.postgresql.org/docs/18/transaction-iso.html)

| 已核对写者 | 实际数据库边界 |
| --- | --- |
| 版本化租户管理、IAM 用户/部门/角色、角色策略和动作目录 | 租户管理和 IAM 事务先获取共同根，再核对当前操作者。当前角色功能、数据和字段权限由版本化 IAM 保存。旧角色和旧授权入口在 PostgreSQL 拒写或转入版本化命令；旧菜单和 2FA 写入的不足见下。 [租户管理](../../../../CP6.Core/Services/Platform/TenantAdminService.Versioned.cs) · [IAM 事务](../../../../CP6.Core/Services/Sys/IamAdministrationV1.cs) |
| 原生授权 DML 和 WF_AUTH | `Sys_Tenants`、`Sys_Users`、`Sys_TenantSsoConfigs`、`Sys_Roles`、`Sys_UserRole`、`Sys_RoleMenus`、`Sys_RoleAction`、`Sys_RoleDataScope`、`Sys_RoleFieldPerm`、`Sys_MenuAction`、`Sys_Menus`、`Sys_Dept`、`Wf_FlowDelegate`、`Wf_ApproverMap` 有语句级触发器，先取共同根，再推进同库 Scope 代次。版本化 WF_AUTH 注册/撤权和原生消费者在共同根后锁租户、环境及域的映射行。部门对象映射、询盘范围、SSO 和设备会话来源各有根保护及适用版本。 [14 表注册](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261028060000_ScopeAuthorizationFenceV1.cs) · [实际 Scope 证据](../W171-01/DELIVERY.md) |
| 订单、Operations、普通库存和 RMA Owner | 订单和 Operations 在实际写事务中先取共同根，再重验当前权限和业务对象。普通 Stock 调整/移库/关闭的应用入口通过 `lock_graph` 获取根。已启用的普通 RMA 草稿/提交/关闭通过 `lock_draft` 获取同一主库的根，v1/v2 共用机制，已佣用数据拒绝旧共享写入。 [Operations](../../../../CP6.Core/Services/Integration/OperationsOwnerV1.cs) · [Stock 应用写事务](../../../../CP6.Core/Services/Integration/OperationsOwnerV1.StockOperation.cs) · [RMA 普通写事务](../../../../CP6.Core/Persistence/Tenancy/TenantRmaReadStore.Draft.cs) · [同库混合模型](../W528-01/DELIVERY.md) |
| 已采用的 WMS 和 Space 入口 | 设备、托盘、条码、标签、WCS、IoT、承运、卷材、余料、角色范围、功能变更及已交付的 Space 站点/楼层/拓扑、编码、场景和库位发布，复用 IAM、Operations 或同一原生根。各原报告按实际动作核对，Space 其他路径见下。 [WMS 角色范围](../BUG-901-WMS-ROLE-SCOPE-FENCE/DELIVERY.md) · [Space 库位发布](../BUG-925-SPACE-LOCATION-PUBLISH-FENCE/DELIVERY.md) · [Space 拓扑](../BUG-933-SPACE-TOPOLOGY-DEACTIVATE-FENCE/DELIVERY.md) |
| CRM 询盘边界 | Main 在本库共同根内读取当前权限并签发。CRM 的业务事务和本地 C02 撤权消费者共享 CRM 库的租户身份行。两种 CRM 本地提交顺序已有证据。Main IAM 提交与 CRM PostgreSQL 业务提交各自有本地事务。 [W246](../W246-01/DELIVERY.md) |

## 仍待核销的正式前置

以 MVC 的 `IActionDescriptorCollectionProvider` 从实际构建程序集枚举 2,101 个路由描述符，1,078 个接受 POST、PUT、PATCH 或 DELETE，其中 567 个有静态动作权限，9 个应用本次栅栏。原始清单为 `D:/CP6-test-artifacts/W531-01/mvc-routes.json`。这些数用于定位候选；还需沿真实 DI 和启用配置核对读型 POST、旧路由拒绝、产生写入的 GET、最小 API、普通数据库角色及后台与外部入口。

| 已定位的范围 | 剩余事实 |
| --- | --- |
| 权威写入口 | 旧菜单新增/更新/树/删除、管理员 2FA 重置、租户 2FA 政策变更在 DML 才取得根，动作资格仍在根外判断。重置本人凭据和修改本租户政策可合法改变当前会话版本，需沿用已有提交完成约定。旧平台管理在根内重查账号及平台资格，完整会话检查由版本化路径提供。只携 access token 的旧注销依赖缓存 JTI，持久化刷新谱系另有数据库根；缓存注销不能据此列作同事务覆盖。 [旧菜单](../../../../CP6.WebApi/Controllers/Sys/MenuController.cs) · [2FA 重置](../../../../CP6.WebApi/Controllers/Sys/UserController.cs) · [2FA 政策](../../../../CP6.WebApi/Controllers/Sys/TwoFactorPolicyController.cs) · [缓存注销](../../../../CP6.Core/Services/Sys/CacheTokenBlacklistService.cs) |
| 现行普通业务 | 财务资产分类与过账、采购价表与订单、品目计划、旧 OA 审批/转交/撤回、WMS 油墨、Space 连接体/模板/分析、订单取号 GET 等仍有仅入口检查的可达路径。默认启动种子也可写日历，未纳入这 9 条 HTTP 证据。 [财务过账](../../../../CP6.Core/Services/Fin/JournalEntryService.cs) · [采购](../../../../CP6.Core/Services/Pur/PurchaseOrderService.cs) · [品目策略](../../../../CP6.Core/Services/Plan/ItemPlanningPolicyService.cs) · [旧审批](../../../../CP6.Core/Services/Oa/TaskDecisionService.cs) · [油墨](../../../../CP6.Core/Services/Wms/InkService.cs) · [Space 连接体](../../../../CP6.Core/Services/Space/ConnectorService.cs) · [订单取号](../../../../CP6.Core/Services/Common/DocNumber.cs) |
| 独立事务和数据库权限 | 银行凭证按行提交，采购收货组合 WMS 自管事务，旧流程触发有独立占位和失败回写，Space Design 使用同库的独立 Context 及 Serializable 事务。接入要在现有提交单元内进行；同物理事务可沿用共享 Context。Stock 应用持根，普通 PostgreSQL 登录的本租户直接 DML 授权仍需核对实际约束。映射启停也须按其真实权威逐项确认，已有同库 RMA 模型和队列纪元证据各有覆盖范围。 [银行](../../../../CP6.Core/Services/Fin/BankReconService.cs) · [采购收货](../../../../CP6.Core/Services/Pur/GoodsReceiptService.cs) · [触发](../../../../CP6.Core/Services/Wf/FlowTriggerService.cs) · [Space 事务](../../../../CP6.Space.Infrastructure/EfSpaceVersionClone.cs) · [共享事务](../../../../CP6.Core/Persistence/DatabaseSharedContext.cs) · [Stock SQL 授权](../../../../CP6.Core/Persistence/Tenancy/StockMovementWriterStorageV1.cs) |

本任务集中审查核对了成功行为、根获取顺序、等待后的拒绝、原结果、写集及适用边界。已定位旁路按所属 Owner 和原事务归属处理，余下全量写者登记和正式 PGT-017 保持待核销。Actions 启动与取消均为 0。发布冒烟只用本地制品和隔离实例。分支与固定席位最终处置以本任务 PR、远端 main 和 `D:/CP6-test-artifacts/W531-01/main-finish.json` 为准。
