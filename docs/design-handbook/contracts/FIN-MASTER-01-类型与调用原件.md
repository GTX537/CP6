# FIN-MASTER-01 类型、原件与调用链补充设计

本册补齐主册附录B的 **167个JSON Schema定义**及附录C的 **390份不可变原件、239段调用/反例**。全部字段约束、原件业务字段和调用的输入、输出、操作者、前驱、引用与拒绝分支已经读完；重复对象按逐值相等复用，变化字段全部阅读。与主册、接受文件及准确补包合读。原件状态仍是 `DEMO / DOCUMENT_EXAMPLES_NOT_BUSINESS_EXECUTION / NOT_RUN`，本册不把示例中的“commit”写成实际系统执行结果。[附录B](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:1372) [附录C声明](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:9154)

## 1. 如何查阅与复核

先看[模块主册](D:/CP6/docs/CP6_开发设计文档_20261010/modules/FIN-MASTER-01.md)理解业务，再按[全部类型和239案例索引](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/FIN-MASTER-01-类型与案例索引.md)跳到准确原文行。索引中的条目均属于已读语义范围，示例实例仍保留在原归档。

本次紧凑阅读用同类型的完整基例加**所有递归差异**，包括字段删除；`@REF:Rn`指向第n份原件的准确五字段引用，`@BODY:Rn`表示与该原件body逐值相等。UUID/摘要只改成可逆符号，不改业务字符串、状态、数值或时间。390条原件的类型前缀摘要核对无差异；将390条原件与239条case从紧凑表示完整还原，与原JSON逐值相等，0差异。该检查只证明整理没有丢字段，不证明API、锁、事务或权限实现通过。[阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/FIN-MASTER-01-supplement-reading.json) [还原检查](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/FIN-MASTER-01-compact-reuse-validation.json)

## 2. 线类型必须保持的边界

|类型/对象|实现约束|
|---|---|
|Key、Id、Hash|Key为1–128字符；Id是约定小写UUID格式；Hash是64位小写十六进制。外部Owner的id/version仍保持其原定义，不把全部外域ID强转UUID。|
|Utc、Date、Interval|Utc是24字符含毫秒UTC格式；Date是10字符日期；业务区间为左闭右开，`toDateExclusive:null`是无穷上界。日期格式校验与业务日/专业校验都要保留。|
|Rev与BpRev|FM本地Rev是1至9007199254740991的整数；MAIN-BP控制版本是正十进制字符串，最大9223372036854775807。不能先过JavaScript Number再转回字符串。|
|OwnerRevision|按Owner读取正确版本域；MAIN-BP使用BpRev，其他声明的Rev保持数值。SourceRef.version与Owner控制水位是两条轴，不能互换。|
|Dec6|税率为六位小数字符串，容量0.000000–999.999999；0.100000是比例10%。容量不是允许税率；专业依据决定合法率、方向、用途和可抵扣性。|
|封闭对象与union|按定义的required与additionalProperties执行；oneOf分支按明确kind/state选择。显式null、字段不存在和遮罩值具有不同含义。|
|四类内容|GL、Tax、SupplierBank、CostCenter各保留自己的字段和规则，不能用自由JSON绕过封闭类型。Tax.allowedUses是非空受限集合；银行草稿Secret为KEEP准确Ref或REPLACE完整内容。|
|银行原件|公开Version存SecretRef/commitment和核验引用；秘密原值单独保护。演示账号、ZZ/XXX、DEMO核验均不代表真实可付款账户或专业批准。|

BpRev边界样例覆盖2^53−1、2^53、2^53+1及Int64最大值；BK42、BR04把高位字符串贯穿依赖观察、预览、发布与原回执查询。不能通过省为null来回避精度问题。[BPR64-1起](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:55877) [BK42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:72371) [BR04](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:72808)

## 3. 一份资料的正常建立与发布

GP先建立GROUP科目，GL再以它为父建立POSTABLE科目；TX建立INPUT税码；CD先建立部门成本中心，CC再建立对应机器中心。每个维护页面都先独立取Options；制作人保存草稿，另一复核人从自己的列表和FM28取准确Proposal，发布人再取自己的FM28头、预览并应用。不能要求复核人从制作人浏览器缓存取得ProposalId，也不能把列表可见当写权限。[GP01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:33828) [GL3W2](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:36170) [CD01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:41047) [CC01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:42690)

银行链先保存秘密和空核验草稿，再由专业Owner核验准确秘密承诺，通过KEEP原Secret修订为带核验证据的版本，随后提交、复核、发布。这样输入不依赖尚未生成的未来核验。BK02R的ProposalRevision推进与Version变化都必须保留。[BK02](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:38936) [BK02R](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:39317)

Version、ProposalRevision、ControlRevision、TimelineRevision和ScopeRevision分别推进。修改activate等批准主体字段，即使内容可以复用同一Version，也必须推进ProposalRevision并清掉旧批准。首次发布可以明确保持SUSPENDED，不能把“有Release”直接映成ENABLED。[KA02R](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:63448) [KA06–KA08](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:64880)

后继发布使用OPEN_SUCCESSOR创建新Proposal，旧已释放Proposal和原回执不可改。预览包含完整before/after、被覆盖段和依赖，`isExecutionPermit=false`；未来ReleaseRef在预览中为null候选，不能伪装成已存在原件。[TX08](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:47954) [TX11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:49362)

## 4. 有限区间替换与暂停恢复

TX的v1税率0.100000原本覆盖[10月1日,∞)。v2的0.200000只替换[10月15日,11月1日)，发布后必须得到三段：旧v1的[10月1日,10月15日)、新v2的[10月15日,11月1日)、旧v1的[11月1日,∞)。11月2日当前业务日读取应选v1；不按最大Version序号选择，也不回写TX07的历史采用。[TX08](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:47954) [TX12](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:49637) [TX13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:49795)

暂停在新的采用前持X门，拒绝新Use，但保留已有UseReceipt和消费者结果。相同原useKey的终态重放返回原结果；历史更正引用标明`isNewUsePermit=false`，审批与更正结果由FIN-GL等原Owner负责。[EV01–EV04](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:46698)

暂停期间仍可按规范发布activate=false的后继版本，Control继续SUSPENDED。恢复必须针对**当前Timeline、其中准确Version集合、当前ControlRevision**重新复核，不能借第一次发布批准，不能从旧暂停回执抄旧Timeline。RS04S故意使用旧Timeline被拒；RS05用独立复核人FM28的新Timeline2，RS06核Control5后推进至6恢复。历史暂停回执继续引用旧Timeline，不随当前头改写。[RS01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:66585) [GL33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:68822) [RS03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:68980) [RS04S–RS06](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:69619)

Role有效区间冲突和同机器成本中心区间冲突可以在预览明确拒绝；制作和专业复核通过不代表发布最终约束已通过。不能只查询当前可见对象判断不存在冲突。[GD05](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:59903) [CM05](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:61396)

## 5. 同事务采用：意图、证明、回执、消费者结果

采用链按GL07P→GL07→GL07C及TX/BK/CC对应三段实现。消费者先在**实际同一个外层事务**暂存完整intent；登记Owner从该事务读取并证明准确subject，FIN-MASTER核subject与所需资源后加入UseReceipt，消费者最终结果再绑定同一intent、subjectDigest、businessEffectKey和UseReceipt，由外层唯一commit。[GL07P–GL07C](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:44360)

subject覆盖Scope、useKey、businessEffectKey、businessDate、currency、purpose及有序完整items；每项准确Root/Version、need、税方向/法域/分类、供应商/受益人/BP引用/subLedger信息都不能省。只证明某张资料本身合法，不能代替对整个业务意图的匹配。UseReceipt不是付款授权，也不是外层业务已提交的证明。[BK07P–BK07C](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:45481) [CC07P–CC07C](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:46097)

IR01改业务日、IR02改币种、IR03换Root/Version，均应被精确subject匹配拒绝。IR04同一个DbContext但不同事务、IR05没有事务，都不能构造同Uow暂存证明；`databaseBinding`字符串相同也无效。IR06所需Machine门缺失时，必须UNPROVEN，不产生正向UseReceipt。[IR01–IR06](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:65320)

锁顺序完整保留为：**IAM/组织/字段/账套/专业依据的前置组按OwnerScopeKey排序 → FM ScopeGuard → 必需BP ScopeGuard → Machine/Organization后置组 → Command → 按kind/id的Root → Proposal/Timeline/Control/UseReceipt/Outbox**。前后置分组按原契约，不把FM说成全系统第一个锁。Machine等后组不得回调FM/BP；BP冻结持自身X门时也不能递归拉FM而形成反序。[总锁顺序](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:82) [禁止递归](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:84) [IR06完整资源计划](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:65725)

## 6. 命令不确定与安全恢复

|情况|准确处理|
|---|---|
|原命令已提交，传输失败|返回OUTCOME_UNKNOWN，使用原commandId/queryOriginal查询；不生成新命令再做一次。|
|有历史读取权，没有release权|可按当前披露权返回原成功回执，不需要重新授予发布权，也不新增Release。|
|历史读取或嵌套原件字段权撤销|RECEIPT_WITHHELD，持久原回执仍保真；不得通过返回其原摘要泄漏隐藏值。|
|NOT_OBSERVED|只说明当前未观察到，`provesPermanentNoEffect=false`；如允许重试也只能同原key/body，不能生成重复资料。|
|旧预览已消费|拒绝新命令使用PREVIEW_ALREADY_USED，不能借新commandId重复发布。|

依据：[RC01–RC04](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:52238) [NG05](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:52097) [PR04](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:66438)。同原键异意图、当前权限和终态重放的完整顺序沿用主册契约；本表不是放宽这些检查。

## 7. BP持久引用与新会话恢复

FM26临时key绑定准确Actor/Tenant/Scope/BP主体、supplier/beneficiary、field/purpose及版本。FM27必须有MAIN-BP可信boundBp上下文；相同请求体从普通UI进入，不能猜主体或往冻结DTO临时塞字段。它只验证基础引用，不付款。[BP01–BP03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:52516)

新会话无需旧key或localStorage：从BP已合法持久的五字段FinanceLink和可信当前主体恢复，FM26R重核当前权限、原版本完整性和BP归属后签发新的临时binding。错误主体、撤权、原摘要未证、当前身份Owner离线分别保留明确拒绝/UNPROVEN分支。[BPR01–BPR07](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:52778)

如果银行后继版本已发布，恢复仍指向原FinanceLink的v2；BPR09可以REBOUND但`enabled=false / REFERENCE_VERSION_CHANGED`，不能偷偷升级到v3，也不能把恢复成功当新采用许可。持久引用、临时定位和当前业务资格必须在代码中分开。[BPR08](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:53726) [BPR09–BPR10](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:55726)

## 8. 异步查询中的时点观察

QueryStart先持久推进generation并设PENDING_UNKNOWN；Q2新来源未证时保持UNKNOWN，Q1旧ALLOW迟到只能历史保存。接管同query必须CAS旧epoch/claim，推进attempt和workerEpoch并换claim；旧worker即使generation正确也不能安装当前头。[Q01–Q05](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:56013) [Q06R–Q07](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:57316)

Q07C捕获完整新SourceCut后，外域先后发生rev4和rev5撤销，而FM本地Scope可能不变。Q08可以安装**被选中的时点观察**，但Q09、QR02、QR03只返回OBSERVED_AS_OF，`observedAt`取源cut捕获时间，`currentEligibility=UNPROVEN`恒保留；离线也不能补成当前许可。FM15新采用另走最终真实资源门。[QR01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:57927) [Q08–Q09](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:57996) [QR02–QR03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:65155)

## 9. 字段披露、导出和旧资料

普通详情的字段分别为VALUE、EMPTY、REDACTED。EMPTY表示确实空值，REDACTED表示不可见，不能互换。隐藏布尔值或有限集合值时，连完整Version/Root/Timeline/Control摘要以及嵌套SourceRef都要受披露控制，避免调用方枚举候选值后比hash。VersionLocator是独立绑定定位，不能作为授权，也不能跨Root/Scope/Actor复用。[BK09](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:61717) [PR01–PR03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:65978) [PR06](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:66538)

仍有历史读权但某嵌套字段被遮罩时，原CommandReceipt和HistoryReference的完整引用闭包也不能直接返回；PR04整回执隐藏、PR05整历史引用不可见。秘密揭示另核当前资格并先成功审计，普通Reader的BK08返回WITHHELD。[BK08](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:58509) [PR04–PR05](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:66438)

导出固定获准全集和字段，CSV保留UTF-8 BOM/CRLF及安全转义，只含maskedAccount。每页/重复下载重新核当前披露权；EX03撤权后没有body。旧Fin_BankAccount的本方资金账户不能映成供应商受益银行，LG01保持原值及遮罩、`canBind=false`，不拿缺失SupplierId补猜受益链。[EX01–EX03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:61860) [LG01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fe3093133f8fa6ca__FIN-MASTER-01_财务基础资料_前后端开发Spec_v1.3_REVIEW_R3_CANDIDATE.md:62029)

## 10. 实施检查与仍未证明的内容

实现应把上述完整case分支转为有关联前驱的自动化测试，重点覆盖半开区间余段、暂停中的后继发布、准确恢复、同Uow资源证明、幂等与不确定恢复、BP大整数、查询乱序/接管以及递归字段披露。不要把本文静态读到的expectedOutput当测试实际结果。

必要材料的语义补读已完成。12项真实采用门、真实IAM与SOD、专业资格/账套、全writer和迁移、秘密存储、银行BP及维度Owner、冻结BP桥、外层事务、恢复竞争与历史报表等仍须按主册逐项证明。补包只在逐行相等证据覆盖的窗口复用；设计归档整理结束不等于这些运行门已经通过。
