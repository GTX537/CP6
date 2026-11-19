# WP02-18 工作台菜单与授权 KPI 交付

接收 #333，使用 Main DEV-1 和 `codex/wp02-18`。准入基线 `06e5a38c4c5be6cfedd00462a9b59f8e341dae79`，产品提交 `44e645255379227a8547d66c79953613e97fdfa5`，核销 [PR 1272](https://github.com/GTX537/CP6/pull/1272)。原派工和 START 均已按原通信分支回读。#243 的历史受限窗口登记保存在本卡历史字段。唯一前置 WP02-03 的受审提交与合并提交已包含于该基线。[派工 #333](https://github.com/GTX537/CP6.AI-Handoff/pull/333) · [START](https://github.com/GTX537/CP6.AI-Handoff/commit/743690248d282b077d8cb4a4185cc6b1573a2cca) · [前置核销 #1237](https://github.com/GTX537/CP6/pull/1237)

## 实现与边界

- 现行菜单维护页面用共享树版本编辑旧节点和新节点。服务端校验内部路由登记、权限键、父级、引用和操作权。影响预览列出受影响租户及角色，确认后按同版本保存。412 后保留草稿，核对差异后可重基。确认丢失时按原操作号回查。停用保留历史关联，现行删除入口要求先停用并处理引用。W664 原封存协议及其发行证据用于明确历史客户端兼容边界。[共享树证据](../W266-01/DELIVERY.md) · [展示与审计](../W267-01/DELIVERY.md) · [既有 W664 证据](../W664-01/DELIVERY.md)
- 个性配置按租户与当前用户保存指标显示和顺序。原生 PostgreSQL 同事务保存 CAS 版本与原操作回执。同键异载荷拒绝。名为同一用户或同号角色的其他租户各自读取其配置和授权。
- 两条角色旧 URI 的版本化请求使用同一个 IAM 写者，媒体类型为 `application/vnd.cp6.iam-role-policy+json`，载荷沿用 `IamRolePolicyCommandV1`。旧媒体类型的未版本化请求继续走 WP02-03 拒绝口径。当前导航读采用实际角色和共享树版本。200 空菜单与 503 授权依赖未就绪分开。应用中的指标明细路由每次经原 Owner 对象范围和字段边界重新读。
- 旧 `GET /api/dashboard` 提供历史 Main 租户级汇总合同；独立 `/api/dashboard/scoped-v1` 显示当前身份可读的八个指标。缓存按租户、主体、授权版本、数据库可见性及业务时区分区。独立库存存储直接读自身 Owner。明细带指标版本和原统计时间，分页仍在当前授权范围内。
- 工作台审批入口使用 OA 的已登记查询页，应用结论由连续原回执给出。已登记的 CRM 跨系统导航携带固定入口及登记域名，CRM 独立建立当前会话。只有验证后的配置源可解析外部地址。CRM 工作区入口取自远端 CRM `5a3d272cea2579aaa904cec5db68d0b021932508`。本卡在本地用模拟 CRM 目标复验导航，部署配置另外管理。[CRM 入口](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/apps/web/app/page.tsx)

## 原验收逐项核对

原目录逐项按 `source_case_id` 与原始设计版本 v0.1 对照。原件 SHA-256 为 `fc9da230a15d2a778d410d750c8aa63a1aa056ef8451c07d35a9802cb9c165a9`。本卡 20 项，目标必需修订 0 项。前后端静态路由登记 189 条逐项一致。[唯一原验收目录](../../assessments/20261011/catalogs/foundation.json) · [工作台设计索引](../../../design-handbook/modules/SYS-WORKSPACE-01.md)

| 原验收 | 结果、入口与证据 |
|---|---|
| AC-WS01-01 | PASS，自引用、环、缺父项及重复 ID 均拒绝，比较事务前后树与版本无变化。 Menu_tree_v1_rejects_invalid_structure_and_native_ABA_changes_revision |
| AC-WS01-02 | PASS，共享预览列出多个租户与角色，复核当前平台操作权。共享停用使同一外租户新读不再含该节点，菜单版本推进。个性配置隔离同名两租户。 Menu_tree_v1_partial_move_global_impact_original_and_concurrent_CAS / Workspace_menu_delete / Workspace_preferences |
| AC-WS01-03 | PASS，菜单和个性配置均以版本前置拒绝并发输家。菜单差异复核保留原草稿；Chrome 捕获实际配置提交后丢失确认，按原键只读获得完全相同回执。 MenuView.spec.ts / WorkspaceMetrics.spec.ts / Workspace_browser_actual |
| AC-WS01-04 | PASS，未处理的跨租户授予及子项阻止现行删除。启用节点须先停用。停用后既有角色映射保留，当前导航版本推进。 Workspace_menu_delete_checks_all_tenant_references_before_removing_nodes |
| AC-WS02-01 | PASS，同一浏览器可见工作台，缺少原动作仍无指标授权。W1 结果排除 W2 记录。单价隐藏策略下只有允许的统计字段参与计算。旧节点编辑用版本化命令，拒绝未登记路由和有引用的权限键改名。 Workspace_browser_actual / Workspace_dashboard_eight_metrics / Workspace_menu_definition_http |
| AC-WS02-02 | PASS，同一 IAM Owner 从两原 URI 接收版本化命令。角色版本推进后，另一个旧前置 412。当前授权撤除且角色同号的第二租户不变，未版本化旧写维持拒绝。 Workspace_navigation_legacy_role_routes_share_cas_and_never_cross_tenants / Actual_policy_HTTP_closes_legacy_bypasses |
| AC-WS02-03 | PASS，已认证无授予得到 200 与空菜单、空动作；同一会话在隔离库导航依赖暂不可用时得到 503。 Workspace_navigation_legacy_role_routes_share_cas_and_never_cross_tenants |
| AC-WS02-04 | PASS，撤除查询动作后当前卡片 HIDDEN，同一详情接口 403。Chrome 使用同名第二租户的实际新会话重开链接仍被拒绝；页面测试验证旧详情在重新授权失败后移除。 Workspace_dashboard_eight_metrics / Workspace_browser_actual / WorkspaceMetrics.spec.ts |
| AC-WS03-01 | PASS，八项用实际 Owner 重读租户、主体、对象范围和字段策略。相同角色号的另租户读取独立，已读取的旧结果不会使撤权后可读。 Workspace_dashboard_eight_metrics / CurrentPermissionConsumption |
| AC-WS03-02 | PASS，已授权真实零、缺权限、订单部门绑定缺失、旧到期查询分属四状态，缺失订单依赖时产品值仍为 3。页面可独立重读失败卡。 Workspace_dashboard_eight_metrics / WorkspaceMetrics.spec.ts |
| AC-WS03-03 | PASS，页面测试暂停模拟租户 A 的响应，切到 B 后 A 的请求取消；A 晚到 700 时显示仍为 B 的 2。Chrome 使用真实身份重新登录并校验原深链。 WorkspaceMetrics.spec.ts / Workspace_browser_actual |
| AC-WS03-04 | PASS，旧摘要 4 项历史合同与租户隔离测试通过；现行工作台走独立 scoped-v1 请求，每卡无资格或缺源各自处理。 DashboardTenantIsolationTests / Workspace_browser_actual |
| AC-WS04-01 | PASS，八项逐表、字段、状态和业务日窗口对照原设计，授权夹具依次得到 1、1、2、2、3、1、2、3。 Workspace_dashboard_eight_metrics / DashboardTenantIsolationTests |
| AC-WS04-02 | PASS，定义输出原 Owner、来源、选择条件、时钟与版本 1。原旧摘要仍能对照历史查询口径。 Workspace_dashboard_eight_metrics / DashboardTenantIsolationTests |
| AC-WS04-03 | PASS，八项实际钻取与原请求版本、时间窗和授权总量一致。订单来源异常返回 UNAVAILABLE 与空值，产品继续读取。 Workspace_dashboard_eight_metrics |
| AC-WS04-04 | PASS，租户、仓库范围和软删除在聚合前处理；受限单价和模拟匿名客户标识均未返回，传入客户筛选参数也未改变口径。行按允许引用输出，拒绝的卡不给数。 Workspace_dashboard_eight_metrics / CurrentPermissionConsumption |
| AC-WS05-01 | PASS，明细保留独立的业务 Owner、原 ID 与实际版本。浏览器地址仅有登记指标、版本、统计时间。 Workspace_dashboard_eight_metrics / Workspace_browser_actual |
| AC-WS05-02 | PASS，直接重开目标页会向原 Owner 发新读请求；撤权和另租户均拒绝，目标无原对象内容。 Workspace_dashboard_eight_metrics / Workspace_browser_actual / WorkspaceMetrics.spec.ts |
| AC-WS05-03 | PASS，已登记 CRM 打开固定工作区，模拟目的站收到零 Main Cookie、Authorization 或 Referer。不认可未登记目标和自带跳转地址。 Workspace_navigation_legacy_role_routes / Workspace_browser_actual |
| AC-WS05-04 | PASS，工作台链接到 OA 已登记查询。模拟生产者提交真实回执前，流程已批准仍为 UNPROVEN；连续原应用回执后业务结论和原引用才更新。 NativeQuery_APPROVED_is_UNPROVEN_until_real_continuous_Owner_receipts_are_ingested / NativeFormQuery.spec.ts |

## 八项口径实测

PostgreSQL 测试用应用本地当日与月初，业务状态取原表合约。受限仓库、删除记录、对象范围及同名跨租户都使用显式夹具。订单金额和单价受当期字段策略控制，明细仅返回可读对象的原 Owner、ID 和版本。当前数据变化后，源端可按同窗口返回不同读取时刻。

| 指标 | 原来源和纳入条件 | 授权夹具实测 |
|---|---|---:|
| TodayOrders | `T_Order.CreateDate` 从应用今日零点 | 1 |
| MonthOrders | `T_Order.CreateDate` 从应用月初 | 1 |
| ActiveWorkOrders | `T_WorkOrder.Status` 1 到 5 | 2 |
| MonthCompleted | 状态 4 或 6，`ActualEndDate` 从应用月初 | 2 |
| PendingOutbound | `T_OutboundOrder.Status` 1 到 3 | 3 |
| StockWarnings | `T_Stock.AvailableQty` 小于或等于 0 | 1 |
| PendingApprovals | `T_ProductMaster.Status=0` 加 `T_StockTake.Status=3` | 2 |
| TotalProducts | `T_ProductMaster` 可读产品 | 3 |

## 验证与采用

- 本地 .NET 与 PostgreSQL 18.6。原生命令、当前授权及 Owner 回归 22 项，当前权限消费 9 项，旧报表 4 项。隔离配置及真实 Main、Chrome 的工作台链路包含在原生回归中。网页单测 79 项，Vue 类型检查和 Vite 构建成功。测试脚本、实际 TRX、Chrome 记录与数字快照保存在 `D:/CP6-test-artifacts/WP02-18/current-20261119`。
- 两租户、同号角色、登录用户、订单、产品、盘点、仓库和消息结果用测试数据模拟。真实 Main API、角色写者、菜单与个性表、SQL 和 Chrome 运行在本地。确认丢失由浏览器在实际提交后注入。订单来源的部门绑定缺失由隔离 PostgreSQL 配置实测，迟到响应由 Vue 夹具控制。OA 应用回执用原 Owner 合同的模拟生产者，CRM 用独立模拟目标。
- 类型检查与浏览器回归使用当前工作树的源码及匹配的依赖锁。W266/W267 的实现与原作业引用按主线复验。当前菜单和权限定向验证使用上述 PostgreSQL 证据。旧仪表板继续对照历史结果，回归原查询 4 项。
- 台账及四份状态页由台账工具生成。采用提交、合并后冒烟、席位状态由同卡 DELIVERY 和官方 `finish.json` 补齐。GitHub PR 主线与候选的适用事件和变更路径另行核查。生产部署状态独立于本卡核销。[台账](../../roadmap.json) · [历史仪表板证据](dashboard-states/DELIVERY.md)

## 工时和席位

实际领取 `2026-11-19T05:24:54.416078-05:00`，技术验证完成 `2026-11-19T06:16:02.872851-05:00`。本卡有效 **3068.457 秒**，预算 14,400 秒，强制拆卡点 21,600 秒。原目标核对 **97.893 秒** 从目标池单列。按 #333 一次结转 WP02-20 **249.883 秒**，前卡新合计 **2,736.406 秒**。计划累计 **21.440 小时**。后续队列仅登记 `WP02-17`。[前卡结转确认 #332](https://github.com/GTX537/CP6.AI-Handoff/pull/332)

历史 `codex/wp02-18-dashboard-states-20261016` 两处引用经授权，在仍等于 `e89198f960a61327a623c6b22a0dcb96469d3b22` 时定向清理并回读。业务分支由官方 `finish` 按实际采用核对。工作区状态与分支处置随 DELIVERY 公布。[历史 #232](https://github.com/GTX537/CP6/pull/232)
