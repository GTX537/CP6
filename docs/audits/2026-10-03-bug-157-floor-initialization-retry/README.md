# BUG157：并发创建楼层的完整事务恢复

活动状态以 [Issue #157](https://github.com/GTX537/CP6/issues/157) 为准；属于 [DB-COMPAT-01 / #134](https://github.com/GTX537/CP6/issues/134) 的 WP6 最终验收发现。修复分支从已确认远端 main `2e1f90c629340944d95fadd7aee428f639304ed6` 建立；这是 PR156 已交付 BUG155 后的主线。现已由 [PR158](https://github.com/GTX537/CP6/pull/158) 正常交付远端 main `5587a2a67ae73715596ca1a135b5863005abac8d`，合并后原步骤复测和两个自有库清理完成，Issue 已关闭。下文本地原件保持合并前适用范围，最新交付原件见[清单](native/post-merge/manifest.json)。

## 原始问题与根因

WP6 接入 BUG155 后的新 PostgreSQL Matrix 在 24 个入口通过后，于 `space-clone` 原 16 项中出现 15P/1F。原 `Design_v1_concurrent_floor_initialization_allows_one_revision_winner` 的第二请求在 `CreateFloorAsync` 保存时发生真实 `40001 / SerializationFailure`，EF 包装后直接传播。进程 exit 1，runner 保持失败，后续九个入口未执行；原 TRX SHA-256 为 `829491EF1736CD49A56D69BEE2AE234F804212F59F114450D4A23DCBC0CD382C`。此入口与 BUG155 的校验申请不同，未重新打开已经交付的 BUG155。

创建楼层先开始 Serializable，再等待版本楼层初始化的事务资源锁。PostgreSQL 等待不会刷新已建立的串行化快照，第二请求获得锁后仍可能看不到赢家写入的版本和幂等结果，保存时失败。入口需要在旧事务完成回滚和释放后，以新快照完整重读和检查。不同幂等键、相同预期修订号应保留一位成功者和一个 `ConcurrencyConflict`；同键同输入应回放赢家的结果。

## 修复范围

`CreateFloorAsync` 对既有 classifier 可识别的死锁/序列化冲突最多尝试三次。每次 attempt 的 `await using` 先完成回滚和释放，wrapper 确认没有残留事务后清理该尝试的跟踪写入，再完整执行模型读取、站点权限、输入检查、幂等读取、资源锁、版本修订检查、楼层与幂等结果的原子保存。未知异常不重试，取消和耗尽继续传播。

关系型入口拒绝接管显式、ambient 或 enlisted 调用方事务，以及带待提交变更的 Context，以免保存或丢弃调用方状态。内部仓库模板路径在调用前已清理跟踪状态并使用只读查询；现有模板初始化用例随完整克隆组验证。公开 DTO、迁移、依赖、资源键、Serializable 和原业务断言保持。

## 回归与实际证据

新增七项：两个协调并发场景、两个调用方状态保护场景，以及未知错误、取消和三次耗尽的控制场景。协调测试使用真实数据库、独立 Context 和生产资源锁；PG 第二事务在赢家提交前建立真实串行化快照，必须实际观察到 native `40001`，之后验证唯一楼层、唯一幂等记录及修订号。三种控制场景在真实事务内注入明确说明的异常，不能代称实际 SQL Server 死锁复现。

未修改生产入口时，`bug157-pg-coordinated-native-red-v2` 的两个场景均因原生 `40001` 失败。此前测试成员名/枚举名的编译失败无 TRX，单独保留，不称为业务 RED。

修复后首次类级筛选实际 26/26 通过，但 runner 的预期数量误设 23，使数量门禁失败。该原件保持 `Success=false`；它多选的三项不作为额外覆盖累加。之后按原必需 16 项加新增七项精确筛选并重新验证。

| 实际执行 | PostgreSQL | SQL Server | 范围 |
| --- | --- | --- | --- |
| 精确克隆组 | 23/23 | 23/23 | 原 16 项＋新增七项，包括原并发步骤及系统/租户模板 |
| 精确设计/发布组 | 41/41 | 41/41 | 原必需组，包含已经交付的 BUG155 六项 |

候选使用锁定依赖本地恢复和构建，零 warning/error。后续组复用相同当前源码及运行物，四次进程 exit 0、零非通过/skip，TRX 精确名称和执行前后源文件/完整运行目录不变均由[最终原件核对](native/execution/bug157-exact-local-verification.json)确认。四次源输入 SHA-256 均为 `91F63672201055365B038346475C73FE993E0D92F44F5EA597A87C55596F86C8`，运行清单均为 `A3A775D9A459B6D813B33CA6C5D8A417C397C468E496BE61789FFEC9AC0FEF1B`。一次[集中源码审查](native/execution/bug157-review.md)无实质阻断；root 继续审查完整交付 diff。原始构建、失败、成功与审查按字节保留在[原件清单](native/manifest.json)，不会因提交 SHA 改变而重复同一业务验证。

Ambient/enlisted guard 与 commit-time 冲突只属于源码审查范围，没有单独原生执行证明；协调回归证明 SaveChanges 的真实冲突恢复。完整 WP6 Matrix、最新发布物 Application 和 WP6 正常远端交付仍待完成，本 BUG 的远端交付、合并后冒烟及清理已完成。

本机 PostgreSQL `18.6` / SQL Server `16.0.1000.6`，两个新建 owner-marked 临时库沿用主线 `DB-COMPAT-01-WP5` 所有权协议，实际任务为 BUG157。公共证据不包含私有连接、凭据、JWT/Cookie、完整业务状态或备份；没有启动 Actions、改变工作流/保护或部署既有环境。父任务 #134 保持 Open。

## 交付核对

候选 `5a339cbcf4f00740cad8672c75398bd879211b7e` 以正常 PR158 合并；完整分页读取的 101 个远端文件逐一与候选 Git blob 相同，远端 main 包含候选且完整树一致。合并后原并发步骤与两个协调场景两库各 3/3、exit 0、零 Other，PG 两个协调场景实际观察40001后恢复；源码/运行物与原23/41成功结果相同，未冒称重新执行全组。

两个自有库先核对 receipt/owner/task、物理身份（SQL database_id68、PG oid390489）和无活动会话，再普通DROP并确认不存在；私有receipt字节、PG角色和环境保持。Issue 于 `2026-10-03T19:37:43Z` Closed，31份合并后原件逐字节归档，清单SHA-256为 `E9F83341D0B6A3CC3E27A903944739FD80B82B83DC284E25E551132C98B76491`。原91份本地证据和历史失败未改写。
