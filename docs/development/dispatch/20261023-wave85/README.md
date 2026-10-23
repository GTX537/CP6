# W85 / 七天一期D2：越库历史观察独立Owner与旧写入口围栏

COORD本聊天单人，开工2026-10-23T22:56:48.790339+00:00。Main `25d00d7773d60e448ab8d4c37450ad6f81a3cb31`，CRM `3e69fa188a43d261660fe871103bc2b7b400a674`，Handoff `081c35e68b10f1b0fa42b4315cef84195c196d8c`。W84 Main472/473、Handoff60全部采用，Issue471关闭；35独立相关用例及类型检查通过，实际19.73min＋独立前置2min。准确任务分支删除，固定树干净detached、席位全部释放；4个PG停止保留。W37–W84不重复。

## 最新输入与前置

AGENTS/POLICY哈希未变，Handoff规则完整核对，status/capacity无告警；D盘约208GB可用。固定目录复用，原根用户未跟踪文件和两个非allowlist历史目录不动。无重复开放CrossDock/cross-dock Issue，登记[Issue474](https://github.com/GTX537/CP6/issues/474)。

已读当前[设计手册](../../../design-handbook/modules/WMS-XDOCK-01.md)、[WP10-32](../../task-plans/WP10.md#wp10-32)直接前置，当前原件为433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md，实际读取§1–11/18AC；早期v1.0只为历史，Issue已纠正来源。R2明确分阶段原IN/OUT结果、完整四维数量、未知保留和旧事实。XD-G01–06无运行采用，WP10-32没有获本窗完整开工/完成。

旧CrossDockService读返回整实体且无当前Owner；Create/Cancel直写，Execute仍计划数量直接IN/OUT、空lot生成日期号，与R2来源/原结果/安全停止不符。当前Stock独立门的迟到拒绝不能代替越库边界。页面500条客户端总数和Number数量、缺身份隔离均需修正。

## 本窗实施

1. 在WP02-03窗口接入PostgreSQL当前原会话、独立wms-cross-dock:query、原生权限/业务围栏及最后重核。具名CROSS-DOCK-OWNER/1历史观察DTO、真实授权后计数和稳定服务端分页；计划原数量字符串，不合成实收/已发/当前四量，不将旧状态或单号当原Receipt/Shipment。
2. 自身实际Creator/真实cross-dock UUID部门2–4/可信租户及正式暂存仓库库位范围。当前源入库/出库query和实际对象/产品/仓库相符后才保有相关来源的可见行；无来源旧记录可按自身历史权读取并标UNRESOLVED_SOURCE，缺来源权限/错来源不回退。旧文本dock不推导正式地点或跨区权限。敏感商业/操作字段按当前MIN投影；只返回显式DTO，不整实体。原lot、旧交易引用及状态保留为历史，未核实际腿结果，知识UNKNOWN。
3. 原生部门映射增加cross-dock真实UUID类型和可信对象查找/UI选择，公共配置同窗口串行；PG迁移及启动只登记query不自动授角色。旧PG读取要求新版；Create/Execute/Cancel在各自当前动作和对象门后明确CROSS_DOCK_OWNER_ADOPTION_UNPROVEN，任何编号、Stock、头状态效果之前拒绝。不能仅凭Planned推未执行安全取消，也不修成第三套直接Stock两腿。非PG历史原范围保留，无数据迁移或原文覆盖。
4. 真实HTTP/no-store及错误，页面只有受权历史观察/详情，具名响应、精确数量、当前权限/epoch/乱序/失败/筛选改变清数据；清楚说明新作业尚不可用，保持原有其他物流页面。未知/旧状态不显示真实已收或已发完成。

## 执行估时与门禁

预计35–55min：实现20–30、相关原生PG/页面验证10–15、一次集中审查与采用/收尾5–10；独立前置约2min，W84收尾重叠只读不重复计。七天目标ACTIVE。

先真实PG复现旧服务缺Owner读/写；验证tenant/self/部门2–4/仓位/来源、字段/精度/超500分页、旧半成功事实不被取消或再执行、零编号/库存效果、实际当前撤权/最终失败、Program/login/HTTP和页面迟到。新部门类型做真实IAM绑定/错误租户/原生FK守卫；只运行相关历史物流及种子，不全仓，不触发Actions。

DEV-1 `codex/bug-474-crossdock-owner`产品/总控，DEV-2 `codex/w85-handoff`通信；单人无代理、新树。自有PG55435按序启停保留。正常PR合并、远端精确核对、准确分支删除、finish释放。24/375整卡、125目标0完整交付、247正式运行NOT_RUN不提升。完整XDOCK/原结果恢复、库存财务及CRM真实Site/Form Owner/公开受理继续对应任务。W51指定停止data删除自动审批blocked by policy拒绝，原地保留、不重试。

## W85验证完成

18独立.NET用例（15原生PG、2既有非PG越库行为、1两租户启动种子）及21Vue用例通过，无跳过；相关构建和vue-tsc通过。一次集中审查闭合；部门类型直接消费者api/sys/iam.ts及原组件实际选择/保存回归同窗纳入。旧写零效果、原生双连接读围栏、真实HTTP、503条分页通过；2个实际PG停止保留。进入正常采用与同任务收尾，七天ACTIVE。
