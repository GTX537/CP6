# WP02-03 第十三轮 A：当前权限读取消费交付

2026-10-17 UTC；执行者为用户新启动的 A / wave13-a-permission-20261017 对话。按[限定执行包](../../../dispatch/20261017-wave13/WP02-03.md)交付，关联安全 P0 [Issue #245](https://github.com/GTX537/CP6/issues/245)。该 Issue 保持 Open，等待 COORD 审查、集成及远端 main 核对；完整 WP02-03 仍 PLANNED/NOT_CHECKED，业务验收 NOT_RUN。

分支 `codex/wp02-03-current-permission-20261017`，工作树 `D:/CP6-worktrees/wave13-a-permission-20261017`。开工及交付前已 fetch 核对 HEAD/main/origin/main 基线 `4704592f52172c941e1cb95bdeb3c73f0dd179e8`；最终候选完整 SHA 由本报告所属提交及 Handoff DELIVERY 固定引用。仅登记白名单内的代码、相关测试及本报告变更，不修改共享台账、四状态文件、认证发行、模型/迁移、Program 或其他执行者范围。

## 结果与边界

- `CurrentPermissionContext.GetAsync` 要求唯一已认证 identity，以非空 GUID 的 `NameIdentifier` / `sub` 和 `tenant_id` 绑定主体；同时存在的主体别名必须一致，tenant 必须与 CP6Context 当前租户相等。不按显示名查用户，不从其他 identity 拼接 claim，不补默认主体或缺失 tenant。现有 JwtHelper 签发令牌、OIDC `sub` 和显式默认租户 claim 保持可用。
- 每次 Get/Prewarm 都在当前租户内读取真实启用用户。`PermissionAggregator.BuildAsync` 和 `BuildEnabledRolesAsync` 统一读取存在且启用的主/附加角色；用户、部门和实体权限行均使用无跟踪查询，标量投影直接查存储。保留菜单/动作并集、数据范围 MAX、自定义部门并集和字段实际存行 MIN。没有重复实现或接入已交付的字段 V1 决策组件。
- 未有真实权限 epoch 前不读写/刷新旧 `perm-ctx:userId` 缓存。`PrewarmAsync(Guid)` 签名保留，但语义为一次即时构建；不为后续请求预存允许。`Invalidate/InvalidateByRole` 保留历史键删除兼容行为，读取正确性不依赖删除成功或所有写入者都调用失效。
- 主体缺失/冲突、用户停用/删除、租户错配及存储查询异常产生 `CurrentPermissionUnavailableException`；PermissionService 将不可用转换为 false，原 RequirePermission 路径产生既有 403/ProblemDetails，无旧缓存 fallback。已取消请求继续传播取消。RequirePermissionAttribute 本身无需改动。
- 仅承诺本次查询可见的此前已提交状态；本实现不建立跨多条查询的一致快照，不提供 Owner 提交前最终撤权 fence，也不制造权限版本、registry/mapping 或热更新协议。每次读取增加数据库访问，是取消无版本授权缓存的明确代价；本轮未作负载/性能容量验收。

## 实际验证与复用

环境：本地 .NET 8 / SDK 8.0.424，PostgreSQL **18.6**；锁定恢复使用现有正式 NuGet 缓存 `D:/CP6-local-test/WP02-02/adopt-a1/n`。项目/依赖/锁文件未改变。

本地证据统一位于 `D:/CP6-local-test/WP02-03/`，TRX 位于其 `wave13-results/`。保留原始失败与成功日志，未重复生成哈希清单或多份报告。以下执行均为本轮真实运行：

| 执行 | 结果 | 证据及适用范围 |
|---|---|---|
| 未改生产代码的回归复现 | 15 失败、0 通过 | `red.log` / `red.trx`：错误主体/tenant、匿名/缺失/冲突 claim、停用、撤回旧缓存及查询故障；用于 Issue #245 根因复现 |
| 修复后的相关测试及必要依赖编译 | 45 通过、0 失败、0 跳过 | `unit.log` / `unit.trx`：CurrentPermissionContext、PermissionAggregator、PermissionChain、RequirePermissionFilter、FourGranularity、StandardRoleSeed、PlanPub/PurHighRiskDenial、PermissionService |
| 补充兼容验证与第二次 PG 尝试 | 19 通过、3 失败、0 跳过 | `postgres-and-followup.log/.trx`：17 项当前上下文（含真实签发令牌、无 Name、匹配 sub、不可访问缓存、旧键失效）及 2 项现有 CRM 投影通过；3 项 PG 因夹具审计表缺失失败，详见下文 |
| 夹具修正后原生 PG 定向验证 | 3 通过、0 失败、0 跳过 | `postgres-final.log/.trx`：跨上下文已提交变更、同名跨租户、真实查询故障经 RequirePermission 返回 403 |

成功结果去重覆盖 **52 个用例**。最后一次仅修改测试夹具所需审计表创建，19 项补充成功结果和此前未受影响的 30 项成功结果直接复用，没有因 SHA 或报告变化再跑产品测试。编译输出仅有既有 CS1998（WmsFeatureFlagsController、CacheServiceTests、CrmOidcServiceTokenTests）及 PermissionsRelationalTests xUnit2017 警告。

PG 使用专属 loopback **55435**、每次全新私有数据根、随机口令及独立测试数据库。最终根为 `D:/CP6-local-test/WP02-03/permission-consumption-20261017T041908Z`，`server-version.txt`、`ownership.txt`、`stopped.txt`、进程日志保留；结束以数据库 data_directory 和 postmaster 文件路径/端口核对所有权后停止。未操作他人进程或既有数据库，未提交口令/数据库文件。

原生回归直接使用 CP6Context、生产 PostgreSQL 配置/模型生成的权限与审计表、真实 SaveChanges 和查询过滤器，覆盖：

1. reader 保持原始跟踪实体及预置旧缓存，另一个 writer 提交角色停用/恢复、附加角色撤回、部门路径/范围/字段权限变更、动作撤回、角色删除、主角色更换、用户停用/恢复/删除；每次读取观察最新提交。结束断言 reader 仍保留原始跟踪值，未用 Clear/Reload 隐藏问题。
2. 同数据库两租户同名用户、同号角色各自取得本租户权限；预先跟踪外租户用户仍不能绕过主体与全局租户过滤。
3. 已允许后在自有测试数据库移除动作表，真实数据库查询失败经 PermissionService → RequirePermission 得到 403，并拒绝菜单查询。

这些是使用测试数据的真实 PostgreSQL 读取消费验证；只创建相关模型表及主键，不运行完整迁移/全库索引/RLS/真实身份发行宿主验收，不称生产身份或数据库迁移验收。未执行 SQL Server、全 Main、跨仓、发布/部署或完整 WP02-03 业务验收。先前[字段决策交付](../field-decisions/DELIVERY.md)保持原有效范围，本轮未改其实现，亦未将旧 14 项结果改称新执行。

可复现命令（PowerShell，工作树根）：

```powershell
& 'C:/Users/tt/.dotnet/dotnet.exe' restore CP6.Tests/CP6.Tests.csproj --locked-mode --packages D:/CP6-local-test/WP02-02/adopt-a1/n -p:NuGetAudit=false --verbosity quiet
$filter = 'FullyQualifiedName~CurrentPermissionContextTests|FullyQualifiedName~PermissionAggregatorTests|FullyQualifiedName~PermissionChainIntegrationTests|FullyQualifiedName~RequirePermissionFilterTests|FullyQualifiedName~FourGranularityIntegrationTests|FullyQualifiedName~StandardRoleSeedTests|FullyQualifiedName~PlanPubHighRiskDenialTests|FullyQualifiedName~PurHighRiskDenialTests|FullyQualifiedName~PermissionServiceTests'
& 'C:/Users/tt/.dotnet/dotnet.exe' test CP6.Tests/CP6.Tests.csproj --no-restore --filter $filter -p:NuGetAudit=false -v quiet
$env:CP6_PERMISSION_PG_ROOT = 'D:/CP6-local-test/WP02-03/permission-consumption-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$env:CP6_PERMISSION_PG_BIN = 'C:/Program Files/PostgreSQL/18/bin'
& 'C:/Users/tt/.dotnet/dotnet.exe' test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~CurrentPermissionConsumptionPostgreSqlTests -v quiet
```

补充 CRM 选择器为 `FullyQualifiedName~CrmOidcBridgeTests.Projection_uses_enabled_real_roles_departments_and_same_tenant_eligible_owners|FullyQualifiedName~CrmOidcBridgeTests.Missing_department_path_does_not_expand_to_all_departments`，与 CurrentPermissionContextTests 同次执行。以上复跑命令适用于最终源码，不把最终 17 项上下文测试误称初次 15 项执行。

## 失败分析、审查及交接

初次 PG `postgres.log` / `postgres.trx` 为 3 失败：夹具用自建 schema，被现有生产适配器 `Search Path=public` 限制拒绝；改为独立数据库中的 public。第二次 PG 为审计依赖 `Sys_FieldAuditLogs` 表未创建；据 CP6Context.SaveChanges 的真实写入补齐该模型表，随后 3 项通过。两次失败实例均核对身份后停止，目录 `permission-consumption-20261017T041729Z` 与 `permission-consumption-20261017T041826Z` 保留。没有绕过生产配置或关闭真实审计。

执行者已检查完整代码/测试 diff、白名单、主体和 tenant 信任边界、无跟踪行为、旧缓存绕行、MIN/MAX 保留及报告必要链接；`git diff --check` 通过。按执行包，由 COORD 在此固定候选上统一完成一次任务级 SPEC/QUALITY 集中审查、共享状态同步、串行合并及远端 main 核对；本交付不把执行者自检称为 COORD 已审查。

START 已送达 Handoff `bf7401f9fdd6c86d650de83bcc19dfb590a6ae3a`，路径 `handoffs/CP6-DEV-20261012/tasks/WP02-03/messages/20261017T041123Z-wave13-start/MESSAGE.md`。P0 通知 `7f0c5cdce73c57997b46a94536173ee33c671bd8`，路径 `.../20261017T041531Z-wave13-p0/MESSAGE.md`（正文手写 UTC 04:19Z 有误，以目录/提交实际 04:15:31Z 为准）。两消息已按固定 SHA 远端回读；后续 DELIVERY 引用最终提交和本报告。

推送前核对候选与远端 main 的 13 个工作流内容一致：12 个仅 workflow_dispatch，唯一 push 仅 `v*.*.*` tag；本任务只推分支。Handoff 仍私有、Actions disabled，候选/默认分支无 workflow。接手时本任务无 Actions 运行；没有触发/重跑/取消 Actions，没有修改触发配置。共享状态、main、生产发布、既有数据库及其他任务范围均未写入。最终远端候选回读及 COORD 收件定位见 Handoff DELIVERY；合并和 Issue 关闭仍待总控完成。


## COORD 审查修正：完整会话用途边界

原候选 `aa0a361f93aad67da0bb8ca27c6954d557dd9530` 已完成唯一一次集中 SPEC/QUALITY 审查。审查确认 GUID/tenant 绑定与新鲜读取范围，但发现一项 P0：真实 JwtHelper.GeneratePendingToken 的 `2fa_verify` / `2fa_enroll` 半登录 JWT 使用相同 issuer/audience/key，现有宿主不通用拒绝 token_use；原候选的新 GetAsync 因不要求 Name 而允许它们通过真实 PermissionService/RequirePermission。审查探针的原始失败在 `C:/Users/tt/CP6-planning-reports/20261017-foundation/permission-probe/probe-final.log`；此问题在合并前被发现，原候选没有进入 main。

依据 PR247 / main `8367914d395fc2a6d0cef22d79d34d69cf87de43` 的三文件修正窗口，原用户 A 的分支和证据保留。唯一内部修正代理在 `codex/bug-245-permission-review-fix-20261017` / `D:/CP6-worktrees/wave13-a-review-fix-20261017` 新增回归并实现用途检查，COORD 随后接手验证、报告与提交，二者没有并行写入。修正起点 `d4e7fec854c8b15d9ab03eead2bac106f9e0cf1b` 已包含原候选及登记主线。

实际合法合同核对：JwtHelper 普通/平台/impersonation access、AuthSessionService 与 CRM OIDC at+jwt 均不签发 token_use。GetAsync 在已认证主体检查后拒绝 principal 上任意 token_use，包括 pending、空值、未知值或来自其他 identity 的标记，不臆造名为 access 的用途白名单；正常无 Name 的 GUID/sub 合同保持。该拒绝经原 PermissionService 返回 false、真实 RequirePermission 返回 403。没有改 Program、发行链、模型或业务事务；不宣称通用 JWT 认证层已完成用途隔离。

本次新增原始证据位于 `D:/CP6-local-test/WP02-03/review-fix/`：

- 内部代理的锁定恢复成功，`restore.log` 保留；产物隔离于 artifacts，使用原正式 NuGet 缓存，未修改依赖/锁文件。
- 修正前新增 CurrentPermissionContextTests 回归为 **6 失败、21 通过、0 跳过**，见 `red.log` / `results/red.trx`。两种真实 pending 的过滤拒绝、未知/空用途及第二 identity 目的边界在旧候选上失败；原始失败保留。
- COORD 在三文件修正工作树执行相关编译和 CurrentPermissionContextTests、PermissionChainIntegrationTests、RequirePermissionFilterTests、PermissionServiceTests，**37 通过、0 失败、0 跳过**，见 `green.log` / `results/green.trx`。包含真实 pending 签发/验签后拒绝、正常/平台/impersonation access、无 Name 主体、真实 CRM OIDC sub 正对照及原读取回归。无 Name 正例是在真实 access 验签后的 principal 移除显示名再读取；不称为另一次宿主发行验收。
- 本次只变 claim 入口与对应测试，原原生 PostgreSQL 3项及其余未受影响行为按上文准确来源复用，没有重启数据库、全仓测试或重跑旧夹具。现有 CS1998 / xUnit2017 警告保留，无新增构建/测试失败。成功数量不与原52项直接相加。

COORD 绿测复现命令：

```powershell
& 'C:/Users/tt/.dotnet/dotnet.exe' test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/WP02-03/review-fix/artifacts --filter 'FullyQualifiedName~CurrentPermissionContextTests|FullyQualifiedName~PermissionChainIntegrationTests|FullyQualifiedName~RequirePermissionFilterTests|FullyQualifiedName~PermissionServiceTests' --logger 'trx;LogFileName=green.trx' --results-directory D:/CP6-local-test/WP02-03/review-fix/results -p:NuGetAudit=false -v quiet
```

修正后的原审查者局部复查、正常集成及远端核对由 COORD 接续；不重开第二次全任务审查。完整 WP02-03 / 生产业务验收、Owner 提交前 fence、负载、全 Main 及其他原未覆盖范围保持不变。#245 在远端包含性与原步骤证据核对前仍 Open；无 Actions、发包、既有数据库操作或部署。


### 修正局部复查结论

原审查者已针对修正提交 `add3373edd1c8d751a9ff2cd32079b4a96b274c1` 完成影响范围复查，最终 **Ready to merge: Yes**，无剩余阻断。已直接核对37项绿测与6失败/21通过红测，未重跑测试或PostgreSQL。原No结论与P0证据保留在同一审查记录，正常集成及远端包含性核对仍由COORD完成；完整任务及其他未验证范围不变。
