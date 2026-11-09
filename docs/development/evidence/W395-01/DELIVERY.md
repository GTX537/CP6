# W395-01 · Main ERP 八表同快照归档

DEV-1 于 2026-11-09 12:32:06 UTC 开工，基线 #834 `c979747d45aeaf9e445d4337f6508920e13a0f4d`。[ERP 原对象与键域](../WP02-02/erp-tenant-isolation/OBJECTS.md) · [W389 v7 资产](../W389-01/DELIVERY.md) · [S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

## v8 入口

在 v7 选项外加 `--include-erp-integration`。v8 保留显式私有资产根要求。原 PostgreSQL `erp_integration` 八表在同一 `REPEATABLE READ READ ONLY` 快照下按精确租户复制，按原主键排序。每表明确列出原列、原唯一键及键域和行计数。空表仍有带表头的成员及摘要。原 `__EFMigrationsHistory_ErpIntegration`、表/命名空间 owner、列类型和原唯一键在同一 holder 核对，`row_security=off` 用于原计数和各 COPY。[v8 投影](../../../../eng/wp02-02/rma-tenant-archive/erp_integration.py) · [原 ERP 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/ErpIntegration/20261002151345_PostgreSqlErpIntegrationBaselineV1.cs)

`CommandInbox.MessageId` 保留 `pg_catalog.C` 和全局主键。Outbox 全局 MessageId 使用 `public.cp6_ci_as_provider_v1`。两者按原 `bpchar` 比较尾空格。`OrderBridgeDispatch.OrderKey` 仍按原 `C` 比较，其键域含 TenantId。原 payload、`ResultDataJson` 文本、共享消息和 bridge 租约、原 RowVersion、重放审计的实际输入 token 均按 PostgreSQL 原值保存。字节字段采用原生十六进制，时间为 UTC。[归档入口](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [原八表模型](../../../../CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs)

    python -B eng/wp02-02/rma-tenant-archive/archive.py export `
      --pg-bin $PgBin --target-file $ExactSourceTarget --username $BackupOperator `
      --installation $InstallationId --tenant $TenantId --output $NewPrivateOutput `
      --include-original-journal --include-rma-source --include-rma-visibility `
      --include-identity-priority --include-rma-iam-references --file-content-root $PrivateLocalRoot `
      --include-erp-integration
    python -B eng/wp02-02/rma-tenant-archive/archive.py verify `
      --bundle $Bundle --tenant $TenantId --file-content-root $PrivateLocalRoot

## 实库

`D:/CP6-local-test/CP6-72H-20261109/W395-01/test-native-01.log` 与 `.exit` 记录退出 0。一次从已停止 W387 原合成夹具复制的 PostgreSQL 18.6 副本，使用原安装、物理标识和环回端口 55567。`source-clone/clone-receipt.json` 记录原库复制前后 status 3、原 `pg_control` 和 HBA 摘要。任何合成写前，夹具核实服务器 `data_directory` 等于专用副本。本窗沿用 W387 的真实 W351/W353/W361 制品，免重新发布。验收原件在 `native-v8/checks`。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_erp_archive.py) · [W387 原合成源](../W387-01/DELIVERY.md)

- A/B 在真实 `CommandInbox` 原 C 键写入大小写不同的原键。B 试图占用 A 的同大小写加尾空格键，原 PK 返回 23505。真实 Outbox 的 B 大小写变体加尾空格也因原 ICU 全局键返回 23505。A/B 同 `AggregateId`、`RequestId`、OrderKey 和 replay OperationId 按各自原租户键成功。
- 快照 `0000000D-00000002-1` 后，A writer 55932 和 B writer 14112 实际提交。八个 ERP COPY 进程导入同一快照。A 的八份成员与切面前独立 native COPY 逐字节一致，切面后八份原生输出均已变化。原 NUL/高字节 payload、尾空格文本、lease、receipt 和原审计 token 留在私有证据。A 完成清单 SHA-256 `ef9876aa2ec112952c9793eb77bda0670b3b1200c85784061867ba36ba424bda`。
- B 实际正常 CLI v8 export/verify 通过，`Cp6_DeadLetterRecord` 与 `DeliveryReplayAudit` 为必需的空成员。B 完成清单 SHA-256 `984bab4a2f745eb9d0636df1cb17bf5611a74755cd12507d355f77a3b9e13c57`。v8 夹具明确用零资产的私有根，原 W389 含实际外部字节的 v7 由当前 CLI 实际复验。
- 副本内仅在原 `CommandInbox` 启用局部 FORCE RLS。低权读取原生计数为 0，v8 正常导出在 ERP 读处收到原生 42501，无完成清单。该故障属于合成夹具的原生 RLS 边界。`test-red.log` 记录实现前缺模块。
- `v5-compatibility.log` 按 #834 源逐项比对 v5 目录、计数、COPY SQL/设置及清单投影完全相同。`final-cli-verify.log` 记录最终 CLI 核原 v5 与本窗 v8。实库完成后将 v8 的排除项文字细化为其他审计，已实测成员、校验和读路径相同。
- 专属 postmaster 50780 已停止，55567 无监听，临时环回 HBA 副本已移除。原源和验证副本原 HBA 摘要保持，原件在 `native-v8/stopped.json`。`source-clone/data` 是专属可丢弃 PGDATA，留待 COORD 核查。
- 集中复查后，Outbox 全局键在目录检查中同时匹配原迁移的 ICU provider、编码、`und-u-ks-level2` 和空自定义规则。专属副本在 55564 定向复测，原定义通过。将实际 Outbox 列和索引改绑同名 `und-u-ks-level3` 后，原校验仍通过，大小写比较已从相等变为不等，修订校验明确拒绝 `erp-integration-key-collation-rejected`。`test-native-icu-review.log` 退出 0，拒绝原件 `native-icu-review/altered-after-review.json` SHA-256 `80547325ac1359d597917c01a09e430b33ec030d932fdc1558d08d42c312a187`。postmaster 45224 已停止，55564 无监听，临时 HBA 已移除。专属副本保留本次故障定义。[原 ICU 定义](../../../../CP6.Core/Persistence/PostgreSqlMigrationPrerequisitesV1.cs)

本窗覆盖 ERP 八表只读切面。其他队列域、序列水位、外部业务效果、当前对账、staging、重放和生产采用仍由独立流程处理。v8 为 `recoveryReady=false`，PGT-021/022 保持 `NOT_RUN`。本地分支 `codex/w395-01`，集中审查、合并及 DEV-1 官方 finish 交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
