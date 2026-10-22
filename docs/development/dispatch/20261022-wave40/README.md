# 第四十轮：MES不良记录独立 Owner

COORD本聊天单人；基线`4ace44a582a4fcd605de38e4716031019603b4b8`；开始2026-10-22T02:41:50.848076+00:00。采用executing-plans、writing-plans、using-git-worktrees的计划/隔离检查，执行以本仓单人、一次任务级审查、固定席位规则为准。W39产品PR387、资料PR15、最后文档PR388已经采用和实际finish，不重复。

|任务|预计代理实际耗时|
|---|---|
|前置、范围登记与Handoff START|10分钟|
|不良记录Owner和页面接线|25–40分钟|
|定向验证及集中作者SPEC/QUALITY|15–30分钟|
|合并、状态、分支与席位收尾|10–20分钟|

未来24小时可完成本窗口；预计实际60–100分钟，复杂故障余量3小时。前置WP02-02原会话/当前权限和W37–W39 OperationsOwner/当前源/最终门/序号事务/页面意图已合入；完整WP02-02/03及业务验收不提升。当前无开放Issue/PR和W40 Actions，三个席位开工前空闲；根目录用户未跟踪文件保留。公共Owner、相关控制器和页面资源由本聊天独占串行编辑。

- [x] `DefectRecordService.cs` / `.Owner.cs`：PG独立入口全部进入OperationsOwner；冻结DTO/查询，分页前筛选可见整对象；独立动作、原会话、tenant、自身与可信订单部门绑定；序号、保存和最终权限同事务，拒绝缺Owner/外来事务/脏上下文，未知提交不重放。Creator不可换成调用参数，数量decimal(21,8)保持；通知仅确认提交后best-effort。
- [x] `OperationsAccessV1.Defect.cs` / `OperationsOwnerV1.cs`：不良本体、指图/工程/材料及可选检验头/全部含软删除明细的完整授权图；新旧关联都验证。实际关联键由PG比较，读写均验证来源工单query；自建检验号不能替代可信部门绑定。分类只作租户引用，不自动加真实角色授权。
- [x] `DefectRecordController.cs`：三个GET补query要求，实际服务异常沿现有Owner中间件；现有add/edit/del保持。`DefectManagementView.vue`复用useOperationsIntent，冻结保存/删除目标、重复提交/确认竞态、旧身份/目标/异步响应、UNKNOWN锁定、保存成功后读回失败保留已确认结果。
- [x] `DefectOwnerPostgreSqlTests.cs`及实际Program HTTP、`DefectManagementView.spec.ts`：先重现缺Owner读写；有效查询/CRUD、软删除子图/租户/部门/动作/原会话/最终撤权、请求冻结、未知提交/序号回滚/提交后通知，页面异步行为；相关既有Owner/Defect兼容回归，完整vue-tsc --build。只跑相关集合，失败证据保留。
- [ ] 一次作者SPEC/QUALITY完整diff、单份`docs/development/evidence/WP02-03/defect-owner/DELIVERY.md`、版本合同、roadmap/四状态/生成进度/Handoff；正常PR、远端采用、停止自有资源、准确分支清理和manage finish。

WMS库存QC、入库检验判定、MES品质检验自身Owner、补充/退货/设备/OEE/计划、无订单部门绑定、采购/财务自身Owner、持久operation/outbox和可靠投递仍留后续。现有MES品质检验自动生成不良的父入口不在本轮宣称采用。无新树/clone/代理/Actions/既有数据库迁移/生产部署；125目标/247运行验收原值保留。

实现、一次作者审查和相关验证完成；[DELIVERY](../../evidence/WP02-03/defect-owner/DELIVERY.md)。83项去重相关.NET成功：12项本轮原生PG/实际HTTP、55项既有Owner PG/HTTP、16项MES兼容/审计/权限；7项Vitest及完整vue-tsc --build通过。最终适用集合0失败/跳过。MES看板不良来源同步采用可选检验图，无另开窗口或改动其他看板；正常PR/Handoff及原分支finish待执行。
