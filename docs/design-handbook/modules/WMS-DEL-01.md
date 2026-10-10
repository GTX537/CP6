# WMS-DEL-01 承运与交付证明

整理状态：**core_semantics_consolidated**。主体规则、DTO/API、全部71项AC、147个连续演算对象和181份R-CORE已完成业务字段语义阅读，共享H31必要规范与夹具按准确SHA复用已完成阅读。指定独立R3审查原文仍未定位，故不标 required_materials_consolidated；真实采用与运行验证仍未完成。

## 1. 业务目的、操作者与边界

Shipping人员为真实已发销售Shipment建立承运Attempt，登记提货/在途观察、逐行签收和配送失败；证据管理员上传POD；独立批准者批准证明更正；履约/财务人员查看各Owner应用回执；协调员恢复原请求。业务需要回答“原发哪一份、实际签哪一份、证据是否完整、来源解释是否仍一致、下游用了哪个版本”，不能用一个Delivered头状态替代这些问题。[交付原文L14](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:14>) [交付原文L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:20>) [交付原文L63](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:63>)

DEL拥有Shipment级累计Proof版本流、不可变Fact、Carrier Attempt/Observation及回执观察。任一DEL事务的Stock、Claim、现金和Journal写集为空。只接受已POSTED销售来源；PURCHASE_RETURN和TRANSFER导原Owner，真实退货由RMA/IN，报损由Stock，财务各轴由Finance。DELIVERED_TEXT只是承运观察，不产生Proof或财务完成。[交付原文L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:41>) [交付原文L55](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:55>) [交付原文L61](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:61>) [交付原文L270](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:270>)

## 2. 有效版本、组合与接受边界

当前准确原件为v1.3_R3，23830行、797705字节、SHA256 `c30b6885dde887be7dd1843c444fe95c49f4f7bb490ed03f98404c9673a2f403`。2026-10-01准确接受记录将其认作USER_ACCEPTED_DEVELOPER_SPEC；正文保留候选标签是历史字节，不能据此改选其他稿。接受范围是原六SPEC、H26/H27、DP01/03/06/11；没有D编号决策表，O-DEL只有01–08，不造O09。[准确接受L13](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:13>) [准确接受L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:103>) [准确接受L117](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:117>) [准确接受L300](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:300>)

强制依赖包括已接受OUT R3及H31-RETURN-OUT-R3-BOUNDED-01共同补充CURRENT/SHARED_ACCEPTANCE。DEL-SOURCE-FENCE-1是额外有界共同采用合同，不能当作已部署表/接口或任意修改Stock/OUT旧语义的授权。[交付原文L16](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:16>) [准确接受L169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:169>) [准确接受L285](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:285>)

历史审查记录称C01–06、R1三问题、R2时点问题已关闭；指定独审`DEL_INDEPENDENT_R3_REVIEW_c30b6885.md`应为3461字节、27行、SHA256 `4a30103ef981273ecd9e0d920d4b798e19ca1724850a34413c09a8b25b16621f`，本地未定位，故此处只引用已读接受/最终审查中的转述，不冒称已实读该独审。71AC全NOT_RUN、0业务执行、8个生产门UNPROVEN。[准确接受L84](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:84>) [准确接受L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:103>) [准确接受L182](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:182>)

## 3. 实体、业务键、单位与版本轴

|实体|身份及数据|版本/范围规则|
|---|---|---|
|ProofStream|Tenant、Mode、准确原ShipmentReceiptRef四字段|一原Shipment一流；换运单/HTTP键不新生可交份额|
|SourceBinding|原Receipt/Intent/Stock/Source回执、全部行/包、OrderLine/Schedule/商业快照、逐段映射|不可变历史Binding；当前SourceKnowledge另存|
|Fact|providerAccount＋proofBusinessKey＋businessRevision，或本地受权登记簿身份|同义回原，异义冲突；发生时刻与首次登记时刻分别保留|
|ProofVersion/ActiveClaim|revision、previousRef、完整累计事实断言、supersededClaimRefs、批准Ref|首版1/null，后继+1；不以delta累加|
|Attempt/Observation|attemptKey、原stream和准确行范围、provider/account/tracking证据；callback按account/eventKey|同范围一个活跃尝试；运输号不是业务唯一键；旧观察不回退控制头|
|CorrectionApproval|完整proposalDigest、旧ProofRef、提议/批准人、epoch/expiry、消费operation|批准人不同于提议人；只能被准确一次操作消费|
|Permit/Work|原operation/bodyHash、全部movement fence、effect heads、sourceSet/cut、四权限控制epoch|Work revision、workerEpoch、Proof revision和fence epoch不可互换|
|FinanceAxis/Disposition观察|Owner签发的稳定key、连续revision/previousRef、结果/逆转引用|财务轴、物流证明和处置完成互不替代|

shipment-local区间[lo,hi)只在同原line内比较，以该行基本UOM计；SourceBinding给local→Stock Root及订单域的准确映射，不能假定坐标数字相等。Q为非负固定8位字符串，SQL decimal(28,8)，范围严格正；与Stock的decimal(21,8)并非同存储类型，适配必须守源范围上限。At为UTC毫秒，Ref完整四字段；Stock原ReceiptRef仍只有三字段，不硬塞digest。[交付原文L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:41>) [交付原文L47](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:47>) [交付原文L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:99>) [交付原文L177](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:177>) [交付原文L15477](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15477>)

D=DELIVERED并集、R=REJECTED、S=SHORT，三者无交叠；U=历史发运域−D−R−S。OpenDelivery=历史域−D，拒/短仍未履约。E是OUT当前有效域，若D⊄E保历史D并标SOURCE_DIVERGENCE，不能裁剪D或用0填未知。新事实可选U或经明确retry disposition恢复的R/S；更正才能有据改D。[交付原文L49](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:49>) [交付原文L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:51>) [交付原文L15216](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15216>)

DEL-CANON-1按Unicode codepoint排序对象键、NFC、无空白，数组顺序保语义，输入先校有序/无交叠。Ref只承诺精确immutable core，不承诺可变/遮蔽View整体摘要；Fact canonicalDigest=hash(FactInput)，Fact.ref还覆盖recordedAt等core；Event.semanticDigest排除运输eventId和自身字段。[交付原文L15389](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15389>) [交付原文L15414](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15414>)

## 4. 六条业务流程、状态与写集

提货：context→sources/search→bind→selection→Attempt CREATED，再凭真实交接证据登记PICKED_UP。只有已POSTED原Shipment可进入；建Attempt不是已提货，超时原request/attemptKey查询，不能让DEL调用OUT commit补发。[交付原文L70](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:70>) [交付原文L270](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:270>) [交付原文L15430](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15430>)

在途：只接已采用provider/account签名身份。可靠序列缺口保WAITING_PREDECESSOR并补准确前驱；provider无序列时明示UNORDERED、相关字段null，不靠receivedAt伪全序。迟到IN_TRANSIT只追加历史，DELIVERED_TEXT生成缺POD待办。[交付原文L74](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:74>) [交付原文L273](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:273>)

签收：一次同Shipment最多200行，服务器生成下一完整累计Proof。发30签20得到D20/U10、PARTIAL且coverage COMPLETE；再签剩10，v2是D30，不能把v1的20+v2的30算50。另一运单重签原20仍OVERLAP。第二行越界时全Proof无新事实。[交付原文L275](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:275>) [交付原文L15179](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15179>)

失败：已签20其余10失败只作用余10，Proof20保留。重派需永久关闭旧Attempt控制、有权custody/批准，新Attempt仍属于原Shipment且只余10；实际退回/损失先持久DispositionWork/原Owner请求，再等RMA＋IN或Stock正式证据。RECEIVED/DRAFT不等COMPLETED，下落未知不能显示退库。[交付原文L83](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:83>) [交付原文L278](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:278>) [交付原文L15381](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15381>)

POD：隔离上传→服务端大小/hash/MIME/扫描→Evidence READY→Proof事务pin/link。对象与DB不伪原子：对象已存DB失败按uploadKey+objectVersion补登记；未清洁文件不READY；Proof失败而READY对象暂存按正式政策保留，先核pin/legalHold再清理。文件替换新增Version，不能覆盖已签原件。[交付原文L87](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:87>) [交付原文L281](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:281>)

更正：独立批准完整proposal/旧Proof后，DP02替换完整断言；保留旧Fact只能用其原断言或有权精确子范围，不能借旧Fact声称新签范围。newFact全部断言必须逐项进入replacement；批准后头已变则412、新proposal，不能套旧批准。真实退回不作负签收。[交付原文L283](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:283>) [交付原文L15208](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15208>)

|命令|完整本域写集|明确没有的写入|
|---|---|---|
|DA01/DO01|Request/Operation、Attempt覆盖或Observation/Inbox、允许时控制头、Audit/Result|Proof、Stock|
|DP01/DP02|Fact、ProofVersion/claims、LineCoverage、ProofHead、ProofEvent/Consumer Outbox、EvidencePin、Request结果、Audit、Permit COMMITTED/Decision；纠正另消费批准|Stock/Claim、Sales、Finance业务结果|
|DF01/DF02|Failure、范围控制终止或DispositionWork/Owner请求/Outbox、允许的新Attempt、结果审计|不代RMA/Stock签终态，不回滚Proof|
|FA/DR回执|Owner原文版本/当前观察头/Core/SourceKnowledge/审计|Proof/Stock/Invoice/Journal|
|Refresh/Replay|原恢复工作、来源知识或原消息派送记录|不重物流、不再更正Stock|

所有metadata及事实写集依原文5.3和增补，不因有一个共同服务类就扩权。[交付原文L262](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:262>) [交付原文L15440](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15440>)

## 5. API、H26/H27与Owner合同

public根`/api/wms/delivery/v1`，internal根`/internal/wms/delivery/v1`。对象closed schema且nullable必须显式null；可信Tenant/actor来自会话。主要入口如下，增补优先于较早简表。[交付原文L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:99>) [交付原文L179](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:179>)

|用途|入口|结果和边界|
|---|---|---|
|上下文/待办/来源|GET /context；POST /queue/query、/sources/search、/sources/bind、/sources/selection|真实全部来源，选择器admissionGrant=false|
|Attempt/观察|POST /attempts、/observations、/observations/recover；GET /attempts/{id}、/observations/{id}|不可变事实与当前控制分开|
|Proof/更正|POST /proofs、/proofs/query、/correction-proposals、/correction-approvals、/corrections|需要完整source/证据/批准，202不代表已签|
|失败处置|POST /failures、/dispositions；GET /failures/{id}、/dispositions/{id}|逐准确范围，Owner终态另查|
|证据|POST /uploads；PUT /uploads/{id}/content；POST /uploads/finalize、/evidence/access、/exports|无第三方URL上传；下载代理每次鉴权|
|恢复|POST /operations/lookup、/operations/resume、/sources/refresh|原请求/phase/body，NOT_OBSERVED不推无效果|
|OUT适配|internal POST /read-delivery|完全保持OUT.DeliveryObservation形状，不加Finance/current fields|
|应用|internal POST /events/query、/applications/receipts；POST /applications/query、/applications/replay|各Owner自己的业务键和原payloadHash|
|历史原件|internal POST /source-bindings/read-exact、/facts/read-exact、/proofs/read-exact、/evidence/cores/read-exact、/entity-cores/read-exact|完整原immutable bytes/digest；历史FOUND不称当前完整|
|隔离恢复|GET /quarantined-evidence/{id}；POST /quarantined-evidence/lookup、/quarantined-evidence/resume|新授权后继保原FactInput，不改现场事实|

详细DTO和补充资源读口见原文；业务结果必须可由准确Ref读回。[交付原文L183](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:183>) [交付原文L223](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:223>) [交付原文L15333](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15333>) [交付原文L15412](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15412>) [交付原文L15463](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15463>)

H26消费者按`(owner,shipment canonical,proof revision,eventType,mode)`原键消费全快照，ApplyDeliveryProof/QueryDeliveryProof通过各Owner采用记录映射真实路由。Owner同事务存key/hash/raw/inbox和自己的结果/receipt；+1/previousRef连续应用，v3先到不能跳v2。同一业务Fact换HTTP键回原Proof提交，不能生新revision。ERP按原OrderLine/Schedule替换同Shipment项，再跨批聚合，不再耗Claim。[交付原文L285](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:285>) [交付原文L15214](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15214>) [交付原文L15337](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15337>)

OUT ReadDelivery保原字段shipmentRef/deliveryProofRef/deliveredLines/state/observedAt/coverage/quantityEffect=false；30发20签是PARTIAL+COMPLETE，COMPLETE指证据切面完整，不指交齐。来源冲突返回UNKNOWN及可读lastConfirmed事实，不能给0。[交付原文L290](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:290>)

H27财务稳定轴key含Owner、原Shipment、OrderLine/Schedule、axis/eventType/valuationLineage；FIN_AR负责Billing/Invoice/Revenue/Cash及收入应收Journal，FIN_INV负责COGS和库存价值Journal。同名Journal两个Owner不能合并。Proof新版本不自动新造金额；APPLIED仅采用物流版本，仍不等Invoice/Journal POSTED。[交付原文L287](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:287>) [交付原文L15355](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15355>) [交付原文L15495](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15495>)

## 6. DEL-SOURCE-FENCE-1、幂等与恢复

**限定适用路由**是DEL有效Proof准入/相应当前组合读取，以及影响这些原Movement解释的Stock更正wrapper、OUT更正关联/有效head和准入知识变更。这里要求Stock/OUT/DEL同CP6数据库、同物理Connection、同SERIALIZABLE DbTransaction，并验证TransactionId/ConnectionId；不是把其他业务一律改为SERIALIZABLE。跨连接/库仅历史或隔离证据登记，不支持本合同REAL有效Proof。[交付原文L15270](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15270>)

持久F键=(Tenant,Mode,原StockMovement完整Ref)。全部参与路径外层先取F0完整Movement集合，再K1原Canonical→S2 Scope祖先→S3 Source→E4效果→D5 DEL头/permit→L6范围/证据→O7消息审计；各rank内canonical序。F0不能从已持K1的旧内层回头补；未加入返回ADOPTION_FENCE_NOT_JOINED。Stock原数量/批准/SourceClosure语义保留，只同commit推进fence epoch；OUT完整关联同理，回滚不升。[交付原文L15276](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15276>) [交付原文L15278](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15278>)

Prepare permit保存完整fenceVector、effect heads、sourceSet/cut、原bodyHash/previousProof、scope/privacy/adoption/writer epochs与expiry。最终事务重新持F0，通过Stock/OUT本地锁内reader捕获新的完整cut，核epoch/set/heads及全部当前准入。final snapshot必须有新readKey/ref/capturedAt；集合可相等，不要求final snapshot等prepare snapshot。随后Fact/Proof/覆盖/Outbox/OperationResult/Permit COMMITTED/Decision同commit，外部只在commit后可见。[交付原文L15290](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15290>) [交付原文L15473](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15473>)

Source先赢则permit旧epoch失效，Proof无新事实，另metadata事务记WAITING_SOURCE；DEL先赢则Proof作为当时合法事实保留，后来Stock更正照常发生，新读必须发现冲突。close/revoke/expire与commit用同F0+permit锁裁决，COMMITTED永远回原结果；NOT_OBSERVED不证明跨Owner无效果。[交付原文L15298](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15298>) [交付原文L15320](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15320>) [交付原文L15444](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15444>)

现场证据已具保存资格而来源未知/冲突：DP01持久Quarantine/原请求/证据pin，但不建Fact/Proof/Delivery Outbox，不报已签。DG07是明确有权后继，允许换准确新Binding/expectedRevision/previousRef，FactInput和业务canonical保持原字节。最终Proof与Quarantine LINKED、两请求结果同事务；已LINKED换resume键也只回原resolution。发生12:00、首次Fact/Proof登记12:20必须分别保存，不借用另分支12:00的Fact。[交付原文L15450](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15450>) [交付原文L15465](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15465>) [交付原文L15541](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15541>)

当前来源查询同F0取完整Stock/OUT/Proof cut；旧cut补齐仅APPLIED_AS_OF，新当前另取fresh。历史exact读不需要重新准入，不以当前UNKNOWN否认过去原件。跨OwnerACK丢按原key/hash查/已采用幂等重派；Owner NOT_OBSERVED只某检查时点未见，不新业务身份。[交付原文L300](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:300>) [交付原文L15333](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15333>) [交付原文L15353](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15353>) [交付原文L15438](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15438>)

Finance完整性另用DEL-FINANCE-AXIS-DISCOVERY-1：每次当前页面向FIN_AR、FIN_INV各取新CAPTURE_CURRENT全职责/所有lineage集合，再exact读取每expectedHead；缺政策UNDETERMINED，不能冒N/A。N/A必须同本次readKey/asOf的有权证据且无历史tracked head；已POSTED/REVERSED轴不能从集合消失。补旧vector只APPLIED_AS_OF，页面新当前再做两Owner discovery；各Owner完整vector不是跨Owner同一原子时点。[交付原文L15491](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15491>) [交付原文L15512](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15512>) [交付原文L15516](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15516>) [交付原文L15535](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15535>)

## 7. 权限、证据安全与隔离

区分Proof登记/纠正/批准、quantity.read、evidence metadata/view/download/export、恢复、财务读权；批准不能自批。PII由正式隐私Owner token化，不进入通用Event/Outbox/日志/指标，不用姓名裸SHA冒匿名化。摘要/文件名/受保护Ref也受内容权控制，不给存在性探针。不可见404，对可见对象无动作权403。[交付原文L304](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:304>)

上传首批PDF/JPEG/PNG、20MiB、单Fact/Proof最多20证据等参数须O-DEL03真实采用。每次下载代理核audience/epoch/scope，长下载中止点重核，不能追回已合法交出的字节；异步导出生成与领取重鉴权。真实保留期不臆定，需policyRef/legalHold/pin；DEMO7天仅样本参数。REAL/DEMO分Tenant/存储/队列/endpoint audience/密钥域，不只是UI旗标。[交付原文L88](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:88>) [交付原文L306](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:306>) [交付原文L310](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:310>)

## 8. 页面与服务器校验

八页：工作台、提货建立、在途、签收、失败、POD/纠正、应用、恢复。工作台列历史发量/有效量及cut/D/R/S/U、Carrier时间、Proof版本、Sales及六财务轴。未知显示最后确认与时点，不0。签收确认页显示旧累计/新增/新累计和全部受影响行；412显示差异，不能自动覆盖。更正按钮明确不办理退货。[交付原文L63](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:63>)

DS03用有权当前快照计算可选`E−D−activeAttemptCoverage−未经处置R/S`；当前未交E−D仅D⊆E且完整才有值，历史未证H−D−R−S另列。任何知识/权限不全相关ranges=null且禁用，不以[]假无剩余。选择器仍不是许可，最终F0门重核。[交付原文L15430](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15430>)

列表后端稳定游标，20/50/100，固定queryDigest/scopeEpoch/readSnapshotRef；不把一页条数当总数。Tracking精确匹配，不能用不可见字段搜索存在性；超过1MiB收窄筛选或另查行详情，不截断后报COMPLETE。Proof最多200行、每行100段，不切开同原子纠正审批求部分成功。[交付原文L65](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:65>) [交付原文L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:99>) [交付原文L15222](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15222>) [交付原文L15428](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15428>)

## 9. 实现整合顺序与旧代码差距

整理建议：先落准确OUT SourceBinding/历史exact读和共同F0 wrapper注册；再Evidence/Fact/Proof/Permit原子模型；随后隔离恢复与Attempt/失败处置；接H26原键消费者与H27完整轴发现；最后单Writer迁移和真实并发证据。此顺序来自上述依赖，未替用户决定新业务政策。[交付原文L225](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:225>) [交付原文L15270](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15270>) [交付原文L15450](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15450>)

原文固定SHA `157630594e3371fe181955d2f6227ff3b6962c84` 的旧Carrier服务是手动mock，缺Tracking会生成随机号，Delivered只改头，AddEvent只追加JSON；缺真实源行/POD/Owner回执。旧RowVersion、Tenant机制已存在，不能写成全无锁/隔离。本轮没有核当前代码运行态。[交付原文L18](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:18>)

迁移按Tenant/site/原Shipment集合冻结旧事件字节/rowVersion、核源与证据、建立LegacyMap并切唯一Writer；只有头状态历史只读“未核POD”，坏JSON保PARSE_ERROR不能空数组。NEW已有Proof或消息后只前滚，不删除新事实退旧/双写。[交付原文L336](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:336>)

## 10. 验收设计和NOT_RUN

71AC覆盖六SPEC及增补。核心断言：30签20+10全快照一次；重复Fact换key无第二版；第二行失败整组零Proof；双人批准与旧头412；Proof/Outbox/Permit同commit；源先赢/DEL先赢/关闭竞争；Stock fresh final cut不能包旧snapshot；隔离恢复一次LINKED；Finance漏新Revenue/lineage能发现、N/A有据、旧POSTED不复活REVERSED；早期discovery不引用未来observedAt。[交付原文L344](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:344>) [交付原文L15434](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15434>) [交付原文L15524](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15524>) [交付原文L15539](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15539>)

所有AC均NOT_RUN。历史静态文书记录与本轮独立静态核对分别保留：本轮149个JSON表示还原、181个core摘要、147演算对象中的1343处完整Ref定位和12个Fact/Event派生摘要均无差异；没有执行作者校验器或业务程序，不能据此证明真实承运/财务链。[准确接受L182](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:182>)

## 11. 尚未确认和阅读缺口

O-DEL01–08均需真实采用：Shipping份额/签收/纠正政策，OUT来源/当前cut适配，隐私/证据/保留，ERP H26、Finance H27，carrier身份/签名/完整查询，RMA/IN/Stock处置读，旧新writer清单。缺采用按原文明确WAITING/BLOCKED/历史读，不自动扩大Owner写权。共同F0须全部相关writer加入，不能一个入口声明同UoW就放开全链。[交付原文L26](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:26>) [准确接受L169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:169>)

指定27行独审原件R01071仍未定位：3461字节、SHA256 4a30103ef981273ecd9e0d920d4b798e19ca1724850a34413c09a8b25b16621f。原主正文、准确接受及最终审查记录完整在库，不能拿它们的PASS转述替代独审原文。当前已定位必要材料无剩余语义阅读队列；此独审缺件独立阻止强制组合关闭。

## 12. 来源与实际阅读

operations已完整读取主原件1–407、15177–15600、23828–23830以及接受305行和最终审查298行；本轮补读408–15176与15601–23827全部业务字段、非JSON说明及R-CORE，另读INDEX12行与当前接受入口CURRENT327行。147例及181个core的876处完整相同子树按结构和值精确复用，其余字段和值全部实读；不把这一过程称作23830行逐字重读。共享H31规范/三长夹具及共同接受的准确来源与既有阅读证据逐项保留。

[连续演算与不可变原件附册](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/WMS-DEL-01-连续演算与不可变原件附册.md)按来源分批、更正、F0 fresh cut、Finance全集、隔离恢复与core存储整理具体开发规则；[补读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/WMS-DEL-01-supplement-reading.json)记录精确行段、JSON指针、复用来源、静态核对及独审缺件。语义阅读闭合不代表真实采用或可上线。
