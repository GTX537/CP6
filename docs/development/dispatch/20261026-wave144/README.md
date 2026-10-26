# W144 原产品主账身份、持久源版本与当前授权读取

COORD 单人继续原七天 D3 的必要 WP04-33 提供者，亦为 D2 主数据缺口的共同前置。W143 按最终证明核销一次，不重复。START 2026-10-26T12:52:25.144172+00:00。DEV-1/Main、DEV-2/Handoff 固定复用。

|任务|预计实际执行|
|---|---|
|原产品身份及不可变源版本，覆盖旧服务与原生 SQL 修改|40–60分钟|
|当前权限、原版本恢复及 PostgreSQL 并发/回滚验证|40–60分钟|
|集中审查、台账、正常 PR 与工作区收尾|20–30分钟|

合计100–150分钟。Main e2e7dc7a49d1c921064d4d21ed8b05c029507386，Handoff 232b4a3d9b23e250bacea8d2e904da9ba0681ad9。四仓远端、AGENTS、席位预检见 wave144-preflight.json；无其他活动任务。

## 原目标、前置和本轮出口

D3 的 accepted MES-SCHED R2 需要真实 Root/Snapshot/Occurrence 和五版本共同事务。当前工单没有这些字段；旧 ProcessStatus/MachineCd 不可代替，计划写入503保护保持。按 WP08-17→WP05-12→WP05-11/20→WP05-03→WP05-02→WP04-33 追溯，先补既有 ProductMaster 的来源提供者。没有把本轮当作排程采用或完整WP04-33。

1. 在现有 PostgreSQL ProductMaster 主账旁保存来源见证，不另建可编辑产品主账。原 UUID/业务代码保持；原生触发器使旧服务、EF及SQL修改均推进持久单调版本，历史内容不可覆盖；删除与跨租户/身份变化保留真实语义，禁止版本ABA。仅覆盖产品头身份/资格原字段，不冒充完整工艺/BOM快照。
2. 当前原登录会话、product 原数据范围与 erp-product:query 参加同一事务。按准确原ID/版本恢复，租户隔离；部门范围只用显式对象绑定。缺来源/权限/版本拒绝，返回明确原业务状态，不推断成熟度或制造许可。
3. 实际服务/API提供身份来源。稳定版本和摘要属于原主账事实；QtyUnit仅原代码，正式版本化UOM/profile、币种及双Owner工程映射仍未证。不得用EA、KNOWN、NOT_APPLICABLE填空，不解除Transfer/REP/RMA/排程门。
4. 新增迁移只在隔离 PostgreSQL 本地验收；覆盖原始更新/删除/回滚/并发、历史恢复、当前撤权与部门映射。原版本证据并非冻结工程基线、制造Snapshot或独立资格。旧产品写入口不在本轮整体Owner改造范围；触发器同步源frontier，不代表它们已完成权限采用。
5. 一次任务级SPEC/QUALITY；一份交付报告，台账和四状态同轮。正常PR合并、远端采用、准确分支清理、finish释放。无Actions、既有环境迁移/生产部署。原七天完整D1–D7出口保持ACTIVE。
