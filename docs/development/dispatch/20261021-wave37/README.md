# 第三十七轮：独立制造／出库指令Owner

COORD本聊天单人执行writing-plans/executing-plans；基线`fb31d246c42644511d12ad22931c6a4832dc6f7a`。开工2026-10-21T13:45:26.166523+00:00，24小时检查窗；代理预计180–360分钟，复杂故障余量10小时。原DEV-1产品、DEV-2资料，零新树/副本/代理。

|任务|代理实际执行估计|
|---|---|
|独立动作、对象/字段、内部桥接边界|15–30分钟|
|MES制造指令Owner与直接依赖|40–80分钟|
|WMS出库Owner、库存事务/提交后副作用|60–120分钟|
|相关PG/HTTP、必要前端、集中作者审查|40–80分钟|
|PR/台账四状态/Handoff/分支和席位收尾|20–40分钟|

- [x] 复用现有当前原会话、权限根和字段算法，在`CP6.Core/Services/Integration/OperationsOwnerV1.cs`建立两条明确映射；独立mes-work-order/wms-outbound-order动作、封存资源域及默认self。范围1/5沿用原Creator/tenant；2/3/4只由真实关联订单及既有显式部门映射确定，无可信映射拒绝，不把仓库/BaseCd猜成部门。WMS交集实际角色仓库/区域范围，原生PG比较；相关权限表锁保证撤权顺序。
- [x] `WorkOrderService`与新partial Owner文件：列表/详情/采号、创建/修改/删除/发行/来源展开及服务取消接当前Owner。输入冻结、旧/新完整图、字段用途/原Owner保留、异常事务与最终校验。发行和原订单创建的内部桥接使用受限可信委托，不接受外部自报父动作，不把原订单cancel委托变成通用MES许可。
- [x] `OutboundService`与新partial Owner文件：列表/详情、CRUD、confirm/cancel/allocate/pick/ship、来源展开接Owner；原生关联检查、完整真实仓库/区域及金额用途。库存、序号、出库子图/包裹同受控事务；预留不足和重复效果不得截断伪成功。保持已有部分配货语义，通知/财务/ERP桥在确认提交后；UNKNOWN不重放。
- [x] `Program.cs`、两个控制器、权限目录/文档同步；新动作只登记不自动授权。复用Cookie/CSRF，Owner拒绝保留401/403/503，跨模块来源及字段不泄漏。检查真实前端调用，必要时修正重复/过时/未知响应，未受影响页面不重测。
- [x] 新增原生PG测试先复现缺陷，再验证动作、租户、self/部门/仓库区域、字段、完整子图、精度/原生键、最终撤权、故障回滚、并发/UNKNOWN、实际Program HTTP；保留原失败，相关W36回归覆盖受影响桥接。每次集中修改后跑相关集合，不逐文件全仓测试。
- [x] 单份DELIVERY/Owner合同、roadmap四状态、作者集中审查与产品/Handoff正常采用；自有进程停止、生成data回收。
- [ ] 本文档PR合并后原W37-01 manage finish；实际分支/席位结果见登记及最终PR收尾评论。

验证复用`run_wave37.py <stage> <FullyQualifiedName过滤> --pg`和本地前端入口；隔离PG55435，不操作既有环境。其他WMS/MES模块、无关联对象的独立部门绑定、持久operation/财务投递、正式workflow/CRM和真实DEV/UAT仍独立。无Actions/真实tenant授权/生产部署。

实现、集中作者审查及上述相关验证已完成；单份[DELIVERY](../../evidence/WP02-03/operations-owner/DELIVERY.md)。10处本轮自有PG生成data累计回收1152009755逻辑字节（1.073GiB）；两个清理窗口实测D盘空闲增加合计1157378048字节（1.078GiB），跨两窗口及中间测试的实际空闲净变化1040855040字节。产品/Handoff已合入；原任务分支完成下述最后文档及finish。

## 远端采用及原任务分支收尾

产品[PR #381](https://github.com/GTX537/CP6/pull/381)正常合并，source`55b761e97b9e44b5f73dedd44f477addad753288`、远端main`bef201e6ed821a79b5b9a63f8581c61630e0aa7a`，任务改动blob一致；#380原故障回归及远端采用后关闭。[Handoff PR #13](https://github.com/GTX537/CP6.AI-Handoff/pull/13)合入默认目标，source`7f56d1eddd060073ca6b46d8ea3d524cbc5884f4`、目标`12d1d4cef10bceac49c17f8245332b7888fb710d`；资料分支已定向删除，DEV-2干净detached并释放。

164项去重相关.NET成功（143项未变证据复用+21项当前PG/实际HTTP）、9项Vitest4.1.11页面及完整vue-tsc --build通过，最终适用集合0失败/跳过；一次作者审查及发现问题的局部复查无剩余本窗阻断。最后文档不改产品/依赖/环境，复用上述证据，仅查差异、链接及台账。自有PG/API停止；10处自有生成data逻辑回收1152009755字节（1.073GiB）；两个清理窗口D盘空闲增量合计1157378048字节（1.078GiB），跨两窗口及中间测试的空闲净变化1040855040字节；日志/TRX/原失败保留。

本提交仍使用原codex/w37-01、DEV-1，最后正常PR后调用manage finish。当前提交不能提前证明自己的未来合并和释放；实际结果以管理登记`D:/CP6/.git/cp6-workspaces/state.json`、最后PR收尾评论及`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave37-final-verification.json`为准。INTEGRATION-1空闲；零新树/clone/完整副本/额外收尾分支/代理。

本独立制造／出库主服务Owner窗口已完成；其他WMS/MES入口（报工/入库/盘点/看板）、无订单关联对象的正式部门绑定、持久operation/outbox及可靠财务投递、正式workflow/CRM和真实DEV/UAT仍独立。表锁跨租户串行化成本及通知/ERP/FIN best-effort边界保持；完整WP02-03、125目标/247运行不提升。无Actions、既有环境迁移、真实tenant授权或生产部署。代理原估计180–360分钟，实际耗时在真实finish后核对。
