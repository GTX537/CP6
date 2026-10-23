# W77 / 七天一期D6：实际配置保存审计与稳定请求槽共同事务

COORD本聊天单人，2026-10-23T19:32:59.373955+00:00。Main `9b8e3743ed318aed16b08e12aafc8ce0ddcc473b`，CRM `5deac62fc4d00a731b5d2dea79877572dc39a49b`，Handoff `77878b85bdc71d93d93ff8975951879668b4e8ee`。W76 CRM116/Main457/Handoff52全部采用，25独立PG通过，实际17.8min＋前置2min；三准确分支删除、固定树干净detached、全部席位释放、Actions0、自有PG停止保留。W37–W76不重复。

## 前置和限定范围

最新AGENTS/POLICY哈希未变，status/capacity无告警，无重复W77开放PR。W76完整Draft Bundle与W68稳定槽均已远端采用。核对原R3 V15规范、AuditFact列及FX26实际configuration.save审计：ActorId、ResourceId、OccurredAtUtc来自实际保存Bundle；脱敏changes仅bundleId/containsPii=false/operation，不复制告知、内容或权限投影。审计Id及保留期限由可信Owner显式提供，不在组件中冒充批准或推断保留策略。

本窗新增显式PG namespace crm_inquiry_save_audits_v1，只保存configuration.save的不可变Owner局部审计。实际Bundle tenant-qualified FK，唯一每Bundle保存审计；引用实际W68 Org/Actor/RequestKey稳定槽的延迟FK及延迟提交校验，核对原结果ResourceId/ETag，因此允许在W68回调内先写审计后写原结果，但缺槽/错Resource/错ETag不能提交。即使绕过应用直接插入也检查实际Bundle actor/time/脱敏JSON，重复和跨租户失败不能留下部分写集。

应用从数据库重新读取Bundle而非接受伪造Actor、时间或changes；全部UTC时间按原文bytea保存，保留年0000、闰秒和任意小数精度；保留期限仅验证合法UTC且晚于发生时间，不用有界主机时间截断。原请求哈希32字节按原样关联，不保存明文RequestKey。事务RC/Serializable、实际同连接、savepoint故障恢复和无隐式commit/retry。返回不可变事实/副本及待提交说明，不是权限证据或commit receipt。

原configuration:read/write字段需求为空，但当前Site/Form范围不能由身份投影DeptIds代替；真实Owner共同权限仍待接。Platform0.10.3已有OutboxV2，后继投递应复用。原TargetLocalInquiryEvent.time仅允许7位小数且无闰秒，而ConfigurationView时间范围更宽；本窗不截断或修改冻结合同，不冒充事件已投递。Outbox原子入队、当前权限/对象最终门、Preview/Activate与真实批准、公开Attempt/受理及Program/HTTP仍待后继窗口，CanAuthorize/CanActivate/CanIssueAttempt=false。

验证实际Bundle+W68原结果+审计共同提交/回滚/原结果重放零再分配、缺槽/错误Actor/资源/ETag、跨tenant、原生多行原子性、混合隔离竞争/取消、精确时间与保留边界、immutable及schema漂移拒绝。只在新自有PG55445执行定向测试与必要构建，一次作者SPEC/QUALITY；结束停止PG保留证据，无既有环境迁移。

## 耗时和出口

预计实际30–50min：实现12–20、相关PG验证8–14、集中审查/三仓收尾10–16；独立前置约3min，W76收尾内只读不重复计时。Main登记start `2026-10-23T19:31:54.754422+00:00`。沿既有七天持续目标ACTIVE，不替换或结束目标。

DEV-1 Main协调；DEV-2 Handoff START保存/finish deferred后bind CRM，CRM采用/finish后bind回Handoff交付。固定工作目录复用，正常PR、远端采用核对、准确任务分支清理、finish释放，不新增代理/工作树。24/375整卡、125目标0交付、247运行NOT_RUN保持，Issue109仍OPEN；不运行Actions或部署。W51指定停止data删除曾被自动审批以blocked by policy拒绝，保留不重试。

## W77实际交付

CRM [PR117](https://github.com/GTX537/CP6.CRM/pull/117)已采用，source `6ac69e5e33c2ab078ecaef03e256a557728efe65`，远端main `49d06e17bc1d4547ef0d6dd9a934cd1a735ddb3e`，11条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/49d06e17bc1d4547ef0d6dd9a934cd1a735ddb3e/docs/development/evidence/W77-01/DELIVERY.md)。31项独立本轮PG用例通过，无跳过，相关API/必要依赖构建通过；真实Owner业务验收NOT_RUN，无Actions。

新增实际Bundle派生的不可变configuration.save审计，只保存原合同脱敏事实，保实际操作者/创建时间；请求HMAC完整32字节，原时间/保留期限无损UTF8，年0000/闰秒/任意精度/合法小写t及同秒小数边界通过。

即时Bundle FK、延迟稳定槽FK及延迟提交guard核对原201 ResourceId/ETag；允许W68回调先写Bundle/审计、随后写原结果，缺槽/错Actor/资源/ETag不能实际commit。共同回滚、原结果重放不再分配、原生多行原子拒绝、混合隔离/同ID竞争/取消及ALWAYS不可变/漂移拒绝均通过。Serializable原生40001由Owner重建事务，无隐藏重试；存储关联不冒充历史同时创建。

最终31项独立PG通过，初始红灯和两项测试假设失败保留；修正精确时间后旧自有fixture schema正确拒绝漂移，同实例另建隔离r2库重验，不修改旧库/既有业务环境。一次作者SPEC/QUALITY无剩余阻断，PG55445正常停止，新旧数据/日志/TRX保留。CRM准确任务分支删除、固定树干净detached、finish释放。

这仅是局部保存审计与实际事务组合；当前Site/Form权限不能由DeptIds替代。真实Owner最终门/创建与保留批准来源、Outbox原子入队/投递、Preview/Activate/真实批准、公开Attempt/受理和Program/HTTP仍待接。原事件时间比配置时间范围窄，后继不能截断时间冒充兼容；Platform0.10.3已有OutboxV2供复用。合成ProtectedResult测试不表示生产加密采用。Issue109保持OPEN，24/375整卡、125目标0交付、247运行NOT_RUN不提升，七天目标ACTIVE。无新包、Actions、既有环境迁移或部署。W51指定停止data删除被自动审批以blocked by policy拒绝，保留不重试。

Handoff [PR53](https://github.com/GTX537/CP6.AI-Handoff/pull/53)已采用 `4dcb673b7c2722ba831e319af579e52b248307c4`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约16.09min，原预算30–50min，最终以finish为准、子任务不重复累计；前置只读核对约3min。
