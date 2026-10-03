# DB-COMPAT-01 WP2 模型回归测试记录

日期：2026-10-03。任务分支从已确认 `d6074aaad3098b61adaeda902b92bf24b3eb0c04` 创建，保留任务改动并依次fast-forward至各独立BUG主线；当前是BUG143/[PR144](https://github.com/GTX537/CP6/pull/144) 合入的main `94c0f8c9cac63a72008c4e6b242c94358f25b305`，BUG137/139/141/143均Closed，SQL原合同oracle仍固定d607。本记录描述 [DatabaseModelCompatibilityTests](../../../CP6.Tests/Persistence/DatabaseModelCompatibilityTests.cs) 的RED、回归与frozen SQL oracle，并区分相邻测试和真库证据。相关 **151/151**、独立索引 **25/25** 均零跳过；历史PG fresh33/33、既有PG完整限定 **36/36** 和334表首次/重复种子保留各按原来源复用，后续PG报价 **2/2**为另一轮实际执行。最终SQL136→140四项升级 **2/2**、完整目录/写入/负对照 **35/35**、未知history **2/2**通过；同新API首次/重复exit0/noHTTP，352表两次seed verify各 **2/2**。30/45文件review及两P2定向复查、后加四文件与reader两行增量review保留原范围。当前LocalVerified，8个owned库清理完成，WP2远端交付Pending；WP3–WP6及整体兼容未完成。细节、执行输入及全部原失败见[实施记录](WP2-IMPLEMENTATION.md)，下方旧日期结论不改称新执行。

测试使用实际 SQL Server / Npgsql provider 的四个生产 Context、设计时工厂、`IDesignTimeModel`、关系模型、`IMigrationsModelDiffer` 和 `IMigrationsSqlGenerator`。模型创建、查询 SQL 和 DDL 生成均不打开数据库连接，不使用 InMemory / SQLite 替代真实 provider。

## 有效 RED

生产模型/helper 修改开始前，在主任务授予的独占构建时段执行：

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter 'FullyQualifiedName~CP6.Tests.Persistence.DatabaseModelCompatibilityTests' --logger 'trx;LogFileName=wp2-model-red-verified.trx' --results-directory tmp/db-compat-wp2-tests/model-red-verified --disable-build-servers -m:1 -p:BuildInParallel=false --verbosity quiet
```

编译成功，零警告；**46 执行 / 24 通过 / 22 失败 / 0 跳过**，退出 1 来自测试失败。TRX counters 同时记录 `error=0`、`notRunnable=0`、`notExecuted=0`。有效 RED 在 Git 忽略目录 `tmp/db-compat-wp2-tests/model-red-verified/` 保留 `.trx` 与完整 `.log`。

- TRX SHA-256：`E99859DFB5E554CC2D228BD5812D3B62D1E248D61A4763FD7A1EDB90AE6EEDF6`。
- 该次测试源码 SHA-256：`50489FB12C6083FE455F82005E3FC07A15AB18CD92A6C2A0CD312E9F0EFDE286`。
- Source HEAD 是分支基线，加当时未提交测试及主任务新增 PG 项目 shell；不把基线称作完整实现提交。四个 production model 和新 helper 当时尚未修改。

| 覆盖 | 有效 RED 的实际结果 |
| --- | --- |
| 四个 SQL Server snapshot 的既有实体表/列/长度/精度/默认值/键/FK/index/check 合同 | 四项全部通过。枚举与 snapshot 存储整数按真实 provider type mapping 比较；新增 tenant-generation 表允许前向加入，不能借此修改已有实体。 |
| 四 Context、两 provider 的设计时/运行时完整关系合同及优化模型关键元数据 | 八项通过。 |
| 实际 tenant 查询 SQL、全局 refresh-token 唯一例外 | 两 provider 均通过。 |
| PG SQL Server 类型/表达式残留 | Core / Space 失败，实际 DDL 仍含 `nvarchar(max)`；Identity / ERP 该项已通过，不将它们虚构成 RED。 |
| PG 全部已有键/FK/index、属性 facets 与查询过滤、业务 check 的保留 | Core / Space 在 T-SQL check 尚未改写处失败；Identity / ERP 通过。 |
| 四 Context RowVersion | 四项失败：没有明确 8-byte max length；后续断言同时约束 bytea、concurrency、generated add/update、save Ignore、8-byte 数据库 check。实际 Platform 私有 setter 元数据另有三项通过。 |
| 两种 replay InputRowVersion | 两项失败：原普通字节映射/非自动生成行为通过，但缺少 8-byte 数据库长度 check。测试并未把审计输入视为自动 RowVersion。 |
| Sys_Lang 全局 NULL tenant 唯一 | 失败：只有 PostgreSQL 默认 NULL-distinct composite unique，不能约束重复全局 key。 |
| PG 标识符长度与 catalog 名称碰撞 | Space 失败：真实关系模型存在 70-byte 标识符；另外三个 Context 当前通过。 |
| UTC/local/date | Space 237 个明确 UTC DateTime 仍有 datetime2；Core 已确认 UTC 属性缺少读回 converter。DateTimeOffset 的共同 Offset=0 输入合同四项失败。 |
| 已有 BIN2 敏感字段 | Core / ERP 两项失败：仍保留 SQL Server collation 名称。普通数据库默认 collation 未被假设为 C。 |
| durable tenant generation | 两 provider 均失败：明确的持久实体尚未增加。 |

Npgsql 允许含非零 offset 的合法 SQL 时间字面量，但实际参数写入要求 Offset=0。这里 DateTimeOffset 断言约束本阶段明确选择的消费者模型 UTC 输入 converter，不能把 SQL literal 本身合法称为 driver 错误。金额/数量 precision 通过实际 provider store type 比较；新增 PG 类型不能默默丢弃原 scale 或长度。

## 保留的测试编写阶段结果

首次执行已成功编译并运行 46 项，实际为 **18 通过 / 28 失败 / 0 跳过**，同时有五个测试源码警告。该次发现测试比较把 snapshot 存储整数误当作业务 enum CLR 类型、SQL type regex 误匹配带引号的 RowVersion 列名，以及 bytea literal 格式假设过严。修正测试本身并清理警告后重新取得上述有效 RED；旧记录原样保留，不能把这些测试自身问题计作生产缺陷。

原始目录为 `tmp/db-compat-wp2-tests/model-red/`；原始 TRX SHA-256 为 `DCAEA5C03E3AEC5626E6663329697B18A7BAAA0B8EC6726AD14815C90DCD8620`。第一次命令与有效 RED 相同，但进行了正常 restore，目录/文件名为 `model-red/wp2-model-red.trx`，没有 `--no-restore`；没有更换 NuGet 来源或签名信任规则。

## 实现阶段的真实失败与 GREEN

全部运行由主任务协调本地独占构建槽；以下目录均在 worktree 的 Git 忽略 `tmp/db-compat-wp2-tests/` 下，原始 TRX/log 与真实失败保留。计数对应当次 filter 输入，不能把不同类的计数都称为模型测试数，也不能把未重新执行的旧结果改称新执行。

| 实际运行目录 / TRX | 真实结果 | 范围与结论 |
| --- | --- | --- |
| `model-first-adapted/wp2-model-first-adapted.trx` | 51 total / 49 passed / 2 failed / 0 skipped | 46 模型 + 5 初始化模式。Core LpnContent 丢失 SQL finalizer 的 nullable unique filter；Space DataBoundary 丢失 Int16 provider converter。另有 nullable check-name 编译警告，后续改为缺名 fail closed。 |
| `model-adapted-2/wp2-model-adapted-2.trx` | 51 / 50 / 1 / 0 | Nullable unique 已修复；DataBoundary 的 HasConversion&lt;short&gt; 在 finalization 前是 provider CLR metadata，第一轮只看 GetValueConverter 仍丢转换。 |
| `model-adapted-3/wp2-model-adapted-3.trx` | 46 / 46 / 0 / 0 | MapType 同时尊重显式 converter 与 GetProviderClrType 后，模型回归通过，零 warning/error。初始化五项复用前两轮真实成功，未在本次重跑。 |
| `model-with-text-services/wp2-model-with-text-services.trx` | 51 / 51 / 0 / 0 | 新 custom text mapper/query-generator 输入加入后，46 模型 + 5 初始化重新执行通过。Native EF key/fixup 和字符串结果比较由 text probe 另证，不与本次混淆。 |
| `init-mode-red/wp2-init-mode-red.trx` | 5 / 4 / 1 / 0 | 旧 runtime guard 不允许专用 initialization-only PG 入口的真实 RED 保留；普通运行拒绝 PG 的合同没有被取消。 |
| `init-ownership/wp2-init-ownership.trx` | 13 / 13 / 0 / 0 | 5 初始化模式 + 8 migration ownership；四 Provider profile/history/schema/assembly 归属及允许的单独初始化模式通过。 |
| `search-path-red/wp2-search-path-red.trx` | 7 / 0 / 7 / 0 | Connection factory / EF string / caller-owned closed connection 未固定 public，以及自定义路径未拒绝的真实 RED。 |
| `search-path-provider-green/wp2-search-path-provider-green.trx` | 85 / 85 / 0 / 0 | 38 DatabaseProvider + 7 DatabaseSearchPath + 40 DatabaseWiring；固定 public 与拒绝不支持路径通过，未连接数据库。 |
| `fixed-ascii-red/wp2-fixed-ascii-red.trx` | 6 / 2 / 4 / 0 | 四模型精确 ASCII 固定域/check 覆盖缺失；两个新 char/nchar unknown-domain 拒绝负例已过，不虚构为失败。 |
| `model-fixed-name-green/wp2-model-fixed-name-green.trx` | 65 / 65 / 0 / 0 | 52 模型 + 5 初始化模式 + 8 ownership；精确固定字符域 Core6/Space77、unknown/nchar fail closed、容量不一致和 rune-safe check 命名覆盖通过。 |
| `frozen-sql-oracle-green-verified/wp2-frozen-sql-oracle-green-verified.trx` | 5 / 5 / 0 / 0 | 四原 SQL 模型合同改用独立、固定 baseline JSON；另一个故意将 Sys_User.UserName 容量100改99的负对照被 oracle 发现。只重新执行这一变更涉及的五项，其他成功结果按输入范围复用。 |
| root `final-provider-model-initialization/wp2-final-provider-model-initialization.trx` | 151 / 151 / 0 / 0 | 本轮实际执行 Provider38、Wiring40、Model53、SearchPath7、Ownership8、Initialization5；不是151个模型测试，也不推断native SQL实际缺失FK已存在。 |
| root `index-repair-integration/wp2-index-repair-integration.trx` | 25 / 25 / 0 / 0 | 另一次实际执行已集成BUG139索引回归；保留legacy136 oracle和21项冻结定义，不将两次来源写成一次176项执行。 |

上述三份阶段性 GREEN TRX 的 SHA-256 分别为：

- `search-path-provider-green`：`A827C56A90B86679CE820A92D885B77305E6FE80067DFD19B03813106370A79F`。
- `model-fixed-name-green`：`E6796469A5661A9579FC1810CEC4F04134AE4BAED2B451D630EAA6CD88E14439`。
- `frozen-sql-oracle-green-verified`：`6207F2C04EE6E029969BC66EE5151B14FC31D5779E27CA8C0452792662DF81B5`。

本轮最后两次真实来源在 root `tmp/db-compat-wp2-tests/` 对应上述目录，TRX SHA-256 分别为 `A4297E89005C6B895615DBD0184CCD5A72E9ADDDAA67DAA29C4DAC5222EE5F05`、`A9BC8ACF1EF3DBA72B827EB1D199E397427D0EDBA498AA5C6E767455A6C5256D`；各自日志为 root `tmp/wp2-final-provider-model-initialization.log`、`tmp/wp2-index-repair-integration.log`，两次均零跳过。各阶段旧结果仍按当时源码/filter保留，未因本次文档同步而重跑或改写。

### SQL oracle 独立性与固定字符输入

现有 SQL 表/列/默认/facet/键/FK/index/check 合同从嵌入的 [sql-server-d6074aaa-contracts.json](../../../CP6.Tests/Persistence/Fixtures/sql-server-d6074aaa-contracts.json) 读取，并断言完整 BaseSha。四 context 原实体数为 **232/83/4/8**；新增 CrmIdentityTenantGeneration 是明确 forward 例外，Snapshot 的 SQL trigger metadata 是新 trigger 与 EF OUTPUT 相容的必要例外。原列/keys/facets 不因当前 mutable SQL snapshot 改变而自动成为测试 expected。独立反例证明 oracle 能抓到一个旧列长度变化；它不是每种可能模型回归的穷举证明。

固定文本声明来自 [PostgreSqlFixedTextDomainsV1](../../../CP6.Core/Persistence/PostgreSqlFixedTextDomainsV1.cs) 的 exact CLR full name/property/capacity manifest：83 项为 Core6 + Space77，Identity/ERP 无固定域。模型测试要求字符 storage、domain annotation 和 raw-byte ASCII/capacity check 存在，拒绝新一般 Unicode 固定列，保留原 MaxLength。该限制不能被扩大为所有 Unicode fixed-char padding 等价。Native text gate 的样例、真实失败、空字符串 range 修正、BIN2/C key comparer、normal value case-only edit 和 operation-result collation 在 [文本记录](WP2-TEXT-PROBE.md) 独立保存。

## 验证边界与后续

模型/生成 DDL 的成功不能证明数据库函数所有三值逻辑、全域 linguistic collation、每个 check/partial predicate、所有金额或业务行为。Root旧PG **31/31**保留其当时输入/scope，不增加后来raw default/unique证明。历史owned新空PG（尾号31d68591）实际 **33/33**，报告 `tmp/wp2-migration-pg-fresh-restored-order-full-write.json` SHA-256 `D66DF2EA7C5F8DB4C74FA8E40379CA5D313DC2EC784599299C90132D2F3BD7C8`；配套input/build/source/binary见[实施记录](WP2-IMPLEMENTATION.md#当前已执行证据与边界)。该轮覆盖四迁移链、**328表条目/208 token安装**目录、四actual Context代表性token写入、generation、NULL language、时间/金额与finance race，增加public search_path/column collation及缺index/错误nullable filter事务负对照。Check名称/validation/引用列一致不自动证明全部表达式语义，也不声称所有token表的业务writer已执行。

最新 `tmp/wp2-migration-pg-final-exact-history-full-write-gates.json` 于 **19:26:27–19:26:40 UTC**实际 **36/36**，SHA-256 `265D3F6E15963D871391F0C90463EE45665A4FFAC6EE7051349AE1D07F765C0C`。这是既有owned PG18.6复验：四owner均previouslyApplied=1/applied=1/pending0，各repeat保持exact同一history，**不改称新fresh**。SourceBase2d+未提交输入/binary保留；sidecar补正SDK10.0.302/1439 source hashes，原nullSDK无效sidecar保留，不改build/runtime/native执行。未知history负对照 `tmp/wp2-migration-pg-unknown-history-identity-rollback.json` **2/2**，在actualCore外txn拒绝未知ID、detach/rollback后恢复exact original完整history，未Migrate；两缺seed参数原JSON实际exit1/DatabaseVersion=null/Failed是preDB预期拒绝，不计业务Passed。原件哈希与适用输入在[实施记录](WP2-IMPLEMENTATION.md)。

时间分类保持 normal UTC instant 严格 Kind、DateTimeOffset 严格 Offset=0、legacy wall-clock 保留 ticks、DateOnly 保日期。MinValue/MaxValue ticks 是获准保留的 sentinel 例外，不能将它概括成一律拒绝所有非 UTC sentinel；真实 PG sentinel/infinity 与 microsecond 限定写入门禁已单独执行，不承诺 SQL 100ns 的完整精度。

主任务随后真实执行 PG 扩展只读 catalog/rollback missing-index 负对照 **20/20**，并在已种子的 SQL136 数据库执行 Core136→137 generation forward 保留数据夹具 **2/2**；其 actual counts、Snapshot/token、4122 language/admin 摘要、EF generation 与事务 rollback 范围见实施记录。前者不改 history，也不声称未来重生成基线 current；后者只证明捕获任务数据库及关键字段，不等同所有业务或生产升级验收。独立 BUG137 已由 PR138 合入 d507 main、祖先验证后关闭，它的交付不等于 WP2 已交付。

SQL full-catalog历史 **14P/3F** 来自不可变queue migration在native NOT NULL字段的冗余IS NOT NULL filter，gate仅在model/catalog均NOT NULL时消除，未改DB/模型/旧迁移；后续 **16P/1F** 报Core缺OrderType index。调查确认21个继承missing nonunique indexes；BUG139 source `083e9c4dcdb3d5b1aad3b57c32cb337f86859361` 的SQL137→138 upgrade/repeat各 **25/25**，352表hash/count与原index定义保留，八native guard通过且标明复用，经PR140/main2d交付Closed。明确历史AK/index aliases仍逐一定义校验，不ignore对象/新建冗余unique。BUG141四FK actualCore138→139/原生upgrade-repeat各9/9、guard11/11、WP2实际SQLOrder gate2/2通过，经PR142/main4d交付Closed；原NFR setup失败和combined NFR/untrusted范围保留。六报价Creator/Modifier容量另属 **BUG143 Open**：原static17GREEN后actualAPP SQL102及missingColumn SQL207失败保留，native rollback15/15不称capacityGREEN；最小修复重新freeze后正在native验收/独立审查。最终 **SQL136→140四forward尚未执行**，不能拼接旧generation-only2/2或独立BUG分项为最终成功。[实施记录](WP2-IMPLEMENTATION.md#sql-full-catalog-的真实失败与继承的索引遗漏)保留原始来源和边界。

独立 raw gate 已实际 SQL reference **2/2** / fresh PG **33/33 中该项** GREEN：65 默认 native typed evaluation、八 local `clock_timestamp()` 在同事务推进、四 exact enabled/valid/unfiltered global ordered unique 以 PG **NULLS NOT DISTINCT** 保留。初次 PG `IN @Tables` 的 **42601** 只计 query/setup 失败；修 `ANY(@Tables)` 后旧已安装 PG 的 **65 defaults + 四 uniques missing** 才是有效69缺项 RED，全部原记录保留，不以 pending migrations 代替实证。

另有 constraint-writes-only 双库 **各3/3**（owner + 金融 + Space）：`tmp/wp2-migration-pg-financial-space-constraint-writes.json` / `tmp/wp2-migration-sql-financial-space-constraint-writes.json`。真实 financial decimal(18,2) **9.995→10.00**、最大值/溢出与 missing parent FK 拒绝；实际 Space composite tenant FK 拒绝跨租户，**100个😀=200 UTF-16 units** 原样，**201 ASCII / 202 supplementary units** 拒绝、未截断且保留上次合法值。Fixtures 完整 rollback。报告及 binary/source hashes 在实施记录，scope 为原生存储/约束，不等于财务或 Space 完整业务/API 验收。

实际PG application的 `tmp/wp2-init-pg-full-restored-model-first-seed.json` / `tmp/wp2-init-pg-full-restored-model-repeat-seed.json` 均exit0/noHTTP；native `tmp/wp2-migration-pg-full-restored-model-seed-prepare.json` / `tmp/wp2-migration-pg-full-restored-model-seed-verify.json` 各2/2，首次后全部334表计数、migration histories、全局/tenant翻译及管理员昵称/密码在重复后保留。强制canonical label策略不在该断言内。普通PG实际运行的 `tmp/wp2-init-pg-ordinary-runtime-guard-preserved.json` 仍在DB工作前拒绝、未监听HTTP；binary/source和报告SHA见实施记录，不将不同轮次混称同一次运行。

本轮相同actual Order graph在 `tmp/wp2-migration-pg-order-relations-reference.json` **2/2**：EF合法六行、四个native孤儿拒绝、精确约束身份、四个删除Cascade及完整fixture rollback均通过。旧SQL136 `tmp/wp2-migration-sql-old136-order-relations-negative-control.json` **1P/1F**为有效RED：owner通过，首条Detail→Order应拒绝孤儿断言失败，其后未达到的断言不计成功。范围仅四关系/级联和该graph，不等于完整Order业务。Wrapper变量碰撞的preDB setup失败在脱敏 `tmp/wp2-wrapper-receipt-name-failure.json` 保留，修复后才运行上述有效结果；专用test role已轮换，`tmp/db-compat-test-credential-rotation.json`保留脱敏证明，未读取receipt。互斥mode在 `tmp/wp2-migration-conflicting-modes-rejected.json` + `.log` 访问DB前被拒绝，原报告的SetupOrRun Failed保留为预期拒绝，不改称迁移成功。

一次集中review已按初始化/Provider/工具 **30文件** 与映射/帮助器/迁移/模型测试 **45文件** 两分工完成；前者两个P2已通过精确history/seed参数前置校验修复并仅定向复查，未重开原scope。`tmp/wp2-review-initialization-probes.json` / `tmp/wp2-review-mapping-migrations.json` 保存各自file/hash、原30scope与新修复hash；原起止timestamps未记录为null，不事后编造。后加报价/最终四项升级四文件与容量reader两行修正已单独定向复核，BUG143源码与native由独立任务闭环，不将它们改称原30/45输入；原增量记录引用BUG143归档。

真实PG seed/首次重复、151/25两次相关单元和36/36按原输入复用；BUG143已在远端main包含、原步骤和native15/15核对后Closed。2026-10-03最终SQL四forward升级2/2、完整目录/定向写入35/35、history负对照2/2及新API两次初始化/352表保留均真实通过。原probe输入记录实际在native期间完成，verified sidecar只补正元数据及recursive output匹配，不伪称pre-run捕获或native重跑。当前8个owned库cleanup完成（[实际原件](wp2-native/wp2-cleanup-final-owned-eight-verified-env.json)），仅WP2 remote待完；普通PG API/worker runtimeguard保留，WP3–WP6和整体兼容未完成，不宣称生产验收。

原RED作者仅新增测试/记录，早前文档同步保留其历史执行范围；此前两审计补记也只核对脱敏原件。2026-10-03集中同步两审计、四状态与计划的当前结论，由主任务执行真实build/native/initializer，本次文档整理不运行.NET/DB、不读取receipt或raw应用streams、不执行Archive/push/Actions。原始2950源码hash是输入观察，不能扩大模型测试或AI review范围；已完成审查按原输入复用。

归档现已完成：140份prepared原件加最终源码字节证明/脚本2份，共142份，原件与副本hash/bytes核对142/142。[manifest](wp2-native/manifest.json)及[最终输入比对](wp2-native/wp2-final-review-source-applicability.json)保留原件身份；75个唯一已记录源码输入字节一致只证明复用适用性，不称新75测试/AIreview。远端WP2交付仍Pending。
