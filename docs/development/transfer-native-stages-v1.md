# 原生调拨执行阶段 v1

W236 承接[原生请求](transfer-native-requests-v1.md)，在已批准请求下准备、执行或永久关闭 DIRECT_MOVE 阶段。服务沿用 `Main.Wms.TransferRequest.v1` 与已有原操作查询；新增端点属于 `/api/wms/transfer/native-requests-v1`。这是现有原生 Stock 库存池的实际采用，完整 TF v1.1 的 Root/Slice/Claim、RSV Use、typed MetricSet、在途与物理指令协议仍需后续实现，不将本适配结果冒充这些合同的回执。

## 行为与当前边界

每个阶段包含准确请求行、`requestFrom`、固定八位正数量和源库存池 ID。请求范围不得越界或与同请求已准备/已记账阶段重叠；一个阶段的所有行必须使用相同准确基本单位。准备不改变库存，也不声称已预留库存。执行时重新检查全部当前余额、已有预留、双库位权限、产品/单位来源、批准人的当前批准权限及库存属性。

同租户 `device + scanOccurrenceKey` 唯一绑定服务器生成的阶段 ID，永久关闭后也不能复用。设备字段是本原生入口的扫描来源标识，并非正式设备注册证明；执行人员来自当前认证身份，绑定于准备人，客户端不能指定另一执行人。原命令键只用于恢复原结果，不能创建另一物理阶段。

`PREPARED → POSTED` 执行同一事务内的全部 Stock MOVE 出/入双腿、交易继承、移动序列、请求修订、历史、待发布事件和原操作日志。StockMovementService 仍为唯一库存写入入口。保留货权、客户、批次、单位、价格、收货日期、有效期、纸卷、质量及召回属性；不混入属性不同的目标池，不扣减或释放别人的预留。库存池只提供原生属性，尚不能证明完整技术身份/OriginPurpose 的逐 Root 账。

执行前必须有足够自由余额，任何仓库的 AllowNegative 都不能绕过本入口的不足检查。任一行、第二条移动腿、日志或最终授权失败时整组回滚。这里没有先提交 Stock、再补调拨关联的路径。

容量沿用原生 Location 的数量合同，包括祖先地点的净变化和 `ReservedCapacityQty`；共享祖先的出/入净变化只合计一次。现有 `CapacityQty=0` 表示原生无限；它不是 typed LIMITED(0) 的替代，也不构成多量纲容量验收。冻库、初始化隔离与盘点冻结继续由各自权威入口拒绝。完整单位指标、逐 Root 占用与其他未收义务继续列为后续范围。

`PREPARED → CLOSED_NO_EFFECT` 需要当前关闭权限与明确 `physicalNotOccurred=true`。已 POSTED 阶段不可关闭；实际已经搬动而数据库未执行时不得谎报未发生，需后续实际差异处置。关闭仅释放该阶段的请求份额，新阶段必须有新的扫描流水。提交响应未知时保留原操作键，查询或原键重发；查询缺失不代表没有效果。

请求内容版本和独立批准原件不因阶段动作改变；状态修订递增。`executionState` 反映 NOT_DISPATCHED/PARTIALLY_MOVED/COMPLETED，完全来自已保存的阶段回执。已完成的请求不因此被当成完整 TF SETTLED。

## 端点

| 方法与相对路径 | 用途 |
|---|---|
| GET `{id}/stock-choices` | 当前可见源库存池与可用数量，不预留 |
| POST `{id}/stages` | `operationId,expectedRevision,etag,contentDigest,device,scanOccurrenceKey,lines,reason` |
| POST `{id}/stages/{stage}/execute` | `operationId,expectedRevision,etag,stageDigest,physicalNotOccurred=false,reason` |
| POST `{id}/stages/{stage}/close` | 同上，`physicalNotOccurred=true` |

`lines` 为 `requestLineId,stockId,requestFrom,quantity`。每阶段最多500段/2MiB。请求当前视图、原历史与原操作返回阶段状态、准确选择和实际 Stock 移动 ID。沿用登录、CSRF、封闭 JSON 和 no-store。前端在待确认操作存在时禁用新写入，刷新/关闭页面不取消服务端阶段。

## 验收

允许使用模拟产品、库存、扫描、身份、权限与容量资料进行生产业务验收；实际代码、PostgreSQL 事务和 HTTP 执行。本轮测试及适用范围见[交付报告](evidence/W236-01/DELIVERY.md)。部署状态独立记录。
