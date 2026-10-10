# PLAN-EXC-01 源事实、封闭读取与公开投影

本册把 CP12 当前组合中的原事实、私有对象、权限公开投影和两份继续有效的 CP04 规则写成开发输入。它与[数据与类型](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-数据与类型.md)、[Owner 与游标](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-Owner与游标.md)合读；真实来源和阅读范围见[独立阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-source-reading.json)。本文的数量和状态场景是规范中的静态设计实例，businessAC=NOT_RUN、runtime=UNPROVEN、实际采用与实际授权为 0；并不证明服务、数据库或实际 Owner 接口已经实现。[当前原事实状态](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:594433) [权限采用状态](D:/CP6-archives/consolidation-20261010/objects/4cd96b35db0852f33c13b1cfa5b05b5c7faf16bad378d335b114844aae987675.json:171925)

## 1. 当前版本选择与 Owner 边界

虽然文件名仍带 CP03/CP04，canonical、native、private、source/public、authority 五个大成员来自 CP12_COMPLETE_SUCCESSOR_ENTITY，须按各自完整 SHA 使用；材料算法说明和强制集合决定来自 CP08_EXACT_COMPLETE_BASE。CP12 的精确父来源为 MAT_PARENT_MAKE100_OUTPUT、versionKey=CP09-A；MATERIAL_NEW 的 Query/Page/Capture/Case 后继为 CP12-A。不能用文件名、旧版本标签或“最近复制时间”覆盖这组关系。[CP12 父版本修正](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177703) [继承配置的精确来源](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/172ccf551439b8dc__CP04_MATERIAL_METHOD_AND_ALGORITHM_SPEC.json:47)

EXC 的操作者是查询缺料、比较评估、准备处置和跟踪恢复的计划人员。其职责是保存原事实证据、形成评估/影响、申请 Owner 动作并消费回执。EXC 不直接修改库存、取消 PO、替换需求控制或重写 MRP 发布结果。私有图内的 Owner 标签也不改变上游事实的所有权。[处置边界](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177334) [来源所有权](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:167925)

| 来源 | 当前设计要求 | EXC 必须避免的替代 |
|---|---|---|
| PLANNING-DEMAND | 原单行/计划行、派生 Demand、当前 Control 均纳入同一原 Owner 的保证 | 只拿原单引用就假定 Control 已当前 |
| MES-WO | 创建上下文、CreationMapping、MaterialLine、Obligation、Transition | 由 WO 冒充 MES-MAT 的剩余量来源 |
| MES-MAT | 原 RemainingBasis、OffsetEvidence、ProtectionBindings | EXC/MRP 重新推导已经扣减的净剩余量 |
| PLAN-MRP | 单 Attempt 的完整输出、发布谱系、选定/保留/保护成员 | 混合不同 Attempt 的输出或将 MRP 输出冒充上游 Demand |
| 原库存、采购、质量、技术、政策 Owner | 各自原事实、精确版本及有作用域的当前保证 | 将 SUP 的投影证明塞进 PUR 原始真值保证 |
| CORE | 精确用途、主体、资源与字段许可的原决策和当前权限头 | 用静态许可样本、其他端点许可或旧下载许可代替 fresh 授权 |

上表来自封闭来源主体规则与所有权决定；17 个 native contract 定义只是本组合的 proposed 专业义务，实际采用仍为 0。不可从可解析的样本、QUALIFIED 静态标签或完整字节推导出实际 Provider Pin 或独立 Owner 接受。[完整 Owner 主体范围](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177326) [17 项专业合同](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:492878)

## 2. 身份、数据对象和版本轴

| 对象或字段 | 开发语义 |
|---|---|
| FactRef | owner、namespace、objectKey、可空 line/schedule/occurrence、versionKey、bodyDigest 共同指向原事实；不按显示标签重新拼接身份 |
| OpaqueKey | 保留原编码、原 text、UTF-8 byteLength 与 rawDigest；不是可随意 trim、大小写归一或截断的键 |
| native body / nativeUtf8 / artifact | 原 body 与原字节、长度、SHA 对应；信任与文件载体元数据分开保存；字节相等只是来源身份核对 |
| CurrentHead | 原 Owner 资源、headRevision、membership、作用域/用途 guarantees、可空 supersedesHeadRef、state；不能当成全局“最后更新时间” |
| graph / assessment / impact | 原源图、不可变评估、影响闭包各有身份和业务 digest；公开脱敏不重写它们 |
| Case revision / readCut / source version | Case 业务修订、固定读取 cut、原事实版本是不同轴；不得用其一代替另一个 |
| sourceReadPlan | readPlanId/version、Case/Episode、origin、声明范围、必需 Owner reads、scopeClosure、currentManifest、purpose、有效期、readiness、digest |
| sourceReadRequest / sourceReadPage | 请求绑定原 Plan/作用域/当前集合；页面保留原请求、原 Owner 文档与完整性证据，不能只存渲染结果 |

上述字段的完整类型约束见数据附册；本册所核原事实具有 633 个具体 body，353 个 CurrentHead。CurrentHead 中 102 个明确带前驱，前驱不为空是合法不可变后继，不应误判为不当前。[原事实封装样例](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:5) [40 个精确当前集合](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:541777) [21 个私有对象](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:3) [读取计划字段](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:102411)

原 Attempt body 在 StartedAt 以 RUNNING 身份创建。完成边界由 OutputSeal 与完整 ResultSet 给出，不为方便消费把原 Attempt 提前改写成 COMPLETED。不可变主数据的命名版本/body 必须捕获；可变谓词还需要原 Owner 当前保证。后来更新的 current 不能补救原 sealed input 遗漏成员。[Attempt 原身份](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:176680) [不可变版本与可变谓词](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177332)

## 3. 从 Owner Query 到私有源页的封闭读取

当前实例有 30 组 Query/Page/Capture，分别覆盖 10 个阶段、每阶段 SUP/MRP/POL 三个提供者。每组三件保留一致的原查询、成员、head、slot、scope 与时钟；读取模式为 FULL_ORIGINAL_NATIVE_BODY。它们不是“从多个远端 GET 各拿最新后拼接”这一实现的证明。7 份 EXC 源页保留 840 处原 native document 嵌入，逐个与 registry 精确比较后复用语义。[全部原查询定义](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:163461) [输入查询样例](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:19746) [7 份私有源页](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:120494)

| 阶段 | 调用用途、时点（2026-10-04 UTC） | SUP / MRP / POL 成员数 | 单份原查询的当前 head 数 |
|---|---|---:|---:|
| BASELINE | PLAN-MRP input.capture，17:57:40 | 18 / 1 / 8 | 8 |
| NEW_INPUT | PLAN-MRP input.capture，18:10:11 | 57 / 54 / 8 | 8 |
| MATERIAL | PLAN-MRP input.capture，18:19:51 | 118 / 118 / 25 | 14 |
| INITIAL | EXC source.read，18:10:00 | 54 / 54 / 8 | 8 |
| PREPARED | EXC source.read，18:10:10 | 57 / 54 / 8 | 8 |
| PUBLISHED | EXC source.read，18:10:28 | 92 / 92 / 8 | 8 |
| SHARED | EXC source.read，18:10:10 | 21 / 0 / 8 | 7 |
| MATERIAL_OLD | EXC source.read，18:20:25 | 163 / 163 / 163 | 15 |
| MATERIAL_NEW | EXC source.read，18:20:25 | 163 / 163 / 163 | 15 |
| SHARED_SETTLED | EXC source.read，18:10:29 | 27 / 24 / 25 | 9 |

这是当前具体实例的封闭成员表，并非生产数据量或固定分页上限。逐个 member/head 身份见阅读证据 query_rosters；对应原件入口为全部 Query 与 currentCuts。[查询集合](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:163461) [当前 cut 成员集合](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:161560)

实现读取入口应先确定用途、Case/Episode、原 origin、读取 Plan 及其 digest，再向 requiredOwnerReads 指定的原 Owner 取完整原文和完整性证据，验证当前保证与具体输入域，最后生成 EXC 私有图/评估/影响和读取页。若分页缺页、水位不封闭、Owner 失败或证据不一致，保留不完整原因；不得把“返回零行”当成已证明无供给。当前静态 Query 样例各为 ordinal=1、前页为空、final=true、next=null、COMPLETE，但接口不能因此写死只有一页。[计划与 requiredOwnerReads](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:102410) [精确请求](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:105233) [原 Owner 查询文档](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:1805)

输入捕获与后来 EXC source cut 分离。前者不能包含其后才生成的 Run 输出；当前引用闭包与 captured input 闭包各有边界。配置中的 $native 是文档编写用 selector，必须解析为明确 FactRef；不是可直接执行的运行时配置。[6 组捕获闭包](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:176681) [配置 selector 边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/172ccf551439b8dc__CP04_MATERIAL_METHOD_AND_ALGORITHM_SPEC.json:48)

## 4. 七个阶段怎样改变业务结果

| 阶段 | 原业务事实与结果 | 不得推导的结果 |
|---|---|---|
| INITIAL | Demand A=100，合格库存=80，shortage=20 | 缺口非 0 不等于已经批准采购或重算 |
| PREPARED | 新收货 20、质量与精确 share mapping 加入，on-time=100，shortage=0；仍使用 ES1 | 不能由缺口归零宣布 Case 已关闭或 MRP 已发布 |
| PUBLISHED | 消费 ES2 完整 MRP 发布链，shortage=0 | MRP 发布和 EXC 消费是两个本地事务与回执边界 |
| SHARED | PO100 保持；A 活跃 0..30、退休 30..60、B 60..100；退休需求为 0，但潜在影响 30、执行未知 | ACCEPTED 回执或需求退休不等于 Owner 已完成处置 |
| MATERIAL_OLD | 原 WO gross60，原净剩余40已含 offset20；late10 不计按时供给，缺口40 | 不再次扣 offset20、不重新展开原 WO |
| MATERIAL_NEW | 新 MAKE100 按 9/5 产生180，其他自由库存50覆盖 0..50，缺口130 | 不把旧保护库存20再释放为新可用量 |
| SHARED_SETTLED | 独立 PUR 结果、SUP 无后续影响证明和 SETTLED 回执齐备后形成新 successor，潜在影响0且可进入本地重算条件 | 不删除 PO100、A30/B40，不重建原请求或修改库存 |

七阶段均保留独立 graph/assessment/impact，明细见对应原对象；材料两 Case 可在兼容组中汇总 170，但仍是两个独立 Case 与各自范围。[INITIAL 评估](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:9144) [PREPARED 评估](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:14462) [PUBLISHED 评估](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:20555) [共享影响](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:4) [原材料剩余40](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:31055) [新材料缺口130](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:41617) [settled 后继](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:48582)

材料算法先净算父缺口，按合法 min20/increment20 批量选择 MAKE100；BUY 不生成制造派生。原 WO100 按其 3/5 已形成 gross60 和净剩余40；新 MAKE100 使用 9/5 形成180。70 库存中 0..20 是已有保护，20..70 的自由50才可用于新派生。late PO10 明确映射旧 WO 的 20..30，晚于其 required-use node；它只产生迟到异常，不减少按时缺口。两份材料图都保存共同闭包，但每 Case 的目标身份仍分别指向旧 WO obligation 与新 MRP derivation。[完整材料方法与算法](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/172ccf551439b8dc__CP04_MATERIAL_METHOD_AND_ALGORITHM_SPEC.json:2) [旧材料图](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:22135) [新材料图](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:32697) [迟到10的精确区间映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:2)

数量计算使用 EXACT 有理数及明确 UOM；shortage=max(0, required-onTime) 仅在所需范围完整时成立。不完整源允许显示已知的 required/onTime 分量，但最终 shortage 为 null/UNDETERMINED。实际发生、可用、质量有效、发布时间、预期到达分别有业务时点，不能用 publication 代替实际收货时钟。[主规范数量与完整性](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:111) [主规范缺口公式](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:121) [主规范时间轴](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:250)

原共享处置时序为 ACCEPTED20、PUR result21、SUP proof26、SETTLED27、新 successor29。ACCEPTED20 时仍须保留 potential30；不可提前解除影响。当前公开 Case 样例七阶段仍为 OPEN、alert VISIBLE、actions/preparations 为空。它们展示一个查询 cut，不证明 UI 可以自动结案或补造动作历史。[共享原请求与时序](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177259) [共享 settled 条件](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177334) [settled 的公开 Case](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:697785)

## 5. 必需关系集合、写入边界和恢复

强制关系是持久模型的必要写集，不可因为 API 顶层对象看似完整就省略。每个 Impact.shares 条目都有精确 ImpactBeneficiary；material sharedProtection 必须绑定实际 stage 的 Protection 及原 MO-1 beneficiary。通用 Firm100 证明并不证明每一个 offset20 的独立性，因此相应 independenceProof 保持 null，原一般证明仍保留在私有 closure。[全部 share 关系](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:212) [保护与边界关系](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:213)

acceptedObligationIds 是本条不可变回执所见的累计接受历史：settled ⊆ accepted；settled 与 unresolved 交集为空；accepted-active=accepted−settled。ACCEPTED 与 SETTLED 可对同一 obligation 形成不同关系行。所有 ID 必须属于同一精确原请求，区间关联来自原 request/receipt 映射，不能对两个数组按位置 zip。没有 acceptance 不代表 settlement；拒绝、未接受和未解决还要看具体 outcome 与原证据。[累计集合语义](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:217) [接受与 settled 独立关系](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:214)

每条 RESULT_SEALED、SELECTED、PUBLISHED 与冷恢复 FOUND 的普通 MRP 回执保留其原 4 个结果成员和 0-based ordinal。这是该回执具体原成员集，不能推广成所有材料输出只有4项；材料算法另规定五成员 output seal。不得为填表而虚构来源父节点。[普通回执结果成员](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:215) [材料五成员封口](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/172ccf551439b8dc__CP04_MATERIAL_METHOD_AND_ALGORITHM_SPEC.json:21)

NEW_FACTS 与 SAME_INPUT 均注册真实 ordinal0 REGISTERED 步骤，记录原 request/original-slot artifact，observedReceipt 与 predecessor 均为空。首次原观察是 ordinal1 并指向0；之后沿前驱连接。冷恢复复用原 work/slot0，不能重发一个新业务命令来制造“恢复成功”。[注册与冷恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:216)

SAME_INPUT 继续引用原 I1/F022、manifest F023、algorithm F025、ES1/F032 和原 Run F130；失败 Attempt F131 经 isolation F098、eligibility F133 后才可转入 successor F134。它不创建新 InputSet、Run 或 Owner conversion。源模型还保留迟到旧 generation 诊断；超时/未知提交结果不能替代隔离证明。具体八 writer fence、事务与本地 cut 的实现边界应合读主册及本地 UoW 附件，本册没有执行或验证这些机制。[原输入恢复绑定](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177357) [原隔离证据](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:289746) [后继 Attempt](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:489176) [旧 generation 诊断](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:492358)

## 6. 页面、字段许可、原授权与固定读取

同一个私有对象可有四种公开结果；公开掩码不能修改私有 graph/assessment digest。完整 tuple 来自 exact native CORE decision，并绑定具体端点 action/resource。下载和导出也必须 fresh 取得当前 tuple。字段组授权独立，父 Case 可读不自动授予源图、影响、历史或原始 slot。[公开许可语义](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:631853) [23 个独立字段码](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:631828)

| 变体 | Case 有效字段码 | 页面行为 |
|---|---:|---|
| full | 23 | 可公开被授权的摘要、当前摘要、源计划、来源/影响/历史/证据、维度等 |
| parentOnly | 5 | CASE_SUMMARY、CASE_CURRENT_SUMMARY、SOURCE_PLAN_HANDLE、PUBLIC_DIMENSIONS、CASE_ISSUES；source/impact 因用途拒绝而无资源 body |
| dimensionsAndIssuesDenied | 3 | 前三项；维度、issues 和依赖这些授权的 evaluation 展示被脱敏 |
| caseSummaryOnly | 1 | 仅 CASE_SUMMARY；当前摘要与源计划也脱敏 |

full 的 23 项精确字段表为 CASE_SUMMARY、CASE_CURRENT_SUMMARY、SOURCE_PLAN_HANDLE、ASSESSMENT_PROVENANCE、ACTION_EXPECTED、SOURCE_GRAPH、SOURCE_METADATA、IMPACT、HISTORY、ORIGINAL_SLOT、RECALC_PROGRESS、LEGACY、LEGACY_MAPPING、AGGREGATE_DIMENSIONS、RESOLUTION_HISTORY_REF、DISMISSAL_HISTORY_REF、EXPORT、QUANTITY_BASIS、AGGREGATE_GROUPS、ACTION_EVIDENCE、CONTEXT_SITE_REFS、PUBLIC_DIMENSIONS、CASE_ISSUES。后三变体对七阶段采用相同脱敏结构，但仍逐端点重新计算许可 tuple；不可用固定数字“5”代替字段身份。[字段码全集](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:631828) [四种公开变体](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:508520)

parentOnly 可见某些数量摘要，但数量 basis 和来源/影响 provenance 被脱敏；聚合状态为 REDACTED、completeForVisibleScope=false。公开 dimensions 是受控 item/owner/site/technical/UOM/时间展示，不能泄漏 native FactRef、scopeKey 或原主体引用。whole disclosure 的 issues 也必须遵守相同限制。字段隐藏与“数量为0”“源集合为空”不同。[父可读但源不可读](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:518879) [维度与 issues 拒绝](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:525411) [仅摘要](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:531867) [维度与 issues 约束](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:631853)

公开 SourcePage 从固定私有源图投影：样例选择 DEMANDS 或 MATERIALS 集合，其余未选择集合清空；metadata 保留完整私有图的 digest/version/sourceId，并给 returnedCount、visibleTotal、permissionDigest 和 LOCAL_PAGE 分页信息。不要对部分公开图重新计算成“完整私有图 digest”。全权限的具体过滤绑定原 CaseOrigin：普通 A 为 PLANNING-DEMAND/DEMAND/A；共享为 PLANNING-DEMAND/CANCEL/A30；旧材料为 MES-WO obligation MO-1；新材料为 PLAN-MRP derived180。不能匹配任意祖先就把其他 Case 纳入结果。[公开查询与源页](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:508521) [材料公开范围](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:663553) [七阶段公开投影](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:3)

84 组 authority 实例是 7 阶段 × 4 掩码 × 3 端点。CaseView/CasePage 用 plan-exc.read；SourcePage 用 plan-exc.source.read。CasePage 的 resourceCaseId=null，另两者绑定具体 Case。拒绝 SourcePage 用途时 fieldMask=[]、无公开 resource body，不以父 Case 的 GRANTED 越权读取源。[完整端点 tuple](D:/CP6-archives/consolidation-20261010/objects/4cd96b35db0852f33c13b1cfa5b05b5c7faf16bad378d335b114844aae987675.json:4) [用途拒绝 tuple](D:/CP6-archives/consolidation-20261010/objects/4cd96b35db0852f33c13b1cfa5b05b5c7faf16bad378d335b114844aae987675.json:6369)

授权证据分开保存原决策、原 current authority head、原 principal、SDK core/wire pin。permissionDigest=H(完整 ExceptionAuthoritySnapshotBusinessCore)；wire 加入 permissionDigest 和外部 artifact，不能把它们回填进原 core preimage。原 core UTF-8 的哈希/长度与 artifact、原 head 的 UTF-8 与 body 已做静态一致性核对；这仍不是实际通道认证、签名验证或 Provider 运行证据。[授权 core 字段](D:/CP6-archives/consolidation-20261010/objects/4cd96b35db0852f33c13b1cfa5b05b5c7faf16bad378d335b114844aae987675.json:1875) [SDK 精确 pin](D:/CP6-archives/consolidation-20261010/objects/4cd96b35db0852f33c13b1cfa5b05b5c7faf16bad378d335b114844aae987675.json:1463) [条件资格边界](D:/CP6-archives/consolidation-20261010/objects/4cd96b35db0852f33c13b1cfa5b05b5c7faf16bad378d335b114844aae987675.json:1865) [digest 与资源范围](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:631859)

SDK 版本说明：该 authority 工件所带 wire codec 的 Owner 枚举没有 MES-MAT；它与当前 EXC-PC08-CORE-SDK 的 completeWirePackage 精确相同。相同叶合同的 completeCodecPackage/completeRecursiveCodec 及 master Owner 定义则含 MES-MAT。应分别保存 core/wire 的精确合同和 digest；这是已核身份的版本范围差异，不能据此说 authority pin 指错版本，也不能将 master 枚举自动覆盖 wire codec。[authority wire 的 Owner](D:/CP6-archives/consolidation-20261010/objects/4cd96b35db0852f33c13b1cfa5b05b5c7faf16bad378d335b114844aae987675.json:1685) [PC08 core Owner](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/EXC_OWNER_LEAF_CONTRACTS.json:100382) [PC08 完整 wire](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/EXC_OWNER_LEAF_CONTRACTS.json:100445)

游标当前指向 CP06 v6 的完整 filter、私有 retained query/binding、固定 cut 与 fresh expired LOOKUP；CP05 是历史，不能回退 v5。页面默认50、允许1..200；导出50000行/20MiB超限拒绝而非截断；cursor15分钟、HTTP2MiB等上限由主规范规定。游标真实发行、私有保留和运行验证另见 Owner 与游标册，本册只核当前引用指向。[当前 v6 指针](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:631860) [主规范页面与导出上限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:127)

## 7. 编码顺序、验收与未决适配

以下顺序是整理后的实施建议：先落精确类型/原字节与来源验证，再做每个 Owner 的 Query/Page/Capture/current 适配和资格记录，然后形成不可变 graph/assessment/impact 与完整关系写集；接着接入本地事务、原 slot 幂等、Owner 回执恢复；最后实施固定 cut 的公开查询、字段脱敏、fresh authority 与游标。每一步都要保留原 Owner 责任，不以同进程调用推导跨域共享数据库事务。建议依据为 sourceOwnership、读取计划、强制集合与权限模型。[来源边界](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:167925) [读取前置](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:102410) [持久必需集合](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:211) [公开权限](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:631853)

未来实现至少要验证以下独立场景；此处只是验收设计，未执行：

- 同版本同 digest 重放保持原事实与原 slot；同原身份不同 body 进入冲突隔离，不覆盖旧事实。[主规范身份冲突](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:107)
- 缺页、缺 Owner 当前保证、输入捕获遗漏或跨 Attempt 拼接不能产生“已完整”的评估。[当前主体与捕获闭包](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177326)
- PREPARED 缺口0、共享 ACCEPTED、共享 SETTLED 是不同语义；原需求/PO/库存和请求身份不被改写。[共享后继规则](D:/CP6-archives/consolidation-20261010/objects/2074ca5b41c3b19dde93444abc8a9f7fff6da1593227fee5593a849db09918b6.json:177334)
- 材料旧40、新130、late10、保护20、自由50和 group170各自有精确范围，不能重复扣减或混合受益人。[材料算量](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/172ccf551439b8dc__CP04_MATERIAL_METHOD_AND_ALGORITHM_SPEC.json:19) [迟到影响映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:2)
- REGISTERED0、原首观察1、冷恢复和累计 accepted/settled 集合关系保留；UNKNOWN/超时不作为成功或隔离。[集合与恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5ebc869e970e245f__CP04_MANDATORY_COLLECTION_DECISIONS.json:211)
- 四种掩码、三种端点、CasePage resourceCaseId=null、权限变化后的下载/导出，以及固定 query/cut 的 cursor 均按精确 tuple 验证。[84 种静态授权组合](D:/CP6-archives/consolidation-20261010/objects/4cd96b35db0852f33c13b1cfa5b05b5c7faf16bad378d335b114844aae987675.json:3) [fresh 公开权限](D:/CP6-archives/consolidation-20261010/objects/e22f8d4a1ea943a857810c9df4b4615c80ecb3089d204c401cae7a306cec719f.json:631853)

## 8. 两项有依据的静态跨工件差异

### PLAN-IF-007：Receipt20 的 ACTUAL_AVAILABLE 时点不一致

原 WMS F014.availableNode 为 2026-10-04T18:10:02Z；对应库存 movement F016.actualNode 同为 18:10:02Z，指向 ROOT/RECEIPT20。PREPARED/PUBLISHED 的 SUP projection 引用这个精确 F014，却把 availableNode 写为 18:00:00Z；对应私有源图 supplies[1] 同样为18:00。这些节点均为 ACTUAL_AVAILABLE、isActual=true、UTC，并非质量有效期、发布时间或预期节点。未读到该投影算法允许为此同一事实重新定义实际可用时钟的规范。[原 WMS availableNode](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:44128) [原 movement actualNode](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:43436) [PREPARED 投影时间](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:348068) [PUBLISHED 投影时间](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:364060) [PREPARED 私有图时间](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:11126) [PUBLISHED 私有图时间](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:16739)

影响：原事实→投影→私有图的时间 provenance 不一致，需要 Owner 明确权威时钟或定义独立的另一业务时间字段。当前场景 required-use 在次日，因此没有据此证明缺口数值变化，更没有证明生产故障。退出条件是 Owner 给出精确语义裁定并发布一致的不可变后继及受影响引用/digest；消费方不能自行选择较早/较晚值，本次不改原文。

### PLAN-IF-008：材料图的 algorithmRef 与材料运行链不同

MATERIAL_OLD 与 MATERIAL_NEW 图的 mrpLineage.algorithmRef 均指向 F025（ALG1、version1.0）。同组合的 MAT_INPUT、MAT_RUN、MAT_ATTEMPT 则精确指向 MAT_ALGORITHM_V2；材料方法说明也是 2.0-proposed。当前主规范明确材料链所有消费者应绑定 v2，同时 F025/I1 历史实例不变。[旧材料图算法引用](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:22239) [新材料图算法引用](D:/CP6-archives/consolidation-20261010/objects/9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925.json:32801) [材料 Input 算法](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:217707) [材料 Run 算法](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:218246) [材料 Attempt 算法](D:/CP6-archives/consolidation-20261010/objects/7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec.json:218756) [材料算法 v2](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/172ccf551439b8dc__CP04_MATERIAL_METHOD_AND_ALGORITHM_SPEC.json:6) [主规范材料 v2 与原 I1 边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:259)

影响：这是同一材料谱系的静态引用不一致，不能据此证明运行选错算法或得到错误库存。退出条件是维护者确认材料图的精确算法 lineage，并让不可变后继、body digest 及所有依赖引用一致更新；不得把原 F025/I1 改写成 v2，也不得只改展示字段而留下旧身份。

## 9. 本册阅读覆盖与可复用边界

七成员的来源 SHA、字节数、原文行段/JSONPointer 与读取方式均保存在独立 evidence。两份小规则全文阅读；五份大 JSON 采用完整当前对象语义阅读、精确子树比对和差量阅读，没有把数百万行重复原字节逐行再读一遍。具体覆盖为：

- Native：633 个实际 body；其中 353 个 head 的共同结构及全部成员/前驱/用途差量，30 组 Query/Page/Capture，17 合同；80 个 closed Schema bundle 的1404递归定义与已经全文语义读取的 master417完全相同。
- Private：21 个业务对象、40 current manifests、7 Plan/Request/Page、28 公共视图；嵌入原文、原字节和重复实例经过精确相等后复用。
- Source/public：7 packets 与 private 对应部分精确相同；4 conditional CORE decision、23字段码与所有额外规则实读。
- Authority：84 tuple 的全部字段结构与每个 stage/mask/endpoint 差量；core字节、artifact、原 head、原 principal 和共同 SDK wire codec；不称作实际授权验证。
- Canonical：全部顶层规则、当前633原事实的符号模型对应关系、21私有对象、40current cuts、30queries和6capture closures。另有147个未进入当前 native registry 的旧模板/保留 authoring 项，仅定位身份和类别，未逐个完整语义阅读；其中126个旧 head scaffold、21个其他项，逐项路径与行段列在 evidence，不能并入“全文已读”。
- 本册不认领完整 local/UoW/physical、97 Owner 叶及游标的阅读；叶合同这里只针对 PC08 core/wire codec 身份作定向核对。这些由其他附册独立记录。

本册范围内七份当前必要成员的规则整合完成，两项静态差异保持开放。此状态不替代根台账对整个 EXC 的剩余必要材料、专业采用、实际 Provider、实现与运行验收的判断。
