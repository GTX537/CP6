# WP02-07 / WP02-11 Platform 下一轮准入建议

日期：2026-10-13。性质：只读准入调查，未授权/启动实现；最终领取、entry_gate 和资源窗口由 COORD 登记。

**建议：两项均可在登记后作为独立库任务实施。** C=WP02-07、D=WP02-11 可并行；与 B=WP02-27 的 Release/trust 范围分离。本轮不建议改派 WP02-25。WP02-07 在真实 PostgreSQL 测试前必须分配独占测试库/角色/命名空间，并落实本任务私有测试项目依赖窗口。WP02-11 未具备远端持久去重/原结果证据的真实写 profile 必须保持关闭；这不阻塞库内新增 opt-in 策略和只读 HTTP 实测。

## 基线和实际前置

- Main `main=origin/main=a1824fec3d4824d45e27693f2e715b311ee5f665`。
- Platform 本地 `main=origin/main=9259a45d39bae7e98ca804a74778a588c1ad4619`；父协调已提供该远端 pin。
- 实读工作树 `D:/CP6.Platform-worktrees/wp02-01-20261012`，HEAD `d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6`，干净，`git diff --quiet origin/main HEAD` 退出 0，整个 Git tree 与当前 main 一致。
- `D:/CP6/CP6.Platform` 主目录仍在旧无关分支，HEAD `97df4f650847d164923750ce0d0de925731aeac4`；不可按该工作目录文件作为当前源码。
- S01-09 交付 `753696c8333325d9faa5d80ea429ab8b9480e71c`（实现 `708253360269396a43c2b25cce1c87cd9f59f9d2`），PR58，合入 main `dc4c0ab216974339ab3c2cebaaf4bb92c9e9b351`。
- WP02-01 交付 `d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6`，PR59，合入当前 main。两项 `git merge-base --is-ancestor <delivery> origin/main` 均退出 0。
- Main 台账中 S01-09 / WP02-01 均 DONE；07 / 11 仍 PLANNED、entry_gate NOT_CHECKED、assignment null。本报告不改状态。

前置能力是**已合入的库源码**。S01-09 提供固定 manifest/profile/schema/hash 准确路由、严格 JSON 原字节捕获及显式 pinned bundle 重载；不是 Owner 内容一致性判定、身份授权或数据库原子性。入口在 `Cp6NativeContractCatalog.cs:16,28,50,57`、`Cp6OriginalJson.cs:19,21,28,30`。WP02-01 的 `ctx-binding-v1` 提供 verifier/当前 authority 与 scope，但真实适配器和 Owner 最终门仍由宿主实现。原 ctx-v1 七字段未变，新接口可供同仓项目引用使用；没有新包发布或 Main/CRM 实际采用证明。

历史测试只按原范围引用：S01-09 110 条库断言；WP02-01 原 HTTP243+Unit259=502 条及 COORD 新 Architecture98。这里未重跑。证据入口：
- Main `docs/development/evidence/wave2-integration-20261012/S01-09/{remote-main,original-validation}.json`
- Main `docs/development/evidence/wave4-integration-20261013/WP02-01/{remote-main,original-validation}.json`
- Platform `docs/development/evidence/S01-09/README.md`
- Platform `docs/development/evidence/WP02-01/VALIDATION.md` / `docs/WP02-01-TRUSTED-CONTEXT.md`

## 原件和范围

| 原件 | 已核对 SHA256 | 本次实读范围/关键入口 |
|---|---|---|
| PLT-DEL v0.2.1 | `642a2fbc35d17fbfaa0bcc51617e24654cbe193e0f907674a3e6f4c41c47eb84` | 正文1–108、249–275；尤其77–94、100–108。未声明全文阅读中间 lease/retention 大节。 |
| PLT-HTTP v0.2 | `9d1416cbd7d0ea6e58a0da79de80dcb2a4e675890345dfb4558194c61048eb3f` | 正文1–133，93–97单独复读；29–43错误，65–83超时，87–111重试，113–129应用门。 |
| S6 INT-ID/OPS v0.2 | `d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc` | 213–267，尤其218–241 precheckpoint，243–261最终门及四类动作。 |

原件绝对路径分别为：
- `D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/64/642a2fbc35d17fbf__PLT-DEL-01_完整静态设计_v0.2.1.md`
- `D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md`
- `D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md`

同时读 Main AGENTS、WP02-07/11/25/27 任务卡、PLT-DEL/HTTP 模块、08 PostgreSQL baseline、09 parallel-development、W2 03-transactions-and-recovery，以及 owner-adoption 的 COM-S6-PRECHECKPOINT-FINALAUTH 条目。foundation.json 原AC身份继续使用；原 finance-recovery schema 保持只读，不能为了统一接口把其 ResourcePlan/NativeUow 当作已取得真实事务的证明。静态接受不改变业务 NOT_RUN；新任务也不要求实现 PLT-DEL 全60AC 或 HTTP 全42AC，必须逐项标明本任务自动覆盖子断言与留给宿主/后续任务的责任。

## C / WP02-07 精确窗口

允许写：
1. `src/CP6.Platform.EntityFramework/InboxV1/`：新版本 caller-owned seam、decision/result、事务适配及必要的新映射；不新建 Platform 业务权威账。
2. `src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs`：仅兼容的新增入口/委托接入。原 constructor、ProcessAsync、旧枚举及旧自有事务语义不改变。若独立新类型足够可不动此文件。
3. `tests/CP6.Platform.UnitTests/InboxV1/`。
4. `tests/CP6.Platform.WP0207.PostgreSqlTests/`：本任务独占的新 non-packable net8.0 项目、测试和本地辅助资产。
5. `docs/WP02-07-CALLER-TRANSACTION-INBOX.md`、`docs/development/evidence/WP02-07/`。

新增测试项目可用私有 `Directory.Packages.props` 显式导入 `../../Directory.Packages.props`，仅新增 exact Npgsql8.0.8、Npgsql.EntityFrameworkCore.PostgreSQL8.0.11。这两个版本与 Main `CP6.Core.csproj:23–24` 已有 pin 一致，也经父协调指明可用；实际 restore 和 resolved graph 仍须留证。沿用 Platform EF8.0.30、SDK/xUnit/TestSDK，不关闭中心版本管理、警告或安全检查。直接按 csproj 运行，无须改 solution。若确需改根依赖或已有项目配置，先向 COORD 提交具体原因和串行窗口，不自行绕开。

当前源码差距：
- `Cp6InboxProcessor.cs:73–100` 异常后另 context 重读可直接 Duplicate；`:139` 自开 Serializable；`:161` 重复早退；`:181–188` 低/同版在业务 callback 前完成/commit；`:192–204` callback 后自行 commit。故**不能只把 precheckpoint 塞进现 callback**。
- 新接口必须由真实 caller context/transaction 驱动，不能新建 context/事务、commit、独立补写失败或提前 transport SUCCESS。PendingCommitResult 与 confirmed outcome 分层；确认丢失按原键查询。
- 七 decision 必须封闭且与旧 enum 分离；transport identity conflict 优先。raw envelope 与 data原片段/content域独立，不使用重编码字节替原 hash。可信比较由 Owner adapter 读取既有 lineage/Projection；不让未知 incoming 自证当前可信基线。
- 同一 native UoW 中的注册保护→Reconciliation→Published→Projection→Inbox→Checkpoint→Observation/Conflict 次序由 caller 证明。只保存 txId 字符串、bool 或 ContextEvidence 均不证明持锁。
- 原结果槽绑定 Owner 原 operation/message身份和不可变输入/结果，不借通用 Inbox 代替 ERP CommandInbox；历史结果披露依当前读权，不能要求先授新写权限。
- `Cp6TransactionalMessagingModelBuilderExtensions.cs:42,60,70,85` 当前全部 `byte[] IsRowVersion`；无 PostgreSQL 项目/provider。新增 PG 映射必须真实验证，不能把旧8字节假设或 SQL Server 记录充 PG 证明。原模型/实体/types 文件暂只读；需要变更时明确兼容依据和额外窗口。
- Outbox.Enqueue `Cp6OutboxStore.cs:24–40` 已仅 Add 不 Save。旧 retention `Cp6MessageRetentionService.cs:17–47` 仅按技术状态/年龄删除；本任务不顺手重做 lease、retention、requeue。新结果/观察不得被旧清理误删除，也不能声称已经完成全保留治理。

本地验证必须覆盖：实际 PG 同连接同事务业务+原回执+Inbox/观察/冲突提交及全回滚；全部重复/旧/同版/唯一冲突重读入口；同消息不同raw/绑定、同版本不同data；同版同data但不同合法运输原文；缺/异连接/已结束事务；A/B租户错误绑定；并发同键、序列化失败恢复；commit confirmation lost 后只读原键且不先返回成功。保留故障及完整环境/SQL/事务身份和源/产物证据，不能用 SQLite/InMemory、空测试或 mocked commit 代替。测试数据库要独占，只创建任务测试资产，不采用 Main/CRM真实业务迁移。

## D / WP02-11 精确窗口

允许写：
1. `src/CP6.Platform.AspNetCore/HttpV2/`：新版本 profile/binding、monotonic deadline、retry decision/parser/handler及安全 errors adapter。
2. `src/CP6.Platform.AspNetCore/Cp6HttpResilienceServiceCollectionExtensions.cs` 和 `Cp6ProblemDetailsExtensions.cs`：仅新增显式 opt-in 重载/委托。原 API、profile、默认及兼容行为保留。
3. `tests/CP6.Platform.AspNetCoreTests/HttpV2/`：含本地专属 clock/fault/loopback helpers。
4. `docs/WP02-11-HTTP-V2.md`、`docs/development/evidence/WP02-11/`。

沿用现有 Microsoft.Extensions.Http.Resilience10.9.0、TimeProvider 与 ASP.NET shared framework；不改 csproj/依赖版本、公共 Testing 或 Contracts/Abstractions。现有测试里已有本地 ManualTimeProvider 和 Kestrel loopback 示例，可只读参考，不需要搬成共享 helper。旧 `HttpResilienceTests.cs`、认证/上下文测试作为回归输入只读。

当前实现确为 legacy：`Cp6HttpResilienceServiceCollectionExtensions.cs:66–70` 零等待、无jitter、忽略 Retry-After；`:131` 按 Timeout数值猜总/单次；`:143` Idempotency-Key格式不能证明 Owner durable dedup。`Cp6ProblemDetailsExtensions.cs:73–75` 原样输出 errors，尚无端点安全白名单/上限。这些差距由新版本扩展解决，不原地改变既有 profile或CRM错误契约。

关键实现/验证：
- 新写retry profile必须有 trusted named-client operation binding，说明tenant/action/key/requestVersion/digest、Owner原receipt/query/retention、method/destination/credential/route epoch和证据。外部 header/布尔声明不授能力。无实际证据的生产binding保持Disabled；07候选实现不是远端业务原子去重证明。
- 保留 legacy transient集合及其不重试 TimeoutRejectedException 的真实行为；新版本按明确策略来源区分 equal-duration attempt/total timeout，未知来源停止，caller取消不重试。子调用继承剩余monotonic budget，HttpClient与pipeline总预算有唯一权威。
- Retry-After 只单值；非负整数及 IMF-fixdate/RFC850/asctime 三日期；超大合法整数直接StopServerWaitExceeded，非法/重复按本地等待但不绕budget。一次UTC→monotonic锚定；仅一次jitter采样；最早时刻=max(S,L)，jitter/maxDelay不截服务器等待，S>30000立即停；决策与发送前均核 W+A≤R，提前醒继续等、迟醒重验，取消零后续attempt。
- 固定子例覆盖 S100/L200、S5000/L2000cap、S60000/R100000/A2000停止、S9000/R10000/A2000停止、等号边界、UTC回拨、重复值、整数溢出和三日期。
- 写入Unknown保原key/body/digest/binding，先查询原结果，不能刷新身份/换租户/换key续写；CircuitOpen不能把先前Unknown变NotSent。敏感异常/输入不进正文，安全errors的可见字段/安全文本/数量32×4×256及整体16KiB超限降通用码，Response.HasStarted或断开不拼第二正文。旧CRM about:blank/业务码/CSRF/OIDC流程不在本库任务更改。
- 实际 loopback HTTP验证attempt数量、取消和预算；deterministic clock补充分支。只读可实际重试，未证明写请求实际零retry。Owner-proven写adapter的 fixture必须明确仅库协议测试；没有真实远端Owner证据不得写“业务exactly once”或“生产写profile已启用”。

## 并行和交付约束

两 lane 都只读：Contracts、Abstractions、Messaging、Testing、Release、ReleaseTests、ArchitectureTests、旧 csproj、根 Directory.Build/Packages.props、global.json、sln、工作流、发布/签名/信任材料。各任务的新公共表面放其独有命名空间/目录；不能各自发明一份公共业务键/digest/租户权威定义。确需共享类型时 COORD安排单一权威编辑窗口。与 B27不共写 Release、共享测试设施或依赖输入。

先从当时已确认最新 Platform main 建独立新 branch/worktree；既有WP02-01工作树本轮仅供阅读。原四个聊天仅获得各自准确任务。COORD统一更新roadmap/生成页/四份状态，串行合并。原任务成功证据按实际源码/依赖/环境复用；新行为使用局部测试及一次完整任务diff审查；涉及public API/新项目/依赖时运行相关架构门，发现门失败不能改测试放宽约束。合并后仅做必要集成检查，记录另一lane新输入的影响。

源码树8个工作流中7个仅workflow_dispatch；剩余 RFC3161 preflight 的 pull_request路径仅其YAML和 `tests/CP6.Platform.ReleaseTests/P10Rfc3161PreflightWorkflowContractTests.cs`，两项窗口均排除。每次push/PR前仍须核候选和受保护分支、过滤及依赖，检查准确归属排队运行；此次只读子任务未查询/取消远端运行，未改变任何触发。无 Actions/发布/tag/部署授权。

## 未验证与操作记录

产品编辑0、测试执行0、数据库操作0、git变更0、网络写入0、Actions触发/取消0、发布0、部署0。唯一写入为本目录 admission-platform.md/json。远端pin由父协调提供且已有合入回执；本子任务独立确认的是本地refs、祖先和tree，不冒称另一次在线fresh验证。PG服务/权限/资源尚未运行核验。新任务运行仍NOT_RUN，真实Owner/宿主/包采用仍需独立证据。


## COORD 后续资源登记回报

父协调在调查期间已创建 C工作树 D:/CP6.Platform-worktrees/wp02-07-20261013（codex/wp02-07-caller-inbox-20261013）及 D工作树 D:/CP6.Platform-worktrees/wp02-11-20261013（codex/wp02-11-http-resilience-20261013）。C独占预约 PostgreSQL 127.0.0.1:55427、D:/CP6-local-test/WP02-07/<owner32>；父协调 pg-resource-probe.json 确认18.6二进制存在，尚无监听和本任务cluster根，未建库。本报告未独立操作服务/数据库。

实施前核对该预约与台账；不得动既有5432服务或使用既有凭据。实际建库/测试/清理都需绑定完整 owner32、cluster实际路径、数据库OID与principal，限定本任务持有资源。这个预约消除了“端口/目录未分配”的协调缺口；实际启动/权限/事务验收仍NOT_RUN。公共API签名与任务局部测试依赖仍须在本人PLAN固定。各来源准确Git blob见同名JSON的source_evidence。
