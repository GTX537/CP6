# W432-01 · 原 PGT-010 保存点与连接复用

2026-11-09 23:24:31 UTC 从 Main #863 `96bd4da563e7ef3f3aff4100c6f311728e652752` 在 DEV-2 开工。范围为原 `PGT-010` 缺少的原生约束、语句超时和保存点复用。完整窗口初估 18–32 分钟。 [原验收项](../../acceptance/postgresql-tenancy.json) · [定向用例](../../../../CP6.Tests/Persistence/Tenancy/TenantStoragePostgresTests.cs)

## 验证范围

用例沿现有 PostgreSQL 夹具和真实 `TenantConnectionLease.InTransactionAsync` 打开普通 A/B。A 在绑定后创建保存点，先写入同租户伴随行，再分别触发真实唯一约束和 `statement_timeout`。预期 SQLSTATE 为 `23505` 和 `57014`，原失败事务内的下一条 SQL 应为 `25P02`。每次回滚到保存点后，核同 PID、XID、当前用户、数据库、路径、会话设置、租户与绑定版本。随后提交原保存点外的行，归还连接后从同一 A 后端读回并继续写入。B 原连接和既有行独立，同一数字键可写入 B 的原 schema。用例仅添加到已有本地 opt-in 测试。

W344 的已执行 `connection-failures-http2.trx` 继续证明真实 Stock HTTP 取消、原生终止坏连接、全事务回滚、原键回收以及 A/B 独立复用。W432 补实际租户 lease 中的保存点约束和超时，完整采纳需用新用例的实际结果。 [W344](../W344-01/DELIVERY.md) · [W344 原 TRX](/D:/CP6-local-test/CP6-72H-20261109/W344-01/results/connection-failures-http2.trx)

## 实际验证与续跑

已核对三份原测试输入及候选源码，复用 W364 封存的 Core、Platform 与 Npgsql 8.0.8，相关租户连接源码到 #863 保持同一版本。私有运行器 `launch.py` SHA-256 为 `2aa844fc3a75c030b414618de61b5ae435e1d9fd8b9aa945c6035e93ed197231`。Git 集中审查确认单一测试改动，`git diff --check` 通过，与待接续 W425 的非重叠测试部分预合并通过。 [运行器](/D:/CP6-local-test/CP6-72H-20261109/W432-01/launch.py) · [实际编译输入](/D:/CP6-local-test/CP6-72H-20261109/W432-01/build/inputs.json)

前两次编译启动检查分别为 28.734 和 22.770 秒，均退出 75。第一轮 14 个样本，最高物理空闲 1,009 MiB、最高可用提交 19,452 MiB。第二轮 11 个样本，分别为 1,013 和 19,449 MiB。编译门槛为连续及即时 1,000 和 19,456 MiB。 [第一轮原始样本](/D:/CP6-local-test/CP6-72H-20261109/W432-01/compile-01/compiler-gate.jsonl) · [第二轮原始样本](/D:/CP6-local-test/CP6-72H-20261109/W432-01/compile-02/compiler-gate.jsonl)

2026-11-10 00:39 UTC，`compile-03` 启动编译器并以 0 退出。编译器实际运行 1.283 秒。定向程序集 SHA-256 为 `1c2defa26ecab2885ab2454bd70dde1f2ff6ef06a0f8b36d9681f309af5b0eba`。#864 相对基线的差异不涉及这组测试与依赖。 [编译进程](/D:/CP6-local-test/CP6-72H-20261109/W432-01/compile-03/csc.process.json) · [产物与输入](/D:/CP6-local-test/CP6-72H-20261109/W432-01/compile-03/build.json)

`native-01` 在 46.641 秒内取得 22 个样本。物理空闲 1,103–1,203 MiB，可用提交 20,956–21,081 MiB。联合启动检查退出 75，PostgreSQL 和测试进程均未启动。 [联合检查原始样本](/D:/CP6-local-test/CP6-72H-20261109/W432-01/native-01/combined-gate.jsonl) · [退出记录](/D:/CP6-local-test/CP6-72H-20261109/W432-01/native-01/terminal.json)

2026-11-10 00:52 UTC，`native-02` 复用同一编译产物，启动前实测 1,483 MiB 物理空闲与 20,943 MiB 可用提交。实际 PostgreSQL 18.6 在独占 `127.0.0.1:55436` 运行，.NET 8.0.30 测试进程 16760 退出 0。整次用时 12.385 秒，原生日志从实例启动至停机为 2.820 秒。真实 `component_probe_pkey` 冲突为 `23505/25P02`，语句超时为 `57014/25P02`。两次回滚后仍为 A 后端 55488、事务 763 和原租户绑定，伴随行均为零。归还再借仍为后端 55488，事务改为 767，应用名、路径及超时恢复原值。B 复用自己的 37932，原行与独立写入均通过。 [原生会话结果](/D:/CP6-local-test/CP6-72H-20261109/W432-01/native-02/tenant-connections-pgt010/pgt010-savepoints.json) · [原生日志](/D:/CP6-local-test/CP6-72H-20261109/W432-01/native-02/tenant-connections-pgt010/postgres.log) · [原测试输出](/D:/CP6-local-test/CP6-72H-20261109/W432-01/native-02/test.stdout)

外围 RSS 采样线程在启动期读取 PID 文件时报身份核对错误，只有启动采样。原夹具停机前经自己的控制连接核对了物理 `data_directory`、PID 文件和端口，实际日志记录实例 18024 启动和完整停机。独立 `pg_ctl status` 为 3，PID 文件与 55436 监听均消失，原进程已退出。 [原夹具停机记录](/D:/CP6-local-test/CP6-72H-20261109/W432-01/native-02/tenant-connections-pgt010/stopped.txt) · [独立停机检查](/D:/CP6-local-test/CP6-72H-20261109/W432-01/native-02/pg-final-status.process.json)

合用 W344 的实际 Stock HTTP 取消及坏连接证据。原 TRX 1 项通过，原生后端 60784 实际收到取消并被复用，管理员随后终止该后端，A 后续使用 55220，B 保持自己的原连接与原结果。W432 覆盖原生保存点及局部状态复用，W344 覆盖托管取消、全事务清理和坏连接替换。集中审查核对各自原件、未变的受影响连接合同、测试输入及台账差异。原 PGT-010 采用这两段实际证据。 [W344 实测](/D:/CP6-local-test/CP6-72H-20261109/W344-01/permission-consumption-w344-01-connection-failures-http2/postgres.log) · [原 PGT-010](../../acceptance/postgresql-tenancy.json)
