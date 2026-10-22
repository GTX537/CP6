# Main 质量独立入口 Owner v1（W41）

2026-10-22；PostgreSQL；补充 [Operations v1](main-operations-owner-v1.md)、[库存/看板 v1](main-stocktake-dashboard-owner-v1.md) 和 [不良记录 v1](main-defect-owner-v1.md)。实际服务、HTTP 和页面采用，不新增角色授权、数据库模型或迁移。

## 独立动作与范围

|资源|动作与当前服务|
|---|---|
|wms-stock-qc|set；StockQcService 单库存及工单关联库存|
|wms-qc-inspection|query/add/edit/judge/cancel；QcInspectionService 列表、单票、从入库计划创建、直入、明细、判定、取消|
|mes-quality-inspection|query/add/edit；QualityInspectionService 列表、单票、模板引用、新建、编辑|

三资源采用实际原会话、可信租户、当前有效角色和启用动作、封存的空字段集合。主体与来源权限独立取交集；所有参与者（包括子收货 Owner 的来源）在根提交前重新读取权限/目录/组织版本。拒绝缺 Owner、不同 DbContext、外来/ambient 事务、脏上下文及预取业务图。

库存 QC 单条同时需要 wms-stock query 和两种资源的库存范围；工单批量需要 mes-work-order query、wms-stock query、完整工单及关联收货图。库存关联在数据库内按 warehouse/location/product/lot 完整匹配；不把同产品批次的其他库位一起改变。必须全部目标获准，否则整笔拒绝；单条响应尊重来源价格读取权限。

入库检验 self 覆盖头及全部含软删除明细 Creator；关联入库、生成收货完整图也必须符合主资源范围。从计划创建需要独立入库 query；失去该来源权限后关联检验不披露，直接检验仍可按自身权限使用。现无被接受的直接/采购检验部门绑定，部门范围不能由调用人或自由文本猜测。仓库/库区按实际位置与当前授权校验。

MES 品质需要主资源和 mes-work-order query 对完整工单图都授权；可选工程必须属于该工单。self 包含检验头、全部含软删除明细和相关不良的 Creator。相关不良必须保持同工单及有效工程；编辑前检查旧图、新图，提交前复查，不能改关联来清洗越权记录或留下孤立不良。原明细 Creator（含 null）/创建时间保留；新对象与 Modifier 取实际 Actor。部门只使用已接受的工单→订单→可信部门绑定。

过滤先于分页/统计；数量、测量精度及 PG 原生键等价保持，列表补全以行 Guid 归并。模板引用受独立 query 与当前租户约束，不要求不存在的工单来源。QC 头/明细及模板加入既有有序表围栏，覆盖 raw SQL；该短表锁仍跨租户串行，细粒度协议另项。

## 关联事务与结果

入库检验 PASS 的判定、序号、收货、库存、检验回写和最终权限在同一 PG 事务。收货内部能力限定同上下文的 wms-qc-inspection:judge 父 Owner、当前检验状态和准确已检数量/产品；仍要求收货 post、字段写权限、当前仓库范围和来源范围。公共收货入口继续拒绝外来事务。中间库存失败、最终授权读取失败或撤权均全部回滚。

MES 品质新建的自动不良、工单状态及 NG 关联库存 QC 与检验同事务；内部库存能力仅允许同上下文 mes-quality-inspection:add 和该实际新建 NG 检验。它不授予独立 stock-qc:set。缺共享写入实现时失败回滚，不能吞异常后宣称业务完成。编辑保留既有编辑语义，不新增重新生成不良或自动释放库存流程。

输入在首次 await 前冻结。确认丢失返回 OPERATIONS_OUTCOME_UNKNOWN，服务不自动重放；持久原操作结果、按原键恢复和 outbox 留在七天计划 D4。通知仅确认提交并释放事务后 best-effort；通知失败不把已提交业务改成失败。本窗不承诺可靠投递。

## HTTP 与页面

实际控制器 GET 补独立 query，保持 CSRF；Owner 401/403/409/503 不被通用业务异常转为 400，响应 no-store。Program 注册封存资源，不自动授予真实角色。

页面冻结请求与确认目标、拦截重复提交和 UNKNOWN 重发，身份改变清数据，迟到响应不能覆盖新身份/目标/已编辑表单。确认新建编号与判定/取消状态保留，即便读回失败；读回失败不会伪装成写入结果未知。列表/CSV 隔离旧身份。数量输入允许 8 位小数；未改变既有 JavaScript number 表示，原生精度由服务 PostgreSQL 验证。本地 Vitest 不替代浏览器 E2E 或真实 DEV/UAT。
