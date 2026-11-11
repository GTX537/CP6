# W482-01 · 首条询盘业务链验收与实际缺口收口

状态：限定导出实现及相关验收通过，正常采用/生命周期收尾中；完整原任务和 CRM-INQ-01 不提前关闭。COORD 单对话执行，属于 [12h 计划](../../planning/20261111-codex-12h-business-chain.md)。Main 协调分支 `codex/w482-01` / DEV-1，基线 `cfdde8f8f2bf8dea457bb692962e41e479202eb4`；CRM 实施分支 `codex/w482-01` / DEV-2，基线远端 main `0cd83eb09826552fc1dc8d631a7c82484ec40363`。两个已登记开发席位用于同一跨仓任务，无额外工作树、无并行实施代理。

## 当前准入与范围

用户已接受首条业务链完整验收，COORD 本次先执行现有原生询盘链和原验收差距盘点，随后在本任务内补直接缺口。原目标 CRM-INQ-01，关联原任务 WP03-02、WP03-04；WP03-01/03-03 的已交付部分及现有有效证据复用，不把本窗口领取或测试通过直接改称整卡完成。

读取的是 CRM 最新远端源码和其 AGENTS，而非落后 24 提交的主目录工作文件。实际入口是 `src/CP6.CRM.Api/Inquiry/CrmInquiryNativeHostV2*` 和相应原生 Owner；不改旧 `CrmBusinessService.Intake.cs` 来替代原生入口采用。现有配置/公开 Attempt/Submission/H01/审核/同意/Retention、签名权限与原结果组件已存在，Platform 0.10.3 按原合同采用。

公开业务的真实调用链按既有 public route、签名服务/人工上下文、CRM PostgreSQL 当前授权及共同事务执行；无业务消息的公开 HTTP 不凭空增加 Inbox。共同 NUM/FILE/PRIV 条件绑定按实际调用和准确合同核对；未交付的真实提供者不得用成功形状的空对象代替。允许构造明确的模拟政策、日历、身份/密钥和外部服务，在相同契约下执行真实应用/数据库场景。

本窗口允许 CRM Inquiry Owner、原生存储/SQL、相应 API/前端和定向测试中为本目标必要的修改；共享合同继续采用已接受 R1，不另造权威 DTO 或放宽字段。2026-11-11 实际请求已确认导出申请 404，原有两条模拟生产业务验收基线通过。为补齐原合同中后台生成步骤，协调范围扩展至 Main 的询盘服务签名上下文及其当前隐私权限来源：新增明确的 v2 服务上下文入口，仅承载原 R3 的 `privacy:export`；既有 v1 的三个目的和签名字节域保持兼容，原 R3 业务 DTO、权限、字段及存储精度不变。后台必须使用自己的当前服务身份及完整导出权限，不能借用 retention-run 或原申请人的旧会话。Main 同时维护本任务报告、roadmap、生成页及四状态文件。仅有 COORD 编辑，Main 授权签名来源、CRM 原生询盘模型/SQL、配置和生成资产串行独占。未授权发布、既有环境切换或部署。

## 执行队列

2026-11-11 补充的文件表示决策：冻结 R3 的 `GET .../download` 默认 `application/json` 仍返回原 `PrivacyDownloadView`。为交付实际资料文件，同一路径显式协商 `application/vnd.cp6.inquiry-privacy-artifact.v1+json`，返回存储时生成的相同 UTF-8 制品字节，包含明确版本、租户/Submission/Job/Artifact 身份及冻结政策允许的字段；无 JSON 重写、精度转换或 Owner 改变。两种表示共用原 `privacy:download` 当前权限和同一一次性凭证。文件表示使用 BFF `inquiry-v3`，在原 v2 基础上绑定 Accept 和 X-Download-Proof；既有 v2 调用保留。此为同任务的必要浏览器/BFF 采用，CRM 原生存储与 BFF 传输资源由 COORD 独占串行编辑。

- [x] 将原 60 项 AC 及 R1 两项修订映射到实际入口、现有成功证据和真实缺口；以业务行为为单位复用，不按断言数核销。
- [x] 复用符合相同输入的已执行场景；在隔离 PostgreSQL/本地 HTTP 上执行必要的现有完整链，覆盖公开提交、审核、重复/未知、当前授权、消息及隐私。
- [x] 实际原生 privacy-export 申请先以 404 失败；补齐 Owner/存储/当前服务授权/文件后定向回归。新增行为的失败与修正均保留。
- [x] 使用实际 React、HTTPS BFF、原生 HTTP/PG 验证文件下载及失权/未知/晚到；模拟外部系统只影响其输入和响应，不伪造业务 Owner 或数据库成功。
- [x] 一次任务级集中 SPEC/QUALITY 审查通过；修正 R1 终态和后台内部超时后只复查相关变更/对应回归。原卡及目标仅在完整条件满足后核销。
- [ ] CRM/Main 分别正常合并并核对远端 main，分别 finish，更新同一报告的实际范围和未运行项。

## 前项采用与当前状态

W481 已随 Main #949 正常合入 `cfdde8f8f`，源提交 `531364174`；本地及远端任务分支准确清理、DEV-2 已干净释放后安全 bind 为 CRM。其 PostgreSQL/验证进程均退出，普通 Actions 为空。[W481 报告](../W481-01/DELIVERY.md)。

原 375 项 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN；CRM-INQ-01 的原/修订场景状态暂保持原值，待完整映射后按真实覆盖核销。记录与原始日志集中在 `D:/CP6-local-test/CP6-12H-20261111/W482-01`。分支：两仓实施中；工作区：DEV-1 Main / DEV-2 CRM；席位：两开发席位已领取，集成席位空闲。

## 已执行结果与当前待验

新增原生导出申请/后台生成/状态/下载/保留清除、Main v2 独立服务权限来源及实际页面。旧合同和安装默认不变；实施、安装/权限说明位于 CRM `docs/handbook/12-SECURITY-AND-PRIVACY.md`。实际政策、日历、IAM/密钥和外部消息输入明确模拟；应用、PostgreSQL 18、TLS、React/BFF、事务及制品字节实际执行。

| 证据目录/文件（上述私有根） | 实际结果及适用范围 |
|---|---|
| `export-08/results.trx` 有效子集 | 7/8 PASS、0 skipped：原 202 回执、同键恢复、加密文件、状态 20→30、错误凭证/并发一次消费、五类不完整当前人工权限拒绝。同次消费者一项因 Effects schema 中连字符导致 42601 失败；修正明确 schema 后，其最终成功见下列 `browser-and-consumer-01`，不把本批整体记为通过。 |
| `browser-network-05`、`browser-network-06` | 1+3 PASS、0 skipped：真实回环 HTTPS BFF、原生 HTTP/PG、实际后台。正常下载、202 丢失原请求恢复、拒绝入口隐藏、晚到文件在失去权限后清除；桌面/手机截图及无横向溢出检查。 |
| `browser-and-consumer-01/results.trx` 中模拟生产验收场景 | 1 PASS：公开提交→H01/审核/同意→实际导出文件→五类原生 route→持久模拟 broker/消费者；保留该次另四项浏览器夹具 CSS 错误记录，不将整次运行称为全绿。 |
| `web-export-green-01`、`web-client-02` | 13 项早期相关 Web 行为及其后最终新增 5 项行为通过；重复覆盖不累计成独立验收数。原命令/结果日志保留。Next typegen 与 Web TypeScript 检查通过。 |
| `main-export-green-03/results.trx`、`main-export-green-04/results.trx` | 10 项 Main 受影响权限/旧 v1 兼容检查 PASS；跨仓一项最终另次 PASS、0 skipped，含独立 CRM 子进程 1/1 PASS。真实 Main TLS v2、CRM 当前 C02 服务源、20→30/文件下载及 Main 实际撤销 export 动作后的拒绝；旧失败保留。 |
| `export-failures-01/results.trx` | 4 PASS、0 skipped：申请末段 23514 故障使 Job/原结果/Submission 版本/审计/Outbox 全回滚；捕获后会话变化拒绝下载且不消费 proof；签名后服务源停用不生成文件；不同 Submission/Job/proof 错绑拒绝。 |
| `expiry-02/results.trx`、`privacy-expiry-corrected.json` | 1 PASS、0 skipped，真实等待五分钟：旧凭证 410、20→30→99、制品/凭证共同清除，公开终态 expiry/artifact/proof 全 null，内部原 expiry/完成时间保留；原申请恢复原 202，不重建 Job。 |
| `review-regressions-01/results.trx` | 5 PASS、0 skipped：原申请增加同键改 If-Match 冲突；并发同键共用一个 Job、异键只有一个 CAS 赢家；真实 PG 拒绝 40/50、缺时钟的 30 和提前删除制品；后台内部 30 秒超时后继续存活并完成同一 Job。 |
| `shared-ui-regressions-01/results.trx` | 8 PASS、0 skipped：受影响 IntakePage 实际 React/BFF/PG 的放行、原丢答审核恢复及六个既有同意场景，新增隐私区默认关闭时不破坏原行为。 |
| `elapsed-01/results.trx`、`privacy-elapsed.json` | 1 PASS、0 skipped。真实原最低 60 分钟 NeedsReview 保留场景：从 16:41 到原截止 17:41:07Z 后处置；同意撤回不改期、另一到期行 legal hold 不改期、制品 DELETE 23514 使全部 PII/版本/导出原子回滚，移除准确自有故障触发器后实际联合清除，原回执 proof/replay 也已墓碑/脱敏。 |

Main 完整默认编译在未受影响的约 60.3 MiB 历史 SQL Server migration designers 发生 Roslyn OOM，原 `main-export-red-01..03` 日志保留。定向检查使用私有 MSBuild targets，仅从该次组件编译中排除历史设计器及依赖其类型的四项无关迁移测试；实际受影响 Main 生产代码、当前 PostgreSQL 模型/迁移和 HTTP 测试仍编译执行。没有修改仓库构建配置或历史迁移，不将此结果称为全仓构建通过。

初次真实导出请求 404 的 RED、权限来源不足、字段只读语义修正、原生 Effects schema 名称及真实浏览器来源夹具失败均保留。最终报告引用适用成功证据，不删除失败、不把错误夹具结果算成业务通过。新增行为与相关故障/边界均已定向验证，未变化输入按上表/历史报告准确复用；提交或 SHA 改变不重复相同执行。

实际五分钟验证的首次 `expiry-01` 揭示真实终态错误：原文件已清除，但公开 99 携带内部 expiry，被已接受的 `CRM-INQ-TERMINAL-PROJECTION/20261013-R1` 拒绝。修正只在 30 输出公开 expiry，99 隐去公开字段并保留内部原时钟；`expiry-02` 完整实时间隔复验通过。没有修改或绕过严格 decoder、原归档、R1 overlay 和历史 expected。

集中审查中另定位后台内部截止的未处理取消：原 30 秒 Owner 截止触发时 stoppingToken 仍有效，旧 worker 的 ExecuteTask 结束。`worker-timeout-red-01` 用实际 PG 行锁与原 30 秒截止复现，不改数据库/业务时钟；只关闭该测试驱动的竞争超时。修正明确捕获该可恢复扫描取消，记录无秘密的中性日志，下一扫描重读原 Job；不判提交失败、不另建任务。中间 `worker-timeout-green-01` 仅在夹具 finally 重复 Rollback 出错，保留失败；修正夹具释放跟踪后 `review-regressions-01` 完整通过。没有扩大为全仓重新审查/测试。

## 原 60 项 AC 与 R1 对照

下表是逐条覆盖核对，不把历史设计文本/类型测试直接算成整条业务通过。W482 的新成功和旧成功复用见上表；`Wxxx` 为 Main 同名 `../Wxxx-01/DELIVERY.md` 及其链接的 CRM 唯一产品报告。既有完整配置由 [W255](../W255-01/DELIVERY.md)、完整审核由 [W310](../W310-01/DELIVERY.md) 核销；两者不是本轮重跑。这里的“已有覆盖”仅说明列出的实际范围，正式原 AC 暂不批量改为 PASS；须按各条完整条件与 R1 共同核对。

| 原 AC-INQ | 当前入口与有效行为证据 | 完整核销时仍须注意的边界 |
|---|---|---|
| 001 | 原生 Public Owner / Submission / Receipt；W117/118、W234/247，本轮原两条基线与五 route 贯通 | 包含公开浏览器状态的全条件核对，不能只看自动 H01。 |
| 002 | 三维 Rate、原 Submission command；W107/117/234/247 | 配额拒绝、重复业务与同槽冲突分别核对。 |
| 003 | 原生 IntakeDecision / ReviewExpiry；W121/253/256/259，W310 完整审核采用 | 新 privacy 不是重做审核。 |
| 004 | Configuration Owner / lifecycle；W102/105/247，W255 完整卡 | 旧 Attempt、相邻区间、后继回退保持。 |
| 005 | 上述公开/H01/配置入口和冻结接口映射；原模块/P01/H01/SC01/DP08/DP10 | 业务映射不提升共同服务的采用范围。 |
| 006 | PublicInquiry 页面与 receipt；W118 原真实浏览器/BFF/PG | 本轮未把所有公开 FE 分支称为新执行。 |
| 007 | IntakePage 队列/详情/审核；W122/310，W482 实际页面增加独立隐私区 | 原审核状态和隐私互斥的受影响回归按本轮检查。 |
| 008 | Configuration 页面/preview/activation；W104/105 | 未变化的配置页面证据复用。 |
| 009 | 冻结 38 DTO、严格 codec、R1 有效版本；S01-08，W482 实际 HTTP decoder | 新文件为明确附加 media/version，默认元数据 DTO 不变。 |
| 010 | 浏览器 receipt Owner 及原生 intake/current-auth；W117/118/120 | 公开五字段不能泄露内部 Lead/Submission 身份。 |
| 011 | Submission 原结果；W117/234/247；本轮再次走实际原链 | 同 attempt/NFC 等价输入恢复原字节，不新增 H01/配额。 |
| 012 | 原命令指纹比较；W117；新 export 原槽 | payload/hash 不进入槽唯一性；需包含新申请 If-Match 冲突。 |
| 013 | Public Submission→H01 staged graph 同一 PG UoW；W117/234、本轮贯通 | 保存阶段不是 commit，无中间可读图。 |
| 014 | Intake release→H01、原结果与全效果；W121/253/310 | 当前原审核完整范围复用。 |
| 015 | Intake reject 无 Lead；W121/310 | 原事件与审计一组提交。 |
| 016 | 当前 Human/Service、Capabilities 与各 Owner；W120/121/128、本轮五拒绝+Main 九拒绝 | 缺字段即拒绝；当前资源、部门/Site/Form 与字段同时满足。 |
| 017 | 冻结严格验签、durable trust/current source；W59/87/119/128，本轮服务/会话变化拒绝 | 当前上下文不是永久授权，不额外重复资格包装。 |
| 018 | 原审核同/异动作并发；W121/253/310 | 新导出不改变审核终态竞争。 |
| 019 | 原七锁 profile、Submission/Job/Outbox 锁序与有界恢复；W117/121，本轮新增 Owner | 实际行锁超过原 30 秒截止后 worker 存活，释放锁后同 Job 完成；未改业务时钟。 |
| 020 | 原结果绑定；W117；W482 两 Submission/job/proof 错绑拒绝 | 只接受当前可信租户/主体下的实际资源。 |
| 021 | 原保留/同意回执与 replay 擦除；W124/127/259；W482 联合删除出口 | 新制品删除和公开 99 不披露秘密分别验证；不能以状态字段替代实际清除。 |
| 022 | 原公开/审核/同意丢响应；W117/121/127/234；W482 实际 202 丢答原申请恢复 | 每次保留原 key/body/If-Match；不把未知提交当失败重建。 |
| 023 | 原生 Outbox/publisher 原 run；W133/234，本轮新增第五 route 进入持久模拟消费者 | broker 接收、消费者效果与 CP6 已发送收据分别判断。 |
| 024 | 原 Configuration preview；W105/255 | 不发 Attempt，不开启公开生效。 |
| 025 | frozen IssuedAttempt / bundle；W106/117/247 | 新激活不更改旧 Attempt 原政策/日历。 |
| 026 | Configuration successor/history；W105/255 | 回退创建后继，不覆写旧版本。 |
| 027 | 完整版本 Policy；W102/105/117/247；本轮 TTL=5/10080、合法最低保留=60 | 不缩小合法数值/日期，也不通过缩短业务期限加速验收。 |
| 028 | Frozen bundle/ConsentFact/receipt；W117/127/247，本轮导出冻结 policy fields | 原 notice/hash/consent 与撤回时钟保留。 |
| 029 | ReceiptProof/browser/source live predicate；W117/118/120/127 | 原同形 404 与时间类别不以新 export 的 403/410 替代。 |
| 030 | 原 Retention Owner；W124/127/259；W482 真实 NeedsReview 60 分钟联合清除 | Converted 月跨度 Lead 清除须保留其实际验证范围，不能把单一 disposition 视为全部。 |
| 031 | H01 Website Lead；W117/134/234/247、当前原生 graph | 无 Opportunity/Quote/Order/Won 效果或采用声明。 |
| 032 | Main IAM 当前来源、CRM Owner、PRIV/FILE 边界；W128、W482 v2 | 原卡要求的共同 NUM/FILE/PRIV 当前采用尚须精确判适用，不能因本地文件存在直接核销。 |
| 033 | 原询盘到 Lead 的入口范围、无 CRM-ACT 写入 | 未来 ACT 复用仍须单独映射；不凭源码搜索冒运行通过。 |
| 034 | 各 Owner 的冻结 Problem/HTTP 与 FE 错误；W118/122/127、W482 | 全错误族的 HTTP/code/retry/UI/audit/mutation 尚须逐族核齐。 |
| 035 | 28 家族/136 步原合成 fixture 与 S01 严格库 | 原设计 fixture 的 NOT_RUN 原字节保留；真实业务结果独立列示。 |
| 036 | W234/247/本轮明确模拟业务/身份/政策/外部服务，实际 Owner/PG/HTTP | 缺企业真实资料不再阻塞业务验收；正式运行身份/候选/部署仍独立未执行。 |
| 037 | 内部稳定槽；W68/117/121/127，W482 tenant+actor+operation+resource+key | payload 仅比较指纹；配置 save 的 resource-independent 特例不变。 |
| 038 | 原 62 列 Lead codec 和 LeadHistory；W108/110/117/121/134/310 | 不用新小对象代替完整原图。 |
| 039 | Persisted winner 的 actor/calendar/SLA/source/consent；W117/134/247 | W247 的纽约只走待审核，冻结 H01 的 Asia/Shanghai 不扩大。 |
| 040 | 原赢家 audit/event；W117/121/127/133/134，本轮 export 请求审计与第五 route | target-local 事件保留版本与名称，不冒充 source-exact。 |
| 041 | 原四 disposition 及 receipt/replay/consent；W121/124/127/256/259，W482 导出 | 四 disposition 全部生命周期联合验收未因本轮两类实际链而自动完成。 |
| 042 | Configuration canonical hash；W63/67/102/105/255 | 保存/预览/激活引用同一不可变内容。 |
| 043 | Active row→rendered form/attempt/expiry；W106/117/247 | 精确版本和原合法字段，不能用当前新 Bundle 替换旧 Attempt。 |
| 044 | Receipt row TTL 11520 分钟；W117/118/247 | 已存行时钟与 cookie 剩余期限绑定；不声称等待完整八天。 |
| 045 | Rate 三维事务、429 max Retry-After；W107/117 | 回放不再消费配额；失败回滚不留半个业务图。 |
| 046 | Receipt 前缀/locator 与独立 credential；W106/117/118 | 非秘密 locator 不授予内部读权限。 |
| 047 | 原 PG tenant-qualified graph/FK；W108/110/117/121；本轮 Job/Artifact 复合 FK | 新状态/制品不改变原精度、TenantId 和现有模型目录。 |
| 048 | Strict schema→typed decode；S01-08/W59/128，W482 默认与文件版本各自边界 | 内部类型直接信任；外部 wire 仍严格校验。 |
| 049 | Ed25519 trust/rotation/revoke/purpose；W59/87/119/128，W482 v2 域与 v1 隔离 | 密钥/角色当前事实与最终事务均需适用。 |
| 050 | 原字段裁剪构造器；W120/122/128；本轮完整 privacy 字段集与政策字段文件 | 当前字段不因某项 PII 原值为 null 而放宽。 |
| 051 | 冻结 enum 与 PG CHECK/Owner：20/30/90/99；S01-08，W482 20→30→99 | 真实 PG 拒绝 40/50 及缺时钟的 30；90 大文件拒绝分支未人为制造越合同输入，不冒执行。 |
| 052 | 原 49 UI 样例严格库；S01-08 R1；W118/122/本轮实际 React 交互 | 49 个类型样例通过不等于 49 个浏览器交互均新运行。 |
| 053 | 原 C#/TS Unicode code point 校验；S01-08、W118/247，W482 key Runes | 新文件保留原字符串，不截断/重写。 |
| 054 | 原 C# 防御复制/NullOnly；S01-08/W54/55 | 复用未变合同库结果，不为相同输入重跑。 |
| 055 | 原 TS 递归 refs/unions/arrays/objects；S01-08 | 新文件 decoder 仅承新版本的外部表示。 |
| 056 | Configuration write allowlist 与不可变 IssuedAttempt；W105/106/117/247 | 新 privacy extension 不修改原 Attempt 绑定。 |
| 057 | 原 fixture 全约束/阶段图、PG 原图测试；S01-08/W108/110/117 | 设计 136 步与实际 PG UoW 不是同一证据；全 fixture 运行范围保留。 |
| 058 | 原保留 row deadline、hold 与多 PII store；W124/127/259；W482 artifact/proof | 实际覆盖与 Converted/四 disposition 剩余分开记。 |
| 059 | 统一 Date/no-store/CSP/no-referrer/nosniff、Host cookies；W118/247，本轮真实下载头 | 默认元数据与新增文件都保持固定 attachment/SHA 头；不加入 proof URL。 |
| 060 | 原静态 harness 与本轮真实日志/退出码/TRX，FAIL 与 NOT_RUN 保留 | 未运行候选不当 PASS；模拟输入不冒充模拟了业务 Owner。 |

R1 两项修订：`COM-INQ-GENERATED-CONTRACT-ADOPTION-POS/NEG` 由明确 R1 组合库及真实 Owner 采用核对；S01-08 已证明 90/99 公开三字段必须为 null、30 保留原关系、49 UI 样例可往返。本轮新增 HTTP 20/30/99 和五分钟清除、实际默认元数据/附加文件，以及当前来源/权限/资源错绑拒绝补运行采用；90 与全原 fixture 的完整范围不越级。正式修订场景仍按 roadmap 原完整义务核销。

## 集中审查、复现与交付

一次任务级完整 SPEC/QUALITY 审查覆盖两仓相对已确认 main 的完整 diff：当前人员/独立服务权限、租户/资源与字段、原 key/If-Match/202、共同 PG 事务/锁序、原事件/审计来源、R1、一次性凭证、加密/文件字节/哈希、Origin/CSRF/v3 签名、页面清除/晚到/互斥、保留/hold 及显式安装/运行权限。R1 和内部超时问题修正后仅复查对应变更并运行上表相关回归；无尚未解决的本任务必需门禁失败。普通文档检查差异、引用与台账生成一致性，未因此运行产品全仓测试。

实际重现入口：从 CRM 根使用 `tests/CP6.CRM.IdentityPostgreSqlTests/pg_resource.py create` 创建自有新 PG，设置 `CP6_CRM_PG_ROOT`、空闲 `CP6_CRM_PG_PORT` 和 helper 输出的 `CP6_CRM_PG_SETTINGS`；`.NET` 测试项目为同目录 `CP6.CRM.IdentityPostgreSqlTests.csproj`。常规新 Owner 过滤 `FullyQualifiedName~Native_privacy_export`，五分钟场景过滤 `Native_privacy_artifact_elapsed_expiry` 并设置 `CP6_PRIVACY_EXPIRY_EVIDENCE`，一小时场景过滤 `Native_privacy_elapsed_retention` 并设置 `CP6_PRIVACY_ELAPSED_EVIDENCE`。浏览器使用 Node 22、现有 Playwright，`CP6_CRM_CONFIGURATION_UI_OUTPUT` 指向本轮 `web-tests`、`CP6_CRM_WEB_STYLES` 指向 `apps/web/app/styles.css`；实际四场景过滤 `Native_privacy_browser`。Main 跨进程用例为 `Inquiry_export_service_v2_actual_TLS_requires_independent_privacy_authority`，需上述 CRM 项目/制品路径、CRM PG settings 和独立 Main `CP6_PERMISSION_PG_ROOT/BIN/PORT`。完整执行输出、TRX、失败、原生最低期限观察及本轮使用的本地构建入口都保留于前述私有根；不输出连接设置或凭证。

CRM [PR #168](https://github.com/GTX537/CP6.CRM/pull/168) 已将源 `f58688e0c60bbb07a0ef3c4cee8495ea7156d193` 正常合入远端 main `8c78760ef7ae746a55bdf34d81a17439f7307715`；祖先关系和远端引用已核实。官方 finish 已成功，准确本地/远端任务分支删除，DEV-2 干净、detached 供复用，席位已释放。运行依赖缓存保留。工具初次因 `.next`/`.test-dist` 两处本轮生成输出拒绝切换；随后自动审批拒绝了删除命令（`blocked by policy`），命令未执行。两处约 1.26 MiB 的再生输出已完整可逆移至本轮私有根 `retained-generated`，未删文件，官方 finish 再次执行后成功。

Main 提供者源提交 `acc5b190904278b7045200f242db4bc3c03c69d9`；本提交汇总同一任务报告、台账、生成页和四状态，待本 PR 正常采用与官方 finish，不能提前称 Main 席位已释放。最终准确分支/工作区/席位状态由同轮 Main PR 和 `D:/CP6-local-test/CP6-12H-20261111/W482-01/main-finish.json` 核销。原 375 项、正式 PostgreSQL 16/11 与完整目标不批量提升。

两个本轮自有 CRM PG 已逐一核系统身份、目录、marker 和无客户端后 smart 停止，旧草稿 PG 早已停止；Main TLS 夹具的 PG 由原拥有者正常停止，HTTP/Node/Chromium/.NET 验证进程均已退出。数据和失败日志保留。候选与受保护 main 的 workflows 树一致：Main `3355235636fd45116f5445fca32fc65e49f13003`、CRM `0c2d74f9fdc63f5178f97baf33950fb3d4dad751`；普通入口为 workflow_dispatch，Main R2 仅版本 tag，未创建 tag。没有本任务 Actions 运行/取消、触发/保护变更、既有数据库安装迁移或部署。
