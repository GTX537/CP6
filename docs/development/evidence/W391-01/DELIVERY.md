# W391-01 · RMA v6 历史 IAM 引用原子 staging

DEV-2 于 2026-11-09 12:06:41 UTC 从 #831 `997c4ae0d1b6b9a2b1ba5bab026b20bfd3891764` 开工。采用 W387 实际 A v6 包，复用 W385 的物理隔离与原 tenant schema、journal、来源、可见性和 Identity Priority staging。[W387 v6 归档](../W387-01/DELIVERY.md) · [W385 v5 staging](../W385-01/DELIVERY.md)

`stage.py` 在原事务中将 `rma-iam-references.csv` 通过 PostgreSQL `COPY HEADER MATCH` 写入私有 `rma_archive_stage.iam_reference_snapshot`。中性表固定 kind、精确租户 UUID、原文本键与 JSONB 字段。校验 12 类精确行数，并对已恢复的 heads 与原 journal 的 actor 并集和两个引用标志做双向核对。缺失用户和角色状态按原 JSON 保留。仅 v6 的 `STAGED` 回执增加 `iamReferenceCounts`，现有只读 `recover` 返回原结果字节。[实现](../../../../eng/wp02-02/rma-tenant-archive/stage.py)

## 实库验证

最终执行 `test-native-02.log` 退出 0，原件在 `D:/CP6-local-test/CP6-72H-20261109/W391-01/rma-stage-v6-20261109T121014-5a622d81`。专属纯 PostgreSQL 18.6 环回实例使用 55567。原 W387 A manifest SHA-256 为 `280ccebb949df15bea0b5182a441e470681afab458d63cde759e81d15a88e641`。

- 实现前旧 CLI 对该原包在连接前退出 1，返回 `rma-stage-bundle-member-rejected`，保留 `test-red.log`。
- 保持原包及 pin，在全部原生 COPY 后把缺失用户的 journal-only actor 改为也有 head。该故障保留原行数和 JSON 类型。backend 58532、xid 757 收到 `P0001 rma-stage-iam-participant-mismatch` 并完整回滚。目标无残留 schema，backend 47532 只读查询无回执。
- 同物理库、同租户、同 pin 通过正常 CLI 重试，backend 59224、xid 758 提交。夹具丢弃 1096 字节 stdout，backend 41508 以只读事务返回持久原字节。回执保留 `STAGED`、`recoveryReady=false` 和 17 条 IAM 引用计数。带换行的恢复输出 SHA-256 为 `572996a33f099406ccd2523af4529d0ad7f833161bede26e1cdae861e27a4110`。
- IAM 与原 journal 由 PostgreSQL 原生 COPY 回出并各自逐字节等于输入。IAM SHA-256 为 `d3fb9815b15226ca7a1b24e6590387b88cd8b98044edf13d89ebb5bbb559655c`，journal 为 `1631e1f3bb968879e831b8b9a3552c07904febf00ad5e763c0c26cebf759a588`。原生查询实测 1 个缺失用户的 journal-only actor、1 个来自两处的原 actor、1 个禁用角色。原 tenant ACL 和中性 schema 隔离通过。
- 首轮 staging 与 recover 已返回 0，随后复用的 Python 3.8.6 时间解析器拒绝原 journal 的合法五位小数秒。保留 `test-native-01.log`。第二轮改用 PostgreSQL 原生 UTC/bytea COPY，产品 SQL 保持一致。两个私有 PG 均停止，最终 postmaster 44780 的 `pg_ctl status` 为 3，55567 无监听。
- 实物 v1–v5 生成的十份 stage/recover SQL 均与 #831 逐字节相同，含原回执读写。结果在 `test-legacy-sql.log`，原行为实库证据复用 W385。

## 复现和范围

使用 W387 实际原包、空闲环回端口及任务专属证据目录。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_iam_stage.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_iam_stage.py' --root "$b/W391-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55567 --bundle "$b/W387-01/rma-iam-20261109T115615-5754e16b/v6-cut/bundle" --manifest-sha256 280ccebb949df15bea0b5182a441e470681afab458d63cde759e81d15a88e641

本窗交付中性 staging 和原回执恢复，当前 IAM、活动切换与正式 PGT-021/022 恢复验收分窗。分支 `codex/w391-01` 本地保存，DEV-2 工作树干净，远端采用及席位释放由 COORD 执行。[正式验收卡](../../acceptance/postgresql-tenancy.json)
