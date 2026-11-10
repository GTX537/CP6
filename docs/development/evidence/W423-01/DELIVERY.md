# W423-01 · W372 同源 ERP 与文件切面

DEV-2 于 2026-11-09 19:22:46.977356 UTC 从 Main #858 `eec44dbe4f526f861f2d59837373a9fd503cfa6a` 开工。接续 W420 已释放的专属 PGDATA 原路径 `D:/CP6-local-test/CP6-72H-20261109/W420-01/source-clone/data`。原 W372 及该副本均为 `pg_ctl status=3`。沿用 `127.0.0.1:55567/w372_live`、OID `16386`、system `7706117870886737900`、安装 `2ad5d2ef-54c4-4abd-9bf9-5d2b0a1a2cc4` 和原 A/B。移交核对副本 `pg_control` SHA-256 `c4774af4900459b1a1f16756d6d375351f3507a0aaedbdaee08db15a666578dc` 及 HBA `0c8dc6e6e57399790417a6e13b3a8e1b5e27aa19708a2122148fbfe3bdcecd42`。[准确移交记录](/D:/CP6-local-test/CP6-72H-20261109/W423-01/transfer/receipt.json)

## 已执行

- 离线复核 W372 v2 的 manifest pin 与全部原成员、W420 A/B 两份五成员历史的原 pin 和两份 LocalFileStore 原字节。移交验证首轮把租户文本传入需要 UUID 的资产接口，修正后离线移交退出 0。W419 `build-03` 真实 C03 驱动、运行时配置及原依赖的摘要吻合。驱动 SHA-256 `55f86cc5a2c96a172ceb3b3406c6c4872235b3233a8aa4535766b7b021d34eef`。本轮没有重编。[离线结果](/D:/CP6-local-test/CP6-72H-20261109/W423-01/transfer-check.stdout) · [原 W419 实库驱动](../W419-01/DELIVERY.md)
- Python 3.8 语法和 CLI 检查成功。用原 W419 PostgreSQL 日志核对快照读取器，识别了原 v10 的 36 次导入，其中包含本轮 v8 所需的 20 个实际客户端标识。这是日志解析的离线检查。新的定向驱动保留 W419 CLI，复用其八表、原命令/结果字节和重复语义比较，并在同库对照 W420 RMA/文件原件。普通读路径预定通过 PostgreSQL 事务级 `SET SESSION AUTHORIZATION` 检查有效会话角色和原租户映射，登录传输采用原 W372 已有结果。[定向驱动](../../../../eng/wp02-02/rma-tenant-archive/verify_same_source_erp.py)
- `native-01` 于 19:30:07.492853 UTC 开始等待并在 19:33:08.164619 UTC 退出 75，耗时 180.669 秒。90 个采样的物理内存为 853–1,003 MiB，可用提交低点 20,308 MiB。物理内存始终低于 1,200 MiB 启动门槛。没有启动 PostgreSQL 或 CLR，55567 无连接状态。结束后副本仍为 `pg_ctl status=3`，控制文件保持移交摘要。[真实终止](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-01/terminal.json) · [原始采样](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-01/postgres-gate.jsonl)

## PostgreSQL 18.6 实测

2026-11-10 03:52:30 UTC 由 COORD 在 DEV-2 恢复原分支并合入 Main #870。`native-02` 在保护原件阶段因 RMA heads 和 FileContent heads 争用同一个 `A-heads.stdout` 而退出 1。该轮在 7.986 秒后正常停止 PostgreSQL，ERP 驱动输入及进程均未生成。定向修复为 FileContent COPY 使用独立文件名。`native-03` 核对准确停机回执后沿用同一 PGDATA，实测验证器对应提交 `36cc762d3eea1b6fa5489615db83a6069587a218`。 [首次停机](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-02/stopped.json) · [任务私有运行器](/D:/CP6-local-test/CP6-72H-20261109/W423-01/launch.py)

```powershell
& 'C:/Program Files/python/python.exe' -B 'D:/CP6-local-test/CP6-72H-20261109/W423-01/launch.py' --attempt native-03 --resume-attempt native-02 --seconds 45
```

- 原 W419 C03 驱动在 W372 副本实际处理 A/B 的同一合成请求和账号。两租户初次投递均为 `Applied`，同原报文重复均为 `Duplicate`。两组各有一条原 Aggregate、Request、Processed Inbox 和 Pending Outbox，其余四表为空。请求版本 3、聚合版本 7、终态 `C03_BUSINESS_PARTNER_NOT_FOUND` 和原报文摘要已核。两租户的全局消息键各异。重复前后的八表和序列摘要为 `5931405ace0ba842d79420fbf814d220f5039d2c8386a3a2a2938d6a1d17aede`。 [实际结果](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-03/checks/actual.json)
- 正常 `archive.py export` 与 `verify` 均退出 0。PostgreSQL 后端 56168 持有快照 `00000012-00000006-1`，`pg_dump` 及各 COPY 在 holder 提交前共导入 20 次。A 的八个 ERP 成员与独立原生 COPY 逐字节一致。v8 的 FileContent 资产投影匹配 W420 的 45 字节原件、原文件和原工程父版本。原文件经正常验证读取。B 的 ERP 八表在切面前后逐字节一致。 [v8 清单](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-03/checks/v8-cut/bundle/manifest.json) · [PostgreSQL 原生日志](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-03/postgres.log)
- 两个有效普通会话角色各读得自身一条 RMA head、两条原版本和两条原流水，外租户原流水返回 0 行且外租户 heads 无 SELECT。实际 `SET LOCAL SESSION AUTHORIZATION` 验证当前角色与租户绑定，登录传输证据沿用原 W372。原 A/B RMA、共享流水、业务伙伴数据、两份 FileContent 的五个历史成员及两个私有文件在前后复核中保持原字节。 [W420 原件](../W420-01/DELIVERY.md) · [A 普通角色](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-03/checks/A-ordinary-rma.stdout)
- `native-03` 退出 0，起止为 03:54:06.752474–03:54:31.396705 UTC，耗时 24.645 秒，其中真实 Owner 3.752 秒。启动 PG 和 CLR 前的物理内存为 1,891 和 1,838 MiB，提交余量分别为 20,106 和 20,013 MiB。原 W372 和转用副本 `pg_ctl status=3`，临时 HBA 移除，55567 释放。转用副本 `pg_control` 新 SHA-256 `f29f886f184f641b59e8c2ccf9aa50b71fdc1109c1707aa196ae0272403d8f7c`，后续接续以此停机回执为准。 [停机回执](/D:/CP6-local-test/CP6-72H-20261109/W423-01/native-03/stopped.json)

本次 v8 清单为 20 个成员，manifest SHA-256 `5e46c292fa326a6b25d25c71d7a7625a53bbfaa8ab05911177f8c2a7e9a42975`。COORD 集中审查了封存驱动和实际输入、原业务角色、日志命名修复、单一快照、原文件和 A/B 原字节。制品保持 `recoveryReady=false`。正式 PGT-021/022 的候选恢复、现行权限、消息对账和独立批准切换继续按原场景验收。
