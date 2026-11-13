# 同库恢复已采用的 RMA v11 单元

`live_recovery.py` 用于原 PostgreSQL 物理库中仍已佣用、迁移为 Ready 的租户。先在关闭的隔离候选完成 v11 Core、journal、FileContent、ERP C03 和 Stock effects 导入，再在源库恢复丢失的 RMA heads/versions。当前共享业务原件、权限、凭据、队列和全局定义保留在源库；不需要改变租户到数据库的映射。

这是受控恢复身份的维护入口。调用者使用源库和候选库已有的 owner 身份，按已授权窗口操作。工具核对物理身份、原 commissioning、精确 manifest/effects pin 和五份导入回执。普通应用继续使用原有租户登录及权限入口。

```powershell
python eng/wp02-02/rma-tenant-archive/live_recovery.py apply `
  --pg-bin $PostgreSqlBin --username $Owner `
  --target-file $SourceTarget --commission-receipt $SourceCommission `
  --candidate-target-file $CandidateTarget --candidate-commission-receipt $CandidateCommission `
  --candidate-id $CandidateId --tenant $TenantId --operation $OperationId `
  --bundle $Bundle --manifest-sha256 $ManifestSha256 `
  --file-content-root $FileContentRoot --effects $StockEffects --effects-sha256 $EffectsSha256 `
  --output $NewOutputDirectory
```

所有目标文件沿用 `PhysicalTarget` 原格式；输出目录必须新建。数据库凭据沿用 PostgreSQL 客户端配置，不写进命令、目标 JSON 或报告。工具在源事务中取得现有认证门、该租户迁移行和业务表锁，核对共享依赖，插入缺失业务行和原操作回执后一起提交。

成功结果 `RESTORED`、`recoveryReady=true` 只说明该源库逻辑单元完成本次恢复。原隔离候选导入回执仍为 `recoveryReady=false`，不因此开放候选。恢复入口不会发送消息；已发送、当前 lease 等状态按既有对账协议保留，UNKNOWN、全局键冲突或不适用 epoch 会拒绝。

提交答复不明时，保持原租户和 OperationId，换一个新输出目录执行：

```powershell
python eng/wp02-02/rma-tenant-archive/live_recovery.py recover `
  --pg-bin $PostgreSqlBin --username $Owner --target-file $SourceTarget `
  --tenant $TenantId --operation $OperationId --output $RecoveryOutputDirectory
```

`recover` 读取同事务保存的原结果字节，不重复写业务。`apply` 同键同输入返回原回执；同键不同绑定拒绝。缺共享原件/外部资产、源已有不同或更新业务行、schema/迁移/佣用不匹配均关闭恢复，不能用旧包覆盖当前数据。完整 schema 毁损、跨库迁移、其他业务模型恢复仍按各自任务处理。

实际 PostgreSQL、普通 HTTP、B 并发、故障回滚及丢答验证见 [W526-01 交付报告](../../../docs/development/evidence/W526-01/DELIVERY.md)。

## Stock 路由纪元

`reconcile_stock_outbox.py` 的报告头包含备份与现行水位、路由纪元和控制版本。同源旧纪元的归档会得到 `BLOCKED/ROUTE_EPOCH_CHANGED`，已确认发布的原消息仍可按原键对账。发送尝试已有原记录但缺少完成回执时，报告为 `UNKNOWN/SEND_OUTCOME_UNKNOWN`。这两种情况都使同库恢复保持未就绪。

需要推进同一发布者的纪元时，由数据库 Owner 在已授权窗口运行 `CP6.TenantStorage stock-outbox-epoch-v1 --stdin`。在迁移 `20261113073000_MainStockOutboxTerminalEpochV1` 生效后，提交两次独立的 JSON 请求。

1. 使用新 `OperationId`，置 `DrainId` 为 null，并提供当前的 `ExpectedEpoch` 与 `ExpectedControlVersion`。已终结的队列可以进入 Draining，未终结的消息、未关闭的运行或未决发送会拒绝请求。Draining 期间新 Stock 消息无法入队，相关业务事务会回滚。
2. 使用另一 `OperationId`，将 `DrainId` 设为第一步操作键。用第一步原回执 `Result` 中的 `Epoch` 和 `ControlVersion` 发起推进。成功后路由回到 Active，旧消息保留原纪元，新消息采用新纪元。

两个请求都需要 `Target`、`TenantId`、`AdminUsername` 和 `AdminPassword`。`Target` 使用上文的物理身份格式。远程连接用默认 `SslMode=VerifyFull`，隔离本机夹具可显式使用 `Disable`。凭据通过重定向标准输入传入。同键同输入返回原回执。提交答复不明时可用原输入加 `RecoverOnly: true` 查询，退出码 4 表示原回执仍不可证实。历史归档原件不作为变更权限。

已验证的纪元推进、旧票据拒绝和恢复对账见 [W527-01 交付报告](../../../docs/development/evidence/W527-01/DELIVERY.md)。
