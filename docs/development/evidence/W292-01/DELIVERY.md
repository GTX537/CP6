# W292-01 历史任务身份缺证披露

W292：历史任务读面明确身份缺证与准确原FormTo引用，保留原字段/旧链并区分未核实身份；受控LegacyPrincipalMapping迁移设计边界已记录，本轮不执行转换。

Main基线`22978c916a9e656d2bde3a46024da5fddff2b89d`；[原WP12-10](../../task-plans/WP12.md#wp12-10)及原OA-TASK R2 §5.1存量自动替换规则。COORD单人复用DEV1/DEV2，[24小时持续计划](../../planning/20261102-codex-24h-oa.md)继续。本窗实现缺证读面与受控映射设计，不执行迁移或把旧任务转换为v2。

## 已实现的行为

旧引擎建任务时可能把AssigneeId/ExpectedHandlerId改为配送代理，并把原人员写入FormTo.OnBehalfOfId；旧办理又可能覆盖这个字段。旧表没有与任务绑定的不可变原grant revision。当前grant、当前路由重算、两个字段相等、管理员角色，均不能证明当时真实principal或合法代理。

历史信箱的每条已存时间线现在返回准确`formToId`及`actorEvidenceStatus=LEGACY_ACTOR_INCOMPLETE`，保留原expected/actual/onBehalf、意见、时间和轮次。页面显示“历史身份未经核实”和“原记录应办/实办/代办对象”，说明不能作为新版审批凭据；不会把未办理的配送onBehalf称作已代理签票。历史查询沿原访问权限，旧处理链仍独立运行。当前v2信箱/动作Owner不接受这些旧实例，也不把此标记当迁移许可。

这并不声称原历史字段是伪造或无效：它们仍是可见原记录；缺少的是把记录升级为v2职责/授权证明所需的来源。此次没有修改task、FormTo、history或旧grant持久内容，没有新增DDL、猜测principal、补写grant、回填NativeTaskId或新增转换入口。

## LegacyPrincipalMapping受控迁移设计（未执行）

原TASK R2明确“本轮不执行迁移”。未来独立授权任务需要以下完整原证据；本段是设计，不是已实现转换能力或通过的迁移验收。

1. 映射唯一身份为租户/环境/原taskId；冻结instance、准确flow publication pin、token/node/stage/round、原assignee及证实的current/created principal。转交后的current必须以实际转交事实为依据，不能永远强绑最初人。
2. 必须引用原路由/StagePlan的准确不可变快照、唯一对应的原FormTo记录、原配送grant ID及当时revision，以及各来源原bytes的hash。hash只验证给定原材料未变，不能替代来源真实性或证明当时授权。当前可变Wf_FlowDelegate、最新grant、当前组织解析或被办理覆盖的onBehalf不能充当缺失原件。
3. 先只读预览材料及缺失/冲突原因。无准确task关联、多个候选principal、原grant版缺失、pin/槽位不一致、覆盖后无法核证任一出现，结果保持LEGACY_ACTOR_INCOMPLETE；禁止用latest SentAt选一行、现grant回填、空scope通配或管理员默认批准。
4. 真正切换须在独立授权后，由迁移Owner采用当前可信租户/操作者与WF_AUTH共同fence，按命令→instance/task及映射槽位加锁，重核源CAS/全部原hash/当前状态；不可变Mapping与独立switch-fence原件同事务提交。新v2采用方必须要求该完整切换原件。读取设计/预览、运输ACK或单独插Mapping都不构成切换。
5. 原task/history/投票保持原件，不补造当时actual，不把配送事实升级为HUMAN_TASK票。未知提交沿该迁移命令原件恢复，不再次切换；回退也必须有独立受控方案，不能删Mapping后假装从未切换。具体DDL、操作权限、迁移选择集、恢复演练属于届时实现与验收；当前未部署这些能力。

当前无合格原grant版本/准确历史关联的旧数据只能保原链或只读；本窗的明确缺证状态正是这一分支。未来获得原材料也不能靠现有历史信箱自动升级。

## 验证与审查

本地隔离PostgreSQL18/真实Main HTTP旧引擎身份场景通过，另2项既有详情行为通过；1项Vue生命周期/原记录展示行为通过，显式应用vue-tsc --build通过。旧任务v2入口404、无权读403、无原历史改写/无v2投票均实际验证；没有执行迁移或新浏览器到数据库链。

新PG/HTTP场景实际执行旧引擎自动配送，验证旧recipient成为assignee、原FormTo有配送onBehalf且无NativeTaskId；实际登录读取原记录/缺证状态，停用当前grant不会改变历史核证结果；沿旧链办理后onBehalf被原行为覆盖，页面数据仍标缺证。当前v2上下文拒绝旧任务，非参与人旧详情403；前后数据库原记录一致且无v2票。输入/用户/历史定义为明确模拟，PG18、旧引擎、Main登录/HTTP实际运行。

Vue验证未办理/已办理/内容清空三种展示：保原人名、无错误代理签票断言、切换为空后提示消失。真实HTTP与Vue测试分别记录，不称本窗浏览器到数据库完整链。首轮缺状态/缺页面说明的失败、类型检查发现测试字面量推断问题，以及新夹具未登记WF_AUTH导致先返回503的记录均保留；夹具补既有RegisterScope后验证旧任务实际404边界，对应修复只做受影响验证。证据`D:/CP6-local-test/wave292/foundation/final-validation.json`。

一次任务级集中审查覆盖全部产品/测试/文档diff：历史原始字段无损、只读查询不写数据、缺证不被当前grant升级、访问/原生边界、UI不将观察值当核实职责。新增字段是历史读DTO的附加信息，未改oa.app/2.0/WFC原命令合同。无Actions、工作流/依赖变化、全仓验证、既有环境迁移或生产部署。

## 真实剩余与生命周期

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-10及其定义发布前置尚待整卡核销；WP12-04/08原依赖保留。

WP12-10新版授权/撤权已有W277交付，本窗补齐历史缺证标记及上述映射设计。受控迁移执行按原合同留独立授权任务；WP12-10的整卡前置WP12-02仍待核销，WP12-04/08也保持原定义依赖。下一按原卡顺序核对定义发布、个人草稿及原子提交的实际剩余，不为了关卡放宽前置。

总台账/四状态同步，一份报告。Main/Handoff远端采用、精确任务分支处置、工作区与官方finish以`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave292-final-verification.json`最终核销；提交时不提前声明释放。固定目录和缓存保留，用户主目录归档保持原样；24h目标继续。
