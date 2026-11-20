# WP04-03 Main 账户映射、ReverseClaim 和纠错目录

WP04-03 在 Main https://github.com/GTX537/CP6/pull/1289 办理 DONE。ERP-BP-01 保持开放，原 AC **79/124**，必需运行修订 **3/10**。本报告核对了完整原目录和实际采用条件。新增的通过项只采用实际执行覆盖原断言的证据。其余保留登记状态并说明已核对的部分。[Main PR](https://github.com/GTX537/CP6/pull/1289) · [原任务](../../task-plans/WP04.md#wp04-03)

## 派工与采用

派工采用提交 `a64317590a2044d623294b7fed750d411a359b85`。派工原提交 `d3063e7a2ac42e18e96e36631ca481394e0046c5`，完整路径 `handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T111422Z-exec-wp02-14b-wp04-03/MESSAGE.md`。执行人 `EXEC-WP02-14B`，Main DEV-1 工作区 `D:/CP6-worktrees/wave17-dispatch-20261018`，任务分支 `codex/wp04-03`。START 路径已按原链修正并回读。[派工原件](https://github.com/GTX537/CP6.AI-Handoff/blob/d3063e7a2ac42e18e96e36631ca481394e0046c5/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T111422Z-exec-wp02-14b-wp04-03/MESSAGE.md) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/965b1ab71710a6718ca3ffbdc8ffd7c05420a92d/handoffs/CP6-DEV-20261012/tasks/WP04-03/messages/20261120T112222Z-exec-wp02-14b-start/MESSAGE.md)

开工 Main `18143a98567d7166ed6168c7312a99781568784b`，CRM `8a9b2a5c3ed468953a0c63a483515c88690a23b4`。已核实 WP02-10、WP02-13、WP02-13b、WP03-05、WP04-02 完整 DONE，并在相应主线上确认实际采用。交付产品提交 `459ddfeaa6a512d260fb58d78b269469f722f127`，受验产品树 `49179dc308fe06ce73351d196ab9223715cb26ef`。合并提交、采用后冒烟、分支处置及正式 finish 以 Main PR 的合并记录与原通信分支 DELIVERY 的实际回执为准。

## 实际执行

- B15 保存已登记签发者的 Account 原字节和 ReceivedTrust，浏览器只收到最小观察。B16 保存同一 Actor、租户、法人及账户的完整 criteria、候选、旧头和适用版本。B17 保存完整意图并发 ETag。连续进程重启验证真实原上下文继续执行。不同窗口混用、无权访视和伪签名均拒绝。
- PostgreSQL 测到两个账户竞争时唯一 ReverseClaim。CREATE_AND_LINK 的 Root、资料版本、九槽、审计、命令和 Outbox 一起提交。故障发生在后段事件写入时，整组回滚。REPLACE 旧占用和新占用随同一提交移动，后段事件错误保留旧关系。LINK 和 UNLINK 的当前/历史分支分开核对。CONFIRM 新增观察和事件，返回 NO_CHANGE 并保留关系版。
- 七字段 Outbox 原字节及 BodyHash 独立恢复。模拟目标处理后回包失败与重试使用同 EventId 和同字节。过期租约响应只入尝试历史。I03 真实授权读取无槽为 UNSEEN，有槽返回原事件。R03 单独签发并验证 `ACC-MAPPING-DECISION/1`，使用登记租户法人、Scope identity 与 epoch、原决定和精确 Account 资料串。关系版 2 与决定位置 1 分别验证。
- CRM 头高于 Main 或不完整冲突覆盖时，B27 持续 UNPROVEN。有据原 r12 在完整当前覆盖和 Owner 证据下更正到 r13，保留原 BP 和原交易引用。凭证撤销后旧 B25 和 B27 即使同值恢复也不能重用。然后真实 UNLINK 到 r14。后发查询失败后，先发迟到成功只入历史。租约接管和同一 JobId 恢复使用隔离 PostgreSQL。
- 实际 C03 报价转订单调用 I02，源核当前反向占用、签名 Owner 决定和本事务可锁的 Account 资格守卫。独立会话观察 REPLACE 等待 Scope X，C03 的业务行、UseReceipt、审计和结果同一外层 WAL 提交。换指后旧新意图拒绝，原键仍取回相同订单提案和 UseReceipt。实际退休及恢复保留映射、历史和冻结。
- 前向迁移以 WP04-02 迁移后的既有命令/伙伴模拟数据为输入，新迁移保留精确原命令内容和指纹。Owner 初始空登记，部署、CRM 权威来源接线及上线采用按其各自登记执行。

## 验证证据与模拟边界

专项 10/10，相邻伙伴 21/21，ERP 内部鉴权 18/18，相邻 Vue 528/528。Vue 类型检查与生产构建通过。卡内结果见 `candidate-final.trx`，最终 Vue 构建实跑见 `candidate-browser.trx`。相邻结果见 `candidate-partner.trx`、`candidate-erp-auth.trx` 和 `candidate-unicode.trx`。伙伴组合的迁移夹具缺失旧资料行，已补齐后在最终卡内通过。Unicode 15.0 原输入单独实跑。相邻路径沿用影响范围一致的已执行结果。

| 验证面 | 使用的真实入口 | 模拟输入和故障 |
|---|---|---|
| B15/B16/B17/B18 | Main Kestrel、当期 IAM 与隔离 PostgreSQL | 两个账户身份、签名 CRM Account、审批原件与来源资格守卫，构造无权、过期/撤证及事件写失败 |
| 浏览器 | 生产 Vue 构建、Chromium 和真实 Main API | 登录用户、设备和字段值，代理在实际 POST 提交之后丢成功回包 |
| 七字段、I03/R03 | Main 持久租约/原件、真实内部 HTTP 鉴权和签名检查 | 各外部 Owner/服务身份、桥接应答、租约时钟和晚到传输。真实 CRM A13 采用留在对应卡 |
| 对账与纠错 | Main B19/B20/B25/B26/B27、实际 Worker 处理逻辑与隔离 PostgreSQL | 签名投影和完整冲突集合、原错误字节、保真的 ReceivedTrust、模拟授权审批/撤销 |
| I02/C03 | 真正 Main 外层事务、资格锁、C03 原事件、订单、回执与 WAL | 测试报价、用户/设备、Account 当前 Owner 守卫和隔离服务运行 |
| 迁移 | PostgreSQL 18 的前向迁移与原命令保全 | 新建隔离库与模拟既有伙伴/命令 |

原件摘要、业务证据 JSON 和真实 Vue 截图归档于 `D:/CP6-test-artifacts/WP04-03/current-20261120`。证据索引为 `proof.json`，详细样本在 `permission-consumption-wp0403-candidate-final` 与 `permission-consumption-wp0403-candidate-browser`。来源发放和目标采用的跨库集成待实际 Owner 接入验证。

WP03-17 官方候选 `2026-11-20T11:03:46.711084+00:00`，正式 finish `2026-11-20T11:06:18.453615+00:00`。核对 Main 采用台账未含补记，按 #360 补 151.743 秒一次，卡内累计从 3033.005 到 3184.748 秒。本卡至核销候选 4043.652 秒，目标核对另列 0.242 秒。计划累计 111632.631 秒。[签收原件 #360](https://github.com/GTX537/CP6.AI-Handoff/blob/ab429fd5618cf799470de105b402066a3382affe/handoffs/CP6-DEV-20261012/tasks/WP03-17/replies/20261120T111120Z-coord-acceptance-ack/MESSAGE.md)

## 现有承接卡与待裁决范围

- WP03-07 承接。 CRM A13 实际采用七字段和 R03。覆盖接收乱序、原始冲突、签名分叉裁决及 CRM 当前资格竞争。 原项 AC047、AC058、AC086、AC091。 必需修订 W1-ACC-03、W1-ACC-04、W1-ACC-05、W1-ACC-06。
- WP03-06 承接。 CRM 账户来源证明及旧 C03 结果按请求回读。保留 ResultVersion、原字节和无证 H02 原 Manifest 的等待行为。 原项 AC065。 必需修订 W1-ACC-08。
- WP04-16 承接。 Sales 正式确认的实际信用、技术和伙伴 Owner 参加。验证 Main 当前映射在缓存投影之后改变及原资格已失效的最终拒绝。 原项 AC041、AC081。 必需修订 W1-ACC-07。
- WP14-04 承接。 用真实采购来料和应付链核实无 Account 的供应商用途，保留其原 BP 参与、关系和实际业务写集。 原项 AC039。
- WP14-08 承接。 跨仓实际环境和参与者的撤权、未知与迟到原结果专项。复核已采用权限、同原键/额度和实际边界，待核部分保留原范围。 原项 AC070、AC073、AC074、AC076、AC077、AC078、AC107、AC108、AC115、AC117、AC121、AC122。 必需修订 W1-ACC-01。

未归属项为 AC011、AC021、AC022、AC023、AC024、AC026、AC027、AC028、AC035、AC036、AC037、AC063、AC072、AC075、AC079、AC082、AC090、AC092、AC093、AC102、AC104、AC105、AC109、AC119、AC124。已当期沿原链发出 QUESTION 与全量核对补充。正式伙伴 CSV 尤其涉及 5001 行、公式转义与当前字段裁剪，已采用的旧档 CSV 范围无法满足这部分原断言。[原链 QUESTION](https://github.com/GTX537/CP6.AI-Handoff/blob/24bfccf40885cef8ad70af11f66268150687027e/handoffs/CP6-DEV-20261012/tasks/WP04-03/messages/20261120T115803Z-exec-wp02-14b-question/MESSAGE.md) · [全量核对补充](https://github.com/GTX537/CP6.AI-Handoff/blob/63671cc45c73931bb02489796fbab3f50e5018ec/handoffs/CP6-DEV-20261012/tasks/WP04-03/messages/20261120T121808Z-exec-wp02-14b-question-audit/MESSAGE.md)

## 原 124 项逐项核对

原目录冻结文本来自 [commercial-finance.json](../../assessments/20261011/catalogs/commercial-finance.json)。未通过或未完整执行行保留原登记。真实受影响场景已在当前产品路径复测，采用旧证据的行保留原条件。

| 原项 | 触发条件 | 完整期望 | 登记 | 证据或明确余项 |
| --- | --- | --- | --- | --- |
| AC001 | BpId/L/BpCd已保存，通用PATCH试图改变其中一项 | 400 BP_SCOPE_IMMUTABLE，原身份/Revision不变 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。身份 ID、法人、编号三个 PATCH 均为 400 BP_SCOPE_IMMUTABLE，原身份、版本和成功命令数保持。 |
| AC002 | 同Tenant另法人已有同BpCd，再创建同号 | 最终UQ拒绝BP_CODE_EXISTS，不按法人放宽旧key单值性；不泄露无权对象 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。同租户 JP-B 预览与执行重用 JP-A 编号被拒，结果为 BP_CODE_EXISTS，回执和候选均不披露 JP-A 对象。PostgreSQL 保留租户及编号唯一约束。 |
| AC003 | BP-C001已RETIRED或旧Status9，再用该号创建 | 不重用身份/编号；候选保留历史 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。B13 退休后按同编号预览和提交，历史候选仍指原 BpId 且显示 RETIRED。B06 返回 BP_CODE_EXISTS，保持一个历史身份。 |
| AC004 | 十三roles全部false或只有CreditMgmt/Maker | ROLE_SET_INVALID；无BpRoot/Command成功记录 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。全 false 及仅信用/制造商角色都返回 400 ROLE_SET_INVALID，没有新增成功命令。 |
| AC005 | 预登记后更改SupplierFlg或CustomerFlg | ROLE_IMMUTABLE；UI无普通改角色入口，历史role不变 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。SupplierFlg 与 CustomerFlg 用资料预览提交均为 400 ROLE_IMMUTABLE，Vue 只在建档显示角色编辑，原版本保持。 |
| AC006 | 合法核心身份但客户AR/员工缺失，分别PRE_REGISTERED和已填非法字段 | 缺完整性可预登记，Detail列missing；非法已填值仍拒绝，不以预登记绕过格式 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。角色资料缺失时合法 PRE_REGISTERED，详情显示缺少 CUSTOMER_AR，已填非法电话仍返回 400。 |
| AC007 | REGISTERED客户缺CUSTOMER_AR或salesStaffCd | PROFILE_INCOMPLETE，准确fieldErrors；无注册BP | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。请求 REGISTERED 时缺 CUSTOMER_AR 和销售担当，预览准确列 PROFILE_INCOMPLETE，执行 422 且没有注册伙伴。 |
| AC008 | E02–E06的客户/AR/Billing/Receipt SELF | 实际新BpId回填三关系，九槽完整，ProfileRevision1和Root一致 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。实际 B06 的三个 SELF 槽指向本次 BpId，九槽完整，Root 与 ProfileRevision1 一致。 |
| AC009 | 同名候选存在但NEW_DISTINCT未列其准确CandidateRef | 预览canApply=false；完整确认+reason后才可建不同编号BP | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。Unicode 归一后同名候选需要准确候选及理由，NEW_DISTINCT 只创建另一个不同编号的主体。 |
| AC010 | 全域查重命中不可见BP | REVIEW_REQUIRED，不返回隐藏ID/名称/数量；不得当空集合创建 | PASS | 正式映射候选命中当前无权据点的伙伴。只返回 REVIEW_REQUIRED 和空公开集，强行建档预览不可执行。 工件 wp04-03-authorization.json。 |
| AC011 | 符合规则候选超过50 | 返回DUPLICATE_REVIEW_REQUIRED，不能只显示50后声称全确认 | NOT_RUN | 正式候选闸及隐藏成员已实测，超过 50 个真实规范匹配候选的阈值未执行。 已沿原链请总控裁决。 |
| AC012 | 两个不同命令同Tenant同时创建同BpCd | 最多一个Root/初始Profile成功；另一个BP_CODE_EXISTS，不能套用对方回执 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。双 B06、各自 UUID、同时用同租户同号执行，200 和 409 各一，只有一条 Root、初始资料与成功回执。 |
| AC013 | B05预览后ScopeRevision改变 | B06 PREVIEW_STALE，无部分写，必须实际重读 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。另事务提升 scopeRevision 后，原 Preview 执行返回 412 PREVIEW_STALE，业务记录数保持。 |
| AC014 | E05创建成功后E23冻结，再重发原Command301 | E40原Revision1/ReceiptHash；当前BP仍Revision2/FROZEN | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。创建后冻结，再按原键执行，返回完全相同的 Revision1 回执，当前仍 Revision2/FROZEN。 |
| AC015 | 同PreviewId换Command或同Command换Preview/body | 分别PREVIEW_ALREADY_USED或COMMAND_IDEMPOTENCY_CONFLICT，不第二次保存 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。同 Preview 换 UUID 为 PREVIEW_ALREADY_USED，原 UUID 改 Preview 为 COMMAND_IDEMPOTENCY_CONFLICT，零重复保存。 |
| AC016 | profile bool传"false"、Dec传JSON浮点、字段超长或未知键 | 400 FIELD_INVALID/UNKNOWN_FIELD，不截断、不自动类型转换 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。字符串布尔、浮点 JSON、超长电话、未知键均 400，类型与精度没有隐式转换。 |
| AC017 | PATCH银行账号/CrmAccountId/Status | OWNER_FIELD_READ_ONLY，不能间接修改Finance/映射/状态 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。账户原始账号、CrmAccountId 和状态均为 400 OWNER_FIELD_READ_ONLY。 |
| AC018 | 无敏感读权Detail将Tel=null并redactedFields，用户只改名称 | 只修改明确operation；原Tel不清空；显式敏感写403 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。失去敏感权后 Tel=null 且列入遮罩，显式 null 写入 403，只修改名称后旧 Tel 保留。 |
| AC019 | RelationSlot指同Tenant不同L或目标缺所需role | RELATION_SCOPE_MISMATCH/REFERENCE_INVALID；不按UUID存在即通过 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。跨法人关系返回 RELATION_SCOPE_MISMATCH，当前目标缺 AR 角色返回 REFERENCE_INVALID。 |
| AC020 | SELF要求AR但本BP无AR角色 | REFERENCE_INVALID；不得创建自证虚假关系 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。SELF 缺目标角色时 REFERENCE_INVALID，只有两个合法样本被持久化。 |
| AC021 | PARENT_CUSTOMER形成环或超32层 | RELATION_CYCLE/REFERENCE_UNPROVEN；无父链部分写 | NOT_RUN | 采用的 WP04-01 与本轮复测均含真实父链成环，完整 32 层限界缺实际输入。 已沿原链请总控裁决。 |
| AC022 | BP兼Supplier+AP，旧PaymentScheduleCd只有一个值 | 不能默填两个槽；明确SUPPLIER_AP/AP_SCHEDULE各自证据；未知不通过对应交易使用 | NOT_RUN | 九槽原图按定义区分。旧 PaymentScheduleCd 单列历史与两个独立用途的完整共存迁移未验。 已沿原链请总控裁决。 |
| AC023 | SupplierPattern!=5却传subcontractTarget=true或外注专属值 | FIELD_INVALID；不能获得外注/制造许可 | NOT_RUN | 复用既有字段约束。外注/制造实际消费者对非 5 模式授予资格的负向链缺实证。 已沿原链请总控裁决。 |
| AC024 | PaymentSchedule paidEachTime=false且closingDay缺失，另测99 | 缺失PROFILE_INCOMPLETE；99保存为末日规则，不替成今天日期 | NOT_RUN | 既有真实 99 月末规则已回读，paidEachTime=false 与缺失 closingDay 的同条明确拒绝未核销。 已沿原链请总控裁决。 |
| AC025 | DeliveryTimeFrom>To且无跨日定义 | TIME_WINDOW_INVALID；不猜跨日；合法HH:mm:ss原值保存 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。不允许未定义的跨日时间窗，合法 HH:mm:ss 回读精确到秒。 |
| AC026 | 旧CurrencyCd=null，新用途读取或维护 | 历史保留NULL未证，新使用不默认为JPY；明确新Currency决定不改旧订单币种 | NOT_RUN | 旧档资料可读、正式用途无兜底。旧 CurrencyCd=null、明确币种及既有订单原币的组合需迁移输入。 已沿原链请总控裁决。 |
| AC027 | 5001行导出；名称以=开头 | 前者EXPORT_LIMIT无截断；合法范围CSV作公式文本转义、当前字段裁剪 | NOT_RUN | 旧控制器 CSV 受旧档投影限制。正式伙伴 5001 行拒绝、公式转义及当前字段裁剪缺入口，已问总控。 已沿原链请总控裁决。 |
| AC028 | Finance/字典Owner暂未知，尝试无关名称维护与注册/使用 | 合法不依赖该证据的维护可完成；涉及未证引用的注册/使用失败关闭，历史可读 | NOT_RUN | 既有 Finance 离线与注册隔离可复用。完整字典/Finance 缺证及独立维护、交易使用、历史的组合未验。 已沿原链请总控裁决。 |
| AC029 | FREEZE使BP Revision1→2 | Root/ProfileRevision2/StateHistory/Audit/Command同txn，不留缺Profile的Revision | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。FREEZE 的资料、时间线、审计和命令 xmin 一致，根行版本和 Hold 与 ProfileRevision2 一致。故障模拟使整个事务回滚，随后同当前版本可成功执行。 |
| AC030 | FREEZE先持Scope X提交，I02随后S | I02读到FROZEN拒绝，零UseReceipt与下游新效果 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。实际 B12 冻结先持 Scope X 和提交门。C03 的 Scope S 等待，按同键处理 PostgreSQL 的已知序列化中止，随后读到 FROZEN。无新增 UseReceipt、订单、业务审计、成功命令或成功 Outbox。 |
| AC031 | I02先持S完成真实业务txn，FREEZE等待 | 一条UseReceipt与业务效果commit；随后冻结成功；历史有效和当前冻结并存 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。实际 C03 已暂存时 B12 等待同一 Scope X。UseReceipt、订单及明细、桥接登记、审计、成功命令和 Outbox 七行的 PostgreSQL 行事务号归属同一条外层 WAL COMMIT，覆盖 EF 保存点子事务。冻结随后提交，原业务保留。 |
| AC032 | 已commit UseKey再次用于新ConsumerRevision或目的 | USE_IDEMPOTENCY_CONFLICT；原同意图重放仅证明当时一次效果 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。已冻结后同键同指纹只回原凭证。把原键用于新的 ConsumerRevision 或用途得到 USE_IDEMPOTENCY_CONFLICT。 |
| AC033 | RETIRE当前有Mapping与旧订单的BP | 不删Mapping/历史、不发UNLINKED、不重用BpCd，新使用拒绝 | PASS | 真实 C03 已保存第二笔订单及其映射。退休该伙伴不改 r2、历史 UseReceipt、原事件或原业务编号。恢复后仍冻结，新用途拒绝。 工件 wp04-03-c03-i02.json。 |
| AC034 | RETIRED且FROZEN执行RESTORE | Lifecycle ACTIVE，Hold仍FROZEN；新使用仍拒绝 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。实际停用再恢复将 Lifecycle 恢复为 ACTIVE，Hold 仍 FROZEN。再次提交 C03 新用途被拒，零新业务效果。 |
| AC035 | PRE_REGISTERED且FROZEN执行REGISTER | 满足资料时可REGISTERED但Hold不变；没有交易放行 | NOT_RUN | 原卡已验注册及冻结独立，缺预登记冻结后成功注册和真实 I02 仍拒绝的完整同一对象组合。 已沿原链请总控裁决。 |
| AC036 | UNKNOWN_LEGACY旧删档普通REGISTER或RESTORE无confirm | 拒绝；有权明确当次重新确认后才建当前REGISTERED，旧历史仍UNKNOWN | NOT_RUN | 旧删档 UNKNOWN 与正式恢复的资料再确认组合需已审批的旧档采用输入。 已沿原链请总控裁决。 |
| AC037 | 冻结/停用BP修改合法名称或交易资料 | 有权可维护，独立状态轴不变；不自动绿灯/恢复 | NOT_RUN | 已核真实冻结与普通资料维护。退休状态下完整名称/资料维护和原保留轴组合仍缺回归。 已沿原链请总控裁决。 |
| AC038 | Supplier-only请求SALES_NEW_SOURCE | BP_ROLE_MISSING；不能以Supplier有效代替Customer | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。完整 Supplier 角色档提交 SALES_NEW_SOURCE 得 BP_ROLE_MISSING。使用采购用途时拒绝携带 Account。 |
| AC039 | E47–E52 Supplier/AP/Schedule/Payment，无Account | 采购参与可成立且mapping=null；不创建CRM Account/PO/AP之外额外效果 | NOT_RUN | WP04-02 真实采购用途 I02 已用 4 角色、Account=null、Mapping=null 做外层回滚测试。真实采购/AP 最终效果尚需消费者。 承接 WP14-04。 |
| AC040 | 客户根ACTIVE但其实际AR或收款依赖FROZEN | 当前角色闭包拒绝新Sales用途，不能只核入口BP | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。主伙伴 ACTIVE，实际应收依赖被 B12 冻结。C03 复核当前角色闭包并拒绝新用途，订单及凭证均未写入。 |
| AC041 | I01 KNOWN_ALLOW但Sales信用或技术Owner拒绝 | 没有实际下游commit/UseReceipt；BP预览不能替下游放行 | NOT_RUN | 本卡 C03 验 BP 资格和订单共事务。实际 I01 正向后由信用/技术 Owner 否决及业务零写需 Sales 正式确认。 承接 WP04-16。 |
| AC042 | 下游改为另一数据库，仅HTTP先获取BP许可 | OWNER_PARTICIPANT_UNPROVEN，不声明共享DB锁提供跨库原子 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。连接到隔离 PostgreSQL 中的另一数据库后，把其原生事务交给 I02，得到 OWNER_PARTICIPANT_UNPROVEN。没有可执行的 HTTP 预授权或本库新凭证。 |
| AC043 | 同UseKey内容/actorOwner不同 | 409同键异意图，原Receipt不覆盖 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。同一原 UseKey 修改已核消费意图、Purpose 或执行 Actor 得 USE_IDEMPOTENCY_CONFLICT，已存原凭证字节保持。 |
| AC044 | 同真实ConsumerOwner/id/version/purpose换UseKey | 唯一意图约束拒第二许可，不能产生第二业务效果 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。换 UseKey 提交同一 ConsumerOwner、BusinessId、Revision 和 Purpose 直接冲突，数据库仍只有一份原凭证。 |
| AC045 | 旧BP禁用后读原UseReceipt/订单引用 | 当前有权历史仍可读，不执行新使用门删除历史；敏感字段仍重新授权 | PASS | 已退休当前伙伴的 USE 历史读取到原键，原记录保留。对账没有重执行业务，恢复先保持冻结。沿用 WP04-02 的当前字段遮罩复测。 工件 wp04-03-c03-i02.json。 |
| AC046 | B15 Account未找到/无权/Source未证 | 如实返回相应availability，observation=null，不创建Account | PASS | 实际 B15 返回 NOT_FOUND、NOT_AUTHORIZED、UNPROVEN 和空 Observation。伪签名也未产生可用观察。 工件 wp04-03-b15-b18.json。 |
| AC047 | 真实Account INACTIVE，Main核实身份关系 | 可维护准确历史映射并显示CRM停用；不能据此新建CRM业务关联 | NOT_RUN | 签名模拟 CRM 明示 INACTIVE 时准许准确身份映射。Main 对未获当前资格的新用途拒绝。CRM 原活动新关联仍须实际消费者采用。 承接 WP03-07。 |
| AC048 | E09–E15首次LINK | Mapping1/Claim/原Event/Command/Audit同txn；空槽没有虚拟rev0 | PASS | 持久 B16、已核 Account 与原 B17 恢复。首次实际 B18 写 r1、唯一 Claim、事件、审计和原回执，失败的另一区域键无成功事实。 工件 wp04-03-b15-b18.json。 |
| AC049 | 两个Account并发LINK同法人同BP | 反向Claim最多一方取得；失败方没有Mapping或Outbox成功记录 | PASS | 两个账户从同一合法候选竞争同一 BP。一个 200、一个 MAPPING_TARGET_CLAIMED，只有一项占用和一次业务写组。 工件 wp04-03-b15-b18.json。 |
| AC050 | REPLACE取得新Claim前发现占用/事务异常 | 旧Claim和旧Mapping完整保留，没有半解绑 | PASS | 实际 REPLACE 在旧占用释放及新占用保存后的事件故障整体回滚。失效凭证亦拒绝，原头、占用和历史数量保留。 工件 wp04-03-mutations.json。 |
| AC051 | 当前LINKED再次LINK另一BP | MAPPING_ACTION_CONFLICT；必须有旧头、证据的REPLACE | PASS | 已 LINKED 的槽再次要求 LINK 另一伙伴，MAPPING_ACTION_CONFLICT，须使用核准 REPLACE。 工件 wp04-03-mutations.json。 |
| AC052 | UNSEEN槽请求UNLINK | 拒绝，不能以空查询制造UNLINKED事实 | PASS | UNSEEN 请求 UNLINK 的预览不可执行，没有写 UNLINKED。 工件 wp04-03-mutations.json。 |
| AC053 | CONFIRM同当前LINKED或UNLINKED | MappingRevision不变，新Observation/Event/asOf，旧body不改；当前关系仍需核证 | PASS | 有目标和已解绑两个 CONFIRM 均返回 NO_CHANGE，关系分别保持 r1/r3，各有一份新观察原事件且摘要不同。 工件 wp04-03-mutations.json。 |
| AC054 | B25 SourceRef存在但原凭证用途无权或范围不符 | Binding NOT_AUTHORIZED/UNPROVEN，不能凭hash变VERIFIED | PASS | 实际源以准确主体校验可读原件。存在的原凭证不支持另一原因时 B25 为 UNPROVEN。 工件 wp04-03-mutations.json。 |
| AC055 | REPLACE的reason/目标/旧头与B25 Subject不同 | B17/18拒绝，无准确Binding不得执行 | PASS | 用已核 Binding 改写原因，B17 无有效预览。当前 Owner 守卫撤销，B18 拒绝且保留原占用。 工件 wp04-03-mutations.json。 |
| AC056 | Mapping预览后目标BP或Scope发生变化 | PREVIEW_STALE/412，不能使用旧候选绕过新状态 | PASS | 伙伴状态变动推进真实 Scope 与 BP 版本。原映射预览执行为 412，无成功映射或 Outbox。 工件 wp04-03-authorization.json。 |
| AC057 | E15/E38发往ACC | 正文仅既有七字段；单Tenant受信身份；没有新增Tenant/BpId/Hash字段 | PASS | Main 出站为准确七字段。单租户登记凭证的模拟 CRM 端直接接收原 UTF-8，每份 BodyHash 可复算。 工件 wp04-03-reconcile-correction.json。 |
| AC058 | E38版本2先应用，E15旧版本1晚到 | CRM保持2；旧1不刷新2的sourceAsOf，Main历史各自保留 | NOT_RUN | Main 按原 bytes 正确重试。CRM 模拟桥保最高版本，CRM 实际七字段 v2 后收到 v1 的业务记录与源时间待 WP03-07。 承接 WP03-07。 |
| AC059 | Outbox租约过期，旧worker晚回ACK | CAS不允许旧Claim覆盖新状态；原Event/body不变 | PASS | 时钟驱动实际 PostgreSQL 租约接管。过期发送者的 409 只留本次尝试历史，新拥有者的 ACK 与原事件不被改写。 工件 wp04-03-leases.json。 |
| AC060 | ACC200处理了较旧或重复消息 | 只ACKED_PROCESSING，不标MATCHES_CURRENT | PASS | 真实投递重跑同一原件时，CRM 模拟桥处理并返回 200。Main 只登记 ACKED_PROCESSING，当前投影保持待核。 工件 wp04-03-reconcile-correction.json。 |
| AC061 | ProjectionReader真实当前五元组等MainHead | 有observedAt/asOf的MATCHES_CURRENT；不当无限实时保证 | PASS | 向已登记签名投影桥读取原五元组并复算摘要。Main 重比全部值后显示有源时间和观察时间的 MATCHES_CURRENT。 工件 wp04-03-reconcile-correction.json。 |
| AC062 | 原绿色之后新的查询失败/可信冲突通知 | 当前知识立即UNKNOWN/CONFLICT，旧绿色仅历史 | PASS | 实际已匹配后发起新代，旧绿色立即失效。后发查询暂失败安装 UNKNOWN，前发迟到成功只在历史。 工件 wp04-03-reconcile-correction.json。 |
| AC063 | 发起查rev1期间Main变rev2，rev1结果晚到 | 历史保存但不装到rev2当前指针，不误显示已同步 | NOT_RUN | 真实同 r12 跨 generation 迟到已过。查询期间 Main 从 r1 升 r2 后完成旧结果的独立样本未执行。 已沿原链请总控裁决。 |
| AC064 | CRM回rev高于Main或同版异义无法解释 | AHEAD/OWNER_STREAM_CONFLICT暂停修复；不MAX+1清冲突 | PASS | Main r12 收到 CRM 受信 r13 为 AHEAD，B27 仍 UNPROVEN。没有用最大值续号，凭证不足的同版异义也保持隔离。 工件 wp04-03-reconcile-correction.json。 |
| AC065 | 旧C03 ResultVersion未含L/映射权威证据 | 不能改名为MappingRevision；新消费不据此建LINKED | NOT_RUN | C03 原 RequestVersion 保持，通过 owner 消费生成原结果。CRM A12 旧 ResultVersion 历史与新映射槽独立采用尚需准确结果流。 承接 WP03-06。 |
| AC066 | I03读不到Main权威槽 | UNSEEN/current=null/events=[]；不是发UNLINKED或创建影子记录 | PASS | 采用有记录模拟服务身份调用实际 I03。无槽为 UNSEEN/current=null/空事件，无权服务凭证拒绝。 工件 wp04-03-i03-r03.json。 |
| AC067 | E39 current原事件base64解码 | BodyHash与E38原UTF8一致，回查无新业务Event | PASS | I03 回包 Base64 解码与 PostgreSQL 已提交 EventId、原 UTF-8、BodyHash 相等，回查不追加业务事件。 工件 wp04-03-i03-r03.json。 |
| AC068 | B20查询job已QUEUED，Owner读取暂失败 | B26显示WAITING/lastError，MainHead不变；job完成和当前匹配分开 | PASS | 实际 B20 查询离线进入 WAITING，原头不动。重启式新 Worker 实例按同 JobId 恢复并安装适用的受信观察。 工件 wp04-03-leases.json。 |
| AC069 | E30–E38有据从BP1改至BP2 | 当前Mapping2/BP2，E21历史BP1/Mapping1原样；不迁移旧订单 | PASS | C03 持 Scope S 提交原订单及 r1 UseReceipt，B18 等待后换到 BP2。原订单、引用与原键回执不变。 工件 wp04-03-c03-i02.json。 |
| AC070 | BP映射/状态保存或重试成功 | 无Handoff/Quote/Won/需求/库存/Finance第二账和商业事件 | NOT_RUN | Main 映射动作真实写集与订单零改，跨 Handoff/商机/工程/库存/Finance 的真实追踪组合尚未执行。 承接 WP14-08。 |
| AC071 | 缺强ETag/ExpectedRev或IfMatch=* | 428/400，不能降成旧无版本Save | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。B13 缺强 If-Match 返回 428，通配符为 400，旧版本为 412，业务行和原结果保持。 |
| AC072 | FK把另Tenant同UUID关联到Profile/Mapping/Relation | 服务与数据库共同拒绝，所有关系必须含T/L | NOT_RUN | 新 Postgres FK 固定租户法人。既有同租户跨法人场景复测，另租户同 UUID 的 Profile/Mapping/Relation 原组缺单独运行。 已沿原链请总控裁决。 |
| AC073 | 撤Main组织/动作/字段权与写命令竞争 | 同CurrentGuard决定一方先后，不能仅用旧按钮/旧预览权 | NOT_RUN | B18 已证撤创建/注册权及版本拒绝。实际竞争中管理员写与不同字段/范围读权的两次提交顺序未覆盖。 承接 WP14-08。 |
| AC074 | 真实Owner未采用、来源代次被撤销、fixture proof混生产 | UNPROVEN/拒绝，不能hash回显VALID | NOT_RUN | 已验未采用、伪签名与外部证据撤销。环境隔离后的生产信任代次和来源切换未激活。 承接 WP14-08。 |
| AC075 | 历史/错误/CSV读取敏感字段时失权 | 按当前字段权遮罩，不从错误、候选或日志泄漏原文 | NOT_RUN | 正式读取、原件密文及隐藏候选已验证。正式伙伴 CSV 当前裁剪缺入口，已问总控。 已沿原链请总控裁决。 |
| AC076 | 模拟与生产使用相同UUID/BpCd/Scope数字 | 物理/身份/控制水位隔离，模拟不占生产唯一域 | NOT_RUN | 隔离 PostgreSQL 与模拟 actor/authority 明确。真实环境隔离与相同入站键/水位的迁移演练留原职责。 承接 WP14-08。 |
| AC077 | 实际commit响应丢失后B22 NOT_OBSERVED | noEffectProven=false，同key查/重试；不新key重建 | NOT_RUN | 真实浏览器丢弃成功 B18 回包，按原键回正确。事务确定回滚时 NOT_OBSERVED。已提交后暂时 NOT_OBSERVED 的组合作为独立负向留验。 承接 WP14-08。 |
| AC078 | Preview/Outbox/ReceivedTrust原文被截断或hash不符 | 停止恢复并报告完整证据缺失，不从当前GET猜原意图 | NOT_RUN | 经实际投递适配器验证改字节会拒绝。B27 完整原件拒绝缺覆盖。Preview、ReceivedTrust 截断和原 B18 恢复的精确破坏组未运行。 承接 WP14-08。 |
| AC079 | 一个普通Profile更新与同BP状态动作同ExpectedRev | 最多一个成功；另一412，资料和状态历史都不覆盖 | NOT_RUN | 邻接实际 412 测试已复跑。一次普通资料更新与状态操作真正竞争同一根版本的并发组合未运行。 已沿原链请总控裁决。 |
| AC080 | 旧commerce-profile/C03/批导入未参加Scope共同门 | 新生产路径O-BP-SINGLE-WRITER未通过，不形成两个写者 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。切换租户内只按法人登记启用正式写者。旧 CRUD、commerce-profile 和内部 C03 资格修改被本地共同门拒绝。原生 COPY 与批量 UPDATE 均由数据库拒绝，未证明法人只读。 |
| AC081 | Finance信用额度为null或旧0，BP有CreditMgmt角色 | 不解释为已授信/无限额度；Sales/Finance仍按其真实信用政策 | NOT_RUN | Main 读取早期财务引用和用途资格保留。Sales 对真实信用未知/旧 0 的最终确认需专业信用 Owner。 承接 WP04-16。 |
| AC082 | 仅文书JSON/摘要检查通过 | 业务AC仍NOT_RUN，不计阶段100，不声称DB并发/物理接口已通过 | NOT_RUN | 静态样例不增加业务通过数。目标仅逐条核销符合本次真实运行或可复用证据的用例。 已沿原链请总控裁决。 |
| AC083 | B16读完整criteria后服务重启，再B17/B18 | 从持久Context恢复原Actor/T/A/L/完整criteria/setHash，不从hash或目标BP反猜 | PASS | 实际两个 Main 进程延用同一有效会话。B16 完整条件在前一进程持久化，后一进程从它生成 B17 并执行 B18。另一个原 B17 也跨进程执行。 工件 wp04-03-b15-b18.json。 |
| AC084 | 同Actor两窗口不同criteria，混ContextRef与另一个setHash | MAPPING_CONTEXT_MISMATCH，零业务写；准确Context可继续 | PASS | 同 Actor 两个候选条件不同且摘要分别固定。混用 Context 与 setHash 得 409 MAPPING_CONTEXT_MISMATCH，准确上下文可执行。 工件 wp04-03-b15-b18.json。 |
| AC085 | Main一直r12，Q1成功迟到、后发Q2先失败/冲突 | 跨Job ProjectionGeneration只允许最新当前代次安装，旧成功不能恢复绿色 | PASS | Main 固定 r12 时 Q2 暂失败先结束，Q1 迟到成功有独立历史原件，currentApplicable=false，不装当前指针。 工件 wp04-03-reconcile-correction.json。 |
| AC086 | 适用可信冲突通知在查询未完成时到达 | 同槽推进generation并置冲突，旧查询只留历史；已被准确更正覆盖的旧通知不复活 | NOT_RUN | Outbox 可信冲突与 generation 维护已接入。查询进行中真实冲突主动通知和已处置通知重放的跨仓采用待验。 承接 WP03-07。 |
| AC087 | 查询job租约过期后旧worker回包/服务重启 | 同时核当前Job Claim和持久Slot generation，旧attempt不得回写 | PASS | 同一 Job 跨 Worker 实例和模拟时钟租约接管，旧 ClaimId 的成功仅留历史，不覆盖接管人的 UNKNOWN 或后续原任务。 工件 wp04-03-leases.json。 |
| AC088 | OwnerConflict=true，随后普通查询声称匹配 | 普通成功不清OwnerConflict；须准确Owner更正及新后继读证 | PASS | OwnerConflict 已建立后，普通 CONFIRM 和声称相等的真实查询保留冲突。精确 r13 后继和新查询才安装绿色。 工件 wp04-03-reconcile-correction.json。 |
| AC089 | r12正确LINKED BP2，已证同版错误，CORRECT_CURRENT | 只生成r13同BP关系，保持Claim并更新其Revision，原UseReceipt/订单不改；重复命令不造r14 | PASS | Main r12 原 BP2、错误 CRM 字节、两个摘要、ReceivedTrust 和完整 Owner 依据核证。r13 只保留 BP2 并推进占用，旧 UseReceipt/订单行不变，重放无 r14。 工件 wp04-03-reconcile-correction.json。 |
| AC090 | 纠错混入其他Scope/Revision、缺原body/Trust或CRM高于Main | B27 UNPROVEN/OWNER_STREAM_CONFLICT，禁止MAX+1/假UNLINK | NOT_RUN | 真实 Main 经模拟签名桥验证缺覆盖、撤证与 CRM 领先均不可更正。混入不同真实范围、逐项破坏原体和执行审计的完整原组未运行。 已沿原链请总控裁决。 |
| AC091 | 正确更正13后真实UNLINK14，原12/13晚到 | 当前14与sourceAsOf保持；历史12/13完整，未伪造中间解除 | NOT_RUN | Main 真实纠错 r13 与解绑 r14 以及重投 r12 已验。CRM 模拟桥不回退，CRM 实际接受端的历史与源时间待 WP03-07。 承接 WP03-07。 |
| AC092 | FieldPolicy隐藏paidEachTimeFlg | ProfileRead保成员且null+redactedFields；公开私有内容hash=null，不返回false或省略成员 | NOT_RUN | 沿用字段遮罩规则。针对 paidEachTimeFlg 的完整读形及公开内容哈希断言仍待运行。 已沿原链请总控裁决。 |
| AC093 | 将R01遮罩null作为非nullable写bool提交 | 原失权Actor403；即使有权Actor也FIELD_INVALID，不放宽ProfileValues | NOT_RUN | 既有错误布尔类型被拒。具体已遮罩 paidEachTimeFlg=null 的失权及有权两次尝试还需复测。 已沿原链请总控裁决。 |
| AC094 | H04真实零候选CREATE_AND_LINK | 前端不提供未来BpId；新BP/Profile/关系/索引/Mapping/Claim/Outbox/Audit/Command同commit，Scope只增一次 | PASS | B16 完整零候选。同一实际复合命令写新 BP、九槽、资料版、归一索引、映射、占用、事件和审计，ScopeRevision 只加 1。Vue 核对新请求不含未来目标。 工件 wp04-03-mutations.json。 |
| AC095 | CREATE_AND_LINK新BP已暂存、Outbox前异常 | 新BP/Mapping/Claim/Command占位均回滚，无孤立BP；原命令查询不伪称已成功 | PASS | 新 BP 和占用已暂存时注入 PostgreSQL 事件故障，全部回滚。原键回查 NOT_OBSERVED，清除故障后同预览同键成功一次。 工件 wp04-03-mutations.json。 |
| AC096 | 零候选预览后另一命令占Account槽或改变Scope | 最终持门失败，不创建孤立BP，也不删除对方关系 | PASS | 真实零候选复合预览后另一伙伴的状态操作推进 Scope。最终 412，无孤立新 BP 或成功映射。 工件 wp04-03-authorization.json。 |
| AC097 | B20从B19真实Revision提交查询job | 仅Idempotency-Key+body expectedMappingRevision；If-Match省略，Fingerprint.ifMatch=null；错revision拒绝 | PASS | 实际 B19 当前修订通过 B20 body 与 Idempotency-Key 入队，不发送 If-Match。旧修订 412。 工件 wp04-03-reconcile-correction.json。 |
| AC098 | E06→E22与每个Preview→Apply连续链 | 实际响应给准确ETag，写请求逐字复制；不能从businessRevision/hash推导8-byte rowversion | PASS | 实际 Vue 向 B18 逐字回传 B17 的 ETag，回包丢失后按一个 UUID 原样重传。相邻 B05/B06、B08/B09 的实际 Vue HTTP 链随候选源复测。 工件 wp04-03-browser.json。 |
| AC099 | 100个U+FDFA合法输入、规范展开1800单元 | 原Name合法，完整Norm保存，不截300、不数据库溢出；hash后全串精确比较 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。100 个 U+FDFA 经 HTTP 保存，原显示 100 单元，数据库完整保存 1800 单元，准确同名经索引后匹配。 |
| AC100 | 归一版本不一致/旧索引覆盖未证，或强制hash索引碰撞夹具 | 前者UNPROVEN，不当无候选；后者再比完整规范串，不把不同企业误判同一 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。模拟归一版本不一致时 REVIEW_REQUIRED，强制相同索引摘要夹具仍按完整规范字符串比较。 |
| AC101 | B01当前Actor只有mapping无create | Base.canCreate=false/createModes=[]，B16不列可执行CREATE_AND_LINK；仍可准确读/关联现存BP | PASS | 角色撤销 add 后 Base.canCreate=false，B16 无 CREATE_AND_LINK，仍可准确读取并 LINK 已建伙伴。 工件 wp04-03-authorization.json。 |
| AC102 | 有create/Base/字段权，无register | 只提供PRE_REGISTERED模式；REGISTERED B05/B06/B17/B18均403 | NOT_RUN | 实测撤 register 后只提供 PRE_REGISTERED，B17/B18 正式注册拒绝。普通 B05/B06 的失权组合仅在 109 项核对。本项保留完整原断言。 已沿原链请总控裁决。 |
| AC103 | CREATE_AND_LINK有mapping而无create，手工构造请求 | prepare及最终Apply均CAPABILITY_DENIED，无可执行Preview/新BP/Mapping/Claim/Outbox | PASS | 仅剩 mapping 时，手工复合 B17 及原预览 B18 都拒绝 403，零新 BP/关系/占用/事件。 工件 wp04-03-authorization.json。 |
| AC104 | 其他权齐备，仅所选Base可读不可管理 | ORG_SCOPE_DENIED；不能用能看到Base证明可创建 | NOT_RUN | 有权 Base 和完全隐藏 Base 均已实测。另一个只读 Base 上拒绝复合建档的明确用例未运行。 已沿原链请总控裁决。 |
| AC105 | 完整newPartner任一Core/Profile/关系创建字段无写权 | FIELD_DENIED，显式null/false也不旁路；只读Finance字段仍不可写 | NOT_RUN | 沿用实际全部创建字段授权，明确无 add/register 已验。仅撤一个必需资料字段的复合预览和应用组合未运行。 已沿原链请总控裁决。 |
| AC106 | R44准备通过后register被撤销 | R45最终同事务门403，无已注册BP或其他半成品 | PASS | REGISTERED 的复合预览已成功后撤 register，实际应用返回 403，全业务写集为零。 工件 wp04-03-authorization.json。 |
| AC107 | create撤权与CREATE_AND_LINK竞争，撤权先持共同IAM门 | 执行在真实当前权限下拒绝，零创建/关联业务效果 | NOT_RUN | 已知权限更改后新应用失败。真实 create 撤权先持共同 IAM 门的并发时序留专项对验。 承接 WP14-08。 |
| AC108 | CREATE_AND_LINK先持共同授权门commit，随后撤权 | 准确一次BP与Mapping一起留史；不追删，不授权下一次新意图 | NOT_RUN | 已核先提交后撤权仍可按原结果读取。真实共门阻断在途撤权后放行的创建先行样本未测。 承接 WP14-08。 |
| AC109 | 普通B05/B06选择REGISTERED却无register | 同AuthorizeCreateIntent拒绝，不形成另一注册旁路 | NOT_RUN | 普通建档通过同一 AuthorizeCreate，待原注册失权路径 B05 与 B06 的单独组合复核。 已沿原链请总控裁决。 |
| AC110 | 普通LINK现有BP只有mapping/当前读取权，无create | 不额外要求未发生的创建能力；其他原映射门仍全检 | PASS | 撤 add/register 保留 mapping、查候选和当前读权时，真实 LINK 成功。 工件 wp04-03-authorization.json。 |
| AC111 | 有create/Base/字段权的普通PRE_REGISTERED创建 | 合法预登记可完成，不额外要求register/mapping | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。撤除 register 后，保留 create、基地和字段权限的演员通过实际 B05 和 B06 完成预登记。 |
| AC112 | 有mapping+create/Base/字段权的CREATE_AND_LINK PRE_REGISTERED | 原子新BP+映射可完成但仍不能交易；无独立B06提交 | PASS | PRE_REGISTERED 的实际 Vue 只发 B18 建档并关联。原命令重复只回一份结果。交易资格继续受 I02 的注册条件约束。 工件 wp04-03-browser.json。 |
| AC113 | B06原提交成功后撤create/register，保留查询/披露权 | 同key/body/IfMatch返回原receipt与hash，replayed=true，零新BP | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。撤除 add/register 后，B06 原 UUID、正文和 ETag 返回完全相同的原回执和摘要。当前业务记录数保持。 |
| AC114 | B18 CREATE_AND_LINK原提交后失去写权但仍可读原结果 | 返回同BpId/MappingRef/receiptHash，零新BP/Claim/Outbox；不要求新建能力 | PASS | 真实复合命令后撤 mapping/add 并改变 BP 版本。按当前读权回同一 BpId、MappingRef、ReceiptHash 和原回执，零新增。 工件 wp04-03-mutations.json。 |
| AC115 | 快查未见→并发原请求commit→撤权→本请求进最终门 | IAM协调门→Scope→Command先重查终态，再考虑新写授权；返回历史，不先403写权 | NOT_RUN | 工作区先重查原终态再进入新写门，已测提交后撤权重放。快查未见、并发提交、撤权时序尚缺组合实证。 承接 WP14-08。 |
| AC116 | 原终态存在但当前结果查询权撤销 | 403不泄露回执，不把不可读旧Command当可新建空位 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。B13 结果存在，撤除 command-result.read 后 GET 原结果和同键 POST 均为 403，无原回执。 |
| AC117 | 仍有查询权但当前敏感披露不足 | 403不返私有hash/引用，不改原ReceiptJson/Hash制造脱敏历史 | NOT_RUN | 普通原回执保留完整原内容，当前可见性复核生效。仍有查询权但仅撤敏感披露权的原私有引用拒绝样本未运行。 承接 WP14-08。 |
| AC118 | 原Actor有查询权但同Command改body/IfMatch | 409同键异意图，无新效果，原回执不覆盖 | PASS | 复用已采用 Main #1281 或 #1283 已确认同条件。相同 UUID 更换 Preview 正文或只更换 If-Match 均冲突。原回执原样，未生成新业务效果。 |
| AC119 | 其他Actor尝试POST原Command，即使可审计查询 | 不代原Actor重放或写；不泄露无权对象，B22审计按独立范围 | NOT_RUN | 按本卡真实原命令限制原 Actor。独立审计角色按 B22 回读权限和范围的完整正向路线没有证据。 已沿原链请总控裁决。 |
| AC120 | 最终有序重查仍无原终态且write权限已撤销 | 完整新写门拒绝，零BP/Mapping/Claim/Outbox；历史分流不放宽新写 | PASS | 未有原终态时撤复合创建所需 add 权。对已存预览的全新原键仍 403，没有伙伴或映射副作用。 工件 wp04-03-authorization.json。 |
| AC121 | 当前身份失效但历史曾为原创建人 | 先401/403，无永久查询特权，不读脏/敏感结果 | NOT_RUN | 工作区既有身份派生与 current reader 复测。用户身份失效后恢复已提交 mapping 的实际 HTTP 条件未验。 承接 WP14-08。 |
| AC122 | 历史快读与真实事务并发，检查锁序 | 快读锁结束后才IAM→Scope→Command，不持Command倒取IAM/Scope；最终唯一键/CAS仍生效 | NOT_RUN | 实际 C03 与 B18 证明 Scope S/X 先后。历史快读锁和工作区命令门的精确并发场景仍需专项。 承接 WP14-08。 |
| AC123 | 原成功命令重放时旧Preview已过期或当前BP变化 | 历史分支不重检新意图期限/资格；新Command仍拒绝过期/已用Preview | PASS | 复合命令成功后实际改变原 BP 与后续关系。按旧预览和原键回原结果，没有重新执行已提交意图。 工件 wp04-03-mutations.json。 |
| AC124 | 原NO_CHANGE或REJECTED已有终态，今日条件改变 | 按查询/披露权回原终态，不当空位重新执行业务 | NOT_RUN | NO_CHANGE 事实和原键分流已验。记录过的 REJECTED 合同及后来可执行条件的原键恢复未完整覆盖。 已沿原链请总控裁决。 |

## 必需运行修订逐项核对

[必需运行修订目录](../../../design-handbook/remediation/08-acceptance-catalog.json)。

| 修订场景 | 输入 | 完整期望 | 登记 | 证据或明确余项 |
| --- | --- | --- | --- | --- |
| W1-ACC-01 | A03 response lost; account now inactive, write right removed but RQ retained | Original receipt returned safely; one Account, one command, no second quota debit. | NOT_RUN | CRM WP03-05 已实际通过 A03 回包丢失、Inactive、撤新写权及原结果；同一运行的原额度一次扣减未核销。 承接 WP14-08。 |
| W1-ACC-02 | Same command key targets another account or actor | Conflict or neutral deny under current RQ; no alias bypass. | PASS | 复用已采用证据。同组织不同主体同键修改拒绝，另一个对象同键返回 409。回查他人键与未观察相同，写入零新效果。 |
| W1-ACC-03 | BP seven-field LINKED revision14 arrives for R03 account | History/passive projection only; cannot manufacture scope decision, bound account version or Effective. | NOT_RUN | Main 七字段原件与 R03 独立。真实 CRM A13 对被动旧版 r14 不制造 Effective 仍待采用。 承接 WP03-07。 |
| W1-ACC-04 | M2a/M2b share predecessor M1; owner has not selected | WAIT_OWNER_RESOLUTION; valid M1 retained only while current proof and local eligibility valid. | NOT_RUN | R03 Main 签发端固定唯一决定并能指出已知指针异义。真实 CRM 两个同前驱候选等待 Owner 的持久语义待验。 承接 WP03-07。 |
| W1-ACC-05 | Same scope/epoch/position contains contrary signed decision | Scope CONFLICT; no new use until complete owner resolution covers both digests. | NOT_RUN | 实际 Main R03 返回同位置异 digest 的 CONFLICT。两份相反签名、完整新 Owner 解决和 CRM 恢复仍缺全链证据。 承接 WP03-07。 |
| W1-ACC-06 | A13 races deactivate or identity-relevant update | Conditional commit detects local stamp or later change invalidates use; never reactivates account. | NOT_RUN | Main I02 保持原真实交易和当前映射资格门。真实 CRM A13 与账户资料修改/停用的同槽事务竞争归 CRM。 承接 WP03-07。 |
| W1-ACC-07 | Local account current, Main changes mapping after projection | New commercial use synchronously checks Main current mapping and BP; cached eligibility is not permit. | NOT_RUN | 本卡实际 C03/I02 改关系后旧请求不能新建；端到端 CRM 已缓存资格、真实 Sales 正式确认和当前信用/技术 Owner 尚待。 承接 WP04-16。 |
| W1-ACC-08 | H02 source notice arrives without original Manifest or account target | WAITING evidence, zero Account/Lead/Contact/Opportunity creation. | NOT_RUN | 本卡涉及旧 C03/七字段/R03 三份 Main 合同。CRM H02 无真正原 Manifest 或目标时等待及完整零写由转换证明消费者验证。 承接 WP03-06。 |
| COM-OWNER-LOCK-ORDER-POS | BP writer与QTN消费同gate竞争 | 串行成功或重新准备，完整原依赖闭包持至提交 | PASS | 复用已采用证据。真实 C03 经 IAM、租户法人 BP S、EST 与产品、QTN，B12 争用同一 Scope X。锁等待由独立 PostgreSQL 会话核实，七条成功记录的行事务号归属于唯一外层 WAL COMMIT。反向提交时按原键重入后封闭，零新用途。 |
| COM-OWNER-LOCK-ORDER-NEG | EST先锁后调用BP，或import绕门 | LOCK_PLAN_UNPROVEN，零新效果 | PASS | 复用已采用证据。同一原生事务先取得 EST 锁再进入 I02，实测 LOCK_PLAN_UNPROVEN 和零新用途。独立的原生 COPY 与批量 UPDATE 未参加共同门，数据库拒绝且无新资格写入。 |
