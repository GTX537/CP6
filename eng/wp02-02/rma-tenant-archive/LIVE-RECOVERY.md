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
