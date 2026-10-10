# WMS-TOOL-01 · 版模实物管理

整理状态：核心规则已整理；共用前驱评审、来源与部分依赖仍需集中补读。当前业务验收12项全部NOT_RUN，实际Owner采用及新路由运行均UNPROVEN。本文是开发设计整理，不改变原规范或开始业务实现。

## 1. 业务目的与责任边界

本模块管理一副实际版模从登记、工程关联、使用、维护到停用报废的事实，帮助仓库和生产人员回答“这件实物是什么、已经用了多少、能否继续使用、维修是否真的恢复可用”。覆盖原4项SPEC：实物使用、维护、报废、工程版模关联。[原范围](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:8)

| 事实 | 权威与边界 |
|---|---|
| 实物登记、使用记录、维护周期、报废决定 | Main WMS负责 |
| 工程号、工程版次、适用产品/工艺和发布效力 | ENG负责；WMS只保存准确关联与证据 |
| 已纳入库存实物的数量和真实位置 | Stock负责；搬位引用TRANSFER原操作 |
| 财务资产和报损后果 | 相应Finance/Stock Owner负责，WMS实物停用不等于全部处置完成 |

新增Tool不自动入库存，也不新建工程版本；完成维护不修改工程版次；报废不删除历史或自动核销未闭作业。[权威边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:20)

## 2. 当前输入与接受范围

当前正文为R2，准确SHA由[必需输入索引](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)的WMS-TOOL-01 / R02269定位。它与INK R2.1、VMI/SAMPLE/AUTO/ANALYTICS各R2构成X4当前六册组合，共27 SPEC、95 AC、46任务。正文中的NOT_ACCEPTED、STOPPED是原作者阶段标签，随后2026-10-08 11:22:25 UTC的独立接受记录覆盖其当前静态设计处置，不回改原字节。[组合和接受](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/59/59dff54660e0a2fe__X4-SIX-TARGET-ACCEPTANCE.md:3)

最终审查为R1全范围、R2八根回归、R2.1 INK单接缝核对的累计结论，不是一次重新跑完六册源码及业务AC。所有实际政策、IAM、adapter、全writer、同UoW、工程/财务采用仍Required/UNPROVEN。[累计审查与限定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/58/5847ad3a0d225be4__FINAL-R2.1-INDEPENDENT-REVIEW.md:9)

## 3. 核心数据与稳定身份

| 对象 | 关键字段/约束 | 业务意义 |
|---|---|---|
| Tool | `toolId:Guid`；`plateNo:string25`；种类；Site；客户/产品/位置引用；`cycleShots:int`；`lifetimeShots:Int64字符串`；`cycleNo`；`businessRevision`；Base64 rowVersion | 一物一PlateNo，内部稳定Guid；周期计数与终身累计分开 |
| ToolUse | Tool、cycle、shots、实际/记录双时间、来源发生身份、工程/政策引用、前后计数、operation与audit | 不可由普通资料PUT修改的实际使用事实 |
| Maintenance | cycle、开始/完成事实、结果、政策、状态 | 每物最多一个OPEN，完成事实与reset应用分开 |
| EngineeringLink | 原Owner、`WdPtnNo`、`WdRev`、有效区间、兼容证据、前驱link | 同时最多一个有效关联；新关联不重解释旧Use |
| ToolDiscard | 原因、有权批准、义务闭合引用、实际发生时刻与操作者 | 不可变报废决定，保留其他Owner处置状态 |
| ActualToolObservation | 原发生identity、reportedBody、knowledge、独立application轴 | 留住缺资格时已真实发生的现场事实，不能当使用许可 |

字段全定义与长度见[对象字典](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:44)。`SourceRef={owner,id,version}`保原三元组；人读单号、同产品码或`latest`不能代替准确身份。[引用与位置](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:50)

一次使用的业务去重键为Scope的environment/tenant/site/mode，加`producerOwner/sourceKind/sourceDocumentId/sourceLineId/occurrenceLeafId/action`；sourceKind为WORK_TOOL_USE或WMS_TOOL_ACTUAL，action为TOOL_USE。**recordVersion、HTTP operationId和证据修订不属于这次真实发生身份。** 工单升级版本、换请求键或维护后重报同一叶，都不能再累加一次。[稳定发生域](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:84)

维护观察是另一判别类型，sourceKind=WMS_TOOL_MAINTENANCE，sourceDocumentId=maintenanceId，sourceLineId=RESULT，action=MAINTENANCE_RESULT。无工单使用由实际凭据稳定四元组首次登记actualOccurrenceId；证据无法稳定定位时只能OBSERVATION_ONLY，不能晋升正常Use输入。[发生选择与登记](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:86)

## 4. 正常流程与独立状态轴

Tool历史状态保映射`0 USABLE / 1 MAINTENANCE / 2 LIFE_REACHED / 3 DISCARDED`；当前资格另为ELIGIBLE、RESTRICTED、UNKNOWN，不能覆盖历史状态。Maintenance与Observation还有自己的应用状态。[状态字典](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:44)

| 动作 | 前提和处理 | 持久产物/后态 |
|---|---|---|
| 正常使用 | 按已选准确发生核shots/时刻/Tool/工单一致，最终核工程、位置、政策、版本 | Use、计数变化、审计与回执共同提交；达到/超过max后LIFE_REACHED |
| 开始维护 | USABLE或LIFE_REACHED，说明计划/寿命/损坏原因 | 一条OPEN Maintenance，Tool为MAINTENANCE |
| 完成PASS且允许reset | 真实证据和最终reset政策均成立 | 完成事实及独立ResetReceipt；cycleNo+1，当前计数归0，Lifetime不减 |
| PASS但无reset政策 | 先保真实完成事实，可经观察认定进入 | COMPLETED_PASS_PENDING_RESET；Tool仍维护且受限；后续单独reset应用 |
| REPAIR_REQUIRED | 完成本轮但仍需维修 | 原Maintenance终态保留，Tool继续维护；后续显式前驱新建维修轮次 |
| UNSERVICEABLE | 完成事实表明不可用 | Tool受限，不能reset/普通use；新政策评估维修或独立报废 |
| 报废 | 冻结意图与义务集，核批准和各Owner终态 | DISCARDED与不可变决定；Stock处置可另为PENDING_OWNER |

正常最后一次使用可使计数超过上限，不倒剪真实使用量：900再用200成为1100并达寿命，之后禁止新的普通使用。[Use与维护](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:58)、[R2完成/reset后继](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:96)

已真实发生但政策或来源资格不足时，用户明确选择记录Observation。它不增加计数、不重置周期、不恢复可用；当前标ACTUAL_RECONCILIATION_REQUIRED并阻普通新使用。之后有权人员作不可变RecognitionDecision，再由apply在同发生排他门下生成/关联实际事实。旧周期不清楚时WAITING_EVIDENCE，不能记入今天周期；已有正常Use则只链接原回执。结果APPLIED_ACTUAL_WITH_RESTRICTIONS保留当时AUTHORIZED/UNPROVEN/VIOLATION，不追授历史使用许可。[观察、认定、应用](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:90)

## 5. API、依赖与原字段入口

正文拟议工具API前缀为`/api/wms/specials/v1/tools`；以下均为设计候选，不能据此声称当前Controller已经存在。Scope和operator来自服务端身份。除create外各写入要求If-Match，缺失428；严格拒绝未知字段和任意状态写入。[基本路由与DTO](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:54)

| 类别 | 路由/操作 | 输入或输出重点 |
|---|---|---|
| 查阅 | GET列表、`/{id}`、`/{id}/history`、工程候选、`/operations/{operationId}` | 有权分页、ETag、availableActions、限制原因、准确原结果 |
| 资料 | POST创建、PUT `/{id}/profile` | 封闭可改资料+operationId；不能直接改计数、位置或生命周期 |
| 使用 | POST `/{id}/uses` | occurrenceSelectionRef必填；服务反查固定recordVersion及native refs并核回显 |
| 维护 | POST维护、complete、reset-applications | 结果事实、reset政策、预期cycle明确分开 |
| 工程/报废 | engineering-links、discard | 精确engineeringRef/兼容证据；批准及expectedObligationSetVersion |
| 发生登记 | use/maintenance occurrence options、get、register | 有权原事实，稳定证据身份，PROVEN_ACTUAL或OBSERVATION_ONLY |
| 真实观察 | observations、application-context、recognition-decisions、apply | 互斥reported union、原观察摘要、有权认定决定、预期application revision |

R2新增路由和封闭字段直接见[发生路由](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:86)及[观察路由](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:90)。依赖ENG的精确版次/当前用途门、MASTER的位置资格、Stock/TRANSFER的绑定和搬位结果、IAM范围及字段策略、OA可信批准与Owner应用合同；使用许可仍需工程双方准确pair/round及本次执行资格，不能由单边link代替。[现实采用门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:78)、[X4累计限定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/58/5847ad3a0d225be4__FINAL-R2.1-INDEPENDENT-REVIEW.md:68)

## 6. 事务、幂等、并发与未知提交

Operation唯一域为scope+operationId，保存action、`inputCodec=tool.v1`、canonical输入摘要及快照、状态与原回执。同键异义409；固定字段顺序、原Unicode码点、decimal规范串、UTC时间、缺/空区别及稳定集合排序按原规范，不能套用另一模块摘要算法。server首次生成的编号由原结果绑定，不能因重发而重生成。[Operation规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:66)

本地顺序为scope→toolId→cycle→link→相关来源键；Tool CAS、发生键排他和每物一OPEN均由数据库最终事务保证。相关Owner必须提供同UoW最终资格；只有外部观察时相应新使用RequiredFenced。Use/计数或维护状态、审计、回执一起提交；跨Owner通知通过Outbox但发送成功不等于应用。[事务门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:70)

503 OPERATION_OUTCOME_UNKNOWN返回operationId及同站statusUrl。原结果查询区分FOUND_TERMINAL、PROCESSING、UNKNOWN、NOT_FOUND_OBSERVED；NOT_FOUND不证明已回滚。同键同义再发仍由数据库唯一根裁决，不因超时建立第二效果。历史APPLIED按当前读权取原回执，不重验新写门，不再增加次数。[未知提交](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:68)

## 7. 权限、租户、敏感字段与审计

原逻辑权限区分add/edit/use/maintenance/dispose/del；新增read/history/engineering-link以及actual-record/recognize/apply、maintenance-restore要核实际IAM采用，不能在整理中伪称已存在权限码。所有读取检查Tenant/Site/仓/客户范围；madeCost单独成本可见权。Actor来自真实认证，维护人不能伪造批准人。[基本权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:76)、[认定与恢复权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:92)

审计记录原operation、真实actor、动作、前后版本、准确引用/政策、实际与记录双时间、理由及结果。自由文本不装凭证；导出按当前字段掩码；UNKNOWN和被引用事实不走普通清理。无写权不必抹去仍有当前读权的原成功回执。

## 8. 页面与校验

保留`/wms/plate-mold`页面入口，改服务端分页，默认50、上限100，createdAt降序+toolId稳定排序；总数未知时不能把当前500条当全量。详情六页签分别是资料、工程、使用、维护、报废、原操作。[列表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:26)

plateType必填；colorCount可空或1–12，sizeNote≤40，madeCost可空非负decimal(18,2)字符串，maxShots可空或1–2147483647，remarks≤500。编号、计数、状态、操作者与系统时间只读；普通编辑不能搬位或改工程事实。使用输入shots为正整数、实际发生时间与来源一致。[资料与使用校验](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:28)

维护页同时显示“完成记录”和“恢复可用”；新使用拒绝后不能自动变成已发生观察，必须由用户明确选择。409保输入并解释变更，403清相关缓存，迟到查询不得覆盖较新请求；关闭弹窗不取消已提交事务，重开按原operation恢复。当前资格、历史状态、观察knowledge和application分别展示。[页面状态](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:38)、[R2交互边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:98)

## 9. 实施顺序与既有代码差异

原文在Main固定SHA `90c871fe571fd6b390f53e8678376d7ce60bcb60`上的来源分析指出：旧WMS PlateNo用PLT2，与工程WdPtnNo/WdRev不同；旧维护直接UsedShots=0，缺独立使用/维护事实，旧接口未显式接收预期版本/业务键，旧列表最多500。此为原设计保存的固定源码事实，本轮未重新审计相应全部源码。[旧实现定位](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:10)

按T01数据/迁移映射 → T02使用Handler与发生生产读取 → T03维护/观察/reset/报废 → T04工程适配 → T05页面 → T06权限/写者切换 → T07对应验证文本推进。数据先保旧键、周期与未知终身知识；不能把旧UsedShots直接当Lifetime。旧use/maintenance/discard/delete与新Handler通过WriterBinding/epoch互斥，被引用对象不软删隐藏。[迁移边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:72)、[T01–T07](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:108)

Main当前双库背景要求上述CAS、唯一约束、事务与迁移按实际Provider验证；本册SQL/锁描述或历史三例测试不自动证明新实现双库成立。

## 10. 验收场景与结果边界

| 原AC | 要验证的业务结果 |
|---|---|
| 01 | 新建一件实物，不自动入Stock或建工程版次 |
| 02 | 900+200保1100，达寿命禁后续普通use；缺政策真实观察可记但不变计数 |
| 03 | 有政策PASS只reset一次；无政策保PASS_PENDING_RESET，后续独立reset；维修后继不覆盖原结果 |
| 04 | 未闭义务阻报废终结，停新用与外域处置分别记录 |
| 05 | 原rev1使用事实不随rev2出现改写；不自动换新工程关联 |
| 06 | 同rowVersion竞争一次效果；同发生换key/source version仍唯一 |
| 07 | commit后响应丢失读回同Use；维护后重报旧发生也不重复 |
| 08 | 报废原回执按当前成本读权遮罩，不重新要求新批准 |
| 09 | 旧周期500可保，Lifetime UNKNOWN；旧观察周期未明不乱归今天 |
| 10 | 查询迟到不覆盖新筛选，分页不冒全量 |
| 11 | 关闭提交弹窗不取消事实；UNKNOWN或Observation不显示正常Use成功 |
| 12 | 跨租户/站点、伪actor、无权动作和只读字段写入均无业务效果或存在泄露 |

全部12项仍NOT_RUN；R2新增子例挂原AC，不增加分母。依据[原AC01–12](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:118)和[R2扩充](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a74c705ef05ece0__WMS-TOOL-01_版模实物管理_完整详设_R2_CANDIDATE.md:102)。

## 11. 尚需确认与必要材料闭合

G1生命周期/reset政策、G2工程精确读取和同UoW用途资格、G3位置资格、G4 Stock/TRANSFER、G5实际IAM与writer fence、G6审批Owner链须按动作确认。未具备只阻依赖它的新生产动作，不擅自取消历史读，也不以管理员权限绕过真实来源。

当前正文、全部X4评审/接受/UA/CURRENT、原范围和AC任务身份及适用性已完成阅读或准确同文复用。固定69+3旧代码作为有界历史证据，完整新源码审计未做；ENG兼容依赖的双方集中核对与Provider现实采用仍待完成。

## 12. 来源与真实阅读覆盖

本轮完整阅读：TOOL R2正文1–124；X4接受1–55；最终R2.1审查1–81；ROOT-DECISION-EVIDENCE 1–13；SOURCE-REUSE-AND-ACCEPTANCE-QUALIFICATIONS 1–102；SOURCE-READ-QUALIFICATION 1–17。准确SHA、实际行段与未读件见[root-specialty-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。源文中的发布/执行提示是历史材料，本轮仅整理设计。

补读完成记录及准确同文复用方法见[X4六专项阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X4-六专项阅读与采用边界.md)。这不替代外域依赖的双方集中核对或实际业务验收。

## 必要材料整理结论

当前17项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
