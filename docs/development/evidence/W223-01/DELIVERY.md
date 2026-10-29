# W223 真实发货与退货履约来源

真实发货可显式保存退货履约来源：准确订单行、实际产品/单位版本、无损基准数量范围和发货批次事实，与库存出库、包装及流水同一事务提交；按当前退货权限回读不可变来源。

`ShipRequest.captureReturnSource`默认false，界面在订单出库发货对话框提供显式选项；每次打开重置该选择。勾选时在实际发货事务中复用已授权订单、W222准确订单行关系、产品原始版本和现有单位绑定。已有订单/产品/库存锁和单位存储锁保证本次决定的一致来源。来源丢失/变更、单位不一致或不能无损表示的数量直接失败；不按SKU猜订单行，不回填历史。

数量按既有单位合同精确换算，去除数据库表示的尾零，不舍入。正式边界保留13位整数/8位小数和半开范围。集中审查纠正了整数单位尾零的合法输入问题，并把订单来源事实限制为本次发货涉及的行，避免披露未发货行。新增两个针对行为场景，其他成功结果按实际输入未变复用。

实际发货、订单履约和发货批次事实具有明确Owner、真实对象ID、新建不可变来源版本及原始JSON摘要；读取直接信任已存合同，不重建资格或逐层复算摘要。`Main.Erp.OrderFulfilment.v1`是本次真实订单行截面，不能冒充旧订单全量修订；`Main.Wms.ShipmentFulfilment.v1`冻结真实出库流水/包装/行，`Main.Wms.ShipmentLot.v1`表示发货中实际批次出现，不能冒充全局批次主档。事实原文随快照可回读，产品/单位沿用已有Owner原文。范围是该发货版本实际发出数量，不是可授权退货余额。

`GET api/wms/rma/native-applications-v1/sources/{id}/fulfilment`按当前RMA对象/仓库/租户范围读取，响应禁止缓存；未捕获返回明确404。原始事实不会因后来订单或单位修订而变化。只保存本次发货涉及的订单行，不含价格/金额。源码输入没有额外签名/资格包装。

新PostgreSQL迁移20261030022300_OutboundFulfilmentV1仅建租户键控快照表和不可变触发器；仅在隔离本地数据库应用。实际HTTP完成了发货和读取，验证权限撤销、跨租户拒绝；实际物理出库测试覆盖精确换算、批次/流水事实和版本历史。插入失败注入验证库存、出库行、包装和序列全部回滚。真实仓库操作及企业环境验收没有执行。

覆盖明确为PARTIAL：Schedule/DeliveryProof/InvoiceLine/CommercialSnapshot/Serial/LPN尚未观察；null/空集合不是不存在关联的证明。没有隐藏已有批次，批次按真实发货事实提供。正式RMA-Q01选项签名、权益预览/占用、RMA-C01/C02/C03正式受理与独立审批仍未完成；本轮不宣称这些合同已验收，不增加七天目标百分比。原有发货后的财务通知等既有best-effort行为不改写，也不冒充本事务内正式财务采用。

一次任务集中审查，加上述修改部分复查。无依赖/工作流变化、Actions执行/取消、部署或既有环境迁移。CRM、Platform未修改。

验证：14 initial PostgreSQL/HTTP cases passed; focused review corrections passed 7 cases (2 additional, 16 distinct backend scenarios in total), zero skipped. 11 related Vue tests and vue-tsc passed. Unchanged evidence reused; no full-suite or remote run.

证据：D:/CP6-local-test/wave223/foundation/related-20261029T050532Z.json、focused-20261029T050934Z.json、web-related-20261029T050558Z.json、web-typecheck-20261029T050637Z.json及对应log/TRX。

预计45–75分钟，截至快照登记21.6分钟。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/199)已合并、准确分支删除、DEV-2释放；Main待正常PR、远端核对和finish。最终结果见wave223-final-verification.json。固定目录、缓存和用户资料保留。

下一步：Continue direct D2 return authorization/fulfilment adoption and other original seven-day exits; no new qualification wrapper or audit-only wave.
