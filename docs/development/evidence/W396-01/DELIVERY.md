# W396-01 · Identity Priority 对账接入 v6 和 v7 原包

DEV-2 于 2026-11-09 12:43:37 UTC 从 #835 `934fe06fcca93bbc26d94128f032769815f441a1` 开工。`reconcile_identity_priority.py` 接受 v5、v6、v7，使用原独立 manifest pin、精确物理源和原 installation/Ready 及表目录检查。v7 通过 `--file-content-root` 交给现有完整归档校验。原有 Outbox/Inbox 读取、空键时的原生授权检查、隔离级别、报告字段和退出合同沿用 W384。[对账入口](../../../../eng/wp02-02/rma-tenant-archive/reconcile_identity_priority.py) · [W384 原对账](../W384-01/DELIVERY.md) · [W389 v7 资产校验](../W389-01/DELIVERY.md)

## 实库验证

使用已停止的 W389 和 W387 专属合成 PostgreSQL 18.6 原库，按 v7 后 v6 串行恢复。复用 W393 私有 `restart_owned.py` 的精确目标检查和临时指定库、指定角色、环回 HBA，进程默认只读，工具本身使用 `REPEATABLE READ READ ONLY`。两个库的原 HBA 摘要前后一致，临时 HBA 已移除。未复制库或运行 DatabaseInit。[W393 原恢复夹具](../W393-01/DELIVERY.md)

- v7 使用 W389 `cut` 原包和真实资产根，manifest SHA-256 `74e75ace3a96d3f053c2c0cfc16d44173c281d32447b2bcf4305221dda9d3681`。CLI 退出 0，backend 46256 实际执行两条空键 SQL，分别引用原 Outbox 与 Inbox，输出 `OBSERVED` 和三项零计数。缺根和错误 pin 的 CLI 均退出 1，连接前拒绝且未发布报告。原报告 SHA-256 为 `adbf87da3f4141d44ffb26b801e716b720e93c19bd2b27efffb17468b9e7418a`。
- v6 使用 W387 `v6-cut` 原包，manifest SHA-256 `280ccebb949df15bea0b5182a441e470681afab458d63cde759e81d15a88e641`。旧 CLI 对原包返回 `priority-v5-bundle-rejected`，保留 `test-red.log`。新版 backend 14112 实际匹配原 Outbox 键，`Published` 保持 `Published`，计数为 1 `PRESERVE_CURRENT`、0 `BLOCKED`、0 `UNKNOWN`。空 Inbox 实际走空键查询。原报告 SHA-256 为 `eca405292b1a5fddd4fdef70782597b82492e57165e1a6cc5d0f2976b97b94aa`。
- v6/v7 读取前后的原租户两表行摘要一致，完成报告均为 `recoveryReady=false`。原件分别在 `D:/CP6-local-test/CP6-72H-20261109/W396-01/native-v6` 和 `native-v7`。v7 私有夹具末尾的正则忽略 SQL 字面空格，首轮夹具退出 1。保留实际成功的 CLI 输出、数据库 SQL 和首轮夹具日志，修正正则后仅复核原日志并退出 0。v6 完整夹具退出 0。
- 实际 W384 v5 pin `e199721d0763698306c7963f48ba76c4417c12f7862a061ec6d72c22dc1eab48` 通过新旧入口对照。私有读取成员逐字节相等，查询、只读事务及 JSONL 生成代码保持原行为，复用 W384 已运行的实库结果。静态对照在 `test-v5-compatibility.log`。
- W389 postmaster 17444 与 W387 postmaster 52436 均已停止，55566、55567 无任务监听。原 HBA 与停止检查分别记录在各自 `stopped.json`。

## 复现和范围

在专属合成源许可窗口中，使用相同原包、实际物理 target 和新的私有输出目录。W396 私有 `run-retained.py` 保存了 v7、v6 的顺序启动、原 HBA 校验、CLI 和独立查询命令。以下为 W389 v7 源已运行时的正常入口。[入口源码](../../../../eng/wp02-02/rma-tenant-archive/reconcile_identity_priority.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    $v7 = "$b/W389-01/file-content-assets-20261109T120815-453ce71b"
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/reconcile_identity_priority.py' --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --target-file "$v7/archive-target.json" --username w350_owner --tenant 13bbd449-719b-42ba-a51d-0c7e91a5728c --bundle "$v7/cut/bundle" --manifest-sha256 74e75ace3a96d3f053c2c0cfc16d44173c281d32447b2bcf4305221dda9d3681 --file-content-root "$v7/private-local-store" --output $NewPrivateOutput

本窗交付当前原键的只读对账。发送、导入、活跃切换和正式 PGT-021/022 恢复验收分窗。分支 `codex/w396-01` 本地保存，DEV-2 工作树干净，远端采用和席位释放由 COORD 执行。[正式验收卡](../../acceptance/postgresql-tenancy.json)
