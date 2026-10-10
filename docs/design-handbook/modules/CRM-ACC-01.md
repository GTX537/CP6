# CRM-ACC-01 客户关系账户

整理状态：`core_semantics_consolidated`。本文整理已接受的账户功能设计；物理 API、DDL、真实 Owner 协议和运行验证尚未由本组合冻结。正文中的“必须”是设计约束，全部业务验收仍为 `NOT_RUN`。当前有效组合为 R01 + R02 + R03 + CR09 补正 + UA-20260929-A-CRM-01；不得单独把 R01 的提案或 R03 的原 C42 夹具当最终要求。[接受范围][UA13] [运行边界][UA36]

## 1. 业务目的、操作者与权威边界

账户是 CRM 维护的客户关系主体：销售人员创建、维护身份与归属，获准人员停用或恢复；账户页汇集来源、联系人、活动和 Main 商业伙伴映射。它既不是 ERP 法律交易主体，也不是联系人集合的权限容器。CRM 拥有 `OrganizationId + AccountId` 对象、状态及资料修订；Main 拥有 BusinessPartner 和映射选择。创建账户不能顺便创建 Contact、Opportunity、订单或 Main BP，停用账户不能级联停用这些对象。[范围与对象][DES17] [创建边界][OP17]

典型入口有人工新增和 Lead H02 转换后读取。人工查重只是给当前有权候选及匹配依据；同名、共享域名不证明同一企业，未见候选不证明全组织唯一。不抓取输入网站，不自动合并。转换来源必须由原 Owner 提供有版本的证明；一个 `LeadId` 只能是来源提示，不能证明转换成功，也不能触发重新资格判断。[身份规则][DES49] [候选][OP11] [来源][OP41]

本基线明确不启用 CRM 主动申请 BP 映射：由 Main H04 发起，CRM 提供批准落点的安全导航、结果回查及被动投影。A09/A12/A13 是服务消费操作，页面不提供“强制应用”按钮，也不能把它们转为普通销售命令。[DEC04][UA24] [被动入口][PD16]

## 2. 当前版本、组合与接受边界

| 层 | 必须合读的内容 | 如何采用 |
|---|---|---|
| R01 | `153b17519b044323a9a943af2074c8331ed868ad` 的 DESIGN、FIELDS-ACTIONS、PAGES、RULES-RECOVERY、CONTRACTS、48 场景 | 定义账户四个原 SPEC、字段与三张账户页面；后续显式替换优先 |
| R02 | `032f6814ee7bbd17e2bab69e08901f698408c4b2` 的 OPS-ACCOUNT、PERMISSIONS-STATE、CONTRACTS、PAGES-DELTA、OP-STATE-MATRIX、66 增量场景 | 精确分开 A01–A13、旧结果回查与新写、结果流与当前映射 |
| R03 | `87464bdcbd6a11046b743df207ebcda05150788d` 的六合同卡、RECEIPT-MAPPING、关系准入、引用消费及32组合序列 | 补齐 Owner 执行域 Fence、映射唯一裁决、最终应用竞争 |
| CR09 + UA | CR09 REVIEW/CLARIFICATIONS 与 UA-20260929-A-CRM-01 | 修正 C42 初态；恢复精确 wire 字段 `BusinessPartnerKey`；接受 DEC01/02，DEC03/04 本基线不启用 |

版本及包含关系直接来自接受记录，不按文件名的新旧推测。三 Target 合计原 12 SPEC、45 OP、146 场景；这不是 Account 自身146场景，Account 为 A01–A46 共46组。总控评审不是独立运行审计；文本接受未签署真实 Main/IAM/附件/导出/PLM 合同，也没有关闭九 RFC。[固定版本][UA13] [总控评审范围][RV7] [验收编号][AC3]

独立 CRM-CON/CRM-ACT 后继设计应在各自模块采用，不可把它们覆盖回 Account 的未变范围。Lead CP22 的 H02 对 Account Participant 提出实际 Enlist 和原子结果要求；需与本功能文本共同实现，不能把本文件的泛化逻辑 API 名当 CP22 的冻结 wire。[H02 设计](D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-LEAD-01.md)

## 3. 数据、身份、字段与版本轴

| 数据组 | 字段/身份 | 业务含义与写入限制 |
|---|---|---|
| 对象与资料 | `OrganizationId + AccountId`；`DisplayName`；aliases、domains；AccountType/Industry/Region | AccountId 服务端生成 opaque；名称 trim 后必填，未给最终最大长度；别名仅对象内去重，域名保留原值和规范化版本；字典字段要绑定版本，不能拿标签当键 |
| 归属 | Owner、Department、归属范围 | Department 与可确认的合法范围必需；Owner 当前可分配，允许 null 的具体范围须有合同，不降为全组织可见 |
| 状态与并发 | Lifecycle、StateReason、RowVersion、CommandKey | Lifecycle 只由专门状态命令改变；原因按动作必需且可能敏感；RowVersion 是并发条件，CommandKey 是冻结意图标识，不是业务实体 ID |
| CRM 联系资料 | CRM 渠道、地址、Notes | 受字段权限约束，不能充当 ERP 税务/法律地址或付款条款；未定长度不能由前端静默截断 |
| 来源 | SourceKind、SourceReference、ConversionEvidence、Lead references | 人工/来源引用须配对；ConvertRequestId、原版本、目标清单来自 Owner；来源只读区不接受普通编辑伪造 |
| 映射请求与结果 | RequestId/RequestVersion、RequestState、ResultVersion、历史结果 | `Received` 仅由 Owner 收讫证明，传输成功或 CRM Outbox 保存不等 Received；每个请求单独一条结果流 |
| 当前映射 | BusinessPartnerKey(string)、OwnerMappingId/Revision、Scope、EffectiveInterval、BoundAccountBusinessVersion、watermark/asOf | BP 键保留字符串原值，包括前导零和斜线；不转 Guid/数字、不拼 URL；当前效力还需 Main 独立证据及本地资格 |

字段清单 A.F01–A.F24 是逻辑字段，不能直接生成物理列、枚举编号或 HTTP 请求结构。PATCH 省略字段表示不改，只有明确可空字段才能传 null；遮罩占位值不是合法写入值。未知字段、Lifecycle/BP/source proof 等越权字段必须拒绝，不能把整个详情对象回传后覆盖。[账户字段][FIELD5] [PATCH规则][FIELD94] [A04][OP23]

必须保留以下相互独立的版本：

1. Account `RowVersion` 对普通资料并发负责；影响 Main 绑定身份的业务版本由 Owner 影响字段集合决定。改展示字段不应自动否定映射；改绑定身份后标需复核，保留原请求冻结载荷和历史结果。
2. 请求结果流由 Authority、Contract/Epoch、ResolvedTenant、Account、RequestId、RequestVersion 定界。Q1 的 ResultVersion=5 与 Q2 的2不可比较新旧。
3. Main 当前映射由 Authority、ScopeIdentity、ScopeEpoch、Owner 决定位置/前驱或有权完整快照决定。
4. 本地运营资格由 AccountLifecycleRevision、IdentityBusinessRevision、PrivacyAuthorizationWatermark、EligibilityPolicyVersion 决定。其失效不擦除历史 Main 成功。

这些轴不能合并成一个“最新版本”。Scope/Fence/MappingEvidence 是新协议需求，并未成为 C03 v1 字段。[版本字典][C2_11] [三裁决域][RM36] [字段补正][UA31]

## 4. 流程、状态与事务内写集

| OP | 入口与条件 | 应产生的本地事实/响应 | 明确禁止的副作用 |
|---|---|---|---|
| A01/A02/A07 | 授权列表、候选、详情查询 | 按相同授权集合返回结果与计数；安全候选依据；各 tab 独立裁剪 | 业务写、业务 Outbox；隐藏精确数量；网站抓取/自动合并 |
| A03 | 合法最小资料、归属/Owner 元数据版本、新意图键；无现有对象，不要求 If-Match | Account Active + 审计 + 必要 Outbox + 幂等回执同事务；最多一个新账户 | 同时创建 BP/Contact/Opp；同键重放二次占配额；无 query 时强开详情 |
| A04 | Active/Retained、当前 edit/字段/范围、RowVersion、白名单 patch | 只维护获准资料；触及 Main 绑定身份时产生需核状态 | 改 Lifecycle/BP/来源证明；改原映射请求载荷；隐藏字段被缺省清空 |
| A05 | Active、专门停用能力、原因、If-Match、键 | Account → Inactive，一次状态事实与配套回执 | 结束联系人关系、停用 Contact/BP、撤回在途 Main 请求；Main 离线阻塞本地停用 |
| A06 | Inactive、恢复能力、原因、当前隐私/归属可证 | Account → Active；重新判断本对象资格 | 恢复匿名化明文、渠道、历史关系、外部权限或自动认定 BP 有效 |
| A08/A09 | A08 当前授权来源读取；A09 可信 Owner 来源证明消费 | SOURCE_HINT / CONVERSION_CONFIRMED / SUPERSEDED / INVALIDATED / UNCONFIRMED；修订留史 | 只凭 LeadId 标转换；重新创建 Lead/Account/Contact/Opp；到期验证误称转换从未发生 |
| A10 | 有批准 Main 落点、当前导航权限，目标重新鉴权 | 仅安全导航 | CRM BP request、业务 Outbox、PII 查询串、客户端提供租户身份 |
| A11 | 当前独立 RQ 及结果范围，原键/句柄 | 原结果安全投影与当前可用性分别返回；未知保留 | 要求当前 edit/Active 才能看原成功；重发业务、自动新键 |
| A12 | 已知请求、可信相同结果流 | 原请求 Owner 结果入史；同版同载荷去重；同版异载荷隔离 | 跨请求比较 ResultVersion；恢复 Account；直接当当前映射 |
| A13 | Main 当前关系证据 + 本地最终资格 + 槽版本 | 当前映射槽、应用回执、Inbox、审计原子应用 | 创建 Main BP、改 AccountLifecycle、靠抢锁先到选择分叉候选 |

上述13操作分别有独立卡，服务消费操作与人工命令不能混用授权。重复停用的原键读原结果；已经 Inactive 上的新停用意图可返回 `ALREADY_INACTIVE`，不得多造状态事实。普通维护在 Inactive 拒绝，但合法历史读取、专门恢复或关系结束不等同普通写。[完整账户操作卡][OP5] [状态矩阵][M5]

`Active/Inactive`、`Retained/Anonymized`、映射 `Effective/Restricted/Unknown`、请求/业务/投影结果是并行状态轴。页面不得只用一个成功/失败标签表示它们。账户恢复后 Main 离线可以让映射仍 Unknown；如果账户自身隐私或归属证据未知，恢复本身应拒绝。[恢复][OP35] [多层状态][CC9]

## 5. API、事件与 Owner 契约

本组合使用 A.API01–04 与 A01–A13 逻辑操作卡，没有冻结 Account HTTP method/path、完整 DTO schema、messageKey、状态码及生产 TTL；不要把页面路由当 API 路由。各动作的精确已有定义从 OP 卡和合同卡定位，物理实现前必须取得 Owner 对应版本。[物理边界][PD51] [有限合同][CC16]

| 契约/Owner | 本模块消费的必要证据 | 必须保留的边界 |
|---|---|---|
| H02 / CRM-LEAD、Account Participant | 原转换请求/版本、目标清单、Owner Proof/Manifest；CP22 真实同库事务 Enlist | A08/A09 只是来源读取/证明投影，不是再次执行转换；Account Participant 实现另遵守 CP22 原子提交 |
| H04 / Main 发起 | 批准的 Main 受理落点和目标授权 | DEC04 明确未启用 CRM producer；无落点时不能造 URL 或本地 request |
| C03 v1 / Main → CRM | 成功精确字段 `BusinessPartnerKey(string)`；原请求与结果版本 | 只支持可证原结果消费；没有 Scope/前驱/当前选择证明，不能臆造 A13 Effective；`BPKey` 只作逻辑简称 |
| A.R03-CT01 / 原命令执行 Owner | ReceiptLocator、FrozenIntent、OutcomeScope、ProjectionCoverage；必要时 ExecutionFence | 结果查询是当前权限下只读；Fence 必须覆盖实际执行域，不是一个本地取消旗标 |
| A.R03-CT02 / Main 当前映射 Owner | ScopeDecisionProof 或有权完整快照，绑定 scope、前驱、候选摘要、Owner 决定位置 | 消费方不能独立替 Main 选择；与 C03 v1 分开签署/版本化 |
| IAM/身份 Owner | 当前能力/范围/字段/用途及 Authority/Contract/Epoch/版本水位 | 旧 Allow 不覆盖新 Deny；新 epoch 无可信过渡时 Unknown，不能使用缓存放行 |

首次观察结果须认证生产者与租户绑定，未知请求隔离，不补建请求。相同流同版同内容去重；同版异内容隔离；低版可留可信历史但不降水位；高数字仍需合法状态转移证据，不能仅按数字采用。[A12/A13][OP70] [流规则][C2_60] [C03 补正][CL27]

## 6. 事务、幂等、竞争与未知提交恢复

### 6.1 稳定操作身份与旧结果读取

ReceiptLocator 的稳定索引是 `OrganizationId + CommandKind + CommandKey`，Target 和 Initiator 是既存绑定，不能放到可改变的索引里绕过冲突。FrozenIntent 保存规范化版本、canonical digest、初始目标版本和模式/目标。第一次提交冻结内容与键；同键异意图冲突，同键同意图返回原结果而不重复扣配额。新本地业务事实、审计、必要 Outbox、命令回执同事务提交。[身份与原子性][C2_11] [恢复路径][C2_28]

处理顺序是：当前身份与 RQ → 定位原槽及核指纹 → 安全读取历史事实 → 如确属新意图，才执行当前 Active/edit/新业务守卫。因对象停用、当前丢失普通 edit 或列表尚未投影，不能把原 COMMITTED 改成失败；失去 RQ 则中性拒绝，连成功存在也不透露。业务 Owner 离线但本地原成功可证时仍可读；身份本身过期时全部业务结果拒绝。[历史优先][C2_28] [授权窗口][RM30]

`OutcomeScope` 至少区分 ENTITY_APPLY、LOCAL_REQUEST、REMOTE_BUSINESS、PROJECTION。实体已提交而列表为空不能重建；本地请求已保存不证明 Main 已建 BP；远端最终拒绝不证明其他执行域从未应用；HTTP 422、Failed/Retryable=false、一次 NotFound、回执到期或未知后的409/412均不自动证明 `NOT_APPLIED_FINAL`。[结果域][RM7] [失败非全局终局][RM18]

### 6.2 何时可以改为新的意图

只有原执行 Owner 的原子裁决明确覆盖原 operation/digest/执行域并建立持久 Fence，证明尚未应用且所有迟到执行都会被阻止，才能支持相应域的 NOT_APPLIED_FINAL。证明包含 Authority/ContractVersion、执行域、FinalityDecisionId/Version、FenceGeneration、NoApplyEvidence、迟到处理及覆盖范围；客户端取消、队列空、本地 tombstone 不足。多域只证明部分时不能推出全局未执行。即使证明齐全，新意图仍需用户明确决定，不能自动换键。[Fence证据与裁决][RM23]

公共回执协议及跨刷新恢复尚未分配/批准；本组合仅保证原 tab 内存持有恢复材料，不能宣传跨刷新、重新登录自动恢复。PII、冻结载荷和 bearer token 不得塞入 URL/localStorage；刷新不自动重放 mutation。[跨刷新边界][C2_50] [UI 恢复][PD47]

### 6.3 当前映射最终裁决

若 M2a/M2b 同 Scope、同前驱 M1，A12 可分别记结果，A13 必须等待 Main 唯一选择或有权完整初始快照。本地锁先后、到达顺序、单号、Q1v5>Q2v2 都不决定赢家。M1 证据仍有效则保留，否则当前 Unknown。相同 Owner 决定重复应用只迁移一次；同决定位置出现相反权威内容，标 `ScopeConflict` 阻止依赖该槽的新商业使用，保留已发生事实，等待 Owner 覆盖充分的解决 checkpoint。[分叉处理][RM42]

最终提交顺序：认证证明 → 读取当前 scope 槽/水位和 Account 本地资格 → 同事务条件比较 slot revision、水位、冲突状态、生命周期及身份业务版本 → 写映射投影、原 ApplyReceipt、Inbox、审计。Account 停用先提交时，A13 应重判 `LocalInactive`；A13 先应用再停用时保留应用历史，但新使用不再有资格。A13 不写 AccountLifecycle，不恢复 Account，不重发 Main request。商业调用也必须重新核当前资格，不能长期缓存一个 Effective=true。[最终应用][RM48] [并发验收][AC24]

## 7. 权限、租户与敏感字段

允许动作是 Function × 资源动作 × 对象范围 × 字段 × 用途 × 商业权益 × 适用特殊能力的交集；缺失映射不是 true。add、query、edit、停用/恢复、PII、来源读取、RQ、服务 producer 是独立能力，不能互相推导。add 无 query 的用户可合法创建，但只得到安全提交凭证与 `CanOpen=false`，不能被强制导航到无权详情。[能力矩阵][PER7] [A03][OP17]

账户有权不授其全部 Contact/Lead/Activity 明文。每个 tab、计数、候选、搜索、排序和目标导航均独立裁剪；禁止通过姓名排序、隐藏精确数量或敏感候选检索探测无权数据。对敏感查询应明确拒绝，而不是伪造空集合成功。地址、渠道、备注、状态原因、旧修订、草稿/差异快照都应服从当前字段权限；撤权后清除旧明文，不用历史回执恢复。[页面裁剪][PAGE29] [字段降权][PD30]

身份水位只在同 Authority/组织/Subject/Contract/Epoch 流中比较。旧 Allow 到达不能覆盖更新 Deny；新 epoch 缺过渡证明失败关闭。身份过期与 Main 离线须分开：后者允许有权读取可证本地事实，前者连 RQ 都不能放行。真实角色名、撤权窗口、身份有效期和商业额度尚未生产确认。[身份水位][PER35] [未确认范围][UA26]

## 8. 页面、交互及服务端校验

| Function / 页面 | 开发落点 |
|---|---|
| MSBBCR300 `/crm/accounts` | 授权筛选、稳定 ID、列表与计数同一集合；cursor 过期重载查询，不把自由文本持久放 URL；不提供未设计的批量编辑/停用/合并/导出 |
| MSBBCR310 `/crm/accounts/new` | 身份、归属、来源、候选分区；元数据无法确认时阻止保存；候选可选已有对象或明确理由继续新建。提交未知后冻结原键/意图，无查询权成功只显示安全回执 |
| MSBBCR320 `/crm/accounts/[accountId]` | 概览/状态、来源、联系人、互动及 ERP 分区；普通资料保存、停用、恢复分别确认。来源显示原 Proof/目标清单及当前证据状态，不能用自由文本勾选“已转换” |

ERP 区至少区分原请求、Owner 业务结果、CRM 本地应用、当前运营效力/截至点、独立未结问题。新请求不会自动淘汰仍有效旧映射；当前账户 Inactive 与历史 Main 成功同时显示。部分数据、无权、空数据、Owner 未核不同，不能都画成空白。缺 Owner 合同的动作明确说明阻塞原因，不提供“强制成功/重试新请求”。[R01 页面][PAGE7] [R02页面][PD9] [五层界面][RM59]

已知未应用的新意图412可保留当前仍授权草稿，重新读取并明确比较后形成新意图；Unknown 后409/412继续 Unknown，不能自动进入第二步。首次提交冻结 UTC/键/内容，刷新、返回和重新登录不触发重放。前端只是辅助，服务端最终验证归属、字段白名单、原版本、当前权限和状态；不能依赖按钮隐藏。[UI恢复][PD47] [服务端守卫][OP23]

保留原五语言、键盘、错误聚焦、文本状态、窄屏 sections 与200%缩放目标。原生 Excel 和浏览器显示没有实测，不能从文字页面规格推定已经满足这些验收。[可访问性][PD51]

## 9. 实施顺序与既有代码差异

以下是由依赖关系得出的实施建议，不是新增产品决定：

1. 先冻结真实 Account 物理 DTO/枚举/字段约束、IAM 能力与身份水位，再实现 A01–A07 和独立 RQ。把普通新写与旧结果读分离，并在数据库实现稳定操作槽、RowVersion、业务事实/审计/Outbox/回执的原子性。
2. 对齐 Lead CP22 H02 的同连接、同事务 Account Participant 与实际结果。Account 页只消费来源证明，不能复制一套转换行为。
3. 实现 Main 已有 C03 v1 的 A12 结果历史，保持 `BusinessPartnerKey` 字符串。A10 只采用批准导航；DEC04 继续关闭。没有新当前映射协议前，界面准确显示当前未核，不伪造 A13 成功。
4. Main/IAM 合同落版后实现 A13：独立 Scope 槽/Owner 决定/本地资格，并验证停用、身份资料变更与映射并发两种顺序。
5. 最后接通当前字段裁剪的页面恢复体验和下列真实隔离验收；公共回执跨刷新方案须经原 Owner 明确接受后再启用。

本轮没有读取并比较固定源码 SHA 下的 Account 实现，故不宣称上述差异是当前代码缺陷。R01 明示72逻辑字段不是生产SQL批准列，双数据库适配没有验证；不得用本设计直接生成上线 migration。待代码核对必须保存实际 SHA、文件和行号，区分“原设计要求”与“源码已实现”。[物理边界][RULE113]

## 10. 验收设计与实际状态

Account 46组全部是未来验收设计，`NOT_RUN`；本次没有 API、SQL、浏览器、单元或集成运行。R01 A01–16、R02 A17–36、R03 A37–46 编号保留，不把一个场景拆成多个通过数。跨模块 C42 应采用 CR09 合法初态，不能照抄原 R03 重叠正例。[原48范围][A1_3] [增量66范围][A2_3] [组合32范围][AC3] [C42补正][UA30]

| 验证主题 | 必须断言的行为 | 原场景 |
|---|---|---|
| 身份/资料 | 同名域名不自动合并、无网页抓取；敏感搜索不泄存在；only-add 同键只建1次且不强开详情；patch不改隐藏/状态字段 | A01–06、A28–31 |
| 非级联状态 | 停用只改 Account；恢复不复活 PII/关系/外部权限；Main离线与账户自身资格未知区分 | A07–08、A20、A32–33 |
| 来源 | H02原结果重放不二次转换；LeadId不够；证明替代/失效只来自Owner修订 | A09–12、A21–22 |
| 原请求历史 | 传输ACK不等Owner业务成功；同流同版冲突隔离；旧结果迟到不恢复Account或覆盖新请求 | A13–16、A23–27、A38 |
| 回查/未知 | Inactive、PII撤权、query撤权但RQ仍有、再撤RQ分层；Owner离线不抹本地成功；过期/NotFound/409/412不换键 | A17–19、A35、A37、A39–40、A46 |
| 当前映射竞争 | 同前驱分叉不抢锁选赢家；单值Owner决定仅应用一次；矛盾决定ScopeConflict；停用/身份变更交错不复活或误认新身份 | A36、A41–45 |
| Main导航 | 批准落点安全导航，CRM request/Outbox都为0；无落点、无权、Inactive不绕Main守卫 | A34 |

每个新写验收同时核准确业务/修订、审计、必要 Outbox、回执数量；纯查询业务写与业务 Outbox=0，安全访问审计另列。O1/O2、Ufull、M1/M2 等是能力/数据夹具，不是生产组织、角色或证据。真实身份、Owner 实例、事务原子性、Fence 对迟到执行的阻断、物理适配均 `UNPROVEN`。[公共断言][AC11]

## 11. 具体待确认项与限制

| 待核点 | 当前有据的行为 | 不得自行补出的内容 |
|---|---|---|
| Account wire/schema | 24账户逻辑字段、13操作卡和三页面 | HTTP path/method、DB表列、最大长度、字段code、统一错误JSON/HTTP码、幂等TTL |
| Main 当前映射 | A13需要Scope/Owner单值选择/前驱或完整快照/本地资格 | 不往C03 v1加Scope/Fence字段，不以最近结果代当前映射 |
| Receipt与执行域Fence | 原键、当前RQ、证据覆盖、不确定性必须保留 | 公共跨刷新receipt、执行Owner签署、持久期限和全执行域tombstone保证 |
| IAM与商业权益 | 权限交集、授权水位、当前字段裁剪 | 生产角色/授予名单、具体TTL、撤权读取窗口、真实额度配置 |
| 旧数据 | 无来源/无版本只显示待核；逻辑字段不是SQL列 | 从AccountId猜历史关系、伪造转换Proof、补造Main身份业务版本 |
| 载体/运行 | 当前文本已接受，九RFC未整项关闭，46Account场景NOT_RUN | 生产启用、双DB已适配、Excel或浏览器实测通过 |

以上依赖应逐项归到真实 Owner，不能要求用户重选已经接受的 DEC01/02，也不能把明确不启用的 DEC03/04记为本基线仍待产品决策。跨 Target 后继必须按根索引 L1/L2 选择，S001–S052 历史材料不能自动替换当前组合。[政策状态][UA21] [真实依赖][UA26]

## 12. 来源与实际阅读覆盖

本次全文语义阅读 R01 DESIGN(89行)、FIELDS-ACTIONS(119)、PAGES(94)、RULES-RECOVERY(115)、CONTRACTS(60)、ACCEPTANCE(80)；R02 OPS-ACCOUNT(80)、PERMISSIONS-STATE(69)、CONTRACTS(82)、PAGES-DELTA(52)、OP-STATE-MATRIX(56)、ACCEPTANCE-DELTA(97)；R03 CONTRACT-CARDS(96)、RECEIPT-MAPPING(60)、RELATION-ADMISSION(73)、REFERENCES-CONSUMERS(67)、ACCEPTANCE-SEQUENCES(63)；CR09 CLARIFICATIONS(31)、REVIEW(51)、UA ACCEPTANCE(40)和RFC-POLICY-DECISIONS.json(24)。此前 FIELD58–72 输出缺段已重新补读，不把截断输出计全文。

已继续全文补读25个登记辅助/来源文件：R01/R02/R03全部TRACE、SELF-REVIEW、DECISION-OPTIONS，R02完整Contact 19卡/Activity 13卡/CONTRACTS，三版来源应用/ISSUES/INDEX和CR09的RESULTS/NEXT/PROGRESS/READ-SCOPE。45件登记材料全部有实际阅读记录，状态`required_materials_consolidated`；这不把源内引用的固定代码、母版或外域全册变成本轮全文审计。本模块引用的公共规则可复用于后续Contact/Activity，后继显式替换仍须采用。精确文件 SHA、范围和剩余项保存在 [commercial-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json)；全部选定输入见 [required-inputs.json](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。


### 12.1 补读确认的实施与继承规则

结果查询身份为可信Organization＋CommandKind＋CommandKey，Target/Initiator是绑定字段，换Target不能绕同键；ReceiptHandle须Owner可验证解析并重新授权，Handle本身不是能力。FrozenIntent固定NormalizationVersion、CanonicalPayloadDigest与初始对象版；旧规范化版缺失不能换算法重算旧hash。ReceiptView分HistoricalOutcome、EvidenceLevel、原提交时点/安全回执及CurrentApplicability，未证或无权字段为null并给原因。[原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1b740011c4af0__CONTRACTS.md:11>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1b740011c4af0__CONTRACTS.md:12>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1b740011c4af0__CONTRACTS.md:13>)

R恢复分支先核独立RQ及当前字段/结果范围，不追加实体query/edit/Active或新写用途门；历史本地成功不会因Owner离线被抹掉，当前效力可Unknown。N分支才核当前写资格和一次真实本域事务。UNKNOWN、过期、一次NOT_FOUND或409/412不得自动新键，原Owner必须有无晚执行的确定终态。R03明确Q行移除A11但保RQ授权，不能错误理解成任何人可查旧回执。[原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1b740011c4af0__CONTRACTS.md:28>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1b740011c4af0__CONTRACTS.md:32>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fa99899053bb28e__SOURCE-APPLICATION.md:43>)

ResultVersion只在同认证Authority/Contract、Tenant、Account、RequestId/RequestVersion流内比较；不同请求Q1 v5与Q2 v2不比较5/2。当前Mapping另需Scope、MappingId/Revision、前驱或完整快照、有效区间、绑定账户业务版及Owner水位。原C03只有BusinessPartnerKey(string)，没有Scope/独立MappingRevision/公共回查；源中的BPKey逻辑简称不能生成新wire字段。收到Success至多显示Owner结果收到，不能因此可交易或Won。[原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1b740011c4af0__CONTRACTS.md:58>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1b740011c4af0__CONTRACTS.md:60>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1b740011c4af0__CONTRACTS.md:62>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/97/97fb3b917d08b067__INDEX.md:9>)

组合内R02操作卡仍须按R03显式增量解释：主要标记期间与关系期间分账；仅切主要不结束旧关系；附件Add/Retain/Remove各核相应许可且同意图全成全败；Main唯一选择先于本地CAS；终态加OutcomeScope/ExecutionFence；工程回执的源版、接收/应用、当前限制和复核分轴。原114＋32=146场景全部NOT_RUN，C42只用CR09合法非重叠前置替换，沿原号不加计。[原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fa99899053bb28e__SOURCE-APPLICATION.md:55>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/dd1fbde7760f4841__REVIEW-RESULTS.json:5>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/dd1fbde7760f4841__REVIEW-RESULTS.json:10>)

源自检中30/60/80候选、四DEC待选和旧缺件是各阶段历史状态；效力以本册前列准确后继接受及独立CON/ACT范围为准。三项GAP/DP08/DP10/L01定位子项在R02已经补证，不能再报当前缺原条目；真实Main/IAM/receipt/文件/导出/PLM实例仍是不同层。未选择CRM主动BP申请不阻默认Main H04，账户不新增主动Producer。[原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d2/d21d88ea813e611c__SOURCE-REUSE.md:35>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d2/d21d88ea813e611c__SOURCE-REUSE.md:41>) [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4a/4a4d37635eaaa6c6__DECISION-OPTIONS.md:10>)


[UA13]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e4af65be952562__ACCEPTANCE.md:13>
[UA36]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e4af65be952562__ACCEPTANCE.md:36>
[DES17]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a4fe1ce60ff0340__DESIGN.md:17>
[OP17]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/ab181510eef18279__OPS-ACCOUNT.md:17>
[DES49]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a4fe1ce60ff0340__DESIGN.md:49>
[OP11]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/ab181510eef18279__OPS-ACCOUNT.md:11>
[OP41]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/ab181510eef18279__OPS-ACCOUNT.md:41>
[UA24]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e4af65be952562__ACCEPTANCE.md:24>
[PD16]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/80/8029ab7aa847c9d1__PAGES-DELTA.md:16>
[RV7]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/66/661b5a2f8e344f0d__REVIEW.md:7>
[AC3]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/12/123cf9b78f7de6ab__ACCEPTANCE-SEQUENCES.md:3>
[FIELD5]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/05/05f59b2151c73245__FIELDS-ACTIONS.md:5>
[FIELD94]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/05/05f59b2151c73245__FIELDS-ACTIONS.md:94>
[OP23]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/ab181510eef18279__OPS-ACCOUNT.md:23>
[C2_11]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1aa9ff8896815__SR-20261002-B-CRM-INQ-MD01-RECOVERY-R2-INDEPENDENT.md:11>
[RM36]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bed32d5ca99a24a2__RECEIPT-MAPPING.md:36>
[UA31]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e4af65be952562__ACCEPTANCE.md:31>
[OP5]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/ab181510eef18279__OPS-ACCOUNT.md:5>
[M5]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/439f98e8c16eb00d__OP-STATE-MATRIX.md:5>
[OP35]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/ab181510eef18279__OPS-ACCOUNT.md:35>
[CC9]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bd97dd191196cc6a__CONTRACT-CARDS.md:9>
[PD51]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/80/8029ab7aa847c9d1__PAGES-DELTA.md:51>
[CC16]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bd97dd191196cc6a__CONTRACT-CARDS.md:16>
[OP70]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/ab181510eef18279__OPS-ACCOUNT.md:70>
[C2_60]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1aa9ff8896815__SR-20261002-B-CRM-INQ-MD01-RECOVERY-R2-INDEPENDENT.md:60>
[CL27]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c2/c20ce5cbfb05b5a1__CLARIFICATIONS.md:27>
[C2_28]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1aa9ff8896815__SR-20261002-B-CRM-INQ-MD01-RECOVERY-R2-INDEPENDENT.md:28>
[RM30]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bed32d5ca99a24a2__RECEIPT-MAPPING.md:30>
[RM7]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bed32d5ca99a24a2__RECEIPT-MAPPING.md:7>
[RM18]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bed32d5ca99a24a2__RECEIPT-MAPPING.md:18>
[RM23]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bed32d5ca99a24a2__RECEIPT-MAPPING.md:23>
[C2_50]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e1aa9ff8896815__SR-20261002-B-CRM-INQ-MD01-RECOVERY-R2-INDEPENDENT.md:50>
[PD47]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/80/8029ab7aa847c9d1__PAGES-DELTA.md:47>
[RM42]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bed32d5ca99a24a2__RECEIPT-MAPPING.md:42>
[RM48]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bed32d5ca99a24a2__RECEIPT-MAPPING.md:48>
[AC24]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/12/123cf9b78f7de6ab__ACCEPTANCE-SEQUENCES.md:24>
[PER7]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f2/f2b0c054816007df__PERMISSIONS-STATE.md:7>
[PAGE29]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56d049450d9d64ea__PAGES.md:29>
[PD30]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/80/8029ab7aa847c9d1__PAGES-DELTA.md:30>
[PER35]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f2/f2b0c054816007df__PERMISSIONS-STATE.md:35>
[UA26]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e4af65be952562__ACCEPTANCE.md:26>
[PAGE7]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56d049450d9d64ea__PAGES.md:7>
[PD9]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/80/8029ab7aa847c9d1__PAGES-DELTA.md:9>
[RM59]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bed32d5ca99a24a2__RECEIPT-MAPPING.md:59>
[RULE113]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c5/c5f63cf5d7f27627__RULES-RECOVERY.md:113>
[A1_3]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3e/3ef2f5a0c2bfce71__ACCEPTANCE.md:3>
[A2_3]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e5/e51b3ead9274f739__ACCEPTANCE-DELTA.md:3>
[UA30]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e4af65be952562__ACCEPTANCE.md:30>
[AC11]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/12/123cf9b78f7de6ab__ACCEPTANCE-SEQUENCES.md:11>
[UA21]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e4af65be952562__ACCEPTANCE.md:21>
