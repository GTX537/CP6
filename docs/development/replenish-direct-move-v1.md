# 补货整单直接移动 v1

W239 继续 [REP](../design-handbook/modules/WMS-REP-01.md) D03/D04，并实施其直接需要的 [Stock](../design-handbook/modules/WMS-STOCK-01.md) 与 [Transfer](../design-handbook/modules/WMS-TRANSFER-01.md) 有界采用。入口仍为 `/api/wms/replenish/v1`。这是带实际 PostgreSQL 数量效果的整单 DIRECT_MOVE，不调用旧 MobileTaskV2。

## 数据与业务边界

`StockDirectMoveV1` 在 `cp6_stock_move_v1` 拥有准确 Root、当前互斥 Slice、现有 Claim Binding、容量以及不可变决定/事件。Root 保存完整产品、技术、货主、基本单位、原用途、原 Site 和来源身份。MOVE 保持原 Root，写出源 OUT 与目标 IN 两腿，不创建第二个实收 Root、不消费或释放已有 Claim Binding；绑定继续指向原 Root 范围。它不是 RSV CREATE/RELEASE/REBIND 或完整 Claim Grant 工作流的实现。

所有六字段 Ref 的 owner/id/version/digestDomain/codecVersion/digest 原样保留，不强转 GUID 或数字版本；只有本实现自己的订单 id 为 GUID。数量为字符串，Root/命令为 13 位整数与 8 位小数，容量/合计为 30 位整数与 8 位小数；内部用缩放 BigInteger，PostgreSQL 用 numeric。容量按完整 Domain/Metric/UOM 和有理数转换计算，同域两端先合并净变动，无法精确表示即失败。串号 Root 必须一整单位，禁止分数移动。

`TransferRootMoveV1` 在 `cp6_transfer_root_v1` 实际登记自己的 SourceDemand、Request、一个完整 Stage、准确 rangeMap、独立批准、设备/执行人、发生键与原回执。Demand/Request 坐标和 Root 坐标分别保存，每段长度相同；Root 的非零坐标不会被改成 Request 的零起点。稳定 dispatchKey 与 operationId 来自 W238 原准备登记。当前内部合同域为 `TRANSFER-ROOT/*`、`STOCK-DIRECT-MOVE-RECEIPT`；不把旧原生库存池 Stage 或本范围的内部摘要宣称为完整 Stock EFFECT-2/通用 H31 协议采用。

Stock、Transfer 和 REP 使用同一 Main 连接/事务。REP 订单当前状态、准确根范围、当前规则、两端权限以及 Transfer 独立批准/设备在负责的边界校验；Stock 在最终数量点检查位置、维度、Claim 移动控制和共享容量。现有 Owner 事务锁加 Stock/Transfer 表锁串行化同库决定，不在事务内调用外部 HTTP。任一 Stock 腿、回执或最终 REP 文档写入失败全部回滚。

## 模拟输入与生产验收

用户已允许生产业务验收使用模拟资料。`POST simulation/stock-opening` 接受 `scope,opening,device`；opening 明确 `dataKind=SIMULATED`，包含完整模拟 Root 期初分区、来源、Claim Binding/Use 控制、地点/原生产品映射和容量。服务器仍要求 `ReplenishSimulation:Enabled=true`、当前 `simulate-source` 与全域对象权限；租户/环境取可信会话和部署配置。设备绑定当前操作者，不能从 body 冒充另一执行人。

该入口建立全新、明确模拟的库存起点；已有原生库存行的相同范围拒绝自动映射，既有环境迁移另做任务。Stock 精确 writer binding 对对应 tenant/warehouse/location/product 的旧 `T_Stock` 写入在数据库入口拒绝，旧 REP/Mobile PostgreSQL 写入口沿用已存在的拒绝路径，其他范围不因此切换。默认配置不开模拟入口；本轮未部署或迁移任何既有数据库。

上游 MASTER 规则、质量/技术移动条件、RSV Use 控制和现场设备资料明确模拟，执行中的数量分区、容量决定、Transfer 记录、REP 完成、原结果和真实 HTTP 都实际运行。模拟输入不等于模拟成功响应。生产业务验收与环境部署状态分别记录。

## 操作与恢复

| 路径（订单路径前缀 `orders/{id}`） | 行为 |
| --- | --- |
| POST `transfer-prepare` | 当前 `RepCommand`，消费原 prepare 登记，实际创建完整 Transfer Request/Stage，等待独立批准 |
| POST `transfer-approve` | `RepExecutionCommand`，原创建者不能自批；还需 `wms-transfer-request:approve` |
| POST `dispatch` | 当前 `execute` 与 Transfer `prepare`，当前已登记设备和新 occurrenceKey，持久派发，数量仍不移动 |
| POST `complete` | 当前 `execute` 与 Transfer `execute`，原执行人/设备，全部实际 MOVE、原回执及 REP 完成共同提交 |
| POST `stop` | 当前 `cancel`；已有 Transfer 还需 `close-stage`。保留已 POSTED；未发送则关闭 REP 源门；未执行的 Transfer 写永久无效果决定 |
| GET `execution-link` | 完整原 Request/Stage/Receipt 引用及当前知识状态 |

`RepExecutionCommand={command,device,occurrenceKey,physicalNotOccurred}`；非派发时 device/occurrenceKey 可显式 null。写入以 commandKey 和当前 expectedRevision 绑定，未知结果只查询原操作或重发原体。同键异输入冲突。写权限撤销后，仍有当前 query/对象/两端权限才能查回原成功。

已派发停止若无法确认实物未发生，保留 RECONCILIATION/UNKNOWN，moved/remaining 为 null，未到保护不释放。未派发的 DRAFT/REGISTERED 可直接关闭源门；晚到 prepare 会被当前订单版本/状态拒绝。已经 MOVED 的停止保留实际效果，不自动反向移动。部分现场事实及后继认账仍需原 Transfer ActualFact/Case 任务，不用计划量或零值补答案。

Stock 移动后的完整观察与 REP 完成同事务更新；ActiveIntent 从各 split 当前原订单推导。已到量进入 onHand 并移出 openIncoming，同一义务不因 Order/Stage 链接重复计数。未知 split 使有效位置保持未知。原批量/原 prepare 响应不会被改写为后来状态。

人工订单只核准确源/目标权限；无关库位无权不会阻止合法人工单。context 在部分可见范围只返回有权的人工选项并标 PARTIAL，不把缺来源的分析范围伪称完整。原范围与当前源撤权回归见 [Issue 665](https://github.com/GTX537/CP6/issues/665)。

## 未完成范围

完整 TRANSIT、ActualFact/Case 认账、通用 Source/RSV Use 生命周期及 Claim 创建/释放、全部质量/MASTER 生产宿主接线、既有库存迁移与完整历史任务族、订单列表/导出、外部事件消费者仍分别推进。当前成功不能关闭完整 WP10-10、WP10-31 或七天 D2。实际验收范围与失败记录见[单份交付报告](evidence/W239-01/DELIVERY.md)。
