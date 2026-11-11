# W478-01 — 同一四域 v11 的 ERP 原消息租约与发送状态

从远端主线 40c2670e5018fd1bc09938c675a896760ca0c948 复用 DEV-2。只启动 W477 停机源的 PostgreSQL 18.6 隔离副本，用 W466 封存应用与 Platform 依赖运行现有 C03 派送器，网络传输替换为本地模拟确认。原 v11 manifest SHA-256 f0d711a4b488e2df294364dacb8c1151f2c01036b84c24e35eed60b53cdd5e07，A 原 MessageId 8859f3b4ed5c4b339410eb2ee875312a，payload SHA-256 8838ee4bf53cbb4915756d1e827c135432d743680841dcba654cc34dcc0ccdaf。[四域原归档](../W477-01/DELIVERY.md) · [C03 派送入口](../../../../CP6.WebApi/BackgroundServices/ErpEventDispatchWorker.cs) · [ERP 对账](../../../../eng/wp02-02/rma-tenant-archive/reconcile_erp_deliveries.py)

| 原归档 | 现行 PostgreSQL | 尝试数 | 租约 | 对账结论 |
| --- | --- | ---: | --- | --- |
| Pending | Pending | 0 | 无 | PRESERVE_CURRENT |
| Pending | Dispatching | 1 | Owner、token、到期均存在，快照内有效 | PRESERVE_CURRENT |
| Pending | Published | 1 | 清空 | PRESERVE_CURRENT |

- 实际派送器提交原租约后进入一次本地模拟确认回调。只读对账观察到原 token 与备份不同并记录当前有效租约。放行本地确认后，原行提交 Published，当前时间 2026-11-11 12:24:22.062316+00。没有重试、死信、重新发布或外部传输。
- 三次只读事务按原全局键读取，原归档始终 Pending。每次的 CommandInbox 和 Outbox 各返回 PRESERVE_CURRENT，零 BLOCKED 和 UNKNOWN。三份原报告 SHA-256 分别为 de36b418484554cb2022baa56baf9123e0281d9c4e8ae8b1bda253a1200a7dd4、ca9f9dd1cf02a31e7060547828050cb2402ec90a66e4cd893a67cc3c36fc3e18、353a6f31628f148577121a809fd139a6c2acc9b44d4acef50abc1757d64e1c0d。
- A 原消息 ID、payload、原时间与业务绑定保持，三个实际 RowVersion 不同。B 原 ERP 八表逐字节一致，A 另外七表一致，RMA 原流水及 Stock 当前读数一致。原 v11 的 41 份成员与文件根未改动，所有本次验证进程均已退出。

第一次私有脚本把快照表名误写为归档文件名，读取时退出，尚未启动派送器。修正表名后使用新副本通过，应用源码和原恢复工具未修改。原始日志、三次报告、原生 COPY、派送回执及停机记录在 D:/CP6-local-test/CP6-72H-20261109/W478-01。

W477 经 #945 采用，任务分支清理、DEV-2 干净释放，实测 8.76–8.85 分钟。本窗覆盖 PGT-022 的旧 Pending 对账场景。完整候选、其他拒绝场景、正式 PGT-021/022 和批准切换仍待对应验收。候选保持 recoveryReady=false，原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。GitHub Actions 和部署未运行。
