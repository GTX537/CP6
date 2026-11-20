# WP04-02 伙伴用途参与门与旧写者收敛

WP04-02 DONE。ERP-BP-01 原 AC **37/124**，必需运行修订 **3/10**，目标保持开放。[Main PR](https://github.com/GTX537/CP6/pull/1283) · [派工 #349](https://github.com/GTX537/CP6.AI-Handoff/pull/349)

| 登记 | 内容 |
|---|---|
| 源码、派工和席位 | Main 基线 `3b6fe2b2599460d868b17d273a625b9b139a526e`，受验源码 `be2cdaeba0841b1b37049b4114368ed6140f3303`，产品树 `0845a7cc1e472cfdafdc7018830413695593e1ac`。执行人 `EXEC-WP02-14B`，Main DEV-1，`codex/wp04-02`。[派工原件](https://github.com/GTX537/CP6.AI-Handoff/blob/e378f254d975397044d48b417a78237291fa33d4/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T040511Z-exec-wp02-14b-wp04-02/MESSAGE.md) · [登记采用](https://github.com/GTX537/CP6.AI-Handoff/blob/1636b65d84a3cb84ebae5e7680ade2afb0d3b2cb/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T040511Z-exec-wp02-14b-wp04-02/MESSAGE.md) |
| 实际 START | `2026-11-20T05:16:42.674449+00:00`。WP02-10、WP02-13、WP02-13b、WP04-01、WP11-65 全部 DONE，受审候选和合并提交均在该 Main 基线。[START](https://github.com/GTX537/CP6.AI-Handoff/blob/5dc94f1e8e5833c87dcf11fa8075c00ec03e6bcd/handoffs/CP6-DEV-20261012/tasks/WP04-02/messages/20261120T051642Z-exec-wp02-14b-start/MESSAGE.md) |
| 证据 | `D:/CP6-test-artifacts/WP04-02/current-20261120`。精确采用 SHA、合并后冒烟、分支处置和席位回执见原链 DELIVERY。 |

## 实际入口与验收

- **I02 与实际消费者。** 实际 C03 收件、报价转订单和 `OrderService` 在其 PostgreSQL Serializable 外层事务中调用 I02。先持 IAM，再持租户及法人 BP Scope S，再锁 EST、已批准产品与 QTN。I02 核对原数据库、连接、事务、当前角色闭包、基地与币种、精确 Account 和映射版本。接入 WP11-65 财务源的试验核对外层事务内的原生银行引用及来源变更后的拒绝。授信仍由授信 Owner 决定。旧 `CreditLimit` 留作来源观察。采购的 Supplier、应付、付款计划及付款依赖通过实际 I02 后由外层回滚。正式采购单据仍由其业务消费者核证。[I02](../../../../CP6.Core/Services/Erp/BpUseParticipant.cs) · [C03 消费者](../../../../CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs) · [资金来源](../WP11-65/DELIVERY.md)
- **消费先提交。** C03 暂存成功写集后，B12 已请求且正在等待同一 Scope X，第三连接读到零已提交行。C03 解闸后，凭证、订单、订单明细、订单桥接、审计、原命令和成功 Outbox 共七行。行事务号均归属同一 PostgreSQL WAL 外层 COMMIT，覆盖 EF 保存点的子事务。随后 B12 成功冻结，原账保持。C03 重放和 I02 同键同意图回放保留一份写集，改用途、版本或演员的同键被拒，同业务版本和用途换键也被拒。
- **冻结先提交。** B12 持 Scope X，真实 C03 在 Scope S 等待。B12 提交后，C03 对 PostgreSQL 已知序列化中止按原键重入，读取 FROZEN。新 UseReceipt、订单、明细、桥接、业务审计、成功命令和成功 Outbox 均为零。C03 按原协议保留原键的拒绝结果及约定失败通知。C03 暂存后强制外层回滚以及后继报价校验失败也保留零成功写集。异库、反向锁序、过期或缺失映射、跨法人和冻结依赖分别留有拒绝证据。
- **原键恢复和历史。** 在实际 IAM 事务门下撤掉状态新动作权。新 UUID 被拒，保留当前读取和结果披露权时可按原 UUID 只读恢复，再撤结果读取权后拒绝。恢复与重放保持原凭证和计数。B21 依据当前法人和字段权限展示安全历史，并裁剪外部账户与受保护 Owner 引用。[伙伴原命令](../../../../CP6.Core/Services/Erp/BusinessPartnerWorkspaceV1.Commands.cs) · [历史读取](../../../../CP6.Core/Services/Erp/BusinessPartnerWorkspaceV1.Query.cs)

### 资格写者与 Scope 盘点

| 当前主线入口 | 法人与采用判断 | 实际约束与复测 |
|---|---|---|
| 旧伙伴页、`BusinessPartnerController` 和 `BusinessPartnerService` | 旧列表、详情和 CSV 继续限于旧档。按已采用伙伴的法人头或可信基地定位 | 已登记租户的旧新建、旧编辑进入同一 Scope X 后拒绝。原 B13 删除语义仅有已采用正式路径可操作，取同一 Scope X。未证明的旧 Delete 关闭。 |
| `commerce-profile` | 会变更 Account、币种及授信来源观察 | 由 `ErpCommerceAuthority` 在 IAM 后查法人，取得 Scope X 并关闭旧写。实测未改旧档。 |
| 内部 C03 伙伴同步及重试批作业 | 由当前伙伴头或可信基地定域，重试处理器复用同一消费入口 | 旧伙伴同步持 Scope X 后拒绝。新订单仅经 I02 和当前依赖，Scope S 持至业务外层事务结束。 |
| PostgreSQL 导入与批写 | 周期或离线写入直达 `T_WebBusinessPartner` 的同一根门。现有通用迁移工具以 PostgreSQL `COPY` 落表 | 在隔离库用原生 `COPY` 和批量 `UPDATE` 实测拒绝、零数据改变。无已登记正式主写者的法人保留旧档只读。 |
| 正式伙伴工作区 | 每个租户和法人明确选择 `WORKSPACE` 或 `READ_ONLY`。租户登记后，缺失或未证明的法人不开放旧写 | IAM → Scope X，数据库同时核对当前法人、主写者和根写入标记。`use_guards_ready()` 核对门实际启用。旧表已有禁止 `TRUNCATE` 的伙伴身份门。 |

旧 SQL Server 示例脚本只作存量盘点。验收使用当前 PostgreSQL 根门，与应用入口及原生 COPY、批 UPDATE 的约束相同。本卡迁移只安装约束，新写入路径由明确登记驱动，生产 PrimaryWriter 切换归 WP14。尚未采用本卡切换的租户保留先前协议，当前正式映射、ReverseClaim 和纠错依 WP04-03。[旧 CRUD](../../../../CP6.Core/Services/Erp/BusinessPartnerService.cs) · [旧商务资料](../../../../CP6.Core/Services/ErpIntegration/ErpCommerceAuthority.cs) · [C03 重试](../../../../CP6.WebApi/BackgroundServices/ErpCommandRetryWorker.cs) · [迁移工具](../../../../tools/CP6.DatabaseTransfer/Program.cs) · [数据库约束](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261120052000_BusinessPartnerUseV1.cs)

### 验收环境与证据

- 实际运行隔离 PostgreSQL 18.6 和 Main 的伙伴、财务读取、C03、订单、审计、命令及 Outbox。使用模拟用户、伙伴、单据、EST 和批准产品数据，故障由测试注入，未使用实物设备。C03 当前映射证明由测试库内同事务映射夹具提供。缺失或过期证明实测关闭。含财务引用而无法核证的消费保持关闭。凭证和业务提交来自实际数据库。此次采用为源码合并，生产环境、生产数据和生产切换未运行。
- `native-candidate.trx` 为 PostgreSQL 专项 **11/11**，记录原 UseKey、实际等待及 WAL 归属。`red-behavior.trx` 保留修改前的批写红测。`c03-related-http-wire.trx` 的 HTTP 授权、事件及运行合同 **106/106**。
- `adjacent-bp-verified.trx` 复验前卡伙伴工作区、B13 与旧 DELETE、WP11-65 的财务读取及 PostgreSQL 前向迁移，**24/24**。`existing-c03-pg.trx` 实际旧 C03 原对象证据 **1/1**。沿用 WP04-01 的 Unicode 15.0 冻结测试文件。未改动 Vue，沿用前卡已采用的界面证据。
- 专项按 `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Business_partner_wp04_02"` 运行。隔离夹具读取 `CP6_PERMISSION_PG_BIN`、`CP6_PERMISSION_PG_ROOT` 与 `CP6_PERMISSION_PG_PORT`，在自建库启用 `pg_walinspect` 读取真实提交记录。`proof.json` 记录验证输入、TRX 和构建产物摘要。台账工具用 `python -B -X utf8 -m unittest discover -s docs/development/tools -p "test_*.py"` 验证，**120/120**。

## 原验收逐项核对

| 原 AC | 原场景 | 判定 | 实际证据或剩余出口 |
|---|---|---|---|
| AC001 | BpId/L/BpCd已保存，通用PATCH试图改变其中一项 | PASS | 复用 Main #1281 的原验收。身份 ID、法人、编号三个 PATCH 均为 400 BP_SCOPE_IMMUTABLE，原身份、版本和成功命令数保持。 |
| AC002 | 同Tenant另法人已有同BpCd，再创建同号 | PASS | 复用 Main #1281 的原验收。同租户 JP-B 预览与执行重用 JP-A 编号被拒，结果为 BP_CODE_EXISTS，回执和候选均不披露 JP-A 对象。PostgreSQL 保留租户及编号唯一约束。 |
| AC003 | BP-C001已RETIRED或旧Status9，再用该号创建 | PASS | 复用 Main #1281 的原验收。B13 退休后按同编号预览和提交，历史候选仍指原 BpId 且显示 RETIRED。B06 返回 BP_CODE_EXISTS，保持一个历史身份。 |
| AC004 | 十三roles全部false或只有CreditMgmt/Maker | PASS | 复用 Main #1281 的原验收。全 false 及仅信用/制造商角色都返回 400 ROLE_SET_INVALID，没有新增成功命令。 |
| AC005 | 预登记后更改SupplierFlg或CustomerFlg | PASS | 复用 Main #1281 的原验收。SupplierFlg 与 CustomerFlg 用资料预览提交均为 400 ROLE_IMMUTABLE，Vue 只在建档显示角色编辑，原版本保持。 |
| AC006 | 合法核心身份但客户AR/员工缺失，分别PRE_REGISTERED和已填非法字段 | PASS | 复用 Main #1281 的原验收。角色资料缺失时合法 PRE_REGISTERED，详情显示缺少 CUSTOMER_AR，已填非法电话仍返回 400。 |
| AC007 | REGISTERED客户缺CUSTOMER_AR或salesStaffCd | PASS | 复用 Main #1281 的原验收。请求 REGISTERED 时缺 CUSTOMER_AR 和销售担当，预览准确列 PROFILE_INCOMPLETE，执行 422 且没有注册伙伴。 |
| AC008 | E02–E06的客户/AR/Billing/Receipt SELF | PASS | 复用 Main #1281 的原验收。实际 B06 的三个 SELF 槽指向本次 BpId，九槽完整，Root 与 ProfileRevision1 一致。 |
| AC009 | 同名候选存在但NEW_DISTINCT未列其准确CandidateRef | PASS | 复用 Main #1281 的原验收。Unicode 归一后同名候选需要准确候选及理由，NEW_DISTINCT 只创建另一个不同编号的主体。 |
| AC010 | 全域查重命中不可见BP | NOT_RUN | 相同编号的另一法人候选回读保持无对象信息。同法人中全域命中隐藏对象的边界由后续可见性辨识场景复核。 |
| AC011 | 符合规则候选超过50 | NOT_RUN | 当前页面和 API 复核了单个及多个候选，超过 50 项的实际边界留在候选覆盖验收。 |
| AC012 | 两个不同命令同Tenant同时创建同BpCd | PASS | 复用 Main #1281 的原验收。双 B06、各自 UUID、同时用同租户同号执行，200 和 409 各一，只有一条 Root、初始资料与成功回执。 |
| AC013 | B05预览后ScopeRevision改变 | PASS | 复用 Main #1281 的原验收。另事务提升 scopeRevision 后，原 Preview 执行返回 412 PREVIEW_STALE，业务记录数保持。 |
| AC014 | E05创建成功后E23冻结，再重发原Command301 | PASS | 复用 Main #1281 的原验收。创建后冻结，再按原键执行，返回完全相同的 Revision1 回执，当前仍 Revision2/FROZEN。 |
| AC015 | 同PreviewId换Command或同Command换Preview/body | PASS | 复用 Main #1281 的原验收。同 Preview 换 UUID 为 PREVIEW_ALREADY_USED，原 UUID 改 Preview 为 COMMAND_IDEMPOTENCY_CONFLICT，零重复保存。 |
| AC016 | profile bool传"false"、Dec传JSON浮点、字段超长或未知键 | PASS | 复用 Main #1281 的原验收。字符串布尔、浮点 JSON、超长电话、未知键均 400，类型与精度没有隐式转换。 |
| AC017 | PATCH银行账号/CrmAccountId/Status | PASS | 复用 Main #1281 的原验收。账户原始账号、CrmAccountId 和状态均为 400 OWNER_FIELD_READ_ONLY。 |
| AC018 | 无敏感读权Detail将Tel=null并redactedFields，用户只改名称 | PASS | 复用 Main #1281 的原验收。失去敏感权后 Tel=null 且列入遮罩，显式 null 写入 403，只修改名称后旧 Tel 保留。 |
| AC019 | RelationSlot指同Tenant不同L或目标缺所需role | PASS | 复用 Main #1281 的原验收。跨法人关系返回 RELATION_SCOPE_MISMATCH，当前目标缺 AR 角色返回 REFERENCE_INVALID。 |
| AC020 | SELF要求AR但本BP无AR角色 | PASS | 复用 Main #1281 的原验收。SELF 缺目标角色时 REFERENCE_INVALID，只有两个合法样本被持久化。 |
| AC021 | PARENT_CUSTOMER形成环或超32层 | NOT_RUN | 实际父伙伴成环被拒，32 层边界尚待实际覆盖。 |
| AC022 | BP兼Supplier+AP，旧PaymentScheduleCd只有一个值 | NOT_RUN | 正式采购角色和三个独立付款关系槽通过 I02 预验与外层回滚。旧 PaymentScheduleCd 有歧义时的完整存量场景继续待验。 |
| AC023 | SupplierPattern!=5却传subcontractTarget=true或外注专属值 | NOT_RUN | Main 预览阻断非模式 5 的外协字段，制造用途资格组合尚待相应 Owner 核对。 |
| AC024 | PaymentSchedule paidEachTime=false且closingDay缺失，另测99 | NOT_RUN | 真实 PostgreSQL 回读月末规则 99，组合用例中的付款必填拒绝仍待完整实证。 |
| AC025 | DeliveryTimeFrom>To且无跨日定义 | PASS | 复用 Main #1281 的原验收。不允许未定义的跨日时间窗，合法 HH:mm:ss 回读精确到秒。 |
| AC026 | 旧CurrencyCd=null，新用途读取或维护 | NOT_RUN | I02 要求当前且字典已证的币种。旧空币种保真和下一步变更与旧单据并排还需完整复验。 |
| AC027 | 5001行导出；名称以=开头 | NOT_RUN | 保留原目录完整断言，需按关联 Owner 与运行证据核对。 |
| AC028 | Finance/字典Owner暂未知，尝试无关名称维护与注册/使用 | NOT_RUN | I02 已核对隔离数据库中的准确资金来源及后继失效。联合名称维护、注册和历史组合沿原场景继续验。 |
| AC029 | FREEZE使BP Revision1→2 | PASS | 复用 Main #1281 的原验收。FREEZE 的资料、时间线、审计和命令 xmin 一致，根行版本和 Hold 与 ProfileRevision2 一致。故障模拟使整个事务回滚，随后同当前版本可成功执行。 |
| AC030 | FREEZE先持Scope X提交，I02随后S | PASS | 实际 B12 冻结先持 Scope X 和提交门。C03 的 Scope S 等待，按同键处理 PostgreSQL 的已知序列化中止，随后读到 FROZEN。无新增 UseReceipt、订单、业务审计、成功命令或成功 Outbox。 |
| AC031 | I02先持S完成真实业务txn，FREEZE等待 | PASS | 实际 C03 已暂存时 B12 等待同一 Scope X。UseReceipt、订单及明细、桥接登记、审计、成功命令和 Outbox 七行的 PostgreSQL 行事务号归属同一条外层 WAL COMMIT，覆盖 EF 保存点子事务。冻结随后提交，原业务保留。 |
| AC032 | 已commit UseKey再次用于新ConsumerRevision或目的 | PASS | 已冻结后同键同指纹只回原凭证。把原键用于新的 ConsumerRevision 或用途得到 USE_IDEMPOTENCY_CONFLICT。 |
| AC033 | RETIRE当前有Mapping与旧订单的BP | NOT_RUN | 本卡独立核对恢复保留 Hold 与新用途拒绝。既有正式 Mapping、订单和停用同场景的完整组合归 WP04-03。 |
| AC034 | RETIRED且FROZEN执行RESTORE | PASS | 实际停用再恢复将 Lifecycle 恢复为 ACTIVE，Hold 仍 FROZEN。再次提交 C03 新用途被拒，零新业务效果。 |
| AC035 | PRE_REGISTERED且FROZEN执行REGISTER | NOT_RUN | 保留独立注册和 Hold 轴。PRE_REGISTERED 冻结后注册成功并继续拒绝新用途的完整原用例未核销。 |
| AC036 | UNKNOWN_LEGACY旧删档普通REGISTER或RESTORE无confirm | NOT_RUN | 缺法人依据的旧 Status9 在专用入口只读；经认可的旧档法人资料、正式重新注册与历史保真由后续旧档采用核对。 |
| AC037 | 冻结/停用BP修改合法名称或交易资料 | NOT_RUN | 状态门和用途参与门独立。冻结或停用中资料维护与下游不放行的完整原场景继续待验。 |
| AC038 | Supplier-only请求SALES_NEW_SOURCE | PASS | 完整 Supplier 角色档提交 SALES_NEW_SOURCE 得 BP_ROLE_MISSING。使用采购用途时拒绝携带 Account。 |
| AC039 | E47–E52 Supplier/AP/Schedule/Payment，无Account | NOT_RUN | Supplier、应付、付款计划和付款角色在外层事务中通过采购用途资格，Account 与 Mapping 均为空。夹具回滚，实际采购单据与应付流程继续归对应消费者。 |
| AC040 | 客户根ACTIVE但其实际AR或收款依赖FROZEN | PASS | 主伙伴 ACTIVE，实际应收依赖被 B12 冻结。C03 复核当前角色闭包并拒绝新用途，订单及凭证均未写入。 |
| AC041 | I01 KNOWN_ALLOW但Sales信用或技术Owner拒绝 | NOT_RUN | 报价金额不符时 C03 回滚已暂存的 I02 凭证，零交易写入。I01 为读模型，实际授信或技术 Owner 与预览的完整组合继续待验。 |
| AC042 | 下游改为另一数据库，仅HTTP先获取BP许可 | PASS | 连接到隔离 PostgreSQL 中的另一数据库后，把其原生事务交给 I02，得到 OWNER_PARTICIPANT_UNPROVEN。没有可执行的 HTTP 预授权或本库新凭证。 |
| AC043 | 同UseKey内容/actorOwner不同 | PASS | 同一原 UseKey 修改已核消费意图、Purpose 或执行 Actor 得 USE_IDEMPOTENCY_CONFLICT，已存原凭证字节保持。 |
| AC044 | 同真实ConsumerOwner/id/version/purpose换UseKey | PASS | 换 UseKey 提交同一 ConsumerOwner、BusinessId、Revision 和 Purpose 直接冲突，数据库仍只有一份原凭证。 |
| AC045 | 旧BP禁用后读原UseReceipt/订单引用 | NOT_RUN | 冻结后的同原键内部回读保留不可变原 UseReceipt，B21 当前可读历史提供安全用途和原伙伴版本。含 CRM 源和受保护依赖的完整披露继续按各 Owner 核证。 |
| AC046 | B15 Account未找到/无权/Source未证 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC047 | 真实Account INACTIVE，Main核实身份关系 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC048 | E09–E15首次LINK | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC049 | 两个Account并发LINK同法人同BP | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC050 | REPLACE取得新Claim前发现占用/事务异常 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC051 | 当前LINKED再次LINK另一BP | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC052 | UNSEEN槽请求UNLINK | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC053 | CONFIRM同当前LINKED或UNLINKED | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC054 | B25 SourceRef存在但原凭证用途无权或范围不符 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC055 | REPLACE的reason/目标/旧头与B25 Subject不同 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC056 | Mapping预览后目标BP或Scope发生变化 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC057 | E15/E38发往ACC | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC058 | E38版本2先应用，E15旧版本1晚到 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC059 | Outbox租约过期，旧worker晚回ACK | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC060 | ACC200处理了较旧或重复消息 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC061 | ProjectionReader真实当前五元组等MainHead | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC062 | 原绿色之后新的查询失败/可信冲突通知 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC063 | 发起查rev1期间Main变rev2，rev1结果晚到 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC064 | CRM回rev高于Main或同版异义无法解释 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC065 | 旧C03 ResultVersion未含L/映射权威证据 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC066 | I03读不到Main权威槽 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC067 | E39 current原事件base64解码 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC068 | B20查询job已QUEUED，Owner读取暂失败 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC069 | E30–E38有据从BP1改至BP2 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC070 | BP映射/状态保存或重试成功 | NOT_RUN | 本卡实际状态与资料成功提交没有发出账户映射动作；跨 Owner 的全库零副作用由 WP04-03 实测。 |
| AC071 | 缺强ETag/ExpectedRev或IfMatch=* | PASS | 复用 Main #1281 的原验收。B13 缺强 If-Match 返回 428，通配符为 400，旧版本为 412，业务行和原结果保持。 |
| AC072 | FK把另Tenant同UUID关联到Profile/Mapping/Relation | NOT_RUN | 保留原条件，随相关共同权限和事务的组合验收核对。 |
| AC073 | 撤Main组织/动作/字段权与写命令竞争 | NOT_RUN | 实际 IAM 门下，撤状态新动作权后新命令拒绝，原命令可按当前结果查看权回读。组织和字段撤权竞争全组合继续待验。 |
| AC074 | 真实Owner未采用、来源代次被撤销、fixture proof混生产 | NOT_RUN | 精确财务引用实测成功和离线拒绝；生产 Owner 启用证明与来源代次撤销需在相应接入验收核对。 |
| AC075 | 历史/错误/CSV读取敏感字段时失权 | NOT_RUN | 详情遮罩、原回执和候选不披露私有值已覆盖，历史及 CSV 的撤权组合仍需实测。 |
| AC076 | 模拟与生产使用相同UUID/BpCd/Scope数字 | NOT_RUN | 使用真实 Kestrel 和隔离 PostgreSQL，测试账号和 Owner 原件均标明模拟。生产与演示同号双域并行还需相关发布验收。 |
| AC077 | 实际commit响应丢失后B22 NOT_OBSERVED | NOT_RUN | 代理在实际退休提交后丢失回包，刷新按同 UUID 返回 FOUND；暂未观测原回执的分支还需完整核对。 |
| AC078 | Preview/Outbox/ReceivedTrust原文被截断或hash不符 | NOT_RUN | 保护并复核原资料/回执，Outbox 或 ReceivedTrust 的损坏恢复路径归 WP04-03。 |
| AC079 | 一个普通Profile更新与同BP状态动作同ExpectedRev | NOT_RUN | 实际 B13 对旧版本拒绝，普通资料写和状态写同时到达的组合尚待核对。 |
| AC080 | 旧commerce-profile/C03/批导入未参加Scope共同门 | PASS | 切换租户内只按法人登记启用正式写者。旧 CRUD、commerce-profile 和内部 C03 资格修改被本地共同门拒绝。原生 COPY 与批量 UPDATE 均由数据库拒绝，未证明法人只读。 |
| AC081 | Finance信用额度为null或旧0，BP有CreditMgmt角色 | NOT_RUN | 资金引用核对存量准确版本，旧 CreditLimit 留在来源观察范围。完整销售与授信 Owner 决策组合未核销。 |
| AC082 | 仅文书JSON/摘要检查通过 | NOT_RUN | 本报告只使用已执行测试和明确限域复用核销，原案例仍由汇总验收判定。 |
| AC083 | B16读完整criteria后服务重启，再B17/B18 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC084 | 同Actor两窗口不同criteria，混ContextRef与另一个setHash | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC085 | Main一直r12，Q1成功迟到、后发Q2先失败/冲突 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC086 | 适用可信冲突通知在查询未完成时到达 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC087 | 查询job租约过期后旧worker回包/服务重启 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC088 | OwnerConflict=true，随后普通查询声称匹配 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC089 | r12正确LINKED BP2，已证同版错误，CORRECT_CURRENT | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC090 | 纠错混入其他Scope/Revision、缺原body/Trust或CRM高于Main | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC091 | 正确更正13后真实UNLINK14，原12/13晚到 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC092 | FieldPolicy隐藏paidEachTimeFlg | NOT_RUN | 实际验证 Tel 字段遮罩，经字段策略隐藏非空布尔的完整案例需核对。 |
| AC093 | 将R01遮罩null作为非nullable写bool提交 | NOT_RUN | 无权显式写 Tel=null 为 403；该修订指定的 nonnullable bool 双权限组合需补验。 |
| AC094 | H04真实零候选CREATE_AND_LINK | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC095 | CREATE_AND_LINK新BP已暂存、Outbox前异常 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC096 | 零候选预览后另一命令占Account槽或改变Scope | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC097 | B20从B19真实Revision提交查询job | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC098 | E06→E22与每个Preview→Apply连续链 | NOT_RUN | 浏览器实际复制 B05/B08 和 B10 响应的 ETag，涉及映射 Preview/Apply 的端到端组合归 WP04-03。 |
| AC099 | 100个U+FDFA合法输入、规范展开1800单元 | PASS | 复用 Main #1281 的原验收。100 个 U+FDFA 经 HTTP 保存，原显示 100 单元，数据库完整保存 1800 单元，准确同名经索引后匹配。 |
| AC100 | 归一版本不一致/旧索引覆盖未证，或强制hash索引碰撞夹具 | PASS | 复用 Main #1281 的原验收。模拟归一版本不一致时 REVIEW_REQUIRED，强制相同索引摘要夹具仍按完整规范字符串比较。 |
| AC101 | B01当前Actor只有mapping无create | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC102 | 有create/Base/字段权，无register | NOT_RUN | 角色撤除 register 后普通 B01/B05 只支持预登记；B06 和映射 B17/B18 的剩余组合仍待运行。 |
| AC103 | CREATE_AND_LINK有mapping而无create，手工构造请求 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC104 | 其他权齐备，仅所选Base可读不可管理 | NOT_RUN | 已实测 B13 对可读不可管理基地拒绝；普通/映射创建的专用组合仍待验证。 |
| AC105 | 完整newPartner任一Core/Profile/关系创建字段无写权 | NOT_RUN | 保留原目录完整断言，需按关联 Owner 与运行证据核对。 |
| AC106 | R44准备通过后register被撤销 | NOT_RUN | B13 当前动作撤销拒绝；R44→R45 的注册撤权组合保留给共同许可边界。 |
| AC107 | create撤权与CREATE_AND_LINK竞争，撤权先持共同IAM门 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC108 | CREATE_AND_LINK先持共同授权门commit，随后撤权 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC109 | 普通B05/B06选择REGISTERED却无register | NOT_RUN | 普通 B05 REGISTERED 无 register 返回 403；B06 撤权后应用同一准入规则的专项用例仍需运行。 |
| AC110 | 普通LINK现有BP只有mapping/当前读取权，无create | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC111 | 有create/Base/字段权的普通PRE_REGISTERED创建 | PASS | 复用 Main #1281 的原验收。撤除 register 后，保留 create、基地和字段权限的演员通过实际 B05 和 B06 完成预登记。 |
| AC112 | 有mapping+create/Base/字段权的CREATE_AND_LINK PRE_REGISTERED | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC113 | B06原提交成功后撤create/register，保留查询/披露权 | PASS | 复用 Main #1281 的原验收。撤除 add/register 后，B06 原 UUID、正文和 ETag 返回完全相同的原回执和摘要。当前业务记录数保持。 |
| AC114 | B18 CREATE_AND_LINK原提交后失去写权但仍可读原结果 | NOT_RUN | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC115 | 快查未见→并发原请求commit→撤权→本请求进最终门 | NOT_RUN | 同一 IAM、Scope、Command 门重查历史，缺终态才授权新写。原始快查、提交及撤权三方组合留待完整竞态复验。 |
| AC116 | 原终态存在但当前结果查询权撤销 | PASS | 复用 Main #1281 的原验收。B13 结果存在，撤除 command-result.read 后 GET 原结果和同键 POST 均为 403，无原回执。 |
| AC117 | 仍有查询权但当前敏感披露不足 | NOT_RUN | B21 对 CRM 来源和外域依赖保留安全引用，原凭证不变。B22 含敏感原内容的独立披露用例留待完整复验。 |
| AC118 | 原Actor有查询权但同Command改body/IfMatch | PASS | 复用 Main #1281 的原验收。相同 UUID 更换 Preview 正文或只更换 If-Match 均冲突。原回执原样，未生成新业务效果。 |
| AC119 | 其他Actor尝试POST原Command，即使可审计查询 | NOT_RUN | 旧 Actor 命令原账独立。其他 Actor 与审计查看权的完整组合留待复验。 |
| AC120 | 最终有序重查仍无原终态且write权限已撤销 | NOT_RUN | 当现有新状态动作权被撤，使用新 UUID 拒绝，事务计数不变。Mapping、Claim、Outbox 的原联合分支继续归 WP04-03。 |
| AC121 | 当前身份失效但历史曾为原创建人 | NOT_RUN | 保留原条件，随相关共同权限和事务的组合验收核对。 |
| AC122 | 历史快读与真实事务并发，检查锁序 | NOT_RUN | 见实际 IAM→BP→EST→QTN、原命令 Scope 后重查及反向锁序负例。读取和写入竞态的其余原断言继续待验。 |
| AC123 | 原成功命令重放时旧Preview已过期或当前BP变化 | NOT_RUN | 当冻结后重放旧 UseKey，保持原凭证和当次伙伴版本。原预览单独过期及原命令组合继续待验。 |
| AC124 | 原NO_CHANGE或REJECTED已有终态，今日条件改变 | NOT_RUN | USE_EXISTING 返回原 NO_CHANGE，未执行已记录 REJECTED 和条件变化组合。 |

原场景的未覆盖出口延续前卡的逐项安排，WP04-03 负责当前 Mapping、ReverseClaim、纠错及其组合闭环。[原目录](../../assessments/20261011/catalogs/commercial-finance.json) · [前卡唯一报告](../WP04-01/DELIVERY.md)

## 必需运行修订

| 原 ID | 判定 | 实际证据或余项 |
|---|---|---|
| W1-ACC-01 | NOT_RUN | 映射与 CRM 相应消费者完整组合归后继卡复核。 |
| W1-ACC-02 | PASS | 复用 CRM #242 实际采用，原台账证据保留。 |
| W1-ACC-03 | NOT_RUN | 映射与 CRM 相应消费者完整组合归后继卡复核。 |
| W1-ACC-04 | NOT_RUN | 映射与 CRM 相应消费者完整组合归后继卡复核。 |
| W1-ACC-05 | NOT_RUN | 映射与 CRM 相应消费者完整组合归后继卡复核。 |
| W1-ACC-06 | NOT_RUN | 映射与 CRM 相应消费者完整组合归后继卡复核。 |
| W1-ACC-07 | NOT_RUN | 实际 C03 对声明映射缺失或过期保持封闭。当前已验证路径的 Mapping 来自同事务夹具，真实 Main Mapping、ReverseClaim 与跨仓采用继续由 WP04-03 核证。 |
| W1-ACC-08 | NOT_RUN | 映射与 CRM 相应消费者完整组合归后继卡复核。 |
| COM-OWNER-LOCK-ORDER-POS | PASS | 真实 C03 经 IAM、租户法人 BP S、EST 与产品、QTN，B12 争用同一 Scope X。锁等待由独立 PostgreSQL 会话核实，七条成功记录的行事务号归属于唯一外层 WAL COMMIT。反向提交时按原键重入后封闭，零新用途。 |
| COM-OWNER-LOCK-ORDER-NEG | PASS | 同一原生事务先取得 EST 锁再进入 I02，实测 LOCK_PLAN_UNPROVEN 和零新用途。独立的原生 COPY 与批量 UPDATE 未参加共同门，数据库拒绝且无新资格写入。 |

## 工时与收尾

开工到核销快照 `2026-11-20T05:46:31.133860+00:00` 卡内有效 **1788.357 秒**，目标核对另计 **0.102 秒**。按 #349 单次补记 WP03-08 **158.562 秒**，该卡合计 **1612.951 秒**，结转后计划起点 **93180.418 秒**，本次累计 **94968.877 秒**。正式集成收尾按实际 finish 留证。本卡无拆分，24 小时检查点已登记。[总控签收](https://github.com/GTX537/CP6.AI-Handoff/blob/008311826fa124fec12fe9c6b8a1aaa1e0601588/handoffs/CP6-DEV-20261012/tasks/WP03-08/replies/20261120T040241Z-coord-acceptance-ack/MESSAGE.md) · [唯一台账](../../roadmap.json)

阶段 C 可开工顺序为 WP03-10（39）、WP03-09（39）、WP02-22（39）、WP04-03（7）、WP03-12（6），实际领取依独立派工。WP02-29 继续按原裁决暂缓。
