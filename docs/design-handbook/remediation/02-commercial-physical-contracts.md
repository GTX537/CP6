# 商业域物理契约补遗 1.0.0

补遗身份：`CP6-COMMERCIAL-PHYSICAL/1.0.0`，2026-10-11。本文件是本次获授权设计修订的新版本，状态为 `DECISION_COMPLETE_CURRENT_TASK_SUPPLEMENT`；不是原 UA 的追认、生产 Owner 签署或运行验收。实际采用 `UNPROVEN`，实现 `NOT_IMPLEMENTED`，业务/并发/迁移/部署验证 `NOT_RUN`。原件、原 SHA 和原历史接受范围全部保留。新物理名称只在本补遗命名空间生效，不反写历史文档中的提案。

范围是 `COM-ACC-PHYSICAL-WIRE`、`COM-ORD-FUNCTIONAL-PHYSICAL-BOUNDARY`、`COM-ORD-QTN-H07-SUCCESSOR`、`COM-HO-OLD-FACTORY`、`COM-QTN-FACT-H07-R2`。本补遗决定实现时应采用什么契约；真实宿主的接线、密钥、Owner 实例、旧数据盘点及所有 writer 的采用证据仍须实施时证明。

规范资产：

- [OpenAPI 3.1、封闭 DTO 和路径](assets/commercial/commercial-wire-1.0.0.openapi.json)：A01–A13、ACC 当前关系查询、Sales 草稿及生命周期、H10/H32、Demand 控制/源纠正/重评/组启用。
- [外部 Owner 精确绑定](assets/commercial/commercial-owner-bindings-1.0.0.json)：指定原件 SHA、行段和完整原类型；并不定义任意 `object`、无类型 `data` 或万能业务服务。
- [140 逻辑字段逐项处置](assets/commercial/ord-dem-fields-1.0.0.json)：每个原字段原号、必需时点、Owner、校验及物理归属均保留；不是生成 140 个数据库列。
- [实读证据和 SHA](assets/commercial/evidence-reading.json)、[验收场景](assets/commercial/acceptance-1.0.0.json)。只声明清单中的原件选段实读，不声明全包逐字阅读或今日源码审计。

## 1. 版本选择与准确覆盖

| 决定 | 当前采用 | 明确保留的旧身份 |
|---|---|---|
| W1-D01 | ACC R01/R02/R03 + CR09 + UA 功能语义映射到新 `/api/crm/v2/accounts` | 原 C03 成功字段仍为 `BusinessPartnerKey(string)`；不改写为 `BPKey` 或 Guid |
| W1-D02 | 新 `ACC-MAPPING-DECISION/1` 单独承载完整 Scope、Owner 唯一选择、资料版绑定 | 独立 83,331B ACC DRAFT 的七字段接口及 BP D-BP06 保持原样；更高 mappingRevision 不足以启用 R03 当前资格 |
| W1-D03 | ORD/DEM 功能承接采用 `ORD-DEM-SOURCE/1`，生命周期采用不可变预览计划 + 原键应用 | R02 140 项为逻辑字段；PB01 未批准历史所有物理候选值，本补遗参数是新决定 |
| W1-D04 | 报价来源按已接受 QTN R2 Q33/Q23/Q24/I10 创建真实 OrderDraft 并分配坐标 | 原 R02/R03 H→C 仅为历史功能解释，不实现第二 Claim 账、不发明 QTN 已有 ONE_SHOT/LIMITED_QUANTITY 枚举 |
| W1-D05 | 对新商业效果同步核 QTN 当前 Fact；对原命令返回当时原件 | Fact 纠错与客户撤销、Control、订单取消、Allocation 释放独立 |
| W1-D06 | 新 Main writer 按来源意图/责任范围唯一，允许同 Opportunity 多订单 | 旧固定源码的一商机一单工厂留历史证据；不宣称今日代码已改或已验证 |
| W1-D07 | 单份额 H10/H32 使用 Sales Outbox → Planning 本地原子接收；不可拆组使用 Planning 同库成员门 | 不把跨 Owner 消息传递称共同 DbTransaction；不自动提供跨库降级 Saga |

证据：ACC `ACCEPTANCE.md` L13–36、`RECEIPT-MAPPING.md` L36–55；ORD R02 L17/L284、R03 L305–351、PB01 L66；QTN R2 L688–710；HO L404–408/L711。准确路径及 SHA 见证据资产的同名条目。

## 2. 共通物理规则、权限、错误与结果恢复

新 wire 使用 UTF-8 JSON，拒绝重复属性、未知属性、超界值、指数十进制及未配对 surrogate；不静默截断。所有封闭 DTO 的 required/nullable 以 OpenAPI 为准。外部 wire 的大小写、数值型与摘要规范仍按原 Owner，不经新规范改写。外部 ID/版本一律保持不透明原字符串；新 Key 最多 256 Unicode 标量，SQL 建议 `nvarchar(512) COLLATE ...BIN2`，按字节/二进制比较，不 trim/NFC/转数值。新本地 revision 为 1..Int64.Max 十进制字符串，达到上限拒新写，不自动换 epoch。QTN 自有 UUID、number QtnRev、Dec、ETag 保持原合同，不被本补遗改型。

新本域规范体采用 `COM-C14N/1`：对象键按Unicode标量序排序、无空白、UTF-8、JSON标准转义；仅本域人工文本按字段规则先NFC/trim，外部opaque字符串不改；整数不用指数，精确十进制先按字符串规则规范；数组顺序保留，Scope候选/覆盖digest集合先按digest升序并拒重复。`H(domain, body)=SHA256(UTF8(domain+"\n") || canonical(body))`。candidateDigest排除自身；MappingDecision摘要仅排除decision.digest和authorityProof；SourceOperation排除sourceDigest。新Command HMAC覆盖规范请求和解析组织/主体，并持久算法/密钥版本。旧QTN/C03等仍按原规范，不能用这个函数重算其native digest。

新本域数量用精确 `decimal(24,6)` 上限及字符串 wire；单位允许的 scale、整包要求、换算分子分母仍取当前 UOM Owner，超出则 422，不舍去输入。新金额不得超过原币种/计价政策范围；QTN 来源继续原 `decimal(19,4)` 正式金额范围及原 minorDigits/HALF_EVEN。业务日期区分 DATE 与 INSTANT：前者仅 date 非空，后者仅 instantUtc 非空；二者都须真实 node/timeZone/calendar。不能以午夜 UTC 替代日期。普通页最多 100 项，商业行/独立份额/组成员每集合最多 200；这些是本版本技术边界，不是企业默认批量政策。超界要求明确拆独立意图，不能拆不可拆组。正文最大 2 MiB，超限 413；完整 Owner 原件另受对应原合同上限，不通过截断造摘要。

可信组织、主体、服务 Producer、环境、ActorPolicy 来自认证通道与当前 IAM 映射，不从 body 推导。新 capability 名是待接线的能力要求，不虚构已有角色。操作权 ∩ 对象/Site/法人范围 ∩ 字段 ∩ 用途 ∩ 当前权益共同成立；服务入口另核受信 Producer/contract/epoch、租户绑定和重放信任。无资格默认拒绝。响应返回前重核适用披露窗口，撤回的字段不从回执或原件缓存泄漏。最小 RQ 不要求今天仍有 edit/Active，但不授完整对象 query。

新人工 mutation 用 `Idempotency-Key`，现存对象另带强 `If-Match`。ACC ETag 是服务端签发的 `"ACC2:{accountId}:{base64url-rowversion}"`；ORD 为 `"ORD1:{orderId}:{base64url-rowversion}"`；Demand 为 `"DEM1:{demandId}:{base64url-rowversion}"`。仅接受一个正确目标强标签，拒 `*`、弱标签、多个同名值；真实提交仍检查数据库 RV。跨 Owner 必需版本也存在不可变计划里，不能仅比 ETag。A03 新建无不存在对象的 ETag。QTN 原 headers 不改名。

新 Command 身份为 `(OrganizationId, Operation, CommandKey)`；target、initiator、规范化版本、完整 body、method/path、初始预条件是冻结绑定，不加入可变唯一键以绕冲突。同键同意图读原终态；同键不同 actor/target/内容不得创建第二效果。保存规范体的加密原字节、密钥版本和 HMAC 指纹；仅保存 hash 不足恢复。幂等先在当前 RQ 下查历史，再在 IAM→Command 范围锁内复查，只有确无终态才走新写权限和业务门。新对象、业务历史、Audit、必要 Outbox 和终态 receipt 同本地事务；不先提交永久 PROCESSING 再尝试业务。明确定义的纯输入拒绝可留本地域墓碑，数据库/超时异常不能被改写为确定未执行。

| HTTP / 新错误码 | 含义 | 恢复 |
|---|---|---|
| 400 `SCHEMA_INVALID` / `PRECONDITION_SYNTAX` | 格式、未知字段、非法 header | 修输入；不改变原未知意图 |
| 401 / 403 `ACCESS_DENIED` | 当前身份/能力/披露不足 | 中性结果；不透露原成功是否存在 |
| 404 `NOT_VISIBLE` | 对象不可见或不存在 | 不用负查询证明未执行；服务原键查询另返知识状态 |
| 409 `COMMAND_KEY_REUSED` / `SOURCE_CONFLICT` | 同身份异义 | 保留两份证据；禁止覆盖或自动新键 |
| 409 `MAPPING_SCOPE_CONFLICT` / `PREDECESSOR_UNKNOWN` | 权威分叉或中间未知 | 查询准确 Owner 原件/完整覆盖，不先到先得 |
| 412 `VERSION_STALE` / `PLAN_STALE` | 已知当前前驱不匹配 | 原意图未知时继续查询；已知未应用后用户明确形成新意图 |
| 422 `BUSINESS_RULE_BLOCKED` / `LOCAL_ELIGIBILITY_CHANGED` | 必要条件明确不符 | 返回安全 fieldPaths/Owner 恢复出口；不是所有远端域的最终失败 |
| 428 `PRECONDITION_REQUIRED` | 缺原键或必需 If-Match | 补原请求条件，不捏造当前版本 |
| 429 `RATE_LIMITED` / 503 `OWNER_UNPROVEN` | 限流、Owner/共同门/密钥不可证 | 原键查询/同内容重放；不得转付费远程测试或启用替身 |

A11/ORD_RESULT/DEM_RESULT 都是已认证只读入口。本补遗新采用安全 locator 跨刷新回查：浏览器只可保存 operation+commandKey，不保存 PII、token、冻结 body；页面刷新不自动 mutation。命令原键和非敏感永久占用墓碑不因 TTL 到期释放，PII 正文按隐私 Owner 实际保留政策处置；失去可验证正文时 `EXPIRED_UNPROVEN`，不允许重用键。NOT_OBSERVED/一次404/后续409/412均 `noEffectProven=false`。true 仅在该执行 Owner 已锁住原命令全部执行入口、原成功优先、持久建立准确原 digest/目标/执行域 Fence 且有当前验证证据时允许；多域未全覆盖不能汇总全局未应用。本版本没有公共“取消未知”接口。

## 3. ACC A01–A13 实施合同

OpenAPI 每个 operationId 就是原 A 号。公共前缀为 `/api/crm/v2/accounts`；服务消费路径与普通页面能力分开。

| 原操作 | method / suffix 或完整服务路径 | 输入 → 输出 | 持久效果/门 |
|---|---|---|---|
| A01 | POST `/query` | PageQuery → AccountPage | 自由文本在 body；授权列表/分页同一集合，无业务写 |
| A02 | POST `/duplicate-candidates/query` | CandidateQuery → CandidateRead | 仅有权候选；reviewToken绑定查询/字段权限/主体/有效期；不证明全局唯一 |
| A03 | POST 根路径 | AccountCreate → CommandReceipt | Account Active、初始资格/资料版、Audit/Outbox/receipt一次提交 |
| A04 | PATCH `/{accountId}` | AccountPatch → CommandReceipt | Active/Retained；省略不改、显式null仅清可空；assignment字段额外权 |
| A05 | POST `/{accountId}/deactivate` | Reason → CommandReceipt | 一次停用修订；无 Main/BP/Contact 级联 |
| A06 | POST `/{accountId}/restore` | Reason → CommandReceipt | 隐私/归属当前可证才恢复；不自动恢复映射/渠道/关系 |
| A07 | GET `/{accountId}` | — → AccountRead | Profile按字段权裁剪；关联 tab 另走其 Owner，Account可见不扩权 |
| A08 | POST `/{accountId}/sources/query` | SourceQuery → SourceRead | 原证明与当前解释分别返回 |
| A09 | POST `/internal/crm/v2/account-source-proofs` | SourceProofNotice → ProjectionReceipt | 精确原 Owner Manifest/Target验证后仅投影；不再执行转换 |
| A10 | POST `/{accountId}/main-navigation` | — → NavigationRead | 仅注册路由ID/opaque handle；不拼 URL/PII/租户参数 |
| A11 | POST `/command-results/query` | ReceiptQuery → ReceiptRead | COMMAND原operation/key或MAIN_REQUEST准确Authority/Account/Request/Version；独立RQ，历史/当前效力/投影覆盖分列；无对象query时不泄ID |
| A12 | POST `/internal/crm/v2/account-c03-results` | LegacyResultNotice → ProjectionReceipt | 解码原 C03 不改字段，只给已知 request 的历史 |
| A13 | POST `/internal/crm/v2/account-mapping-decisions` | MappingNotice → ProjectionReceipt | 全 Scope/选择/资格最终条件比较，仅本地投影 |

创建字段决定：displayName trim 后 1..200；aliases 最多10×80；domains 最多20×300，保存原输入及 `ACC-NORM/1` 的 IDN/URL host 规范值，只本对象去重、不抓站。accountType/industryCode/region 为可空 `{code,dictionaryVersion}`；不把独立 DRAFT 的默认 PROSPECT 强加 R01。departmentId必需、ownerUserId仅当前可分配或有权明确 null；普通选填缺省→null/[]。其余 contact/address/notes 长度以 schema 为准，敏感字段无输入权就省略，不能送null掩盖清空。手工来源仅 `manualSourceReference` 提示，不可宣称转换成功；正式 source proof、Lifecycle、BusinessPartnerKey、version/audit 均服务端只读。PATCH只含duplicateReview拒绝。

CP22 保留准确 native `CreateAccountRequest`（schema `NativeAccountCreateCp22` 作为对照）和原 X-Command-Id。H02 coordinator 根据已持久 conversionId + participantKind + clientRef 映射 Owner operation identity，同连接/同 DbTransaction调用 Account Participant，不能向新 A03 发 HTTP，也不复用浏览器键成为全 Owner 通用键。旧 native 字典值经固定字典版映射；website映射为一个域名来源，缺少的 R01 region/domain等保持未知/空，不编造值。映射有损或未知代码时拒绝适配并保原请求；使用现有账户须比较当前资格和 edit version。原转换已建立账户时 A09 只核 Manifest 包含该 Account，零额外创建。

### 3.1 三条映射协议隔离

1. **C03 v1**：保 `TenantId/RequestId/AccountId/RequestVersion/ResultVersion/BusinessPartnerKey` 或原失败分支。A12 以精确 `ACC-C03-V1` 原件引用读取原字节，通过原 decoder 处理；不复制未完整审定的原 wire 为另一 DTO。请求域 `(Authority, Contract, Epoch, Tenant, Account, RequestId, RequestVersion)`；未知请求隔离、不补建。ResultVersion不跨请求比较。
2. **独立七字段 DRAFT/BP D-BP06**：`POST /internal/crm/v1/account-erp-mappings` 仍恰为 eventId/accountId/legalEntityKey/mappingRevision/state/businessPartnerKey/sourceAsOf。其 `(Tenant,Account,LegalEntity)` 快照及 UNLINKED/null 规则原样保留；ACK仅持久处理，不是 CRM 新用途许可。独立存储槽，不能把较大 revision 复制进 A13，不能凭本地字段补 Scope 或 owner decision。
3. **新 R03 当前关系协议**：本补遗 `ACC-MAPPING-DECISION/1` 与 Main `POST /internal/main/v2/account-mapping-decisions/query`。不是修改上述两份 wire。Main 对同 `(mappingAuthority, scopeIdentity, scopeEpoch)` 持有唯一决定域；Scope 必须包含解析组织、法人、账户、`ACCOUNT_BUSINESS_PARTNER` 用途，scopeIdentity必须由 Owner 分配并与这些维度一一对应。组织不得通过 body 切换。

### 3.2 新决定全文和当前资格

`MappingDecision` 必须带完整 Scope、decisionId/position/digest、明确 predecessor、所有本次候选（mappingId/revision/BPKey/boundAccountBusinessVersion/candidateDigest）、选择结果或 UNLINK、有效半开区间、完整覆盖水位、identityImpactPolicy 和 authorityProof。所有摘要由规范全文算出；digest 不是认证本身。SELECT恰有一个 selectedCandidateDigest且属于 candidates；UNLINK必须 selected=null。SUCCESSOR 要求精确前驱且 position=前驱+1；初始 COMPLETE_SNAPSHOT 允许 predecessor=null，必须 coversEntireScope=true，Owner证明该截止点完整候选及单值状态。快照不能仅自称 complete；完整性由受信 Verifier核验。

新authorityProof引用的完整原件必须满足封闭 `MappingAuthorityProof`：issuer、完整scope、decisionDigest、completeAtWatermark、signedAt、固定purpose、注册keyId、算法Ed25519及signatureBase64。签名对象为 `UTF8("ACC-MAPPING-AUTHORITY-PROOF-1\n") || COM-C14N/1(proof去signatureBase64)`；不可自带信任公钥。Verifier从该组织/authority/用途当前可信注册表解析keyId，核撤销/替换、签发时点和当前可使用政策，并逐项匹配消息Scope/决定/覆盖水位。缺真实注册实例为UNPROVEN，不接受DEMO样例身份。旧C03/BP签名和信任机制不随此新协议改动。

同位置异 digest → ScopeConflict；恢复只能是更高位置的 CONFLICT_RESOLUTION，覆盖所有已知冲突digest、完整当前候选/所退休决定和准确单值选择，不能普通高数字洗冲突。跨 epoch 必须 Owner注册的旧epoch封闭证明、新epoch初始完整快照；缺一拒绝切换。candidateDigest覆盖其全部身份、BP及绑定资料版；decision.digest计算时只排除自身 digest 和 authorityProof载体，不删业务字段；推荐前缀 `ACC-MAPPING-DECISION-1`。这里的 `position` 是 Owner新协议决定轴，不是旧 mappingRevision/ResultVersion。

本版 `ACC-IDENTITY-IMPACT/1` 的绑定资料字段集合固定为 displayName、aliases、domains、accountType、countryCode、province、city、address；真实规范值变化才推进 IdentityBusinessRevision。notes/shortName/industry/region/渠道/归属只推进各自资料或资格版本，不无端作 Main 身份变更。Main authorityProof必须引用这份准确政策或后续明确版本；禁止自行缩小集合。升级政策先由 Main重新绑定当前资料版，旧决定留历史、当前资格待核。

A13 验证 candidate.boundAccountBusinessVersion 恰等当前 IdentityBusinessRevision。最后同一 CRM事务持有 Account资格域与 Scope槽，比较 SlotRevision、Owner watermark、ConflictState 及 `{lifecycleRevision, identityBusinessRevision, privacyAuthorizationWatermark, eligibilityPolicyVersion}`，再写 DecisionHistory/Slot/ApplyReceipt/Inbox/Audit。停用先发生→历史可记但新用 LocalInactive；A13先提交→停用随后推进资格并使新用失败。资料绑定失配→ReviewRequired；证据缺失→Unknown；不自动改Account状态。

`mapping-eligibility/query` 只给 asOf 解释、`isExecutionPermit=false`。每次 Main新商业使用都须同本次事务/已证明 Owner guard重核当前映射、当前BP用途及本地资格；异步 A13缓存不能提供跨库瞬时资格保证。不能共同保护该动作时关闭该动作，不影响有权历史和独立账户维护。

## 4. ORD/DEM 物理边界和 140 字段

新接口只承担本域 mutation/接收/回查。ATP、OA、Finance、WMS、PLM 专业接口继续其 Owner；不是把其所有事实塞进一个通用订单 JSON。140字段资产逐项保留原始业务校验；`WIRE` 是具体 schema/member，`TRUSTED_CONTEXT` 不接受 body，`PERSISTED/READ_MODEL` 是本域事实或计算投影，`OWNER_RECORD` 用原表列明结构的不可变原件，不是客户端任意 JSON。Quote相关021–024明确后继适配。

`OwnerRef` 六成员只是受控原件地址，必须在 `(owner, contractId, contractVersion, evidenceId, evidenceVersion, contentDigest)` 精确定位完整原件；正文不可由浏览器自签、伪布尔或重新构造。消费器用下列有限 allowlist 选择原 schema/Verifier；不存在 binding、未认识新版本或无法读取完整原件即 503/422，不使用通用反序列化。原件保存在受控 EvidenceStore（密文原body、native schema identity、hash、ReceivedTrust、Verifier/密钥版本、范围、观察时间），业务引用与其同租户 FK关联。禁止只保存 digest 后把恢复所需正文删掉还声称可恢复。剩余非命令字段也已落到封闭 `OrderDomainFields` 的精确member/type；`POST /{orderId}/details/query` 按指定版本/行/份额/字段读 `OrderDomainRead`，字段无权则省略并标redactedFields，不返回占位0或裸私有hash。命令不接受该读模型整对象回写。

| 字段/用途 | 唯一允许的原结构/责任 | 最后检查 |
|---|---|---|
| soldTo/billTo/payer/party snapshot | BP准确角色/关系原件；ORD-CON002 TradingPartyScope | 同本次法人/动作/客户角色闭包，不把联系人当BP |
| credit、预付 | ORD-CON002 CreditDecision/ExposureCommitment/PrepaymentCoverage | COMMITMENT_REQUIRED要实际承诺/分配；独立Finance唯一暴露域 |
| payment/delivery/pricing、priceTerms | ORD R02 §10.3 PriceTerms + CON009 PriceBasisSnapshot | 完整币种、PriceUom、basisQty、税/折扣/舍入、有效份额；缺价不当0 |
| approval、confirm/change plan | CON003 ApplyIntent/ApprovalOutcome/ParticipantGuarantee | 准确对象/编辑版/动作/范围/全文；所有批准者当前资格 |
| target conversion/unit/date/technical/purpose | R02 §10.3 Conversion/TransitDerivation + CON011五种专业结构 | 当前UOM、日历节点、适用活动/用途/技术；不得扩大制造许可 |
| shipment/delivered/return | CON008 FulfillmentFact/ResponsibilityMap/DeliveryEvidence/CorrectionAuthority | 原FactId/Version和快照/增量，原数量完整分配；不按OrderVersion丢晚事实 |
| coverage/reservation | CON007 RunResultScope/SupplyOccurrence/ReservationResult | 当前Demand/控制/Policy/供给水位；Reservation含Allocation |
| controls/disposition/closure | CON006 ImpactScopeManifest/ControlProof/DispositionResult/HandoffAcceptance/ClosureAssessment | 指定全部入口和在途原键，无未知执行风险才可结清 |
| CRM贡献 | 已接受 OPP-COMMERCIAL/1 原consumer，CON010仅作ORD功能映射 | Created不Won；责任组/份额/币种/后继不重复贡献 |
| QTN来源 | `QTN-H07-R2` / `QTN-FACT-R2` / `QTN-I11-R2` | 下文完整原件/共同事务要求优先于旧Claim功能图 |

上述合同编号精确 SHA/行段/原类型名在 owner-bindings 资产列出。Owner自身更详细已接受规范优先于 ORD的逻辑要求；必要接缝尚未实现只阻依赖动作，不允许创建伪Owner成功数据。本补遗不新增导入/导出、预测、安全库存或服务MRP入口；134–136/139–140字段仍归原授权查询/格式/快照 Owner，不因有字段变成已启用功能。

### 4.1 Sales 草稿和生命周期

公共前缀 `/api/main/v1/sales-orders`；所有路径/DTO在 OpenAPI 精确列出。POST根只接受 DIRECT_EVIDENCED、EXTERNAL_AUTHORIZED、REPEAT_REFERENCE；Quote必须走原QTN Q24。新草案可缺客户/材料，但 sourceIntentKey稳定；外部身份保存源规范化版本和完整原业务键，保前导零。复购必须新客户意图，referenceOrder只参考，不复制Confirm/Release/批准/许可。

PUT `/{orderId}/draft` 保存完整可见编辑稿，0..200行允许待补；不能以部分字段权限覆盖整个body，缺完整编辑披露时拒绝PUT。服务端生成新行/交期 ID；existing ID须确属本单，删除只限从未正式/外部使用的编辑成员。正式 EffectiveVersion不被保存覆盖。QTN来源行的客户/原报价行/数量坐标/价/税/尾差/Fact/币种不能通过此入口修改；允许与已分配来源一致的交期等资料补齐。

`confirm/release/change/cancel/close-plans` 读取并持久不可变 OrderEffectPlan，包含准确编辑/有效版、逐份额范围、必要 SourceOperations、完整Owner原件、水位、条件和10分钟准备期限；不是许可。`*-applications` 只提交 PlanRef+reason、原键、If-Match，不能替换计划内容。计划过期/任一相关水位变动须重做预览；业务条件不因10分钟TTL尚未到而放宽。计划和完整输入按Actor/组织/动作绑定，最多消费一次；原键恢复仍查原receipt。

| 动作 | 前置与本地写集 | 边界 |
|---|---|---|
| Confirm | 所有份额量守恒、客户接受、BP角色、价格/技术最晚条件、OA及信用/预付真实当前门；写不可变ConfirmedVersion/Confirmation/SourceBinding/Audit/receipt/H08 Outbox | 无Demand；QTN分配不再扣一次；CRM回传未知不再次Confirm |
| Release | 真实ConfirmedVersion与准确Schedule/目标/日期/技术/用途/当前控制；写SalesRelease/Line、每谱系SourceVersion、SourceOperation原body/Outbox/LOCAL_REQUEST回执 | 成功仅本地请求；Planning BusinessResult另返；独立份额逐项，不可拆组另处理 |
| Change | 建独立编辑后继与精确ChangeDiff/影响清单；当前批准/发运/信用/工程再核后写有效商业后继及必要H32 | 纯改价且供给不变需CommercialSupplyScopeMap，不假造Demand量变化 |
| Cancel | CancelSlice分UNRELEASED/ACCEPTED/UNKNOWN，总量等请求；当前R/S水位重验；写取消事实、逐谱系真实目标/控制/Outbox | UNKNOWN不当0；已发变化使计划失效；商业取消不删除PO/WO/已发事实，不自动返报价额 |
| Close | 本次范围必要义务和可能再执行的未知全部闭合，独立后果只可有权承接；写不可变Closure | Demand普通补给结束不表示已签收/财务结清；迟到更正新PostClosureReview不改旧closure |

需要多个本地Main参与者时采用预规划共同事务；有真实跨库Finance等依赖时保存 `ApplyIntent` 的逐参与者保证与原键，只有覆盖本次提交且可串行裁决的保证才能应用。缺保障保持 WAITING_PARTICIPANT；不因QTN同库而推定所有Finance天然同库。QTN对自身严格同UoW的H07绝不降级跨库保证。

### 4.2 H10/H32 精确源接收

`POST /internal/main/v1/demand-sources/operations` 接受 `SourceOperation`；服务信任校验、body.operationKey与运输重投绑定。同一新源自然域为 `(Tenant, SalesAuthority, ReleaseLineageId)`，唯一源版 `(该域, SourceVersion)`；operation唯一 `(Tenant, Producer, OperationKey)`。同原版/同body的新运输别名返回原结果；同版异body冲突。正文 sourceDigest覆盖全对象除自身，前缀 `ORD-DEM-SOURCE-1`；原字节另算 BodyHash，不混同运输去重。

SourceVersion由Sales本谱系单调分配；`previousIssuedSourceVersion`表示上一已发版，`expectedAcceptedSourceVersion`表示真正被Planning接受的前驱。二者不相等是正常可能情形。中间所有非接受版需 `interveningFinalResults` 精确覆盖：v1接受、v2已明确拒绝、v3可沿v1；v2未知不准普通v3越过。紧急 `CONTROL_SOURCE` 是独立控制轴，可高于未知版先保存，仍保原操作未知及其真实后果。

| kind（本补遗新枚举） | target / 前置 | Planning原子写 |
|---|---|---|
| INITIAL_RELEASE | target非null且两个量>0；无既有Demand；无控制禁止；expectedAccepted=null | 一个Demand与Version、SourceBinding、ControlSet、原SourceResult、Inbox/Audit |
| REVISE_TARGET | target为新总量；有真实接受前驱；适用取消/变更依据 | 同Demand后继；30→40结果40，解释delta10 |
| REVISE_NONQUANTITY | target量须等接受目标；技术/期/地点变化及适用门有据 | 同Demand后继，不加量 |
| CANCEL_EXISTING | 确有Demand；target含真实剩余目标，可0；cancelAllocation必需 | 同Demand后继/终结控制；保已发及原接受事实 |
| CONTROL_SOURCE | target=null，controls非空；无需Demand；尚未产生真实SalesRelease时两个release ID为null | SourceControl及结果，demandId=null，不造0量需求或假release ID |
| PREPARE_SUCCESSOR | 完整target、activationManifest，明确前驱与守恒 | PreparedDemand/SourceBinding；不进入有效MRP输入 |

没有Demand时“取消”必须映射CONTROL_SOURCE，不能伪CANCEL_EXISTING空对象。合法整份100且可覆盖60仍接100，覆盖另账。只有 `BUSINESS_ACCEPTED` 原结果带实际Demand身份；业务拒绝保存原输入/原因但不删除此前有效版。`SOURCE_CONTROL_APPLIED` 可无Demand，transport ACK不代这两者。

查询 `/results/query` 同时返回 immutable historical 和 current。历史v1接受100、当前v3目标40同返，不能恢复v1效力。Planning本地锁顺序 IAM/Producer → Operation唯一槽 → SourceLineage缺行范围 → ControlSet → DemandRoot/Version → 当前Membership；同事务固定完整原输入、结果、控制和来源映射。Sales只按可信原结果更新投影。SalesOutbox失败保本地请求，Planning响应丢失原键查询/重发原body；禁止扫订单补造Demand。

### 4.3 Demand 专用动作与不可拆组

`/demands/{demandId}/controls` 仅改变当前Owner有权的 reason/version；解除本原因不解除QA/PLM/Sales原因，TERMINATE不可普通Resume。`source-correction-requests` 只创建给Sales的准确字段纠正请求，Planning不能直接改Sales数量/期/地点/技术。`reevaluations` 绑定DemandVersion/ControlSetRevision，保存本地请求再交既有MRP Run Owner，返回不冒已覆盖；旧Run晚到仍历史。

`POST /internal/main/v1/demand-sources/activation-groups` 是新 `DEM-ACTIVATION/1`。不可变清单固定旧退出/新启用全部成员、原Demand版本/量/UOM、实际技术/价格责任映射、商业批准与逐执行Owner GateProof。每个prepared成员必须有真实准备结果；任何 UNKNOWN 拒绝整组。

本补遗选择 **Planning单一权威成员事务**：所有有效Demand、MembershipHead和MRP输入资格门在同一支持的数据库；先持当前 IAM/必要已规划源门，再按稳定GroupId、LineageId、DemandId排序取全组X门，重核发运及执行围栏，最后同commit退休旧剩余成员、启用全新成员、推进membershipRevision、写Decision/receipt/Outbox。MRP读取或发布必须持相同成员门/比较明确membershipRevision；Prepared从来不进入净算。实际执行方要在真执行入口核该成员身份及自己原控制，已发/已开工不由这次事务撤销。

若实际部署不支持该共同成员事务或下游执行门，组启用返回 `ACTIVATION_CAPABILITY_UNPROVEN`，已准备成员保待核而非部分生效；不能“先开B40再补C30”。A100已发30只转剩70，B40+C30恰70；若发运变40，旧70清单失效。无需假定Sales/所有执行Owner也在此数据库，但其保证必须覆盖真实切换窗口，未证则关闭组应用。原结果未知查询原group/manifest；已成功只返原成员决定，不重建全组。终止要求持久 fence封闭全组旧意图，再给 ABORTED_WITH_FENCE；不能只删prepared行。

### 4.4 Demand 当前读取、原键恢复和整组终止

`GET /api/main/v1/demands/{demandId}`（DEM_READ）要求当前 `planning.demand.read`、同租户及原来源披露范围。返回 `DemandRead` 的强 ETag、当前 `controlSetRevision`、完整 `DemandCurrent` 和读取时点；不存在或不可见统一 404。ETag 使用本文 DEM1 版本身份，在同一读取快照内与控制版本一致。读取不刷新业务版本、不构造 SourceResult，不能以此代替提交时最终门。

`POST /api/main/v1/demands/command-results/query`（DEM_COMMAND_RESULT）以闭合三种人工 operation + 原 `commandKey` 查 `CommandReceipt`。唯一域 `(Tenant, Operation, CommandKey)`；原命令槽必须持久 target/actor/完整请求/原 If-Match/结果。`planning.command.read` 与原 target 当前披露权均需满足；查询不要求仍有原 mutation 权，也不放行已撤销读取权。`NOT_OBSERVED/IN_FLIGHT/EXPIRED_UNPROVEN` 不证明无效果。查询只读，不能把查询动作写成原命令成功；原键重发只能采用原 body 和原 If-Match。

`POST /internal/main/v1/demand-sources/activation-groups/results/query`（DEM_ACTIVATION_RESULT）支持 `ActivationQuery` 两个互斥形态：完整 groupId/groupVersion/manifestDigest，或原 DEM_ACTIVATE/DEM_ABORT + commandKey。按可信租户/producer 路由和当前 `service:planning.activation.result.read` 查原组命令，组身份相同但 digest 不同返回 409 `ACTIVATION_MANIFEST_CONFLICT`。`FOUND` 才带原不可变 `ActivationResult`；其余状态 result=null。历史结果与当前适用性分开，查到旧 ACTIVATED 不恢复旧成员资格。未知时保持准备态、源围栏及未知范围，不新建 group 来逃逸。

`POST /internal/main/v1/demand-sources/activation-groups/abort`（DEM_ABORT）要求独立 `service:planning.activation.abort`、完整原 manifest、reason 和 Idempotency-Key。在激活同一全组资源计划/成员事务内先查 command，再查组决定：已 ACTIVATED 返回 409 `ACTIVATION_ALREADY_COMMITTED`，不得回滚真实事实；已 ABORTED 返原组终局，新运输命令仅关联该原终局。未决组须在同一提交写不可变 `ActivationFence`、组 ABORTED 决定、命令原结果及审计/Outbox。fence 固定 group/version/manifest/decision，永久阻该组所有迟到 ACTIVATE 和 PREPARE_SUCCESSOR；所有这些写入口在取得组门后必须查 fence。

终止不删除 prepared 成员或旧版本。返回 ABORTED_WITH_FENCE 时 retired/activated 均为空、membershipRevision 为未改变的当前值，fence 引用上述完整新原件；ACTIVATED 的 fence 必为 null。激活与终止同门互斥，先提交者决定终局；事务结果未知仅查原键，不宣称已封。无法共享成员事务/缺行范围或全写者围栏能力时 503 `ACTIVATION_CAPABILITY_UNPROVEN`，不能写假 ABORTED。其他认证、异体幂等、请求过大和错误恢复采用第2节共同规则。

围栏引用采用新增有限 binding `DEM-ACTIVATION-FENCE`：owner=`Main.Planning`、contractId=`DEM-ACTIVATION-FENCE`、contractVersion=`1`、evidenceId=`decisionId`、evidenceVersion=`groupVersion`，contentDigest=`H('DEM-ACTIVATION-FENCE-1', 完整ActivationFence body)`。body包含可信tenantId；解引用必须取本租户/准确组版/manifest的完整不可变正文并核摘要，受当前读权保护。此Ref只证明永久禁止该组旧意图，不授激活权；既有OwnerRef allowlist新增这一准确条目，不接受同名外域对象。

## 5. QTN H07 后继消费与 Fact 接缝

**原QTN接口不改**：前缀 `/api/main/v1/quotations`，Q33 `POST /{id}/versions/{versionNo}/order-intents`；Q23 `POST .../order-source-preview`；Q24 `POST .../order-source-requests`；Q25 `GET /commands/{commandKey}`；Q35仅取消PREPARED。完整类型引用固定R2 SHA `45ac9a34dec6e8e49c7fceaa0020e314f2e753f631b93a03cf280eec1fae29da` L348–374/694–710。

| 旧ORD逻辑 | 当前准确实现 | 不支持的旧假设 |
|---|---|---|
| 查可转用量 | Q23每原QuoteLine坐标补集；PREPARED不占 | 不用Sales计算第二个Q−C−H余额 |
| 创建草稿占H | Q24/I10同事务真正DRAFT + QtnOrderAllocation COMMITTED + Intent/receipt/BPUse | 不能先远端占用再本地建草稿并称当前H07成功 |
| 草稿H→Confirm C | 原Allocation保持；Sales独立商业确认和事实 | 无Quote claim conversion调用、无第二扣量 |
| 改草稿数量/源商业价 | 本版已分配源字段不可原地改；从未确认且无未知时I11整份取消原草稿，再用户明确Q33新Intent | 不发明QTN局部resize/reprice接口；不能先放再证明取消 |
| 已Confirm后取消/换单 | ORD真实变更/取消、执行和财务后果分别处理；原Allocation保留 | I11不适用，不自动回流配额；需要返额是后续另版QTN能力，当前端点明确拒绝 |
| 跨Quote版本共享额度、ONE_SHOT | 仅以当前QTN真正声明的版本/line/坐标和原承诺链规则处理 | 不把历史候选策略当已存在枚举或跨版余额服务 |

I10必须在同Main连接/同DbTransaction验证完整 `QuoteOrderSource`：QuoteVersion、acceptanceId/digest、完整FactHead/correction链、customerEvidence、BP/Mapping、币种/税、每line的ranges/数量/价/净额/税/含税/有符号尾差及sourceDigest。保存全部原source而非hash，订单sourceReceipt带相同FactSnapshot及 `H('QTN-ACCEPTANCE-FACT-SNAPSHOT-1', snapshot)`。`customerEvidence`逐字段等Fact.effectiveEvidence；Fact quote/id/digest分别等原来源身份。当前Fact必须PROVEN，>1要完整准确Correction链。Q24与I10共同门再读当前Root/ActiveAcceptance及祖先争议；错误 `ACCEPTANCE_FACT_STALE`/`ACCEPTANCE_EVIDENCE_MISMATCH` 不造订单。

QTN量坐标 `[a,b)`，净额F(b)−F(a)，税G(b)−G(a)，F为原minor单位舍入；Sales显式保ranges/roundingAdjustment。0.335×3拆三次1为0.34+0.33+0.33，不全做0.34；不得平均价消尾差。原line identity不按SKU合并，BATCH_COUNT=1不可拆半次。全行Allocation、真实Order/Lines/Source/OrderCommand、BP SALES_NEW_SOURCE、QTNReceipt/Intent/Audit同commit，无Confirm/Release/Demand/Won。

共享锁计划保持QTN R2 L752–759：IAM及命令 → 已规划非QTN源Scope（BP先于EST）→ BP Mapping/Claim/Root → EST → QtnScope/Root/Version/Control/Acceptance/Fact/Intent/Allocation → Sales唯一来源槽/根。Sales新Confirm/Change/Release若涉及QTN源，事务前定位完整闭包并沿同序加入Fact；不能先持Sales根后回取QTN，也不能持锁时远程HTTP。无法同UoW采用的H07返回ORDER_PARTICIPANT_UNPROVEN，不能换Saga冒通过。

I11输入和输出完全继承QTN R2原类型。Main Sales实际取消事务必须证明原订单 **从未** 商业Confirm、无Demand/库存、无任何未知效果，匹配完整allocation/receipt hash/expectedRevision。整条原allocation一次 OWNER_RELEASED，真实订单取消后继与CorrectionReceipt同commit；不要求旧BP今天可新交易或旧Quote未过期。若曾Confirm后又取消，条件仍不满足；查不到订单不是证明。禁止局部放回数量或QTN UI直接删Allocation。

### 5.1 Fact影响：历史不变与当前阻断

原订单采用A/Fact1，后来调查更正到B/Fact3：旧Order/SourceReceipt/Allocation按当时A/Fact1原字节保留；原Q25/key恢复仍回A，不因今天失写/来源未证改写原成功。同Key换B是异义；只有显式新Intent/Key，完整当前门通过后才新建B/Fact3来源。

Sales实现原I19 `SalesQuoteSourceImpact.Receive(AcceptanceFactImpact) → AcceptanceImpactReceipt`；本补遗仅规定准确消费者职责，不另发明上游wire。Inbox以原event/body去重，业务影响键 `(Tenant, Quote, OriginalDecisionId, FactRevision)`；不能仅用ControlRevision，因为换证可不改Control。异body冲突，缺前驱WAITING_PREDECESSOR；准确CurrentRead读取完整Correction链补洞。持久原影响、CurrentSourceKnowledge和待评估责任，不删除订单、不改Confirm、不释放Allocation、不发客户REVOKE或重发Won。

每个会新增业务效果的 Confirm、Release、Change、后继来源采用同步读取并共同参加QTN FactHead，不等异步影响到达。UNPROVEN阻依赖新效果；安全停止、已发生事实收录和授权历史查询走其专门门，不因Fact失证被全部封死。恢复PROVEN仅标OWNER_REVALIDATION_REQUIRED；当前Sales所有其他控制仍有效。事实纠错的WITHDRAWN不能作为“客户真实终结”或返额证据。源影响在INT/HO保持原contract身份传播，平台不改业务body解释，OPP商业结果仍只从原唯一消费者进入。

## 6. 持久模型、唯一性与迁移适配

这里给出新模型责任与约束，不提交DDL、迁移脚本或运行配置。所有PK/FK/唯一域含Tenant，非本域 ID按原Owner编码保留；完整JSON只能由已验证封闭schema产生，同时保存原body与摘要及schema identity。不可变事实append-only；Head/Slot以RV/CAS更新。缺行范围锁与唯一约束同时保护首次竞争。

| 模型/唯一域 | 必存数据及不变量 |
|---|---|
| AccountRoot `(T,AccountId)` | 资料、Lifecycle/Privacy、RowVersion及四元资格；IdentityBusinessRevision独立，不因投影进位 |
| AccountCommand `(T,Operation,Key)` | 冻结target/actor/完整请求/原header/HMACkey/终态；业务和结果原子 |
| AccountSourceProjection `(T,Owner,ProofId,ProofVersion)` | 原完整Manifest引用/目标/前驱/历史与当前知识；不可补建来源 |
| AccountC03Result `(T,Authority,Contract,Epoch,Account,Request,RequestVersion,ResultVersion)` | 原C03字节/trust/BusinessPartnerKey；结果不作当前槽 |
| AccountLegacyMapping `(T,Account,LegalEntity)` | 独立七字段完整快照/冲突，与新Scope存储隔离 |
| AccountMappingDecision `(T,Authority,ScopeIdentity,ScopeEpoch,Position)` | 完整决定/candidates/选择/覆盖/冲突；同位置异义保双方不覆盖 |
| AccountMappingSlot `(T,Authority,ScopeIdentity,ScopeEpoch)` | 唯一当前指针、SlotRevision、watermark、ConflictState、ApplyReceipt；本地资格条件提交 |
| SalesOrderSource `(T,SourceOwner,SourceIntentIdentity)` | QTN为真实OrderIntentId；完整source/Fact/ranges/receipt/owner proof；同身份不同digest冲突 |
| SalesVersion/Line/Schedule `(T,OrderId,Version,ChildId)` | 稳定行/份额、不可变有效商业版与独立编辑版；原QTN行价/范围保真 |
| SalesApplyIntent/Command `(T,Operation,Key)` | Plan全文、参与者原请求/保证/结果、原header、合法终局及Fence；不清未知后换键 |
| SalesRelease/SourceOperation `(T,SalesAuthority,Lineage,SourceVersion)` | 完整不可变源请求、真实接受前驱/已发前驱、原OperationKey/Outbox |
| DemandSourceSlot `(T,SalesAuthority,Lineage)` | 当前接受版、最后观察已发版、唯一Demand、ControlSetRevision；至多一个未知普通后继 |
| DemandOperation `(T,Producer,OperationKey)` | 原body/digest、实际SourceResult与完整历史；不与当前效力混写 |
| DemandVersion `(T,DemandId,Version)` | 目标总量、全部技术/日期/地点/源证据；来源变化不覆盖旧版 |
| SourceControl `(T,Lineage,Owner,ReasonId,ControlVersion)` | 完整作用域/动作/业务时点/前驱/原件；无Demand也有控制 |
| DemandMembership `(T,MembershipScope)` | currentRevision与准确有效成员；组旧退出/新启用同事务；所有MRP消费者核同门 |
| Activation `(T,GroupId,GroupVersion)` | 完整manifest/成员准备/执行证明/原结果/Fence；同组版不同成员冲突 |
| SalesFactImpact `(T,Quote,OriginalDecisionId,FactRevision)` | QTN完整Fact影响原body/trust、链进位和当前知识；绝不覆盖原订单来源 |

**旧工厂差异的准确来源**是HO主文引用 `GTX537/CP6@157630594e3371fe181955d2f6227ff3b6962c84`、`CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs` git blob `432323972ad0d5a156b8f169b42c5c3b75e48e5a`，其L36–37同CrmOpportunityId任意订单拒绝。这个结论不声称本次已审今日HEAD。

新适配器必须分别维持三种唯一身份：CRM商业受理 `(T,CRM_HO,HandoffId,RequestVersion)` 只决定真实受理；QTN订单 `(T,QTN,OrderIntentId)`；其他准确有据订单 `(T,SourceOwner,SourceBusinessRoot,ResponsibilityGroup,CustomerIntent)`。OpportunityId只为来源关联/索引，不是订单唯一键。两个Owner确认的不同责任组可产生两个订单；同责任组/同客户意图换运输event不得重复。一个受理不必立刻建订单，不能把Handoff版本当所有下游订单的全局唯一键。实际商业Confirm仍按原Group/Order/Line/Schedule/EconomicSlice/Claim/binding产出OPP-COMMERCIAL/1完整事实，不在HO复制Won总账。

迁移必须逐原业务键制作不可变 `LegacyAdoption` 记录：实际source SHA/实体PK/RV、原schema、完整原snapshot/hash、原已执行/未知范围、目标contract、映射/缺项、允许动作和审定Owner。旧Confirmed标记/BookedAmount/Created消息不得生成真实ConfirmationRef、Fact、Allocation或历史Domain事件。旧资料只读观察可以继续；需要新正式效果时必须有真实来源原件与当前Owner采用记录，不通过选择DIRECT_EVIDENCED绕过未证Quote。

切换方案固定为一个PrimaryWriter、按 `(Tenant, SourceProtocol, BusinessSourceKey)` 排他的路由注册；同范围legacy mutation与新writer不可同时可写。允许双读/影子校验但影子不写业务、Command/额度/Outbox。切换前列全旧入口（API/worker/import/job）、未决键/队列积压、已成事实/重复/冲突分类，停止新增旧范围后逐键承接原消息身份，不能重新生成event逃逸幂等。旧无法表达多责任组时新来源不得调用旧工厂；门未证新商业writer关闭。回退仅回到仍能解释新事实的适配版本，不能启用默认Confirmed或单机会去重旧writer覆盖新范围。该政策是设计，不执行切换、停消费者、迁移或部署。

## 7. 验收、验证范围和闭合结论

[acceptance资产](assets/commercial/acceptance-1.0.0.json) 给出可复现场景与准确预期：ACC同键/RQ、Scope分叉/选择/停用竞争、七字段不得越权升级；QTN原A与当前B、Fact通知延迟、真实同事务回滚、H/C零二次扣量、I11限制、多责任组；Demand目标总量、未知前驱、取消先到、组准备/启用、发运并发、原因并存及未知恢复。

本次本地静态验证只证明JSON可解析、OpenAPI本地引用闭合、封闭对象的required成员合法、A01–A13都有精确入口、140原字段唯一且与原字典相符、原件SHA和已读行段可定位。生成器是本次新写的文档资产工具；没有执行归档脚本。静态通过不等于HTTP服务、SQL锁/唯一性、真实IAM撤权、密钥签名、来源Owner、并发注入、UI、迁移或部署通过。

五项finding的**设计决定**由本补遗与资产闭合；其 `actual_adoption_status=UNPROVEN`、`implementation_status=NOT_IMPLEMENTED`、业务 `NOT_RUN` 继续保留。实施必须固定目标代码SHA、逐writer接线和真实受隔离环境的回归证据后才能改变这些状态。原历史UA、QTN218AC/HO64AC以及ORD/DEM历史用例结果不在本次重标PASS。
