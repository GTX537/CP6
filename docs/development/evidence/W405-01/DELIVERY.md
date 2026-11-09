# W405-01 · RMA 与 Main Stock 同快照归档

DEV-1 于 2026-11-09 14:08:16 UTC 从 #842 `3cc67ba67059add4fd93bb60007ebe08468072a0` 开工。`archive.py export` 在现有 v9 参数外接收 `--include-main-stock-outbox`，生成 `cp6-rma-source-archive-v10`。原 RMA、共享来源、IAM、ERP、资产引用及隐私历史与 14 个 `stock-outbox-` 前缀成员使用同一个 PostgreSQL holder。Stock 的原目标标记、编码、目录和 root 校验在同一事务中完成。各原成员使用同一 exported snapshot，完成清单保留原成员摘要和精确行数。[归档入口](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [Stock 原切片](../W403-01/DELIVERY.md) · [v9 原切片](../W402-01/DELIVERY.md)

    python -B eng/wp02-02/rma-tenant-archive/archive.py export `
      --pg-bin $PgBin --target-file $ExactSourceTarget --username $ArchiveReader `
      --installation $InstallationId --tenant $TenantId --output $NewPrivateOutput `
      --include-original-journal --include-rma-source --include-rma-visibility `
      --include-identity-priority --include-rma-iam-references --file-content-root $OriginalAssetRoot `
      --include-erp-integration --include-privacy-evidence --include-main-stock-outbox
    python -B eng/wp02-02/rma-tenant-archive/archive.py verify --bundle $Bundle `
      --tenant $TenantId --file-content-root $OriginalAssetRoot

`verify` 核完整清单、成员摘要以及原 Stock 目录、行数和租户键范围。兼容 W403 独立切片和原 v9 校验。v10 记录 Main Stock 固定 Active `CurrentEpoch=1`，`recoveryReady=false`，现行 `stage.py` 的格式门仍负责拒绝未采用的版本。[定向实库断言](../../../../eng/wp02-02/rma-tenant-archive/verify_stock_archive.py) · [阶段入口](../../../../eng/wp02-02/rma-tenant-archive/stage.py)

## 真实源与派送守卫

仅复制并启动 W402 停机合成源的任务专属 PGDATA。原 W402 与 W387 的 HBA 和控制文件均按前后摘要核对，源始终为 status 3。实库 PostgreSQL 18.6，`127.0.0.1:55567/w374_live`，OID 16386，系统标识 `7706139311168619192`。沿用 #811 已封存 Core、Tests、Platform 及依赖，以合成 actor 和真实授权数据调用 `StockMovementService.ApplyCommandAsync`。只编译私有驱动，受控运行使用 workstation GC 512 MiB 和 PostgreSQL 32 MiB shared buffers。[#811 实库原件](../BUG-808-STOCK-RESTORE-OUTBOX/DELIVERY.md) · [Stock 原 Owner](../../../../CP6.Core/Services/Wms/StockMovementService.OriginalOperations.cs)

首轮无 Stock root 的 v10 真实拒绝。随后 A/B 的原操作 `e5613f4e-5b81-4fe2-821b-f45ee016f84e` 在不同原生事务 1314、1324 实际提交，两次原 Owner 读回成功。真实 A worker 随后在 Platform guard 校验返回 `CP6_OUTBOX_GUARD_MIGRATION_REQUIRED` 并回滚，合成发送器无调用。W351 发布迁移 DLL 中的 guard body 为 LF，#811 实测迁移和实际 Platform 包预期为 CRLF。两个迁移体归一化后的 SHA-256 一致。保留原 guard 和真实 Pending 记录。首轮 postmaster 49992 已正常停止，驱动峰值工作集 189.5 MiB，系统物理可用内存最低 690.7 MiB。W403 独立切片的 Published、原票据和原完成回执另有既存实证。[原生守卫问题 #844](https://github.com/GTX537/CP6/issues/844) · [W403 Published 原件](../W403-01/DELIVERY.md)

## 实际同切面与阶段门

- 在同一任务副本接续原 A/B 操作，两边 `COMMITTED` 且同操作 UUID，Stock 状态均为 `Pending`，原消息键和 root 各自独立。实测守卫为 1,389 字节、0 个 CR、SHA-256 `4e2458c394e2996b4333203da9e9f604dac81d7fba80cd1b291414cd6aacc194`，触发器配置原值与 W351 LF 正常匹配，晚写前后一致。切面中的 Stock 原日志来自 `Source` 的那一条原记录，派送 companion 和 DLQ 为表头成员。
- holder 52000 导出快照 `0000000B-00000006-1`。holder 打开时，Stock backend 13140 实际提交 A 晚到操作 `5f00ba57-7e71-4e68-83a3-0199b83169da`，隐私 backend 50660 独立提交一条合成审计。切点后 A 库存从 `11.24691356` 到 `12.37037034`，B 仍为 `11.24691356`，B 原消息与账保持一致。两次晚写只在当前库观察到。v10 的 35 份 CSV 与切点前正常 CLI 导出逐字节一致，原 RMA dump 经原生 `pg_restore` 独立取出的逻辑行一致。服务日志实际记录同一个快照的 36 次导入。
- 最终 v10 manifest SHA-256 `7ac25b3c3eae626277c03802e29203e7cf1f7912664e05f92aeaba817453b6e9`。正常 `verify` 通过。实际缺 Stock 成员、改变票据成员、改为其他租户后重算成员摘要三例均拒绝。原 W402 v9 pin 与 W403 独立原包复验通过。
- 原 `test-native-03.exit` 为 1，原因是末尾夹具误期待阶段格式先拒绝。权威 #843 `bccadb753e34c87991640260ffa05547eab0a701` 的原 `stage.py` 对完整 v10 实际返回 `rma-stage-bundle-member-rejected`。仅私有探针改成 v9 文件名集合并重固定 v10 header 时，阶段返回 `rma-stage-format-rejected`。两例均无 native driver。后续针对原 cut、原服务日志和两个真实阶段门的离线核对 `test-offline-final.exit` 为 0。

原件在 `D:/CP6-local-test/CP6-72H-20261109/W405-01`。`native-01` 留真实 worker 拒绝，`native-02` 留 Windows 出站端口冲突的启动前失败。等待端口自然释放及连续资源样本后，`native-03` 完成原 cut。实际启动样本 891.7 和 892.2 MiB，实际晚到 Owner 前 838.7 和 844.6 MiB，晚到驱动峰值工作集 185.8 MiB，系统可用物理最低 680.4 MiB。postmaster 24620 于 14:41:51 UTC 完成停止核验，55567 无监听，临时 HBA 移除，W402/W387 原控制文件和 HBA 摘要一致。最终原件及两处阶段检查摘要在 `offline-final/result.json`。

本窗真实业务范围为合成数据上的直接 Stock Owner 调用和同库历史切面，样本原操作为 v1。v10 保持 `recoveryReady=false`。`codex/w405-01` 本地保存，DEV-1 交付时工作树干净，远端采用与席位释放交 COORD。
