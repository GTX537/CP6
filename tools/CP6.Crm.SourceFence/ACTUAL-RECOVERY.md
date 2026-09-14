# C04A 首写前来源恢复

本机来源恢复必须直接核验已永久终止的 CRM 目标，并持有目标读取锁直到来源提交。普通 `Closed` 状态、离线状态文件或人工摘要都不足以重新开放。此组件不停止服务、不恢复路由、不隔离管理员，也不批准实际执行。

## 顺序与绑定

1. CRM `target-freeze-anchor-local` 读取已验证且没有首写的 Closed 目标，输出 `TargetRollbackAnchor` 及其规范摘要。它绑定组织和原目标物理/门禁结构身份，尚不是永久关闭证明。
2. 来源冻结请求的 `TargetClosedEvidenceSha256` 填该 anchor 摘要；按原 `freeze-actual` 流程独立绑定每份实际来源。原生和 Docker 同名库不能合并为一份请求。
3. 若需首写前退回，先以原目标门禁关闭 Enabled 目标，再执行 CRM `abort-target-local`。许可绑定原目标、当前 Closed generation、批准材料引用及排序去重的 1–16 个源冻结请求摘要。Written 目标拒绝终止。
4. CRM 同一事务保留原 Control/Audit 行及 42 表保护，在 Control 上安装永久拒绝变更的触发器，记录 `CP6.CRM.TargetRollback.v1`。旧 Enable 因操作结构漂移而拒绝，直接修改 Control 被 SQL 拒绝。不会将整个数据库设为只读；其他 Schema 继续工作。
5. 来源 `reopen-actual` 直接连接目标，核对规范回执摘要、实际 SQL 身份、永久触发器、Closed/无首写和保护快照，再检查源请求属于许可集合且原 anchor 一致。在来源事务中移除自有保护并保存 `CP6.C04A.ActualSourceRollback.v1`；目标表锁一直保留到来源提交。

协议源 [`TargetRollbackProtocol.cs`](../../contracts/c04a/TargetRollbackProtocol.cs) 与 CRM 同路径文件逐字节一致，各自编译；两个工具通过 SQL/进程边界协调，不互相引用服务程序集。该 v1 是本机受控操作协议，不是 R2 候选或生产推广凭据。

## Core CLI

沿用实际检查的 `C04A_SQL_CONNECTION`、`C04A_EXPECTED_DATABASE`、`C04A_EXPECTED_DATABASE_GUID`、`C04A_EXPECTED_SERVER_NAME` 及超时变量。`C04A_EXPECTED_SCOPE_SHA256` 若提供仍为原冻结前 scope。新增：

Docker 来源另按[本机容器绑定](LOCAL-CONTAINER.md)提供容器文件与摘要；同一 Windows 协调器保持本机 CRM 目标锁，来源通过精确 loopback 发布端口访问。原生来源的请求/身份序列化不增加 null 字段。

| 变量 | 内容 |
| --- | --- |
| `C04A_RECOVERY_REQUEST_PATH` | 恢复请求 JSON 的绝对路径 |
| `C04A_RECOVERY_REQUEST_FILE_SHA256` | 已审阅文件原始字节的 SHA-256，小写 64 位 |
| `C04A_TARGET_SQL_CONNECTION` | 精确 CRM 目标连接，仅经进程环境传递 |

恢复请求使用 PascalCase：`RunId`、`SourceFreezeRequestSha256`、`TargetRollbackReceiptSha256`、`ApprovalEvidenceSha256`，可附 `Format=CP6.C04A.ActualSourceRecoveryRequest.v1`。类型规范摘要与文件原始字节摘要用途不同；单句柄读文件，拒绝未知/重复属性、错误格式和摘要。

```powershell
dotnet <published-directory>/CP6.Crm.SourceFence.dll reopen-actual
dotnet <published-directory>/CP6.Crm.SourceFence.dll recovery-status-actual
```

缺少协调输入时，`reopen-actual` 仍在 SQL 连接前返回 `C04A_TARGET_ROLLBACK_COORDINATOR_REQUIRED`；`seal-forward-only-actual` 仍未接通。原 `status-actual` 只验证 Frozen 回执，恢复后使用 `recovery-status-actual`。

相同恢复请求可重放；来源必须仍为同一冻结 run 的 Reopened/generation 2、同一身份与恢复后结构/权限/程序/作业/迁移摘要。旧 CRM 恢复后新增业务行不破坏重放，也不会生成重复 Audit。新请求、漂移、目标证明丢失或目标首次写入一律拒绝，不自动修复。目标终止是该部署的永久终态，没有取消终止命令。

恢复状态仅 `TargetTerminalClosureIndependentlyVerified=true`；完整写入隔离、批准、路由和进程生命周期验证保持 false。SQL sysadmin 可以绕过数据库保护，因此实际执行仍须独立生命周期/权限控制与 owner 审阅的恢复包。

## 验证

Core 新增请求和实际 Foundation 拒绝测试。CRM 的独立 `eng/c04a/CP6.CRM.CutoverRecovery.Tests.csproj` 需要 `CP6_CRM_TEST_SQL`、`C04A_CORE_PUBLISHED_CLI`、`C04A_CRM_PUBLISHED_CLI`，用两个真实发布工具检查完整恢复、目标锁持续持有、元数据漂移、错误来源和 DDL 失败原子性。它只创建唯一测试库；最小旧表列用于协调验证，不冒充真实列迁移或生产验收。已通过且未受影响的旧测试沿用原证据。
