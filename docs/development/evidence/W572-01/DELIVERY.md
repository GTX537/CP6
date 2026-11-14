# W572-01 · 旧 OA 单笔转交与 Main IAM 撤权

2026-11-14。本地 Main 基线 `32a1182e31e506789c614dec3766cf5edad8f643`，受测修复 `f86490c267cc7585bf6640464a0c943d215ae3e6`。模拟 OA 数据通过真实 Main Kestrel、登录会话和 PostgreSQL 18.6 验证。 [缺陷 #1065](https://github.com/GTX537/CP6/issues/1065)

`POST /api/oa/inbox/transfer` 原有动作许可检查和引擎事务分开。旧请求可以在数据库写入等待时跨过撤权提交，随后仍完成转交。旧版实测撤权先提交时仍返回 200，另一个顺序通过。先执行的夹具启动因新目录名不符约定被拒，改用约定的新隔离目录后才得到实际基线。失败原件分别保存在任务目录的 `baseline/results.trx` 和 `baseline-correct-root/results.trx`。

单笔入口复用 `CurrentWriteAuthority` 和请求的 Core 事务。它先取得 Main 共同 PostgreSQL 事务根，再重验当前会话和 `oa-inbox:transfer`。引擎在该事务内继续执行自己的委派、归属和租户检查，并提交改派、传签读模型、履历及待办通知意图。

| PostgreSQL 提交顺序 | 观测与回读 |
| --- | --- |
| Main IAM 撤权先提交 | 撤权持共同根等待角色动作表，转交后端在 `cp6_authentication_v1.acquire()` 上等待。撤权提交后旧转交返回 403，原处理人和履历保留，没有新的传签、历史或通知意图。 |
| 旧 OA 转交先提交 | 转交持共同根等待工作流任务表，撤权后端在同一个根上等待。合法转交返回 200。原传签行转为已转交，新待签行指向受让人，改派、历史和待办各提交一次。之后旧会话再次转交返回 403，已提交写集保留。 |

两种顺序均由独立 PostgreSQL 连接用 `pg_blocking_pids` 观察，验证阻塞期间没有可见写集，随后读回角色修订、权限代次、身份快照、Outbox、审计和原回执。最终 2 项 PostgreSQL HTTP 并发回归与 13 项相关既有组件测试通过，零失败、零跳过。组件测试覆盖批量转交和审批服务原有行为。

本地使用已受测的 WebApi 程序集执行 PostgreSQL 配置的 Debug 发布冒烟。310 个发布文件的 SHA-256 清单为 `78c23dab42d6f05b09b6071a5219dc1981133864f17212ca9b561412fad6ad0e`，WebApi 为 `946792ce37f17272464ab04f38f316c9da17362118fed08ff4287100a329e0ac`，与并发受测程序集相同。首次发布冒烟的 PowerShell 启动等待未退出，核对并停止自有进程后改用独立的 PostgreSQL 启动进程。最终复用本任务隔离数据库，API 实际启动，`/health/live` 返回 200，匿名转交返回 401。原件、清单和停止记录位于 `D:/CP6-test-artifacts/W572-01`，发布及测试进程已停止。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。旧 OA 其他写者仍需验收，包含退回、批量转交及办理、撤回和触发器。财务、独立 Space Design、普通数据库写者、Main C02 broker 投递、源库无法访问或缺失尾段 WAL 时的恢复及生产切换编排仍待。Actions 启动、重跑和取消为零。此处使用本地隔离环境。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [恢复验收 W571-01](../W571-01/DELIVERY.md) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
