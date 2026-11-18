# WP02-06 CRM 身份来源采用及会话恢复

WP02-06 整卡 DONE。CRM 在原租户、原 PostgreSQL Store 采用 Main 批准的新来源，从准确版本读取完整目录并保留原字节。切换后的旧会话无法继续或重建，新会话按当前权限读取同一业务结果。CP6 在本 DONE PR 复核 SYS-TENANT-01 原 23 条验收，结论为全部通过。

## 开工与交付范围

- 主线开工为 CP6 `56feafdc99d5757d678830928c69a5416106f920`、CRM `5f09533b9168ee614a6df1f75629b39635585f86`、Platform `f3fbe4be67d40641f7a8a969d74be65a95541819`。WP02-05 含余卡、WP02-07 的 PostgreSQL 调用方 Inbox 均完整 DONE。Main 发布已由 #1247 采用，Platform 参与者证据由 #61 采用。 [WP02-05b](../WP02-05b/DELIVERY.md) · [Platform #61](https://github.com/GTX537/CP6.Platform/pull/61)
- 按原任务定义，重复、旧版、同版消息在技术早返前走 precheckpoint，保留权威向量及原报文。使用 CRM 自有 PostgreSQL，共同持有业务最终授权和撤权 fence。正向核对准确重建、重登录后当前读权、原结果及组织切换清理。反向核对漏项、错源、旧版和同版异内容，以及旧会话无法复活。对应 INT-ID-01 的 AC-ID01-01/04/05/06、AC-ID05-02/03 和 SYS-TENANT-01 的 AC-TN04-04。 [原任务定义](../../task-definitions.json)
- CRM 源 `9a07b96409eb4a98df22064c784b2fa9fb7a4dff` 已由 [CRM #239](https://github.com/GTX537/CP6.CRM/pull/239) 合入 `ac8a2706a018317eafce8247aff1e7fe5e416329`。Main 已批准发布的实现源为 `effeac4f80c6576f82d92343b18e49ba335b9f8b`，本 PR 的跨仓驱动源为 `dea4e2ca3aa9fc450c09190e08eb797e81c75449`。本次 Main 业务实现沿用已确认远端版本。 [跨仓测试入口](../../../../CP6.Tests/IdentitySourcePublicationHttpPostgreSqlTests.cs)

## 实际入口和 PostgreSQL 边界

1. 在已采用原生身份发布的 CRM 数据库，以原迁移角色显式运行 `PostgreSqlIdentitySourceAdoptionV1.InstallAsync`。为身份写者授予 `crm_identity_baseline_v1."SourceAdoptions"` 的 SELECT/INSERT。来源改动与历史报文均由 PostgreSQL 守卫保护。普通业务会话角色沿用原租户绑定及最终授权检查。
2. 变更经 Main 平台管理员批准后，为对应 `CrmInquiryNative:Tenants` 项配置 `IdentitySourceRevision` 和批准接口原始响应的 `IdentitySourcePublicationSha256`，同时使用批准后的 issuer、region、reader 登记。哈希取原 UTF-8 响应字节。租户 GUID、Store 身份和 C02 合同包使用原绑定。已有登记按版本顺序采用。 [Main 批准接口](../WP02-05b/DELIVERY.md)
3. CRM 后台使用现有绑定 reader 凭据访问 `GET /internal/crm/identity/source/v1/publications/{revision}`。核对可信租户、准确版本、批准前后登记、完整数量、快照摘要及原来源。未知版本或缺漏直接失败。旧来源、过期版本、同版异内容及本地超出该发布水位的事实拒绝推进。
4. PostgreSQL 在同一 Serializable 事务中持有原七锁和会话 fence，保存完整发布原件、前后来源、边界、水位、历史撤权与原 Outbox；更新有效登记、生成完整选择与当前发布并封存旧会话。新签发令牌按批准时刻的下一整秒起接受。库端保护覆盖并发写会话。新配置按当前登记处理事件，旧报文仍可追溯。未知提交按已持久化版本及 SHA 恢复确认。
5. 已采用后继续核对当前完整目录。只有首末可信目录均确认准确元组时，重用已封存旧 issuer 的撤权原字节。旧批准原件和原会话撤权事实随原身份保留。业务原配置保持同一租户和对象 GUID，按当前身份、用途和持锁授权读取。

## 验证

Windows 本机，.NET SDK 8.0.424，PostgreSQL 18.6，CRM 57306 与 Main 57304 为任务独占实例。模拟账号、租户、业务、审批和密钥。Main 实际运行 Program 登录、批准、授权码与服务令牌签发，并用实际 C02 控制器提供准确版本。在读端显式使用测试公钥。CRM 原生后台在独立进程通过真实 HTTP 消费，真实 PostgreSQL 核对旧 JTI、原始发布 SHA、完整项目、后继目录、同 TenantId、当前 issuer/region 以及新会话可写入。独立 CRM Kestrel 用例采用模拟 OIDC/权限签名，核对旧 Cookie 401、合法新会话原业务响应逐字节相同及原业务行数。

| 本地结果（`D:/CP6-test-artifacts/WP02-06`） | 实际覆盖 |
|---|---|
| `crm-final.trx` | 74 通过，零跳过。精确取版失败关闭、同版冲突/原重送、原结果、旧 Cookie、新会话及基线、接收、鉴权、切组织和撤权回归 |
| `crm-refresh-final.trx` | 1 通过，零跳过。已采用后读取新水位，复用原撤权字节 |
| `actual-main-3/main.trx`、`actual-main-3/crm-actual-main.trx` | 两仓各 1 通过，零跳过。Main 真实批准和签发 → CRM 真实读取、采用和会话封存 |

两项目本地构建通过。复用 WP02-05b 的 Main 发布基线证据及 W131/W246 的原 precheckpoint、实际业务与撤权双序、未知提交恢复。W253 的原登录切组织继续作为该场景的实测。首个红测显示原 CRM 固定来源拒绝变更。联跑修正旧测试账号的认证代次和历史撤权来源断言，原失败按实际结果保留。未执行生产环境切换、既有业务数据库迁移或生产部署。本轮跨仓走本地 HTTP，实际 Dapr/Kafka 证据引用 W627。

复现使用 `tests/CP6.CRM.IdentityPostgreSqlTests/pg_resource.py` 准备独立 CRM 实例，设置 `CP6_CRM_PG_SETTINGS` 后运行相关 VSTest 过滤器。双仓父测试筛选 `Approved_source_publication_keeps_tenant_exact_directory_and_original_command_over_http`，另设 `CP6_WP0206_CRM_ASSEMBLY`、`CP6_WP0206_RESULTS`、`DOTNET_HOST_PATH` 及 Main 现有 `CP6_PERMISSION_PG_*` 隔离实例参数。短期凭据由父测试直接交给子进程。

## SYS-TENANT-01 原验收

按原验收目录核对 23 条断言及其固定来源。该目标无必需修订运行场景。WP02-02、WP02-04 已完整交付。本次完成 WP02-04 留下的 AC-TN04-04 跨系统缺口，并逐条复核原证据及两仓远端采用。 [原验收目录](../../assessments/20261011/catalogs/foundation.json) · [WP02-04 前次核对](../WP02-04/card-closure/DELIVERY.md)

| 原 AC | 实际证据 | 判定 |
|---|---|---|
| AC-TN01-01 | 租户、管理员、角色菜单和原回执同事务。 [W265-01](../W265-01/DELIVERY.md) | PASS |
| AC-TN01-02 | 实际边界拒绝空值、长度、重码、时区与部分保存。 [W264-01](../W264-01/DELIVERY.md) · [W265-01](../W265-01/DELIVERY.md) | PASS |
| AC-TN01-03 | 同操作恢复原租户，异义冲突，回查保护临时密码。 [W265-01](../W265-01/DELIVERY.md) | PASS |
| AC-TN01-04 | 真实并发与 ABA，旧版 412，原 Id/Code 保持。 [W264-01](../W264-01/DELIVERY.md) | PASS |
| AC-TN01-05 | 平台、普通、模拟会话各自通过当前权限边界，首管理员为租户身份。 [W265-01](../W265-01/DELIVERY.md) · [W529-01](../W529-01/DELIVERY.md) | PASS |
| AC-TN01-06 | 历史审计缺陷的实际红绿与当前原子失败分别留证。 [tenant-audit](../WP02-04/tenant-management-audit/DELIVERY.md) · [W264-01](../W264-01/DELIVERY.md) · [W265-01](../W265-01/DELIVERY.md) | PASS |
| AC-TN02-01 | 库存与暂停双序、CRM 与 Main 的当前共同门均有真实事务证据。 [W435-01](../W435-01/DELIVERY.md) · [W649-01](../W649-01/DELIVERY.md) · [W645-01](../W645-01/DELIVERY.md) | PASS |
| AC-TN02-02 | 合法当前权读取暂停前原结果；旧会话的新效果被拒。 [W529-01](../W529-01/DELIVERY.md) · [W649-01](../W649-01/DELIVERY.md) | PASS |
| AC-TN02-03 | 恢复推进权威版本，需要新登录；未确认的 CRM 新效果保持关闭。 [W264-01](../W264-01/DELIVERY.md) · [W649-01](../W649-01/DELIVERY.md) | PASS |
| AC-TN02-04 | 跨库确认缺失时新许可关闭，合法历史读取沿原门。 [W649-01](../W649-01/DELIVERY.md) · [W645-01](../W645-01/DELIVERY.md) | PASS |
| AC-TN02-05 | 固定原操作键，未知提交回查原件，未观察不代表未执行。 [W264-01](../W264-01/DELIVERY.md) · [W649-01](../W649-01/DELIVERY.md) | PASS |
| AC-TN02-06 | Enable 与过期日期保独立字段，后台须当前批准。 [W264-01](../W264-01/DELIVERY.md) · [W529-01](../W529-01/DELIVERY.md) | PASS |
| AC-TN03-01 | 普通 A/B 角色绑定保护 Owner 归属，API/浏览器拒绝选择头。 [W426-01](../W426-01/DELIVERY.md) · [W514-01](../W514-01/DELIVERY.md) | PASS |
| AC-TN03-02 | 缺失、未知或伪造 tenant、issuer、region 在连接前拒绝。 [W514-01](../W514-01/DELIVERY.md) | PASS |
| AC-TN03-03 | 正式写者清单与已采用可信入口可追溯，保留历史兼容入口的实际范围。 [W645-01](../W645-01/DELIVERY.md) · [current-entry](../../authorization/current-main/README.md) · [W514-01](../W514-01/DELIVERY.md) | PASS |
| AC-TN03-04 | 实际登录切组织，旧 Cookie 查询和写入都返回 401，新 B 查 A 返回 404。 [W253-01](../W253-01/DELIVERY.md) | PASS |
| AC-TN03-05 | 未知和他租对象统一拒绝，平台管理仍需当前三道闸。 [W516-01](../W516-01/DELIVERY.md) · [W265-01](../W265-01/DELIVERY.md) · [W529-01](../W529-01/DELIVERY.md) | PASS |
| AC-TN04-01 | 真实 Main、CRM 及服务主体/来源配置一致，采用先后均有运行记录。 [W131-01](../W131-01/DELIVERY.md) · [W649-01](../W649-01/DELIVERY.md) · [W627-01](../W627-01/DELIVERY.md) · 本轮双仓及 CRM 回归 | PASS |
| AC-TN04-02 | 缺目录、错源、旧版或内容不完整拒绝发布，已注册来源不被覆盖。 [W130-01](../W130-01/DELIVERY.md) · [W131-01](../W131-01/DELIVERY.md) · 本轮双仓及 CRM 回归 | PASS |
| AC-TN04-03 | 同版异摘要先在正式 precheckpoint 隔离，保留原摘要并撤当前发布。 [W131-01](../W131-01/DELIVERY.md) · 本轮双仓及 CRM 回归 | PASS |
| AC-TN04-04 | 普通改名保原 Id；变更 slug、issuer、region 和 reader 须批准并按准确新来源采用。旧会话封存及原业务结果经实测。 [W264-01](../W264-01/DELIVERY.md) · [WP02-05b](../WP02-05b/DELIVERY.md) · 本轮双仓及 CRM 回归 | PASS |
| AC-TN04-05 | 恢复、重建及原报文重放维持撤权和旧会话拒绝，原业务归属和原件可追溯。 [W649-01](../W649-01/DELIVERY.md) · [W253-01](../W253-01/DELIVERY.md) · [W627-01](../W627-01/DELIVERY.md) · 本轮双仓及 CRM 回归 | PASS |
| AC-TN04-06 | 实际交付分别核对代码、登记、启用、运行，原生消费者和恢复目标使用唯一的当前写者。 [W131-01](../W131-01/DELIVERY.md) · [W645-01](../W645-01/DELIVERY.md) · [W627-01](../W627-01/DELIVERY.md) · 本轮双仓及 CRM 回归 | PASS |

## 同次核对 INT-ID-01 与 SYS-IAM-01

WP02-06 是两项目标覆盖表上的最后一张实施卡。已核对原 INT-ID-01 的 35 条、SYS-IAM-01 的 40 条验收，以及两者各 2 条必需修订运行场景。原生身份边界的实际证据覆盖本卡出口。目标按下列现有任务继续保留精确核对。 [覆盖表](../../02-target-coverage.md) · [原验收](../../assessments/20261011/catalogs/foundation.json) · [修订验收](../../../design-handbook/remediation/08-acceptance-catalog.json)

| 目标 | 后续卡 | 待补证据 |
|---|---|---|
| INT-ID-01 | WP02-09 | 实际 CRM 入站业务 Owner 的原图、原回执、同一事务 Inbox/Outbox 与最终授权，继续核对重复原交易、同键异摘要、无门阻断及未知提交恢复。对应本目标两条必需修订。 |
| INT-ID-01 | WP02-24 | 原 CRM 商业结果的查询与受控重放，须用实际 Owner 的准确原键、当前字段决定和披露权核销。 |
| INT-ID-01 | WP14-08 | 全链实际 CRM 业务 Owner 的字段用途及撤权竞态尚需组合验收。覆盖导出、排序、重放和隐藏字段写拒绝后的零部分提交。 |
| SYS-IAM-01 | WP13-04 | 实际 Space S5 成员 grant-clause 交集及 61 字段/78 DTO 映射须按原范围采用并验证；两条必需修订仍开放。 |
| SYS-IAM-01 | WP14-08 | 跨域已采用 Owner 的身份切换、撤权和业务未知提交须实链对账，按当前权限及原业务键验证恢复与零重复效果。 |

## 分支、工作区与预算

本卡分仓串行推进，CRM 产品 PR 与 Main DONE PR 各用 `codex/wp02-06`，共用本报告。两仓候选工作流与各自已确认的远端主线内容一致。普通流程手动，Main R2 候选仅版本 tag。本卡运行、重跑、取消 Actions 均为零。DEV-2 原有依赖缓存保存在 `D:/CP6-worktrees/.retained-caches/CP6-dev-2-cp6-web-node_modules`。CP6 DEV-1 和 CRM DEV-2 依官方 `main-finish.json`、`crm-finish.json` 核对远端采用、准确分支处置和席位释放。

原号 DONE / 完整 DONE 为 **48 / 46**，登记卡 **50/379**，目标 **4/125**（含一项 TEST_ONLY）。本窗关闭一张卡、一项目标，新增拆卡 0。技术窗口从 `2026-11-18T02:05:09.397969+00:00` 到 `2026-11-18T02:25:40.6049326+00:00`，计 1231.207 秒，低于 M 卡 4h。三个目标核对计 147.558 秒，累计目标核对 0.629h。根据台账候选与官方 finish 的实际时间戳，结转 WP02-13b 的正式收尾 262.086 秒，计划累计 7.037h。未到 24h 检查点，缓冲消耗为零。

下一卡 **WP02-09**，已按严格前置核对，全图最长下游链 42 条边。本候选之后的合并与席位收尾时间随下一张卡记录。
