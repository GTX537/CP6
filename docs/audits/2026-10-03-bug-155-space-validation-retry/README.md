# BUG155：Space 并发校验申请的完整事务恢复

活动状态以 [Issue #155](https://github.com/GTX537/CP6/issues/155) 为准；属于 [DB-COMPAT-01 / #134](https://github.com/GTX537/CP6/issues/134) 的 WP6 收尾发现。修复分支从已确认远端 main `59e09f6e68a36734c64144abfcefc81b7c9c8d9d` 建立。现已由 [PR156](https://github.com/GTX537/CP6/pull/156) 正常交付，并完成合并后原步骤复测、远端包含性和两个自有库清理，Issue 已关闭。

## 原始问题与根因

WP6 正式 PostgreSQL Matrix 的 25 个入口通过后，`space-design-publish` 35 项中 34 通过、1 失败。失败为原 `Concurrent_same_input_reuses_one_run_and_one_job`：两个独立 Context 申请同一个 tenant/version 的校验，生产 `RequestValidationAsync` 保存时发生真实原生 `40001 / SerializationFailure`，通过 EF 包装后传播。进程 exit 1，正式 runner 返回失败，库及原始报告保留；原 TRX SHA-256 为 `764A72A1C3072C957A370F85BAE43D5BDFFE3FC717762BD55A6710DA4DCC7EE9`。

服务先开启 Serializable，再等待事务级资源锁。PostgreSQL 锁等待不会刷新已建立的串行化快照；请求获得锁后仍可能读取旧版本/校验结果，在保存时遭遇冲突。该入口缺少失败事务完整处置之后，以新快照重新读取并执行整个申请的恢复协议。原业务断言应继续要求一个 ValidationRun、一个 Validate Job、另一请求复用同一结果。

## 修复边界

`RequestValidationAsync` 保留参数、结果、Serializable、资源键、权限检查、输入哈希、run/job 创建及原业务唯一性。针对既有 classifier 已识别的死锁/序列化冲突，最多执行三次完整事务尝试。每次失败先由 attempt 的 `await using` 完成回滚/释放，确认没有残留事务，再清理该尝试跟踪的状态，重新开启事务并读取当前数据。未知错误不重试，取消和耗尽继续传播。

关系型入口要求自有事务和无待提交变更的 Context，拒绝接管显式/ambient/enlisted 调用方事务，也不保存或丢弃调用方未提交的状态。非关系型路径保持原有单次行为。数据库迁移、依赖、公开接口、校验规则和正常 worker 未改；生产代码修复不能由测试忽略异常代替。

## 回归与证据口径

原三项校验测试保持。新增六项覆盖：

- 两个独立真实数据库请求的协调并发。PostgreSQL 第二事务在第一事务提交前建立真实串行化快照，保留生产资源锁；必须观察实际 `40001` 后恢复为同一结果和唯一 run/job。
- 调用方 Added 状态不被保存或丢弃；显式调用方事务保持可用并可自行回滚。
- 未知错误不重试，取消中止恢复，持续冲突最多三次且数据库没有部分 run/job/状态提交。

后三种恢复控制在真实数据库事务中注入已说明的异常，分别验证控制边界；它们不能代称真实 SQL Server 死锁或 PostgreSQL 序列化冲突。实际原生冲突由协调回归和原始 WP6 失败单独证明。

新增协调回归在未修改生产服务时已确定性失败，包含真实 native `40001`；`bug155-pg-coordinated-native-red-v2` 保留源码输入哈希、完整运行目录清单、原始 TRX、进程和错误观察。先前 using 位置编译失败、测试同步超时及缺少事务扩展命名空间的编译失败也保留，均不称作该原生 RED。

| 实际执行 | PostgreSQL | SQL Server | 范围 |
| --- | --- | --- | --- |
| 校验原三项与新增六项 | 9/9 | 9/9 | PostgreSQL 真实 40001 后恢复；两库均检查控制边界 |
| 完整设计/发布相关用例 | 41/41 | 41/41 | 原必需 35 项＋新增六项，逐项名称与 TRX 完全对应 |

四次实际进程均 exit 0、零非通过/skip，源码和完整运行目录执行前后保持一致；九项包含在 41 项中，不能相加作不同覆盖。候选先以锁定依赖本地恢复/构建，实际 SDK `10.0.302`、目标 `net8.0`，构建零 warning/error；SQL 与两库 41 项复用该已验证运行物，不重复构建。

[最终原件核对](native/execution/bug155-exact-local-verification.json)重新逐文件核对当前源码与运行物，四次均绑定源码输入 SHA-256 `A7E7C4F1E4C313A286923B4D26D6721AB817B58C55B1F84B5475682CAFA5852A`、运行清单 `1E38B3A295FDDF407FF4262D2372745DAE2C5A2B11B8C7436B8D883C0F3C555F`。两源码文件的准确哈希及调用方/事务边界审查见[集中审查](native/execution/bug155-review.md)，无实质阻断。Ambient/enlisted guard 和 commit-time 冲突属于源码审查范围，没有单独实际执行证明；已执行的 native 回归证明 SaveChanges 冲突恢复，不扩大为这些分支的原生复现。

[原件清单](native/manifest.json)保留 105 份、7,983,250 字节的成功/失败/构建/审查及实际使用的当时未交付 WP6 模块原件，每份副本和源文件字节哈希一致。原正式 WP6 Matrix 失败未改写为通过，初版原件和审查中的 Pending 保留各历史时点。

本机环境为 PostgreSQL `18.6` / SQL Server `16.0.1000.6`，使用两个新建 owner-marked 临时库。其 fixture 沿用 main 中的 `DB-COMPAT-01-WP5` 所有权协议，测试任务为 BUG155；不接管其他阶段库。凭据只在私有文件和子进程环境中，公共证据不包含连接、JWT、Cookie、完整业务状态或备份。未启动 Actions、修改保护、覆盖业务库或部署生产。

## 交付确认

候选 `5a868092891d079f5234407f397f47a31cfc7f75` 经 PR156 正常合入远端 main `2e1f90c629340944d95fadd7aee428f639304ed6`，完整文件树相同；远端全部 114 个文件经分页取得并逐个 Git blob 核对。最初 CLI 的 100 文件读回作为不完整观察保留，不冒称完整列表。17 个工作流及依赖在推送、PR 和合并前逐次核对，Actions 排队/运行及启动/取消均 0。

合并后原并发与协调并发两项在两库各 2/2、实际 exit 0、零 skip，绑定相同已验证源码与完整运行物；PG 再次实际观察 40001 后恢复。两个精确 receipt-owned 临时库经 owner/task、物理数据库身份和无会话核对，以普通 DROP 清理并确认不存在，无强制断开；原凭据 receipt 和 PG 角色保持。2026-10-03 18:16:14 UTC Issue 已 Closed，31 份交付后原件见[清单](native/post-merge/manifest.json)。

WP6 已接入交付主线，将新六项纳入完整设计/发布必需清单。按受影响源码和新发布物继续完成最终双库验收，父任务 #134 保持 Open；BUG155 的完成不代称整体兼容或生产部署。
