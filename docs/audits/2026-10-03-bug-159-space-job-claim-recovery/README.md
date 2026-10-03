# BUG159：Space 多 worker 领取任务的事务恢复

活动状态以 [Issue #159](https://github.com/GTX537/CP6/issues/159) 为准。这是 [DB-COMPAT-01 / #134](https://github.com/GTX537/CP6/issues/134) 的 WP6 最终矩阵发现；修复分支从远端 main `5587a2a67ae73715596ca1a135b5863005abac8d` 建立。当前为 `LocalVerifiedRemotePending`：双库本地回归、构建和一次集中审查通过，远端交付仍待完成，父任务保持 Open。

## 根因与边界

正式 PostgreSQL Matrix 的前 26 个入口通过后，`space-jobs-generation-retention` 原 17 项中 16P/1F；原 `Two_workers_competing_for_one_job_create_one_attempt` 报出真实 `23505 / UX_Space_JobAttempt_Tenant_Job_AttemptNo`。该次运行失败，后续七个入口未执行，原件保持失败状态。

两个 ReadCommitted 事务均读到原待领取任务，各自计算相同 AttemptNo。EF 可能先插入 attempt，再执行 Job 的并发版本检查，因此 PostgreSQL 唯一键异常会先于 DbUpdateConcurrencyException。原领取循环只处理后者，数据库的唯一索引保证没有重复 attempt，但败方把异常传播给 worker。

修复沿用既有五次领取上限及事务隔离级别，使用现有 classifier 识别乐观并发、已知死锁/序列化冲突，以及精确匹配的 attempt-number 唯一索引。完整回滚、清除该领取尝试的跟踪状态，并释放旧事务后，下一次重新读取可领取任务及过期 attempt。未知错误及其他唯一约束不重试，取消继续传播；五次冲突耗尽仍返回空，供原 worker 轮询策略处理。租户过滤、任务类型过滤、优先级、过期接管、终态同步、租约 fencing、迁移和公开契约保持既有行为。

## 回归与原件

新增六项：协调并发的真实原生冲突；未知唯一约束不重试；五次上限及回滚；取消；序列化和死锁的完整事务重试。协调测试让两位 worker 都读完原任务，再提交赢家，最后放行败方保存，检查只有一个 attempt 和有效租约。PostgreSQL 必须观察到真实 23505；其他恢复控制在真实事务中注入失败，不能称为实际数据库死锁或串行化故障复现。

| 执行 | 实际结果 | 原件 TRX SHA-256 |
| --- | --- | --- |
| 修复前 PG 协调回归 | 0P / 1F，原生 23505 | `0D445FC2EFD3958E269674F4F0327A65DE7B490BE1488124734E87617C990720` |
| 修复后 PG 精确组 | 23/23，零 skip | `854A1593302F64A5F1702CB829B1AEF7523AE8BAA6C0750426F374E329DD561C` |
| 修复后 SQL 精确组 | 23/23，零 skip | `D05427A53BA4ABD78467564DAAE85A01F84146FD113102D5382AF386B540D2D1` |

精确组为原 17 项任务领取/处理/checkpoint/生成/保留用例加新增六项。两库运行同一当前源码 manifest `A7C99E1BF0E50C03A6A3672B5742444A920E7A222F17599661A3FD2AF32817B7` 和 runtime manifest `45120859AAEA5113C225945E36E78098DED276B27E83309685B61A8D44F6BE13`。实际一次修复后 locked restore/build 零 warning/error，SQL 复用同一产物，不因提交 SHA 改变重复构建。原始 Matrix 失败、稳定 RED、GREEN 的精确 case 集合、命令、日志、源码/产物绑定及审查见 [原件清单](native/manifest.json)。

## 交付范围

一次 [专项审查](native/execution/bug159-review.md) 无 P0/P1/P2 阻断；审查核对了三份源文件、1,702 项当前源码绑定、四份原始 TRX 和两份精确 case 审计。62 份公共原件已按字节归档，manifest SHA-256 为 `4A28315CE5666D6ED4420836BC40AF96FC94165F9D319C4756CB3B2BCD900B25`；私有凭据和数据库备份排除。

这项修复的正常提交/PR、远端 main 核对、合并后原步骤冒烟及两个自有临时库清理仍待完成。WP6 后续还要接入修复并完成整个双库 Matrix 和最新应用恢复验收；局部 23/23 不代称整体兼容、既有数据搬迁或生产验收。

所有验证仅在本地自有临时库执行。未启动/取消 Actions、修改工作流/分支保护或部署生产；私有凭据、连接/ownership receipt、JWT/Cookie、备份和数据库文件不归档。
