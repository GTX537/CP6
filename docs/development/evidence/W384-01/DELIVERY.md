# W384-01 · Identity Priority 当前原键对账

DEV-1 于 2026-11-09 11:30:40 UTC 开工，基线 #827 `b21d364e2eec480847521be0a3a5e8ad61406f38`。[W380 v5](../W380-01/DELIVERY.md) · [S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

## 操作入口

`reconcile_identity_priority.py` 接受 v5 归档、独立提供的 manifest SHA-256、TenantId、原物理库 target 文件和私有新输出路径。它校验完整制品，只暂存实际消费的 Outbox 和 Inbox 两个已核摘要的成员。目标须匹配归档中的回环地址、端口、库名、OID、系统标识和当前原 installation/Ready；工具沿用原归档控制读权限。认证沿用归档工具的 libpq 约定。[操作入口](../../../../eng/wp02-02/rma-tenant-archive/reconcile_identity_priority.py) · [物理与授权检查](../../../../eng/wp02-02/rma-tenant-archive/archive.py)

    python -B eng/wp02-02/rma-tenant-archive/reconcile_identity_priority.py `
      --pg-bin $PgBin --target-file $ExactSourceTarget --username $BackupOperator `
      --tenant $TenantId --bundle $V5Bundle --manifest-sha256 $PinnedDigest --output $NewPrivateOutput

同一个 PostgreSQL `REPEATABLE READ READ ONLY` 事务用最多 128 个键的有界批次查询当前行。键通过 JSON 安全传输，使用实际 `bpchar`/ICU 唯一域查 Outbox `MessageId` 和 Inbox `(ConsumerName, MessageId)`，包含受 RLS 保护的全局冲突判定。每个同租户匹配须保有原行 Id、原键字节、消息绑定及摘要；Outbox 同时核归档和当前原 payload 的实际摘要。变更的租约只报告是否替换、是否活跃及到期时间。跨租户只返回 `GLOBAL_KEY_CONFLICT`，同租户绑定不符返回 `BINDING_CONFLICT`，原键缺失保持 `UNKNOWN`。当前记录值保留在原表。[原 PostgreSQL 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/IdentityPriority/20261002151342_PostgreSqlIdentityPriorityBaselineV1.cs)

完整结果原子发布 `reconciliation.jsonl`。`OBSERVED` 退出 0。有冲突或 `UNKNOWN` 的 `BLOCKED` 退出 2，仍保留完整观察。输入或原生读取拒绝退出 1，完成报告只在整个只读事务结束后产生。所有完成报告均为 `recoveryReady=false`。

## 实库

W351 源 `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 的真实发布 DatabaseInit，以及 W353/W361 的现成 CLI，建立隔离全 Core PostgreSQL 18.6 合成数据。主要证据在 `D:/CP6-local-test/CP6-72H-20261109/W384-01/identity-reconcile-20261109T114146-bce90bb2`。DatabaseInit PID 28200 退出 0，制品摘要保留于 `inputs.json`。v5 manifest SHA-256 为 `e199721d0763698306c7963f48ba76c4417c12f7862a061ec6d72c22dc1eab48`。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_identity_reconcile.py)

- 正常 v5 导出后，真实对账 CLI 先返回 `OBSERVED` 及 139 条保留，再对 135 个 Outbox 键和 4 个 Inbox 键返回 132 条 `PRESERVE_CURRENT`、5 条 `BLOCKED`、2 条 `UNKNOWN`。两个只读 backend 分别为 60504 和 56880。实际覆盖 Pending→Published、Processing→Processed、轮换中的活跃租约、payload 字节变更而旧摘要未变、绑定与 Inbox 摘要变更、原键缺失和 B 占用两个全局键。Outbox 的 B 行实际使用大小写与尾空格变体。原 A 引号、反斜杠和 Unicode 键在数据库默认 `standard_conforming_strings=off` 时也匹配，工具在只读会话设置正确字符串规则。
- 输出仅含原 A 键与安全的当前状态或冲突分类。测试未从 B 披露租户、绑定、payload 或状态，也未披露租约 token/owner。读前后的 A/B 四表独立 native COPY 逐字节一致，私有 `native-state-hashes.json` 留有摘要。
- 实际拒绝错误摘要、归档和目标不一致，以及对外声明和 pin 一致但数据库原生 OID 不符。局部夹具在真实 Priority Outbox 启用 FORCE RLS，低权操作员的普通读取为 0。`test-native-03.log` 保留原生 42501 和 Windows 开放 CSV 文件掩盖主错误。最终修复只调整失败时的文件关闭顺序，沿用原数据和 v5 制品定向复测。`run03-rls-cleanup-recheck/result.json` 记录 harness 通过，原生 `42501`、CLI 退出 1 并保留主错误，输出只余两份 holder 日志。
- `test-red.log` 记录缺实现。`test-native-01.log` 记录不合合同的测试 token 被 23514 拒绝，`test-native-02.log` 记录夹具初次 CONNECT 拒绝。最终定向复测仅用临时 HBA 副本对相同受限角色开放指定库的回环认证，原文件 SHA-256 一致。主夹具 postmaster 18736、复测 postmaster 8496 均已停止，55566 无监听，临时副本已移除。

可用同一批已发布输入重建实库夹具。

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B eng/wp02-02/rma-tenant-archive/verify_identity_reconcile.py `
      --root "$b/W384-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 `
      --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' `
      --cli "$b/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" `
      --journal-cli "$b/W361-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll" `
      --webapi "$b/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll" `
      --webapi-source c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40

## 范围

本窗覆盖当前主库中 Identity Priority v1 的两个原全局键。发送、重放、恢复导入、当前路由/水位、checkpoint、其他队列域和生产切换有各自的权威边界。正式 PGT-021/022 仍为 `NOT_RUN`。[正式验收卡](../../acceptance/postgresql-tenancy.json)

本地候选在 `codex/w384-01`，由 COORD 集中审查、合并及 DEV-1 官方 finish。
