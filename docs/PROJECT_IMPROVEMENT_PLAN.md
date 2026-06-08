# CP6 ERP→MES→WMS 业务功能完善设计

> **生成于** 2026-06-03，基于代码盘点 + Bridge Hook 闭环 Phase 1-4 已落地后的现状。
> **关联文档**：[PROJECT_STRUCTURE.md](./PROJECT_STRUCTURE.md) §2.3 描述当前闭环架构；本文档列出**已知缺口 + 改进设计**。
> **范围声明**：Phase 5 mcframe7 連携已明确不做，本文档不涉及。

---

## 一、当前真实状态（代码盘点）

| 已有 ✅ | 缺失 ❌ |
|---|---|
| Bridge Hook Phase 1-4 闭环（正路径） | ERP 受注无 `CancelAsync`（`OrderService` / `IOrderService` 均无 cancel 方法） |
| WMS `OutboundService.CancelOrderAsync`（含状态校验 + 引当解除） | MES `WorkOrderService` 同样无 cancel |
| FEFO/FIFO 引当（Outbound/Expiry/Kitting/Mobile/Ink 多处一致） | Bridge Hook 失败**只**走 `ILogger`，无 DB 持久化、无重试、无补偿表 |
| QualityInspection NG 自动起 `DefectRecord` | WMS 出荷**不校验** QC 状态（NG 品可被分配出货） |
| RMA Service（242 行已实装） | RMA 回写 ERP 受注（贷方/扣账）逻辑不清晰 |
| 部分出荷（`ShippedQty < AllocatedQty`） | 欠品 → 反向告知 ERP「需要 backorder」缺失 |
| `OperLogFilter` 全局记录 | 跨模块端到端 trace（同一 WebOrderNo 串所有事件）缺失 |
| 三个 Bridge Hook 都有 `ILogger` Info/Warn/Error | 没有可见的失败队列 / 重试机制 / 健康监控 |

**核心判断**：闭环架构合理，**异常路径 + 可观测性**是从 demo 到生产的最大差距。

---

## 二、4 个完善维度

### 维度 1：业务异常路径闭环（**最高优先**）

#### Gap 1.1 — 受注取消链不存在 [P0]
- **现状**：`OrderService` 无 `CancelAsync`。客户取消订单只能软删整条记录，已展开的 WO/Outbound 不会反向解锁。
- **后果**：客户取消 → MES 继续排产 → WMS 继续引当 → 实际损失。
- **设计**：
  1. `IOrderService.CancelAsync(webOrderNo, reason, user)`，状态机：仅 `Confirmed/InProduction` 可取消，`Shipped` 不可
  2. 新增 **`IOrderCancelBridgeHook.OnOrderCancelledAsync(webOrderNo)`**（对称 Bridge 模式）
     - 调 MES `WorkOrderService.CancelByOrderAsync`（若指図未着手）
     - 调 WMS `OutboundService.CancelByWebOrderAsync`（若未出荷）
  3. 半路状态返回 `PartialCancelResult`，前端弹窗让营业决策（继续完成 vs 强制中断）

#### Gap 1.2 — 材料欠品反流 [P1]
- **现状**：MES 指図発行时 WMS 引当失败抛 `InsufficientStockException`，未结构化记录。
- **设计**：
  1. 新表 `T_MaterialShortage`（WorkOrderNo + MaterialCd + RequiredQty + AvailableQty + DetectedAt + ResolvedAt）
  2. `StockMovementService.ApplyAsync` 检测不足时不抛异常，改为写 `T_MaterialShortage` + SignalR 推送 + `IPurchaseHintHook`（未来对接采购）
  3. WMS Dashboard 加「未解决短缺清单」widget

#### Gap 1.3 — 品质 NG → 阻止出荷 [P0]
- **现状**：QC 起 `DefectRecord`，但 `OutboundService.AllocateAsync` 不查 QC 状态，NG 品可被分配出货。
- **设计**：
  1. `Stock` 加 `QcStatus` 字段（`Pending/Passed/Failed/Hold`），默认 `Pending`
  2. QualityInspection PASS 改 `Passed`，NG 改 `Failed`
  3. `OutboundService.AllocateAsync` 过滤 `QcStatus = Passed`（或显式 `IncludeUnchecked = true` 用于紧急放行）
  4. NG 品自动 `MoveAsync` 到「不良品仓」（WarehouseType=NG）

#### Gap 1.4 — RMA 闭环到 ERP [P1]
- **现状**：`RmaService` 处理 WMS 端退货入库，但是否回写 ERP 受注（贷方/扣账）不清晰。
- **设计**：
  1. RMA 確定 → 新 `IErpBridgeHook.OnReturnConfirmedAsync(rmaNo)` → ERP 生成 `CreditNote` + 受注 `ReturnedQty` 累计
  2. 区分 RMA 类型：退货换新 / 退货扣款 / 退货报废 → 三种走不同 ERP 凭证

---

### 维度 2：可观测性 + 可靠性（**生产必备**）

#### Gap 2.1 — Bridge Hook 失败仅在日志 [P0]
- **现状**：`WmsBridgeHook.cs:38` `_logger.LogError` 后吞异常返回 `Failed`。无主动查询渠道。
- **设计**：
  1. 新表 **`T_IntegrationEvent`**：
     ```
     EventId GUID PK
     SourceModule string (ERP/MES/WMS)
     TargetModule string
     HookName string
     SourceNo string (受注号/指図号/出庫号)
     TargetNo string?
     Status enum (Pending/Success/Failed/DeadLetter/Compensated)
     Attempts int
     LastError string?
     NextRetryAt DateTime?
     CorrelationId GUID (串联同一业务链)
     PayloadJson string
     CreatedAt / UpdatedAt
     ```
  2. 所有 Bridge Hook 改三段式：
     ```csharp
     var evt = new IntegrationEvent { Status = Pending, ... };
     await _db.IntegrationEvents.AddAsync(evt);
     await _db.SaveChangesAsync();
     try { 
         await Execute(); 
         evt.Status = Success; 
     } catch (Exception ex) { 
         evt.Status = Failed; 
         evt.LastError = ex.ToString(); 
         evt.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, evt.Attempts)); 
     }
     await _db.SaveChangesAsync();
     ```
  3. 后台 `IntegrationEventRetryWorker : BackgroundService` 每分钟扫 `Failed AND NextRetryAt <= now`，最多 5 次
  4. 5 次失败 → `DeadLetter` + SignalR 推 Dashboard + 邮件告警

#### Gap 2.2 — 跨模块端到端 trace [P1]
- **现状**：一个 WebOrderNo 在 OperLog 里散落多条，无法一次性看「从受注到出荷」时间线。
- **设计**：
  1. `T_IntegrationEvent.CorrelationId` 串联（受注号→指図号→出库号→回写号 同一 ID）
  2. 前端 `/erp/order/{webOrderNo}/timeline` 页面，按 CorrelationId 拉所有事件 + OperLog → 时间轴
  3. SignalR `OrderProgressHub` 实时推「指図完成→入库→出荷」给营业看板

#### Gap 2.3 — Bridge 健康监控 [P2]
- **设计**：
  1. WMS Dashboard 新「集成健康」area：MES Bridge 24h 成功率 / ERP Bridge 当前队列长度 / 最近 5 次失败
  2. `IntegrationHealthService.GetMetricsAsync()` 从 `T_IntegrationEvent` 聚合
  3. Prometheus `/metrics` 输出 `cp6_bridge_success_total{hook}` `cp6_bridge_retry_queue{hook}`

---

### 维度 3：跨模块 KPI / 报表（**面试 demo 加分**）

| Gap | 数据源 | 设计 |
|---|---|---|
| **3.1 受注→出荷 Lead Time / OTD 报表** | `Order.OrderDate/RequestDate` + `OutboundOrder.ShippedDate` | ReportCenter 按客户/产品分组，输出准时率 + 平均延迟 |
| **3.2 在庫滞留分析** | `Stock.ReceiveDate` | 「滞留 Top 50」+ Dashboard widget：`(now - ReceiveDate) > 30/60/90 天` 分桶 |
| **3.3 生產計画達成率** | `WorkOrder.PlannedQty` + `ProductionResult.GoodQty` | 「指図達成率」报表 + 日 KPI 卡片 |
| **3.4 受注済未出荷 dashboard（最高商业价值）** | 跨 Order/WorkOrder/OutboundOrder | 营业首页 widget：列受注号、客户、应交期、当前状态（在 MES 哪个工程/WMS 哪个状态），含「催货」按钮（SignalR 推工厂） |

---

### 维度 4：数据完整性边界

#### Gap 4.1 — 欠品分批出荷 → ERP backorder [P1]
- **现状**：WMS 部分出荷 `ShippedQty += shipQty` 累积；当 ShippedQty < OrderQty 且后续无库存，ERP 端无「关闭剩余 / 转 backorder」机制。
- **设计**：
  1. `OrderDetail` 加 `BackorderQty` 字段
  2. WMS 出荷确认后，营业可在 ERP 端按受注明细「关闭剩余」（生成 backorder 新明细 or 放弃）
  3. ERP Dashboard 加「Backorder 队列」

#### Gap 4.2 — 多仓位路由策略 [P2]
- **现状**：`OutboundService.AllocateAsync` 按 ProductCd 引当但不区分仓位优先级。
- **设计**：
  1. `Warehouse` 加 `OutboundPriority` int
  2. 新表 `T_OutboundRoutingRule`（客户区域 / 产品类别 → 首选仓）
  3. 引当排序：RoutingRule → WarehousePriority → FEFO

#### Gap 4.3 — 多币种 / 汇率冻结 [P2]
- **现状**：日本企业系统但有海外客户场景。`Order.UnitPrice` 是单一货币。
- **设计**：受注时按 `BusinessPartner.CurrencyCd` + `T_FxRate` 当日汇率冻结到 `Order.FxRate`，回写时按冻结汇率换算

---

## 三、改进路线（按价值/成本排序）

| 阶段 | 改进项 | 影响 | 工作量 |
|---|---|---|---|
| **Phase 6** | Gap 1.1 受注取消链 + Gap 2.1 IntegrationEvent 持久化 | P0 × 2，从面试 demo 到生产可用的临门一脚 | 中（~3-5 天） |
| **Phase 7** | Gap 1.3 QC 拦截出荷 + Gap 2.2 端到端 trace | 数据完整性 + 可调试性大幅提升 | 中（~3-4 天） |
| **Phase 8** | Gap 3.4 受注済未出荷 dashboard + Gap 3.1 OTD 报表 | 商业价值看板，面试演示效果好 | 小（~2 天） |
| **Phase 9** | Gap 1.2 材料欠品反流 + Gap 4.1 backorder | 闭环更完整 | 中（~3 天） |
| **Phase 10** | Gap 1.4 RMA→ERP + Gap 2.3 Bridge 健康监控 | 锦上添花 | 中（~3-4 天） |

**建议立即开始**：Phase 6（受注取消链 + IntegrationEvent 持久化）—— 这是当前架构最大的两个 P0 漏洞。

---

## 四、与既有架构的兼容性

- 所有改进遵循既有 Bridge Hook 设计原则：**接口隔离 + Best-Effort + 配置可禁用**
- 新表加在既有 `CP6Context` 的 OnModelCreating 模式下，配迁移
- 新 Service 走既有 `BaseProvider` + 测试在 `CP6.Tests/` 对应目录
- `IntegrationEvent` 持久化是对**现有 Bridge Hook 调用语义的非破坏性增强** —— 接口不变，调用方无感

---

*生成于 2026-06-03，via /document-generate skill 风格的 gap analysis。*
