# WMS-RMA-01 客户退货实物处理

整理状态：**required_materials_consolidated**。当前准确接受的正文、canonical、API、数据、围栏、AC、追踪、连续案例及必要上游窗口已完成语义整理；完整基例、全部递归差异与同 SHA 原件复用的边界见§12。五种终端处置仍是本地 REQUIRED_FENCED 资格门；52项业务验收 NOT_RUN，实际 Owner 采用 UNPROVEN。

## 1. 目的、操作者与Owner边界

售后申请者选择准确原发运行提交退货申请，独立授权人批准准确可退份额，仓操作员分批实际接收到正式待检位置，Quality出专业结果，商业Owner决定信用/退款授权/替换，Finance分别处理贷项、退款、库存价值、COGS和GL。RMA保存编排、范围、回执链接和恢复，不代各Owner做决定。正常退货保留历史Shipment/Delivery；发100退20仍历史发100、另记退20，原Demand不自动重开，补发需要新Demand/Order/Release谱系。[S原件L9](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:9>) [S原件L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:20>) [S原件L31](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:31>)

原直接接口只有WMS-IN、QA-NCR、ERP-CREDIT；H47到PLM-FBK为有界反馈，FIN-AR/INV/GL是支撑消费/观察，不在本整理扩成新直接业务Owner。Stock仍是唯一数量事实Owner。[S原件L17](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:17>) [S原件L37](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:37>) [S原件L403](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:403>)

## 2. 当前版本、规范优先级及接受

准确接受单位是R3主文（166788字节/955行，SHA256 `dc0ca1dd23d08f92a59d87739c0dda33fc8c6f0e50f7b34d83ff62b5a47b1a82`）和完整review pack（1097956字节，SHA256 `0cc476a9e4147f809921eb916235a4e45c88690aebabb03853884f618c7d3f9f`）。包中canonical/API/data/fenced/examples/source/trace/AC整体受接受，不只一份主文；DRAFT/NOT_ACCEPTED标签按后续准确接受解释。[A原件L9](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6b/6bc33b9183f5e3da__ACCEPTANCE.md:9>) [A原件L19](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6b/6bc33b9183f5e3da__ACCEPTANCE.md:19>)

原规范排序为固定Catalogue/Blueprint/Plan→正式NEXT/分配→准确已接受Owner合同→`WMS-RMA-01_CANONICAL-CONTRACT.json`→派生API/数据/围栏/AC/示例→主文。canonical与派生漂移需有权澄清，不能实现者任选。[S原件L5](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:5>)

R3独审只复核R2三组返工及直接传播：Stock Int64/八kind、五终端Effect Owner边界、P05/P07证据Q09→Q10→Q11；本范围0阻断finding，不代表全量重新业务验收。准确接受明确五终端目标全REQUIRED_FENCED、effectOwner/ownerContractRef=null；WMS-IN未升格为终端MOVE/ADJUST/SHIP Owner。52AC NOT_RUN，运行采用UNPROVEN。[R原件L5](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d0462c3db19828b8__D-WMS-RMA-MD-SPEC-01_R3_INDEPENDENT-STATIC-REVIEW_20261002.md:5>) [R原件L91](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d0462c3db19828b8__D-WMS-RMA-MD-SPEC-01_R3_INDEPENDENT-STATIC-REVIEW_20261002.md:91>) [A原件L29](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6b/6bc33b9183f5e3da__ACCEPTANCE.md:29>) [A原件L44](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6b/6bc33b9183f5e3da__ACCEPTANCE.md:44>)

## 3. 数据、数量、身份和状态

|对象|核心身份/字段|持久约束|
|---|---|---|
|Application/LineFact|applicationId/version、server selection、requestedLines、原履约快照|DRAFT编辑新增完整版本和行集，Head CAS；授权前无ReturnShare|
|AuthorizationDecision|准确Application、APPROVED/REJECTED/REVOKED/EXPIRED、有权人、期限、例外批准|只有APPROVED同事务生成claim和shares|
|ReturnSourceCoordinate|sourceOwner、ShipmentLine owner/id、InvoiceLine或NONE、Schedule或NONE、baseUom code/version|键排除RmaId/Authorization/source version，后者仅资格证据|
|ReturnRangeClaim/Share|原坐标、半开范围、量、申请行/授权、当前状态/前后继|ACTIVE_RESERVED、CONSUMED_BY_RECEIPT、UNKNOWN_HOLD都占额度|
|RmaInboundBinding/PhysicalReceipt|原claim/范围/转换与正式IN/Stock manifest、Receipt/Movement/位置|一实收share唯一Stock结果，RMA字段不塞Stock wire|
|QualityDecision/Partition|准确QA原bytes/ref/version/head；A/R/H/P及独立VOID|四态互斥、并集等有效scope，观察不签专业结果|
|DispositionSlice/Work|服务器派生slice/effect/adapter、准确范围和目标|当前五支只本地qualification，不能伪APPLIED|
|Commercial/Financial/Feedback|原Owner typed result与连续前后继、来源/用途/范围|量、金额、退款、库存价值及改善效果不混为一轴|

这些对象的唯一键和事实/Head分离见数据章；普通旧单号、SKU、随机CreditNote号不能作来源或幂等身份。[S原件L348](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:348>) [S原件L350](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:350>) [S原件L653](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:653>) [C原件L752](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:752>) [C原件L825](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:825>)

数量采用decimal(21,8)及版本UOM，输入十进制文本，不JS float；未知量=null且有knowledge，只有证明零才0。Stock的PositiveInt64/NonNegativeInt64是无前导零JSON字符串，上限9223372036854775807；其中 Stock 的版本/revision/epoch/sequence不得变为 number。RMA 自有 applicationVersion/lineVersion/decisionVersion、Work revision/leaseEpoch 和切换 writerEpoch 等仍按各自 schema 的 JSON integer；不能把 Stock 的字符串要求扩展成所有领域版本统一字符串。Stock ReceiptRef三字段、EvidenceRef四字段保持原wire。[S原件L191](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:191>) [S原件L357](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:357>) [C原件L176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:176>)

九轴是申请、授权、实物、质量、质量集成资格、处置、商业、财务、工程反馈。可同时“已收未检”“商业批准、Finance Pending”“反馈受理、改善未知”。Closed仅本域无未决动作，迟到/更正仍追加并唤醒影响Work。QA readiness的REQUIRED_FENCED不是PENDING/HOLD质量态，VOID不是第五质量态。[S原件L55](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:55>)


事实持久化共43类，采用不可变 Fact 与可 CAS 的 Head 分离：Application/Line、Authorization、原履约快照、EntitlementHead/ReturnRangeClaim、ReturnShare、InboundBinding/PhysicalReceipt、Quality、Disposition、Commercial/Financial、Feedback、CommandBinding/Receipt、Work/Checkpoint、H30 claim/ack、证据/审计和切换登记分别保有自己的身份。新申请版本写完整行集；原坐标不能因 sourceVersion 改变再开额度；已收 `CONSUMED_BY_RECEIPT` 仍占原可退域。DispositionAdapterExecution 当前只能保存本地资格结果，不能用数据库字段占位伪造远端 Stock 结果。[原件L12](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a5/a5a4316bf2aa0d30__WMS-RMA-01_DATA-DICTIONARY.json:12>) [原件L814](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:814>) [原件L3103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:3103>)

## 4. 从申请到各Owner结果的流程与写集

申请C01生成Application v1和LineFacts，C01E仅DRAFT编辑并CAS；C02服务器重新计算正文摘要写SUBMITTED；C02W追加WITHDRAWN，不生成/删除share。C03授权在同源cut核Shipment/Delivery/Invoice/Schedule/商业原件及额度，只有APPROVED生成ReturnRangeClaim+ReturnShare；撤销仅阻未执行份额，已收历史不删。[S原件L210](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:210>) [S原件L348](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:348>)

可退额度以集合而非多个量字段相加：E为当前有权eligible域，A为占用态claims并集，P为本次提案。准入要求`P⊆E`、`P∩A=∅`且`measure(A∪P)≤measure(E)`；remaining=measure(E)−measure(A)。received/disposed/credited是已claim的子事实或独立额度轴，不再与authorized叠加。同原余20两请求15竞争，最多一件成功，另一明确冲突，不能Math.Min/trim成5。[S原件L350](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:350>) [C原件L920](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:920>) [D原件L341](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a5/a5a4316bf2aa0d30__WMS-RMA-01_DATA-DICTIONARY.json:341>)

物理收货必须走WMS-IN-RMA-ADAPTER-1：prepare/commit/query/seal。可沿原真实Root/Movement时用Stock `RETURN/RETURN`，含原Movement、spans和returnQuality；无法沿原Root但为新可追踪来源时用`RECEIPT/RECEIVE`，含新源原因、dimensions、正式LOCATION、qualityBasis。组合混成RECEIPT+RETURN或RETURN+RECEIVE在任何数量事务前400。原Root分支不是错误逆向，新来源分支不借缺谱系任意补量。[S原件L252](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:252>) [S原件L356](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:356>) [C原件L6474](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:6474>)

只有正式databaseBinding/MainUow participant已采用时，Stock Posting、IN ReceiptOutcome、RMA PhysicalReceipt/H30 result及share claim转态同commit；未证明INBOUND_ADOPTION_UNPROVEN，不能改走直调Stock或异步第二方案。FOUND必须原Stock SUCCEEDED+COMMITTED/真实Receipt/Movement；响应丢失查原locator，不再收8；下一真实批12独立一次，两份合20。[S原件L359](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:359>) [S原件L715](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:715>) [S原件L812](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:812>)

质量：只链接/观察QA原任务与结果。A10/R5/H3/P2互斥并集20；遗漏2不得把头置已判，未知保PENDING/覆盖不足；QA后继带previousRef/head/影响范围，RMA保存原bytes，不改专业结果。RMA冻结时QA-NCR资格声明必须与后继准确采用另核，不能把当时“未接受”泛化为当前整个QA-NCR没设计。[S原件L277](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:277>) [S原件L371](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:371>)

终端处置当前只设计目标和资格门：

|选择|固定adapter|目标Stock语义|当前可做|
|---|---|---|---|
|RESALE_STOCK|RMA-DSP-RESALE-MOVE-1|MOVE/MOVE|当前可售资格与每个限制释放证据的目标投影；MOVE本身不使库存可售|
|REPAIR|RMA-DSP-REPAIR-MOVE-1|MOVE/MOVE|内部返修位置目标，product/technical/owner/origin purpose不变，不是返修完成|
|SCRAP|RMA-DSP-SCRAP-ADJUST-LOSS-1|ADJUSTMENT/ADJUST LOSS|准确处置决定和独立Claim释放证据的目标投影|
|SUPPLIER_RETURN|RMA-DSP-SUPPLIER-RETURN-SHIP-1|SHIP/SHIP至EXTERNAL/TRANSIT|退运目标/缺件，不用ADJUST或入向RETURN替代|
|RETURN_TO_CUSTOMER|RMA-DSP-CUSTOMER-RETURN-SHIP-1|SHIP/SHIP至EXTERNAL|返客户目标，generic Stock SHIP不是完整Owner合同|

五支全部REQUIRED_FENCED、Owner=null。Q14签当前proposal，C09只接受proposal/selection/ranges/disposition/option，系统派生slice/qty/quality/effect/adapter；本域事务冻结range claim、Work、outbox、qualification。D01–D05不生成Stock manifest/plan/locator/result/seal proof、不真实调用Owner，也不把无合同推canInferNoEffect=true。HOLD/PENDING只能C09H本域ContinueHoldAnnotation，无终端slice/范围消费。未来必须独立准确接受Owner route/capability/request/result/query/seal/correction合同后再资格，不能自动升级这个结果。[S原件L298](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:298>) [S原件L360](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:360>) [F原件L115](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/52311061f83b95c7__WMS-RMA-01_REQUIRED-FENCED-CONTRACTS.json:115>) [C原件L2046](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:2046>)

商业结果为闭合components union：CREDIT、REFUND_AUTHORIZATION、REPLACEMENT/SUPPLEMENTARY_SHIPMENT、RETURN_ONLY、REJECTED。后两者只单独；退款授权引用同bundle CREDIT且不二耗退货量；信用/替换/补发组合范围不交叠。CREDIT按InvoiceLine量/net/tax/gross/币种，net+tax=gross，同币种、总量精确；RMA只观察。AR_CREDIT/REFUND_CASH绑定商业结果，INVENTORY_VALUE/COGS绑定实收证据，GL绑定其会计来源，不能要求RETURN_ONLY先有贷项才记录价值Pending。[S原件L323](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:323>) [S原件L353](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:353>)

H47以SourceFactId/Version、product/baseline知识、批次/工单、症状影响证据提交；无意义quantity/sourceLine不硬塞PLM请求。同来源版本幂等，PLM ACCEPTED/MERGED不等改善有效，也不关闭NCR/RMA。[S原件L403](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:403>)


五支公开输入为闭合 union：再销售只选 destinationOption，返修选 repairOption 和 destination，报废选 scrapDecisionOption，退供应商选 supplierReturnOption，返客户选 customerReturnOption；共同只交 proposal/selection/ranges/disposition。effectKey、adapter、qualityBasis、Stock intent 和 Owner 身份由服务器派生或按围栏保持缺失。D01 prepare 不冻结 Stock intent；D02 commit-attempt 只返回本地不能执行的观察；D03 查原本地结果；D04 缺 Owner 合同不能出具无效果证明；D05 只记资格观察。八种 Stock JSON 分支在附件中用于类型一致性，不能据此新增 RMA 的 TRANSIT/CONSUME 等业务路由。[原件L1510](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:1510>) [原件L7120](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:7120>) [原件L841](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/561c763d68c1f87a__WMS-RMA-01_API-CATALOG.json:841>) [原件L369](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:369>)

商业与财务分轴的可执行校验包括：CREDIT 每条 InvoiceLine 的范围量精确、net+tax=gross 且同币种；REFUND_AUTHORIZATION 引用同 bundle 的 CREDIT，金额不超其 gross、不得再次消耗退货量；CREDIT/替换/补发的消费域互斥，RETURN_ONLY/REJECTED 各自只能单独出现。库存价值或 COGS 可以基于实收等待成本，commercialRef 可空；贷项/退款则须有相应商业来源。未取到金额时保 null/等待，不能显示0。案例实收20、信用15、net150+tax15=gross165，AR 已记账而现金退款待办时总财务轴仍 PARTIAL。[原件L2192](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:2192>) [原件L2577](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:2577>) [原件L2157](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:2157>)

## 5. API、H30与生产消费契约

public根`/api/wms/rma/v1`，service根`/internal/wms/rma/v1`；命令Idempotency-Key，改头强If-Match，读写no-store。准确type/null/enum以canonical为准，不以主文required字段列表代全部schema。[S原件L428](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:428>) [S原件L652](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:652>)

|用户/服务动作|准确路径组|效果|
|---|---|---|
|空白页和原来源|GET /context；POST /original-fulfilment-options/search|签有权selection，不占量|
|申请|POST /applications；PUT /applications/{id}；POST /applications/{id}/submit、/withdrawals|不可变申请流|
|授权|POST /rmas/{id}/authorization-decisions、/authorization-revocations|原坐标claim/份额|
|收货|POST /rmas/{id}/physical-returns；IN service `/internal/wms/inbound/v1/rma-return-intents/{prepare,commit,query,seal}`；RMA internal /inbound-receipts/apply|唯一IN→Stock路线|
|检验/专业结果|POST /rmas/{id}/inspections；internal /qa-ncr/query、/quality-decisions/observe|有权原件观察/待办|
|处置|POST /rmas/{id}/disposition-proposals、/dispositions、/hold-continuations；internal /disposition-owner-gates/{prepare,commit-attempts,query,seal-attempts,observations}|当前全为本地fenced资格门|
|商业/工程|POST /rmas/{id}/commercial-outcomes、/engineering-feedback；internal /erp-credit/query、/plm-feedback/query、/owner-outcomes/observe|冻结请求，依Owner资格派送/查询|
|更正/恢复|POST /rmas/{id}/corrections、/works/{id}/resume、/commands/query、/effects/query|准确原前驱后继，UNKNOWN不换键|
|证据与审计|POST /evidence/accesses；GET /evidence/accesses/{id}及/content；POST /audit-exports；GET /audit-exports/{id}；POST /audit-exports/{id}/downloads|每次字节交付当前鉴权|
|H30|internal /h30/physical-return-results/query、/h30/consumer-claims、/h30/consumer-acks、/h30/consumer-claims/query|原物理事实生产与有界财务使用|

操作目录给完整request/response/problem及commitKnowledge，202只持久Work，不等物理成功；200也可能原结果重放或纯查询。[S原件L432](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:432>) [S原件L443](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:443>) [S原件L454](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:454>) [S原件L476](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:476>) [S原件L789](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:789>)

H30PhysicalReturnReceipt完整引用RMA、原ShipmentLine/InvoiceLine/Schedule、ReturnShare/claim/ranges、IN/Stock回执/位置/量/初始PENDING质量及纠正链。FIN_AR/INV先取H30RmaRangeGuard，再最终同coordinate锁重核，按consumer/purpose/stableEffect/range建立UseClaim，ACK为RECEIVED或APPLIED各阶段；UNKNOWN_HOLD不释放。支持RETURN_CREDIT_SOURCE、RETURN_VALUE_SOURCE、RETURN_RECONCILIATION，不能拿一个消费者用途占用当全部用途余额。[S原件L372](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:372>) [C原件L8241](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:8241>) [C原件L8418](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:8418>)

QA/Credit/PLM各有自己的六态query：FOUND、STALE_HEAD、NOT_OBSERVED、UNKNOWN、PENDING、REQUIRED_FENCED。只有FOUND有完整typed result/version/digest/currentHead/terminal；非FOUND必须null。Command/Effect/H30查询只有携准确noAdditionalEffectProof的SEALED_NO_EFFECT可推无新增效果；未知不是此分支。[S原件L370](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:370>) [S原件L392](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:392>) [F原件L14](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/52311061f83b95c7__WMS-RMA-01_REQUIRED-FENCED-CONTRACTS.json:14>)


H30 guard 是有界读取结果，并非预先批准提交：消费时重新锁定同一原坐标 Head，校验当前来源、范围和用途后写 UseClaim。ACTIVE/APPLIED/UNKNOWN_HOLD 都占用途范围；ACK=RECEIVED 只表示接收，APPLIED 必须附真正消费者回执。RETURN_CREDIT_SOURCE 的商业/政策质量条件不能由 RETURN_VALUE_SOURCE 的物理+估值基础替代，反向也不能要求纯库存价值先造商业贷项。更正与迟到观察保持原 consumer/purpose/stableEffectKey，未知继续查询，不换键重发。[原件L7831](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:7831>) [原件L8241](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:8241>) [原件L8320](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:8320>) [原件L8512](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:8512>)

## 6. 事务、并发、恢复和锁序待核

授权在SERIALIZABLE下按稳定原坐标取EntitlementHead UPDLOCK+HOLDLOCK，再扫所有occupied segment防phantom，Decision/claim/share/receipt/audit同事务。版本更换不产生新坐标；UNKNOWN或已收范围保持占用，只有有权全路径无效果/释放证明的未消费范围能转RELEASED_BY_SUCCESSOR。[S原件L352](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:352>) [S原件L662](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:662>)

申请完整Fact+LineFacts+Head+Binding+Receipt+Audit同UoW；收货独立要求Stock/IN/RMA同UoW；Owner观察原bytes/head/checkpoint同本域事务。QA/ERP/Finance/PLM远程调用须先保存frozen request/Work/outbox，不能持RMA锁等待；处置当前没有远程调用。[S原件L712](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:712>)

**跨册待核差异**：RMA正文和data把Scope/来源/Entitlement/RMA head放在IN Work/Manifest及Stock Canonical前；Stock/OUT规范则先Stock Canonical/叶，再Scope祖先与细资源。收货又要求共同事务，两组叙述不能直接实现成彼此回头取锁。应在IN/RMA/Stock共同采用时核全部资源收集与唯一组合排序，取得明确Owner解释/版本补充；本整理不静默重排任一原件，也不把静态差异称真实死锁事故。[S原件L699](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:699>) [D原件L314](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a5/a5a4316bf2aa0d30__WMS-RMA-01_DATA-DICTIONARY.json:314>) [STK原件L1414](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1414>) [OUT原件L905](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:905>)

所有可能跨Owner的步骤先存原queryLocator/effectKey、body和Checkpoint。worker以leaseEpoch/rowversion接管，旧worker最后CAS失败；租约到期不释放业务claim。启动/周期扫PENDING/UNKNOWN及超期RUNNING，耗尽转人工待续，不把UNKNOWN改REJECTED。可能已收查SO01/原Effect；商业失败保物理；更正先永久fence全部相关使用、枚举子键、已应用走Owner逆向、UNKNOWN保claim，再追加RMA successor。[S原件L790](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:790>)

## 7. 权限、Scope与证据

申请、授权、收货、Quality观察、处置、商业请求、恢复、审计、切换分别有能力；Admin角色不代capability。Tenant/Environment/Principal/服务Owner来自可信会话/登记，浏览器不能交trusted digest、Owner URL、system principal或已审批布尔。恢复还需原步骤能力，不能变成通用越权执行。[S原件L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:41>)

证据仅接服务器signed option；Q09建立访问，Q10查状态，Q11流出字节前再次核object/capability/field policy并审计。导出READY不是授权，收窄/撤权/过期零字节，新政策需重新生成；无bearer URL。仅customerRef业务需要，普通列表不额外下发PII，日志不写原附件/凭证。[S原件L388](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:388>) [S原件L470](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:470>) [S原件L938](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:938>)


证据状态闭合为可用、处理中、过期或撤销等定义分支；EXPIRED/REVOKED 的 contentRoute/digest 不得继续可用，下载返回零字节。审计 C18 建任务、Q12 查结果、Q13 实际下载时重核当前能力及字段政策；旧 READY 不提供长期 bearer 权限。导出固定列集合和过滤快照，超出50000行或20MiB拒绝而非静默截断；政策变化须重生成。[原件L11282](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:11282>) [原件L11485](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:11485>) [原件L11638](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:11638>) [原件L938](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:938>)

## 8. 页面、交互和校验

八页为申请授权、RMA详情、实收、质量处置、商业财务、工程反馈、恢复审计、迁移切换。详情逐原份额显示九轴和quantity/coverage/sourceCut；EMPTY/MISSING/UNKNOWN/PENDING/PARTIAL/FAILED/CONFLICT/REVERSED分别呈现。第一页固定watermark与权限/筛选快照，Refresh新cut，不能把服务不可读当空列表。[S原件L66](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:66>)

收货位置从MASTER/WMS正式option与evidence选，不拼`${WarehouseCd}-RMA-HOLD`；Lot/Serial/LPN/UOM不符阻普通写，不能按SKU借批。处置UI只交纯选择字段，显示五支missing qualifications；HOLD继续保持另按钮。商业页展示批准量15/实收20、贷项Posted/退款Pending分别，金额未知不0。[S原件L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:99>) [S原件L259](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:259>) [S原件L353](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:353>)

默认20最大100分页，command1MiB，每RMA100份额、400质量/处置段、100证据引用，审计50000行/20MiB超限拒绝不截断。发生和登记时间分开；当前未知显示最后确认与cut。[S原件L938](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:938>)

## 9. 整合顺序和旧Main差距

建议先准确源映射/原坐标claim及申请授权流，再IN唯一适配和共同UoW/锁序采用、原键恢复，再QA/商业/H30查询消费与证据页面，随后逐个被正式补齐的终端Owner合同，最后legacy单Writer切换。五终端当前fenced模型应完整实现其待办/资格显示，不把静态目标投影当执行代码。此为依赖排序建议，不新增业务决定。[S原件L346](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:346>) [S原件L712](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:712>)

原文固定旧Main SHA `157630594e3371fe181955d2f6227ff3b6962c84`：Create直接Authorized、头号/SKU弱来源、拼隔离位、Judge漏行仍Judged、整量单处置、Close先Closed再best-effort信用桥、随机CreditNote/ReturnedQty错行等需REWIRE。旧ConditionLevel只是物理观察，不映Quality；旧0/1/2/3/4/5/9不直接映新九轴。本轮未核当前运行实现。[S原件L896](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:896>)

迁移分类OLD_CONTINUE、PROVEN_MAPPABLE、MANUAL_RECONCILIATION、READ_ONLY_UNPROVEN；按Environment/Site/Warehouse/legacy scope完整writer清单、水位、mapping准备，OLD_FENCED后CAS新epoch才ACTIVE。有新事实后只能前滚/新受控epoch，不删新账再启旧双写。[S原件L420](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:420>) [S原件L927](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:927>)

## 10. 验收设计与执行边界

52项 AC 的 given/when/then/requiredEvidence/status 已全部阅读，仍全部 NOT_RUN。验收要覆盖授权前 share 为空、余20的15+15竞争只成功一次、两种IN/Stock分支、原键响应丢失恢复、四态质量分区、五终端零Stock调用、信用和实物量不同、不同财务轴、旧writer阻断、撤权零字节。[原件L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a90d0ad670765ff__WMS-RMA-01_AC-CATALOG.json:1>) [原件L804](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:804>)

连续附件完整语义读取51个节点：15业务案例、19 schema 正例、12 schema 反例、5语义反例。全部新字段和值已读，重复对象用精确子树引用；51个表示还原均相等。以下是案例族的实施含义：

|案例族|必须保持的语义|
|---|---|
|历史发100、正常退20；分批收货|历史发运/Demand不重开，每真实实收份额一次Stock效果，丢响应查原键|
|相同SKU多原行；重复授权；并发争用|原ShipmentLine/InvoiceLine及价格谱系决定身份，额度按原坐标集合排除，不按SKU合并或截量|
|质量 A10/R5/H3/P2；五处置|四态互斥且合20；resaleMax10只是资格上限，所有五Owner仍空、实际调用0|
|RETURN_ONLY；信用15/实收20|库存价值等待与商业贷项独立；AR Posted/退款 Pending保持财务PARTIAL|
|查询、更正、旧数据、H47与切换|NOT_OBSERVED不证明无效果；原前驱后继；旧未证数据只读；反馈幂等不等改善；单writer与下载重鉴权|
|类型正反例|保持Stock八kind和Int64分支、禁trusted输入、拒质量/金额/范围不守恒；仅静态类型例不是路由采用|

案例文案的完整SC19目标与其局部 wire 演示不是同一套已执行账：首例标题授权20，而嵌入申请/授权只演示10；“8+12”章节的两种Stock分支各用5演示形状，不能拿局部数字证明连续20实收已经执行。Financial 的叙述字符串 WAITING_SOURCE_REASSESSMENT 也不能直接生成 FinancialState 的新枚举。[原件L6](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:6>) [原件L593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:593>) [原件L382](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:382>)

本轮只做文档语义读取、原值还原及身份核对。STATIC-CHECKS 和历次独审中的历史 PASS 属原作者静态记录；未执行候选 validator、业务验收或真实 Owner 调用。[原件L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a9d16347b79e115__D-WMS-RMA-MD-SPEC-01_R3_CUMULATIVE-STATIC-DESIGN-CONCLUSION_20261002.md:1>) [原件L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9f/9f2e09174013aa41__D-WMS-RMA-MD-SPEC-01_STATIC-CHECKS.json:1>)

## 11. 待确认、文本差异及缺口

1. 收货共同锁序差异见§6和全局 DC-001，需 IN/RMA/Stock 共同采用时统一全部资源预收集与顺序；此处不静默重排。后继采用见 DC-002：QA-NCR、ERP-CREDIT、PLM 的后续准确接受不能自动解除 RMA 本组合的运行围栏；五终端 Owner 合同仍须逐路由明确。[原件L699](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:699>) [原件L29](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6b/6bc33b9183f5e3da__ACCEPTANCE.md:29>)
2. **OPS-IF-048：资格状态如何投影到 Work/聚合轴尚不一致。** canonical 的 DispositionState 和 WorkState 都没有 REQUIRED_FENCED；Slice 有该值，而 D05说明要求 slice/head及Work保留 REQUIRED_FENCED。全部附件已读，仍未见完整投影表。不得把 Slice 枚举直接塞入九轴或 Work，也不能自行选择 BLOCKED/PENDING 宣称冲突已解；需 Owner 提供准确映射及 typed Work/Detail 例。[原件L357](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:357>) [原件L414](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:414>) [原件L1985](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:1985>) [原件L1021](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/561c763d68c1f87a__WMS-RMA-01_API-CATALOG.json:1021>) [原件L458](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:458>)
3. **OPS-IF-049：H30 seal 正例的效果身份不一致。** h30Sealed 查询 `FININV|RMA-1|0-5`，所附 terminalReceipt 却列 `RMA-1|DSP-1|EFFECT`，未附可证明两者关联的消费者 binding。结构符合 SEALED 分支不等于该消费者域已永久关闭；此例不能直接用于释放 FIN-INV claim。需补同一消费者 effectKey 的 binding/receipt/proof 及关系断言。五终端 D04 的当前围栏不受该演示例解除。[原件L2465](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:2465>) [原件L2468](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:2468>) [原件L2489](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:2489>) [原件L4251](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:4251>)
4. **OPS-IF-050：源坐标案例混用发运头与行标识。** 原 snapshot 中 `shipmentRef.id=SHIP-100`、`shipmentLineId=SHIP-100-L1`，后续 claim 的 shipmentLineRef.id 使用 SHIP-100；当前未附二者等价映射。规范键明确按 ShipmentLine owner/id，所以实现不能按该示例把同头多行塌缩为一源坐标。需准确原行身份或有权映射，并用同SKU不同原行/价格的例核定。[原件L80](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:80>) [原件L84](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:84>) [原件L999](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2159cd0850d66cf1__WMS-RMA-01_CONTINUOUS-EXAMPLES.json:999>) [原件L814](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json:814>)

以上后三项是已接受材料中的具体待核表达/案例关系，登记在[运营整合待核](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/operations-integration-findings.json>)，不推断已上线缺陷，也不改写历史接受结论。

## 12. 来源和实际阅读覆盖

|必要材料|本次与可核验复用的语义范围|
|---|---|
|主正文|1–955全文；RequiredFenced 1–198全文|
|Canonical|1–12543全部根规则及195定义；完整对象或全部递归差异，195对象还原相同；Stock字符串版本与RMA整数版本逐类型区分|
|API/Data|52操作、23能力和全局约束；43事实族、全部主键/唯一约束/并发、锁序、sameUow、claim公式|
|Continuous examples|4846行的51节点全业务语义；完整新字段和全部递归差异、精确重复引用；演示UUID/digest用可逆别名，不把重复占位摘要当真实Owner原件证明|
|AC/Trace/Static|52AC全部条件/证据/状态；16来源行、3接口、1横切、8页；反向52API/43data/52AC/15example索引与已读正向对象精确相等；65静态检查、31schema案例、5语义反例仅历史验证范围|
|接受和评审|CURRENT 159行、累积结论105行、R3独审99行、最终SR69行、接受53行、review input30行、index23行、作者reading receipt40行全文|
|当前review pack|15成员身份/字节全等；manifest列48上游来源全匹配原source ZIP|
|固定source packet|48成员逐一分类并核SHA/bytes；6上游摘录的31窗口逐窗与已读原件文本/UTF-8 LF字节相同；FIN-INV绑定与H30围栏、NEXT/准备/阅读包本轮全文；全局scope仅RMA对应对象及必要DP读取|

六个摘录来自 OUT、DEL、FUL、GL、POL、IQC；FUL 准确同 SHA 语义阅读由商业组提供，其余按本组/平台已完成原件范围复用。原件路径、每窗口行段、SHA、重复引用和语义闭合分别保存在[运营阅读账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/operations-reading.json>)。字面未再次逐行显示的重复范围保留为 `literal_not_reread_ranges`，与语义未读分开。

**仍未全文读取的历史材料范围**：source packet 内12份旧Main代码快照只核身份/角色，历史全局audit/Registry及超出RMA的Catalogue/Blueprint/Plan正文只做身份或有界RMA读取；作者 Source-Read-Receipt 不冒称本组已读这些旧代码。它们进入历史材料独立处置队列，不作为当前必要规范已完成的依据。真实实现、52AC、跨Owner共同事务/锁序和实际采用仍未验证。[原件L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2c6d443876ecf87b__D-WMS-RMA-MD-SPEC-01_SOURCE-MANIFEST.json:1>) [原件L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/70912f41104eabb0__D-WMS-RMA-MD-SPEC-01_SOURCE-READ-RECEIPT.md:1>)
