# F1 财务油路接通设计（收入确认 / 存货过账 / 成本差异 / 年结 / 完工反冲）

日期：2026-07-07
状态：**用户已拍板两大口径（永续盘存 / 完工反冲）**；本 spec 定架构与规则，开工令时按 writing-plans 出细计划。
依据：`cp6-global-audit-2026-07-07` 主题 T1 全部四条 + T4 反冲（反冲从 M-MES 移入本包——它本质是 MES→WMS→GL 的集成问题）。

## 1. 问题与目标

审计确认：FIN 自动记账桥（出货开票/取消红冲/完工成本归集）**代码与单测全在但生产零触发**（`FinShipmentInvoiceRequest` 从未被构造，`OutboundService.cs:527` 只调 ERP 桥）；库存移动全程不产生存货分录；成本差异算出存表不入账；无年结。目标：**让"完整 ERP"在会计正确性上成立**——账实实时一致（永续盘存）、收入自动确认、成本闭合、年度可锁。

**用户拍板存档（2026-07-07）**：①存货过账=**永续盘存**（每笔移动实时凭证）②MES 原料=**完工反冲**（报工良品数×BOM 标准用量自动扣料；领料指示保留为实物搬运指令，差异走盘点）③年结口径（本 spec 默认，见 §6）：损益结转本年利润→未分配利润+锁年+期初结转。

## 2. 架构原则

1. **对齐已走通的 WMS→ERP 模式**：业务服务在提交事务内直呼 Hook（如 `OutboundService:527` 调 `_erpBridge`），Hook 内落 IntegrationEvent 供审计/重试。FIN 侧照抄：**业务方直呼 `IFinBridgeHook`，不发明新机制**。
2. **同事务原子**：凭证生成与业务动作同一 SaveChanges（复用 IApprovalCallback 已成文的原子性铁律语义）；凭证失败=业务回滚，绝不"货走账没走"。
3. **科目映射做成主数据**，不硬编码：新表 `Fin_PostingRule`（见 §3），运维可配可审计。
4. **幂等**：每条自动凭证带 SourceType+SourceId 唯一键，重试/重放不重记。

## 3. 子域 A：存货过账规则引擎（永续盘存的地基）

**新表 `Fin_PostingRule`**（TenantId 隔离，走 P1 规范横切三件套）：

| 列 | 说明 |
|---|---|
| MovementType | 库存移动类型（IN_GR 采购入库 / OUT_ISSUE 生产领料·反冲 / IN_FG 完工入库 / OUT_SHIP 销售出库 / ADJ_GAIN·ADJ_LOSS 盘盈亏 / SCRAP 报废…以 StockMovementService 现有类型枚举为准，开工时逐一映射） |
| DebitAccount / CreditAccount | 借贷科目（引用现有 GL 科目表） |
| CostSource | 取价来源：Standard（物料标准成本）/ Actual（批次实际）——纸箱行业标准成本法，默认 Standard |
| Enable / Remark | |

**接线**：`StockMovementService` 每笔移动提交时查规则→生成凭证（复用现有双凭证引擎）；查无规则的移动类型 → **fail-closed 拒绝过账并告警**（E-FIN 新码），不静默跳过——与审批解耦包同一哲学。种子：预置全套默认规则（借:原材料/贷:GRNI 等标准分录模板，随包交付逐租户）。

**盘点差异**：`StockTakeService` 差异调整（ADJ）自动走上表规则（借/贷:盘盈盘亏损益科目）；同包补**盘点冻结**（StockTakeService.cs:14 自认未实装）——盘点单开启即冻结所涉库位出入库，关单解冻。冻结属于本子域因为它保证过账基线正确。

## 4. 子域 B：出货→开票→红冲油路

- `OutboundService` 出货确定处（:527 一带）追加直呼 `IFinBridgeHook.OnShipmentConfirmedAsync`（构造 `FinShipmentInvoiceRequest`——该类型及消费端 `ArInvoiceService.CreateFromShipmentAsync` 已存在且有单测，纯接线）。
- 出货取消处对称接 `OnShipmentCancelledAsync`（红冲，消费端已在）。
- 同时过账 COGS：借:销货成本/贷:库存商品（走子域 A 规则表 OUT_SHIP）。
- **注意**：开票时点=出货确定（拍板默认）；若客户要求"检收基准"开票，规则表留 TriggerPoint 扩展位但本期不做。

## 5. 子域 C：完工反冲 + 成本归集 + 差异结转

- **反冲**：`ProductionResultService` 全工序完工处（:256 一带）按 良品数×BOM 标准用量 生成 OUT_ISSUE 反冲移动。**✅负库存守卫（2026-07-11 用户拍板）=允许负库存记账+告警**（产线不能因账停；库存记负值 + 发告警通知；差异后续由盘点吸收）。领料指示保留为搬运指令（不再是唯一扣账通道），已领未反冲差异由盘点吸收。
- **成本归集**：完工同时触发 `OnWorkOrderCompletedAsync`（消费端已在）：借:库存商品(标准)/贷:WIP。
- **差异结转**：`CostSettleService.SettleAsync` 扩展——TotalActual 与 Standard 的差异（Material/Labor/Overhead 三行已算出存表）生成差异凭证：借/贷:成本差异科目（价差量差本期不细分，一科目起步）。**✅月结去向（2026-07-11 用户拍板）=月结时差异科目余额结转销货成本 COGS**（实际成本法标准做法，差异并入当期损益，差异科目月末清零；不做留存/分摊）。

## 6. 子域 D：年度结账

- `PeriodCloseService` 扩展 `YearCloseAsync`：①校验 12 期全 Closed ②生成年结凭证（JournalEntry.cs:82 已预留类型）：全部损益科目余额清零结转"本年利润"→再转"未分配利润" ③资产负债类科目生成下年期初余额 ④锁年（已锁年度任何凭证拒绝，含手工）。
- `BalanceSheetService` 本年利润改为"本年度累计"口径（当前=建账以来累计，年结后自然归正）。
- 年结可逆：提供 `ReopenYearAsync`（高危权限独立 action，红冲年结凭证），审计留痕。

## 7. 子域 E：油路探测器（跨模块闭环 E2E）

随包交付 4 条集成测试进 CI，任何桥断线当天红：
1. 受注→出荷→AR 发票自动生成→回款核销。
2. PO→GR（借:原材料）→三单匹配→AP→付款（已通链+新增 GR 存货分录断言）。
3. 工单→反冲扣料（借:WIP/贷:原材料）→完工入库（借:FG/贷:WIP）→差异结转。
4. 盘点冻结→差异→盘盈亏凭证。

## 8. 横切与范围外

- 权限/审计/i18n/错误码全按 `docs/00-横切接线规范.md`；E-FIN 错误码水位开工时对照 Fin 现有码表锁定。
- IntegrationEvent 补 userId（审计 T4 尾项 BridgeHookBase.cs:75 Creator="system"）随本包顺带修——发布 payload 带 operator（对齐 Space 波1 publishedBy 先例）。
- **范围外**：现金流量表/合并报表（B1 不变）、检收基准开票、价差量差细分、多币种存货重估。

## 9. 排期与依赖

- 前置：P0（无硬依赖但建议先行）+ P1 规范（横切照抄）。
- 与模块波关系：反冲已从 M-MES 移入本包；盘点冻结已从 M-WMS 移入本包（均为过账正确性前置）。M-MES/M-WMS 波相应减负。
- 开工令时：按 writing-plans 出细计划（需实读 StockMovementService 移动类型枚举/双凭证引擎 API/CostSettle 结构后写码级任务），编码=Opus 4.8。
