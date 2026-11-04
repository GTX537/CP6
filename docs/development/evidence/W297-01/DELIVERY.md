# W297-01 — DOC可信撤回与明确重新授权

W297：DOC实际Owner可信撤回、同控制头应用互斥、独立本轮业务委派/BLOCKED明确接续、原结果恢复与运输已完成相关本地验收。

基线`6223e3d7be8f7e20d0fc60941e0f4010865efe55`；任务分支`codex/w297-01`，COORD单对话，DEV1固定席位。属于新24h目标`CP6-24H-20261104`首窗，七天/W249和前周期不重复。

## 实际行为

- 准确Scope/DOC/Submission控制头与原应用共同CAS；先应用保原APPLIED并返回可恢复CONFLICT，先撤回后到批准/撤回决定只记STALE，不生成批准效果。终态回执不改写。
- 用户仅发送封闭commandId/reason，实际认证人、委托人及准确grant revision由真实DOC Owner冻结。独立review-withdraw/review-delegate动作不默认授给角色；TASK授权不能替代。当前对象/字段/准确送审源必须可读，撤回不额外要求正文可写。
- 业务事实、原命令结果、不可变Outbox同事务；500个补充平面字符完整保留。BLOCKED后新明确授权追加连续事实，旧正文/键不变；已接续旧项不再参与运输，当前控制头阻止迟到旧事实抢先生效。
- 业务可先于送审受理撤回；原送审尚未启动时Workflow明确BLOCKED/WORKFLOW_NOT_STARTED，启动后相同原正文可继续。撤回后新送审创建独立控制头，同一旧轮次禁止分叉。
- 真实DOC页面提供本轮委派/撤销、显式再授权及原结果恢复，区分业务状态与Workflow运输状态。缓存仅保存原操作标识；丢响应后刷新查询原键，不制造第二个业务效果。
- 三个前向迁移在隔离PG18验证。旧真实送审/应用明确登记EXISTING_SUBMISSION_ADOPTION来源；GET不创建或修补控制头，缺失立即失败。未对既有数据库运行迁移。

## 验证与集中审查

15个不同的实际PostgreSQL18测试场景有对应成功执行，含真实认证HTTP、两连接竞争、旧提交前向采用、真实Chrome明确再授权/丢响应刷新恢复；21项相关Vue行为及最终vue-tsc通过。复用逐方法有效来源，不把早期失败/未执行改称通过。

单一来源索引：`D:/CP6-local-test/wave297/foundation/final-validation.json`，逐测试链接实际运行JSON/TRX和失败记录。新增场景覆盖真实HTTP拒绝伪Actor/CSRF、跨租户/字段拒绝、精确grant撤销/重授、两连接胜负、缺Outbox回滚、412只重取Workflow版本头、提交丢答原键查询、旧控制头前向采用。相关未变代码复用此前本窗成功执行。

真实浏览器截图/结果：`D:/CP6-local-test/wave297/permission-consumption-doc-withdraw-boundaries-20261104T005511Z/document-withdrawal-browser/`。真实登录、Vue、Main API及PG；只丢弃已真实提交的响应，未模拟内部Owner。后续权限/运输边界改动由对应PG/HTTP回归验证，未重跑同一浏览器场景。

一次任务级集中审查，覆盖完整产品diff、原合法域、当前动作/字段/Scope、不可变事实与原命令原子性、同control竞争、BLOCKED精确grant及接续、前向采用、真实页面恢复与运输。审查修正DOC不应要求正文编辑权限、已接续旧命令不得抢先生效，并精简内部重复序列化/回执验证。修正后只验证对应影响。

模拟范围为用户、文档/文件、政策、授权、Workflow和服务注册输入；执行环境为隔离本地PG18、实际Main Program、真实Vue/Chrome。此为模拟业务数据本地业务验收，不是部署状态。无全仓测试、无生产候选/生产环境验收、无CRM/Platform采用声明。

## 剩余与交付状态

原375保持30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标与247正式验收状态不变。WP12-19仍待需求集、版模处置、库位规则等实际Owner采用及整卡核销；本项不关闭原卡。

本轮有效累计采样3259秒；W297最终实际耗时以两仓finish后的`wave297-final-verification.json`为准，不能把首窗当三天目标完成。下一W298从新的远端主线实施需求集真实Owner。

本报告提交时Main/Handoff正常远端采用、准确分支删除及官方finish仍待完成，最终证明写入`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave297-final-verification.json`。必须同时核对分支处置、固定工作区干净和席位释放，才能声明本窗交付。

Actions未触发/未取消，工作流与依赖配置无变化，push/PR前按准确树复用既有事件审查。未部署，未迁移既有数据库，用户主目录未跟踪资料保留。
