# W226 原商业退货来源采用

真实发运事务已冻结准确原订单商业行；退货申请详情通过独立来源Owner查询原价格/金额与当前变化，保留十进制字符串、原null和精确行身份。

商业快照与库存、包裹、原履约在同一PostgreSQL事务保存，失败整笔回滚。价格正文不进入普通WMS来源Facts；独立接口检查当前RMA、仓库、订单对象及UnitPrice/Amount权限。历史取消/软删除仍按当前权限读取，物理缺失明细明确LINE_MISSING；旧发运不按今天价格补写。ERP只提供事实，Main WMS保留独立退货决定及E/A/P职责；政策、发票、交期、签收、可退资格、独立批准及正式IN仍未完成。

[接口与限定范围](../../sales-return-source-v1.md)。

验证：PostgreSQL related run: 20 PASS and 1 fixture timestamp failure; corrected the fixture to match the existing timestamp-without-time-zone column, focused rerun 1 PASS. Review-focused 2 PASS covers missing-line self scope denial and actual HTTP authority. Combined 21 affected cases covered, including actual shipment/HTTP, exact original/current values, field/object/tenant/warehouse denial and rollback. Vue formal page 13 PASS; vue-tsc PASS. Initial failure retained; unchanged W224/W225 evidence reused. No Actions, real-browser/target-environment acceptance or existing-environment migration.

One task-level complete diff review covers source ownership, exact UUID lineage and decimal/null preservation, shared shipping transaction, current object/field authorization, stored-fact trust, migration immutability and page identity invalidation. Focused correction denies original commercial reads under self scope after the exact source line has been physically deleted: its creator can no longer be proven. Header-based department/full scopes retain their legitimate historical reads. No duplicate qualification/hash validation, alternate approvals or eligibility defaults; only affected regression rerun.

本地证据：D:/CP6-local-test/wave226/foundation/ 下 related-20261029T073521Z.json, focused-20261029T074012Z.json, web-related-20261029T073532Z.json, web-typecheck-20261029T073620Z.json, review-focused-20261029T074343Z.json及对应log/TRX。原失败发生在测试夹具更新历史取消时间，产品未失败；仅按原数据库字段类型修正，保留失败记录。页面测试使用模拟API，未运行真实浏览器；不作为生产或完整业务验收。

预计60–100分钟，截至快照登记27.04分钟。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/202)已合并、准确任务分支已删除、DEV-2已释放；Main待正常PR、远端采用和finish。最终生命周期见wave226-final-verification.json；用户资料、固定目录和缓存保留。

下一步：Continue actual entitlement and independent authorization, then formal inbound consumption, preserving every original seven-day exit.
