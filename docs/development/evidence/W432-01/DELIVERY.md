# W432-01 · 原 PGT-010 保存点与连接复用

2026-11-09 23:24:31 UTC 从 Main #863 `96bd4da563e7ef3f3aff4100c6f311728e652752` 在 DEV-2 开工。范围为原 `PGT-010` 缺少的原生约束、语句超时和保存点复用。完整窗口初估 18–32 分钟。 [原验收项](../../acceptance/postgresql-tenancy.json) · [定向用例](../../../../CP6.Tests/Persistence/Tenancy/TenantStoragePostgresTests.cs)

## 验证范围

用例沿现有 PostgreSQL 夹具和真实 `TenantConnectionLease.InTransactionAsync` 打开普通 A/B。A 在绑定后创建保存点，先写入同租户伴随行，再分别触发真实唯一约束和 `statement_timeout`。预期 SQLSTATE 为 `23505` 和 `57014`，原失败事务内的下一条 SQL 应为 `25P02`。每次回滚到保存点后，核同 PID、XID、当前用户、数据库、路径、会话设置、租户与绑定版本。随后提交原保存点外的行，归还连接后从同一 A 后端读回并继续写入。B 原连接和既有行独立，同一数字键可写入 B 的原 schema。用例仅添加到已有本地 opt-in 测试。

W344 的已执行 `connection-failures-http2.trx` 继续证明真实 Stock HTTP 取消、原生终止坏连接、全事务回滚、原键回收以及 A/B 独立复用。W432 补实际租户 lease 中的保存点约束和超时，完整采纳需用新用例的实际结果。 [W344](../W344-01/DELIVERY.md) · [W344 原 TRX](/D:/CP6-local-test/CP6-72H-20261109/W344-01/results/connection-failures-http2.trx)

## 实际验证与续跑

已核对三份原测试输入及候选源码，复用 W364 封存的 Core、Platform 与 Npgsql 8.0.8，相关租户连接源码到 #863 保持同一版本。私有运行器 `launch.py` SHA-256 为 `2aa844fc3a75c030b414618de61b5ae435e1d9fd8b9aa945c6035e93ed197231`。Git 集中审查确认单一测试改动，`git diff --check` 通过，与待接续 W425 的非重叠测试部分预合并通过。 [运行器](/D:/CP6-local-test/CP6-72H-20261109/W432-01/launch.py) · [实际编译输入](/D:/CP6-local-test/CP6-72H-20261109/W432-01/build/inputs.json)

两次启动检查分别为 28.734 和 22.770 秒，均退出 75。第一轮 14 个样本，最高物理空闲 1,009 MiB、最高可用提交 19,452 MiB。第二轮 11 个样本，分别为 1,013 和 19,449 MiB。编译门槛为连续及即时 1,000 和 19,456 MiB。编译与实际 PostgreSQL 运行待执行，PGT-010 保持 `NOT_RUN`。 [第一轮原始样本](/D:/CP6-local-test/CP6-72H-20261109/W432-01/compile-01/compiler-gate.jsonl) · [第二轮原始样本](/D:/CP6-local-test/CP6-72H-20261109/W432-01/compile-02/compiler-gate.jsonl)

在同一分支和 DEV-2 复用固定输入。资源满足时执行 `python -B D:/CP6-local-test/CP6-72H-20261109/W432-01/launch.py compile --attempt 03 --seconds 25`。定向编译通过后，预留 1,200 MiB 物理内存、19 GiB 可用提交及独占 55436，执行 `python -B D:/CP6-local-test/CP6-72H-20261109/W432-01/launch.py native --attempt 01 --compiled compile-03 --seconds 45`。验收时核对原生错误、事务、同租户复用、B 行以及实例停机，再评估正式采用。
