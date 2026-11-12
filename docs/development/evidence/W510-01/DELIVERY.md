# W510-01 · 询盘 PostgreSQL 已发 COMMIT 断连与原键恢复

采用 [PGT-016](../../acceptance/postgresql-tenancy.json) 的真实 Owner 场景。CRM 测试源 `e3f5ae5db7cd989571ac3759f6c26202ee31929a` 已由 #200 采用，核对远端 Main `08ce0e29b5943eb54a9d7912c5c98d1e5931b615` 与测试树一致。环境为本地 Kestrel、.NET 8、PostgreSQL 18.6 和普通 Owner 登录。租户、业务与签名权限来源使用模拟输入。[CRM #200](https://github.com/GTX537/CP6.CRM/pull/200)

- 在 `EnqueueGates` 的实际等待固定 Owner 后端 17768 和 xid 844。环回代理放行实际 COMMIT，收到 PostgreSQL `CommandComplete COMMIT` 后截断回包。HTTP 返回可重试 `503 crm.configuration_unavailable`。最初仅一次签名授权和一次实际 Owner 尝试。独立 PG 后端回读确认原操作、规范化输入摘要、业务、审计及一条新增 Platform Outbox 已经提交。
- 同键当前权限被拒时返回 403，零 Owner 连接。重新取得当前签名授权后，新 PG 后端 29560 返回原 201、原 UTF-8 正文和 ETag。独立读取核对 journal、业务和伴随写集的原计数及摘要无新增。原 operation/input、原结果和 Outbox 哈希与连接记录留在实际运行证据。
- 两个实际客户端取消及授权到期场景在审核待锁时回滚，原 ETag 与命令、效果、Outbox、门行保持。复用 W505 的实际 `40001`/`40P01`、有限整事务重试、每次当前授权及耗尽后原键恢复。Main Stock 同期源已有实际数据库 COMMIT 完成消息丢失、原键回读及取消/失效连接验证。[W505](../W505-01/DELIVERY.md) · [Main COMMIT](../W333-01/DELIVERY.md) · [Main 同输入回归](../W338-01/DELIVERY.md) · [Main 取消](../W344-01/DELIVERY.md)

`commit-recovery-02.trx` 的真实 COMMIT 中断通过 1 项，首轮同输入的审核取消和到期通过 2 项，共 3 个不同用例，0 跳过。首轮原生断点已触发，夹具最初把签名权限之前的表单定位读连接计入实际写入尝试而失败。修正授权之后的计数边界后，仅复验受影响场景。TRX、精确断点、摘要及原生日志位于 `D:/CP6-test-artifacts/W510-01`。两套独占 PG 与环回代理均已停止。

同次实测输入保留 `WorkingPeriod.day=-0` 的原字节和摘要，类型化原响应 day 为 0，19 位小数秒与 `0000-02-29` 词法保持。PGT-025 的物理租户 schema、共享原回执和无损恢复组合待其窗口采用。现行 CRM 固定 schema 代表本案例的真实 Owner；完整 WP02-10 消费者和存储采用按原任务继续。

正式 PostgreSQL 由 17 PASS / 10 NOT_RUN 更新为 18 PASS / 9 NOT_RUN。原询盘矩阵维持 60/60。一次集中审查覆盖断点、当前授权、取消、原键和精度。CRM 本地与远端任务分支已定向清理，固定 DEV-2 已释放。Actions 触发和取消均为零，部署为零。
