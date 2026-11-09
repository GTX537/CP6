# W423-01 · W372 同源 ERP 与文件切面暂停检查点

DEV-2 于 2026-11-09 19:22:46.977356 UTC 从 Main #858 `eec44dbe4f526f861f2d59837373a9fd503cfa6a` 开工。接续 W420 已释放的专属 PGDATA 原路径 `D:/CP6-local-test/CP6-72H-20261109/W420-01/source-clone/data`。原 W372 及该副本均为 `pg_ctl status=3`。沿用 `127.0.0.1:55567/w372_live`、OID `16386`、system `7706117870886737900`、安装 `2ad5d2ef-54c4-4abd-9bf9-5d2b0a1a2cc4` 和原 A/B。移交核对副本 `pg_control` SHA-256 `c4774af4900459b1a1f16756d6d375351f3507a0aaedbdaee08db15a666578dc` 及 HBA `0c8dc6e6e57399790417a6e13b3a8e1b5e27aa19708a2122148fbfe3bdcecd42`。[准确移交记录](/D:/CP6-local-test/CP6-72H-20261109/W423-01/transfer/receipt.json)

## 已执行

- 离线复核 W372 v2 的 manifest pin 与全部原成员、W420 A/B 两份五成员历史的原 pin 和两份 LocalFileStore 原字节。移交验证首轮把租户文本传入需要 UUID 的资产接口，修正后离线移交退出 0。W419 `build-03` 真实 C03 驱动、运行时配置及原依赖的摘要吻合。驱动 SHA-256 `55f86cc5a2c96a172ceb3b3406c6c4872235b3233a8aa4535766b7b021d34eef`。本轮没有重编。[离线结果](/D:/CP6-local-test/CP6-72H-20261109/W423-01/transfer-check.stdout) · [原 W419 实库驱动](../W419-01/DELIVERY.md)
- Python 3.8 语法和 CLI 检查成功。用原 W419 PostgreSQL 日志核对快照读取器，识别了原 v10 的 36 次导入，其中包含本轮 v8 所需的 20 个实际客户端标识。这是日志解析的离线检查。新的定向驱动保留 W419 CLI，复用其八表、原命令/结果字节和重复语义比较，并在同库对照 W420 RMA/文件原件。普通读路径预定通过 PostgreSQL 事务级 `SET SESSION AUTHORIZATION` 检查有效会话角色和原租户映射，登录传输采用原 W372 已有结果。[定向驱动](../../../../eng/wp02-02/rma-tenant-archive/verify_same_source_erp.py)
- `native-01` 于 19:30:07.492853 UTC 开始等待并在 19:33:08.164619 UTC 退出 75，耗时 180.669 秒。90 个采样的物理内存为 853–1,003 MiB，可用提交低点 20,308 MiB。物理内存始终低于 1,200 MiB 启动门槛。没有启动 PostgreSQL 或 CLR，55567 无连接状态。结束后副本仍为 `pg_ctl status=3`，控制文件保持移交摘要。[真实终止](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-01/terminal.json) · [原始采样](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-01/postgres-gate.jsonl)

## 接续

待 COORD 恢复 `codex/w423-01` 的 DEV-2 窗口并明确放行后运行以下入口。入口要求原 `native-01` 为资源退出且没有启动记录，重新核验移交 pin、原制品和副本状态。门槛保持 PG 1,200 MiB 两次加 fresh、CLR 1,050 MiB 两次加 fresh、可用提交至少 19 GiB，以及 55567 的 Windows 独占绑定。[任务私有运行器](/D:/CP6-local-test/CP6-72H-20261109/W423-01/launch.py)

```powershell
& 'C:/Program Files/python/python.exe' -B 'D:/CP6-local-test/CP6-72H-20261109/W423-01/launch.py' --attempt native-02 --seconds 180
```

A/B 的本轮 C03 原写入和重复、普通会话读、八表和资产在正常 v8 中的同切面比较均待执行。RMA 和 FileContent 采用 W420 的实际先前结果。W423 本地检查点留在 `codex/w423-01`，DEV-2 干净，是否暂时释放席位由 COORD 处置。
