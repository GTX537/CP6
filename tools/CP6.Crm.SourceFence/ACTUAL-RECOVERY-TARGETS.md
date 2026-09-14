# 多组织目标的首写前来源恢复

实际切换涉及多个组织目标时，冻结来源必须绑定整个目标集合。仅一个目标关闭不能证明其他目标没有写入。此入口读取每个目标的实时永久关闭证明，并在全部目标锁持有期间恢复一份来源。

## 固定集合

[`TargetRollbackSet.cs`](../../contracts/c04a/TargetRollbackSet.cs) 定义 `TargetRollbackSetAnchor` 与 `TargetRollbackSetReceipt`，与 CRM 仓库逐字节镜像。集合包含 2–16 项，按 `OrganizationId.ToString("D")` 的 ordinal 顺序排列，拒绝重复组织、绑定与不合法成员。摘要使用默认 PascalCase JSON 的 SHA-256，不使用 CLI 的 camelCase 展示 JSON 字节。

在任何实际冻结前，以 CRM `target-freeze-set-anchor-local` 从原配置读取所有 Closed 目标的 Anchor。将集合 `anchorSha256` 放入每份 `ActualSourceFreezeRequest.TargetClosedEvidenceSha256`；原生 CP6DB、C02 私有 Core 库和 Docker CP6DB 分别固定实际身份、scope 与请求。集合范围由具体执行包明确，不从当前可用目标推断完整性。

需恢复时，先使用既有 `abort-target-local` 逐目标永久终止。每个目标的许可都必须包含待恢复来源的原冻结请求摘要。部分终止会保持来源冻结，不能将余下目标从集合中删除。CRM `target-abort-set-status-local` 返回经过实时核验的完整集合回执与 `receiptSha256`。

`ActualSourceRecoveryRequest` 格式保持不变，其 `TargetRollbackReceiptSha256` 改为完整集合回执摘要。单目标原有请求/回执摘要保持兼容；原单目标入口无法恢复集合绑定来源。

## Core 操作输入

新增命令 `reopen-targets-actual` 与 `recovery-status-targets-actual`。沿用原实际来源、恢复请求及可选 Docker 绑定变量；额外固定 `C04A_RECOVERY_TARGETS_PATH` 和 `C04A_RECOVERY_TARGETS_FILE_SHA256`，后者为文件原始字节小写 SHA-256。

目标文件使用严格 PascalCase JSON，最多 16 KiB：

```json
{
  "Format": "CP6.C04A.RecoveryTargets.v1",
  "Targets": [
    { "OrganizationId": "11111111-1111-1111-1111-111111111111", "ReceiptSha256": "<目标一回执摘要>", "ConnectionEnvironmentVariable": "C04A_RECOVERY_TARGET_ONE" },
    { "OrganizationId": "22222222-2222-2222-2222-222222222222", "ReceiptSha256": "<目标二回执摘要>", "ConnectionEnvironmentVariable": "C04A_RECOVERY_TARGET_TWO" }
  ]
}
```

示例为结构说明，摘要占位符必须替换。连接仅在本机环境变量中提供，不写入目标文件。变量名称须匹配 `^C04A_RECOVERY_TARGET_[A-Z0-9_]{1,64}$`；成员、回执、变量引用必须唯一且组织排序固定。文件在同一句柄下读取、验摘要、拒绝重复/未知字段与错误格式，再解析私有连接。目标仍只允许本机原生 SQL；容器仅作为来源。

```powershell
dotnet <published-directory>/CP6.Crm.SourceFence.dll reopen-targets-actual
dotnet <published-directory>/CP6.Crm.SourceFence.dll recovery-status-targets-actual
```

## 事务与恢复边界

先依次获得所有目标连接和事务，逐个核验组织、回执、源请求成员关系及永久关闭保护，再打开来源事务。只有完整集合摘要与原冻结 Anchor 都一致，才可解除旧表保护。任一目标 Enabled、Written、缺回执、漂移或绑定错误都会拒绝；已取得的目标锁在失败时全部倒序释放。

来源提交前重新核对所有目标回执、集合摘要、Anchor 和来源标记。全部目标锁持续到来源 COMMIT 后才释放；没有跨库写事务或自动撤销目标终止。精确重放允许来源业务重新写入，仍要求恢复后元数据与原请求一致。第一条新系统业务写后，目标不能签发终止证明，来源恢复因此被拒绝。

该组件不证明审批、管理员隔离、启动入口或路由控制。没有实际来源/预览切换；CRM11/C04B 正式验收和观察期仍开放，总体 60%（3/5）。
