# W304-01 — 原表单提交合法域、原子性与草稿前置核销

原 WP12-03、WP12-04 **均不能关闭**。准确处置到重基的 W303 已经远端采用，原生 v2 草稿和提交链已有大量有效业务证据；本次核对仍找到旧入口适配、准确前快照复制恢复，以及合法大数据输入被命令信封限额截窄的真实余项。原375维持31DONE/2IN_PROGRESS/342PLANNED；原59维持7闭环/52余项。核销指核清实际范围，不表示整卡完成。

本窗沿 CP6-24H-20261104 单人目标执行，基线 `b4d7315d2bbd512b5515c3f2cde3ab6cd5a19d69`。完整读取原 OA-FORM R1 第1–189行，核对原任务卡、设计手册和当前服务/控制器/PG保护/页面；W303 Main755/Handoff280准确采用、分支清理和官方finish已通过 `wave303-final-verification.json`。

## 已交付能力与仍缺实现

| 原范围 | 当前实况 | 剩余或实际来源 |
|---|---|---|
| WP12-03 本人Active草稿、准确pin、partial类型、CAS、墓碑/恢复 | 原生v2已实现 | [W275](../W275-01/DELIVERY.md)实际PG/HTTP；[W278](../W278-01/DELIVERY.md)实际浏览器。删除墓碑恢复不等于重基前快照复制。 |
| 文件/选项/精确数字 | 当前真实Owner已采用 | [W285](../W285-01/DELIVERY.md)、[W287](../W287-01/DELIVERY.md)；[BUG725](https://github.com/GTX537/CP6/issues/725)只提交权限文件准备修正，W287有效证据含其回归。无需等待整个FILE/PRIV或所有其他消费者完成。 |
| 重基准确source/target/digest、可见损失/全部原行/逐行诊断 | 已实现 | [W293](../W293-01/DELIVERY.md)；隐藏损失实际处置与一次消费由[W302](../W302-01/DELIVERY.md)、[W303](../W303-01/DELIVERY.md)补齐。普通盲确认仍拒绝。 |
| 从重基前准确快照恢复为新草稿或明确再次转换 | **未实现完整用户命令** | `PersonalFormOwnerV2.Drafts.cs` 的 RestoreDraftAsync仅从DELETED恢复同草稿；Rebase.cs只选择最新Published，不存在准确history→新draft的恢复入口。保留before_json不等于恢复功能。 |
| 旧Draft/Create/Save/Rebase/Delete端点统一受控适配 | **未实现** | `DraftController`仍调用旧`DraftService`；旧列表包含DataJson，旧Rebase为独立顶层映射，旧Delete直接Remove。新的v2持久表/原生运行写保护不能被说成旧表全部已采用。保留旧success envelope、固定来源映射、缺准确前驱先GET新上下文均待实施；不因新页面停旧发起链接就关闭服务兼容要求。 |
| WP12-04 DIRECT/DRAFT封闭分型、五expected上下文、准确schema关联 | 原生v2已实现 | Submission.cs共同Owner事务、Context.cs准确FORM激活与唯一FLOW绑定；本窗新增五维分别不匹配的两分型拒绝验证。 |
| 实际FormData/instance/task/草稿/receipt/outbox同事务 | 原生v2已实现 | W275已有通知故障与最终授权失败；本窗新增**第一个真实人工任务写入失败**整笔回滚，以及两个不同submit命令争同草稿。失败者不借用胜者命令。 |
| 显式无绑定许可及绑定不存在保护 | 已有同WF_AUTH根门 | Policy.AllowsUnbound显式控制；Resolve读取唯一有效binding，授权根门涵盖激活/绑定变化；W275旧无绑定context遇新binding拒绝。没有将“无binding行”当作无须锁定的许可。 |
| legacy SubmissionKey/RequestHash与WFC原意图固定映射 | **未完成adapter** | 新原生行用`pf:<native-command-row-id>`记录唯一SubmissionKey和原digest；这只是原生v2写入，不是旧SubmissionKey到新slot的持久映射。旧FormSubmissionController/Service仍独立选latest/创建。 |
| data完整合法域 | **发现缺陷** | [Issue #756](https://github.com/GTX537/CP6/issues/756)：data允许1MiB，HTTP Raw/RequestSizeLimit及公共parser却限制整个信封1MiB。当前是已定位静态根因，独立BUG分支执行实际HTTP先红后绿后关闭，不能在本报告冒称已修复。公共深度限制需先核公共合同，不能简单全局放宽。 |
| 普通FORM Service节点真实业务Owner采用 | 未实现能力仍明确拒绝 | RuntimeCapability返回FORM_SERVICE_OWNER_REQUIRED。没有把拒绝当服务执行成功；属于对应运行/Owner采用范围，另行安排，不以本窗假造NoOp补齐。 |

旧数据/真实环境迁移和发布切换仍是独立授权范围。本核对未执行回填、批量转换或清理历史数据；后续adapter必须保持准确来源，无法证明历史pin/实际人时明确拒绝或标未核实，不读当前角色补历史。

## 原AC逐项证据映射

下面只核本两卡直接相关的25条AC（F02六条/F03八条/F04六条/F05五条）。F01目录与F06检索分别由其卡核对，不复制为本窗新增成果。原125/247正式状态不变。已有记录按原来源复用，未声称本窗重新执行；“部分”保留具体未覆盖路径。

| AC | 实际证据或缺口 |
|---|---|
| F02-01 | W275 `PersonalForm_draft_partial_validation_CAS_tombstone_restore_and_original_are_owned`：创建一个本人草稿、准确pin、无实例/通知；v2有证据，旧adapter未完。 |
| F02-02 | 同上及PersonalFormValueTests；W285文件、W287选项。1MiB合法输入缺陷#756未修，不能把小数据正例外推全合法域。 |
| F02-03 | W275跨租户/Owned、W285字段/对象文件准入；源码所有Draft读改使用实际人Owned，代理审批不作为草稿Owner。完整旧链未受此结论覆盖。 |
| F02-04 | W275双连接保存CAS、编辑页面错误/原意图行为；W303隐藏数据不被403空回写。 |
| F02-05 | 新DraftsAsync投影摘要无DataJson，排序updatedUTC/id；详情与附件独立当前授权。旧DraftService.List仍带DataJson，余adapter。 |
| F02-06 | W275 `PersonalForm_context_change_closed_DTO_and_disabled_form_keep_draft_active`；W282退回原实例另路由，未复活已Submitted个人草稿。 |
| F03-01 | W275 bound真实人工任务与准确根/子pin，W278真实页面提交；本窗不同命令同草稿只一原生提交补证。 |
| F03-02 | W275原命令同输入准确回放及不同输入拒绝；本窗恢复全ID且单task/instance/data。旧SubmissionKey adapter未完。 |
| F03-03 | W275封闭DTO和绑定由无到有；本窗五项expected分别不匹配，两种模式均拒绝、草稿原token/data/Active保持。非法body未知字段由JsonUnmappedMemberHandling.Disallow负责，不新增镜像断言。 |
| F03-04 | 本窗真实首task写CHECK故障→全部业务回滚→同意图再提交；W275通知故障/最终scope变化和延迟COMMIT确定失败复用。 |
| F03-05 | W275 unbound实际FormData、无instance/任务，SUBMITTED_UNBOUND不称Approved；原生policy显式放行。 |
| F03-06 | 原生slot含tenant/environment/actual/operation/resource；查询使用当前read与字段/FileRef资格。旧FormSubmissionService仅按历史SubmissionKey查原行，不具有完整新actor映射，**部分，旧链待适配**。 |
| F03-07 | W275 CommitFault原结果与写权撤销后查回；W278实际浏览器丢答恢复；本窗两不同命令同草稿只一成功，另一保持NOT_OBSERVED。 |
| F03-08 | W275原生runtime guard、W282 APP与SFS原入口分流，SFS不接受APP冻结Subject作为个人草稿；业务Owner决定/应用仍独立。 |
| F04-01 | W293准确classification/default/required path，W303混合可见隐藏损失与所有原行保留。 |
| F04-02 | W293/W275明确损失确认、准确preview digest；W303隐藏损失必须独立真实处置Ref。 |
| F04-03 | W293 MaxRows缩小保原行并阻提交；W302/303受控隐藏处置真实HTTP/浏览器，不盲确认、不泄露path/value。 |
| F04-04 | W275预览过期、W293准确source/target；W303源/政策/后发目标/处置期限变化拒绝旧Ref。 |
| F04-05 | W293 requiredAfterRebase/动态规则及逐行诊断；Active可保留无效目标数据，实际Submit重新完整验证。 |
| F04-06 | W275/W303原命令回放保同token/receipt、消费仅一次，无额外任务；前快照复制恢复仍为原§6文本独立缺口。 |
| F05-01 | W275 Owned+Active删除/Submitted不可删，v2保业务数据；旧物理删除adapter未完。 |
| F05-02 | W275真实两连接submit/delete只有一个合法终态，不以本窗submit/submit替代该证据。 |
| F05-03 | W275原删除命令及CAS，不以无行404伪装成功；旧DELETE无同原slot，仍余。 |
| F05-04 | W285实际文件原件/hold/已删草稿拒下载且无连带物理删除；新墓碑服务无purge，未宣称全PRIV清理Owner已完。 |
| F05-05 | 当前PersonalFormEditor源码有明确删除确认、取消直接return，错误交共享原命令处理；未找到完整“取消/失败保行/刷新不复活”组合的实际页面证据，故为**部分**，由W316实际新旧入口验证补齐，不把本次源码阅读算作运行通过。 |

## 直接前置核对

S01-02和S02-00正式DONE；WP12-01/02已整卡DONE（准确定义/发布/激活源可直接采用）。WP02-10、13、14、15整卡仍PLANNED，但当前个人FORM所需的原生PG同事务、当前授权根门、不可变命令审计、文件Owner绑定及字段处置留存已经实际采用，不把其他域连接profile/全域审计/全部清理尚未采用当作本调用链重复准入要求。

具体来源：W275 Main原事务/授权/审计，W285 FORM文件绑定与下载，W302/303当前字段处置政策/留存/hold/准确消费。既有数据迁移、全库租户schema与部署都不由此次前置核对触发。

## 后续实际开发顺序

1. 独立BUG756：合法data限额与必要命令元数据边界，原HTTP复现、边界拒绝、原摘要/恢复保持。预计0.6–1.3小时，低置信，完成后记录实际。
2. W316：原WP12-03/04旧入口逐端点受控适配及固定历史来源/命令映射；缺准确前驱明确返回获取新上下文，禁止服务器伪填期望；同tombstone、原success envelope及实际actor/SubmissionKey隔离。预计1.5–3小时；先核准确源与当前旧服务读写，未核来源不自动迁移。
3. W317：原WP12-03重基前快照到**新草稿**的显式恢复，当前本人/字段/留存/文件资格、准确before原件和两动作链；旧草稿不回写旧schema。预计1–2.5小时。
4. 再按原队列W305历史身份、W306信箱直接前置、W307–309文档/需求集推进；W311–315隐私储备保留，预算末尾W310据实收妥。现发现的新实际余项优先于未开始的储备，不用短窗口结束24小时目标。

时间均是当前低置信工程预测，非已校准概率。下一累计21600秒重新校准，暂停/空等不计、两仓不重复计。


## 本窗执行、审查与生命周期

3项新增实际PostgreSQL18场景通过：真实首人工任务写失败全事务回滚/同意图恢复；两连接不同提交命令只一原生提交和原回执；五expected上下文逐项不匹配在DIRECT/DRAFT均拒绝且保原草稿。原生业务行为未修改，其他有效PG/HTTP/Chrome证据按原窗口复用，本窗未新跑HTTP/浏览器/UI或全仓。

实际来源`D:/CP6-local-test/wave304/foundation/final-validation.json`。首轮3项均未到业务执行：固定55435端口被无关进程的出站连接占用，SocketException明确失败，未伪装为业务通过。保留该失败日志；只为隔离fixture增加CP6_PERMISSION_PG_PORT显式参数，本轮使用已确认空闲45435，不停止无关进程、不更改系统端口范围。默认仍55435，解析错误或端口不可用快速失败，不盲重试。真实人工任务fixture使用明确审批节点，避免用start→end图假充首任务写失败。

第二轮竞争用例的最后一条SQL计数遗漏租户过滤，误把前一个用例的提交计入；改为准确tenant_id后，定向重验两个受影响的原子性场景通过。五上下文用例输入未改，复用第二轮通过记录。全部失败及修正运行均保留在同一验证索引，未把夹具失败归为产品已通过。

COORD一次集中SPEC/QUALITY审查完整分支diff，核原卡25条直接AC与当前代码/证据的真实范围、旧入口/原件恢复未完项和#756独立BUG，确认未提前关闭原卡。新增测试通过真实Owner/PG约束/双连接验证结果与原子性；测试端口显式选择保原默认及失败即停，未改产品/依赖/工作流或机器网络配置。没有未处置的本窗合并阻断项；已登记的原产品余项不冒称修复。

本窗当前累计有效18381秒，开工17732秒；最终耗时以官方finish后采样为准。没有Actions触发/取消、工作流/依赖变化、既有数据库迁移或生产部署。本地仅隔离PG18应用现有迁移，生命周期结束前停止本任务进程。

两仓远端采用、准确任务分支删除、固定工作区干净及官方finish凭据：`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave304-final-verification.json`。该证明PASS前不宣称已释放。原37531DONE/2IN_PROGRESS/342PLANNED，原59余52，125/247正式状态不改。下一独立BUG756，随后W316旧入口适配、W317准确前快照恢复，再续W305及原队列；24h目标持续ACTIVE。
