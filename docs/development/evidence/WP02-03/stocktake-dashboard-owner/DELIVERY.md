# W39-01 盘点、独立库存与看板 Owner：单份交付

2026-10-21；COORD本聊天单人，原DEV-1/codex/w39-01，基线`c465d257074d1164b7de07585538761627c24bc3`。[计划](../../../dispatch/20261021-wave39/README.md)、[Owner v1](../../../../design-handbook/remediation/main-stocktake-dashboard-owner-v1.md)、[P0 #386](https://github.com/GTX537/CP6/issues/386)。W38合并/准确分支清理/真实席位释放已核对，不重复。本地Ready to merge Yes；远端采用、分支与席位处置待实际核对。

## 改动与审查

盘点query/add/count/submit/approve/cancel、库存query/adjust/move/历史/流水、WMS与MES所有看板（含SP）采用当前原会话Owner。独立动作、可信tenant、完整字段源、自有完整软删除子图、真实部门映射和仓库区域先于分页/聚合；最终授权贯穿提交。新query目录不自动赋权，读取no-store和HTTP权限错误保持。

盘点所有差异、库存、流水、序号和头明细一笔提交；开始计数在同库存围栏刷新计划快照，审批拒绝过时实体库存。移动双腿原子、外部伪造STOCKTAKE/负库存标志拒绝；已采用父Owner保持专用内部库存原语。合法精度/Creator、阈值/差异及历史非PG行为保留。MES SP保留日期和numeric边界，去除未限权全局连接；WMS日期差使用PG适用实现。

五页面隔离旧身份/请求、冻结意图、防重复和UNKNOWN误重发；保存成功单号/状态不被读回拒绝抹去。PG实时通道改为tenant组零参数InventoryInvalidatedV1，实际数据回到Owner查询；Space仅补新事件合并刷新适配。未采用的BusinessNotification独立管线和SPACE业务Owner不提升。

本轮一次作者SPEC/QUALITY集中审查全部任务差异、独立/内部路径、字段来源、完整图、PG关系/锁顺序、原子回滚/UNKNOWN、所有HTTP接线、目录不赋权、旧父Owner消费者及页面时序。定向修正：开始计数刷新计划快照；审批校验过时库存；SP日期/numeric边界保留；实时明细广播旁路；新目录/锚点对应旧测试合同；上次成功后下一次UNKNOWN锁定；确认状态读回失败保留；列表重查清空旧行。相关复查通过，无本窗剩余合并阻断，无代理或重复全仓审查。

## 真实验证与失败保留

123项去重相关.NET成功：14项本轮原生PG/实际HTTP、38项既有Owner PG/HTTP、71项库存兼容/权限；68项Vitest（10本轮页面、17既有Owner页面、41列表组件）和完整vue-tsc --build通过。最终适用集合0失败/跳过。逐项来源`D:/CP6-local-test/wave39/foundation/final-validation.json`，两份.NET实际记录按精确类/方法筛选，后记录覆盖适用旧结果；不是123项一次全量重跑。68页面包含复用17项未改变的W37/W38页面成功结果。仅本地，未触发Actions。

|记录|结果/适用范围|
|---|---|
|stocktake-dashboard-red-20261021T235207Z|2项真实失败：原生PG缺Owner仍可读取/写库存；原故障已在后续通过|
|stocktake-dashboard-first-20261021T235934Z|2/2通过|
|stocktake-dashboard-behavior-20261022T000148Z|6/6通过，首批盘点/库存/仓库字段行为|
|stocktake-dashboard-transport-20261022T000824Z|58通过、5失败、1未执行；只复用其中50项精确Owner用例（12本轮、38既有）。误选5项Space因外部artifacts相对文档路径缺失失败、1项Tenancy因专属夹具未启用跳过，均保留原结果，不计入通过|
|stocktake-dashboard-focused-20261022T001732Z|73/73通过：2项新增冻结raw并发/实际告警历史，71项兼容及权限目录/锚点|
|typecheck-first|TS2322：CpFormDialog要求Promise<void>，原回调返回false；改为保留对话框并拒绝失败|
|stocktake-dashboard-pages / pages-final / list-denial-final|26/26初验、10/10最终本轮页面、41/41列表组件；17项未改既有页面证据复用|
|typecheck-build-final / typecheck-usage-final|完整vue-tsc --build退出0；较早root --noEmit未覆盖引用项目，不用作完成依据|

14本轮原生PG/HTTP覆盖缺Owner、整笔盘点/移动第二行故障回滚、精度/实际操作者/冻结、软删除完整self、仓库区域原生键、字段来源/无金额推断、跨tenant、所有MES聚合/SP数据库日期与边界、最终权限失败/真实提交丢失确认、当前动作/旧事务/预加载图拒绝、真实Program登录/CSRF和17读路由/写入/401/403/503、真实Owner下Hub只入tenant组和无业务载荷、raw并发序列及过时快照拒绝、历史/告警/总数在范围内聚合。38既有Owner PG/HTTP只因共享库存原语/围栏变更回归，不重复W38交付。71项旧InMemory库存/盘点/财务桥与权限种子单列。

实际API夹具跳过全量启动初始化；目录/菜单幂等与无自动授权另测。Hub使用SignalR路由替身，未做真实WebSocket端到端；Vitest不冒充浏览器E2E。无SQL Server新增验收、全仓回归、真实DEV/UAT或生产验收。

## 工作区与剩余范围

5处已停止的自有PG生成data核对ownership、绝对边界、无进程/重解析点后回收575457573逻辑字节（0.536GiB），清理时段D盘实际空闲增加578404352字节。保留日志/TRX、原失败、ownership和复用缓存；未动既有数据库或用户资料。

原DEV-1产品、DEV-2资料，INTEGRATION-1空闲，零新树/clone/源码副本/代理。正常PR与远端采用核对后manage finish；候选阶段两开发席位仍占用，不提前宣称释放。根用户未跟踪资料保留。预计实际2–3.5小时、复杂故障余量5小时，最终实际耗时由finish记录核对。

其他WMS/MES独立入口（QC、补充、退货、设备/OEE、计划等）、无订单对象正式部门绑定、采购/财务自身Owner、持久operation/outbox、可靠投递、正式workflow/CRM及真实DEV/UAT仍独立。短表锁跨租户串行化、通知/财务best-effort、无持久重放保护明确保留。完整WP02-03、125目标及247运行验收不提升。无Actions取消/触发/重跑、workflow变更、既有数据库迁移、真实权限授予或生产部署。
