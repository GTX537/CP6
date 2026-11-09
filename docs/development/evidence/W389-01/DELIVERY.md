# W389-01 · FileContent 原外部资产引用

DEV-1 于 2026-11-09 11:58:24 UTC 开工，登记基线 #830 `ae875a6c72f44121acdc7eff95ffed6713fa5315`。W387 经 #831 正常采用后，公共归档入口在 `997c4ae0d1b6b9a2b1ba5bab026b20bfd3891764` 串行接入。[W387 v6](../W387-01/DELIVERY.md) · [S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

## 操作入口

在 v6 的五个显式开关后加 `--file-content-root <专属绝对路径>` 生成 v7。工具从同一个 PostgreSQL 只读切面导出一个 `file-content-assets.csv`，按原 UUID 排序，空切面保留表头和零计数。Native 表和 `ready()` 的 owner 须匹配原物理库，按真实 FileContent 迁移和原不可变触发器准入。COPY 使用 `row_security=off`。工具按原租户和文件键读取原 `contents`、`heads` 和当前 `events`，同时查验 `working_versions` 中原工程父版本及摘要。[归档入口](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [资产投影](../../../../eng/wp02-02/rma-tenant-archive/file_content_assets.py) · [原 PostgreSQL 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261029030000_FileContentV1.cs)

清单绑定 `Main.File.Content.v1` 的内容版本 `1`、SHA-256、原文件名、媒体类型、长度、工程父版本和当前状态版本、ETag、事件摘要。文件路径仅接受 `native-content-v1/<tenant:N>/<guid:N>`。工具在指定的私有本地根下检查普通文件，拒绝链接与重解析路径，按原长度读取并核实原 SHA-256。磁盘原件仍是独立的外部引用，包内保存相对 key 和资产元数据。完整 v7 `verify` 要求提供本地根并重查实际字节。[原 FileContent Owner](../../../../CP6.Core/Services/Pub/FileContentOwnerV1.cs) · [LocalFileStore](../../../../CP6.Core/Services/Pub/LocalFileStore.cs)

    python -B eng/wp02-02/rma-tenant-archive/archive.py export `
      --pg-bin $PgBin --target-file $ExactSourceTarget --username $BackupOperator `
      --installation $InstallationId --tenant $TenantId --output $NewPrivateOutput `
      --include-original-journal --include-rma-source --include-rma-visibility `
      --include-identity-priority --include-rma-iam-references --file-content-root $PrivateLocalRoot
    python -B eng/wp02-02/rma-tenant-archive/archive.py verify `
      --bundle $Bundle --tenant $TenantId --file-content-root $PrivateLocalRoot

## 实库

`D:/CP6-local-test/CP6-72H-20261109/W389-01/test-native-01.log` 和 `.exit` 记录夹具退出 0。唯一 PostgreSQL 18.6 专属实例的原件在 `file-content-assets-20261109T120815-453ce71b`。复用 W351 `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 真实发布 DatabaseInit，PID 13572 退出 0，W353/W361 使用已固定 CLI。原制品摘要在 `inputs.json`。本窗 FileContent 行和磁盘字节是合成夹具，通过真实 PostgreSQL 原约束和触发器；W383 已实际执行原 Owner 上传、LocalFileStore 和独立候选下载。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_file_content_assets.py) · [W383 原证据](D:/CP6-local-test/CP6-72H-20261109/W383-VERIFY/DELIVERY.md)

- A/B 使用相同文件和父记录 UUID、相同相对叶键，各自持有自己的租户前缀及原 SHA-256。A 的工程父记录已推进到版本 2，FileContent 继续准确引用原版本 1。A/B 的切面外写入真实提交。A head 由 backend 39048 推进到状态版本 2，COPY backend 22112 导入快照 `0000000B-00000015-1`，A 切面仍保留状态版本 1 及原 ETag/事件摘要。常规 CLI 的后续 v7 看到版本 2，外部原件字节不变。
- A 成功导出时 B 的对应物理文件不存在。A 包仅有 A 的一个引用，未收录 A 的未引用 staging 对象。B 原件补齐后实际 v7 输出其两条引用。另有真实 v7 空清单、v6 正常 export/verify，以及六个既有 v1–v6 实物经当前 CLI 验证。原结果在 `legacy-verify.json`。
- 实际导出拒绝缺文件、长度错误、同长度不同 SHA-256、有效键经过 Windows junction、存储键含越界段，以及原工程父摘要不匹配。上述导出均未生成完成 `manifest.json`。实际 v7 verify 拒绝缺根和原件被改写。
- 切面 v7 manifest SHA-256 `74e75ace3a96d3f053c2c0cfc16d44173c281d32447b2bcf4305221dda9d3681`，资产成员 SHA-256 `1ede37edff57a5bac9a0028024ba60fd22ec707a3a9b0ba24950d66f6ce1c0cf`，后续当前资产成员 SHA-256 `30d675f059b773f503f5f388ad1ccc697c248b34186fd8fc4e499fad8d1252e6`。`test-red.log` 保留实现前模块缺失。Postmaster 49920 已停止，55566 无监听。

## 范围

本窗覆盖指定本地根的原 FileContent 依赖验证。FileContent 事件历史和原 journal、FormFiles、其他附件、对象存储、签名与密钥、staging 和生产恢复仍有各自的边界。v7 记录 `recoveryReady=false`，正式 PGT-021/022 保持 `NOT_RUN`。[正式验收卡](../../acceptance/postgresql-tenancy.json)

本地候选在 `codex/w389-01`，集中审查、正常合并和 DEV-1 官方 finish 交 COORD。
