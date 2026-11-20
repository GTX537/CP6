# WP04-01 交易伙伴不可变资料和注册控制

Main 卡 DONE，ERP-BP-01 保持开放。完整实测通过 **27/124** 项原 AC，余 **97** 项按下表保留。10 项必需修订中，保留 CRM 已实际采用的 **1/10**。 [Main PR](https://github.com/GTX537/CP6/pull/1281) · [派工 #345](https://github.com/GTX537/CP6.AI-Handoff/pull/345)

| 登记 | 内容 |
|---|---|
| 执行人和席位 | `EXEC-WP02-14B` · Main `DEV-1` · `codex/wp04-01` · `D:/CP6-worktrees/wave17-dispatch-20261018` |
| 开工和源码 | 开工 Main `ed2f4d717028895a1b0c1327ae281ed4ab3cad1e` · 验证源码 `e72c1fb9250f4328fc5a7ea4781b7003009e5f5f` |
| 派工原件 | [ce7a37f67e210867960cec14ddc6d7eb3f693e8e](https://github.com/GTX537/CP6.AI-Handoff/blob/ce7a37f67e210867960cec14ddc6d7eb3f693e8e/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T020319Z-exec-wp02-14b-wp04-01/MESSAGE.md) · [登记分支采用 9ee6e3b770e3d90ebc7583e6270246be82c08ed4](https://github.com/GTX537/CP6.AI-Handoff/blob/9ee6e3b770e3d90ebc7583e6270246be82c08ed4/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T020319Z-exec-wp02-14b-wp04-01/MESSAGE.md) |
| START | [85b8abcceaf18013ed7841f2830c83c8b0539f66](https://github.com/GTX537/CP6.AI-Handoff/blob/85b8abcceaf18013ed7841f2830c83c8b0539f66/handoffs/CP6-DEV-20261012/tasks/WP04-01/messages/20261120T021951Z-exec-wp02-14b-start/MESSAGE.md) |
| 正式收尾 | 以本 PR 的远端采用、合并后冒烟、`manage.ps1 finish` 和原通信分支 DELIVERY 为准。 |

## 实际实现与取证

- Main 原伙伴根表保留身份与租户内业务编号。在明确登记的法人和基地内，B05/B06 以原预览、当前权限、字典、完整候选及 CAS 执行。`NEW_DISTINCT` 生成一条新伙伴，`USE_EXISTING` 只生成 NO_CHANGE 命令和审计。ProfileRevision 保存 128 个白名单字段、九槽、准确 Owner 引用和状态，原版本不可变。143 原字段及 128 写成员对应原 R3 字段表的确切名称和类型。
- Main B11、B12、B13、B14 分别执行 REGISTER、FREEZE/UNFREEZE、RETIRE、RESTORE。真实 PostgreSQL 验证 Profile、状态、审计和原命令同事务。财务来源锁定时冻结、退休仍可提交，注册在必须引用未证时拒绝。旧 DELETE #1277 在未采用旧档仍用现有管理员和动作权限关系，已采用伙伴追加同一不可变历史。新工作区对缺法人依据的旧档只读。原粗粒度详情、搜索和 CSV 保留旧档读取，新工作区记录使用当前法人与逐字段权限的正式读取入口。
- Vue 管理页运行实际建档、资料维护、冻结、退休和恢复。测试代理在实际 Main 退休提交后丢失成功回包。浏览器传输重试保持原 UUID，刷新后的恢复只查询原结果。数据库只增加一次退休业务结果，恢复 ACTIVE 仍保留冻结。测试以实际生产构建和 Chromium 运行。
- 交易参与 I02、全量旧写者切换、CRM 主映射与 ReverseClaim、纠错和投影由 WP04-02、WP04-03 后续交付。生产数据归属登记和 PrimaryWriter 切换走 WP14。状态、资料和既有财务原件回读的实证只适用于报告的隔离条件。新建尚未取得绑定 BP 的财务原件时按未证处理，采用的既有财务链接须由 WP11-65 提供方证实。

Windows，实际 Main Kestrel、Vue 生产构建、Chromium、隔离 PostgreSQL 18。模拟用户、设备、法人/基地、业务数据、CRM 离线、财务 Owner 发布和存储错误。 CRM 集成在测试进程明确关闭。法人与基地、用户和角色、模拟 FIN-MASTER 原件精确版本、当前字段权限、表锁故障、断连及归一索引碰撞夹具，均只在本卡隔离库和本机进程中运行。Main API、Vue、数据库触发器、权限门、账套及 WP11-65 原引用读取均用产品实现。

| 本地验证 | 实际结果 |
|---|---|
| 基线先红 | `red.trx` 通过旧用户正式登录访问新入口为 404；基础绿 `first-green.trx` 为 1/1。 |
| HTTP/PG/浏览器与回归 | `workspace-final.trx（后端）及 workspace-browser-verified.trx（实际 Vue）、workspace-migration-release.trx（前向迁移）`，37/37 通过，0 失败，0 跳过。含本卡、旧 DELETE #1277、WP11-65 原件及原身份读取回归。 |
| Unicode | 原 R3 固定 143/128 名称摘要、固定 Unicode 15.0.0 的官方 19,074 行兼容合成，PG 长规范串、索引全串比较。[Unicode 15.0.0 数据](https://www.unicode.org/versions/Unicode15.0.0/) |
| Vue 构建与单元 | 生产构建和 vue-tsc --build 通过。全量 Vitest 3545 通过、3 项失败、2 项运行错误。用相同依赖在开工基线复现同样失败的测试和运行错误。 |
| 证据文件 | `D:/CP6-test-artifacts/WP04-01/current-20261120`。实际回读记录 `wp04-01-*.json`、浏览器流程 PNG、测试 TRX、构建日志和具体命令在该目录。 |

浏览器和前向迁移保留了原运行记录，最终结论采用更新后输入的两项专项复测。其余 35 项使用相同的 Main 产品源码和隔离条件。前向迁移确认只登记动作定义，旧记录保留原身份和版本，范围与角色均无自动授权。

## ERP-BP-01 原 AC 逐项核对

逐项按 [原 AC 目录](../../assessments/20261011/catalogs/commercial-finance.json) 的 R3 原身份核对。待验包括已证一部分和本卡外待完整运行的场景。精确证据口径见相应行。

| 原 AC | 原场景 | 结果 | 验收和剩余出口 |
|---|---|---|---|
| AC001 | BpId/L/BpCd已保存，通用PATCH试图改变其中一项 | PASS | 身份 ID、法人、编号三个 PATCH 均为 400 BP_SCOPE_IMMUTABLE，原身份、版本和成功命令数保持。 |
| AC002 | 同Tenant另法人已有同BpCd，再创建同号 | PASS | 同租户 JP-B 预览与执行重用 JP-A 编号被拒，结果为 BP_CODE_EXISTS，回执和候选均不披露 JP-A 对象。PostgreSQL 保留租户及编号唯一约束。 |
| AC003 | BP-C001已RETIRED或旧Status9，再用该号创建 | PASS | B13 退休后按同编号预览和提交，历史候选仍指原 BpId 且显示 RETIRED。B06 返回 BP_CODE_EXISTS，保持一个历史身份。 |
| AC004 | 十三roles全部false或只有CreditMgmt/Maker | PASS | 全 false 及仅信用/制造商角色都返回 400 ROLE_SET_INVALID，没有新增成功命令。 |
| AC005 | 预登记后更改SupplierFlg或CustomerFlg | PASS | SupplierFlg 与 CustomerFlg 用资料预览提交均为 400 ROLE_IMMUTABLE，Vue 只在建档显示角色编辑，原版本保持。 |
| AC006 | 合法核心身份但客户AR/员工缺失，分别PRE_REGISTERED和已填非法字段 | PASS | 角色资料缺失时合法 PRE_REGISTERED，详情显示缺少 CUSTOMER_AR，已填非法电话仍返回 400。 |
| AC007 | REGISTERED客户缺CUSTOMER_AR或salesStaffCd | PASS | 请求 REGISTERED 时缺 CUSTOMER_AR 和销售担当，预览准确列 PROFILE_INCOMPLETE，执行 422 且没有注册伙伴。 |
| AC008 | E02–E06的客户/AR/Billing/Receipt SELF | PASS | 实际 B06 的三个 SELF 槽指向本次 BpId，九槽完整，Root 与 ProfileRevision1 一致。 |
| AC009 | 同名候选存在但NEW_DISTINCT未列其准确CandidateRef | PASS | Unicode 归一后同名候选需要准确候选及理由，NEW_DISTINCT 只创建另一个不同编号的主体。 |
| AC010 | 全域查重命中不可见BP | 待验 | 相同编号的另一法人候选回读保持无对象信息。同法人中全域命中隐藏对象的边界由后续可见性辨识场景复核。 |
| AC011 | 符合规则候选超过50 | 待验 | 当前页面和 API 复核了单个及多个候选，超过 50 项的实际边界留在候选覆盖验收。 |
| AC012 | 两个不同命令同Tenant同时创建同BpCd | PASS | 双 B06、各自 UUID、同时用同租户同号执行，200 和 409 各一，只有一条 Root、初始资料与成功回执。 |
| AC013 | B05预览后ScopeRevision改变 | PASS | 另事务提升 scopeRevision 后，原 Preview 执行返回 412 PREVIEW_STALE，业务记录数保持。 |
| AC014 | E05创建成功后E23冻结，再重发原Command301 | PASS | 创建后冻结，再按原键执行，返回完全相同的 Revision1 回执，当前仍 Revision2/FROZEN。 |
| AC015 | 同PreviewId换Command或同Command换Preview/body | PASS | 同 Preview 换 UUID 为 PREVIEW_ALREADY_USED，原 UUID 改 Preview 为 COMMAND_IDEMPOTENCY_CONFLICT，零重复保存。 |
| AC016 | profile bool传"false"、Dec传JSON浮点、字段超长或未知键 | PASS | 字符串布尔、浮点 JSON、超长电话、未知键均 400，类型与精度没有隐式转换。 |
| AC017 | PATCH银行账号/CrmAccountId/Status | PASS | 账户原始账号、CrmAccountId 和状态均为 400 OWNER_FIELD_READ_ONLY。 |
| AC018 | 无敏感读权Detail将Tel=null并redactedFields，用户只改名称 | PASS | 失去敏感权后 Tel=null 且列入遮罩，显式 null 写入 403，只修改名称后旧 Tel 保留。 |
| AC019 | RelationSlot指同Tenant不同L或目标缺所需role | PASS | 跨法人关系返回 RELATION_SCOPE_MISMATCH，当前目标缺 AR 角色返回 REFERENCE_INVALID。 |
| AC020 | SELF要求AR但本BP无AR角色 | PASS | SELF 缺目标角色时 REFERENCE_INVALID，只有两个合法样本被持久化。 |
| AC021 | PARENT_CUSTOMER形成环或超32层 | 待验 | 实际父伙伴成环被拒，32 层边界尚待实际覆盖。 |
| AC022 | BP兼Supplier+AP，旧PaymentScheduleCd只有一个值 | 待验 | 九槽已分别运行，旧重载值无独立历史来源时的迁移和交易准入在 WP04-02 的旧写者及用途案例核对。 |
| AC023 | SupplierPattern!=5却传subcontractTarget=true或外注专属值 | 待验 | Main 预览阻断非模式 5 的外协字段，制造用途资格组合尚待相应 Owner 核对。 |
| AC024 | PaymentSchedule paidEachTime=false且closingDay缺失，另测99 | 待验 | 真实 PostgreSQL 回读月末规则 99，组合用例中的付款必填拒绝仍待完整实证。 |
| AC025 | DeliveryTimeFrom>To且无跨日定义 | PASS | 不允许未定义的跨日时间窗，合法 HH:mm:ss 回读精确到秒。 |
| AC026 | 旧CurrencyCd=null，新用途读取或维护 | 待验 | 旧资料读路径保留原值及未证状态，当前用途读与旧订单币种需走真实消费。 |
| AC027 | 5001行导出；名称以=开头 | 待验 | 保留原目录完整断言，需按关联 Owner 与运行证据核对。 |
| AC028 | Finance/字典Owner暂未知，尝试无关名称维护与注册/使用 | 待验 | 字典与财务故障时已复核注册拒绝、无关修改及历史读取；新用途仍由 WP04-02 复核。 |
| AC029 | FREEZE使BP Revision1→2 | PASS | FREEZE 的资料、时间线、审计和命令 xmin 一致，根行版本和 Hold 与 ProfileRevision2 一致。故障模拟使整个事务回滚，随后同当前版本可成功执行。 |
| AC030 | FREEZE先持Scope X提交，I02随后S | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC031 | I02先持S完成真实业务txn，FREEZE等待 | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC032 | 已commit UseKey再次用于新ConsumerRevision或目的 | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC033 | RETIRE当前有Mapping与旧订单的BP | 待验 | 实际 B13 已保存退休、原 Profile 和 Hold；已有 Mapping、旧订单与其后的新用途组合转 WP04-02/WP04-03。 |
| AC034 | RETIRED且FROZEN执行RESTORE | 待验 | 从 B14 恢复 ACTIVE 并保留 FROZEN；同时验证新用途拒绝由 WP04-02。 |
| AC035 | PRE_REGISTERED且FROZEN执行REGISTER | 待验 | REGISTERED 与 FROZEN 并存，实际新交易采用门由 WP04-02。 |
| AC036 | UNKNOWN_LEGACY旧删档普通REGISTER或RESTORE无confirm | 待验 | 缺法人依据的旧 Status9 在专用入口只读；经认可的旧档法人资料、正式重新注册与历史保真由后续旧档采用核对。 |
| AC037 | 冻结/停用BP修改合法名称或交易资料 | 待验 | 预览及维护可在 FROZEN/RETIRED 时按现行授权只改资料，实际商业用途仍需 WP04-02 组合确认。 |
| AC038 | Supplier-only请求SALES_NEW_SOURCE | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC039 | E47–E52 Supplier/AP/Schedule/Payment，无Account | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC040 | 客户根ACTIVE但其实际AR或收款依赖FROZEN | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC041 | I01 KNOWN_ALLOW但Sales信用或技术Owner拒绝 | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC042 | 下游改为另一数据库，仅HTTP先获取BP许可 | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC043 | 同UseKey内容/actorOwner不同 | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC044 | 同真实ConsumerOwner/id/version/purpose换UseKey | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC045 | 旧BP禁用后读原UseReceipt/订单引用 | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC046 | B15 Account未找到/无权/Source未证 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC047 | 真实Account INACTIVE，Main核实身份关系 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC048 | E09–E15首次LINK | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC049 | 两个Account并发LINK同法人同BP | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC050 | REPLACE取得新Claim前发现占用/事务异常 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC051 | 当前LINKED再次LINK另一BP | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC052 | UNSEEN槽请求UNLINK | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC053 | CONFIRM同当前LINKED或UNLINKED | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC054 | B25 SourceRef存在但原凭证用途无权或范围不符 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC055 | REPLACE的reason/目标/旧头与B25 Subject不同 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC056 | Mapping预览后目标BP或Scope发生变化 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC057 | E15/E38发往ACC | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC058 | E38版本2先应用，E15旧版本1晚到 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC059 | Outbox租约过期，旧worker晚回ACK | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC060 | ACC200处理了较旧或重复消息 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC061 | ProjectionReader真实当前五元组等MainHead | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC062 | 原绿色之后新的查询失败/可信冲突通知 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC063 | 发起查rev1期间Main变rev2，rev1结果晚到 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC064 | CRM回rev高于Main或同版异义无法解释 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC065 | 旧C03 ResultVersion未含L/映射权威证据 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC066 | I03读不到Main权威槽 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC067 | E39 current原事件base64解码 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC068 | B20查询job已QUEUED，Owner读取暂失败 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC069 | E30–E38有据从BP1改至BP2 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC070 | BP映射/状态保存或重试成功 | 待验 | 本卡实际状态与资料成功提交没有发出账户映射动作；跨 Owner 的全库零副作用由 WP04-03 实测。 |
| AC071 | 缺强ETag/ExpectedRev或IfMatch=* | PASS | B13 缺强 If-Match 返回 428，通配符为 400，旧版本为 412，业务行和原结果保持。 |
| AC072 | FK把另Tenant同UUID关联到Profile/Mapping/Relation | 待验 | 保留原条件，随相关共同权限和事务的组合验收核对。 |
| AC073 | 撤Main组织/动作/字段权与写命令竞争 | 待验 | 已验证写入对当前权限及撤权有效；指定权限与业务写竞争的完整锁顺序由 WP04-02。 |
| AC074 | 真实Owner未采用、来源代次被撤销、fixture proof混生产 | 待验 | 精确财务引用实测成功和离线拒绝；生产 Owner 启用证明与来源代次撤销需在相应接入验收核对。 |
| AC075 | 历史/错误/CSV读取敏感字段时失权 | 待验 | 详情遮罩、原回执和候选不披露私有值已覆盖，历史及 CSV 的撤权组合仍需实测。 |
| AC076 | 模拟与生产使用相同UUID/BpCd/Scope数字 | 待验 | 使用真实 Kestrel 和隔离 PostgreSQL，测试账号和 Owner 原件均标明模拟。生产与演示同号双域并行还需相关发布验收。 |
| AC077 | 实际commit响应丢失后B22 NOT_OBSERVED | 待验 | 代理在实际退休提交后丢失回包，刷新按同 UUID 返回 FOUND；暂未观测原回执的分支还需完整核对。 |
| AC078 | Preview/Outbox/ReceivedTrust原文被截断或hash不符 | 待验 | 保护并复核原资料/回执，Outbox 或 ReceivedTrust 的损坏恢复路径归 WP04-03。 |
| AC079 | 一个普通Profile更新与同BP状态动作同ExpectedRev | 待验 | 实际 B13 对旧版本拒绝，普通资料写和状态写同时到达的组合尚待核对。 |
| AC080 | 旧commerce-profile/C03/批导入未参加Scope共同门 | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC081 | Finance信用额度为null或旧0，BP有CreditMgmt角色 | 待验 | WP04-02 的真实用途参与、旧写者或下游消费。 |
| AC082 | 仅文书JSON/摘要检查通过 | 待验 | 本报告只使用已执行测试和明确限域复用核销，原案例仍由汇总验收判定。 |
| AC083 | B16读完整criteria后服务重启，再B17/B18 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC084 | 同Actor两窗口不同criteria，混ContextRef与另一个setHash | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC085 | Main一直r12，Q1成功迟到、后发Q2先失败/冲突 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC086 | 适用可信冲突通知在查询未完成时到达 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC087 | 查询job租约过期后旧worker回包/服务重启 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC088 | OwnerConflict=true，随后普通查询声称匹配 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC089 | r12正确LINKED BP2，已证同版错误，CORRECT_CURRENT | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC090 | 纠错混入其他Scope/Revision、缺原body/Trust或CRM高于Main | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC091 | 正确更正13后真实UNLINK14，原12/13晚到 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC092 | FieldPolicy隐藏paidEachTimeFlg | 待验 | 实际验证 Tel 字段遮罩，经字段策略隐藏非空布尔的完整案例需核对。 |
| AC093 | 将R01遮罩null作为非nullable写bool提交 | 待验 | 无权显式写 Tel=null 为 403；该修订指定的 nonnullable bool 双权限组合需补验。 |
| AC094 | H04真实零候选CREATE_AND_LINK | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC095 | CREATE_AND_LINK新BP已暂存、Outbox前异常 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC096 | 零候选预览后另一命令占Account槽或改变Scope | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC097 | B20从B19真实Revision提交查询job | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC098 | E06→E22与每个Preview→Apply连续链 | 待验 | 浏览器实际复制 B05/B08 和 B10 响应的 ETag，涉及映射 Preview/Apply 的端到端组合归 WP04-03。 |
| AC099 | 100个U+FDFA合法输入、规范展开1800单元 | PASS | 100 个 U+FDFA 经 HTTP 保存，原显示 100 单元，数据库完整保存 1800 单元，准确同名经索引后匹配。 |
| AC100 | 归一版本不一致/旧索引覆盖未证，或强制hash索引碰撞夹具 | PASS | 模拟归一版本不一致时 REVIEW_REQUIRED，强制相同索引摘要夹具仍按完整规范字符串比较。 |
| AC101 | B01当前Actor只有mapping无create | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC102 | 有create/Base/字段权，无register | 待验 | 角色撤除 register 后普通 B01/B05 只支持预登记；B06 和映射 B17/B18 的剩余组合仍待运行。 |
| AC103 | CREATE_AND_LINK有mapping而无create，手工构造请求 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC104 | 其他权齐备，仅所选Base可读不可管理 | 待验 | 已实测 B13 对可读不可管理基地拒绝；普通/映射创建的专用组合仍待验证。 |
| AC105 | 完整newPartner任一Core/Profile/关系创建字段无写权 | 待验 | 保留原目录完整断言，需按关联 Owner 与运行证据核对。 |
| AC106 | R44准备通过后register被撤销 | 待验 | B13 当前动作撤销拒绝；R44→R45 的注册撤权组合保留给共同许可边界。 |
| AC107 | create撤权与CREATE_AND_LINK竞争，撤权先持共同IAM门 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC108 | CREATE_AND_LINK先持共同授权门commit，随后撤权 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC109 | 普通B05/B06选择REGISTERED却无register | 待验 | 普通 B05 REGISTERED 无 register 返回 403；B06 撤权后应用同一准入规则的专项用例仍需运行。 |
| AC110 | 普通LINK现有BP只有mapping/当前读取权，无create | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC111 | 有create/Base/字段权的普通PRE_REGISTERED创建 | PASS | 撤除 register 后，保留 create、基地和字段权限的演员通过实际 B05 和 B06 完成预登记。 |
| AC112 | 有mapping+create/Base/字段权的CREATE_AND_LINK PRE_REGISTERED | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC113 | B06原提交成功后撤create/register，保留查询/披露权 | PASS | 撤除 add/register 后，B06 原 UUID、正文和 ETag 返回完全相同的原回执和摘要。当前业务记录数保持。 |
| AC114 | B18 CREATE_AND_LINK原提交后失去写权但仍可读原结果 | 待验 | WP04-03 的 Mapping、ReverseClaim、事件、对账和 Owner 纠错。 |
| AC115 | 快查未见→并发原请求commit→撤权→本请求进最终门 | 待验 | 保留原条件，随相关共同权限和事务的组合验收核对。 |
| AC116 | 原终态存在但当前结果查询权撤销 | PASS | B13 结果存在，撤除 command-result.read 后 GET 原结果和同键 POST 均为 403，无原回执。 |
| AC117 | 仍有查询权但当前敏感披露不足 | 待验 | 保留原条件，随相关共同权限和事务的组合验收核对。 |
| AC118 | 原Actor有查询权但同Command改body/IfMatch | PASS | 相同 UUID 更换 Preview 正文或只更换 If-Match 均冲突。原回执原样，未生成新业务效果。 |
| AC119 | 其他Actor尝试POST原Command，即使可审计查询 | 待验 | 保留原条件，随相关共同权限和事务的组合验收核对。 |
| AC120 | 最终有序重查仍无原终态且write权限已撤销 | 待验 | B13 在状态动作被撤后拒绝新键，仍能重放旧键。跨 Owner Mapping/Claim/Outbox 分支归 WP04-03。 |
| AC121 | 当前身份失效但历史曾为原创建人 | 待验 | 保留原条件，随相关共同权限和事务的组合验收核对。 |
| AC122 | 历史快读与真实事务并发，检查锁序 | 待验 | 保留原条件，随相关共同权限和事务的组合验收核对。 |
| AC123 | 原成功命令重放时旧Preview已过期或当前BP变化 | 待验 | 原成功后状态变化及预览已用仍按原键返回，单独将旧 Preview 时钟推进至过期的边界尚待实测。 |
| AC124 | 原NO_CHANGE或REJECTED已有终态，今日条件改变 | 待验 | USE_EXISTING 返回原 NO_CHANGE，未执行已记录 REJECTED 和条件变化组合。 |

## 必需修订逐项核对

已采用 CRM 的核销只复用该仓准确版本 `677509b2b5fa48db213d00e44098a9de654e53ea`。[CRM #242 实际报告](https://github.com/GTX537/CP6.CRM/blob/677509b2b5fa48db213d00e44098a9de654e53ea/docs/development/evidence/WP03-05/DELIVERY.md) · [修订目录](../../../design-handbook/remediation/08-acceptance-catalog.json)

| 修订 | 结果 | 证据和剩余出口 |
|---|---|---|
| W1-ACC-01 | 待验 | CRM A03 回包丢失后，账户停用、写权移除、当前回查权保留的组合。沿用 WP03-05 报告的缺口出口，由 CRM 同合同后续核验。 |
| W1-ACC-02 | 复用 PASS | CRM #242 已实际采用。原账户命令改账户或 actor 时冲突或按当前读取权限拒绝，没有别名旁路。此证据保留原 CRM 范围。 |
| W1-ACC-03 | 待验 | BP 七字段 rev14 与 R03 账户同时采用后只做历史/被动投影。WP04-03 与 CRM receiver。 |
| W1-ACC-04 | 待验 | 共享前驱而 Owner 未选决时停在 WAIT_OWNER_RESOLUTION。WP04-03 与 CRM correction。 |
| W1-ACC-05 | 待验 | 同 scope/epoch/position 相反签名及完整纠错覆盖。WP04-03 与 CRM correction。 |
| W1-ACC-06 | 待验 | A13 与对账户停用及身份修改竞争。沿用 CRM A13 归属。 |
| W1-ACC-07 | 待验 | Main mapping 后续变化，新商业用途同步验 BP 和 mapping。WP04-02/WP04-03。 |
| W1-ACC-08 | 待验 | H02 源通知缺 Manifest/目标时等待且零跨域写。沿用 CRM H02 归属。 |
| COM-OWNER-LOCK-ORDER-POS | 待验 | Main 已实测本域事务。BP 与真实 QTN 消费在同一门竞争，完整闭包仍待 WP04-02 联合验收。 |
| COM-OWNER-LOCK-ORDER-NEG | 待验 | 跨用例的锁序倒置、调用者先读后锁或异库失败关闭由 WP04-02 联合验收。 |

## 工时与登记

从席位 START `2026-11-20T02:19:38.366234+00:00` 到核销快照 `2026-11-20T02:56:54.849806+00:00`，本卡有效 **2234.210 秒**，目标核对单列 **2.274 秒**。按 #345 将 **135.875 秒**只补记给 WP03-05 一次，该卡最终 **1854.370 秒**。结转后的计划起点 **89092.746 秒**，当前累计 **91329.230 秒**。[WP03-05 总控收尾](https://github.com/GTX537/CP6.AI-Handoff/blob/b06752bb4ae4b6428dceb21de6c77beda4a108d8/handoffs/CP6-DEV-20261012/tasks/WP03-05/replies/20261120T020029Z-coord-acceptance-ack/MESSAGE.md) · [唯一进度台账](../../roadmap.json)

M 卡预算 4 小时，强制拆卡点 6 小时。本卡无需拆分。阶段 C 重新计算的候选顺序为 WP03-08（40）、WP04-02（39）、WP02-22（39）、WP03-12（6）。本轮只执行 WP04-01，其余候选等独立派工。
