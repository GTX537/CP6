# W80 / 七天一期D6-D7：FX26 配置事件赢家审计因果链修订

COORD本聊天单人，2026-10-23T21:18:23.753660+00:00。Main `244141bb29d12515c3bcec2d390e11002014941d`，CRM `5297a8e641f21b089858078cabe4c11779984aad`，Handoff `7c3fecd3152044582b9adbaac840ba524aa19f59`。最新AGENTS/POLICY哈希未变，status/capacity无容量告警。W79 Main461/462、Handoff55已采用，Issue460已关闭；62相关用例通过，实际50.30min＋前置约6min，准确任务分支删除、固定工作区干净detached、席位释放、Actions0、六个自有PG停止保留。W37–W79不重复。

## 范围和前置

本窗修复 CRM Issue118（P2）：冻结 FX26 的六个成功配置保存/预览/激活事件均把causationid写为自己的eventId，违背原authorization-matrix winnerAudit。W78真实保存事件已从实际审计取得causationid，该实现不代表冻结夹具已经修正。原件、S01固定22输入和原schema版本保留；增加显式选择的版本化派生入口及固定profile，不隐式升级既有loader，不全局替换UUID，不执行归档代码。

从每个成功步骤实际INSERT AuditFact/OutboxFact写集取得赢家关系，核对同租户、资源、操作、时间，再修改这六条事件在FX26所有前后状态/expectedOutbox中的准确副本，仅改causationid与PayloadHash，并重算受影响dbBefore/dbAfter摘要。保留eventId、row identity、业务写集、错误/冲突/重放预期及其它family。旧快照中的携带成功事件需一致修订，但不能把负例改为成功。显式来源、六条赢家源pointer、精确before/hash、派生摘要与相关事件断言一起记录；完整场景仍syntheticOnly/NOT_RUN。

相关验证覆盖原步骤重现、赢家关联、所有副本/摘要一致、重放零新事件、负例/其它family保持、错误来源/版本/篡改拒绝，以及原S01生成检查和实际严格事件解码。仅fixture/派生工具受影响，不改数据库、Owner、权限或发布链，不以本窗关闭完整询盘业务。Issue109独立保留OPEN。

## 耗时和执行

预计实际55–85min：修订实现25–40、相关验证15–25、集中审查及三仓收尾15–20。只读前置约4min，W79收尾内检查不重复计时；Main登记start `2026-10-23T21:17:25.831001+00:00`。既有七天目标ACTIVE，D6/D7直接前置连续推进。

DEV-1 Main协调；DEV-2 Handoff START保存/finish deferred后bind CRM，CRM采用/finish后bind回Handoff交付。只复用固定目录；一个实现任务一个分支 `codex/bug-118-inquiry-causation-fixture`。正常PR、远端采用核对、准确分支清理、manage finish释放。PostgreSQL新增基线保持，本窗无数据库变更或PG启动；不运行Actions、不部署、不增加代理。24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持。W51指定停止data删除的自动审批拒绝（blocked by policy）继续保留，不重试。

## W80实际交付

CRM [PR120](https://github.com/GTX537/CP6.CRM/pull/120)已采用，source `2241b288c8a43a2f0293b0384e8e58d5ed8e224c`，远端main `82d726fbc4ae3d90a0b9cfcbf24b78516829fa17`，14条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/82d726fbc4ae3d90a0b9cfcbf24b78516829fa17/docs/development/evidence/W80-01/DELIVERY.md)。14项相关本地用例通过：8派生工具行为、6现有C#严格解码，无跳过；原S01与显式revision只读校验通过，无PG启动/Actions/部署。

显式CRM-INQ-FX26-CAUSATION/20261023-R1从实际INSERT的六个审计/事件对取得赢家因果；原始准确字节、规范winnerAudit、版本profile和全部携带副本pointer可追溯。修正causationid、PayloadHash与状态摘要，事件身份、负例/原201重放零新增效应和其它family保持；不隐式升级原S01/schema，不复制整份大归档。原步骤红灯保留，修订在现有原生事件解码器接受/往返。CRM Issue118远端采用后关闭，Issue109独立保留OPEN。

一次COORD集中SPEC/QUALITY无阻断；无数据库变更，W78原生Outbox行为按原来源复用，不称本轮PG重跑。CRM准确任务分支本地/远端删除、固定树干净detached、finish释放。本窗仍是夹具合同修订，完整当前Owner、Site/Form最终授权、配置激活、公开受理和真实投递未完成；24/375整卡、125目标0完整交付、247运行NOT_RUN保持，七天目标ACTIVE。

W51指定停止data删除曾被自动审批以blocked by policy拒绝，保留不重试。后继继续剩余WMS独立入口Owner和真实CRM公共基础。

Handoff [PR56](https://github.com/GTX537/CP6.AI-Handoff/pull/56)已采用 `ce5783b5e078e4f9cbeb9b7eec7e120284ea6790`，准确分支删除、固定树干净detached、finish释放。Main协调PR/finish尚待本次收尾。登记开工至此约11.96min，原预算55–85min，最终以finish为准、子任务不重复累计；前置只读约4min。
