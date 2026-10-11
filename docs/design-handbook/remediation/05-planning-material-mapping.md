# 计划与生产材料消费补充 R1

本补充关闭 `OPS-IF-019 / PLAN-IF-001` 的双边映射缺口；两编号保留为同一问题来源。MAT 原 RemainingBasis 及其摘要不变，新增 `MAT-MRP-RESIDUAL/1` 由 MAT Owner 生产、Planning 读取。它不创建 WO/PR、领取库存、解除保护或替 Finance 关账。

## 1. 新只读合同

`POST /internal/mes/material-obligations/{materialObligationId}/mrp-basis/v1/read`。服务身份必须获准 `MAT_MRP_BASIS_READ`，Tenant/Actor/Producer 从可信上下文取得，body 不能重指定。服务身份授权必须同时覆盖绑定的原义务、目标 Planning scope 与用途；隐藏原件只能由原 Owner 提供准确覆盖证明，不能在 Planning 端填零。

请求为 `MAT-MRP-RESIDUAL-READ/1`：`requestKey`、`mappingVersion='1.0.0'`、`bindingRef`（MAT Evidence）、`expectedMaterialRevision`、`requestedScope`、`asKnownCut`、`basisKind`。Ref/Evidence/Scope 逐字段保持 MAT 原三字段 Ref、外包 digest Evidence 和 portion 区间类型；无通用五字段 Ref 强转。`basisKind` 只允许 NET_RESIDUAL 或 GROSS_WITH_EXPLICIT_OFFSETS，表示要求生产者给哪一种明确表达，并不让调用方指定数值。

200 返回完整 `ResidualHandoff`：准确原生 RemainingBasis 引用与原 bytes digest、完整 MaterialBinding、SourceLineage/NeedNode 引用、mappingVersion、请求摘要、请求/实际覆盖 Scope、Owner 向量、当前代表集合、原责任份额、BasisKind、inputQuantity、offset 明细、alreadyAppliedOffsetSet、excludeFromFurtherNettingSet、仍占用的保护、transition、未决动作、coverage、canCloseMaterialResponsibility、knowledge 和允许用途。

原 MaterialBinding 全部保留：Root/WO/Snapshot及摘要/MaterialLine/Obligation/Operation、MBOM occurrence、Item、Purpose、Site、executionScope、UOM、usage/loss/substitution、transition 和 sourceProtection。不能因为 Planning 不显示某字段而从身份比较中删除。

400：未知/重复成员或非法基础类型；401/403：当前服务身份/用途不成立；404：不可透露或不存在的义务；409：原请求键异体、expected revision/Scope/绑定不匹配；503：原 Owner/完整 cut 不可取得。错误返回稳定 code、traceId 和恢复建议，不返回隐藏数量/原件。新 code 的范围由本新合同限定，不塞入 MAT 原 ActionKind/ErrorCode。

读结果以 `(Tenant,consumer,mappingVersion,requestKey)` 定位，冻结 requestDigest 和准确来源向量。相同键同体只可在当前读取权限下取原结果；原结果是历史 as-known 证据，不保证当前可用于新 Seal。异体 409。重新获取当前事实使用新请求键；读响应丢失只查询原键。该读取可保存只读 Capture/Receipt，不产生材料业务效果。

## 2. MAT 生产规则与两种量义

MAT 在一致的来源 cut 下读取原义务事实、准确 WO/材料绑定、TransitionManifest、确认消费/退回/更正事实、reservation/line-side 归属和未决动作。禁止从 `planned-issued`、WO.CompletedQty 或当前 BOM 重建旧义务。

`responsibilitySlices` 的每项明确原义务/份额、实际当前代表、准确 Scope、grossQuantity、同量纲依据和产生该责任的原证据。转移的每个份额只能选择一个当前代表；完整且不重叠的份额量之和为 G。原 planned 只有与这一准确代表/Scope 的原依据一致时才能用作其毛责任，不能无条件复制。

每个 offset 含 OffsetId、责任份额、原事实/版本、数量/UOM、抵扣类别、Scope、适用节点、资格证据、包含/转换关系和仍占用保护。把已耗、线边、预留分为具名阶段，不把三个总数相加。只有原政策明确可抵且交集已证明的覆盖集合 E 才参与 `μ(E)`；同一实物由预留转线边再消耗时选择其当前覆盖，不三次抵扣。

| 模式 | inputQuantity 的准确含义 | Planning 处理 |
|---|---|---|
| GROSS_WITH_EXPLICIT_OFFSETS | Owner 当前代表毛责任 G | `R=G−μ(E)`；负结果为不一致，拒绝正式采用 |
| NET_RESIDUAL | Owner 用上述完整份额/抵扣证据计算并发布的净值 R | 原值使用；alreadyAppliedOffsetSet 不再扣 |

两模式均给出相同的原责任、offset 和保护证据；Owner 生产时必须自核净值及 gross/offset 关系。不能为不支持该模式的旧 DTO 补一个默认标签。`excludeFromFurtherNettingSet` 必须包含全部已使用 offset 所代表的供给份额，仍真实占用部分同时保留在 protection 中；从再次净算池排除不等于释放占用。

任何缺失/歧义/相交未决作用域使 knowledge 变为 PARTIAL/UNKNOWN，inputQuantity 不可作为正式承诺数量，`eligibleForFormalSeal=false`；保留已知诊断及缺失范围，不缩小请求 Scope 假称完整。只有准确已证与请求完全不相交的未决动作可保留为旁域限制。`canCloseMaterialResponsibility=false` 本身不等于数量0或读取失败；已完整、仍有合法责任和保护的输入可以供规划，不能据此关账。

## 3. 当前性、完整性及重复处理

正式 Seal 必须重验 mapping/native-contract pin、Binding、整个 Owner 向量及 source/protection/transition 当前头。发生质量 Hold、退料、更正、转移或身份撤权时，旧 Capture 是历史，不能继续作为当前准入。获取不到当前证据时阻正式新效果，查询原结果仍按当前授权办理。

Scope 区间由原 Owner 给出真实份额映射，禁止按比例猜坐标。分页必须固定同一 cut 且有最终完整性证明；来源集合不全、序列有洞、缺失保护或资格时诊断可保存，正式 Seal 拒绝。每个原份额一次计算只采用一个模式，不能把 gross 与 net 两条结果相加。

Planning 的 ConsumerApplication 沿用 `(consumer,targetKey,sourceFactRef,applicationVersion)` 唯一性。RECEIVED 与 APPLIED 分开；同事实不同 MessageId 不能重复生效，同版本异体隔离。重建只写投影/RebuildReceipt/Audit，不写 Consumption/Stock/Claim，也不再发送一次数量效果 Outbox。

## 4. 必须保存的连续算例

### 净剩余和已抵集合

Owner 净剩余40已包含其已抵20，新独立 MAKE 派生180，额外自由50不含该20：`40+180−50=170`。禁止再次减20得到150，也不得把20交给其他需求。已抵20中的10受到 Hold/退料/纠正后，先使旧40的当前适用性失效；只有 Owner 后继明确发布50并有完整新证据时才采用 `50+180−50=180`，旧170保历史。

### 80→50+30 代表替换

原派生80经准确 MaterialTransitionManifest 转给 WO 材料行50、保留30、待决0。固定 transitionId/revision、原 derived obligation/occurrence、真实 WO/materialLine/obligation、UnitBasis、currentRepresentativeRef、SourceProtectionRef 与原 receipt。当前输入为50+30=80，禁止旧80+新50=130。TRANSFERRED50是责任代表替换，不是已满足量，不再减50。

同 transition/revision 同体用原 receipt，异体冲突；pending>0或后继未知保原保护，不重造剩余责任。前40真实已发生、余60合法移交的取消例只对准确[40,100)证明无新执行，根关联总100与取消事实范围分列。

## 5. 其他计划设计资产的明确决定

| 登记 | 本次决定与保留项 |
|---|---|
| PLAN-IF-002 | POL/SUP 功能接受不能替代 ATP R11 精确端口实例。按准确端口、schema pin、Scope、完整来源、全写者最终门登记采用；缺任一 Required 条件则保 FENCED |
| PLAN-IF-006 | StoredLegacyAnchorRecord.ReportedRequiredQty/ReportedAvailableQty 改用原 SignedDecimal，保历史符号及原值；正式新责任仍按自己的非负类型，不 abs/clamp/补0 |
| PLAN-IF-007 | Receipt20 ACTUAL_AVAILABLE 选择原 F014/F016 的 `2026-10-04T18:10:02Z`；PREPARED/PUBLISHED 投影和图发布不可变后继，原18:00对象保历史。所有相关摘要与引用随新body重新生成 |
| PLAN-IF-008 | MATERIAL_OLD/NEW 两图的算法引用采用 MAT_INPUT/RUN/ATTEMPT 的完整 MAT_ALGORITHM_V2（2.0-proposed）身份；原 F025/I1 保留。算法支持8类输出不要求每次产出8行 |
| PLAN-IF-009 | branches 是逐portion配置，三种 branch 枚举不等于最多三条。新 producer/profile 版本去掉 inline maxItems=3，与 closed/consumer 类型一致；逐完整 allocation tuple 做双向集合等值与去重，禁止取同branch首项 |
| PLAN-IF-012 | PO15 OpenAPI 503 同步当前 API_REGISTRY 的 CREATION_CLOSURE_REQUIRED_FENCED、CREATION_CLOSURE_TRANSPORT_INVALID、OWNER_RESPONSE_STATUS_INVALID，不改变底层关闭语义 |
| PLAN-IF-013 | 18 个 PlanningFactHead 包装 ResourceKey 采用对应实际 Row.ResourceKey，实际数据行不改；包装和主键不一致时导入拒绝 |
| PLAN-IF-014 | 同 event/leg/command 的 revision/head 原件采用原 native receipt 的 request/result/snapshot digest，BodyJson和对应摘要连贯生成；不能只改引用而留下原body摘要 |
| PLAN-IF-015 | CP19 step1 executionRequestKey 采用被冻结请求与前驱共用的 `CP18-MAKE-579b4a48-c253-5aa9-834d-1847c3733973`；结果、捕获字节及其依赖摘要同步新算例，原响应保历史 |
| OPS-IF-006 | 连续 MES-WO 算例的 AUTH-PROD-001 采用原 Source/Check 的 version2，修候选 PURPOSE-BASIS及其依赖摘要，不伪造 v1→v2 许可 |
| OPS-IF-007 | UNSTARTED-60 取消事实 Scope 明确[40,100)，根关联100另保；前40不是无执行证明范围 |
| OPS-IF-008 | 准确新wire统一 SUCCESSOR_SCOPE_HELD，ALREADY_HELD仅保旧例历史 |

这些决定还须对应具体修订资产和验证记录；本页中的修订方向不能代替摘要重新计算或类型/样例检查。全局处置表若仍标 IN_PROGRESS，该项不能视作已验收。

## 6. 已有设计和有意差异

QA-POL 采购按每 source/part 叶总体选择政策，制造已有完整总体按自己的规则；不能统一成母单数量。IQC R5 的 ORDINARY_STOCK_USE 是准确 QAP 后继作用域，NCR 对 IPQC SourceEnvelope 的无损适配已有指定后继，均保范围不外推。SLOT/REP 消费 Transfer 时必须匹配原 SourceDemand/完整 current 门，不能以 NEW 同名枚举证明实际提供者可用。

SCHED 改机/改期仍按完整 Root 组和六阶段处理，原 WO 改期白名单不扩大。PERF 要求原 Owner 槽、局部暂停及真实分窗量；R2.1 已取消“WHOLE_SHIFT 必暂定”的旧组合，CLOSED 的两个分母仍必须在同一发布门齐证。上述设计完整性和实际实例/运行验证分别记录。

## 7. 准确依据

MRP R03 63–98 行，MAT v1.1 49–74、329–351、485 行，以及 WO v1.1 转移/取消条款；完整路径与 SHA 见 `00-disposition-register.json` 中 DR-MRP-MAT。其他行分别关联同表的原 finding_id。本轮复用此前有界阅读并补读上述核心段，未声称通读每个大JSON。

## 8. 商业与计划相接处的采用清单

| 原登记 | 明确采用什么 | 不满足时的处理 |
|---|---|---|
| COM-EST-PRICE-COVERAGE-V2 | ERP-EST R2 v2 使用完整 PRICE coverage、其准确 coveragePolicy 版本、Scope/单位/有效时点及最终保护；提供者的整张覆盖结果与最终保护参与者分别注册，消费 ERP-QTN 保原估算用途 | 老PRICE列/签名不具备这些证据；缺覆盖、政策身份或保护，阻相关新估算/报价效果，不读取当前单价补旧证据 |
| COM-FSC-FACT-GUARD-EXTENSION | FSC R2 新EST事实读取按准确事实版本与用途固定；QTN同次检查原关系和事实两个守卫，事实纠错与客户撤销分别处理 | EST I03旧Usage仍保原域，不能以新事实用途静默扩大旧I03；缺一个守卫保持该路径FENCED |
| COM-RFQ-PR-SOURCE-STATE-ADAPTER | PO v1.1 Source内部 EXECUTED_PENDING_LEDGER，PR内部P→E，RFQ异步P→C各有原责任层；原ClaimRecord CONSUMED等wire不变 | 按原receipt/complete manifest推进本层状态；网络已接收不能提前把另一层标已入账，不为消除名字差异创造新wire枚举 |
| COM-GR-ORIGINAL-SCOPE-AND-COMPOSITE-ADOPTION | GR原逐叶Domain+有效RangeSet+当前ScopeSet/Quality绑定，消费Stock v1.1.1/PO v1.1和专业QA后继的完整共同ResourcePlan | 非前缀VOID不改回旧前缀算法；商业验收与MATCH只用准确叶范围，缺专业映射/共同事务实例时阻新效果 |

采用记录必须固定提供者、消费者、contractId/version/schema SHA、允许Scope/用途、全部写者、实际当前门、原文受理/应用区分及运行证据引用。本文已给出所需设计边界，真实记录在编码阶段由部署配置和Owner实例填写；不得用本手册的静态PASS自动填actualAdopted=true。

MRP/MAT机器合同和10个正反设计样例在 `assets/material-handoff/`；读取原键入口是 `GET /internal/mes/material-obligations/{materialObligationId}/mrp-basis/v1/reads/{requestKey}`，仍以可信tenant/consumer域定位并做当前读取授权。无法取得请求所指准确cut返回503；成功读取一个明确标为PARTIAL/UNKNOWN的原事实可返回200诊断，必须禁正式Seal。不能因响应200就忽略knowledge与coverage。

`offsetCoverageMembers` 是Owner明确给出的原实物份额标识集合，每个份额数量及责任归属必须有原证据；各offset引用其覆盖成员。计算量采用成员集合并集，每成员只计一次，净模式记录全部已抵offset，毛模式保持已抵集合空；两模式均将全部已用成员排除再次净算。份额别名、不同UOM、交集未知或来源不完整由Owner显式拒绝正式量，不能用集合ID不同推定物理不相交。

## 9. Planning / WO 规范夹具后继

PLAN-IF-007/008/014/015 与 OPS-IF-006/007/008 的生成入口和精确预映像见 [planning-fixtures](assets/planning-fixtures/README.md)。`tools/build_planning_fixture_patches.py` 只读固定 SHA 的原件，在内存生成四个全新 sealed EXC 世界、395 个原生依赖文档、来源页及新 CaseRevision/Head 绑定；原版本保持历史。完整新包的 SHA/bytes、四世界 84/116/188/188 个精确成员、61 个摘要依赖变更和所有 native/query/capture/artifact 后继在 manifest 中可复核。源 package 不整包复制入仓库。

同目录的有界补丁修正 SC06 receipt→revision/head 两份正文及全部摘要，CP19 23 处 execution key 和 44 处结果摘要副本，以及 WO 新示例的 v2 gate、UNSTARTED60 `[40,100)` 和正式 `SUCCESSOR_SCOPE_HELD`。旧 WO 占位摘要仍是历史说明，真实可复现 v2 后继明确指向 SC06/CP19。对象闭包的静态通过不代表历史 SQL ledger 已承载新数据；新 Case 引用不再指向旧全仓快照，实际 Owner/Core/Store 采用及运行仍为 UNPROVEN / NOT_RUN。
