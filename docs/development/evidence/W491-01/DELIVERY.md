# W491-01 · 询盘四种处置的原生保留闭环

状态：历史数据装载准入已实际核验，现行写入口拒绝过期历史凭据；四处置原生保留仍未完成，等待明确的测试初始装载边界。

本窗接续最新开发对话 `01a1cada-68af-79f0-be6f-ff20d71129e1`（“确认 CP6 48 小时目标完成”）的 W490 最终交付，按[既有实施计划](../../../superpowers/plans/2026-11-11-inquiry-native-retention.md)推进原 WP03-04。该对话的 12 小时预算已结算，本窗不重启旧预算，也不把预算结算当完整业务完成。

## 准入与范围

- 已确认 Main `fef657c53dcde89546fb54cb86a298e1c130a1c0`、CRM `21239ca957b54f41b7cc988164e99a92b7c1bfe0` 与当前远端一致，无开放产品 PR；三席原本空闲、干净且无关联进程。
- Main 官方 `start` 已领取 DEV-1 的 `codex/w491-01`，由 COORD 维护本窗台账。CRM 产品窗口复用 DEV-2；两仓分别正常交付和 finish。
- 先在初次 INSERT 前建立因果完整的模拟历史政策、批准、日历及询盘/Lead 原图；实际 PostgreSQL 时钟与现行 Owner 执行保留、hold、完整 PII 清除、失败回滚和原结果恢复。不回写已落库业务日期、期限或重新封存伪造历史。
- 使用既有 CRM 原生事务、当前授权、原件鉴真、保留和隐私导出实现；W482/W484/W485 等已有效执行的直接范围复用。模拟外部身份与业务输入明确标注，不以本窗证明 Main SYS-PRIV/SYS-FILE 整卡采用。
- 本窗限定历史保留夹具、相关行为测试及真实失败所必需的最小产品修正。未调用的 Stock #844、正式 0.10.4、完整 WP02-02 恢复和 Main 构建入口不作为本窗前置。
- 未授权 Actions、正式发包或部署。原 375 任务、60 原 AC、247 修订和正式 PGT 的状态仅按实际完整证据分别核销。

## 验证与剩余

CRM 新增 `InquiryHistoricalRetentionTests.cs`，只验两个真实行为：

1. 同一现行 Bundle/政策下，以全新 ID 首次写入 2024-02-29 的模拟历史 Attempt。`InquiryPublicAttemptsV1.AppendAsync` 可以暂存，但真实 PostgreSQL 在提交时抛出 `23514 / CRM_PUBLIC_ISSUANCE_EXPIRED`，事务没有留下该行或任何业务图；换成另一全新、当前有效的 Attempt，同一 store 正常提交。该探针故意固定当前 Bundle，只隔离过期触发器，**不是完整合法历史政策/业务图，也不是四处置保留 PASS**。
2. 当前公开 HTTP 提交及真实自动 H01，明确使用 Converted 24 CalendarMonths 和原宽字符输入。真实 Retention Owner 两次恢复同一 unit 均返回 `NotDue`；Submission/Lead 的 PII、实际 PG 版本和 seal 保持。只证明未到期控制，不证明已经经过 24 个月。

实际命令：`dotnet test tests/CP6.CRM.IdentityPostgreSqlTests/CP6.CRM.IdentityPostgreSqlTests.csproj --artifacts-path D:/CP6-test-artifacts/W491-01/build --disable-build-servers -m:1 /p:UseSharedCompilation=false /p:BuildInParallel=false --filter FullyQualifiedName~Native_historical_retention --logger 'trx;LogFileName=historical-retention-admission.trx' --results-directory D:/CP6-test-artifacts/W491-01/results`。沿原计划设置 2 CPU、1 GiB GC 上限与禁用共享编译；隔离 PostgreSQL 18，端口 55572。结果 **2 passed / 0 failed / 0 skipped**，测试耗时 21 秒，命令 exit 0。原始日志和 TRX 保留于上述任务私有目录；`pg_resource.py stop` 已验证本任务资源身份并停止实例，数据和日志保留。

本次未修改产品代码、数据库 schema 或生产写入口，没有触发 Actions、发布或部署；数据库拒绝是现有合同的正常行为，不登记为产品 BUG。W490 原有询盘 39 PASS / 21 NOT_RUN、关联修订 2/2、正式 PGT 16/27 保持。

## 计划冲突与待决定的接续方案

原计划要求“所有行第一次插入前”构造历史原件，同时“不放宽约束换取夹具成功”。当前写入口的职责是**新业务写入**，它要求首次 INSERT 在真实当前时间仍有效：

- `InquiryPublicAttemptSchemaV1.sql` 的 `require_issued` 要求 `ExpiresTicks > clock_timestamp()`；Attempt TTL 的存储合法域为 5–60 分钟，且 expiry 必须等于 issued + TTL。上述真实提交拒绝已经证实，不能靠把 expiry 延长到今天解决。
- `InquirySubmissionLedgerV1.sql` 的 `require_complete` 还要求原 PublicAttempt 在当前提交时未过期；`InquiryConfigurationLifecycleSchemaV1.sql` 要求 activation 使用的 preview proof 在当前提交时未过期。这两项是源码核验，未冒称额外动态实验。
- 因此，补齐更多图字段不能解除第一个时效门；今天审批、篡改已有日期、假 PG 时钟、放宽生产 TTL 或绕过正式 Owner 都不是该计划的合法实现。

建议把**测试专属初始状态装载**与实时新业务写入口分开，作为原计划的明确修订：只对 `pg_resource.py` 新建且拥有的隔离库，先装载一次性模拟历史完整原件；初始装载阶段不执行在线写入的“凭据此刻仍有效”触发器，随后恢复全部原版触发器、FK、静态约束与 schema 检查，在同一完整数据集上执行原件/完整图鉴真，再让现行 runtime/Owner 在真实当前时钟和权限下处理。装载失败整组回滚；测试不能连接既有业务库，不增加生产历史写入 API，不把初始装载称为历史公开 API 或正式恢复验收。

这会改变原计划“首次 INSERT 也走全部现行时效门”的边界，**尚未实施，也不视为已获例外授权**。若不采用该修订，保留本次诊断与 24 月未到期控制，先推进不依赖此历史夹具的 W492 本地 PostgreSQL 构建入口。四处置全图、hold/释放、末段故障和恢复仍作为原 WP03-04 的明确余项，不能据本次两项测试核销 AC030/041/058。

## 生命周期

Main DEV-1 与 CRM DEV-2 均已官方 start，属于同一 W491-01。CRM [源提交 727ff141](https://github.com/GTX537/CP6.CRM/commit/727ff14152bc40d0d609c11232cab07aba8bd918)已推送并回读核对 `origin/codex/w491-01`，尚未创建 PR 或合并 main。一次集中审查完整单文件 diff，覆盖模拟范围/真实拒绝、零部分写入、正例对照及未到期不变行为，无产品/schema 修改；相关本地验证见上，不因提交 SHA 改变重跑。

CRM 官方 `finish --outcome deferred` 已完成：远端任务分支保留，DEV-2 干净 detached，席位释放；隔离 PG 已验证停止。Main 本协调提交同步唯一台账、生成视图和四状态后保存同名远端分支，并执行暂缓 finish，最终精确 HEAD 与释放结果以本轮工具记录为准。暂缓仅因为上述测试初始化边界未获决定，不是缺少真实企业数据。

两仓候选与已 fetch 的受保护 main 工作流内容一致：CRM 只有手动验证入口；Main 普通门禁全部手动，R2 candidate 仅匹配 `v*.*.*` tag。本次只推任务分支，不命中远程运行入口；未修改触发、无本任务运行可取消。

Main 完整 diff 已集中核对：只更新本窗报告、旧计划的准入结论、roadmap/派工入口/生成视图及四状态；原任务和完整验收统计不变。`manage_roadmap.py --write` 的结构/来源与生成一致性检查 PASS，`git diff --check` 通过；这两项不是业务验收。Main 仅文档/登记变更，没有启动产品构建或额外代理。

恢复时先执行官方 `resume` 并读取两仓 `origin/codex/w491-01` 及本报告。若用户选择继续，先落实这里的初始装载边界，再完成原四处置范围；若选择W492，按该独立计划重新登记开工，不能占用未释放席位或把W491改称完成。
