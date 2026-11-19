# WP03-02 浏览器绑定公开提交与 H01 自动受理

WP03-02 卡验收通过。执行人为 `EXEC-WP02-14B`。CRM 受测及已采用基线为 `5a3d272cea2579aaa904cec5db68d0b021932508`，CP6 提供方及台账基线为 `d409b993c8885113903aff9fbf808e10269ac6c5`。CRM 使用 DEV-2 `D:/CP6-worktrees/dev-2` 与 `codex/wp03-02`，CP6 的同卡台账使用 DEV-1。WP02-09、WP02-09b、WP02-16、WP02-16b 和 WP03-01 均为 DONE，已核实实际远端采用。按原卡保持公开表单、五字段回执、原命令复查和 H01 原生事务的适用范围。CRM 本轮产品差异为空。 [卡定义](../../task-plans/WP03.md#wp03-02) · [派工](https://github.com/GTX537/CP6.AI-Handoff/blob/f1902f336ac1e8613ea4bacbc18d4ad363da3945/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261119T005436Z-exec-wp02-14b-wp03-02/MESSAGE.md) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/4ae7a2dcada620aeddf89849af0ef287ef42141c/handoffs/CP6-DEV-20261012/tasks/WP03-02/messages/20261119T010724Z-exec-wp02-14b-start/MESSAGE.md)

## 入口与正反验收

| 场景 | 当前基线实测 |
|---|---|
| 原表单与正常提交 | 真实 Next App Router `/site/{siteKey}/forms/{formKey}` 经 BFF 调用 `GET /api/site/{siteKey}/forms/{formKey}`。CRM 在 PostgreSQL 持久化原 Bundle、到期及绑定哈希。合法同意后经 `/submissions` 得到 201。独立连接观察到一份 Submission、一条自动受理 Lead、一次消费和一个原命令，两条原生事件分别对应 Submission 与 Lead。原 H01 为 Website/New，Lead 及历史与 Submission 双向绑定，未产生商机、报价或订单。 |
| 重复与并发 | 同浏览器、原 Attempt、原键及同载荷的 HTTP 重放返回原 201、字节和 ETag。并发只产生一组业务效果，未重复扣额、写审计或入队。同键异载荷得到 `409 crm.public_idempotency_conflict`。 |
| 事务失败 | PostgreSQL 在 H01 图、原回执和命令均已暂存后的原生 Effects 写入注入故障，HTTP 得到 503。独立连接查不到半份 Submission、Lead、消费、原命令、审计效果、限额或当前事务的 Outbox。原生事实图的部分提交/回滚检查补证 ReceiptProof 和 ConsentFact 的事务约束。到期和取消落在写图之后的两例也回滚。先前 GET 的 IssuedAttempt 仍属于自己的已提交事务。 |
| 绑定及期限 | 第二个 Chromium 上下文先由真实 GET 取得自己的有效绑定，再提交首个浏览器的原命令，得到 `404 crm.public_not_found`。原生 Origin、坏 CSRF、坏 Cookie 同形拒绝，过期的原 Attempt 按数据库时钟得到 `410 crm.public_attempt_expired`。无内部资源标识，无新增业务图。 |
| 提交确认丢失与刷新 | Kestrel 在 PostgreSQL 提交后中断首个 201；同页草稿进入待确认。重试沿原 URL、原请求体、原幂等键和原 CSRF，原浏览器仅有一次表单 GET。两次 POST 最终只有一份 Submission 和一条 Lead。真实 Next 回执导航及 `GET /api/site/{siteKey}/receipts/{receiptId}` 刷新均为 200，响应字节与独立连接读取的原命令结果一致。 |
| 当前披露资格 | 公开回执仅有 `receiptId`、`displayReference`、`receivedAtUtc`、`message`、`expiresAtUtc`。原响应清理 Attempt Cookie，并签发 Secure、HttpOnly、SameSite=Strict 的回执 Cookie。第二个浏览器读取为 `404 crm.receipt_not_found`。已验证凭据不符、撤销、墓碑、过期、匿名化、未知和跨租户保持同一 404 形状，当前门复核先于原命令披露。 |

实现以 `CrmInquiryNativeHostV2` 为实际 PostgreSQL 入口。`CrmInquiryPublicOwnerV1` 签发，`CrmInquirySubmissionOwnerV1` 在串行化事务重新确认浏览器、原版本、可信身份和当前权限，并写入完整图、审计、Platform 0.10.3 Outbox 及原结果。`CrmInquiryPublicReceiptOwnerV1` 根据当前浏览器、证明和原行状态读回。Web 采用 `PublicInquiryPage.tsx` 和真实 `/api/site/[...path]`。该运行档明确启用原生公开流程。 [CRM 原生路由](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/src/CP6.CRM.Api/Inquiry/CrmInquiryNativeHostV2.cs) · [提交 Owner](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.cs) · [写图](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.Graph.cs) · [实际页面](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/apps/web/src/inquiry/PublicInquiryPage.tsx)

## NUM、FILE、PRIV 实际采用

有效消费者为 `CRM-INQ-TERMINAL-PROJECTION/20261013-R1`，提供源 Main `ce38171868312588ce8f4b9e843ed6c39f4d0b0f`。实算 OpenAPI SHA-256 为 `fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d`，与生成合同及派工一致。逐项沿冻结入口确认如下。 [采用说明](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/contracts/s01/README.md) · [冻结调用判断](../W135-01/DELIVERY.md)

| 能力 | 提供方、消费者及实际行为 |
|---|---|
| NUM | Main 编号归属方 v1 由 #1256 交付并已在当前 Main 采用。V15 允许 H01 未分配 `LeadNo`。CRM 当前 `WriteGraph` 使用该规则，两个实际 Next 流程独立查询均为 SQL NULL，原回执的随机 `displayReference` 仍按原键返回。当前公开/H01 写入没有 NUM 调用及授权交换。 [Main 编号合同](../../number-allocation-v1.md) · [提供方验收](../WP02-16b/DELIVERY.md) |
| FILE | Main 原件、扫描、绑定、当前下载与处置的 v1 能力已按 WP02-14b 交付。CRM 当前有效 R1 的公开请求、回执及 H01 两个对象均为闭合字段集。实际页面没有附件输入，提交图没有 FILE 请求及授权交换。FILE 导出制品的实际消费由 WP03-04 验收。 [提供方验收](../WP02-14b/DELIVERY.md) · [当前 OpenAPI](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/contracts/s01/revisions/CRM-INQ-TERMINAL-20261013-R1/crm-inq.terminal.openapi.json) |
| PRIV | 公开接收及 H01 实际使用 Main `/connect/inquiry-service-context` 的 `cp6-inquiry-service-context-v1`。`public:receive` 是服务身份用途，自动受理使用 `intake:release` 的域分离签名与投影。Main 当前源码的实际 TLS 签发、Basic 服务认证、当前 C02 来源和 CRM 独立 PostgreSQL 已联验。消费复核 tenant、site/form、主体、用途、当前版本及期限。ConsentFact、notice 摘要、policy version、用途、期限与原提交同事务在 CRM 持久化。原键回放不再次签发；实际读取复核 Proof 和原行的当前资格。CRM 公开路径没有跨库写 Main PRIV 主账。`InquiryConsentPermissionSourceV1` 和 `InquiryRetentionPermissionSourceV1` 的当前提供方及 CRM 具名用途分别定位于 WP03-04 的撤回、保留或导出链。 [Main 签发版本](https://github.com/GTX537/CP6/blob/d409b993c8885113903aff9fbf808e10269ac6c5/CP6.WebApi/Services/InquiryServicePermissionContextV1.cs) · [CRM 消费版本](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/src/CP6.CRM.Api/Inquiry/CrmInquiryServiceIdentityV1.cs) · [PRIV 提供方验收](../WP02-15/DELIVERY.md) |

## 页面状态与原验收

原 49 个状态包括公开表单 10、回执 5、受理队列 7、受理详情 8、配置 11、隐私导出 8。当前 C#、TypeScript 均按显式 R1 接受并往返原样例。下面逐项核对公开 15 个状态的实际页面映射。浏览器故障分支中的模拟响应与原生 HTTP 的真实拒绝各有记录。 [原设计](../../../design-handbook/modules/CRM-INQ-01.md) · [49 个原样例](https://github.com/GTX537/CP6.CRM/blob/5a3d272cea2579aaa904cec5db68d0b021932508/contracts/s01/upstream/composed/229813cf102aad92__ui-positive-cases.json) · [W507 错误映射证据](../W507-01/DELIVERY.md)

| 表面 | 设计状态 | 当前入口及观察方式 |
|---|---|---|
| 表单 | `loading` | 等待真实 GET 时显示正在打开，按页面初态和合同样例核对。 |
| 表单 | `ready` | 真实 GET 选择发布 Bundle、原期限、可选字段与告知。Chromium 实际可编辑。 |
| 表单 | `validating` | 浏览器真实空表提交显示字段错误，发出的 POST 数为零。提交严格解码另查合法 Unicode 边界。 |
| 表单 | `submitting` | 发出真实 POST 期间锁输入，由 React 门和延迟响应场景核对。 |
| 表单 | `ambiguous` | 真实确认丢失后禁用编辑，保持同一请求并显式重试。限额及安全等待分支同时有回归。 |
| 表单 | `success` | 真实 201 后进入原回执 URL。浏览器实际显示安全参考编号。 |
| 表单 | `stale` | `crm.form_updated` 等确定性拒绝进入显式重开和重新同意，Chromium 注入错误后的下一次提交走真实 Owner。原仍有效的旧 Bundle 走原事实。 |
| 表单 | `accessLost` | 当前权限拒绝或访问丢失事件清空草稿和字段。实际迟到成功响应不能继续跳转。 |
| 表单 | `error` | 表单读取异常进入重试入口，提交的已知输入错误显示允许的内容。对照 Web 客户端、实际 Problem 和合同样例。 |
| 表单 | `expired` | 实际 PG 到期 HTTP 410。浏览器过期分支显式重开，保留允许字段并重新同意。 |
| 回执 | `loading` | 真实回执路由显示读取中，响应前无参考编号，按页面初态和合同样例核对。 |
| 回执 | `ready` | 成功、同浏览器刷新均从真实 GET 读出原 200 与五字段字节。 |
| 回执 | `accessLost` | 生命周期事件清除当前显示；资格错误对外采用同形不可用页面。按页面分支及 HTTP 当前门核对。 |
| 回执 | `notFound` | Chromium 的第二浏览器与原生过期／撤销／匿名化分支返回同形 404。 |
| 回执 | `error` | 非 404 请求失败时显示通用故障及重读按钮。按实际 API 错误处理、页面分支和合同样例核对。 |

| 原 AC 编号 | 本卡对应出口与既有证据 |
|---|---|
| 001、005、006、010、043、044、046、052、059 | 真实表单、冻结发布源、BFF、H01 和安全回执。页面及状态见上表，原 TTL、cookie、响应头、随机 locator 和所有状态形状有当前验证。配置切换的完整正反依据复用 WP03-01。 |
| 002、011、012、019、020、022、029、034、037、045 | 重复、竞争、同键冲突、原键未知恢复、当前读门、限额和拒绝合同。当前提交、浏览器与 PostgreSQL 回归覆盖本卡分支。扩展受理的错误及锁序复用 W121/W507。 |
| 013、023、031、038、039、040、047、057 | H01 完整原图、来源、日历及 SLA、独立连接回读、事务审计/Outbox 和零重复。FX24 下游消息采用沿 WP02-09、W494 原证据。 |
| 021、025、027、028、032、041、056、058 | 已发 Attempt 冻结、版本化策略、同意事实、原行保留期限和已撤资格的回执拒绝。完整配置与保留处置分别沿 WP03-01 与 WP03-04 的原范围。 |
| 009、048、049、053、054、055 | 有效源码上的 C#/TS、生成引用、严格结构、Unicode、集合、签名和双仓当前来源。S01-08、W117、W484 的旧证明按固定输入复用。 |

原 60 条 PASS 延续现有台账。本卡核对实际公开路径，目标 CRM-INQ-01 的整体验收仍有 WP03-04。必需修订 `COM-INQ-GENERATED-CONTRACT-ADOPTION-POS` 与 `COM-INQ-GENERATED-CONTRACT-ADOPTION-NEG` 定位到 W484 的实际运行。当前重新执行 55 处修订与全部 257 处引用闭合、合法键下 201／4001 Unicode 码点拒绝 422、200／4000 成功、原事件结构相等、仅 boolean 扩展及重复键拒绝。终态 90/99 的 null、过期下载证明和错误 ETag 沿 W484 的 W482/W483 证据。 [原 AC 目录](../../assessments/20261011/catalogs/commercial-finance.json) · [必需修订](../../../design-handbook/remediation/08-acceptance-catalog.json) · [W484](../W484-01/DELIVERY.md) · [原范围状态](../../roadmap.json)

## 执行证据及验收边界

- 当前 Windows、PostgreSQL 18.6、.NET SDK 8.0.424、Node 22.22.0、Next 16.3.3、Playwright 1.62.1 与 Chromium。真实 Kestrel、真实 PostgreSQL 和 Next 实际路由使用本地隔离实例。业务、租户、有效策略、身份登记和非联测场景的外部身份响应使用模拟输入。Main 联验使用真实控制器与签名服务和模拟登记；提交丢答在 CRM 已提交之后注入。证据保存在 `D:/CP6-test-artifacts/WP03-02/`。
- CRM 的 `native-browser.trx` 有 124 项通过，含 10 项 React/Chromium/BFF 场景。初选还命中一项需要独立 Main 桥接的用例和一项 PGT-017 专用夹具，分别为缺配置失败与跳过。前者已由实际 Main 发起补跑，父结果为 `main-service-context.trx`，子结果为 `permission-consumption-main-submit/actual-main-submit-*.trx`，各一项通过。后者沿既有 PGT-017 实测保留复用范围。 [PGT 实测](../W645-01/DELIVERY.md)
- `r1-policy.trx` 三项通过。`next-real-browser-edge.trx` 两项通过，含真实页面正常提交及确认丢失恢复，每个场景核对刷新后回执字节、浏览器隔离及实际 PostgreSQL 中的图。前两次现场夹具分别因 Next 路由提醒节点的选择器重叠和跳转后旧响应不可读取而失败。修正夹具后以本地 HTTPS 边缘读取 201 字节，浏览器继续通过正常路由。原失败日志均已保留。
- 共 130 项 CRM 运行用例通过，另有一项 Main 父端联验。生成合同 C# 定向检查 168 项，含 49 个状态。Web 定向测试 47 项，TypeScript 合同测试 82 项。CRM 构建零错误和警告，Main 构建零错误并保有基线警告。实际 Next 生产模式构建、合同生成 `--check` 通过。命令、原始 TRX、构建日志、测试专用夹具和回执摘要均在本卡证据根。
- 完整 PRIV 导出、制品下载、保留擦除及 CRM-INQ-01 整目标终验沿 WP03-04。生产启用、既有数据迁移、正式 broker 和环境准入沿各自原责任。普通 Actions 与部署均为零。

## 远端采用及工作区

CRM 已采用实现以 #143、实际页面及当前错误合同 #198 和上述远端源为核对入口。CP6 的关卡 PR 同时提交本报告、唯一台账和生成状态页。实际合并提交、合并后冒烟、两个分支的准确处置和席位释放由官方 `finish` 与原通信分支的本卡 `DELIVERY` 回读。 [CRM #143](https://github.com/GTX537/CP6.CRM/pull/143) · [CRM #198](https://github.com/GTX537/CP6.CRM/pull/198) · [本卡通信目录](https://github.com/GTX537/CP6.AI-Handoff/tree/codex/dev-handoff-20261117-exec-wp02-14b/handoffs/CP6-DEV-20261012/tasks/WP03-02/messages)

实际区间、8 小时预算及 12 小时强制拆卡点见台账 `WP03-02.card_execution`。此卡核销全部原范围，阶段 A 下一个候选交 COORD 正式选定。 [单卡计划](../../planning/20261117-card-execution.md)
