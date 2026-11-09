# W425-01 · PGT-003 暂停检查点

DEV-2 `codex/w425-01` 于 2026-11-09 20:01:38.279186 UTC 从 Main #859 `e7fb11afe1dcff44dbb5886964d67a85a438bcde` 开工。新增精确 PGT-003 的定向 PostgreSQL 测试。它先用正式连接管理器在 A/B 各执行读写并显式提交，再把仍打开的 A 普通 SCRAM 连接交给原 B 租约的事务入口。B 使用同一有效目录绑定和经可信上下文入口验证的合成人类身份。用原生 PID 和会话身份标记限制日志窗口，断言只收到一条身份守卫 SQL，业务回调与业务 SQL 均为零，并比较两租户业务行的原生前后摘要。[测试入口](../../../../CP6.Tests/Persistence/Tenancy/TenantStoragePostgresTests.cs) · [原验收卡](../../acceptance/postgresql-tenancy.json)

## 实际离线结果

- 仅调整现有 PostgreSQL 测试夹具和可信身份测试适配器。夹具使用 32 MiB shared buffers，原生日志标识后端 PID。计划在隔离库中临时开启 A 普通角色的 SQL 记录，既有测试身份默认行为保留。封存 W364 Core 的 SHA-256 为 `1b7b9bb2af0b5d8d5a5eb075582bdb9a68cfacb27fdb3a67f3858dfc47076f00`。对照 #859 的租约、管理器、绑定和依赖清单一致。复用 W371 的 .NET 8 参考路径。尚未进行生产代码构建。
- `compile-01` 在 1,031 MiB 物理和 20,398 MiB 可用提交时启动 PID 11736，20:12:21.271671 UTC 退出 1。直接 Roslyn 的 `/warnaserror` 报五条 `CS1701`。封存 `xunit.core` 引用 `System.Runtime 4.0`，`xunit.assert` 引用 `System.Runtime`、`System.Collections.Concurrent`、`System.Collections` 和 `System.Collections.Immutable` 的 6.0 版本，直接编译以选定的 .NET 8 合约解析。仅为下一私有编译参数加入 `/nowarn:1701`，其余告警仍视为错误。[原编译诊断](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-01/csc.stdout) · [进程结果](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-01/csc.process.json)
- `compile-02` 自 20:14:24.508934 UTC 等待 120.548 秒，20:16:25.057963 UTC 退出 75。60 个样本的物理内存为 638–894 MiB，可用提交低点 19,418 MiB。未达到编译启动门槛，没有启动第二个编译器。当前私有 `CP6.Tests.dll` 尚未生成，PostgreSQL 与测试 CLR 尚未启动。[原资源样本](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-02/compiler-gate.jsonl) · [原终止](/D:/CP6-local-test/CP6-72H-20261109/W425-01/compile-02/terminal.json)

## 接续

在准确检查点恢复 DEV-2 后先运行 `inspect`，确认分支、提交、干净状态、三个跟踪源文件与 Git blob 的字节及封存依赖。COORD 分别放行后依次运行编译及实库。编译门为两次及临启动 1,000 MiB，联合 CLR 与 PG32 门为两次及临启动 1,200 MiB，可用提交均至少 19 GiB。复用既有夹具的 55436 独占端口，启动新任务私有目录。实库入口只接收明确指定且已验证产物与源码指纹的编译窗口。[私有运行器](/D:/CP6-local-test/CP6-72H-20261109/W425-01/launch.py) · [固定输入](/D:/CP6-local-test/CP6-72H-20261109/W425-01/build/input-pins.json)

```powershell
& 'C:/Program Files/python/python.exe' -B 'D:/CP6-local-test/CP6-72H-20261109/W425-01/launch.py' inspect
& 'C:/Program Files/python/python.exe' -B 'D:/CP6-local-test/CP6-72H-20261109/W425-01/launch.py' compile --compile-attempt compile-03 --seconds 120
& 'C:/Program Files/python/python.exe' -B 'D:/CP6-local-test/CP6-72H-20261109/W425-01/launch.py' native --compiled-attempt compile-03 --seconds 160
```

PGT-003 仍为 `NOT_RUN`。分支保留本地检查点，DEV-2 工作区干净，席位处置交 COORD。
