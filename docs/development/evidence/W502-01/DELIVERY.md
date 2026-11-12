# W502-01 审核赢家事件与版本

真实 HTTP 和 PostgreSQL 复现 Issue #190。待审核记录合法申请隐私导出收到 202，用当前 ETag 放行或拒绝均收到 200。两条原生 Outbox 都将同一聚合写为 `[1,2,2]`，而持久赢家历史有三个事件位置。原始失败保留于 `decision-version-red-01.trx`。 [Issue #190](https://github.com/GTX537/CP6.CRM/issues/190)

审核 Owner 从原生持久历史计算聚合版本。PostgreSQL v2 延迟提交守卫按同一事件规则回读 `SubmissionHistory`，核对关联 Outbox。原表与 v1 归档 SQL 继续保留。数据库 Owner 显式 `InquiryDecisionLedgerV1.InstallAsync` 在事务中确认旧守卫的准确身份，再以命令表锁替换函数。正常运行只核对已安装守卫。既有环境的采用使用该显式入口，本轮实际验证在隔离数据库完成。

修复后放行和拒绝均为 `[1,2,3]`。联合回读赢家业务行、命令、效果、审计和实际 Outbox 正文及摘要，核对请求前后版本、Actor、时钟、资源、聚合版本、Causation、Correlation 与 H01 回执。原键重放恢复原 200 和 ETag，旧 ETag 的异动作返回 412，两个请求都没有新增效果。

旧守卫升级用例建立 v1 兼容原结果，并在隔离库装入准确冻结的旧函数。实际请求在升级前返回 503。显式升级及重复执行通过，升级后恢复原 200，原命令、效果、出站字节及行版本一致。`decision-version-green-02.trx` 共 13 项通过、0 跳过，还覆盖普通放行与拒绝、晚段回滚、禁用守卫拒绝、五个历史保留样本和实际导出制品。自有 PostgreSQL 已核验停止。运行脚本与两轮原始结果在 `D:/CP6-test-artifacts/W502-01`。

R-040 的其他已执行路径沿用 W134 原持久 H01 来源事实、W494 实际消息和接收效果、W491 四处置保留及 W496/W497 撤回和导出原事实。当前补齐了导出与审核相继写入的赢家事件版本。 [W134](../W134-01/DELIVERY.md) · [W494](../W494-01/DELIVERY.md) · [W491](../W491-01/DELIVERY.md) · [W496](../W496-01/DELIVERY.md) · [W497](../W497-01/DELIVERY.md)

CRM #191 采用源 `be4bd8fd03bffc401bdf8e568c263cc39c354258`，远端 `52a914c9b8b5332dd1ae0c77dd2307ef25915f1c` 与验证源码树一致。一次集中审查核对 Owner、原数据、显式升级、权限和回归。CRM 本地和远端任务分支已清理，DEV-2 的 W502 已干净释放。采纳 AC-INQ-040，原询盘 52 PASS / 8 NOT_RUN。继续 W503 验证下游效果和当前授权边界。Actions 运行与取消均为 0，部署 0。 [CRM #191](https://github.com/GTX537/CP6.CRM/pull/191)
