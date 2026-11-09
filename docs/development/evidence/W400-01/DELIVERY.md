# W400-01 · 同一 v8 原包的 IAM 与 Identity Priority 对账

DEV-1 于 2026-11-09 13:29:58 UTC 从 #839 `ee2735d66c2d9476448aaf6af1ea8f38cd3a2e54` 开工。IAM 和 Identity Priority 两个入口接受原 v8，各自沿用现有完整归档校验、独立 manifest pin、原资产根、物理目标、只读查询及报告合同。对同一租户和 v8 pin，两个命令各持一个当前快照。[IAM 入口](../../../../eng/wp02-02/rma-tenant-archive/reconcile_iam.py) · [Identity Priority 入口](../../../../eng/wp02-02/rma-tenant-archive/reconcile_identity_priority.py)

    $common = @('--pg-bin', $PgBin, '--target-file', $ExactSourceTarget, '--username', $AuthorizedReadRole,
                '--tenant', $TenantId, '--bundle', $V8Bundle, '--manifest-sha256', $ManifestPin,
                '--file-content-root', $OriginalAssetRoot)
    python -B eng/wp02-02/rma-tenant-archive/reconcile_iam.py @common --output $NewIamOutput
    python -B eng/wp02-02/rma-tenant-archive/reconcile_identity_priority.py @common --output $NewPriorityOutput

## 实库结果

复用 W395 专属可丢弃副本、PostgreSQL 18.6 和原 `w374_live` 物理目标。实际检查 `data_directory` 为 `W395-01/source-clone/data`、原数据库 OID 16386、系统标识 `7706139311168619192`、环回端口 55567 和默认只读。W387 仅提供已保存的原 target JSON。运行器复用 W393 的指定库、指定角色临时环回 HBA 和 PID/路径/端口停止检查。[W395 原包](../W395-01/DELIVERY.md) · [W393 原夹具](../W393-01/DELIVERY.md)

- A 原包 `v8-cut` pin `ef9876aa2ec112952c9793eb77bda0670b3b1200c85784061867ba36ba424bda`。IAM 17 条 `UNCHANGED`，包括原 journal 有引用而当前用户缺失的 actor 事实。Identity Priority 匹配一条 `Published → Published` 原 Outbox，空 Inbox 执行了原生权限查询。
- B 原包 `v8-b` pin `984bab4a2f745eb9d0636df1cb17bf5611a74755cd12507d355f77a3b9e13c57`。IAM 12 条 `UNCHANGED`。Identity Priority 匹配 B 自己的一条 `Published → Published` 原 Outbox，空 Inbox 执行了原生权限查询。四个正常调用均退出 0，各自为 `REPEATABLE READ READ ONLY`、`OBSERVED`、`recoveryReady=false`。
- 基线的两个入口均在连接前拒绝同一有效 A v8 原包，原日志 `test-red-iam.log`、`test-red-identity_priority.log`。将 A 原包复制到本任务私有目录，只改动 v8 特有的 `erp-outbox.csv`，沿用原 manifest pin。两个新入口均在连接前退出 1，返回 `bundle-digest-mismatch`，未发布报告。
- 本次原包的资产根是原空根，Inbox 原键数为零。旧版本的原验证沿用 W393 IAM v6/v7 与 W396 Identity Priority v5/v6/v7。本窗观察 IAM 与 Identity Priority 当前原事实，恢复与切换另行验收。[W396 旧版本实测](../W396-01/DELIVERY.md)

实际运行器、日志和原始报告在 `D:/CP6-local-test/CP6-72H-20261109/W400-01`。`test-native.exit` 为 0，`native/report-digests.json` 记录四份原报告摘要，`native/stopped.json` 记录 postmaster 57256 已停止、55567 无监听、临时 HBA 已移除及副本原 HBA 摘要前后一致。`codex/w400-01` 本地保存，DEV-1 交付时工作树干净，远端采用与席位释放交 COORD。
