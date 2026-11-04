# W298-01 — 需求集可信撤回与显式再授权

W298：需求集实际Owner可信撤回、原审批应用共control、准确本轮业务委派/BLOCKED显式接续、不可变原结果及实际页面恢复完成本地验收。

基线`afc3cc08a081f2affc3f7033b758e312838169d6`，`codex/w298-01`，COORD单人、DEV1固定目录。属于`CP6-24H-20261104`持续24h目标，不重复七天/W249和前周期。

## 实际行为

- 真实Scope/需求集/Submission控制头与原审批应用共同CAS。先应用保原APPLIED并返回可恢复CONFLICT；先撤回后到决定只记STALE，不生成批准效果。原正文、成员和准确版本不修改，终态回执不改写。
- 用户只提交封闭commandId/reason；实际认证人、本轮业务grant及其准确revision由Owner冻结。独立review-withdraw/review-delegate不默认授角色，TASK委派不能替代。当前需求集Scope/Members及准确成员来源可读，撤回无需正文可写；原EvidenceMode保持，跨模式/跨租户/隐藏来源拒绝。
- 事实、Outbox和原命令同事务，缺任一效果回滚；500个补充平面字符原因无损。BLOCKED后明确新授权追加连续事实，旧正文/键保留；已接续旧命令不再运输，接收端当前control拒绝旧事实抢先。
- 支持送审受理前业务撤回；尚未启动时Workflow明确BLOCKED。新明确接续不篡改旧授权，之后原提交可启动并由新命令撤回。旧轮次可被新轮次替代但不能分叉。
- 现需求集工作台嵌入撤回、独立业务委派与恢复操作。刷新后只查询已保存的原操作标识；存储失败不发送，身份变化清理私有页面。业务已撤回与Workflow运输状态分别显示。
- 三项PG前向迁移只在隔离PG18执行，真实旧提交登记EXISTING_SUBMISSION_ADOPTION来源，已应用结果/未应用提交保留；GET不创建历史或修补缺头。

## 实际验证与审查

11个不同的实际PostgreSQL18场景有成功执行，含认证HTTP、两个连接竞争/回滚、准确授权、前向采用和真实Chrome原结果恢复；22项受影响Vue行为及vue-tsc通过。逐方法复用有效来源，早期失败记录保留。

单一来源索引`D:/CP6-local-test/wave298/foundation/final-validation.json`包含每项实际运行JSON/TRX。新增场景覆盖伪Actor/CSRF、跨租户和成员字段拒绝、准确grant撤销/重授、两连接竞争、缺Outbox回滚、412重取Workflow版本、提交丢答原键恢复、pending intake及旧提交前向采用。三项原需求集批准/来源撤回/原子性回归通过。

真实浏览器：`D:/CP6-local-test/wave298/permission-consumption-set-withdraw-boundaries-20261104T013639Z/requirement-set-withdrawal-browser/`。真实登录/权限、Vue、Main API、PG18，走现有“需求集目录→打开→送审历史”；创建新本轮授权，丢弃实际提交的响应，刷新后查询原结果，数据库确认两条连续事实及旧BLOCKED原件未变。已查看实际恢复截图。内部Owner未模拟。

最初浏览器直达详情URL空白为基线已有路由缺失，已登记[P1 BUG #744](https://github.com/GTX537/CP6/issues/744)，初始失败日志保留；该直达链接未在W298修复，将在W298正常finish后独立分支优先修复，不冒称直达验收通过。

一次任务级集中审查覆盖完整diff、准确需求集/成员来源与EvidenceMode、当前权限/最终fence、apply/withdraw同control、不可变原事实/原命令、BLOCKED准确grant/显式接续、前向迁移、真实页面与运输。内部已验证回执读取直接复用，不再重复wire解析；该调整只复测实际丢答运输/上下文读取。无尚未解决的本窗功能/权限/原子性审查项。

模拟输入为用户、需求成员/集合、审批政策、授权、Workflow与服务注册；实际执行环境为隔离本地PostgreSQL18、Main Program与Chrome。未变oa.app wire/TLS采用已有实现和有效证据，未重复全仓测试。此为模拟业务数据本地业务验收，不是生产部署或跨仓采用。

## 余项、工时与交付状态

原375保持30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标与247正式验收不变。WP12-19仍待版模处置、库位规则等实际Owner采用及整卡核销，本窗不关闭原卡。

本轮有效累计采样4849秒，W298从3730秒开始；最终耗时以finish后的`wave298-final-verification.json`为准。先修BUG744，再继续W299；累计7200秒重新估算，完成较快即补原范围真实余项，不把本窗替代三天目标。

本报告提交时Main/Handoff正常远端采用、准确分支删除及官方finish尚待完成。最终证明`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave298-final-verification.json`必须同时核对分支处置、固定工作区干净及席位释放，才宣告本窗完成。

无Actions触发/取消，无workflow/依赖配置变化；push/PR前核对准确引用与已审查事件内容。未部署、未迁移既有数据库，主目录用户未跟踪资料保留。
