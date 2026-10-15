# WP02-02 ERP 共享存储行隔离与 EF 事务组件交付

本轮基线为 `f6cc17b936d05b0336d319cbfa5097a77511d658`，任务分支 `codex/wp02-02-erp-tenant-isolation-20261015`。范围按 [PLAN](PLAN.md) 与已接受 `cp6-pg-tenancy-v1`，仅完成真实 `ErpIntegrationContext` 八表的内部接入组件。完整 WP02-02 保持 IN_PROGRESS，真实业务验收不提升。协调状态、提交、PR、Main 远端包含性与 Handoff 由 COORD 统一记录。

## 实际改动

- 新增内部 `ErpTenantRowSecurityV1`：受控调用者显式传入当前 NpgsqlConnection/Transaction，首次安装受保护登录 OID＋name→TenantId 目录及无参数 SECURITY DEFINER helper。helper Owner 为新建最小 NOLOGIN 角色，仅可读目录；固定受控 search_path、完全限定对象、无 PUBLIC EXECUTE、无 GUC 身份依据。
- 八张表全部继续位于固定 `erp_integration`，原主键、MessageId 全局唯一域、字段类型、精度、索引、文本兼容函数及六表 RowVersion 触发器不变；不复制到租户 schema。安装 ENABLE/FORCE RLS，分别定义 SELECT/INSERT/UPDATE/DELETE 策略，UPDATE 独立限制旧行与新行。六张可变表只授 SELECT/INSERT/UPDATE；两张 replay audit 只授 SELECT/INSERT；全部表不授 runtime DELETE/TRUNCATE。删除与保留采用留待 Owner 独立批准，详见 [实际对象与 writer 清单](OBJECTS.md)。
- 安装拒绝已有目录/helper、RLS/策略、不明表/角色所有权、非 Owner 表/列 ACL、会影响新目录的非安全默认 ACL、未知 public 函数及 public relation/列授权，保留实际 owner-only 迁移历史。既有文本函数定义复用 `PostgreSqlMigrationPrerequisitesV1` 的冻结验证器；token schema/sequence/trigger 清单与权限定向核对。不存在自动启动安装、隐式提交、重试或接管旧布局。
- 目录采用数据库内固定双整数 advisory transaction key `(1129330260, 1)`，即 ASCII `CP6T`／目录版本 1。helper 为 VOLATILE，先共享事务锁再读取目录；目录 INSERT/UPDATE/DELETE 的 BEFORE STATEMENT trigger 先取同 key 排他事务锁。一次 ERP 事务检查过的映射保持至事务结束，避免 ReadCommitted 下 guard 与后续 RLS 之间改绑。目录 TRUNCATE 直接拒绝，避免表锁/advisory 锁反序。共享锁不赋予 helper Owner 更新目录权限；该锁仅保护存储身份，不是宿主当前授权/撤权最终门。
- 新增内部 `TenantStorageTransaction.ExecuteErpAsync`，仅允许 `erp-integration-v1` model、`erp-integration-tenant-rls-v1` migration 标记与登记 `erp_integration` queueDomain。真实 context 绑定租约的同一实际连接与 DbTransaction，生命周期限于 awaited callback；每条同步/异步 EF reader/scalar/nonquery 命令核当前 scope/catalog、连接身份、同一事务及数据库 helper 返回 tenant 等于当前 binding。由 Owner 在 callback 后显式 CommitAsync；无自动重试、无 context 池、无动态租户模型缓存。
- 旧 `DatabaseConnectionFactory` 只允许 public search_path，本轮保持它不变；新 adapter 直接复用 `DatabaseContextOptions.ConfigurePostgreSql` 中三个既有 provider 服务。为真实 pg_catalog 租约字符串查询，将 `PostgreSqlTextQuerySqlGenerator` 的已知业务 collation 引用限定为 `public`；原 C collation 和比较/容量语义保持。未改模型、迁移历史、Program、认证、Owner、业务路由、包或项目依赖。

## 实际本地验证

最终 [日志](final-verification.log) 与唯一最终 [TRX](erp-tenant-postgres.trx)：exit 0，total/executed/passed 均为 **10**，failed/error/notExecuted 均为 **0**；测试执行 27.4336 秒。真实 PostgreSQL **18.6**，实际 baseline `20261002151345_PostgreSqlErpIntegrationBaselineV1`、原前置函数/collation/六表 token trigger 与模型实体，数据为明确标注的合成 A/B。租户 schema 内仅 `component_probe` 是原子性探针，不能代替真实 ERP 业务账。

```powershell
$env:NUGET_PACKAGES = 'D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:NUGET_HTTP_CACHE_PATH = 'D:/CP6-local-test/WP02-02/adopt-a1/http'
$env:CP6_TENANCY_PG_ROOT = '<全新 tenant-connections-GUID 绝对目录>'
$env:CP6_TENANCY_PG_BIN = 'C:/Program Files/PostgreSQL/18/bin'
& 'C:/Users/tt/.dotnet/dotnet.exe' test CP6.Tests/CP6.Tests.csproj --no-restore `
  --filter 'FullyQualifiedName~ErpTenantPostgresTests' `
  --logger 'console;verbosity=normal' --logger 'trx;LogFileName=erp-tenant-postgres.trx' `
  --results-directory 'docs/development/evidence/WP02-02/erp-tenant-isolation'
```

使用 SDK 8.0.424 与已恢复依赖。未选择 PostgreSQL 集成运行时沿既有 Fact 明示跳过；实际最终选择运行没有跳过。

实际覆盖包括：普通 A/B 对全部八表直接 SQL 只读自身行、自身 INSERT、跨 tenant INSERT/UPDATE 拒绝、无 WHERE／不读旧列的常量 UPDATE 旧行保护；两种 audit 更新拒绝及全部 DELETE/TRUNCATE 权限拒绝；GUC、SET ROLE、目录读写、DDL/TEMP、helper Owner 写目录／读业务表拒绝；未知登录与删除后重建同名角色不匹配旧 OID；原 CommandInbox/Outbox 全局 MessageId 冲突与 EF 错误脱敏；真实 EF 同连接/同事务、跨 schema 回滚与显式提交；Unicode/末尾空格/UTF-16 长度兼容及 RowVersion 更新；scope 结束、嵌套 B、绑定轮换、数据库映射漂移/缺失在业务命令前拒绝；双向目录并发等待（直接观察 pg_locks）及等待控制端提交后读取新映射；首次安装对未知策略/对象/Owner、表/列/default/public 授权与 token trigger 异常的真实拒绝。

| 记录 | 实际结果与范围 |
|---|---|
| [red.log](red.log) | exit 1，夹具原 Owner 连接 pg_catalog 被旧 public-only factory 拒绝；未到隔离行为。修正为独立 Owner 迁移连接 public。 |
| [red-isolation.log](red-isolation.log) | exit 1，真实 Platform 实体非公开构造函数导致夹具 seed 失败；修正夹具使用实际非公开构造，不以设置失败冒充行为 RED。 |
| [red-isolation-2.log](red-isolation-2.log) | 有意义的首次 RED：原真实模型及合成 A/B 数据下普通账号实际读到 2 行，预期 1 行。之后才实现生产隔离代码。 |
| [implementation-1.log](implementation-1.log) | 8 项通过，0 失败/跳过；这是后续审查收紧 ACL/DELETE、字符串及目录锁之前的阶段结果，不作为最终代码全部结果。 |
| [red-text-query.log](red-text-query.log) | 新字符串查询实际被数据库拒绝，暴露 pg_catalog 下未限定业务 collation；随后仅限定该已知 collation 的 public schema。 |
| [implementation-2.log](implementation-2.log) | 9 项通过，0 失败/跳过；包含 ACL 收紧、无 runtime DELETE、真实字符串/RowVersion 与 EF 全局冲突脱敏。尚不含最终目录事务锁。 |
| [final-verification.log](final-verification.log) / [TRX](erp-tenant-postgres.trx) | 最终 10 项通过，0 失败/跳过；包含目录锁、public relation/列 ACL 及前述最终行为。 |

最终自有根目录：`D:/CP6-local-test/WP02-02/tenant-connections-d328eb3ea49744caa847d76335d7bfe9`。`server-version.txt` 为 18.6；夹具核实际 data_directory、postmaster.pid 路径/端口和 ownership marker 后停止，仅停止自有实例。收尾读取 `stopped.txt`，确认该目录无活动 postmaster.pid、55436 LISTEN 为 0。本轮七个新根目录均确认 stopped、无活动 PID；原数据、私有凭据文件及失败日志保留，未覆盖或清理。其余历史失败/阶段根目录依次为 `tenant-connections-3fe08e1874e2411f946d585bb3e19e42`、`tenant-connections-8e71a94c2cb441e6a0079398eaa0a42e`、`tenant-connections-c1824a92c8ed4ebab7bbf160a6efdcec`、`tenant-connections-4b285a34b2e745bbbc98c974e899be12`、`tenant-connections-192ce9d1971343c6a5927349bd33cbc9`、`tenant-connections-0c6990c9fda0492a9ec9e5e3ed4446ce`，均在同一 `D:/CP6-local-test/WP02-02/` 下。

既有 System.Security.Cryptography.Xml 8.0.4 NU1903、CS1998 与 xUnit2017 告警保留；中间测试新增的 EF1002 已改为明确固定 SQL 后消除，最终无新增告警。没有触发/重跑/取消 GitHub Actions、修改工作流、生产发包/签名、部署或迁移既有业务库。

## 复用与未验证范围

复用上一 [连接组件报告](../tenant-connections/DELIVERY.md) 中未受影响的池预算、A→B→A 池复用、专用登录、凭据轮换/排空、取消、session reset、强制错借及 PGOPTIONS 防护：原 focused-final 的 28 项有效范围，加取消异常脱敏后单项回归，保持原来源，不称本轮重跑。新增租约 getter/检查只供本 adapter 使用，既有连接路径无语义改变；本轮独立验证实际同连接事务与 scope/binding 拒绝。原 provider 合同证据仅复用于未改变类型/转换/迁移范围；受影响的 collation 限定、Unicode/空格/长度与 RowVersion 已实际运行。

仍未完成：其他上下文的布局/RLS/动态模型缓存采用、完整业务对象/writer 清单、真实宿主认证及当前授权、Owner 最终门和真实业务原子写集、已登记版本与部署环境 readiness 接线、重入 provisioning/迁移历史/中断恢复、完整逐租户备份恢复、容量与长事务测量、原 17 PGT 及真实业务验收。全目录锁是本组件的保守边界，控制映射变更可能等待其他租户长事务；不据此承诺生产吞吐或恢复指标。首次安装没有幂等恢复/升级承诺，既有对象一律走独立受控升级方案。

本轮一次集中 SPEC/QUALITY 审查结论为 **PASS，无剩余阻塞**。列 ACL/default ACL/public 授权面、DELETE 最小授权、目录 TOCTOU 与说明链接问题在该次审查中关闭；修正部分经局部复查，未为每个小修重新启动完整审查。审查者读取了最终 10/10 TRX 与日志，复用了有效结果，没有重复运行测试。

COORD 已同步台账、生成任务页、四状态与当前入口；`manage_roadmap.py --write` 的来源、结构和视图一致性检查通过。WP02-02 保持 IN_PROGRESS / CONFIRMED，24/375 DONE、0/125 业务完成、247 NOT_RUN 与历史缺件保持。此前已接受的业务基线决定不变，自动技术信任配置尚未安装所致的 `basis_adoption_valid=false` 继续如实保留，原清单覆盖 `inventory_coverage_valid=true`；本检查不执行业务测试，也不将技术信任声明为通过。正常 PR、远端包含性和本地 Main 同步结果由协调 Handoff 的本轮交付记录提供。
