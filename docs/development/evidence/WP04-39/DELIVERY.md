# WP04-39 Main.C03 伙伴成功结果兼容版本

Main 从 `632494cf4f5dc6b8506d8b0933d2dcd33b269ae1` 领取，受验产品为 `027fa2ced58f501654fb6f952c140ba96b1f3738`。直接前置 WP02-09b、WP04-03 及适用拆分余项完整 DONE。本卡提供 A27 的 Main 成功原件与可复现解码依据。[原派工 #396](https://github.com/GTX537/CP6.AI-Handoff/blob/3cebaf8f622f76011acbe83052046f595260211d/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261122T120635Z-exec-wp02-14b-wp04-39/MESSAGE.md) · [原链 START](https://github.com/GTX537/CP6.AI-Handoff/blob/0ccae81d9a3b97f4b31baa38abc213cb62c6fc38/handoffs/CP6-DEV-20261012/tasks/WP04-39/messages/20261122T121331Z-exec-wp02-14b-start/MESSAGE.md)

## 实际出件和固定原件

隔离 PostgreSQL 18.6 上，以合法预登记伙伴和独立 v1 请求走 Main `POST /internal/erp/v1/events`。伙伴从预登记更新为有效，WAL 核对 Inbox、请求日志、伙伴更新和 Outbox 的行事务均归属同一个外层 COMMIT `1961`。同键再次投递保持一行 Inbox、一行请求、一份结果和一次伙伴更新。具有当前对象读权的 C01/C02 服务两次走正式 v2 GET，均返回 200、同一响应及 `Cache-Control: no-store`。将 `payload` 解码为字节后，与已存 Outbox 和下列文件逐字节一致。[接口与回读](../../../crm/C03-ERP-INTEGRATION.md#含斜线伙伴键的成功原件) · [结构化对账](observation.json)

| 核对项 | 观察值 |
| --- | --- |
| 结果固定路径 | `docs/development/evidence/WP04-39/result.v2.cloudevent.json` |
| 结果 SHA-256 | `27b1f13e8091f578afa09d1b621094029d6be6ac989f1da91250ff8f9ff7058b` |
| 原请求 | [`request.v1.cloudevent.json`](request.v1.cloudevent.json)，SHA-256 `0d1e709f308d7f0331a65b230d754f235e4ce2f92ed718969a59791c9ce5f541` |
| 原请求身份 | `ACC-C03-V1/1.0.0`、`C03-BP-REQUEST-PROOF/1`、来源 `urn:cp6:crm`、epoch `owned-result-source-1` |
| 出件身份 | `com.gtx537.erp.business-partner-synchronized.v2`、`ACC-C03-V2/2.0.0`、来源 `urn:cp6:erp` |
| 事件键及时间 | `a46de4f28a984fff853fdcd862b37672`，`2026-11-22T12:42:31.0119463Z` |
| 租户、Account、原请求 | `20e35ae4-d441-4c10-aa72-f5a23687987f`、`0d9e86bd-f0fc-483d-ad5d-78fa72a3d779`、`6f85f32b-1fbb-4a19-a8f6-633b712549ca` |
| 原请求消息键 | `66c1d964082943f09432d368209534d8`，与结果 `causationid` 相等 |
| 业务版本 | `requestVersion=3`、原命令 `aggregateVersion=7`、`resultVersion=1` |
| 查证协议和解码 | `C03-BP-RESULT-PROOF/2`，`ErpEventValidator.DecodeBusinessPartnerSuccessV2` 返回 `BP-2026/0007` |

合并前的固定原件可从受验产品 `027fa2ced58f501654fb6f952c140ba96b1f3738` 的同一路径取回。解码时，用 `ErpEventValidator.Load` 加载 `contracts/events/erp`，复算原字节 SHA-256 后，将字节传给 `DecodeBusinessPartnerSuccessV2`。合同包 SHA-256 `ab1778b6b08b57f8976e7b707cb24324934c951c9189582318c0a3dd66328528`，schema SHA-256 `307ea9a9ba2888d360eacacb362ffef26838f2323547f71b713fa482a4d48991`，查证合同 SHA-256 `2935a6033a78e20c3a53214e487eb2b4e220d8430f5f574870577fdb74c14006`。[成功原件](result.v2.cloudevent.json) · [合同包](../../../../contracts/events/erp/business-partner-synchronized/v2/contract-bundle.v1.json) · [查证合同](../../../../contracts/erp/business-partner-result-proof/v2/contract.json)

## 正反验收

| 场景 | 实际结果 |
| --- | --- |
| v2 斜线键、前导零、大小写 | 成功原件解码为精确字符串 `BP-2026/0007`。契约测试核实混合大小写、键长度范围和非法路径。 |
| v1 合法键与斜线 | `0007-BP`、`0008` 按原值接受，含斜线的键拒绝。已有成功和失败样本继续经 v1 查证和 Outbox 重投递。v1 查证响应保持原字段，冻结合同逐字节相同。 |
| 查证与结果版本错配 | v1 入口查 v2 原件、错误业务 `resultVersion` 均 404；合同、schema 和数据版本错配在解码校验中拒绝。 |
| 原请求、原件或摘要不可证 | 缺原请求、缺 Outbox、请求摘要不匹配和已存摘要不匹配均 404。 |
| 错租户及对象权限 | 租户头与令牌错配 400。未授对象读权、错 Account 和撤对象读权后同原键查证均 404。 |
| 披露及业务效果 | 所有拒绝均无原字节、伙伴键和摘要。查证和拒绝后仍为一份原结果、一项伙伴效果；无新命令或结果。 |
| 伙伴 Owner 与旧写者限制 | 前置 WP04-03 的 C03/I02 权威映射和替换回归通过。WP04-02 已切换旧写者仍不能改变伙伴资格。 |

## 复现与核销

- 本地基线 v1 定向 78/78。测试先证实旧产品在实际入口遇该斜线示例返回 `RETRY`，再验证兼容路径。最终 Main 定向 103/103，覆盖 v1/v2、实际 HTTPS、Owner 及已切换旧写者回归。独立夹具的原请求查证 1/1。最终运行结束 `2026-11-22T07:42:46.4659441-05:00`。合同样例数及事务对账、数据库隔离问题修正后复跑，两类 PostgreSQL 夹具分别运行。
- 用 .NET 8 和隔离 PostgreSQL 18，设置 `CP6_PERMISSION_PG_BIN`、`CP6_PERMISSION_PG_ROOT`、`CP6_PERMISSION_PG_PORT` 及 `CP6_C03_COMPAT_EVIDENCE`，运行 `dotnet test CP6.Tests/CP6.Tests.csproj`。本次筛选 `ErpEventContractTests | ErpRuntimeOptionsTests | Account_C03_compatible_success | Account_C03_registered_service | Business_partner_wp04_02_legacy_crud_commerce_and_C03 | Business_partner_wp04_03_actual_C03 | Business_partner_original_proof_matches_real_commands_and_denies_unverified_sources`，`candidate-final.trx`、`original-proof.trx`、夹具日志及停止检查均在 `D:/CP6-test-artifacts/WP04-39/current-20261122`。每次运行生成独立的 UUID、时间和 SHA-256。[实际运行用例](../../../../CP6.Tests/Platform/IamAccountC03CompatibilityPostgreSqlTests.cs)
- 使用实际 Main HTTPS 请求处理、身份发布、凭据签发、对象读权、事务和 Outbox。测试环境模拟 Dapr 入站头、原请求发送方、预切换租户及合法伙伴、授权与撤权，摘要故障只注入该轮隔离库。v2 通过指定租户清单启用，生产清单默认空。正式冒烟以远端采用树运行并记录在原链 DELIVERY。
- #396 授权由 WP04-03 划入 9984.096 秒、WP04-02 划入 4415.904 秒，合计 14400 秒，21600 秒强制拆卡。按 #395 的原 DELIVERY 查重，WP03-29 单次补记 220.714 秒，累计 1575.673 秒，原目标核对 0.520 秒单列。[预算授权 #396](https://github.com/GTX537/CP6.AI-Handoff/blob/bf469ca177ceb00927949aeb9140a279efa79bed/handoffs/CP6-DEV-20261012/tasks/WP03-06/replies/20261122T120526Z-coord-main-c03-compat-authorization/MESSAGE.md) · [WP03-29 签收 #395](https://github.com/GTX537/CP6.AI-Handoff/blob/33872f98e4c51015b71ff96b39316319d453d574/handoffs/CP6-DEV-20261012/tasks/WP03-29/replies/20261122T115937Z-coord-acceptance-ack/MESSAGE.md)
- CRM-ACC-01 为原 AC 23/47、必需运行修订 5/10。A27 仍为 `NOT_RUN`，实际入史、原键恢复、消费显示及批准落点由 CRM WP03-06b 另行派工。A13 归 WP03-07。[状态台账](../../roadmap.json) · [单卡执行计划](../../planning/20261117-card-execution.md)
