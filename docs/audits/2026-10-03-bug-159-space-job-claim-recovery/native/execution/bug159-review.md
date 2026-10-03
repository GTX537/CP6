# BUG159 专项代码审查

审查日期：2026-10-03。结论：**当前 BUG159 源码与已补齐文档可交付；未发现 P0/P1/P2 实质问题或代码审查阻断项。** 此结论允许继续既定提交、归档和正常 PR 流程，不表示已完成远端交付、整个 WP6 或生产验收。

## 审查对象与方法

- 工作树：`D:\CP6\tmp\worktrees\bug-159-space-job-claim-recovery`。
- 基线与审查时 HEAD 均为 `5587a2a67ae73715596ca1a135b5863005abac8d`；审查包含相对基线的未提交改动及新增文件，不能仅用 HEAD..HEAD 空差异表示本次范围。
- 依据已读的 `C:\Users\tt\.agents\skills\requesting-code-review\SKILL.md` 及 `code-reviewer.md`，按项目 AGENTS 的“一任务一次集中审查”节奏执行。未再派生 agent。
- 完整阅读三份待审源码、完整 lease ledger、Job/attempt 领域行为、异常 classifier、相关 Context 映射与保存钩子、PostgreSQL token/标识符配置、两库 Context 配置与 fixture、DI 独立 ledger Context、processor runner，以及 processing/publish worker 调用路径。阅读既有 classifier 测试用于解释其分类契约，未声称本次运行这些测试。
- 最终差异同时包含 `.gitattributes`、专项 README 及 `PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md`、`CHANGELOG-AI.md`。新增证据归档和最终 index 审计尚由根代理执行，未把准备状态当作产品缺陷。
- 本审查只读代码、Git 差异、已有日志和证据并计算文件哈希；未运行 dotnet、测试、数据库、API、构建、Git mutation 或 Actions。唯一写入为本报告。

## 实际源码 SHA-256

以下均为审查时对实际文件重新计算的 SHA-256，全部与两库 GREEN 的源码输入清单一致。

| 文件 | 实际 SHA-256 |
| --- | --- |
| `CP6.Space.Infrastructure/EfSpaceJobLedger.cs` | `CE5DF1E508348C339BCB701ACC7ABF40D1B096DDFF2E26790D4314B7E5026048` |
| `CP6.Space.IntegrationTests/SpaceJobSqlServerTests.cs` | `F877C7E38EBA199409D28BAB52A34505793281F19A11CF8F5E603B9A98F7BFEF` |
| `CP6.Space.IntegrationTests/SpaceJobSqlServerTests.ClaimRecoveryTests.cs` | `1FB0350EC729C16C3C919261074296FA7C4DDD7C2C58C362C9C18460A9D9DDBF` |

另外对 `bug159-pg-jobs-exact-green/source-inputs.json` 的 **1702 项**执行当前文件存在性及逐项 SHA-256 对照：Missing=0、Mismatch=0。源码 manifest 实际哈希为 `A7C99E1BF0E50C03A6A3672B5742444A920E7A222F17599661A3FD2AF32817B7`。PG/SQL 的源码 manifest 哈希相同，两库运行前后 runtime manifest 的实际哈希均为 `45120859AAEA5113C225945E36E78098DED276B27E83309685B61A8D44F6BE13`。

## 要求、实现与测试对应

生产代码引用指 `CP6.Space.Infrastructure/EfSpaceJobLedger.cs`；新增用例引用指 `CP6.Space.IntegrationTests/SpaceJobSqlServerTests.ClaimRecoveryTests.cs`。

| 要求 | 代码与测试映射 | 审查判断 |
| --- | --- | --- |
| 两 worker 读取相同待领取 Job，EF attempt 插入先于 Job token 检查触发 PG 原生 23505，应精准恢复 | 生产 242、253–262 行使用现有 classifier；仅 `UniqueConstraint` 且 `MatchesConstraint("UX_Space_JobAttempt_Tenant_Job_AttemptNo")` 进入恢复。Context 3281–3283 行的既有唯一索引同名，PG 标识符适配保留该长度未超限的名称。新增 16–68 行协调两个 worker 到保存前，再依次提交；44–45、149–153 行要求观察真实 PG 23505 和精确约束。 | 原生 RED→GREEN 证据与代码分支吻合；没有把任意 23505 视为领取竞争。 |
| 冲突后完整回滚、清除旧跟踪、释放旧事务，再重新读取 | 生产 159–166 行每轮创建新的 ReadCommitted 事务；244 行以 `CancellationToken.None` 回滚完整事务，245 行清除 tracker。`await using` 的事务作用域在本轮离开时异步释放，下一轮才开始新事务并重新查询。新增 106–108 行核对各轮不同 TransactionId 和最终 CurrentTransaction=null；46–47 行核对原生败方事务释放及无待保存改动。 | 不在已失败 PG 事务或旧 EF 状态上重复 SaveChanges。领取、旧 attempt 放弃及终态同步等事务内步骤会随整轮回滚。 |
| 最多沿用既有五次，冲突耗尽仍返回 null | 常量为 5，生产 159、250 行保留既有上限与返回语义。新增 76、101–108、117–121 行要求保存五次、五个事务、最终 null，数据库仍 Queued、AttemptCount=0、无 attempt。 | 没有无限重试或消耗业务 attempt 计数。 |
| 既有 optimistic concurrency 继续恢复；已识别 deadlock/serialization 同样重试整个事务 | 生产 259–260 行分别保留 `OptimisticConcurrency` 与 classifier 的 `CanRetryTransaction`。classifier 仅将已识别 deadlock/serialization 设为该可重试类型。新增 82、85、111–115、172–174 行验证这两种分类在新事务中恢复并只保留一个 attempt。既有双 worker 与接管/fencing 回归仍在精确组中。 | 原乐观并发行为保留，新增已识别事务冲突走同一整轮恢复路径。死锁/序列化测试为保存前注入控制，不是实际数据库死锁/40001 复现。 |
| 未知唯一约束和其他未知错误应传播 | 精确 ordinal 约束匹配拒绝名称未知、大小写不同及其他约束；classifier 的 Unknown、其他完整性类别均不能满足 filter。新增 73、97–98、106–108、117–121、164–170 行要求原始未知约束异常对象原样传播且只尝试一次。 | 未吞掉其他唯一约束、FK/check 或普通 DbUpdateException，错误不会被伪装为空队列。 |
| 取消应传播且不阻止必要回滚 | 生产 161 行轮首检查取消；冲突后以不可取消 token 回滚/清 tracker，再于 246 行检查取消。直接取消异常不匹配冲突 filter。新增 79、99–100、106–108、117–121、171、175 行要求取消异常、一次事务、无持久化 attempt。 | 最后一轮冲突后的取消也会传播；不会带着已取消 token 跳过回滚。 |
| 只有一个有效 lease/attempt，保留现有租户/类型过滤、优先级、接管及 fencing | 领取查询过滤和排序未修改；`SpaceJob.Claim` 设置 attempt number、ActiveAttemptId 和 lease 状态；既有 RowVersion token 与唯一索引共同保护并发。新增 42–47、63–68 行要求败方 null、一个 attempt、赢家 attempt 与 Job 活跃租约一致。既有 `Expired_lease_takeover_abandons_old_attempt_and_fences_old_worker` 等仍通过。 | 无新跨租户读取、共享 DbContext 并行操作或 lease 协议变化。 |
| 不改迁移、依赖或 DTO | 完整 Git status/diff 的生产修改仅 ledger 的取消检查与冲突筛选；测试 helper 支持 interceptor 并拆为 partial，新增六项回归。无 csproj、锁文件、迁移或公开 DTO 变更。 | 范围符合专项修复要求。 |

## 已有验证证据核对

本审查复用下面已有本地执行，实际读取各原始 TRX、result/entry-result、两个 exact-case-set-audit、源码/runtime 清单及修复后构建记录；没有重新执行。

| 来源目录（均在 `D:\CP6\tmp`） | 原始 TRX 结果 | 实算 TRX SHA-256 |
| --- | --- | --- |
| `wp6-pg-formal-matrix-floor-fix/entries/space-jobs-generation-retention` | 17 项：16P/1F；失败为原双 worker 用例，异常链含精确约束的原生 23505 | `2038CECC286F9676FCAB7EE95E865A45AA40676AEEF79374B2A2FEEF9EA79D7F` |
| `bug159-pg-coordinated-native-red` | 1 项：0P/1F；协调回归仍出现同一原生 23505，observer 记录精确约束 | `0D445FC2EFD3958E269674F4F0327A65DE7B490BE1488124734E87617C990720` |
| `bug159-pg-jobs-exact-green` | 23 项：23P/0F，零 notExecuted/skip；协调回归观察到真实 23505 后通过 | `854A1593302F64A5F1702CB829B1AEF7523AE8BAA6C0750426F374E329DD561C` |
| `bug159-sql-jobs-exact-green` | 23 项：23P/0F，零 notExecuted/skip | `D05427A53BA4ABD78467564DAAE85A01F84146FD113102D5382AF386B540D2D1` |

两库原始 TRX case names 与各自 result 和 exact-case-set-audit 完全一致；与原失败入口的 17 个实际 case 对照，Missing=0，Additional 恰为新增六个 `Claim_*` 用例。两个 result 实际哈希分别为 PG `DB486CAA587C9404E752BE3BEC0FE1963805D4DE92C618C3EA1D6E6D110B2B65`、SQL `8C7C0BA0F2C24B32384CF95C86C086C0E64B3A11B21CAE8170041CA23ABD1D95`，均与各自 audit 中 ResultSha256 一致；TRX 实际哈希也与 result 一致。

修复后 `build/project-01.json` 记录相关 IntegrationTests 工程 restore/build exit=0；实际 `build/private/01-build/stdout.log` 尾部为 Build succeeded、0 Warning(s)、0 Error(s)，其 SHA-256 与构建记录一致。SQL result 明确 BuildExecuted=false，复用同一产物；这仍是已有执行结果，不能称为审查员新运行的验证。

README 与四份项目状态准确记录“本地通过，远端待交付”，保留原 Matrix 失败及后续未执行范围，说明 injected controls 的限制；`.gitattributes` 只增加本专项 native 证据字节保留规则。未发现凭据、机器配置、调试残留、范围漂移或门禁弱化。对已跟踪差异执行 `git diff --check` 返回 0；新增源码与 README 另按实际文件阅读，未遗漏 untracked 文件。

## 问题与交付判断

- **P0：无。P1：无。P2：无。没有代码审查阻断项。** 未将纯风格、无关重构或待生成归档文件列为问题。
- 实现优点：采用现有 provider-aware classifier 且精准匹配唯一索引；回滚与取消先后顺序正确；沿用原领取次数和 lease 协议；协调原生回归与控制测试清楚分开，既有任务/处理/checkpoint/生成/保留用例没有删减或跳过。
- **当前源码与文档可交付。** 归档/manifest 和最终 index 本地审计、正常提交/PR、远端 main 包含性核对、必要合并后原步骤冒烟、自有临时库清理及 Issue 关闭仍由根代理按实际结果完成，本报告没有代办或提前宣称这些流程完成。

未验证边界：本审查没有重跑应用、接口、数据库或全仓测试，没有制造 native deadlock/serialization，也未验证新的生产候选、环境推广、既有数据库搬迁、整个 WP6 最终双库矩阵或最新应用恢复验收。局部两库 23/23 只证明本 BUG159 的相关已选用例；原 WP6 失败维持失败状态，父任务 #134 的整体验收仍未完成。未启动/取消 Actions、修改工作流/分支保护或部署任何环境。
