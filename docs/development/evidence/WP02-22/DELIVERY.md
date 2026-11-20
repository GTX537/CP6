# WP02-22 商业交接原生合同、来源保护与原结果

执行人为 EXEC-WP02-14B。Main DEV-1 的官方开工为 2026-11-20T08:09:53.303293Z。核对最新 Main 基线 af73b53801e3ce0f9ba5d0a8b0b7a7fbd91e48ae 后领取分支 codex/wp02-22。S01-09 和 WP02-10 完整 DONE 且无余卡。Platform #58 的 0.10.4 原字节实现已被 Platform 远端 f3fbe4be67d40641f7a8a969d74be65a95541819 采用。Main #1238 的事务原语是本卡基线的祖先。CRM 远端核对为 2887d57b76c4869e0aa7c05e38dbb1754872a5e1。[派工 #355](https://github.com/GTX537/CP6.AI-Handoff/blob/0b6ec808bbd6ff07502bd9c46dd4e07b5e3445d8/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T075945Z-exec-wp02-14b-wp02-22/MESSAGE.md) · [原链 START](https://github.com/GTX537/CP6.AI-Handoff/blob/2cd12e768dd80cf49eaa37bd5be8b91f3fc568ff/handoffs/CP6-DEV-20261012/tasks/WP02-22/messages/20261120T080953Z-exec-wp02-14b-start/MESSAGE.md) · [Main 保护 PR](https://github.com/GTX537/CP6/pull/1286)

## 实际交付

- Main 通过 CommercialOriginalV1 固定调用 H03 请求 CRM-HO-OPP/1、H03 原结果 OPP-INTAKE/1、H06 OPP-QUOTE/1 和 H08 OPP-COMMERCIAL/1。调用方提供已经登记的 T、Authority、L、Producer、Consumer、Contract、Epoch 和 RouteVersion，正文仅按自己的封闭合同验证。H03 的 Authority 定位目标 Main，源保护参与者保留实际源 Owner。原结果绑定原 Owner 和规范流。BodyHash 冻结实际 UTF-8 字节，业务摘要独立核对。规范值保留 opaque 字符串、正 Int64 十进制文本和净额的四位字符串。Group 完整快照核本份订单、成员、覆盖、证据摘要以及原币种有效位。实际订单谱系、Head 和报价/受理关联由对应消费方负责。[原件编码与核对](../../../../CP6.Core/Services/ErpIntegration/CommercialWireV1.cs)
- CommercialProtectionOwnerV1 提供 MAIN_INTAKE_NEW_EFFECT 和 CRM_RESULT_PROJECTION。已登记来源在自己的 Npgsql 外层事务内保存原件与控制根。Prepare 逐一绑定原请求、原件、参与集和保护用途，当前授权与读权分别复核。来源撤销先封新 grant，既有保护等待所绑定执行终态。控制闭包的持久索引支持用途政策及同一 Producer/Contract/Epoch 的全流撤销。[来源保护与原件查询](../../../../CP6.Core/Services/ErpIntegration/CommercialProtectionOwnerV1.Source.cs)
- 消费方先持久化 PREPARING 并查证每一份原 grant，齐全才 READY。消费事务先取当前权门，再取同一执行根，业务回调、原收据与 COMMITTED 同提交。Abort 使用同一根和不可变 ABORTED 墓碑。独立 READ COMMITTED 查询才产生可核验的终态引用。Settle 在源事务锁外向已登记消费 Owner 查原终态，核对完整绑定后才收口。历史原收据按当前读权返回。
- 新账通过 PostgreSQL 前向迁移登记于 erp_integration。敏感原字节、证据和收据以 Data Protection 保存，保留准确原键与索引。Main 待真实 Owner 调用 ICommercialAuthorityGateV1 和 ICommercialOwnerOriginalsV1；业务采用和实际 RouteBinding 分别由后续消费、投递与联验卡证明。旧 C03 主线入口继续以原 C03 固定 topic 与冒号 partition 工作，Platform 通用入口按 event-slug 与斜杠 partition 复验。

## 本地验证和模拟范围

产品源码 be3b1f41dc4a40d75b82bfad363f8916b988d402，产品树 dfa7ac211164e1a865e1d5ff278868398e15dd5e，最后核对 2026-11-20T08:43:57.2004599Z。Windows 上使用实际 Main 源码、Kestrel 回环 HTTP 及隔离 PostgreSQL 18.6。定向合同、保护和旧事件测试 107/107，相邻 C03 HTTP、运行配置及原键 PostgreSQL 测试 30/30。各次运行均核对通过、跳过和隔离实例停止状态。

| 场景 | 实际运行与结果 | 模拟范围 |
|---|---|---|
| 四种原件 | 归档原件从发送到回环接收再恢复，四个 BodyHash 与业务摘要对照向量一致。重新编码得到 409。v2 补件、Intake 专用纠错及 F02 保护键和 GrantDigest 也按冻结样本复核。 | 外部 Owner、客户、业务数据、设备和路由登记均使用合成夹具。实际 loopback HTTP 与 Main 原生合同代码参与。 |
| 类型与旧协议 | 未知 schema/epoch、错误 T/Owner/L、额外字段、重复字段、非空 tax/gross、错误 H08 覆盖、Decimal 越界和版本缩窄拒绝。真实 C03 和 Platform 验证器各自只接受对应 partition 和 topic。 | 合同样本为测试数据。当前实际 CRM QuoteHead 的前后状态由消费卡复验。 |
| 同原键、回包丢失及重启 | 两类用途分别保留一份保护、业务效果和原收据。Prepare、外层 COMMIT、Settle 已实际提交后模拟丢回包。第一个测试进程退出，再由另一个进程从同一数据库与文件密钥环读出两条原账。两进程 PID 31808、35636，原键恢复证据 SHA-256 4a1b80eaae48429768044e2be0d9092a147cbebdd263eb9d5f2a9fa634c3e1db。 | 模拟故障在提交后丢弃响应。用户、设备、外部 Owner 注册及回调内业务数据是夹具。PostgreSQL、持久密钥、跨进程恢复与实际 Main 状态机参与。 |
| 撤销、未知与终态 | PostgreSQL 查得 Abort 在同一执行根实际阻塞。消费提交后 Abort 只返回原 COMMITTED。Abort 先提交时晚到者零业务写。源撤销立即拒新 grant，同流的两份既有 grant 逐一对账。超过 progressDeadline 三天仍 GRANTED。失联、缺失及不匹配证明维持保护。 | 业务效果在独立测试表记录。测试时钟前推，源权限及外部 Owner 连接由隔离夹具模拟。 |
| 原子性、权限与迁移 | 外层回滚后 READY 与业务零写。回调写入后故障并尝试提交外层，保存点仍撤回回调写入。撤去新动作权后仍按当前读权取原收据，撤回读权时立即拒绝。前向迁移保留旧 C03 记录，原 C03/Platform 协议另外通过回归。 | 模拟用户、设备和源权限，以实际 PostgreSQL 当前权限行和事务锁参与。证据表仅用于隔离测试。 |

可复查产物位于 D:/CP6-test-artifacts/WP02-22/current-20261120，主要为 verified-product.trx、c03-regression.trx、proof.json、ledger-facts.json。台账结构校验、四份项目状态页同步和 120 项台账工具测试通过。冒烟、远端采用提交、官方 finish、分支清理和 DEV-1 释放以同链 DELIVERY 的实际回执核对。生产启用依相应发布与消费卡的实际审批和证据。

## 目标核对和工时

INT-COM-01 保持开放。逐项核对 106 项原 AC、20 项必需运行修订和适用采用证据。原 AC 10/106 完整通过，20 项具备局部证据，必需运行修订 0/20。实际 Main 受理、CRM 投影、原键目录和真实已登记的运输另依对应卡交付。原目录和修订 ID 按冻结版本复核。[原 AC 源](../../assessments/20261011/catalogs/foundation.json) · [修订源](../../../design-handbook/remediation/08-acceptance-catalog.json)

核销快照 2026-11-20T08:45:07.893807+00:00。本卡有效 2114.350 秒，目标核对另计 0.241 秒。根据 WP03-09 的官方 finish 和 Main 已采用候选时间查重后，WP03-09 单次补记 171.111 秒，合计 2335.222 秒。计划累计 100973.385 秒。正式收尾的实际时间在原链另记。[总控签收](https://github.com/GTX537/CP6.AI-Handoff/blob/57b90168278943546dc19fe004980914558ffcc4/handoffs/CP6-DEV-20261012/tasks/WP03-09/replies/20261120T075706Z-coord-acceptance-ack/MESSAGE.md) · [唯一台账](../../roadmap.json)

## 缺口登记

| 未 DONE 责任卡 | 实际剩余出口 |
|---|---|
| WP03-23 | CRM 提交和补件的完整写集。由实际 CRM 商机提交方验证 Intent、H/V、原 Event、Pending 和 Outbox 的一次外层提交与历史恢复。 关联 AC01、AC04、AC41、AC42、AC62。逐项断言及已有证据见本卡报告。 |
| WP04-13 | Main 受理、当前权限和原键目录。由实际 Main 受理方验证全租户 H 根、候选详情和内容权限、版本槽、完整目录、最终事务及持久结果。 关联 AC03、AC05、AC25、AC27、AC49、AC52、AC53、AC54、AC85、AC86、AC87、AC88、AC89、AC90、AC91、AC101、AC102、AC103、AC104。逐项断言及已有证据见本卡报告。 |
| WP03-24 | CRM 原事实商业投影。由实际 CRM 消费者验证 QuoteHead、原受理与责任组、Group 与 Opp 事务、冲突、净额及 FirstWin 账。 关联 AC09、AC10、AC11、AC12、AC13、AC14、AC15、AC16、AC31、AC37、AC40、AC44、AC45、AC46、AC47、AC48、AC50、AC51、AC56、AC66、AC67、AC95。逐项断言及已有证据见本卡报告。 |
| WP03-25 | 原字节投递、租约和安全恢复。由实际 CRM 投递端验证已登记原路由、可恢复传输账、原键查询和 WAITING 续办。 关联 AC02、AC43、AC55、AC57、AC58、AC59、AC60、AC61、AC68、AC69。逐项断言及已有证据见本卡报告。 |
| WP14-02 | 不同实际 Owner 的完整闭环验收。串起采用的 Main、CRM、账户、QTN、Sales、运输及各自真实安全门，逐条完成两类 W1-ORD、双边保护、有界重建、S6 和实际部署前采用矩阵的隔离验证。 关联 AC06、AC07、AC08、AC17、AC18、AC19、AC20、AC21、AC22、AC23、AC24、AC26、AC28、AC30、AC63、AC64、AC65、AC70、AC71、AC72、AC73、AC74、AC75、AC76、AC77、AC78、AC79、AC80、AC81、AC82、AC83、AC84、AC94、AC96、AC97、AC98、AC99、AC100、AC105、AC106，另含本目标 20 项登记的必需运行修订。逐项断言及已有证据见本卡报告。 |

## 106 项原 AC 逐项复核

原用例的运行要求和预期摘自冻结目录。NOT_RUN（部分）表示本卡已有相关原语证据，原断言继续开放。

| 原 AC | 原输入与预期 | 台账状态 | 本地证据或剩余责任 |
|---|---|---|---|
| AC01 | H03本地提交。完整HO S01–S06，Key原样发送 → 202仅LOCAL_REQUEST，CRM Intent/HO请求/Outbox各1；Main主账0 | NOT_RUN | CRM 提交和补件的完整写集需按原输入验收。 WP03-23。 |
| AC02 | 只Broker ACK。HO S08，Main结果未回 → 运输BROKER_ACKED；Main UNKNOWN；Won不得true | NOT_RUN | 原字节投递、租约和安全恢复需按原输入验收。 WP03-25。 |
| AC03 | Main受理。S30原v2，当前守卫全有效 → Intake根/Decision/OPP-INTAKE Outbox同提交各1，不建订单 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC04 | NEEDS_INFO补件。S16→S18七prior→S26–S30 → 同H v2/新Intent/Event，v1原字节保留；CurrentPending仅v2 | NOT_RUN | CRM 提交和补件的完整写集需按原输入验收。 WP03-23。 |
| AC05 | 受理前驱缺失。先投v2，Main尚无v1结果 → WAITING_PREDECESSOR，无ACCEPTED/新Intake终态 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC06 | 多候选BP。两个同名不同BP合法候选 → REVIEW_REQUIRED/人工明确决定；不可自动选首项 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC07 | BP映射同事务。BP E07–E15真实LINK → Main Mapping/Claim/Outbox一次；CRM独立提交 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC08 | 映射ACK非完成。BP E15仅2xx，无projection read → ACKED_PROCESSING且projection UNKNOWN | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC09 | H06原完整wire。HO S36准确QuoteResult → Quote引用一次；net原值tax/gross null；Forecast/Won不变 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC10 | 报价接受非订单确认。QuoteResult合法ACCEPTED无Sales结果 → 显示Main报价接受，Commercial无新增贡献 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC11 | Created非Won。OPP B A-created → 成员0/0，pending true，Outcome非WON | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC12 | 两组有效确认。OPP B A-confirm40+B-confirm60 → CNY C100/X0/E100，FirstWin1，原组各独立序号 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC13 | 取消保历史。B A-cancel10→A全取消→B全取消 → 当前E90→60→0，FirstWin历史仍1 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC14 | CANCELLED保有效范围。OPP15.7确认100取消60有存活40 → E40；不能由头取消清0；coverage须原Owner范围证明 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC15 | H03拒绝与确认矛盾。同source已REJECTED_FINAL再来真实Confirm → HANDOFF_RESULT_CONFLICT，保两原件，不最新时间胜出 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC16 | 责任替换不双计。原60退休→新20+40 REPLACE → 当前60非120，逐旧share守恒且旧键墓碑保留 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC17 | 合法旧受控读。真实T/client/allowlist/token，原key且200合法 → 旧观察成功但isExecutionPermit=false，不转换新QuoteVersion | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC18 | AllowAnonymous非无认证。无C03.Services身份访问旧read → 401或身份拒绝，业务写0 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC19 | 租户header不符。token A/header B → 拒绝且不泄B对象 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC20 | client被撤销。jti记录RevokedAt非null → 403，旧token剔缓存，本次不偷偷重试 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC21 | 404不可当未执行。Main runtime不存在或原key未找到 → NOT_FOUND_OR_NOT_CONFIGURED，原业务仍UNKNOWN | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC22 | 503与timeout。body读取超10秒或SQL unavailable → UNAVAILABLE保历史，不变空金额/不发正式请求 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC23 | 无效响应。200错tenant/key/重复属性/oversize → INVALID拒绝整响应，无部分采用 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC24 | 法人范围未知。旧DTO无L且Owner key→L未证 → SCOPE_UNPROVEN，不默认tenant单法人 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC25 | 原键无未来receipt。HO S13 exact H/V/intentDigest → 可查目录；NOT_OBSERVED noEffectProven=false | NOT_RUN（部分） | 已有独立原 Execution 缺失为 UNPROVEN、noEffectProven=false，仍需 HO S13 原 H/V/intentDigest 完整目录。 WP04-13。 |
| AC26 | 准确Quote原链。QTN I13 fromOrdinal缺口 → 完整原event/trust/水位，非当前GET伪前驱 | NOT_RUN（部分） | 保留 H06 节点原字节与 ordinal；QTN I13 仍需证明缺口中所有原事件、Trust 及水位。 WP14-02。 |
| AC27 | 目录分页源变化。第一页后cut/权限generation改变 → cursor stale从null重查，不能拼全量 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC28 | 源当前完整性未知。SourceVerifier complete=false → 不ACCEPTED/不投影新应用，历史保留 | NOT_RUN（部分） | 不完整参与集合和原 Grant 读证失败会阻执行；待真实 SourceVerifier 的两侧消费。 WP14-02。 |
| AC29 | H03闭合schema。原Envelope额外processingType顶层 → CONTRACT_INVALID，无偷偷忽略 | PASS | Main 原生 H03 闭合解析拒绝顶层 processingType。 |
| AC30 | H04七字段。AccountErpMappingEvent加入tenantId → 拒绝扩展；T仅真实可信上下文 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC31 | H06tax/gross。原QuoteResult tax改为非null → DOMAIN_TYPE_MISMATCH，不写QuoteHead | NOT_RUN（部分） | 原生 net-only 拒绝非空 tax 和 gross；仍需真实 CRM QuoteHead 前后快照。 WP03-24。 |
| AC32 | opaque版本。quoteVersion=quote-v10并非base64 → 按Key保持；不解码为rowversion/数值排序 | PASS | quote-v10 与带空格、全角字符和前导零的 opaque 版本按原字符串保留。 |
| AC33 | Int64上限。ordinal="9223372036854775807"后继 → 拒绝耗尽，不转int溢出/换epoch | PASS | Int64.MaxValue 仍用十进制串，拒绝后继耗尽、JSON number 与越界。 |
| AC34 | C03真实partition。旧消息tenant:aggregate → 只旧adapter接受；slash规则不可混用 | PASS | 实际 Main C03 metadata validator 只认原固定 topic 与冒号 partition，拒绝 slash。 |
| AC35 | 通用Platform partition。通用event-slug及tenant/aggregate → 按原通用validator，不能声称固定C03 topic等同 | PASS | 实际 Platform Dapr delivery validator 识别 event-slug、斜杠 partition，拒绝旧 C03 topic 与冒号组合。 |
| AC36 | 新body过旧validator。H03含lines或Quote含tax:null → 旧C03拒绝，Gate不能伪通过 | PASS | 将 H03 的 lines 与 H06 的 tax:null 带到真实 C03 validator 时拒绝。 |
| AC37 | 未知schema/epoch。可信service携未采用contract或新epoch → 隔离，当前Head不变，不自动换代 | NOT_RUN（部分） | 原生 schema/epoch/Owner 不匹配拒绝；业务队列隔离及实际 Head 写数待证。 WP03-24。 |
| AC38 | 原体roundtrip。发布原bytes→transport→消费还原 → BodyHash逐字相同；不同则不采用 | PASS | 隔离 Kestrel 回环传送四份原字节，Main 捕获前后四个 BodyHash 一致；重编码收到 409。 |
| AC39 | 金额类型与精度。Money.amount number/指数/第五有效小数 → 422/隔离，不四舍五入或default0 | PASS | Main 原生严格拒绝 number、指数、第五小数、超出 decimal(19,4) 及格式改值，不舍入或归零。 |
| AC40 | 旧订单错误升级。C03 OrderCreated带BookedAmount → LegacyObservation/CREATED_NOT_WON，无ConfirmationRef补造 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC41 | 同O同体。ACT02响应丢后同原header/body → 回同H/V/Event，不新增Outbox | NOT_RUN | CRM 提交和补件的完整写集需按原输入验收。 WP03-23。 |
| AC42 | 同O异义。原Key改reason/target/token/If-Match → COMMAND_CONTENT_CONFLICT，不覆盖原成功 | NOT_RUN | CRM 提交和补件的完整写集需按原输入验收。 WP03-23。 |
| AC43 | 同Event异body。改net保EventId → Transport冲突保双原件，不更新投影 | NOT_RUN（部分） | 同一自然源替换原 BodyHash 拒绝；真实 Transport 冲突需保存两原件。 WP03-25。 |
| AC44 | 新Event同业务。旧A-created换运输alias → DUPLICATE同历史ordinal，CommercialRevision保持5 | NOT_RUN（部分） | 独立 BusinessDigest 保持语义同一；实际 CRM 要据原版本做一次投影且保留 CommercialRevision。 WP03-24。 |
| AC45 | 旧ordinal异义。历史ordinal2改额50 → BUSINESS_VERSION_CONFLICT，即使当前ordinal3亦隔离 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC46 | 缺前驱。B4先于B3到达 → WAITING；B3原链恢复后才应用B4，不能跳链 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC47 | 错误前驱digest。n+1 previousBusinessDigest错1字 → CONFLICT，无Head推进 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC48 | 请求版本与结果分轴。v2结果ordinal1，v1ordinal较大 → v2按自身流，不由最大ordinal切请求 | NOT_RUN（部分） | 请求版本和结果 ordinal 为独立字符串坐标；真实当前请求选择待证。 WP03-24。 |
| AC49 | 并发同H/V。两个Main workers不同运输alias → 一Decision/一业务结果，其余别名 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC50 | 并发不同组同Opp。G1/G2并发Confirm40/60 → Opp根串行，总100无丢组/反向锁死锁 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC51 | SQL事务故障。每一写集边界注入constraint fault → 全部业务回滚，不留孤立Outbox/半Won | NOT_RUN（部分） | Main 原语在数据库事务和保存点故障时原效果与终态全退；CRM 原 Inbox、贡献及历史写集待证。 WP03-24。 |
| AC52 | 撤权最终竞争。本地IAM撤权先持共同门或源撤销先阻新grant → 新执行拒绝且业务写集0；既有grant按R1终态互斥收口 | NOT_RUN（部分） | 原语使用本地当前权门再取执行锁，源撤销立即关新 grant；真实受理 IAM 与业务写者要采用。 WP04-13。 |
| AC53 | 历史终态查询。成功后失decide权仍有RQ → 回原安全终态，不执行新写；失RQ拒绝披露 | NOT_RUN（部分） | 受保护执行可在新写权限回收后按当前读取权返回原收据；真实 Intake 决定和安全输出待证。 WP04-13。 |
| AC54 | 租户隔离幂等。A/B相同operation UUID及展示业务号 → 各自键域独立，不能命中他租户原结果 | NOT_RUN（部分） | 原保护行与端点参数都有 TenantId；隔离 PG 的错误租户读拒绝，两个真实 Tenant 受理同 key 待证。 WP04-13。 |
| AC55 | 发送ACK丢失。broker接受后worker崩溃 → lease过期同Event/原Body重投，业务效果不重复 | NOT_RUN | 原字节投递、租约和安全恢复需按原输入验收。 WP03-25。 |
| AC56 | 消费ACK丢失。CRM已commit后响应丢 → 原Inbox回原receipt，金额/FirstWin不增 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC57 | WAITING重启。持久缺源消息后重启，源后来到 → 原payload/trust续办，不能Event存在duplicate早退 | NOT_RUN | 原字节投递、租约和安全恢复需按原输入验收。 WP03-25。 |
| AC58 | 密钥缺失。WAITING原KeyVersion无法解密 → REPLAY_EVIDENCE_UNAVAILABLE；不拼造当前GET | NOT_RUN | 原字节投递、租约和安全恢复需按原输入验收。 WP03-25。 |
| AC59 | 人工重排原体。原HO商业RetryPlan指DEAD → 仅Delivery revision/调度/审计变；业务原key/body不变 | NOT_RUN | 原字节投递、租约和安全恢复需按原输入验收。 WP03-25。 |
| AC60 | 租约旧worker晚回。lease1失效lease2已claim，lease1失败到达 → 旧回报只观察，不能覆盖lease2或确定ACK | NOT_RUN | 原字节投递、租约和安全恢复需按原输入验收。 WP03-25。 |
| AC61 | 业务拒绝不自动重试。REJECTED_FINAL且网络健康 → 停止业务重复；新输入需明确新意图 | NOT_RUN | 原字节投递、租约和安全恢复需按原输入验收。 WP03-25。 |
| AC62 | 补件不是改旧body。NEEDS_INFO后需求保存新版本 → 原H下一V，旧V不可UPDATE | NOT_RUN | CRM 提交和补件的完整写集需按原输入验收。 WP03-23。 |
| AC63 | 映射REPLACE保历史。BP E30–E40原同scope决定 → 新r+1当前，旧r及旧Use/Order引用不变 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC64 | 映射AHEAD。CRM投影revision>Main已证head → AHEAD暂停自动修复，不能MAX+1 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC65 | 映射查询乱序。Q2新generation失败后Q1旧成功返回 → 当前仍UNKNOWN/CONFLICT，不变绿色 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC66 | Intake专用纠错。原拒绝+真实Main候选+准确Correction → 原OPP整笔写集，原拒绝保历史，无再Confirm | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC67 | 不相关冲突保留。resolution只列source G1冲突 → G2/金额冲突不清，当前可用性按剩余决定 | NOT_RUN | CRM 原事实商业投影需按原输入验收。 WP03-24。 |
| AC68 | NOT_OBSERVED恢复。HO查询job已COMPLETE但无结果 → 业务UNKNOWN，noEffectProven=false，不换H/Key | NOT_RUN | 原字节投递、租约和安全恢复需按原输入验收。 WP03-25。 |
| AC69 | 错误消息租户/法人。真实T A路由收到body B或L B → 隔离零业务，不能编辑body后重放 | NOT_RUN（部分） | 原生 T/L 不符拒绝并保持原 bytes；实际消息接收隔离、原件维护和业务零写待证。 WP03-25。 |
| AC70 | 重建缺全量历史。Main事件齐但缺本域Attribution/Loss → REBUILD WAITING，active generation不切换 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC71 | 重建CAS竞争。g准备后实际writer推进或新cut出现 → CAS失败全回滚，原generation保留 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC72 | 重建成功重启。g→g+1完成后重启同job → 回原receipt不建g+2、不新FirstWin | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC73 | 未配置默认。production config全null/旧Enabled false → 新动作disabled，保允许的只读；不显示已发送 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC74 | 真实C01/C02映射。reader T不在Storage或Identity → Host/能力诊断失败，不fallback默认tenant | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC75 | 两token混用。DAPR_API_TOKEN当APP_API_TOKEN → callback拒绝，不以本地认证代业务scope | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC76 | MARS及非SQL。MARS=true或in-memory provider → 事务能力门失败，无新Main受理 | NOT_RUN（部分） | 原语强制已登记的 Npgsql 连接和原生事务；实际受理 Host 启用门及完整环境异路待证。 WP14-02。 |
| AC77 | schema hash不符。两端同版本名但bundle内容不同 → CONTRACT_NOT_ADOPTED，不能忽略hash | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC78 | 双writer。原OPP与另一HO消费者都准备写贡献 → 关闭新流；不以重复去重掩盖不同算法 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC79 | 无Source fence。只有TTL读证据无共同门 → O-IC-GUARD未证，新动作零业务 | NOT_RUN（部分） | 待验证生产写者闭包；本卡未证或失联明确保持 GRANTED，不把 TTL 当可信终态。 WP14-02。 |
| AC80 | 旧新积压。旧C03 backlog与新raw flow并存 → 分类只观察/单写者；不重命名topic或直接转Confirm | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC81 | 保留政策未知。retention policy=null → 本目标cleanup disabled，不猜年限 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC82 | production secret。诊断/日志/导出请求配置 → 只Secret引用/安全指纹，无明文token | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC83 | SC24真实注册。未来隔离SQL+正式Host，至少T A/B → 必须RunId/SHA/数据库证据；stub/文书解析不可标PASS | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC84 | 重新开启。route/client/schema变更后尝试resume → 重新校验双边能力/原积压/原版本，不能清队列后启用 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC85 | R1空白候选到决定。F01-01空页→S30提交→F01-03候选→F01-05详情→IC01/03 → 真实H/V和BodyHash连续，M00Q/D无业务效果，M03才原子受理 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC86 | R1字段裁剪阻决定。F01-06 requirement REDACTED/null → decisionContentComplete=false，无PREPARE_DECISION；不以不可见当客户缺资料 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC87 | R1不可知候选详情。L2用户读取L1 H根或传任意H/V → 404 REQUEST_NOT_AVAILABLE，不泄L1/源正文 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC88 | R1旧Detail准备失效。详情后scope/fieldPolicy/requestGeneration变化 → M02/M03拒绝STALE或FORBIDDEN，不接受客户端完整标记 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC89 | R1撤销先阻grant。F02-N04 source撤销先提交new-grants blocked → Prepare DENIED，无Grant/MainDecision/业务Outbox | NOT_RUN（部分） | 数据库撤销登记后新 Prepare 持久 DENIED，Grant 空且隔离效果表零增长；待真实 Decision/Outbox。 WP04-13。 |
| AC90 | R1业务先持Execution根。10:08:19锁→撤销20阻新grant→Abort21等→10:09commit → Abort回COMMITTED；仅一个Intake/结果，源10:09:02安全收口 | NOT_RUN（部分） | 实际 PostgreSQL 确认 Abort 等待同一执行根，消费提交后返回 COMMITTED，原保护收口；Intake/结果业务表仍需真实消费方。 WP04-13。 |
| AC91 | R1Abort先持Execution根。F02-N01/02写ABORTED后原Grant晚到 → 旧worker不得业务commit，0Decision/0结果Outbox，原终态不重开 | NOT_RUN（部分） | 实际 PostgreSQL 持久 ABORTED 使晚到回调零写；实际 Main Decision/Outbox 零写仍需受理方。 WP04-13。 |
| AC92 | R1失联过deadline。F02-N03 10:14仍无消费方Terminal → 源GRANTED/PENDING_TERMINAL，自动释放false，不以到期判未执行 | PASS | 隔离 PostgreSQL 持久授权越过 progressDeadline 三天仍 GRANTED，撤销一直 PENDING_TERMINAL，伪证与失联不释放。 |
| AC93 | R1崩溃后原账收口。Prepare响应丢/commit响应丢/Settle ACK丢依次恢复 → 同Execution/protectionKey查持久原结果，不新grant/代次/业务 | PASS | 丢弃 Prepare、业务 COMMIT 和 Settle 三个实际提交回包，两独立进程按原 key 恢复；两类用途分别只有一份 grant、业务收据和原终态。 |
| AC94 | R1敏感访问即时撤销。业务grant尚GRANTED而用户读取权限撤销 → 立即拒绝披露；不能业务用途保护延期用户访问；政策不相容不开用途 | NOT_RUN（部分） | 隔离数据中的当前读权在既有 GRANTED 下独立即时拒绝；真实用户、披露政策及共同写者的实际采用待证。 WP14-02。 |
| AC95 | R1历史事实恢复不重验销售新写权。原用户失交接编辑权但实际Main结果待CRM应用 → 按CRM_RESULT_PROJECTION及当前机器合法性，不重新申请原用户新用途；已APPLIED历史只RQ | NOT_RUN（部分） | CRM_RESULT_PROJECTION 可按当前机器授权表恢复本地原账；真实 CRM 投影、原销售权转换与已 APPLIED 分支仍需消费方。 WP03-24。 |
| AC96 | R1第二H全漏通知。F03 H2本地Link存在，G-SECOND全部通知缺失 → 完整4query发现7原结果及G-SECOND20，绝不只查H1 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC97 | R1归属转入原source。F03 B组来源商机不同但有效归属转入P → 查询B原H/V，保原Source及归属历史，不能用P自己H替代 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC98 | R1准备后集合增长。F03-N01 sourceSetGeneration4→5，新增H3 → 旧plan WAITING，active仍1，整集合重新枚举，不局部补后CAS | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC99 | R1目录最新水位增长。F03-N02 cutRecheck ADVANCED → 不READY切换；重取有界cut，原MainOriginalPage wire不改 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC100 | R1客户端不声明全集。R01携originalQuery/sources/complete等额外字段 → closed DTO拒绝；只有服务端表枚举生成完整查询集 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC101 | R1跨L同H不同Event。F04-N01已有L1根而L2合法route新event → HANDOFF_IDENTITY_BINDING_CONFLICT；全T根1，L2槽/Decision/Outbox0 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC102 | R1跨L并发缺行。同T同H两个L V1并发进入空根 → 全T不含L锁与PK至多一根，另一路安全冲突，不各自建槽 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC103 | R1同H不同T独立。T A/B相同H且各自有权V1 → 两根合法独立，无跨T原结果命中 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC104 | R1时间因果与摘要传播。S30 10:08→IC详情05/观察10→Context12→S34 10:09 → 观察不早于提交；独立撤销10:08:20新proof；IC02/03/08 ContextHash相同 | NOT_RUN | Main 受理、当前权限和原键目录需按原输入验收。 WP04-13。 |
| AC105 | R2联合谱系意图及Event唯一。F03四完整Envelope与七Owner结果同Tenant；负例把B意图或Event改成H1v2原键 → 原键→完整原体唯一；负例拒绝，不覆盖旧意图或Outbox | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |
| AC106 | R2修订单调和归属切点。H1v2销售8→9、第二INITIAL9→10；归属只推进商业6→7；11:00恢复 → 销售cut10、商业cut7；旧8/9不可用，四查询七结果候选180 | NOT_RUN | 不同实际 Owner 的完整闭环验收需按原输入验收。 WP14-02。 |

## 20 项必需运行修订逐项复核

两套 W1-ORD 编号按各自命名空间保留，实际完整采用均由 WP14-02 复验。

| 必需修订完整 ID | 原输入与预期 | 当前证据和余项 |
|---|---|---|
| DR-COM-HO-OLD-FACTORY::W1-ORD-01 | QTN Q24 commits; HTTP response lost → Q25 returns original OrderDraft/Fact/Allocation; same Intent cannot make another Order. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-HO-OLD-FACTORY::W1-ORD-02 | Confirm QTN-sourced draft → Current Fact and Sales/BP/Finance/approval gates checked; no H-to-C call and no second allocation. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-HO-OLD-FACTORY::W1-ORD-03 | QTN allocated draft quantity changes → No in-place source quantity edit; original draft permanently cancelled under I11 proof, then explicit new Intent, or changes stay blocked. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-HO-OLD-FACTORY::W1-ORD-04 | Original evidence A/Fact1 corrected to B/Fact3 → Old command returns A/Fact1 bytes; new Intent uses B/Fact3. Reuse key with B conflicts. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-HO-OLD-FACTORY::W1-ORD-05 | Fact becomes UNPROVEN before delayed impact arrives → Synchronous source guard blocks new Sales effect; old orders/Confirm/Allocation retained. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-HO-OLD-FACTORY::W1-ORD-06 | Two distinct responsibility groups share Opportunity → Distinct owner-approved source slots can create two Orders; same slot alias still one. No Opportunity-wide uniqueness. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-QTN-FACT-H07-R2::W1-ORD-01 | QTN Q24 commits; HTTP response lost → Q25 returns original OrderDraft/Fact/Allocation; same Intent cannot make another Order. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-QTN-FACT-H07-R2::W1-ORD-02 | Confirm QTN-sourced draft → Current Fact and Sales/BP/Finance/approval gates checked; no H-to-C call and no second allocation. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-QTN-FACT-H07-R2::W1-ORD-03 | QTN allocated draft quantity changes → No in-place source quantity edit; original draft permanently cancelled under I11 proof, then explicit new Intent, or changes stay blocked. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-QTN-FACT-H07-R2::W1-ORD-04 | Original evidence A/Fact1 corrected to B/Fact3 → Old command returns A/Fact1 bytes; new Intent uses B/Fact3. Reuse key with B conflicts. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-QTN-FACT-H07-R2::W1-ORD-05 | Fact becomes UNPROVEN before delayed impact arrives → Synchronous source guard blocks new Sales effect; old orders/Confirm/Allocation retained. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-QTN-FACT-H07-R2::W1-ORD-06 | Two distinct responsibility groups share Opportunity → Distinct owner-approved source slots can create two Orders; same slot alias still one. No Opportunity-wide uniqueness. | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-IC-PROTECTION-ADOPTION::COM-IC-PROTECTION-ADOPTION-POS | 撤销请求与已经获得grant的本地commit竞争 → 允许原grant先commit；source根据原终态结算，结果有证据 | NOT_RUN（部分）。本地已证同根竞争及持久终态收口；各真实 Owner 共同写者和实际业务提交待证。 WP14-02。 |
| DR-COM-IC-PROTECTION-ADOPTION::COM-IC-PROTECTION-ADOPTION-NEG | 用grant超时推导ABORTED或复活权限 → 拒绝释放，不输出过期敏感数据 | NOT_RUN（部分）。本地已证超过期限不释放、当前读权可即时拒绝；实际双边安全和披露采用待证。 WP14-02。 |
| DR-COM-IC-BOUNDED-REBUILD::COM-IC-BOUNDED-REBUILD-POS | 完成全部页且代际未变 → 原结果可被CAS采用，UNCHANGED不产生新商业事实 | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-IC-BOUNDED-REBUILD::COM-IC-BOUNDED-REBUILD-NEG | 中途漏页或新的成员加入 → 废弃当前候选重建；空页不证明无效果 | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-S6-PRECHECKPOINT-FINALAUTH::COM-S6-PRECHECKPOINT-FINALAUTH-POS | 重复外部事务且原checkpoint完备 → 当前读权限下返回原receipt | NOT_RUN（部分）。本地按当前读权返回原账，实际 S6 原 checkpoint 完整重复链待证。 WP14-02。 |
| DR-COM-S6-PRECHECKPOINT-FINALAUTH::COM-S6-PRECHECKPOINT-FINALAUTH-NEG | 同txId不同摘要或最终授权无真实gate → DRIFT quarantine撤销allow；无gate BLOCKED | NOT_RUN。实际原 Owner 写集需完成登记场景。 WP14-02。 |
| DR-COM-IC-ORIGINAL-TRANSPORT::COM-IC-ORIGINAL-TRANSPORT-POS | 按准确新路由运输原包并当前授权重放 → 按准确新路由运输原包并当前授权重放 | NOT_RUN（部分）。回环 Kestrel 已证原字节，隔离 PG 已证当前授权重读；实际已登记生产者和接收者传输适配待证。 WP14-02。 |
| DR-COM-IC-ORIGINAL-TRANSPORT::COM-IC-ORIGINAL-TRANSPORT-NEG | 删字段/改版/Platformcheckpoint替原Ownercut拒绝 → 删字段/改版/Platformcheckpoint替原Ownercut拒绝 | NOT_RUN（部分）。本地拒绝未知版、删改字节；真实路由拒偏及 Owner cut 不能被 Platform checkpoint 替代仍待实际闭环。 WP14-02。 |
