# 04 R2A MOVE 试点

## 1. 范围与前置

范围固定为一个租户、一个仓库、一个设备组、十台真实 Android 设备。启用前
必须完成四类角色范围、设备激活/禁用、扫描器配置、条码与 UOM、打印机与
标签、异常接管和数据库恢复演练。

通过 Web 生产控制台提交仓库开关申请，OA/WF Inbox 由不同人员批准。
`ProductionMoveEnabled=false` 时 v2 拒绝创建、来源同步、认领和启动新 MOVE，
错误码为 `WM-R2A-DISABLED`；已开始任务仍可扫码、暂停、接管、部分完成、
完成、异常和取消。设备有效性始终校验。v1 行为不扩展。

## 2. 候选环境演练

- `scripts/prepare-r2-pilot.ps1` 仅在隔离仓准备任务；
- `scripts/invoke-r2-pilot.ps1` 运行 500 SignalR 连接、100 RPS 读取与真实
  扫码/完成工作流；
- LAN/WAN 分别保存证据；
- 扫码 P95 默认不高于 300 ms，WAN 可批准放宽到 1000 ms；完成与实时事件
  P95 不高于 2 秒；
- 演练断网、响应丢失、结果未知、相同 operationId/clientScanNo 重试、暂停、
  接管、部分完成、异常、取消、设备远程禁用。

负载和编排阈值由上述脚本及
`scripts/test-r2-pilot-contract.ps1` 维护，本规范不复制脚本内部实现。

## 3. 两周运行与每日对账

连续 14 个自然日由十台真实设备运行，累计至少完成 1000 个 MOVE。每天核对：

- MOVE 任务状态与完成数；
- OUT/IN 库存事务配对、重复 operationId 和重复库存事务；
- 仓库/库位/产品/批次数量变化与实际库存；
- 异常、部分完成、接管、取消和未知结果；
- 十台设备的激活状态、心跳和版本。

使用 `scripts/new-r2-reconciliation-evidence.ps1` 生成每日双人复核记录，随后
通过 `scripts/publish-r2-evidence.ps1` 归档。访问令牌和查询连接串不得进入
记录。

## 4. 退出门禁

`scripts/test-r2-pilot-exit.ps1` 对选定连续窗口执行机器门禁：

- 至少 14 个连续、唯一日期；
- 累计至少 1000 个完成 MOVE；
- 每天至少十台已激活且健康设备；
- 零重复库存事务、零库存丢失、零未解释差异；
- 每天对账人与批准人不同；
- 数据库恢复成功，RPO ≤ 5 分钟、RTO ≤ 60 分钟；
- 所有输入以 SHA-256 纳入 `R2A_PILOT_EXIT` 证据。

任一项不满足即为 NO-GO，不得申请 Serial/LPN。R2A 退出证据 URI 是
Serial/LPN 开关申请的必填输入。
