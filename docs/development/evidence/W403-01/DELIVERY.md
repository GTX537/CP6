# W403-01 · Main Stock Outbox 同快照切片

DEV-1 于 2026-11-09 13:42:23 UTC 从 #840 `990d38a1f892ebfdc7b8148e60b9628829bc471a` 开工。独立入口导出 `cp6_stock_outbox_v1` 的 13 张表和 `Source` 原外键引用的 `cp6_main_owner_v1.operations`。`capture` 和 `rows_sql` 可供持有同一 PostgreSQL 快照的后续调用方复用。[导出入口](../../../../eng/wp02-02/stock-outbox-recovery/stock_outbox.py) · [Main 原迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261023003000_MainStockOutboxV1.cs) · [Stock V2 原迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261026001000_MainStockCreationOutboxV2.cs)

导出核对精确物理目标、数据库 Owner 持有的原表和迁移、原生键、原 `Source` 外键，以及 `Authority.TenantId` 与固定 Main route root。按实际租户、root 和 native ID 保留 native MessageId、派送与 claim、mutation、send、audit、seal、DLQ。Stock 的全局 MessageId 使用原 `varchar` 和数据库默认 collation。原操作按 `Main.Wms.StockMovement`、租户、操作 UUID 取 `Main.Wms.StockOperation.v1/v2` 关联原件。输入、结果、报文字节和原文本回执保留原生表示。`source_xmin` 记录源物理观测，票据里的原 token 仍保留原文本。

    $tool = 'eng/wp02-02/stock-outbox-recovery/stock_outbox.py'
    python -B $tool export --pg-bin $PgBin --target-file $ExactSourceTarget --username $ArchiveReader `
      --tenant $TenantId --output $NewPrivateOutput
    python -B $tool verify --bundle $Bundle --tenant $TenantId --manifest-sha256 $ManifestPin

每个租户各用一个 `REPEATABLE READ READ ONLY` 快照。所有成员导入同一快照，完成清单在 COPY 成功后发布。Published 核原票据与完成回执，实际存在的 send attempt 核原票据，已完成 run 核其票据的原处理回执。`verify` 按外部 pin 核清单、成员摘要、列、租户范围及原计数。导出为 `recoveryReady=false`，Main 路由为固定 Active epoch 1。[Main Host 授权](../../../../CP6.Core/Services/Integration/StockOutbox/StockOutboxHostV1.cs)

## 实库验证

只启动 W403 专属 W352 合成 PGDATA 副本，PostgreSQL 18.6、环回 55560、`tenant_management_component` OID 16385、系统标识 `7706073187213080876`。原 W352 复制前后和实测前后均为 status 3，原 HBA 与 `pg_control` SHA-256 相同。副本用指定库和指定合成角色的临时 HBA，默认只读。仅在副本的受控夹具事务建立少 claim-ticket 权限的合成 LOGIN。[实际夹具](../../../../eng/wp02-02/stock-outbox-recovery/verify_stock_outbox.py)

- A 原包 manifest SHA-256 `376a0f9fb2a82d7d3ee670516b0f817c14bdabff68e23ea8d9bee8b458b1f56d`。A 原消息 `Published`，原 epoch 1，run、claim、ticket、send、mutation 各一条，audit 和 token seal 各两条，DLQ 是只有表头的成员。完成回执 `1ea41b2a-ef92-443a-9fad-c0f0ca43d9ce` 的 Input、Output、原票据及 send 的 OriginalTicket 逐字节等于独立 PostgreSQL 读取。
- B 原包 manifest SHA-256 `bf9b44d1badc8c3d07d63172fedc5c31ae966c46768f2544680b0193472a101b`。A/B 原操作 UUID 同为 `67742a77-6354-4758-b9f6-16b268894c8e`。B 使用独立原消息、root 和 `COMMITTED` 记录，消息为 `Pending`，派送 companion 与 DLQ 为表头成员。两租户各有 14 个数据成员，实际正常 `verify` 均通过。两份原操作 input/result、native payload 与观测 `xmin` 均等于各自独立数据库读取。当前两个样本的原操作为 v1。
- 受限角色对 native 表有 SELECT，缺 `Cp6_OutboxClaimTicketV2` 的 SELECT 时 PostgreSQL 返回 42501。使用同一服务器但错误数据库 OID 时，目标核验拒绝。两例均未发布清单。复制 A 包后仅改变原票据 CSV，保留原 pin，`verify` 拒绝成员摘要。以 B 租户验证同一原包时，`verify` 拒绝租户范围。
- W352 旧物理备份 `1bccbe4392a3e4688841b02392c890cd0fe223b0b22632a713ba0ce4f10d9d86` 的原 TOC 含全部 13 表和原日志，既有证据中备份时 A 为 Pending。此次导出来自其保留源库的后续 A Published、B Pending 状态。#811 候选禁止重派及 W386 Platform epoch 推进沿用各自的原结果。[#811 候选隔离](../BUG-808-STOCK-RESTORE-OUTBOX/DELIVERY.md)

本窗原件在 `D:/CP6-local-test/CP6-72H-20261109/W403-01`。`test-red.log` 记录实现前缺少独立入口。`test-copy.exit` 和两次 native 运行均为 0。首轮证据留在 `native-01`，补紧已完成 run 的原回执关联后，等待主机内存门槛再复用同一副本。最终 `native-02/checks` 保留原包、原生读取和失败原件。`native-02/stopped.json` 记录 postmaster 28256 于 13:59:04 UTC 停止、55560 无监听、临时 HBA 已移除、源和副本原 HBA 摘要一致。统一 RMA 包采用、落表、当前对账与正式 PGT-021/022 验收各属后续窗口。[S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

分支 `codex/w403-01` 本地保存，DEV-1 交付时工作树干净，远端采用和席位释放交 COORD。
