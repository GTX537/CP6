# W425-01 · PGT-003 实际错借物理连接

DEV-2 `codex/w425-01` 于 2026-11-09 20:01:38.279186 UTC 从 Main #859 `e7fb11afe1dcff44dbb5886964d67a85a438bcde` 开工。新增精确 PGT-003 的定向 PostgreSQL 测试。它先用正式连接管理器在 A/B 各执行读写并显式提交，再把仍打开的 A 普通 SCRAM 连接交给原 B 租约的事务入口。B 使用同一有效目录绑定和经可信上下文入口验证的合成人类身份。用原生 PID 和会话身份标记限制日志窗口，断言只收到一条身份守卫 SQL，业务回调与业务 SQL 均为零，并比较两租户业务行的原生前后摘要。[测试入口](../../../../CP6.Tests/Persistence/Tenancy/TenantStoragePostgresTests.cs) · [原验收卡](../../acceptance/postgresql-tenancy.json)

## 实际离线结果

- 仅调整现有 PostgreSQL 测试夹具和可信身份测试适配器。夹具使用 32 MiB shared buffers，原生日志标识后端 PID。计划在隔离库中临时开启 A 普通角色的 SQL 记录，既有测试身份默认行为保留。封存 W364 Core 的 SHA-256 为 `1b7b9bb2af0b5d8d5a5eb075582bdb9a68cfacb27fdb3a67f3858dfc47076f00`。对照 #859 的租约、管理器、绑定和依赖清单一致。复用 W371 的 .NET 8 参考路径。尚未进行生产代码构建。
- `compile-01` 在 1,031 MiB 物理和 20,398 MiB 可用提交时启动 PID 11736，20:12:21.271671 UTC 退出 1。直接 Roslyn 的 `/warnaserror` 报五条 `CS1701`。封存 `xunit.core` 引用 `System.Runtime 4.0`，`xunit.assert` 引用 `System.Runtime`、`System.Collections.Concurrent`、`System.Collections` 和 `System.Collections.Immutable` 的 6.0 版本，直接编译以选定的 .NET 8 合约解析。仅为下一私有编译参数加入 `/nowarn:1701`，其余告警仍视为错误。[原编译诊断](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-01/csc.stdout) · [进程结果](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-01/csc.process.json)
- `compile-02` 自 20:14:24.508934 UTC 等待 120.548 秒，20:16:25.057963 UTC 退出 75。60 个样本的物理内存为 638–894 MiB，可用提交低点 19,418 MiB。此轮未启动编译器。[原资源样本](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-02/compiler-gate.jsonl) · [原终止](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-02/terminal.json)
- 2026-11-09 22:02:25 UTC 从同一检查点恢复。Main #862 相对任务基线的 Core、PostgreSQL、测试和包定义保持原输入。`compile-03` 的新鲜样本为 1,007 MiB 物理、19,874 MiB 可用提交，Roslyn PID 52136 实际退出 0，编译 1.171 秒，含门槛与收尾 6.992 秒。私有 `CP6.Tests.dll` SHA-256 `eb4922882657504ce85a3a81c8c0a56e56a986079eb6656f83a974bc83b0b239`，使用原三个源码指纹和仅针对旧 xUnit 引用的 `CS1701` 兼容参数。原 `stderr` 为空，编译器已退出。PostgreSQL 和测试 CLR 待实跑。[实际编译](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-03/csc.process.json) · [制品与原件](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-03/build.json)

## 实库验证

2026-11-10 01:03 UTC 恢复 DEV-2 的准确源码与 `compile-03` 产物。启动前物理空闲 1,320 MiB，可用提交 20,888 MiB。CLR 55380 实际执行用例并退出 0，整次 12.842 秒。独占环回 55436 的 PostgreSQL 18.6 从启动至停机为 2.460 秒。 [测试进程](/D:/CP6-local-test/CP6-72H-20261109/W425-01/native-01/test.process.json) · [退出记录](/D:/CP6-local-test/CP6-72H-20261109/W425-01/native-01/terminal.json)

真实 A/B 正向控制各自提交。B 误接的原物理后端在前后均为 48928、`tenant_a`，原 B 绑定预期 `tenant_b`。租约以 `IdentityRejected` 拒绝，原生日志在两个 PID 标记之间仅记录身份守卫查询，业务回调和业务命令均为零。A/B 原行前后摘要均为 `eceaccd8d91a492e1a591573b1e2e74fdd552a719cec31d0210542cb7d5008e1`。 [实际结果](/D:/CP6-local-test/CP6-72H-20261109/W425-01/native-01/tenant-connections-pgt003/pgt003.json) · [原生日志](/D:/CP6-local-test/CP6-72H-20261109/W425-01/native-01/tenant-connections-pgt003/postgres.log)

改用私有采样入口记录初始化期同目录的负 PID 和默认 5432。实际实例记录为正 PID 14376 和 55436，停机身份核验沿用原条件。夹具停机成功，独立 `pg_ctl status` 返回 3，监听与实例 PID 均已释放。 [采样入口](/D:/CP6-local-test/CP6-72H-20261109/W425-01/launch-pid-observation.py) · [原采样](/D:/CP6-local-test/CP6-72H-20261109/W425-01/native-01/runtime-memory.jsonl) · [实际停止](/D:/CP6-local-test/CP6-72H-20261109/W425-01/native-01/stopped.json)

合入 Main #865 到任务分支后，按同一参考集定向编译两次实证涉及的合并源码。编译器 22596 退出 0，用时 1.354 秒。集中审查覆盖真实错借、守卫和两侧业务摘要，以及 W432 在同一测试文件中的非重叠回归。原 PGT-003 采用。 [合并源码与产物](/D:/CP6-local-test/CP6-72H-20261109/W425-01/integration-compile-01/build.json)
