# 06 运营与逐仓推广

## 1. Go/No-Go

每个候选、环境和仓库分别开会，不继承上一仓批准。最低参与者为 Release、
Security、DBA、Operations、Warehouse Owner、Inventory Owner。会议记录必须
链接清单、部署、R2A、R2B、恢复和告警证据，并记录批准人及时间。

以下任一情况为 NO-GO：

- 清单哈希、运行镜像 digest、Git SHA 或最新迁移不一致；
- live/ready/release、Redis、远程原生制品或 TLS 核对失败；
- 库存差异、重复事务、库存丢失或未解释序列/LPN 差异非零；
- 关键告警无负责人，十台设备健康数不足；
- 数据库恢复 RPO > 5 分钟或 RTO > 1 小时；
- R2B 产品跨仓、存在活动任务、预检数量不一致或审批过期。

## 2. 监控与停用阈值

持续监控 API/Web 可用性、SQL/Redis readiness、SignalR 连接、任务和扫码/
完成延迟、错误码、重复 operationId、库存对账、设备心跳、对象存储归档。

触发以下情况立即停止下一波次并阻止新业务：

- 健康检查持续失败或发布身份不一致；
- 任一重复库存事务、库存丢失或无法解释差异；
- 无法恢复的序列/LPN 聚合差异；
- 数据库、Redis、消息或证据存储不可用超过批准窗口；
- 安全事件、证书/密钥泄露或无法验证制品签名。

关闭开关仍须走 OA/WF 双人审批。MOVE 关闭后只阻止新建/认领/启动，允许安全
收尾；若已启用 Serial/LPN，必须先关闭 Serial/LPN，再关闭 MOVE。

## 3. 恢复与证据

每个推广波次前完成备份恢复演练，RPO ≤ 5 分钟、RTO ≤ 1 小时。数据库不降级；
应用回滚只选择 Schema 兼容的旧 digest，数据问题通过前滚修复。所有候选、
部署、每日对账、退出与事故证据使用
`scripts/publish-r2-evidence.ps1` 归档，保留期由合规审批决定。

## 4. 推广波次

1. 为目标仓重新填写 01 输入与 RACI；
2. 用已批准清单部署相同 digest，生成本环境 deployment evidence；
3. 为该仓单独完成 R2A 两周门禁；
4. 如在范围内，为该仓单独完成 R2B 预检、审批、转换和退出；
5. Go/No-Go 批准后进入观察期；
6. 观察期无停止条件才开始下一仓。

不得同时序列化多个仓库，不得把试点证据复制为下一仓证据，不得因波次相邻而
跳过恢复演练、双人审批或每日库存对账。

## 5. 职责交接

Release Owner 保管候选事实；Platform/Operations 负责部署与运行身份；DBA
负责迁移与恢复；Warehouse Owner 负责作业冻结、设备和任务；Inventory Owner
负责序列/LPN 数量真值；Security 负责签名、密钥、TLS 与证据保留。事故发生时
由 Operations 统一宣布 Stop，只有新的 Go/No-Go 记录可以恢复下一波次。
