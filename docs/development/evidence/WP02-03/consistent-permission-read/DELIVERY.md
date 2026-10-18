# W18-01 / BUG-275：PostgreSQL 当前授权读取一致性

本窗口修复 [P0 #275](https://github.com/GTX537/CP6/issues/275)：旧实现分别读取启用角色及权限，另一连接在中途一次提交“停用角色 + 新增动作”后，会拼出从未有效授予的动作。现在 PostgreSQL 使用一条参数化 SELECT 取得同一语句快照内的用户、有效角色、部门、菜单、动作、数据范围与字段权限。最终相关验证去重 **57 通过、0 失败、0 跳过**；一次任务级 SPEC/QUALITY 作者自审无剩余阻断，候选可正常合并。当前报告时点尚未合并，Issue 保持 Open；远端结果由后续 Handoff 与总控台账记录。

## 范围与技术决定

实施基线 `d60ccef1b32a042b664c452ba2137a573bb1fbea`（计划 PR274），分支 `codex/bug-275-permission-snapshot`，复用 `D:/CP6-worktrees/wave17-dispatch-20261018`。开始记录为 Handoff `f802116a4e006b18c982336108ff010b12ec76d1` 的 `tasks/WP02-03/messages/20261018T151553Z-wave18-start/MESSAGE.md`。独立任务分支、唯一总控实施；没有子代理或独立审查者。

- PostgreSqlPermissionReader 从 EF 模型取得并引用表名，主体和 TenantId 均为 Npgsql 参数；每个租户表保留显式租户条件，Sys_Menu 沿既有共享表边界。CTE 只允许持久化启用用户和启用主/附角色。各集合分别聚合，避免集合相乘；不读取密码、令牌或无关列。
- 菜单/动作并集、数据范围 MAX、custom department 并集与字段 MIN 保持；JSON 只作内部结果承载。字符串原值、引号、Unicode 和尾空格保留。动作拼接沿现有 PostgreSqlTextQuerySqlGenerator 的 bpcharsend/convert_from 规则，不使用会去除 bpchar 尾空格的普通转 text。
- 不创建或拥有调用者事务、不提交/回滚它、不改变隔离级别、不重放副作用。READ COMMITTED 每次调用得到新的语句快照；已有 REPEATABLE READ 保留调用者原快照，仍是完整一致的旧视图。后者**不是提交前当前授权**；Owner 最终撤权 fence 仍未交付。[PostgreSQL 隔离语义](https://www.postgresql.org/docs/18/transaction-iso.html)支持这一范围；内部聚合使用[原生 JSON 函数](https://www.postgresql.org/docs/18/functions-json.html)。
- RequestAborted 传播到前置用户查询与真实聚合命令。IPermissionAggregator 保留原 BuildAsync(Guid) 入口，增加取消重载；唯一仓内实现与 DI 仍为 PermissionAggregator。CRM BuildEnabledRolesAsync 兼容入口保持。
- 主体/tenant claim、pending token 拒绝、旧缓存不能授予权限和故障拒绝保持。历史非 PostgreSQL 路径保留；未增加 SQL Server 开发或验收要求。

## 实际验证及失败记录

原始日志、运行元数据与 TRX 统一保留在 `D:/CP6-local-test/wave18/w18-01/`，TRX 在 `results/`，编译产物在 `artifacts/`。项目/依赖/锁文件不变，使用 .NET 8、现有锁定 NuGet 缓存 `D:/CP6-local-test/WP02-02/adopt-a1/n` 和本机 PostgreSQL 18.6。锁定恢复 `restore-20261018T151654Z.log` 成功。下列结果均为本窗口实际执行，不把旧结果改称新执行。

| 运行 | 结果 | 结论 |
|---|---|---|
| `red-20261018T151654Z.log` | 编译失败 | 新测试缺少 ISqlGenerationHelper namespace，已补；不是业务 RED |
| `red-20261018T151837Z.log` / `results/red.trx` | 1 失败 | 真实两连接一次提交停用+export，旧聚合错误允许从未有效授予的 order:export；#275 原步骤 RED |
| `snapshot-and-cancel-red-20261018T152915Z.*` | 6 通过、2 失败 | 并发与调用者 RC 等已通过；动作尾空格丢失已修。取消测试清理重复 rollback 掩盖断言，先修测试清理 |
| `cancel-red-20261018T153109Z.*` | 1 失败 | 修正清理后确认请求取消不能打断真实 PostgreSQL 锁等待；传播取消前的有效 RED |
| `pg-green-20261018T153237Z.*` | 9 通过、0 失败/跳过 | 最终代码必要编译及全部选中原生 PG 行为 |
| `related-green-20261018T153523Z.*` | 46 通过、0 失败/跳过 | 同一编译产物上的 CurrentPermissionContext、PermissionAggregator、PermissionChain、RequirePermissionFilter、PermissionService、FourGranularity |
| `crm-green-20261018T153624Z.*` | 2 通过、0 失败/跳过 | 同一编译产物上的现有真实角色/部门/Owner 投影与缺少部门路径不扩权合同 |

原生 PG 九项覆盖：从未存在的权限组合；动作/数据范围/字段一致视图；调用者 RC 未提交修改不被提交且后续读观察撤权；调用者 RR 保留旧完整视图且事务结束后恢复新鲜读取；合法文本与聚合、跨租户字段隔离；请求取消真实 relation lock 等待、连接释放及后续读取；原有追踪实体/旧缓存仍读已提交改变；同名/同角色号跨租户；实际查询故障经真实 PermissionService/RequirePermission 返回403。并发拦截器只安排真实数据库命令时序，不替换查询结果。

收尾仅为测试中已赋值的 MenuKey 增加非空注解，消除新增 CS8620；生产代码未变。`text-followup-20261018T153841Z.*` 编译及该原生文本回归1项通过，0失败/跳过；其余56项复用上述未变输入结果，成功数量不重复相加。

数据库端口 55435，五次运行使用全新自有数据根 `D:/CP6-local-test/wave18/permission-consumption-{red-20261018T151837Z,snapshot-and-cancel-red-20261018T152915Z,cancel-red-20261018T153109Z,pg-green-20261018T153237Z,text-followup-20261018T153841Z}`。各根均已核对 stopped.txt、无 postmaster.pid，端口无监听；数据库及成功/失败原件保留。未访问现有 CP6DB 或 PostgreSQL 副本。夹具使用 fixture_owner 创建并访问最小相关模型表，**未证明普通数据库身份、完整迁移、全部索引或 RLS 采用**。

编译的既有 NuGet 漏洞提示（System.Security.Cryptography.Xml / MessagePack）、CS1998 / xUnit2017 / xUnit2029 警告保留，未抑制或顺手修改依赖；该历史依赖风险不被本修复消除。未执行全仓、SQL Server、生产业务或发布验证。

复现最终相关验证（PowerShell，独立任务目录；每次 PG_ROOT 必须新建）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$artifactRoot='D:/CP6-local-test/wave18/w18-01/artifacts'
& C:/Users/tt/.dotnet/dotnet.exe restore CP6.Tests/CP6.Tests.csproj --locked-mode --artifacts-path $artifactRoot
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave18/permission-consumption-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path $artifactRoot --filter FullyQualifiedName~CurrentPermissionConsumptionPostgreSqlTests -v:q
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --artifacts-path $artifactRoot --filter 'FullyQualifiedName~CurrentPermissionContextTests|FullyQualifiedName~PermissionAggregatorTests|FullyQualifiedName~PermissionChainIntegrationTests|FullyQualifiedName~RequirePermissionFilterTests|FullyQualifiedName~PermissionServiceTests|FullyQualifiedName~FourGranularityIntegrationTests' -v:q
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --artifacts-path $artifactRoot --filter 'FullyQualifiedName~CrmOidcBridgeTests.Projection_uses_enabled_real_roles_departments_and_same_tenant_eligible_owners|FullyQualifiedName~CrmOidcBridgeTests.Missing_department_path_does_not_expand_to_all_departments' -v:q
```

## 集中 SPEC/QUALITY 与交付边界

唯一总控已按用户要求独自审查相对最新 origin/main `d60ccef1` 的完整代码/测试差异、模型查询过滤、全部调用点、真实红绿证据及允许写集。重点核对参数/标识符、租户谓词、持久化启用状态、聚合与文本等价性、独立集合避免乘积、故障拒绝、事务所有权与取消清理；无剩余阻断。git diff --check 通过。此为**作者自审**，不冒充另一位审查者；无需重新运行未变成功证据。共享台账、生成视图和四状态在同一任务提交同步。

完整 WP02-03 保持 PLANNED/NOT_CHECKED；Owner 提交前撤权 fence、权威版本/registry/映射签认、普通身份完整 RLS、跨仓业务采用、负载容量及真实 DEV/生产验收仍未完成。原 [current-permission-consumption](../current-permission-consumption/DELIVERY.md) 的主体与缓存边界作为基础保留，本次只消除其多语句一致性缺口。W18-02/03/04 仍需后续串行实施，不以这一个窗口宣布持续目标完成。

正常分支 push/PR/merge 前复核与已分析工作流内容相同、实际路径未命中窄 PR 过滤；不推 tag、不 dispatch、不绕过保护。接手至报告时点没有本轮排队/执行中 Actions；没有取消项或触发配置变更。远端合入核对后才关闭 #275，随后在同一复用目录从最新 main 创建下一任务分支。
