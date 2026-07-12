# M-ERP T5 任务简报：IAuditable 贴点(BusinessPartner/Product/价表/Order)

## 背景与位置
M-ERP 横切接线波第六任务(计划原文: 「T5 IAuditable：BusinessPartner/Product/价表/Order 贴点」——钱与主数据优先原则)。同型先例=M-WMS T6(commit eb5fb51): 纯标记接口单行追加,无 schema 改动无迁移(Sys_FieldAuditLog 已存在,运行时 ChangeTracker 捕获),豁免须源码注释坐实理由,配 WmsAuditTests 式测试(含负测试)。先 `git show eb5fb51` 学样。

## 需求
1. 圈定贴点实体(在 CP6 实体目录实查,不要凭名单猜): 
   - BusinessPartner(取引先)主档
   - Product(製品)主档
   - 价表类: SheetUnitPrice(用紙単価)及 FxRate(為替)等 ERP 定价主数据实体——以「改它会影响算价/金额」为圈定原则
   - Order(受注)头/明细
   - 相邻同类实体(如 Quotation/Estimate 头)是否纳入由你按「钱与主数据优先」判断,报告里给出纳入/豁免的逐实体裁决表
2. 每实体单行追加 `IAuditable` 标记接口,零其他改动。
3. 豁免的实体(高频写入/追加型日志类)在源码注释坐实理由,照 WMS Stock/StockTransaction 先例。
4. 测试: ErpAuditTests 照 WmsAuditTests 形状——贴点实体变更被 ChangeTracker/拦截器捕获产生 Sys_FieldAuditLog 行(真实断言),含负测试(未贴实体不产日志)。
5. 零业务逻辑改动;若发现某实体贴点会引入迁移(schema 变化),停下报 DONE_WITH_CONCERNS/BLOCKED,勿自行加迁移。

## 验证与提交
- `dotnet test --filter ErpAuditTests` 全绿;全量测试(以 T4 后基线为准)不许跌。
- 单 commit 即 push。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\erp-t5-report.md`(逐实体裁决表+豁免理由+测试证据)。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。
