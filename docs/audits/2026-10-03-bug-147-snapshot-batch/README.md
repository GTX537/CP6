# BUG147：SQL Server 身份快照批量保存

关联 [Issue #147](https://github.com/GTX537/CP6/issues/147)，阻断 [DB-COMPAT-01 / #134](https://github.com/GTX537/CP6/issues/134) 的 WP4 SQL Server 业务对照。任务基线为远端 main `0b0ab74a04c4d840a2c0bb05e36395aff8b89d32`；WP3 已通过 PR146 交付。本修复独立于仍未提交的 WP4 PostgreSQL 业务改造。

## 实际问题

WP4 在当前 140 个 Core 迁移的独立 SQL 库运行原 `ValidSnapshotsAsync`，真实业务保存产生五个身份快照和对应 Outbox。库默认 collation 为 `Chinese_PRC_CI_AS`，Snapshot.AggregateId 保持业务要求的 `Latin1_General_100_BIN2`。

EF 为有 generation trigger 的 Snapshot 批量插入生成 `MERGE … OUTPUT INTO @inserted5`。表变量中的 AggregateId 没有指定 collation，继承数据库默认；随后回查 RowVersion 的列与列 JOIN 发生 SQL468。触发器本体仅按 TenantId（Guid）推进 generation，并非冲突位置。第一次失败及参数值隐藏的实际 SQL 均保留，不能把迁移安装成功等同于该业务保存成功。

## 修复边界

采用 SQL Server 原生成器已有的逐行 `INSERT+SELECT` 路径，仅用于 `crm_identity.Snapshot` 的目标批量插入。保留同一事务、原二进制键、真实触发器与数据库返回的 8-byte RowVersion。其他表、单行、更新/删除及 PostgreSQL 继续原有生成器行为；不修改模型、迁移、库默认 collation 或全局 batch size。

实现依赖锁定的 EF Core SQL Server 8.0.30 内部扩展点，升级 EF 时须重跑专项回归。设计核对了精确版本的 [SqlServerUpdateSqlGenerator](https://github.com/dotnet/efcore/blob/v8.0.30/src/EFCore.SqlServer/Update/Internal/SqlServerUpdateSqlGenerator.cs)、[批处理结果映射](https://github.com/dotnet/efcore/blob/v8.0.30/src/EFCore.SqlServer/Update/Internal/SqlServerModificationCommandBatch.cs)和[服务注册](https://github.com/dotnet/efcore/blob/v8.0.30/src/EFCore.SqlServer/Extensions/SqlServerServiceCollectionExtensions.cs)。

逐行 INSERT 会使 tenant generation 在一次 SaveChanges 中推进多次。它是用于识别快照集合变化的 opaque 边界，不是业务版本号或保存次数；现有 reader 比较边界是否相等。单个 Snapshot 的业务 Version、payload/hash 和 RowVersion 契约保持。

## 验证状态

原 WP4 SQL 业务保存已真实失败：迁移检查 1P、业务 1F、0 skip。独立自动化回归也已确认相同 SQL468：真实 140 个迁移完成后，一次保存五个不同 aggregate 失败（[原件](native/bug147-native-corrected-red.json)、[TRX](native/bug147-native-corrected-red.trx)）。

此前第一次测试准备将大小写变体放入同一 EF tracker，在执行 SQL 前因其原有不区分大小写的键比较器失败（[保留原件](native/bug147-native-first-red.json)）。修正测试为五个规范的不同键后才得到上述 SQL468，未修改生产键比较器；数据库 BIN2 的大小写变体另用两个独立 context 验证。两个测试库均已正常 DROP，随后只读查询确认该任务前缀残留为零。

修复后的 Core 及包含 WebApi 的测试项目构建均为 0 warning / 0 error。实际执行七项真 SQL 回归与 111 项相关配置、模型、共享 Context 检查，合计 **118 passed / 0 failed / 0 skipped**。专项内容如下：

| 真 SQL 场景 | 结果 |
| --- | --- |
| 同一 SaveChangesAsync 新增五个快照，逐键回填原生 8-byte token | 通过 |
| 旧入口直接 UseSqlServer，同步 SaveChanges 批量新增 | 通过 |
| 调用方事务内快照和 generation 一并回滚 | 通过 |
| 原 C02 业务保存：五个 version1 快照、payload hash、五条真实 contract-valid Outbox | 通过 |
| 其他 ServiceToken 表批量写及 token 回填 | 通过 |
| 后续 batch-e 行实际 CHECK547 故障，自动事务内快照/generation 全回滚 | 通过 |
| 两个独立 context 保存大小写不同的 BIN2 aggregate 键 | 通过 |

七项共享一个新建的 `CP6Test_Bug147_7a17397a2878497db5ee8b9913286146` 库，实际 Core140 安装、pending0、目标 trigger enabled、默认 Chinese/目标 BIN2 collation 均已核对。临时故障约束在 finally 移除并核对不存在；fixture 最终普通 DROP，root 随后只读核对本任务前缀数据库数为0。SQL 环境由现有 SqlServerFact 入口显式提供，本次必需用例无跳过。

复现入口是本任务的测试类。先在本机配置 `CP6_TEST_SQLSERVER`，连接需允许在 loopback SQL 实例创建和删除测试自有新库；fixture 不使用连接字符串中的既有数据库作为写入目标。

```powershell
if (-not $env:CP6_TEST_SQLSERVER) { throw '请先配置本地 SQL Server 测试连接。' }
dotnet test CP6.Tests/CP6.Tests.csproj --filter 'FullyQualifiedName~CP6.Tests.Persistence.IdentitySnapshotBatchSqlServerTests'
```

采用测试真实运行前的三份变更源码和程序集 SHA 绑定；构建、TRX、原始成功/失败与清理观察已归档，见 [manifest](native/manifest.json)、[118项结果](native/bug147-final-related-and-native.json)和[清理/场景拆分](native/bug147-final-cleanup-and-case-observation.json)。首次测试准备的跟踪冲突、原业务 SQL468、修正后独立 SQL468 保持各自真实失败分类，未改写为通过。

一次任务级集中审查由独立 reviewer 核对三份代码/测试及真实门禁（[原件](native/bug147-code-task-review.json)），root 核对全部文档、属性及证据差异，无实质阻断。最终代码与实际118项门禁/审查输入一致，文档变更未触发业务重跑。

测试未覆盖生产部署、完整 WP4 PostgreSQL 业务，也没有重跑未受影响的 WP1–WP3 全部门禁。模型/迁移没有变化，EF 内部扩展点的升级风险和 opaque generation 语义如上。当前本地验证与审查通过，正常 PR 与远端交付待完成；Issue147 保持 Open。
