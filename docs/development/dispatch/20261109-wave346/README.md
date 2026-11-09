# W346/W347 两个独立 CP6 窗口

均从已确认的 Main `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 开工。#801 已正常合并、远端回读、删除准确分支、释放 DEV-1，并回读 GitHub CLOSED。COORD 串行回收台账与合并，两窗各自保留源码、进程与数据库。 [#801 交付](../../evidence/BUG-801-PG-FIRST-ADMIN/DELIVERY.md) · [72 小时计划](../../planning/20261109-codex-72h-execution.md)

## W346-01 Stock 首装回执丢失的实际只读恢复

2026-11-09 06:50:06 UTC，STOCK-RECOVERY，DEV-1，`codex/w346-01`。原 WP02-02，关联 WP02-10。S02-00、WP02-01 与 W344 采用已核实。独占 PostgreSQL 端口 55556。估 30–50 分钟。

在现有 CLI 增加准确的 `inspect-stock-read-v1 --stdin`。实际首装子进程提交后舍弃输出，另一个子进程用原物理目标与目录取回同一安装 ID，并驱动原写栅栏。保留未装、错目标、目录或读取边界漂移的真实结果。原 v1 安装 ID 是数据库回执，调用者 OperationId 的合同另行设计。核当前已批准写扩展后的读取恢复。

## W347-01 两版本共同 service-token 本地业务

2026-11-09 06:50:27 UTC，COMPAT-VERIFY，DEV-2，`codex/w347-01`。原 WP02-28，WP02-25 和 WP02-27 及 W345 制品采用已核实。使用新私有 PostgreSQL 库与进程。估 25–50 分钟。 [两份已固定制品](../../evidence/W345-01/DELIVERY.md)

复用 W345 两个准确收据和原制品，安装共同 `service-token-v1` 五表合同，启动两个实际宿主。旧版签发、当前版撤销，再以旧版读取及重放原结果，核真实同库行、快照、generation 与 Outbox。覆盖错误安装和异租户等可判定拒绝组合。结果按同一已安装布局的本地应用兼容范围登记。

各执行者写自身范围、真实验证和独占交付报告。COORD 回收共享状态与合并。
