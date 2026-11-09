# W404-01 · RMA v9 历史证据私有暂存

DEV-2 于 2026-11-09 13:59:21 UTC 开工，基线 #841 `b917153c035e923ff06c73db5656a2fa7951207f`。现有 `stage.py stage` 接受外部摘要固定的 v9 归档，将审计与 legal hold 写入 `rma_archive_stage.security_audit_snapshot` 和 `rma_archive_stage.legal_hold_history_snapshot`。沿用原 PostgreSQL 类型、列顺序、NULL、PK 与 hold 原 `version` 唯一键。审计字符列复用与原 Core 相同的 ICU 定义，hold 原 `sequence` 按 `bigint` 保存。两表分别绑定归档租户，hold 另绑定原源环境及原活动行 review 条件。[暂存入口](../../../../eng/wp02-02/rma-tenant-archive/stage.py) · [W402 原件](../W402-01/DELIVERY.md)

两次 COPY、行数校验、前置归档成员及原 `STAGED` 回执使用同一个 PostgreSQL 事务。v9 回执保存经归档校验的 `privacyEvidence` 原段，包括 `historical-only`、源环境、原表合同和行数。既有 `recover` 按同一物理目标、租户和摘要返回持久化字节，`recoveryReady=false`。目标按原物理身份、数据库 ACL 和空库门进入暂存。现行 audit、hold、环境绑定和原序列留在各自现有 Owner 入口。[W398 v8 暂存](../W398-01/DELIVERY.md)

## 实际验证

使用原 W402 v9 manifest SHA-256 `e1350c68ebc5618df6124384442630d3bba88186d8ae018b7a9fc2af4801e3bd` 及原私有资产根。全新 PostgreSQL 18.6 回环目标 `127.0.0.1:55564/w404_privacy`，数据库 OID `16386`，系统标识 `7706172247649359776`。开始时可用物理内存 923.3 MiB，虚拟内存 20737.9 MiB。CPython 3.8.6 原生复现退出 0，运行 13.876 秒。业务输入为 W402 合成的 A/B 审计与 legal hold 历史。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_privacy_evidence_stage.py) · [实际原件](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/actual.json)

- 两份原生 COPY 后、写回执前，只在测试驱动加入一条新 `sequence` 复用原 `version` 的 hold。PostgreSQL 在 `legal_hold_history_snapshot_version_key` 返回 `23505`。暂存及租户 schema 均回滚，按原目标及原摘要 `recover` 得到 `rma-stage-receipt-not-found`。[晚段原生错误](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/v9-late-native/stage.stderr) · [回滚后查询](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/v9-rollback-no-receipt.stderr)
- 用相同物理目标及原 pin 走正常 CLI 重试，测试端丢弃 3824 字节响应。正常 `recover` 返回的单条结果行与数据库持久化回执逐字节一致，`privacyEvidence` 等于原 manifest 对应段。结果行 SHA-256 `4ecf5ec8d5cb89be28b2fb1573a0c89034099a8cd6ba6e974589328c02084700`，独立保留含行结束符的原 CLI 与 psql 流。[原回执](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/v9-recover.stdout)
- 暂存 CSV 各两行，按原键做 PostgreSQL COPY 回读，逐字节等于 W402 输入。审计 842 字节，SHA-256 `656603562bc6fd67ce48bcb9d45d38fae8e8e99cee6b76e9fc4dee6aaecc4ec4`。Hold 814 字节，SHA-256 `fa9f9757a828ccc8d8d6def3f49a8ef48ce74b7f1184d9ac7168bfa7461f074b`。原 JSONB、Unicode、尾空格、NULL、时间精度与 `9007199254740993`/`9007199254740994` 均保留。实际目录核对原类型、NULL 属性、PK 和唯一键。[审计目录](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/security-audit.csv-catalog.stdout) · [Hold 目录](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/legal-hold-history.csv-catalog.stdout)
- 审计异租户、hold 异目标和异环境三次原生更新均返回 `23514`。实际 A/B 普通角色分别访问两张历史表均返回 `42501`。目录确认私有表和数据库权限边界，真实 audit 表、hold/环境 schema 及 staging 序列均不存在。[原生隔离](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/v9-native-inactive-acl.stdout) · [原生 ACL 错误](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/ordinary-a-audit-denied.stderr)

用原 v1–v8 制品离线生成 stage/recover SQL，候选和 #841 的 16 份驱动逐字节相同，v8 stage 亦与 W398 实库原驱动相同。初版比较额外要求 v1 等同于更早的 W370 原驱动，发现两者早已有缩进差异。最终以本窗 #841 基线比较并保留旧差异。沿用 W398 等实库历史结果。[基线兼容](/D:/CP6-local-test/CP6-72H-20261109/W404-01/legacy-driver-compatibility-final.log) · [较早 v1 差异](/D:/CP6-local-test/CP6-72H-20261109/W404-01/legacy-historical-v1-format-diff.log)

唯一 postmaster `53580` 已原生停机，`pg_ctl status` 返回 3，55564 无监听且 `postmaster.pid` 已消失。可丢弃 PGDATA 为 `W404-01/rma-stage-v9-20261109T140404-6c7227ce/data`，同级证据及 W402 原归档保留。[停机原件](/D:/CP6-local-test/CP6-72H-20261109/W404-01/rma-stage-v9-20261109T140404-6c7227ce/stopped.json)

本窗范围是私有历史暂存与同原件回执恢复。正式 PGT-021/022 保持 `NOT_RUN`。本地分支 `codex/w404-01`，集中审查、PR、合并和 DEV-2 官方释放交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
