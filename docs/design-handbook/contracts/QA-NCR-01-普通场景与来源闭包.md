# QA-NCR-01 普通场景与来源闭包

本册解释 R12 条件普通数据中的业务关系，供实现源适配、close 和 H30 时定位完整实例。源件为 `NCR-R12-NORMAL-DOCUMENT-VALUES.json`，SHA-256 `c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9`，52,670 行、171 个 values。全部业务字段及完整递归差量已读，原件字符串中的 Base64 JSON 经只读解码与具名 body 对照；重复子树精确复用，未逐字重读重复编码字符。原件明确 `actualOwnerFacts=false`、`candidateProfilesEnabled=false`、`runtime=UNPROVEN`、`businessAc=NOT_RUN`；条件 authority、source、adoption、IAM、topology、专业 proof 不是实际 QUALIFIED/ACTIVE/APPLIED 证据。[原文声明](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:1)

## 1. 三种初始来源必须保留原件关系

三个来源分别给出 OriginalAnchor、typed source read、final guard、SourceProjectionBasis 和 SourceBinding。`sourceUseKeyDigest` 使用 `QA-NCR:MAIN-SOURCE-USE-KEY:6` 摘要域，payload 为环境、sourceKind、完整永久 root 和固定 `sourceUseDomain=QA-NCR:INITIAL-SOURCE:6`，不含 version/digest；原版本身份另在 SourceVersionBinding 保存，同版本异摘要冲突。不能用换版本绕成第二张 NCR，也不能只按业务单号或 latest 去重。[准确摘要 payload](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/eb/ebb0386a3a64d072__NCR-R6-DIGEST-DOMAINS.json:143)Projection 是 NCR 的派生解释，SourceBinding 仍引用准确 original 和 projection，不能把 projection 自称专业 Owner wire。[IPQC 全组](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:69)

- **IPQC EXEC**：保完整 Decision、原 API 与 standalone ExecQaSource、QAP context/Resolution27、family/task/plan、产品 baseline、unit/coordinate assignments。用途为 MASS_PRODUCTION。最终门同时绑定 source、任务与族当前 head、mapping、authority/registration、完整 writer 与真实 UoW；既存 context 的读取不是重新解析 policy 的 mutation。
- **IQC**：保完整 Disposition、sourceBundle、PUR/GR 与 supplier/product/technical baseline、scope/mapping/alias、effect map，最终核 IQC scope/observation/disposition/family 及 writer heads。不能用 NCR 对其来源的解释替代 IQC 决定。
- **RMA**：来源为 POSTED 的物理 receipt，20 EA、范围 `[100,120)`，带原 shipment/order fulfilment、return share/range claim、WMS-IN/Stock 与 correction anchors。没有原 technical baseline 时示例明确 UNKNOWN；用途 FIELD，不凭产品号补造有效 baseline。

三组完整数据位于 values 0–14、L69–5893；三者各自的完整来源结构必须单独实现，不能统一删成只有 id/version 的通用 Ref。[三来源原件起点](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:69)

## 2. Stock close 的 15 项义务与提交顺序

条件分区是 12 EA `[100,112)` 的 USE_AS_IS 与 8 EA `[112,120)` 的 SCRAP。close basis 精确覆盖 15 个 CKey；四项 REQUIRED 分别为 QualityApplication、QualityApproval、ScrapPhysical、TechnicalBaseline，其余十一项各有显式 NOT_APPLICABLE 的 policy/source authority、scope、purpose 与原因。空集合或“不需要”字符串不能替代 NA 依据。QualityApproval 覆盖全部 20 EA；Stock quality application 和技术使用依据覆盖接受的 12 EA；WMS-DISP 实际报废依据覆盖 8 EA。[完整 close basis](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:5894)

Stock quality proof 保 application command/result、目标 object、range mapping 和 current head。报废则另保 StockRequest basis、实际 stock/object/location heads、mapping policy、expected head，以及 APPLIED 结果中的 movement、stock transaction 和原 receipt。质量决定本身不会生成这组物理事实；财务观察 UNKNOWN 也不会因质量 close 自动变成成功。

实例给出有限无环顺序：原件 anchors → close basis → MainResult → CLOSED MainHead 19 → 各专业 guard/use → close receipt → slot/checkpoint/fact maps。MainResult 不提前反引未来 guard/use/receipt；receipt 记录 GlobalRv 27 与 0–14 ordinal。15 个 guard/use 绑定同一实际 UoW 与 effect，成功才一起持久化。LOCAL_QUALITY、PROJECTION、PHYSICAL、PLM 的 locked vectors 各保各自源 head、authority、writer、membership 与专业对象，不能只锁 NCR head。[结果与提交链](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:5894)

这些对象仍是条件设计值，`DOC`/`COND` 形式的 authority/producer 名称不能直接注册为生产权限。完整 Stock close 图在 L5894–17234。

## 3. NON_STOCK close 不借用库存效果

另一世界以 IPQC MASS_PRODUCTION 来源关闭同样的 15 项义务。QualityApplication 与物理退役的负责 Owner 为 MES-EXEC-01；NON_STOCK context 绑定原专业对象和待处理的 8 EA 范围，request 为 RETIRE_OBJECT，包含 expected object head、quality basis、range mapping、technical restriction、permission 和合同 profile。[NON_STOCK context/command 起点](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:20620)

APPLIED 结果必须有对象状态转换、child result、负责 Owner commit 且无剩余待处理范围；NCR 决定或质量批准不足以证明物理完成。request/result 各保 OriginalAnchor，raw SHA 与 Owner semantic digest 分开。NON_STOCK_PHYSICAL 最终门除公共 authority/source/writer 外，还锁负责对象、requirement、operation、result 的 current heads。此分支不造 StockMovement，也不表示当前 MES 已实现或实际采用该 adapter。完整世界在 L20620–33176。

## 4. H30 输入准备、发布与历史重放

H30 仅输出接受的 12 EA portion，不把全部 20 EA 或 RMA 全单量当 REACCEPT。SourcePortionIdentity 带环境、NCR、来源 binding、physical return、inspection scope/source version、decision/partition、范围与 FIELD purpose；QuantityUnitBinding 另保 Finance EA、原 unit/range mapping 和 source capture。Owner tuple 的 owner/sourceBusinessKey/commandKey/inputDigest 四元身份完整，不用 publication 的新 body 覆盖旧 tuple。[H30 generation 1](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:17339)

Qualification/registration proof2 使用各自域、NUL 与完整 nonself payload；内部 `QA-NCR` 与外部 `QA-NCR-01` 经 OwnerNameBinding 明确桥接合同、schema、result codec、scope/service/adoption/qualification/registration codec，不能全局 rename。四份 external binding 为 BASIS、APPROVAL、HEAD、LOCATION，每份都保原 body、bytes、外部 SHA/codec、local ref 与 name binding。质量可用性和技术可用性共同形成 REACCEPT 依据，不产生 Finance value。

PREPARE_INITIAL 由 selector 形成 receipt 与交付 tuple（InputBinding、Control、request、preparation）；InputReady 是有权通知，`doesNotAuthorizeFinanceEffect=true`。完整 Control snapshot 保本地 head/record/source、决定批准、source controls/reads、技术可用与 restrictions、unit/location/policy、qualification/registration current heads、encoders/writers/authority heads。随后才能 Quality basis → external4/result bytes → Publication → current publication CAS → notice/audit/maps。[输入到发布的 DAG](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:51900)

generation 2 在本地 head 18→19 及 qualification successor 后生成新 Control/InputBinding/commandKey/inputDigest，保同 sourceBusinessKey、同准确 portion 与 predecessor links；REPREPARE_CURRENT 校验旧 control。旧 tuple lookup 返回历史 FOUND_INPUT 但 `isCurrent=false`，Owner query 为 STALE_HEAD；旧 prepare 重放不续 currentness，旧 publication 和 Finance use 均保留。新 tuple 仅在条件世界为当前 FOUND。不能以重放旧成功绕过 source/authority/current 最终重核。[generation 2 与查询](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:33177) [顶层生命周期声明](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:51913)

六种 query 状态均有独立语义：FOUND；STALE_HEAD/REPREPARE；NOT_OBSERVED 且 `provesNoEffect=false`；UNKNOWN/QUERY_ORIGINAL；PENDING/语义尚未映射；REQUIRED_FENCED/待 qualification。Finance 最终消费必须让 H30 FinalRead、Guard、Use 与准确 publication/query/result/head/input/control/qualification/name binding 及 Finance effect 在同一真实 UoW 绑定。条件示例没有执行准备、发布或消费。

## 5. EXEC/OUTPUT 原协议与 candidate adapter 的边界

OUTPUT 原 API 的旧五成员 receipt mapping 与 standalone OutQualitySource 的完整 `receiptSubject` 分开保存；后者绑定 MES-OUTPUT document/execution line、output `[100,120)`、root `[500,520)` 及原 conversion。候选六成员 envelope 从真实完整原件无损投影，旧 API bytes 保持不变，`candidateProjectionIsOriginalOwnerWire=false`。另有显式 complete-response candidate10 mode，必须由登记 profile 选择，不能靠解析失败后换 schema。[OUTPUT 全组](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:45042)

values141–170 补齐 EXEC/OUTPUT 的原 TaskDetail、FamilyContinuity、PlanInstance、Resolution27、shared FamilyControlHead、DirectorySnapshot、QAP FamilyCut 和 IPQC FamilyCut 及各原件 anchor。TaskDetail.planInstanceRef、Decision、Context 必须为完整同一 Evidence。FamilyContinuity.ref 是 Main Quality 授权 Registry 预登记族，TaskOwner 复用该完整身份；Main 不另发第二个 family ID。原件 hash profile 为 `SHA256(canonical original body)`，不加 wrapper/domain/self ref。[原 task/family/plan 全组](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:50117) [精确关系声明](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:51964)

Shared directory 的 COMPLETE 不是只数一条：示例同时提供 key、revision、member heads、coverage 与原 family cut。变更或缺失成员仍需 Owner 的准确 current gate。IPQC FQC 与 EXEC/IPQC 世界使用各自 stage/family/task，不能因名称相近混用。

R11 direct-dependency 声明尤其限定：EXEC 对应完整来源及依赖的 NON_STOCK close 图；OUTPUT 是独立替代来源世界；H30 两代保原 RMA source binding 的 R10 body bytes，没有声称 IPQC 来源的 H30 row 已存在。未来实际 IPQC 使用仍须同一 corrected read/current family/plan 关系。[依赖范围](D:/CP6-archives/consolidation-20261010/objects/c211637f002e62fb7677c54f7004b09c59d7c546f2f284d7432476e9d40252c9.json:52662)

## 6. 覆盖与验证边界

本组 Owner215、correction29+34cases、CP4 255、Main171 四件普通数据通过 4,823 个可逆节点表示逐新增字段/差量语义阅读；1033 个内嵌 Base64 JSON 解码与 body/raw SHA 静态对照无差，4 个来源投影往返无差。该数字证明本次阅读表示与来源的保真，不等于 schema validator、业务程序、SQL、真实 UoW 或验收执行。详见 [独立补读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-NCR-01-supplement-reading.json) 和 [Owner/CP4 附册](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/QA-NCR-01-返工消费与Owner合同.md)。26 项准确责任采用另由主代理读取和保留真实/历史范围，未用候选数据中的 adoption refs 代签。

## 7. 保留补例和版本入口

保留的R7九对象把同feedbackKey下draft1/2及各自submission digest分开；v1结果不能满足v2。Correction协调锚准确version2。两个serial映射12+8 EA与范围[100,112)/[112,120)，serial数量2不能当20EA计量。1205条固定cut历史示例给13页计数及最后5条完整typed记录；末页complete仍不续专业current、不能作close basis。保护性WITHDRAWN只需准确本地predecessor及撤权能力，不因技术源不可用阻止撤销，也不等于技术额度释放。它们是保留语义补例，R12 ACTIVE入口的当前主普通数据仍为171对象。[R7原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2c61498ea376adb__NCR-R7-NORMAL-DOCUMENT-VALUES.json:1) [R12 ACTIVE入口](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f9/f98ed1c8a0fdb9d4__NCR-R12-ACTIVE-ARTIFACT-INDEX.json:1)

旧业务CP2只允许A09绑定执行准入，head5→6仍OWNER_PENDING、work仍PENDING；A10/A14严格解析与当次权限/lease后503零写，A16仅恢复A09，A17不显示dispatch token。当前CP4_R1继承旧事实、另建完成/重检/关闭消费设计；不能因后继合同已设计采用而改写旧v1路由结果或把缺失CP8当已恢复。[旧生命周期](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5e/5e055b5492379437__LIFECYCLE-MATRIX.md:1) [准确CP4_R1独审边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ae/aed63d930d1365f1__INDEPENDENT-STATIC-REVIEW-CP4-20261003.md:5)

R12主文仍保Main6/R9/candidate10等精确wire家族名；文件前缀不是部署版本。SOURCE-PINS中缺历史coreCP8/dispatchCP6/R5/母版措辞按冻结时状态保留；真实R12设计接受以UA/CURRENT和Q1–Q4为准，不能按较早manifest的candidate状态否定后发接受，也不能把后发接受倒推真实运行。[source pins](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/234a638911960eef__NCR-R6-SOURCE-PINS.json:1) [实际CURRENT](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9aa6d4aae771428d__CURRENT.json:1)
