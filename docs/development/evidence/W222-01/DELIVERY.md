# W222 准确订单行来源交付

订单生成出库单已在同一Owner事务保存准确订单行ID和不可变数量/单位快照；编辑按原行身份保留来源，退货来源直接采用该事实。修正生成时把计价单位当作数量单位的问题（Issue #645）。

生成记录由现有有权订单读取直接建立，快照仅含实际订单/订单行ID、业务行号、商品、原数量/数量单位及捕获时间，不含价格。物理数量单位使用QtyUnit；不回退到UnitPriceUnit。订单行ID不由浏览器写入，也不按SKU推断。

有来源的出库编辑使用现有LineNo定位原行，保留其UUID；允许改数量/备注及删除未处理行，拒绝改变来源订单、客户、商品、单位，拒绝重复/未知行号。删行保留来源历史。原生手工/历史未关联行不回填；旧RMA来源没有OrderLine时省略该可选字段，保持原版本摘要兼容。

新增PostgreSQL迁移20261030022200_OutboundOrderLineageV1，创建租户键控来源表及不可变历史触发器；仅在本地隔离测试库应用，不变更既有环境。已有Owner事务负责权限及整体提交，无新增资格/哈希证明包装。RMA在原有授权来源内读取快照，历史申请可保存该快照；没有财务字段披露。

测试的出库生成和RMA HTTP读取是真实服务调用；实物已发货状态由测试夹具设置，不宣称实际仓库发运或企业端到端验收。相关既有生成、CRUD、RMA创建提交和HTTP入口一并验证。集中审查发现出库页面删行会重排行号；已改为保留原行号、按最大行号分配新增行并显示真实行号。新回归在修复前真实失败（行2被改成行1），修复后10项相关页面测试全部通过。只复查这处修正，未重跑后端或全仓套件。Issue #645原步骤在实际PostgreSQL生成回归中验证EA/BOX区别；原源代码直接赋UnitPriceUnit，失败依据明确，未声称跑过旧版本红测。

正式ShipmentLine、InvoiceLine、Schedule、版本化UOM/范围、权益及独立审批仍未完成；本轮只消除真实订单行来源丢失这一直接缺口。125目标/247验收/375任务正式状态不提升。一次集中任务审查；无依赖/工作流变更、Actions运行/取消或部署。

验证：14 related PostgreSQL/HTTP tests passed, zero skipped; same-product identity, exact quantity/unit, edit preservation/rejection, atomic rollback, immutable history, old source-version compatibility and current authorization.

证据：D:/CP6-local-test/wave222/foundation/related-20261029T043252Z.json、log及results/TRX。

预计45–75分钟，截至快照登记10.54分钟。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/198)已合并、准确分支删除、DEV-2释放；Main待正常PR、远端核对、Issue645关闭和finish。最终结果见wave222-final-verification.json。固定目录、缓存和用户资料保留。

下一步：Continue direct D2 return authorization/fulfilment adoption and other original seven-day exits; no new qualification wrapper or audit-only wave.

页面证据：web-regression-before-20261029T044016Z.json（预期1失败、9筛选未执行，保留原失败）；web-related-20261029T044038Z.json（10/10通过、0跳过）。Handoff记录的是审查前14项后端结果，该页面修正在本任务Main交付中闭环。

类型检查vue-tsc通过：web-typecheck-20261029T044123Z.json。最终源码未再改变，无重复后端验证。
