# W245-01 排程操作链与原 D3 出口

W245排程操作链收口：UTC微秒拖拽草稿/确认、准确独立操作/计划/预览/队列/工作入口、原工作只读核对、当前能力和Owner链接；原七天D3限定出口核销。

**生产业务验收可以使用模拟数据。** 本轮沿用明确模拟的工单/工程/资源/执行状态、身份和来源资料，实际运行隔离本地 PostgreSQL18、Main HTTP宿主及Vue/路由组件。模拟输入不等于模拟业务执行；本范围业务验收通过。生产部署未执行，既有环境未迁移。

## 本轮实际交付

- 看板拖拽只产生本地草稿，按UTC平移并保留六位微秒和原时长；Escape/放弃取消，未提交离开提示。准备前比较原看板五域/操作版本，变化立即失败，不更新预期版本硬提交。完整工单前后变化确认复用已有TIME_ONLY原子提交与原命令恢复。离开或丢失响应保留原命令引用。
- 独立操作、变更计划、排列预览、资源队列、原工作路由实际挂入宿主，保留当前权限。操作链接带准确UUID，工单号重用时拒绝错对象。预览服务端翻页展示全部候选，确认仍提交完整工单组；队列可读当前授权历史。
- TIME_ONLY原工作读取/核对查原不可变计划、application和首次实际命令；后来的同计划回放不替换原命令。未观察到不表示无效果，核对不重新应用。work.read/work.reconcile和preview.read入口独立于query/change.apply；Rebind/批次复用原工作协议。
- 当前能力与执行/历史门在Owner边界计算，页面仅作显示/前置；提交最终权限/业务条件仍由原Owner负责。深入口读取不覆盖已有待核引用；另一个页面写入的新原命令不会被旧批次继续/停止清除。

[接口与页面合同](../../scheduling-board-v1.md)。未新增依赖、数据库迁移、角色授予、内部证明包装或全仓防御重构。

## 原七天 D3 限定出口审计

对照[原七阶段表](../../planning/20261022-seven-day-delivery.md)和[W129原出口审计](../W129-01/DELIVERY.md)，D3要求独立Owner、查询先范围过滤、写入与当前权限同事务、可信租户后台。W129明确不自动附加完整APS/六阶段换机/Finance。W240–244已额外交付相应排程扩展，本轮补齐实际操作链；这些历史交付保留，不将完整产品/发布资格继续当作本期阶段的新增前置。

|原D3出口|实际已采用证据及本轮结论|
|---|---|
|设备/停机Owner、可信新对象归属|[W47](../WP02-03/machine-owner/DELIVERY.md)当前权限/部门/状态和停机事务；[W136](../W136-01/DELIVERY.md)明确部门、Owner生成UUID及绑定/原结果共同事务、撤权/故障回滚、未知恢复。当前MachineService.Creation实际接线存在，旧Create方法不是未完成证明。|
|OEE独立统计/重算、可信后台|[W48](../WP02-03/oee-owner/DELIVERY.md)完整来源先授权、PG来源快照/最终权限、TenantScopeRunner和可信worker；算法仍如实标LEGACY_CALC_V1，完整PERF发布不自动进入本期。|
|计划与达成率读/统计/导出|[W49](../WP02-03/planning-owner/DELIVERY.md)PlanningOrders完整来源交集、查询前范围、KPI和CSV最终权限。相关旧计划8项页面回归本轮通过；旧无Snapshot资料仍LEGACY_READ_ONLY。|
|工作中心/费率实际引用|[W79](../W79-01/DELIVERY.md)实际Owner及当前来源、精确费率编辑同事务；当前WorkCenterService/ProcessCostRateService.Owner接线保留。|
|计划写能力和页面闭环|[W240](../W240-01/DELIVERY.md)准确Root/Snapshot/Occurrence/五域、WO/SCHED共同事务与原回执；[W241](../W241-01/DELIVERY.md)完整Root排列批次/未知恢复；[W242](../W242-01/DELIVERY.md)资源队列；[W243](../W243-01/DELIVERY.md)换机阶段；[W244](../W244-01/DELIVERY.md)统一看板。本轮补独立详情/草稿确认及原工作核对。旧协议入口继续拒绝未采用历史数据，实际新入口已可用，不算缺少新协议。|

以上十个既有交付final proof均PASS，Main采用提交重新核对为本轮基线祖先，准确分支/席位收尾已完成，记录`wave245-d3-audit.json`。本轮未修改Machine/OEE/Achievement/WorkCenter/费率行为，原有效证据按其实际范围复用，未宣称重新运行这些历史测试。**本轮正常合并并finish后，原D3限定出口完成。** 全WP08整卡、36项正式产品验收、真实环境推广另列，不因此改PASS；也不把这些额外范围反向阻塞本期D3。

## 验证与集中审查

12 distinct PostgreSQL18/actual HTTP scenarios PASS (9 board + 3 work/capability/current-authorization);64 distinct affected Vue/router scenarios PASS;app vue-tsc PASS. Original D3 evidence reused from W47/48/49/79/136/240–244 with adopted-main ancestry verified. Explicit simulated production business inputs; actual PostgreSQL, API, and component code. No Actions or production deployment.

后端3项新场景验证：原回执/首次命令稳定与只读核对；当前权限、精确Owner链接及原工作撤范围404；实际HTTP独立work/preview权限、撤权403和只读核对不增效果。原9项Board场景在W245首轮真实运行并复用。终态来源为`D:/CP6-local-test/wave245/foundation/pg-flow-20261101T182548Z.json`及`pg-context-20261101T183351Z.json`，共12个不同场景零跳过。

前端终态64个不同场景：首轮63项中60通过、3失败；其中两个拖拽夹具使用test-utils错误设置只读MouseEvent属性，一个初次动态模块加载超时。改用真实事件派发并将组件导入放在用例计时之前，受影响4套件29项通过。新增跨页原命令保护后，两套件19通过1失败，原因是旧夹具没有返回实际提交的commandId；修正模拟回执遵守同一服务合同后，最终Arrangement/Detail14项通过。类型检查曾发现测试路由联合类型展开不合法，明确单视图字段后通过。失败日志全部保留，不将它们算通过；未改输入成功结果按套件复用，不累计重跑数。相关实际日志在`D:/CP6-local-test/wave245-web-*.log`，最终类型`wave245-web-final-types-reviewed.json`退出0。

One author task SPEC/QUALITY review: original immutable plan/group/command lookup, tenant/actor/current object visibility, independent work/preview permissions, capability display versus final authority, accurate Owner links, UTC microsecond draft and stale predecessor rejection, whole-root confirmation, paged complete manifest, route/identity response races and original-command preservation. Fixed deep-work read overwriting saved state and cross-tab batch continuation clearing another command; affected behavior regressions passed. Same internal contracts and existing write/recovery protocols reused, no extra qualification wrapper.

未运行全仓回归、浏览器布局、正式生产候选/扫描/签名或生产部署，不声称这些通过。工作流/依赖未变，推送前核当前源与目标签名复用已审查内容；Actions未运行、未取消或修改。

## 剩余和生命周期

本轮合并收尾后原D3限定出口DONE，D1/D3/D4闭环；D2/D5/D6/D7仍需各自原出口审计及余项。完整WP08/APS、36项正式产品验收、125目标与生产部署不据本期阶段关闭而完成。实际环境为隔离本地PostgreSQL18/HTTP/Vue，生产业务验收输入允许明确模拟。

3/7仅表示已关闭阶段数，不能当工程量完成百分比。375正式任务24DONE/2IN_PROGRESS/349PLANNED、125目标/247运行验收原状态不变。下一步：先核对原D5宿主采用的实际剩余与W130–143证据，以模拟身份/服务/传输执行适用集成；原D2/D6/D7按原出口核销，不继续扩成全APS或125目标。

COORD本聊天单人，复用DEV-1/DEV-2，Main基线`80166305934a6716cf40df6e3eb7218f84965aab`；预计实际45–75分钟，最终实际按START至finish计一次，不将预估当已耗时。W244只按最终proof归档一次。此为合并前快照，准确PR、正常采用、分支清理及席位释放以同轮final proof核销，固定目录和缓存保留。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/221)已完整采用并finish；Main待最终核对。本快照距开工23.21分钟。

收尾局部复查：独立换机计划链接遇到已有工作时明确显示原工作；完成后返回会恢复链接中的目标计划，避免空白选择态。`D:/CP6-local-test/wave245-web-rebind-final.log`原9项受影响场景通过，替代此前同套件结果，仍共64个不同页面场景。最后两处纯链接和既有类型内的恢复分支按局部diff检查，复用前述类型结果。
