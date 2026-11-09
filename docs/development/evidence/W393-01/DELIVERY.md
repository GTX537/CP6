# W393-01 · RMA 当前 IAM 原引用对账

DEV-1 于 2026-11-09 12:16:04 UTC 开工，基线 #832 `ddd7206b0bf8c6b8a82e2526acf084195b026ab5`。[W387 v6](../W387-01/DELIVERY.md) · [W389 v7](../W389-01/DELIVERY.md) · [S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

## 操作入口

`reconcile_iam.py` 接受确切 v6/v7 归档、独立提供的 manifest SHA-256、TenantId 和原物理源。它复用既有制品验证并消耗已核摘要的私有 IAM 成员，v7 同时核实际外部原件。地址、端口、库名、OID、系统标识、installation 和租户 Ready 均使用原归档的实际边界。操作员需原归档的控制和源读取权限。[操作入口](../../../../eng/wp02-02/rma-tenant-archive/reconcile_iam.py) · [原归档信任边界](../../../../eng/wp02-02/rma-tenant-archive/archive.py)

    python -B eng/wp02-02/rma-tenant-archive/reconcile_iam.py `
      --pg-bin $PgBin --target-file $ExactSourceTarget --username $BackupOperator `
      --tenant $TenantId --bundle $OriginalBundle --manifest-sha256 $PinnedDigest `
      --output $NewPrivateOutput

v7 调用加 `--file-content-root $OriginalPrivateLocalRoot`。当前图复用真实 RMA heads、原 Owner journal 与现行出库可见性投影。原 journal 安装和原生 owner 先经核验，再把当前 IAM 图以 native COPY 导入同一 `REPEATABLE READ READ ONLY` 快照，保持原 `row_security=off` 读取边界。[原 IAM 投影](../../../../eng/wp02-02/rma-tenant-archive/iam_v6.py)

按原 `(kind,id)` 顺序流式比较，`CHANGED` 和 `CURRENT_ONLY` 保存当前事实，`UNCHANGED` 保存相同的两侧原值。只在原图出现时记 `UNKNOWN`，可能因引用离开了当前 RMA 图。仍被 journal 引用但缺失的用户保留 `user_present=false`。工具保留原事实、当前事实和修订，不据此计算有效权限。完整报告 `iam-reconciliation.jsonl` 原子发布，`recoveryReady=false`。

## 实库

证据在 `D:/CP6-local-test/CP6-72H-20261109/W393-01`。`test-native-v6.log` 和 `test-native-v7.log` 均记录退出 0。顺序复用 W387 与 W389 已停止的 PostgreSQL 18.6 合成源，沿用各自真实 W351/W353/W361 初始化与 commissioning。`test-red.log` 保留实现前缺模块。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_iam_reconcile.py)

- W387 A 原 pin `280ccebb949df15bea0b5182a441e470681afab458d63cde759e81d15a88e641`。当前 holder 54812 和 COPY 22284 使用 `00000003-00000002-1`，输出 14 条不变、3 条变化。原 `ReturnReason` access 1、当前 3、角色 72 修订变化，以及租户到期时间与认证策略修订变化均实际保留。原 CLOSED actor 仍由 journal 引用，当前 `user_present=false` 且用户名为空。A 报告 SHA-256 `062885a20f6c9690dfa79ec1523981f33c9dfa0549e409482006113a95cfb7c4`。
- W387 B 原 pin `0adf47cc128ce1a2f3381d9a5b8a5ec15dcb6c00c90a5fa0a346f8264763307c`。holder 58556 和 COPY 4576 使用 `00000005-00000002-1`，12 条均不变。B 同号角色 72 保留其原修订和 access 2，A 报告无 B 用户或租户事实。A/B 两侧独立原生图对账前后逐字节相等，摘要在 `native-v6/checks/evidence-summary.json`。`UNKNOWN` 和 `CURRENT_ONLY` 用本次真实事实组成单边输入验证合并行为，实库图保持原状态。
- 真实 CLI 拒绝错误摘要、调用方目标不匹配和重新 pin 但原生 OID 错误。W389 原 v7 pin `74e75ace3a96d3f053c2c0cfc16d44173c281d32447b2bcf4305221dda9d3681` 与原私有 FileContent 字节实际核对成功。当前 holder 3500 和 COPY 32052 使用 `00000001-00000002-1`，三条 IAM 引用均不变。报告 SHA-256 `dd164995c83ee4af535d21e4b76188255508feb2a2a909587bbf6f170db2ee8c`。省略资产根实际拒绝，未发布报告。
- 两个保留夹具的原登录值均为运行期生成。复测经批准对各自确切数据库和原夹具 owner 使用单行回环 HBA 副本，原 HBA 的字节长度和 SHA-256 前后一致。只读默认和实际角色有独立原生证据。Postmaster 39732 与 57560 均已停止，55567 与 55566 无监听，临时副本均已移除。详情见各自 `stopped.json`。

本窗范围是原源当前事实和历史引用的差量。IAM 写入、有效授权、路由、恢复采用和生产切换仍由各自权威流程处理。PGT-021/022 保持 `NOT_RUN`。本地分支 `codex/w393-01`，集中审查、合并及 DEV-1 官方 finish 交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
