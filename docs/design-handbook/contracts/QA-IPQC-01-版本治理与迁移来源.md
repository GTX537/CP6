# QA-IPQC-01 版本治理、来源目录与旧系统迁移

本册说明当前设计采用哪组原件、历史返工解决了什么，以及旧实现只能怎样作为迁移输入。原件中的历史工作安排是资料内容，不是本次执行指令。当前设计接受、本文整理完成和真实业务验收分别记录。

## 1. 当前组合与历史评审

当前是 Recovery-R4 Systemic-R4：46成员ZIP `5a3adbef…`、主文 `c82ef3d5…`、canonical `ec15532b…`。包内manifest保留制作时的独审PENDING/接受NOT_REQUESTED，包外接受和累计/窄审说明承接后续状态；不能拿包内旧状态替代后来的准确接受，也不能改写冻结原件。[46成员manifest](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4f/4fc2ce659a20d968__MANIFEST_RECOVERY_R4_SYSTEMIC.json:1)；当前接受边界见[主册](../modules/QA-IPQC-01.md)。

|阶段|当时结论与后继处理|
|---|---|
|Recovery-R4|RETURN：五SPEC、Owner wire、API/连续案例、核心Quality四态、DDL与载荷未同源。F01局部静态闭合保留。|
|Systemic|RETURN：canonical旧R2指向、QAP内部ref、Decision源身份、F04重开、F06前驱、锁读图与旧入口边界。|
|Systemic-R1|RETURN：抽样seed/rank/Owner原型、全包引用、具体current资源及批准关系不足。保留已关闭的F04/F06/rowversion/复核分权。|
|Systemic-R2|RETURN：Population与PhysicalMembers引用混用、字符串化JSON漏图、current read未绑定锁、批准只证明各行分别存在。|
|Systemic-R3|前述范围关闭，单一P1仍是Approval未与ApprovalEvidence完整语义tuple绑定。|
|Systemic-R4|按R3单一项补17列复合UQ/FK及API/FE同事实门；窄审与累计复审共同支撑最终设计接受。|

逐阶段原文：[R4退回](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67b628c615889832__SR-20261002-C-QA-IPQC-RECOVERY-R4-INDEPENDENT-FULL.md:1)、[Systemic退回](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc4985023611586a__SR-20261002-C-QA-IPQC-RECOVERY-R4-SYSTEMIC-INDEPENDENT-FULL.md:1)、[R1退回](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b3/b36a186688b01f96__SR-20261002-C-QA-IPQC-RECOVERY-R4-SYSTEMIC-R1-INDEPENDENT-FULL.md:1)、[R2退回](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2a/2a40dd66dec4c74e__SR-20261002-C-QA-IPQC-RECOVERY-R4-SYSTEMIC-R2-INDEPENDENT-FULL.md:1)、[R3单项退回](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9553f58b94f743ce__SR-20261002-C-QA-IPQC-RECOVERY-R4-SYSTEMIC-R3-INDEPENDENT-FULL.md:1)、[R4响应表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/90/901e2257c13b868d__REVIEW_RESPONSE_MAP_RECOVERY_R4_SYSTEMIC_R4.md:1)。本次阅读全文并核当前资料，未重跑历史作者validator，也不把历史PASS说成本次执行。

## 2. 引用目录怎样使用

2835个目录条目以 `(owner,id,version)` 唯一绑定digest/body。1307条正文与已读F01 BASE、连续案例、前端或载荷完全相同；其余1528条包括820个逐件物理成员和708个完整业务对象。10组成员按F02–F10各自domain/sourceContext/stage/数量建立，F03/F08各10件，其余100件；成员覆盖 `[i-1,i)`，不能把名称相似的源份额、测量槽、observation、range outcome当同一个身份。[原目录](D:/CP6-archives/consolidation-20261010/objects/90f0f51375c908e46992961b2c166d3188aec361a142c5e6a75d5a4e534773e2.json:1)。

目录含DEMO技术基线、完整性、物理成员、质量规则、原测量、复核、覆盖、批准、QAP、PLM和Stock结果。`synthetic=true`、fixture-only及薄状态对象保留原意义，目录收录不等Owner生产采用。参考示例的DIMENSIONAL-FULL-COUNT、上下界0/10、v7策略/v3计划/v12要求仅是本组DEMO值。

F01仅BASE进入canonical目录；legalVariant在同一文件中作为隔离的替代世界，不能把变体中的同名身份覆盖BASE。`populationRef`证明Population，`membershipCompleteRef`证明PhysicalMembers，seed使用准确memberSet/plan/requirement/scope，二者不可互换。source/mapping/task/resultSet/decision/lease/writer各有独立版本轴。

2835个正文摘要和目录内原键唯一性静态比较为0差；这是所列字节与身份检查，不是全部跨Owner业务关系或数据库外键验收。当前连续样例仍有三项采用前澄清，见[连续案例附册§6](QA-IPQC-01-连续案例附册.md)。

## 3. 范围保留

五原SPEC分别是工序、成品、结果处置、工程产出关联、原始试验与PLM分权；共36 REQ、50业务AC。原P07/P09/P18、H20/H22/H23/H42/H44和DP02/07/11/14/15/16各自保留批准层级。P18是NEW_INTEGRATION_ID_REVIEW，H42/H44是DETAIL_FROM_APPROVED_PLM_REVIEW；其范围表达不能冒充已实现适配或运行采用。14个对象的输入、规则、过程、失败和AC已在范围扩展件逐项给出。[五SPEC追踪](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/18/18234b9b9640e6cf__FIVE_SPEC_TRACE_RECOVERY_R4_SYSTEMIC.json:1)、[14项范围展开](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/30/30fc004df70af6e8__APPROVED_SCOPE_EXPANSION_RECOVERY_R4_SYSTEMIC_R4.json:1)、[正式NEXT](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b8e963915658a58__NEXT-C-QA-IPQC.md:1)。

QA-POL/MES-EXEC/MES-OUTPUT是准确直接前驱，IQC只是同Quality专业机制支撑；采购Scope/GR双投递/SC10不成为制造检验规则。PLM-VER/DOC的RequiredFenced输入要提供有权读取和持久回执能力；未知、无权、不可用分别显示，不借Quality PASS代签工程评价。

## 4. 旧系统差异与迁移约束

来源固定为旧Main `15763059…`，不声称当前仓库仍相同。本轮完整读旧Service450行、DTO83行、Entity65行、Controller87行以及Vue/router指定窗口。[旧实现原窗](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fb695351e66c5ef0__QA-IPQC-01_SOURCE-READING-PACKET.md:1924)。

|旧行为|新设计整合时必须保留的差异|
|---|---|
|InspectionNo/WorkOrderNo/ProcessCd及默认InspectionType=3|不能据此制造canonical source、阶段、策略、冻结Population或cycle；须有准确LegacyMapping。|
|AutoJudge只补空的item.Result；OverallResult仅为空时汇总；显式传入值可保留|不能把旧服务概括为无条件从全部测量重判，也不能把旧客户端判定当新专业批准。|
|Create FAIL同事务自动建D07 DefectRecord；WO状态4且PASS直接改6|新Quality/NCR/MES分别拥有专业处置、缺陷链和工单事实，需独立合同与真实回执。|
|Create提交后best-effort标Stock FAILED，缺服务或异常只记日志|旧检验保存成功不证明Stock应用；新设计须分别保存专业决定、Owner结果、UNKNOWN及恢复。|
|Update删除原明细后替换，当前方法不执行Create中的Defect/WO/Stock后续动作|不能把旧记录更新当不可变Correction/Retest或完整撤回；保留旧历史和差异。|
|模板按当前active值读取，前端自动填completedQty，缺测量只警告仍保存|新任务冻结准确方法/要求/单位/总体与资格，不用最新模板或数量相同替代物理范围证明。|
|旧列表导出当前页面rows；新建/详情均进入旧quality-inspection|切换后旧两个入口只读，导出范围与新的授权cursor/asOf合同区分。|

旧接口是`api/mes/inspections`；Create/Edit分别有mes-quality-inspection add/edit权限点，查询和模板有类级Authorize。新动作须按新Owner/actor/current授权设计实现，旧权限名不自动等价。PREPARE→ACTIVATE→RETIRE以单一WriterEpoch阻双写；旧PASS缺Policy/Scope/Baseline只能只读或隔离。

## 5. 本次实际范围

当前46成员逐项字节/SHA相等；8份历史作者程序仅分类保留，未执行、未冒称逐行审完；5份旧canonical/API/schema模型按历史隔离，不能覆盖当前Systemic。60成员准备包已按成员分类，必要旧实现窗口、原范围及准确已读接受前驱单独处理，不把整包hash当全文阅读。

阅读包18个JSON范围对象、Plan/Handoff及治理说明已读；30个编号原窗与准备包成员逐行相等，其中11个接受主文窗口按团队已完成的准确原件复用。业务测试、SQL/TS编译、前端运行、真实IAM/PLM/Owner采用仍NOT_RUN/UNPROVEN。逐来源范围与比较明细见[治理阅读证据](../evidence/QA-IPQC-01-governance-reading.json)。
