# 第十八轮：公共基础单对话持续实施计划

> 已交付W18-01：PR276 / main `f58080a06df806b00e4f7da0ba2a64404253ee6b`，#275关闭。W18-02实际START已发布；沿同一目录与单总控串行接续。下方旧候选状态保留为历史。

> W18-01 当前进展：真实并发复现确认 P0 [#275](https://github.com/GTX537/CP6/issues/275)，已以 PostgreSQL 单语句快照修复。最终9项原生PG、46项权限、2项CRM投影通过且无跳过；取消与尾空格问题的失败记录保留。[单份交付/作者自审](../../evidence/WP02-03/consistent-permission-read/DELIVERY.md)可供正常集成，尚未合并/关闭Issue。W18-02/03/04未开工，持续目标仍active。

> 执行方式：按 executing-plans 在当前总控对话串行实施；用户明确要求不再分派新对话或子代理。任务状态以 `docs/development/roadmap.json` 为唯一来源，本文固定范围、顺序和验收条件。

**目标：** 在未来24小时优先完成当前授权读取、租户管理强审计、身份快照与消息一致性三个公共基础切片，并完成相关PostgreSQL/HTTP验收及逐任务正常合并。

**架构：** 沿用Main现有CP6Context、PermissionAggregator、TenantAdminService、IdentityChangeCapture/IdentitySnapshotWriter以及已接受的Platform正式包。每项修改先核对实际调用链和数据库事务，保留既有聚合、身份、租户、精度与消息合同；不建立第二套权威目录。

**技术栈：** .NET 8、EF Core、Npgsql、PostgreSQL 18、本地xUnit及ASP.NET Core HTTP验证。后续数据库唯一目标为PostgreSQL。

## 基线、工时与边界

开始时间：2026-10-18 15:08:30 UTC（America/New_York 11:08:30）；滚动检查点：2026-10-19 15:08:30 UTC。16～24小时是执行估算，包含修正、验收和集成，不是保证全部完成的日期。时间到仍未完成时保留实际状态和可恢复进度，不降低门禁，不将未完成目标标记完成。

已核对Main/main/origin/main：`b4516ce3b2e4098347ee9a0d89322fcd561a5da9`。第十七轮全部窗口关闭，GitHub活动BUG查询为空；原有成功证据、失败记录及候选保留。既有S01、S02-00、Platform包、service-token资格control、登录/仪表盘/菜单/库存修正不重复实现。

| 窗口 | 所属卡片 | 预计 | 输出 | 顺序 |
|---|---|---|---|---|
| W18-01 | WP02-03 | 3～5小时 | 当前授权读取一致性及并发回归 | 首项实施 |
| W18-02 | WP02-04，关联WP02-10 | 5～7小时 | 租户管理与强审计同事务 | W18-01合并后核对实际前置再开工 |
| W18-03 | WP02-05 | 4～6小时 | 身份权威快照/版本/Outbox实际缺口闭环 | W18-02合并后核对实际前置再开工 |
| W18-04 | 本轮集成验收 | 2～4小时 | PostgreSQL/HTTP串联证据和收尾 | 三项产品切片就绪后 |
| 修正机动 | 本轮 | 2小时 | 处理新增失败和集成差异 | 按实际风险使用 |

这些是现有卡片下的有限窗口，不增加375任务分母，也不关闭完整WP02-02/03/04/05/10。当前读取的一致快照不等于Owner提交前撤权fence；租户管理强审计原子化不等于全部生命周期、幂等操作恢复或所有业务writer已加入共同门。历史合法读取与新业务效果的权限合同分别保留，不笼统封禁所有暂停租户的历史读取。

本轮不承担全Main普通身份RLS/profile安装、现有数据库迁移、CRM消费改造、跨库撤权协议、询盘业务闭环、正式发包或生产发布。遇到这些前置缺口应准确记录并保持未就绪；不得用stub或旧高权限连接声称普通权限采用完成。

## 目录与执行规则

- 执行者：当前总控对话 `01a14537-9073-7482-83dc-109799c39299`，单人串行。原A/B/C/D无新任务。
- 物理目录：复用 `D:/CP6-worktrees/wave17-dispatch-20261018`。旧任务已合入远端、工作区无未提交或忽略文件、无目录内遗留进程。计划及各产品窗口分别从当时最新main创建 `codex/` 任务分支；上一任务合入并核对后才切下一分支。
- 根 `D:/CP6` 的7项未跟踪用户资料保留。不新建worktree，不清理原证据/数据库/远端分支。依赖下载缓存复用；构建输出放本轮自有目录，不共享其他活动任务的可写输出。
- 本地证据根 `D:/CP6-local-test/wave18/`；第一窗口PG端口55435，结束核对所有权后停止。后续窗口串行复用端口但使用独立新测试数据库或fixture根，避免把旧库状态当新验证。HTTP使用自有loopback临时端口，真实资源建立后登记实际身份。
- 已有PostgreSQL副本与用户数据库不改动。本轮隔离PostgreSQL组件/HTTP验证如使用测试数据，明确其范围，不记作真实DEV或业务验收。
- 每项只有一份DELIVERY，合并集中SPEC/QUALITY自审和验证记录；明确作者自审，不冒充独立审查。安全、并发和事务使用真实PG专项验证。修正后只复查影响部分；成功证据输入未变时复用。
- 每次push/PR/merge核对工作流引用及路径过滤，不触发Actions、不推Tag、不修改分支保护。正常PR合并且远端main包含候选后才能宣布该窗口交付。

## W18-01：当前授权读取一致性

直接复用：WP02-03已经合入的可信主体/tenant绑定、真实启用角色读取、未版本化缓存不授予权限，以及原PostgreSQL权限夹具。完整WP02-02仍未结束；本窗口仅在现有授权读取路径建立一致性，不扩大存储采用声明。

允许产品写集：

- `CP6.Core/Services/Sys/CurrentPermissionContext.cs`
- `CP6.Core/Services/Sys/PermissionAggregator.cs`
- `CP6.Core/Services/Sys/IPermissionAggregator.cs`（保留原入口，增加显式取消重载）
- PostgreSQL单语句读取辅助文件 `CP6.Core/Services/Sys/PostgreSqlPermissionReader.cs`
- `CP6.Tests/CurrentPermissionContextTests.cs`
- `CP6.Tests/PermissionAggregatorTests.cs`
- `CP6.Tests/CurrentPermissionConsumptionPostgreSqlTests.cs`
- 单份报告 `docs/development/evidence/WP02-03/consistent-permission-read/DELIVERY.md`

步骤与验收：

- [x] 固定普通请求、登录Prewarm、CRM聚合调用及已有事务边界；核对既有授权算法、取消和异常处理。
- [x] 在原PG夹具加入受控并发：读到旧角色后，另一连接一次提交角色撤回与权限改变，再继续原读取。先证明当前多语句READ COMMITTED是否产生数据库从未存在的授权组合；保留实际RED。
- [x] 让相关PG读取在同一一致快照内完成。明确自有事务和调用者事务的所有权：不提交/回滚调用者事务，不以无效隔离级别冒充一致性，不自动重放含副作用的调用者事务。若隔离级别或调用链要求超出写集，先更新本窗口技术方案再实施。
- [x] 验证下一次调用观察已提交撤权；A/B租户、失效用户、真实pending token拒绝、正常access及旧缓存限制仍保持。不能把一次快照内允许称为提交前授权保证。
- [x] 定向运行 `CurrentPermissionContextTests`、`PermissionAggregatorTests` 和实际变化涉及的过滤器测试；原生PG运行 `CurrentPermissionConsumptionPostgreSqlTests`，要求选中场景无跳过。未受影响的历史证据按来源复用。
- [x] 完整任务diff集中自审、报告/共享状态、正常PR合并与远端核对。

复现入口（在本窗口工作目录，环境变量使用自有新根）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
& C:/Users/tt/.dotnet/dotnet.exe restore CP6.Tests/CP6.Tests.csproj --locked-mode
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --filter 'FullyQualifiedName~CurrentPermissionContextTests|FullyQualifiedName~PermissionAggregatorTests' -v:q
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave18/permission-consumption-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~CurrentPermissionConsumptionPostgreSqlTests -v:q
```

## W18-02：租户管理强审计共同事务

> 当前候选验证/作者集中自审完成，[单份DELIVERY](../../evidence/WP02-04/tenant-management-audit/DELIVERY.md)记录39项去重通过；正常合并与远端核对尚待执行，#277保持Open。下方15:54Z为历史进展。

实施基线事实（现已修正）：TenantAdminService原先SaveChanges，再通过best-effort ISecurityAuditService记录管理事件。SecurityAuditService明确吞掉审计失败；这不能满足新设计要求的管理强审计同commit。

进展补充（15:54Z）：#277真实RED已确认，原生PG管理6项+身份后阶段4项、相关21项+平台闸6项已通过；取消与真正HTTP尚待执行。现有锁定依赖没有TestServer，采用自有Kestrel loopback动态端口，未增加依赖。

冻结补充（2026-10-18 15:42Z）：ITenantAdminService的兼容取消入口、TenantController实际HTTP取消传播、原TenantControllerTests和新TenantManagementAuditPostgreSqlTests / TenantManagementPostgresFixture纳入本窗口。复用已存在的PG服务器生命周期，不新增工作树；平台审计显式TenantId，不改变请求租户上下文。

计划写集：`CP6.Core/Services/Platform/TenantAdminService.cs`、必要的同目录管理审计辅助文件、`CP6.Tests/Platform/TenantAdminServiceTests.cs`及新增同目录PostgreSQL事务回归；真实调用需要时核对`CP6.WebApi/Controllers/Platform/TenantController.cs`。实现前以实际文件存在性和调用合同冻结最终写集；不修改普通登录审计的best-effort合同。

- [x] 从W18-01后的最新main创建独立分支，登记本窗口与实际前置。
- [x] 对create/update/suspend/reactivate分别核对平台三道权限闸、现有身份捕获、事务/savepoint及Sys_SecurityLog租户盖章。
- [x] 管理事件在业务保存前加入同一工作单元；平台审计准确落平台租户，不因修改请求TenantContext污染业务身份。审计失败与业务/身份快照一并回滚；普通认证审计沿原行为。
- [x] 真实PG制造审计插入失败和晚阶段失败，检查Tenant、admin、role/menus及身份事件没有半保存；正例事件各一次、操作人/目标准确，取消不泄漏事务。
- [x] 检查既有调用者事务、重复请求与UNKNOWN边界。未新增持久请求槽时不宣称幂等恢复，不因超时自动重试创建。
- [ ] 一份DELIVERY、集中自审、相关HTTP验证和正常合并。

## W18-03：身份权威快照、版本与Outbox一致性

已有生产路径为`CP6Context.SaveChanges` → `IdentityChangeCapture` → `IdentitySnapshotWriter`，包含tenant/user/department/permission聚合及优先撤权队列。先核对已有成功证据，仅为仍未覆盖的实际风险增加运行。

计划写集：`CP6.Core/Services/CrmIdentity/IdentityChangeCapture.cs`、`IdentitySnapshotWriter.cs`、`IdentityPermissionSnapshot.cs`及必要的`CP6.Core/EFDbContext/CP6Context.cs`事务段；`CP6.Tests/CrmIdentityChangeCaptureTests.cs`、`CrmIdentityRevocationTests.cs`及新增PostgreSQL专项文件。公共事务段只在此串行窗口编辑，不更改不相关模型、迁移或包版本。

- [ ] 按最新main建立用户主/附角色、部门移动/停用、角色变更的真实捕获矩阵，核对同事务最终事实和版本分配；已满足项直接引用，不重写。
- [ ] 对发现的遗漏、旧跟踪值、并发版本或失败回滚缺口先建立实际失败回归，再修正最小范围。若没有产品缺口，保留实际验证交付，不为制造改动而重构。
- [ ] 两连接竞争同聚合首版本/后续版本；验证单调版本、准确最终内容、无同版本异内容；晚阶段快照或Outbox失败时业务、版本及消息全部回滚。
- [ ] 用户/部门/角色跨租户数据不可进入另一租户快照；禁用事件走既有优先队列，同一物理事务无孤立消息。服务token已验范围不重复运行。
- [ ] 该交付只证明Main提供者的实际范围；CRM健康投影、完整目录协议、所有writer的授权epoch/fence仍单列，不由Main测试替代。
- [ ] 一份DELIVERY、集中自审、正常合并与远端核对。

## W18-04：串联验收与收尾

- [ ] 用已合入的真实产品代码和自有PG/HTTP实例验证普通身份读取、角色改变后的下一次读取、租户管理成功/失败、身份快照与Outbox对应结果。HTTP按实际应用过滤器/中间件链运行，夹具替换部分明确记录。
- [ ] 只补跨切片新出现的接口/事务风险；不重复未变化的全部单测或原生矩阵。
- [ ] 各任务远端main包含性、单份报告、台账、四状态与生成视图一致；本轮Handoff广播收尾，旧A/B/C/D不需START/ACK。
- [ ] 停止自有进程、释放端口，保留原始日志和失败记录；更新可继续任务，不新增物理worktree。
- [ ] 完整卡片/目标/业务验收保持真实状态。只有上述必需工作全部完成后，才把本轮持续目标标记完成；出现外部阻塞时按实际缺口留存，不假称24小时成果已交付。

## 当前登记

计划与W18-01范围已由当前总控按用户授权登记。W18-01在本规划正常合并后开始实现；W18-02/03/04保持顺序待开工，由同一总控核对最新前置后直接接续，不要求离开的用户重复批准。每次实质范围变化更新本计划和台账。当前尚无本轮产品修改、测试成功或业务验收声明。

## W18-01实施选择（2026-10-18）

真实PG已复现并登记P0 [#275](https://github.com/GTX537/CP6/issues/275)，在同一窗口优先修复；分支改为 `codex/bug-275-permission-snapshot`，最新main基线仍d60ccef1。使用一条参数化SELECT读取完整授权事实，SQL标识符来自EF元数据，所有租户表显式绑定当前可信TenantId；数组分别聚合，避免多集合笛卡尔膨胀。JSON仅在数据库与内部读取器之间承载原始字段，不扩成外部合同。普通READ COMMITTED读取每次使用一个新语句快照；既有调用者事务由调用者控制，不新建、提交、回滚或重试其事务。既有REPEATABLE READ/SERIALIZABLE仍受调用者原快照时点约束，不将其旧快照宣称为提交时当前授权，完整Owner最终门留待对应任务。

请求取消验证需要把RequestAborted传到实际授权SQL，增加聚合器取消重载并保留原单参数入口；这是本窗口取消/资源边界的必要实现，不改HTTP、数据库模型或正式包合同。已核对仓内唯一实现为PermissionAggregator，无其他实现者或该接口的mock。
