# BUG #143：恢复六个报价审计姓名列的容量

[Issue #143](https://github.com/GTX537/CP6/issues/143) 修复历史 SQL Server 重建迁移把三张报价表的 Creator、Modifier 放宽为 `nvarchar(max)`，而模型和继承属性始终要求 nullable `nvarchar(100)` 的缺口。独立分支 `codex/bug-143-quotation-audit-capacity` 从已确认远端 main `4d4e260819f0a1b59810403d016c69a2fd7319ae` 创建；该基线已含 BUG141，经 [PR142](https://github.com/GTX537/CP6/pull/142) 合入后 Issue141 于 `2026-10-02T19:34:16Z` Closed。

本 BUG 有效 RED **6 Failed / 7 Passed / 0 Skipped**。初版静态目标测试 **17/17** 后，真实 SQL 揭示 **102**（COLLATE方括号语法）与 **207**（缺列提前绑定）两个生产缺陷；各全目录/数据回滚 **15/15** 只证明恢复，不是容量迁移通过。定向纠错后目标仍 **17 Passed / 0 Failed / 0 Skipped、零 warning/error**；修正版本的 SQL 原生安装/重复、容量边界/依赖/晚失败回滚、独立完整审查和远端交付仍 Pending，Issue143 Open。不把脚本/模型断言当作真实 ALTER 验收，不宣称 WP2、全部业务或 PG API 完成。

## 独立原定义与历史原因

冻结 `d6074aaad3098b61adaeda902b92bf24b3eb0c04` snapshot blob `21ed77b4056ee44b4f44e61de13d5d34774d6934` 与当前 snapshot 相同；实际继承 [BaseEntity](../../../CP6.Entity/BaseEntity.cs#L21) 的 Creator/Modifier 均 MaxLength(100)。[模型/属性测试](../../../CP6.Tests/Persistence/QuotationAuditColumnCapacityRepairTests.cs#L31) 逐六列独立核对 store name、nullable、string、100、nvarchar(100)、Never/non-token，并反射实际领域 CLR 类的继承属性；没有从新 helper 导出 expected 定义。

| dbo 表 | 列 | 初版 migration 行 | 重建 Up 行 | frozen/current snapshot 行 |
| --- | --- | --- | --- | --- |
| T_Quotation | Creator | 73 | 126 | 4194 |
| T_Quotation | Modifier | 75 | 128 | 4272 |
| T_QuotationCalc | Creator | 97 | 149 | 4429 |
| T_QuotationCalc | Modifier | 99 | 151 | 4452 |
| T_QuotationDetail | Creator | 130 | 182 | 4502 |
| T_QuotationDetail | Modifier | 132 | 184 | 4520 |

[初版](../../../CP6.Core/Migrations/20260420115704_AddQuotationMSBBPA030.cs#L73) 六列为 nvarchar(100)。[重建 Up](../../../CP6.Core/Migrations/20260420121204_FixQtnCalcNoMaxLength.cs#L61) DROP/RECREATE 三表时显式使用 nvarchar(max)，但其同期 [Designer](../../../CP6.Core/Migrations/20260420121204_FixQtnCalcNoMaxLength.Designer.cs#L802) 六列仍声明 MaxLength(100)/nvarchar(100)。因此缺口是 Up DDL 与同期 model 不一致，不能归因于 Designer 删除 maxLength。旧136迁移、实体、model、snapshot 均不修改。

父任务此前对 exact-owned SQL 目录的 327 frozen maps/6501列全量比较只发现这六个 capacity 差异；原 native metadata 于 `2026-10-02T19:12:38.671591Z` 采集，SHA256 `FFD3CB71D3DCE7FB7F03722326BE4006629CD8A41F85908A0C4C657177FC73DF`。完整只读分析及复现留在 ignored `tmp/wp2-sql-column-audit-{native,report}.json`、`tmp/wp2-sql-column-audit.ps1`、summary.md；它是缺口来源，不是此修复的原生通过证明。默认值与其他WP2门禁由父任务独立处理。

## 前向修复与拒绝合同

Public [QuotationAuditColumnCapacityRepairSql.CreateStatements()](../../../CP6.Core/Persistence/QuotationAuditColumnCapacityRepairSql.cs#L16) 返回不可写的六条独立 SQL commands；[migration](../../../CP6.Core/Migrations/20261002193500_RestoreQuotationAuditColumnCapacity.cs#L13) `20261002193500_RestoreQuotationAuditColumnCapacity` 是 SQL-only、每条单独 SqlOperation、默认同一 EF transaction，Down 明确拒绝。main 当前 Core138→139；父任务已有 generation 的隔离库可正常追加为139→140，不能把当前条数断言固化成永久合同。

guard 要求 exact dbo 表/列名、原生 nvarchar（非 user-defined/assembly type）、nullable、非 computed/identity、max_length=-1 或200，并检查原 collation 非空、在 `sys.fn_helpcollations()` 中精确BIN2存在且仅ASCII字母/数字/下划线；SQL COLLATE不接受方括号名称，只有上述校验成立才拼接安全的原literal。已是100时保持原列，无 ALTER。max 列在metadata通过后用动态 `sp_executesql`、bit OUTPUT读取 `DATALENGTH(column)>200`，包含TABLOCKX/HOLDLOCK锁到外层事务结束；超长报 **51043**，包括尾空格及UTF16 surrogate pair字节，不使用LEN。动态读取避免缺列在guard前被静态编译为207。锁后重新核对定义/collation，再执行 `nvarchar(100) COLLATE validatedExistingLiteral NULL`。

缺表/列、未知类型/可空性/长度/原 collation 或锁前后 metadata 变化均拒绝。没有 truncate、截断函数、删改/补写原行、DROP/rebuild 依赖或吞错；SQL 自身依赖错误原样传播，默认 migration transaction 应整体回滚。实际超长/边界、不同排序规则、未知定义/依赖与晚失败回滚仍需负责人真实验证，静态 guard assertions 不能替代。

## 本地实际执行与保留的失败

实际 locked restore：`dotnet restore CP6.Tests/CP6.Tests.csproj --locked-mode --disable-build-servers -p:BuildInParallel=false -m:1 -nodeReuse:false -v:quiet`，exit0；[原日志](bug-143-restore.log)为空是 quiet 成功输出，不伪造构建细节，也没有改锁文件或 NuGet trust。

首轮 [RED TRX](bug-143-red.trx) 与 [log](bug-143-red.log) 为 **12 Failed / 1 Passed**：六个真实 missing ALTER 失败，另外六个属性检查错误地反射 snapshot 的 named property-bag CLR 类型，GetProperty 为 null。仅修正测试使用实际 Quotation/Calc/Detail CLR 类型，不改生产；有效 [RED TRX](bug-143-red-verified.trx) 与 [log](bug-143-red-verified.log) 为 **6 Failed / 7 Passed**，六个缺修复断言实际失败、独立六个 frozen/继承属性和 legacy136通过。两轮原件都保留，首轮夹具失败不算业务 RED。

实现后 [首 GREEN TRX](bug-143-green.trx) 与 [log](bug-143-green.log) **17/17**。随后只修正禁止 LEN/截断函数的测试 regex，避免函数左括号后的 word-boundary 产生假阴性；生产源未变。[最终 TRX](bug-143-green-verified.trx) 与 [log](bug-143-green-verified.log) 仍 **17/17、零跳过、零 warning/error**。本次17项包含六原定义、六完整脚本修复断言、旧136链保留、public只读精确六目标、metadata/DATALENGTH/collation/data-preservation guard、六默认transaction SQLops与 provider/Down 拒绝。

初始四轮均真实执行 `dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter FullyQualifiedName~QuotationAuditColumnCapacityRepairTests --logger trx --results-directory ... --disable-build-servers -m:1 -nodeReuse:false -p:BuildInParallel=false --verbosity quiet`；独立目录/逐文件 SHA/bytes 见[验证清单](verification.json)。原件留在 ignored `tmp/bug-143-tests`，初始9文件和后述纠错轮两文件逐字节归档，`-text` 保留原 SHA。原 FK12+索引25 成功证据复用 BUG141 的相同 helpers/tests/定义来源，不重跑或改称本次执行；其 registry 断言已是 Contains-specific migration，新增本 migration 不引入永久条数依赖，legacy136本次重新验证。

## 初版真实失败及定向纠错

初版 source helper `61B7C6CD…` 与 Core `FDB1C2BC…` 的静态17通过后，负责人使用实际编译应用执行[初始化](wp2-init-bug143-real-ef-late-overlong-rejected.json)，实际进程exit `-532462766`、无HTTP；[sanitized失败摘要](bug-143-original-real-ef-syntax-failure.json)记录第一列 ALTER 报SQL **102**，没有到达预期最后列超长 **51043**。[完整回滚比较](bug-143-sql-compare-before-real-ef-rejection-to-after-real-ef-rejection.json) **15/15**保留352表摘要/行数和6832列/索引/FK等捕获目录，仅说明失败事务恢复。原应用stdout/stderr留ignored，摘要保留原SHA；不归档原日志或直接密码哈希。

[原执行输入](bug-143-original-actual-input.json) SHA256 `5AE3F3E93AFAFAC07B5226E9B329ADB72AC7129F62430AA3FA488136E3C6A7ED` 和[原EF脚本](bug-143-original-generated-migration.sql) `C2934D6A635A8D45903203C862E60A822A0A092A715439449BCDF68C5034313E` 逐字节归档，明确属于失败版本。三份16MB全目录捕获和原应用日志留ignored，清单只存来源/hash/bytes，不能把原Core或该脚本当作纠错后的执行输入。

随后[原缺列诊断](bug-143-missing-column-original.json)与[实际输入SQL](bug-143-missing-column-original.sql)证明 fixture已建立，但返回 **207** 而非51043。诊断query进程exit0是捕获错误后的包装结果，JSON Status=Failed、ExpectedErrorObserved=false；不是成功guard。[全状态恢复比较](bug-143-sql-compare-after-real-ef-rejection-to-after-original-missing-column.json)也 **15/15**。另[manifest setup失败](bug-143-input-manifest-setup-failure.json)是猜测不存在的CP6.Platform.dll，发生于DB访问前；改为发现实际五个包程序集，不算migration RED。

只定向修生产helper两处：校验native collation后使用无方括号literal；metadata guard后动态读取DATALENGTH并输出bit，保留同一事务的锁和重验。migration/模型/旧文件均未改。新增[纠错后TRX](bug-143-native-fix-green.trx)与[log](bug-143-native-fix-green.log)真实 **17/17、零skip/warning/error**，同filter/flags，增加安全collation与延迟绑定的source断言；它仍是静态门禁，修正版真实验收Pending。所有原17结果与102/207/setup失败保留，不改称预期拒绝或native GREEN。

**TestBuildProducedActualApi=true；StandaloneApiBuildPerformed=false。** CP6.Tests 的正常 ProjectReference 构建生成 actual API/Core，没有独立 APIbuild 命令或 api-build.log；实际构建来源是五轮transitive dotnet test日志。纠错后 `CP6.WebApi/bin/Debug/net8.0/CP6.WebApi.dll` SHA256仍 `3AAD7F977AF059AEBEA728D1251C0C4F17776064718D09C7321E717EACBED470`，同目录 `CP6.Core.dll` 已变为 `A277E0BF86A6F66CD1A11F4885F7E34B7FD0CA17F1291F9ACF49854502521927`。须绑定Core与三源，不能仅凭未变API hash复用原失败执行输入。旧source/runtime hash与新值在清单分开记录；source commit/docs变化不要求重编译同一输入。

本 agent 未连接DB/读password receipt、准备native fixture、改PG/WP2或现有环境。负责人拥有真实隔离 SQL 及后续native验收；当前独立完整任务审查 Pending，远端main包含性/正常PR/Issue关闭 Pending。未push/PR、触发Actions或部署，取消项0。
