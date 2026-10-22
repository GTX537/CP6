# W44-01：库存调整/移动原操作恢复

WP02-10 / 七天一期D4限定窗口；COORD本聊天单人。Main基线`3ed818471dad9fd68b4b49971de72b20886c9caf`，DEV-1 `codex/w44-01`、DEV-2 `codex/w44-handoff`；预计实际1–2h，按真实finish修正。W43实际34.94分钟且已完成分支清理/席位释放，W37–W43不重复。产品和Handoff已采用，原分支最后文档及实际finish见下文。

## 改动与一次集中审查

StockMovementService的新版本独立命令使用固定operationId。既有实际PG事务包含Stock单腿/Move双腿、流水、采番、前后库存审计和不可变原结果；同键同体不重复扣增、异体拒绝。GET恢复应用只读，当前query/原actor和全部精确Stock/Transaction ID的部门/仓库/区域范围共同控制披露；新写权限撤销不阻合法查询。同业务键新对象不能借旧结果。UNKNOWN不是回滚证明；显式close与执行共用现有权限根，关闭先提交则晚到命令拒绝，执行先提交则只能回原结果。

复用W43 Main journal和W38实际库存Owner，无新EF模型/迁移或新锁命名空间。原权限/输入精度/库存冻结与父操作逃生标志边界保留；写重放仍消耗技术权限根revision，GET/scope不写该revision。已知提交后才调用原best-effort桥/通知，原结果回放不重发。实际HTTP新路径和客户端API已采用；现有页面没有apply/move调用，未声称页面采用。

一次作者完整SPEC/QUALITY审查覆盖事务/两腿/序号回滚、当前授权与原键身份、按原ID披露、codec范围、close真实竞争、CSRF/no-store、旧入口兼容和客户端不自动重发。发现通知测试在可吞异常的callback内断言，改在callback外核验提交观察结果并定向复验；生产代码无需因此修改。START准备记录复制到新assignment的旧W43验证/生命周期字段已清除，旧事实保留在历史，当前字段只记录W44。无剩余合并阻断、无审查代理。

合同：[stock original v1](../../../../design-handbook/remediation/stock-original-operation-v1.md)。

## 实际验证

38项去重相关.NET通过（10项新增PG/真实HTTP、28项既有库存/Owner边界），3项客户端Vitest及完整vue-tsc -b通过。明细与真实来源`D:/CP6-local-test/wave44/foundation/final-validation.json`；全部本地隔离PG18，实际API Program/登录/CSRF。

|批次|结果与有效范围|
|---|---|
|stock-original-20261022T064927Z|10/10：准确原输入冲突、调整/MOVE只生效一次、两腿/序号/日志全回滚、最终权限失败、COMMIT确认丢失与当前只读恢复、写撤销、原对象ID替换/其他actor/tenant隔离、同键并发、close先/后真实根锁竞争、实际HTTP新命令/CSRF/失效会话|
|regression-20261022T065247Z|28/28：StocktakeDashboardOwner/Boundaries、OperationsOwnerBoundaries/Stock四个明确源文件；盘点冻结/精度、字段/范围、双腿失败、历史接口/实际HTTP、原权限根与撤权、提交后通知。相同产品产物no-build|
|notification-final|1/1；仅增强测试观察断言后复验该行为；并入去重10项，不累计成11项|
|frontend.log|3/3；原键透传、UNKNOWN不自动重发、显式close及服务端scope|
|vue-tsc-build.log|完整vue-tsc -b exit0；早先根项目--noEmit空输出不作为完整类型检查依据|

无本窗失败批次；保留所有原始日志/TRX/观察记录。已有NuGet漏洞/nullable等告警未在本窗修改；不作为正式安全发布证明。未跑全仓/浏览器E2E/真实DEV-UAT/生产，不虚报。

## 剩余与生命周期

此为既有库存Owner的原结果采用，不完成正式WMS-TRANSFER Root/Claim/capacity/SourceDemand、REP、RMA或SCHED。Finance bridge与通知仍best-effort，确认丢失后可能未发送；原结果不证明外部处理，不主动重发。可靠Outbox/实际消费者与正式角色/环境/保留政策仍另窗。部门映射没有自动继承到新流水；缺真实映射时原结果当前披露拒绝。旧无键HTTP/父Owner原语不回填为可恢复操作。

自有PG/API停止；3个生成data按ownership/绝对边界/无进程/无重解析点核验回收346049312字节，日志/TRX/ownership及可复用缓存保留。当前原DEV-1/2待正常PR采用与finish，INTEGRATION-1空闲，无新增目录。七天目标ACTIVE，D4仍PARTIALLY_ADOPTED；完整任务、125目标及247运行验收不提升。无Actions触发/取消/工作流变更、既有环境迁移或生产部署；根用户资料保留。

## 远端采用与原分支收尾

[产品PR #399](https://github.com/GTX537/CP6/pull/399)正常合并，source`acdfcafccb942da4eedef059d31973feafee1902`、远端main`1fc7a349af49f013f8792ab333ba23b82ea8b6d8`，19项任务文件blob一致。[Handoff PR #20](https://github.com/GTX537/CP6.AI-Handoff/pull/20)采用source`75c832473a21fe7c0dd74f3c04c9a036f777a4d1`、目标`13bc845c445095603878ef2a560559af0b02e6bf`，3项资料blob一致；准确Handoff本地/远端分支已删除，DEV-2干净detached并释放。

38项去重相关.NET（10新增PG/实际HTTP、28既有库存/Owner边界）、3项客户端Vitest及完整类型检查通过。产品输入合并未变，复用真实成功结果；最后文档仅diff/链接/台账检查。自有PG/API停止，3处生成data回收346049312字节，日志/TRX/ownership保留。本任务分支Actions0运行，无取消项/触发修改。

此快照 2026-10-22T07:02:16.933941+00:00 原DEV-1/codex/w44-01仍做最后采用文档；正常最终PR后manage finish，不提前声明自身释放。准确分支处置、工作区状态、席位与实际耗时见最终PR、实时管理登记及`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave44-final-verification.json`。原估1–2h按真实finish修正。

七天目标ACTIVE，D1完成、D2/D4局部采用；继续其余直接前置。库存原结果不是正式Transfer/REP/RMA/SCHED或可靠财务投递，客户端API采用不等于页面恢复。125目标/247运行及整卡不提升，无新树/代理/Actions/既有环境迁移/生产，根用户资料保留。W37–W43已实际收尾不重复。
