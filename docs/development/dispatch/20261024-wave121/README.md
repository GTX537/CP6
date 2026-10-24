# W121 原生询盘人工放行与拒绝独立 Owner

COORD本聊天单人；七天持续目标ACTIVE。W120 Main PR532、CRM PR146、Handoff PR96已精确远端采用、准确分支清理和官方finish；47.65分钟实际，原估110–170。本轮不重复已完成交付。

## 前置与范围

最新AGENTS、POLICY、Main/CRM主线、总台账、Handoff与官方status已核对；固定席位干净，保留原缓存和用户资料。W117完整公开提交/H01原图、W119人员上下文完整来源绑定和W120原生读取均已采用；原R3V15 Spec/OpenAPI/授权/ReasonRequest/命令槽和事件合同已交叉核对。完整WP03-02/03和正式验收不因限定窗口完成。

原POST /api/crm/v1/intake/submissions/{submissionId}/release|reject独立写Owner，原会话和CSRF、锁外新鲜人员HTTP取证；最终身份七锁→持久信任→稳定命令槽→资源行锁，重查完整当前来源、精确site/form、supervisor/manageddepartment、query/view-pii/add或edit与六字段。冻结If-Match/Idempotency-Key/ReasonRequest、返回200IntakeSubmission和准确ETag；不信任浏览器权限DTO。

实现同事务H01完整Lead/LeadHistory与原Submission状态/历史、当前ConsentFact、原bundle/calendar/SLA/合法Unicode、原保留规则；拒绝不写Lead。原图封印按既有profile无损重封，PII到期/撤回与当前权限检查先于原结果解密。稳定槽排除payloadhash，同槽同载荷返回保存原结果零副作用；变化冲突、真实并发胜者、提交前回滚和未知提交恢复明确覆盖。

现有crm_inquiry_submission_v1.Commands仅允许submission.received/201，Effects每资源唯一，不可复用作人工决策。新增版本化PostgreSQL决策原结果/审计/效果账及显式安装入口，保留tenant约束、完整性/不可变/保留期和原生Outbox绑定；不改旧账为另一语义。检查v4目录约束，不能靠删除原门禁容纳新FK/触发器。新增schema仅为存储版本，不等于租户隔离；既有TenantId与授权不删。安装只在本轮自有隔离新PG，既有环境迁移/安装未授权。必要EmbeddedResource项目登记在本轮串行窗口，不新增包/恢复自动工作流。

HTTP/BFF显式开关默认关闭、严格方法/路径/正文/响应/会话CSRF边界；已开启写Owner才可按当前完整权限显示可用动作。页面、隐私/保留worker和真实Outbox发布者后继，不冒充当前采用。

## 任务与实际执行估计

|任务|预计实际执行|
|---|---|
|W121-02 决策原结果/效果账及PostgreSQL保护|25–40分钟|
|W121-02 当前人工写Owner、放行H01与拒绝事务|25–40分钟|
|W121-02 HTTP/BFF及PG并发/故障/重放定向验证|20–35分钟|
|W121-01/HANDOFF 一次集中审查、台账同步及三仓收尾|10–15分钟|

合计80–130分钟，登记起点`2026-10-24T20:49:53.419236+00:00`，各仓不叠加。PostgreSQL唯一新增数据库，精简相关本地验证与一次任务级审查，实际风险包括权限/租户/PII/幂等/并发/事务。无Actions、分支保护变更或生产部署。

- [x] 稳定决策命令槽、原结果和效果原子存储。
- [x] 原会话当前权限下H01放行/拒绝及完整图保存。
- [x] 实际HTTP/BFF、并发和故障恢复定向回归及集中审查。
- [ ] 三仓远端采用、准确分支清理和官方finish。

正式24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY不变。

## 实际交付

CRM显式开启原生询盘人工放行/拒绝Owner，原会话和CSRF、锁外新鲜人员上下文、最终身份七锁→持久信任→稳定槽→原资源行锁，复核完整当前来源、精确site/form、主管/管理部门、query/view-pii/add或edit与六字段。Serializable同事务写完整H01 Lead/History或拒绝、原Submission状态/历史/加密原因、Consent处置保留期、受保护原200结果/H01命令/回执、审计与正式Outbox。保持原收到时间、日历、SLA及合法Unicode；拒绝无Lead。当前权限/撤回/PII期限先于重放；公开原201重试仅接受账本证明的版本转移。

CRM [PR147](https://github.com/GTX537/CP6.CRM/pull/147)采用 `0c9e9a74b6b15ec2572f8f594e14f018703fe867`，源 `5c8c7b2d96bbd0c642f5777aab9c40d539b92c48`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/0c9e9a74b6b15ec2572f8f594e14f018703fe867/docs/development/evidence/W121-02/DELIVERY.md)。68项唯一相关本地成功检查：24项PostgreSQL/HTTP、44项Web；实际H01/拒绝图、幂等/CAS/并发、真实HTTP响应丢失、晚期回滚/期限/撤回、实际Node BFF、公开原操作重试与存储保护覆盖，必需0跳过。 本地证据 D:/CP6-local-test/wave121/foundation/final-validation.json。W119实际Main人员HTTP签发按未变输入复用，不计本轮新执行；本轮TLS身份夹具与生产验收分开。真实失败保留，最终使用新的自有PG实例安装最终DDL验证，初版库保留且停止，没有迁移任何既有环境。

新增版本化crm_inquiry_decision_v1显式扩展与唯一SQL EmbeddedResource，无包/工作流改变；原v4图/旧合同门禁不变。抹除保护和延迟完整性检查保留。原生Outbox route保持Blocked/unassigned，无实际投递声明。默认IntakeWritesEnabled=false；allowedActions保守为空，页面/动作展示后继。

自有PG均停止、无新junction，缓存/失败记录/用户文件保留，无Actions、保护变更、既有环境安装/迁移或部署。Main本轮仅协调文档。页面、隐私/保留worker及实际Outbox发布者后继；正式24/375、0/125、247NOT_RUN不变，D6PARTIALLY_ADOPTED，七天ACTIVE。
Handoff [PR97](https://github.com/GTX537/CP6.AI-Handoff/pull/97)采用 `e30af151185e316255233a275ff5d12bf49a857a`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟42.12分钟，原估80–130分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。

本提交时Main最终合并和席位收尾待；以官方finish与final_proof作最终证明，下轮统一核销，不额外创建记账分支。
