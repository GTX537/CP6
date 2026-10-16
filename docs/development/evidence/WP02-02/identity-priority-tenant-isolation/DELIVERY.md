# WP02-02 第九轮：Identity 共享消息表租户隔离与 ERP 同事务适配交付

源码基线 `37d84b4b6eaeefe7e8b35c21df26a8b1f8c119d5`，分支 `codex/wp02-02-identity-storage-20261015`。范围按 [第九轮执行包](../../../dispatch/20261015-wave9/WP02-02.md)，仅交付内部存储安装器和事务适配器；完整任务保持 IN_PROGRESS / CONFIRMED。候选提交与草稿 PR 的准确身份通过本任务 Handoff 固定提交交付；台账、四状态、合并和远端 main 包含性由 COORD 串行处理，当前不声明已集成。

## 实际改动与使用边界

- `IdentityTenantRowSecurityV1` 对真实 `IdentityMessagingContext` 的固定 `crm_identity_priority` 四表安装 ENABLE/FORCE RLS，SELECT/INSERT/UPDATE/DELETE 分别定义策略，UPDATE 同时约束旧行 USING 与新行 WITH CHECK。runtime 仅授 SELECT/INSERT/UPDATE；DELETE/TRUNCATE 与保留清理仍由后续 Owner 窗口决定。四表为 `Cp6_DeadLetterRecord`、`Cp6_InboxAggregateCheckpoint`、`Cp6_InboxMessage`、`Cp6_OutboxMessage`。
- 原模型和迁移完全不变：保留 Id 主键、TenantId、字段/文本排序规则、四表真实 RowVersion、`cp6_storage_identity` 原 token sequence/function/trigger。Inbox 的 ConsumerName+MessageId 和 Outbox 的 MessageId 仍全局唯一；Checkpoint 的 ConsumerName+TenantId+AggregateId 保持原域，不变成全局二字段键。
- 从 ERP 安装器抽取唯一 `TenantLoginDirectoryV1` 与 `TenantLoginV1`：仍只有一个受保护目录，session_user 同时匹配登录 OID 和名称；helper 专用 NOLOGIN Owner 只读目录，固定 search_path，无 PUBLIC EXECUTE。全局事务锁 `(1129330260,1)`、VOLATILE 先共享锁后读、目录 I/U/D 排他锁及 TRUNCATE 拒绝原样共用。
- 两种安装入口明确分开：默认只允许新目录首次创建；`appendToKnownDirectory: true` 必须先精确验证既有 v1 表/约束/索引、角色所有权与 ACL、触发器、函数属性和正文、所有登记映射及 helper 有效权限，再追加全新 Identity 层。不使用 IF NOT EXISTS 接管未知对象，不自动修复漂移或重装 Identity。正文仅规范化 CRLF 和外沿空白，内部定义逐字相等；错误正文、STABLE、禁用目录锁 trigger、helper 多余权限、映射漂移均拒绝。
- 本轮实际验证的追加顺序为 **已安装 ERP baseline+ERP overlay → 原 IdentityPriority baseline → Identity overlay（既有目录模式）**。还在同一独占集群的另一自有数据库验证 Identity 独立首装后回滚。未宣称其他历史布局或不同初始化顺序自动可升级。
- 抽取 `TenantEfCommandGuard` 供 ERP/Identity 共享。`ExecuteIdentityAsync` 在既有 `TenantStorageTransaction` 中创建真实 context，复用同一物理连接/事务，每条同步/异步 EF 命令前重查 scope/catalog/连接身份/当前目录映射。无自动重试，不移除并发保护；同一 Owner 事务顺序调用 `ExecuteErpAsync` 与 `ExecuteIdentityAsync`，回调完成后才由 Owner 显式提交，不嵌套 Execute、不外借 context 生命周期。

允许 profile 为有限集合，不接受任意字符串或多余 queue domain：

| 使用方式 | ModelVersion | MigrationVersion | QueueDomainRefs（顺序不敏感） |
|---|---|---|---|
| ERP 单独 | `erp-integration-v1` | `erp-integration-tenant-rls-v1` | `erp_integration` |
| Identity 单独 | `identity-priority-v1` | `identity-priority-tenant-rls-v1` | `crm_identity_priority` |
| 同事务组合 | `erp-identity-messaging-v1` | `erp-identity-messaging-tenant-rls-v1` | `erp_integration`、`crm_identity_priority` |

这些是内部显式配置/安装标记，不是已部署环境 readiness 或当前业务授权证明。未修改 Platform 合同、项目/依赖锁、Program、模型迁移历史、认证/Owner、实际 writer/dispatcher 接线。

## 实际本地验证

使用 SDK 8.0.424、原锁依赖和 PostgreSQL **18.6**。真实迁移为 `20261002151342_PostgreSqlIdentityPriorityBaselineV1` 与 `20261002151345_PostgreSqlErpIntegrationBaselineV1`；A/B 数据为明确的合成测试数据，不是生产或真实宿主业务验收。

- [锁定还原](identity-restore-1.log) 通过，无包升级或锁文件改动；[受影响测试项目构建](identity-build-3.log) 0 error。保留既有 MessagePack 2.5.302 NU1902、System.Security.Cryptography.Xml 8.0.4 NU1903、CS1998/xUnit2017 告警，没有覆盖成“无告警”。
- [Identity 最终运行](identity-attempt2.log) / [TRX](identity-attempt2.trx)：5/5 PASS，0 failed/skipped，17.3122 秒。包含两种安装方式与已知布局异常拒绝、普通 A/B 四表读写隔离、无 WHERE 常量 UPDATE 的旧行限制、跨 tenant INSERT/UPDATE 拒绝、DELETE/TRUNCATE/目录读取/SET ROLE 拒绝；Inbox/Outbox 跨租户全局冲突和安全异常（无原始消息/inner exception）；Checkpoint 相同 ConsumerName+AggregateId 在不同 tenant 可共存；两个真实 context 的同物理连接/同事务、共同提交或回滚、四表 8 字节 token 插入/更新变化、回调后 context 已释放和嵌套适配拒绝；Identity 单独 profile、scope 结束/嵌套切换、model/migration/queue 错配、catalog revision 与目录漂移/缺失拒绝。
- [ERP 定向回归](identity-erp-regression.log) / [TRX](identity-erp-regression.trx)：4/4 PASS，0 failed/skipped，10.3872 秒。只重跑抽取实际影响的未知/重建登录 OID 绑定、pg_catalog 下文本兼容与真实 token、每条命令 scope/catalog/mapping 重查、目录锁双向等待和等待后读取新映射。完整八表其他矩阵与连接池行为没有重复运行。

可复现命令（每次 PG 运行必须使用不存在的新根；两组测试分开运行，不能共用同一根）：

```powershell
$env:NUGET_PACKAGES = 'D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:NUGET_HTTP_CACHE_PATH = 'D:/CP6-local-test/WP02-02/adopt-a1/http'
& 'C:/Users/tt/.dotnet/dotnet.exe' restore CP6.Tests/CP6.Tests.csproj --locked-mode
& 'C:/Users/tt/.dotnet/dotnet.exe' build CP6.Tests/CP6.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
$env:CP6_TENANCY_PG_BIN = 'C:/Program Files/PostgreSQL/18/bin'
$env:CP6_TENANCY_PG_ROOT = 'D:/CP6-local-test/WP02-02/tenant-connections-' + [Guid]::NewGuid().ToString('N')
& 'C:/Users/tt/.dotnet/dotnet.exe' test CP6.Tests/CP6.Tests.csproj --no-build --no-restore `
  --filter 'FullyQualifiedName~IdentityTenantPostgresTests' --logger 'console;verbosity=normal'
# ERP 定向运行另设一个全新 CP6_TENANCY_PG_ROOT，使用同一命令替换 filter：
# FullyQualifiedName~ErpTenantPostgresTests.Directory_mapping_is_pinned|FullyQualifiedName~ErpTenantPostgresTests.Unknown_and_recreated_login|FullyQualifiedName~ErpTenantPostgresTests.Ef_rechecks_nested_scope|FullyQualifiedName~ErpTenantPostgresTests.Ef_business_text
```

## 保留的失败与独占资源收尾

[首次 build](identity-build-1.log) 因新工作树尚无 assets 得到 NETSDK1004；随后按原锁 restore 后构建通过。[第一次 PG 运行](identity-attempt1.log) / [TRX](identity-attempt1.trx) 在夹具追加目录时 5 项均失败，原因是本轮校验器 SQL `btrim` 默认只去空格，而原函数正文含首行换行，C# `.Trim()` 已移除它。这是安装校验器缺陷，不是预期的隔离 RED，也没有执行业务断言。只读实际 catalog 诊断定位到函数定义判据；收紧到仅规范化换行/外沿空白后，最终运行通过，并追加错误函数正文仍被拒绝的回归。

三个自有根分别为 `D:/CP6-local-test/WP02-02/tenant-connections-26f43de42f0e45c0a2dac670c7398b40`（失败）、`tenant-connections-6cb27c9ea7e74ea3925fb7fea74f5c61`（Identity 成功）、`tenant-connections-f37f7a5723b642d8a04524fdd3c58b8f`（ERP 回归成功），后两者同在 `D:/CP6-local-test/WP02-02/` 下。失败根曾为只读 catalog 诊断重启；核实际 data_directory、postmaster.pid 路径和端口后再停止。最终三根均有 stopped marker、无活动 postmaster.pid，55436 无 LISTEN；数据、凭据私有文件和失败日志保留，没有清库或复用既有业务数据库。

## 复用、未验证与审查

复用 [ERP 已交付报告](../erp-tenant-isolation/DELIVERY.md) 未受影响的八表 RLS/ACL、原全局 key 与原模型/迁移证据；本轮共享目录和适配层相关四项按上述重新验证。复用 [连接组件报告](../tenant-connections/DELIVERY.md) 中池预算、A→B→A 复用、取消、凭据轮换、session reset、错借与 PGOPTIONS 防护的原范围，不称本轮新执行。所有运行仅本地，没有触发/重跑/取消 Actions、修改工作流、正式发包/签名、生产部署或迁移既有业务库。

仍为 NOT_RUN/后续范围：真实 IdentitySnapshotWriter 与 dispatcher 宿主接线、当前授权和 Owner 最终门、真实业务原子写集、其他上下文、完整逐租户 provisioning/版本提升/中断恢复/备份恢复、生产权限与长事务容量、原 17 PGT 及完整业务验收。目录全局锁沿用保守一致性边界，不据此声称生产吞吐或恢复指标。

2026-10-16 一次集中 SPEC/QUALITY 审查结论为 **PASS，无剩余阻断项**。审查覆盖完整代码差异、目录/权限与事务边界、实际验证及报告；唯一阻断项为上述函数体外沿换行误拒绝，已修复并经局部复查及真实 PG 回归关闭。执行者核对 README、报告链接和最终范围，没有新增产品测试或重复审查。

推送与草稿 PR 前已核对候选与远端 main 的工作流内容一致：普通验证仅 workflow_dispatch，R2 候选仅受保护版本 tag push；本次仅推送任务分支、不推 tag，不会命中自动运行条件。任务分支无排队/执行中 Actions，无取消项或工作流变更。Handoff 私有仓 Actions 禁用，任务与默认分支无工作流。共享状态与合并交由 COORD，不以资料运输成功提升业务状态。
