Refs #159, #134

两个 Space worker 同时读到同一待领取任务时，PostgreSQL 可能在 Job 的乐观并发检查之前插入相同 AttemptNo，导致原生 23505 直接传播。领取循环现精确识别 attempt-number 唯一索引冲突，以及已有 classifier 能识别的乐观并发、死锁和序列化冲突，完整回滚并释放旧事务后重读任务。沿用既有五次上限、任务筛选和租约语义；未知约束不重试，取消传播，无迁移或公开契约变更。

本地 PostgreSQL / SQL Server 原任务、checkpoint、生成和保留 17 项加六项新回归，各精确 23/23，零跳过，使用同一源码和构建产物。修复前的协调回归实际观察到 PostgreSQL 23505 并失败；修复后仍观察到该原生冲突，最终只保留一个领取者和一个 attempt。locked restore/build 零 warning/error；恢复控制的注入失败与原生并发证据明确区分。

一次任务级审查及原始失败/成功、命令和源码/产物哈希见 `docs/audits/2026-10-03-bug-159-space-job-claim-recovery/`。合并后另做原步骤冒烟、远端 main 核对及自有库清理，再关闭 #159。WP6 最终双库矩阵、最新应用恢复验收及父任务交付仍待完成。本 PR 不触发 Actions、不修改工作流/分支保护、不搬迁现有业务数据或部署生产。
