# 正式退货实物入库 v1

W230 将已批准份额接入实际 PostgreSQL WMS-IN/Stock 事务。用户已允许生产验收使用模拟业务数据；本实现模拟缺失的库存根映射、外部保管、质量政策等来源，原发货读取、入库单、库存数量、申领段和恢复结果均由实际代码执行。

## 业务路径

1. 从当前有效授权登记到货行。每行保存批准份额、精确区间、数量、批次/序列/LPN引用和到货时间。位置必须在当前仓库权限内，允许收货、非拣货、已完成初始化。待入库登记占住对应可收范围，尚不产生库存变动。
2. 准备入库时从已登记原件读取来源。原库存根使用 `RETURN/RETURN`；明确的新物理来源使用 `RECEIPT/RECEIVE`。按原流水分组形成冻结计划，一个登记可包含多个计划；每个 Stock manifest 的 parts 与实际行一一对应，RMA 的区间与申领只保存在适配绑定内。
3. 确认时重新核对当前授权、生效期、库位、入库权限、来源部门和库存版本。在既有 OperationsOwner 事务与锁内，实际 Stock 写入与原库存根范围登记、原生收货单/明细、RMA 申领消耗、结果、原命令及待发事实一起提交。任一失败全部回滚。原生流水使用 `INBOUND-RMA`，不触发采购暂估或宣称退款完成。
4. 查询从同库保存的原操作恢复。准备完成但未观察到 Stock 操作时返回 `UNKNOWN`，`canInferNoEffect=false`。关闭操作取得同一写入锁，保存永久终态；此后该工作不能再提交。关闭保留到货事实，释放该工作的待收范围，不撤销已经入库的事实。

一个登记中的多行与多计划是一个事务。部分收货只把实际收到的批准段转为 `CONSUMED_BY_RECEIPT`；其余段仍为 `ACTIVE_RESERVED`。批准撤销继续保留已收货范围。库存根占用按租户与 rootId 记录，来源版本变化不能重置已退回的根区间。

## Owner 与存储

- RMA 使用 `RmaFormalOwnerV1`；实际入库边界为 `RmaInboundAdapterV1`（`WMS-IN-01`），内部 Stock 计划参与者最终调用现有 `StockMovementService.ApplyWithinOwnerAsync`。三者共享实际 DbContext/数据库事务；不另开业务事务、不重做内部资格验证。
- PostgreSQL 前向迁移 `20261030023000_RmaPhysicalReturnV1` 增加工作、不可变历史、登记行/范围、收货原件、待发记录和来源映射，以及 Stock 冻结计划、原操作和根区间。新增动作不自动授予任何角色。
- 原生 StockTransaction 仍是流水事实。`movement_sequences` 为新原生流水赋予同事务单调序号，用于 Stock wire 的 ledger sequence / pool revision；所有现有 PostgreSQL 库存写入沿用同一钩子。旧流水不改写。序号因事务回滚产生空洞是合法的。
- 原生收货的部门访问使用同事务保存的实际原发货来源；全部来源必须处于当前部门和仓库范围。读回已提交结果也检查当前入库读取权限。旧业务入口中没有可用于取消本批收货的独立取消接口；后续冲正必须接入本条完整链路。
- 精确数量沿用当前正式申请的十进制字符串与 PostgreSQL `numeric(21,8)`，页面使用 BigInt 计算区间数量。新建/合并到既有原生库存池时核对单位及货权，避免不同单位或货权混合。
- 原生收货单与库存流水保存完整账号名。PostgreSQL 专用映射及前向迁移将两处操作人容量从 20 扩至账号合同的 100，保留原值及原有文本比较语义；不截断或替换身份。

## API 与页面

入口为 `/api/wms/rma/v1/applications/{applicationId}/physical`：`GET context`、`POST registrations`，以及 `GET works/{workId}`（可选 revision）、`POST works/{workId}/prepare|commit|seal`、`GET works/{workId}/inbound`。原命令查询为 `POST /api/wms/rma/v1/physical/commands/query`。

写入使用原有 Idempotency-Key、当前会话/CSRF、Correlation-Id 和 no-store；客户端只提交收货意图与工作/头版本，不生成 Stock 计划或哈希。页面提供到货登记、部分范围、准备/确认/关闭与原请求恢复。原请求保存在租户/用户隔离的 IndexedDB，通过 Web Locks 互斥；未知结果先查询，显式重派保持原始正文与命令号。切换用户或申请时保留未确认请求并丢弃旧响应。

`inbound` 查询按原正式合同 `fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json` 返回逐计划的 `RmaInboundReceiptOutcome`。原件来自用户本机成果归档，具体路径与实际校验输出记录在[交付证据](evidence/W230-01/DELIVERY.md)引用的 `canonical-wire-validation.json`；它不是本仓跟踪文件。实际测试输出的 preparation、receipt result 与 outcome 使用同一原合同核验。

## 模拟数据与范围

`RmaInboundSimulation:Enabled=true` 与 `CasesJson` 提供明确场景。每项包含 TenantId、不可变原 ShipmentRef/ShipmentLineId、Version、StockDimensions、RootMap、明确的 PriorReturnedSpans、外部 SourcePlace 或 NewSourceReasonRef，以及 QualityBasisRef。测试 `RmaPhysicalReturnPostgreSqlTests` 给出可复现的完整配置；不把缺失历史自动当零，不把缺失来源静默当新库存。

当前生产者是已经捕获的原生发货事实，包含批次引用，原生发货尚不输出序列号/LPN集合。适配器保留已有追踪引用和完整来源维度；其他发货生产者、复杂序列/LPN拆分和整个通用 Stock 合同不由本轮宣称完成。新原生库存沿用既有创建/货权表示；额外部门绑定必须使用已有独立创建合同，不从当前操作人猜测历史归属。

本轮只完成退货实物入库。冲正/更正、质量判定及五种最终处置、商业/财务、非空特批来源、自动到期关闭和待发事实发送器仍为后续实现。`inbound_outbox` 是已提交待发原件，不能声称外部消费者已经应用。全部模拟验收与实际部署状态分别记录，本轮不部署生产、不迁移既有环境、不运行 GitHub Actions。
