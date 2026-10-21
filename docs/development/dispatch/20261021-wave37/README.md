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
- [ ] 一次作者SPEC/QUALITY、单份DELIVERY及Owner合同；正常PR采用、roadmap四状态/Handoff同步。仅回收本轮已停止自有PG生成data，保留日志/失败；原任务分支manage finish，定向分支清理、固定席位释放。

验证复用`run_wave37.py <stage> <FullyQualifiedName过滤> --pg`和本地前端入口；隔离PG55435，不操作既有环境。其他WMS/MES模块、无关联对象的独立部门绑定、持久operation/财务投递、正式workflow/CRM和真实DEV/UAT仍独立。无Actions/真实tenant授权/生产部署。

实现、集中作者审查及上述相关验证已完成；单份[DELIVERY](../../evidence/WP02-03/operations-owner/DELIVERY.md)。10处本轮自有PG生成data累计回收1152009755逻辑字节（1.073GiB）；两个清理窗口实测D盘空闲增加合计1157378048字节（1.078GiB），跨两窗口及中间测试的实际空闲净变化1040855040字节。正常PR及原任务生命周期收尾待执行。
