# 询盘四处置原生保留闭环 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax. 按CP6规则单对话、一任务分支、一份交付报告、一次集中SPEC/QUALITY审查；不自动启动代理或拆成逐字段验收窗口。

**Goal:** 用合法、明确声明的模拟历史业务输入，让真实PostgreSQL时钟下的现行保留Owner完成NeedsReview、Converted、Rejected、Expired的完整擦除/拒绝/恢复闭环；准确缩小AC030/041/058的实际缺口。

**Architecture:** 新增测试专属历史夹具，在**所有行第一次插入前**构造完整原始图、政策/日历/同意/回执/原命令及伴随事实。日期属于模拟业务原始输入；随后只运行现有真实Owner、数据库时钟和当前授权。既有Current Owner生成的新原件不回写历史时间，数据库时钟不替换，实际月数经过与历史输入验收分开记录。

**Tech Stack:** 现有CRM .NET 8/Npgsql/PostgreSQL 18、`CrmInquiryRuntimeDbContextV3`、原生列保护/完整性与Retention/Privacy Owner、现有HTTP/PG测试夹具。

---

W491 的准入测试证实现行 PostgreSQL 拒绝首次插入已过期的 Attempt。2026-11-12 用户确认允许在独占测试库初始装载模拟历史。装载事务用模拟原件的发生时间核对相应写入时效，保留外键、静态约束和关联检查，每段恢复原触发函数定义后才进入下一段。现行 Owner 在装载提交后采用实际 PostgreSQL 时间和当前授权。验证结果见 [W491 交付报告](../../development/evidence/W491-01/DELIVERY.md)。

本计划沿用 [W484 未来组件投影](../../development/evidence/W484-01/DELIVERY.md)、[W482 实际 60 分钟](../../development/evidence/W482-01/DELIVERY.md) 和 [W485 实际 Rejected 保留](../../development/evidence/W485-01/DELIVERY.md) 的对应证据。

## 准入与限定范围

建议优先窗口`W491-01`，归原WP03-04。实际先查规范任务/分支/开放PR；从最新已确认CRM main领取DEV-2的`codex/w491-01`，Main协调窗口和产品窗口分别登记。预计一个3–5小时完整实现/验收窗口，4h按实际出口重排。Stock #844及默认Main构建不属于这条CRM内部调用链的统一开工门。

首先逐条核WP03-04的条件消费绑定。模拟业务、政策、身份及外部服务输入按2026-10-29用户决定执行；当前权限、Owner、同事务、精度和原结果协议实际运行。不得用NoOp替换真实内部依赖，或据本地CRM隐私Owner成功声称Main SYS-PRIV/SYS-FILE整卡已采用。

所改文件：

- 新增`tests/CP6.CRM.IdentityPostgreSqlTests/InquiryHistoricalRetentionFixture.cs`：在现有`partial IdentityCurrentInspectionTests`内放测试专用的新历史图构造/初次持久化及原始输入清单，复用其私有合法夹具。
- 新增`tests/CP6.CRM.IdentityPostgreSqlTests/InquiryHistoricalRetentionTests.cs`：同一partial类中增加`Native_historical_retention`前缀的方法，覆盖四处置矩阵、一个未来未到期控制、hold、故障回滚和恢复。
- 复用`InquiryRetentionOwnerTests.cs`的`RetentionStorageHost`、`RetentionOwner`、`StartRetentionUnit`、`InsertRetentionHold`、`RefreshCoverage`与原HTTP读回；复用W484完整子图定义，不复制第二份擦除实现。
- 需要实际导出Owner/API及artifact/proof足迹的样本使用既有`privacyExports:true`，需要实际撤回Owner的样本使用`consentWrites:true`；其他样本不默认增加权限。保留24个月样本不传缩短期限的`minutes`参数。
- 仅当实际失败证明产品缺口时，最小修改现有`CrmInquiryRetentionOwnerV1*.cs`或相关原生存储；先登记Issue并保留RED。没有实际缺口时只交付验收测试，不为测试开生产历史写入路由。

## 1. 先建立合法模拟历史图

- [ ] 为全新模拟租户构造完整的历史政策、批准者/批准事实、Bundle和日历，作为初始模拟业务输入写入现行native存储。批准时间、生效区间及日历必须与原业务时间因果一致，并通过原存储约束、内容哈希及Owner读回。当前配置Owner允许首版已到生效时间，但今天生成的ApprovedAt不能冒历史批准；现行配置Owner用于新鲜控制场景，历史输入采用测试专属初始图，不增生产写入路由、不改已有Bundle日期或哈希。
- [ ] 使用原默认保留语义：Converted **24 CalendarMonths**；其他处置按原政策中的分钟值。日期与deadline从同一个原始业务输入派生，Converted保留日历月边界，不换成固定天数。加入2024-02-29→2026-02-28等明确样本；实际执行日期不适用时，选已到期的合法闰年样本并记录原日期，不改PG时钟。
- [ ] Converted分别覆盖自动H01与人工放行：前者原收到/转换时间同点，后者保持`ReceivedAtUtc < DecisionAtUtc`，按原转换锚点加24个月，不能把人工放行也按收到时间算。Rejected按原决定时间；Expired按原`ReviewExpiresAtUtc`加政策分钟数，不能因Worker晚执行而滑到“当前时间+TTL”。
- [ ] 每例新建全新tenant/resource/operation/attempt/receipt/job标识及完整新行。在第一次INSERT前设定模拟`ReceivedAtUtc`、处置时间、日历/SLA、原保留deadline及子图时间；以真实列保护器保护对应新ID的数据，并从实际保存后的PG版本封存完整性。不得从已经写入的新询盘改旧日期、改deadline后重新封存，或借W484组件的未来时间参数调用原生Worker。
- [ ] 按现行原生合同准备全部必需行：Submission/Lead/Calendar与互相FK、Consent、原Attempt/ReceiptProof、原命令/回执、风险/历史、审计/Outbox、导出Job/Artifact/Proof及声明范围内的子图。存储角色/同UoW/当前真实约束与现有原生测试相同；初次保存后的实际PG版本用于封存及事件绑定，不捏造旧生产版本。夹具初始化可由测试拥有者执行，业务处理必须使用现行runtime/Owner。
- [ ] EF图与ADO原件表共享同一实际Npgsql连接/外层事务；可以多次SaveChanges取得版本，不能先提交图、再补原件。任何初始图鉴真失败都回滚整组并报告，不能用未完成夹具继续验收。
- [ ] 现行`CrmRuntimeGraphStore.InsertConvertedSubmissionAsync`先走旧candidate codec，**不作为原生R3宽字符历史夹具的替代入口**。采用`CrmInquiryRuntimeDbContextV3`、原生列保护和`InquiryNativeGraphIntegrityV1.RequireSubmission/RequireLead`，在原当前模型下验证新图；完整性、FK或合法域失败立即保留，不能放宽约束换取夹具成功。

夹具记录“当前建立的模拟历史业务输入”，不声称在2024年运行过公开API，也不称正式恢复/迁移已通过。首次写入成功后保存基线，后续不再改变原业务时间和期限。

现行`CrmInquiryRetentionOwnerV1.Originals.cs`会鉴真以下原件；历史夹具不能只插Submission/Lead后补一个seal：

| 原始行组 | 初次构造时必须保持的实际绑定 |
|---|---|
| Consumptions / Commands / IssuedAttempts / ReceiptProofs / PublicAttempt | Actor、IssuedAttemptId、PayloadHash、ConsumedTicks=ReceivedAt；原PublicReceipt/ETag与ResourceVersion一致；Original中的Credential/Browser各32字节，与浏览器绑定及credential hash相符；Attempt原密文或tombstone形态完整 |
| 人工决定Commands | Actor/Operation/RequestHash对应原保护上下文；OriginalETag=ResourceVersion，LeadId/Submission状态对应持久赢家；保留/Redacted字段不能矛盾 |
| H01Command / LeadCreationReceipt | tenant/actor/submission/lead/operation相同；原签名投影只是历史输入，不能拿其过期声明当当前授权；当前擦除使用新鲜独立service capture |
| Bundle / Calendar / Consent / 完整业务图 | tenant/版本/内容身份及批准生效因果一致；Consent.RetainUntil与原行截止相同；实际初次PG版本和完整子图封存，旧SLA/收到时间不重算 |
| Job / Artifact / Proof | tenant/job/submission/artifact、原请求/完成/到期及保留关系一致；用真实保护器和原proof绑定，明确此前是否已消费/过期，不伪装当前可下载 |

这些是生产Owner已经拥有的不变量，夹具采用其原合同；下游不增加重复校验/资格回执层。现有`AuthenticateOriginals`、`RequireSubmission/RequireLead`及真正Owner执行会验证输入，缺件立即失败。

以下模拟输入使用审核 10080 分钟、NeedsReview 12960 分钟、Rejected 和 Expired 各 1440 分钟、Converted 24 日历月。NeedsReview 和 Expired 的截止日不同，可以检出对错误状态保留期的使用。实际执行时从 PostgreSQL 读取当前时间并确认到期。

| 场景 | 原模拟时间 | 应保留的截止/控制 |
|---|---|---|
| 自动Converted | 收到/转换2024-02-29T08:00Z | 2026-02-28T08:00Z，验证闰日月末 |
| 人工Converted | 收到2023-12-31T08:00Z，原人工放行2024-01-02T08:00Z | 2026-01-02T08:00Z；原SLA仍由收到时间和日历决定，不能按建Lead时间重算 |
| NeedsReview | 收到2026-10-01T08:00Z，审核截止10-08T08:00Z，过期Worker尚未处理 | 保留截止10-10T08:00Z，直接验证NeedsReview保留 |
| Rejected | 收到2026-10-01T08:00Z，原拒绝2026-10-02T08:00Z | 10-03T08:00Z |
| Expired | 原收到2026-10-01T08:00Z，review due为10-08T08:00Z | 真实当前过期Owner晚执行，保留仍到10-09T08:00Z，随后真实Retention处理 |
| 未到期控制 | 当前真实公开提交/当前H01及政策24个月 | 真实当前时间尚未到期，Owner返回NotDue，原PII/版本保持 |

每例使用新tenant/resource/operation，不把表中的日期更新到已有事实。人工放行与NeedsReview的不同锚点、迟到过期处理和原SLA保留是实际语义差别，不为日期字段逐字编写镜像测试。

## 2. 真实数据库时钟下运行Owner

- [ ] 从实际PG读取当前时间并证明确实越过各原行deadline；未来控制样本的deadline仍未到。不把单元TimeProvider或壁钟偏移传进数据库/Owner。
- [ ] 调用现行`RetentionOwner(h).RunBatchAsync(h.Tenant)`或原`StartRetentionUnit`/`ResumeUnitAsync`，核四处置各自原始来源和截止行；NeedsReview不会静默借用Expired期限，Converted保留原Lead/Submission状态、身份、收到时间、SLA及非PII事实。
- [ ] Expired增加一条真实过期业务转换：初始历史NeedsReview图先经`CrmInquiryReviewExpiryOwnerV1`再经Retention Owner，而非只插一个正确终态就宣称过期转换已验收。该正例使用`RetentionStorageHost(reviewExpiryGrant:true, reviewExpiryScope:5)`；默认夹具不提供额外处置权限。复用现有review夹具时，将其测试用`ReviewClock`显式清为null，让Owner走真实数据库时间；当前retention service proof和额外`crm-lead:edit`仍都必需，擦除权限不代替处置权限。
- [ ] 一次组合回读完整PII足迹：Submission/Lead密文和查找hash、风险/历史reason、W484已列活动/更正/协作者字段、原命令受保护结果、receipt/export proof、artifact、缓存/输出、审计/Outbox的PII规则。擦除后原件不复活，匿名化标记/版本/回执及当前API投影相符，B租户及其原结果保持。
- [ ] 回执在历史输入中本已过期时，正确记录擦除前也为404；通过持久proof/tombstone证明Owner动作，不能把既有过期造成的404归因为本次擦除。导出TTL与原保留期限取原合同规定的较早边界。
- [ ] 用现有合法hold控制输入核同一到期图的阻止与解除，当前主体/tenant/service source失效时新效果拒绝；原Started/终态及技术恢复按原键处理。保留未到期控制和当前真实授权检查。
- [ ] hold按原规则保护需要保留的内容；原披露凭据/导出TTL仍独立到期，不能因hold延长下载授权或复活旧proof。原回执/命令的到期脱敏与正文保留分别回读，沿现有Owner语义判定。

## 3. 故障和恢复按整个任务验证

- [ ] 在全图擦除末段和artifact/proof联动删除各选一个现有注入点，实际触发数据库/保存失败：业务、子图版本、seal、原结果、审计和消息全部回滚。去除故障后以原run/unit恢复，只有一套最终效果。
- [ ] 重建Owner/Worker实例并重复恢复，原已完成结果不再擦除/发消息，旧receipt/export proof不恢复可用。明确区分对象重建与OS进程重启；若声称后者，必须实际启动新进程并记录身份。
- [ ] Next源码和依赖没有变化时复用有效构建。仅选与实际变化有关的HTTP/浏览器读取，避免重跑配置页面和不相关全站。W482/W485已真实经过的60分钟场景、W483五分钟刷新和W484持久子图失败回归按相同输入复用。

本地资源和构建示例（从已登记CRM工作树运行；只选上述新增方法前缀）：

```powershell
$python='C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13.13-windows-x86_64-none/python.exe'
$dotnet='C:/Users/tt/.dotnet/dotnet.exe'
$env:CP6_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_CRM_PG_ROOT='D:/CP6-test-artifacts/W491-01/pg'
$env:CP6_CRM_PG_PORT='55572'
$env:DOTNET_ROOT='C:/Users/tt/.dotnet'
$env:DOTNET_PROCESSOR_COUNT='2'
$env:DOTNET_GCHeapHardLimit='0x40000000'
$env:DOTNET_gcServer='0'
$env:MSBUILDDISABLENODEREUSE='1'
$settingsPath=& $python -B tests/CP6.CRM.IdentityPostgreSqlTests/pg_resource.py create
if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$settingsPath)){throw 'Owned PostgreSQL creation failed'}
$env:CP6_CRM_PG_SETTINGS=([string]$settingsPath).Trim()
try {
  & $dotnet test tests/CP6.CRM.IdentityPostgreSqlTests/CP6.CRM.IdentityPostgreSqlTests.csproj --artifacts-path D:/CP6-test-artifacts/W491-01/build --disable-build-servers -m:1 /p:UseSharedCompilation=false /p:BuildInParallel=false --filter 'FullyQualifiedName~Native_historical_retention' --logger 'trx;LogFileName=historical-retention.trx' --results-directory D:/CP6-test-artifacts/W491-01/results
  if($LASTEXITCODE -ne 0){throw 'Historical retention verification failed; retain actual output'}
}
finally {
  & $python -B tests/CP6.CRM.IdentityPostgreSqlTests/pg_resource.py stop $env:CP6_CRM_PG_SETTINGS
}
```

开工先检查端口；资源脚本只输出非密settings路径。TRX必须实际包含上述方法、选定行为全部通过且0跳过，不能将空过滤结果计PASS。stop必须沿用同一ROOT/PORT及精确settings，核Owner/系统标识/目录/数据库/无外来连接后停止；不读取或输出凭据内容，不删除数据库证据。若create失败，不拿空路径执行stop。

## 4. 验收采纳和收尾

集中SPEC/QUALITY审查核原始输入合法性、初次创建与当前时钟的区分、四种来源deadline、完整PII足迹、权限/hold/回滚/原恢复和模拟标记。测试计数只报告真实唯一行为，不作为目标。

只有原AC完整断言对应本次及有效复用证据时，才由Main台账采纳；不自动将AC030/041/058或原WP03-04翻PASS/DONE。SYS-PRIV/FILE等条件消费必须按真实合同逐条说明采用或不适用依据。CRM关联的两条运行修订已2/2通过；全项目另245条NOT_RUN并非本询盘的245个前置，不将局部成功覆盖其他Owner或正式PGT。

产品分支正常PR/合并并核远端，随后Main同窗口同步唯一`docs/development/evidence/W491-01/DELIVERY.md`、台账/生成视图和四状态。停止自有服务并官方finish；准确任务分支处置、工作区状态、席位释放同时报告。下一业务窗口再处理FX24真实Inbox去重/poison及其余询盘完整断言，不以producer UNKNOWN冒消费者失败。
