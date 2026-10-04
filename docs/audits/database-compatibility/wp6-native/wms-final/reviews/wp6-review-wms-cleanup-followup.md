# WP6 清理等待与 WMS 合并：只读增量审查

**结论：本次限定增量未发现 P0/P1/P2 实质阻断。** 清理仍以回执、所有权、物理身份和会话可见性为前提；等待只处理精确会话错误，普通 DROP 不终止会话。WMS 合并保留 WP6 owned fixture 验证，并将 BUG163 拦截器接入两种 provider。清单只扩展 WMS 8→25，其他 61 个入口未变。

这不是 WP6 验收通过或 #134 完成声明。当前代码的最终双库 Matrix/Application、恢复审计、归档及正常远端交付仍由 root 完成；本审查没有执行或认证其最终结果。

## 审查身份与边界

- 审查者：`/root/wp6_wms_cleanup_followup`；日期：2026-10-04 UTC（任务起始本地日期 2026-10-03）。
- 工作区：`D:\CP6\tmp\worktrees\db-compat-wp6-20261003`；审查前后 HEAD 为 `e940825fc7511b41a1a80b4b73897dcc69e51bbb`，工作区无已跟踪改动。
- 已确认 `origin/main`：`6322ac152cab7d462719169fb0301f49e9ee39ba`。
- 只读核对实际 Git diff、源码、文件哈希、已有公共 JSON/TRX 和文档新增顶部。未运行 .NET、数据库、sqlcmd/psql、构建、测试、API 或进程变更；未转派、未修改已跟踪源码/文档、未推送或操作 Actions。仅新建本报告，未覆写旧报告。
- 使用 review 技能的结构检查；仓库已确认的增量节奏与本任务只读限制覆盖技能的全仓运行、环境初始化、修复及转派模板。检查单读取自 `C:\Users\tt\.agents\skills\gstack\review\checklist.md`。

## 增量核对结果

1. **清理等待。** 读取 `git diff e53ec9f1^ e53ec9f1` 的真实实现差异。Lifecycle 的旧 `BF192F54…`→当前 `D96CC590…` 仅新增 `Wait-Cp6OwnedDatabaseIdle` 并替换全目标预检、逐 DROP 重检入口。`SessionWaitSeconds` 默认 15，允许 0..60；每次 poll 调用原 `Test-Cp6OwnedDatabase -RequireNoSessions`，依次重核 Context/Receipt、允许状态、数据库名/task/owner、PG marker、physical identity、principal/owner、SQL session visibility。仅异常消息大小写精确等于 `CP6_COMPAT_DATABASE_HAS_SESSIONS` 时按 stopwatch deadline 重试；其余异常立即传播。等待限于每次身份/空闲核对的重试预算，不是整组清理或所有原生调用的统一 15 秒总时限。
2. **清理安全边界保持。** 首次变更回执/执行 DROP 前仍先核对全目标；每个目标在 DROP 前再完整核对。新增会话可等待自然释放；查询与 DROP 间出现新的会话仍由原生普通 DROP 拒绝，未加入 KILL、pg_terminate_backend、FORCE 或 SINGLE_USER。成功 DROP 后仍查询 absence 并写 Absent。身份变化会先于会话判断拒绝，不能在等待中接受替换数据库。
3. **离线控制的真实适用范围。** 新控制脚本只使用 module-local fake state，覆盖两库预检短暂会话、预检后至 DROP 前会话、真实 stopwatch 持续会话超时、等待时 owner/physical 变化、初始错误 owner、零等待，及 SQL 无会话可见性。原 Lifecycle 控制只给既有持续会话拒绝用例添加 `-SessionWaitSeconds 0`。已有 RED 为 Failed/0 checks/精确会话错误，GREEN 为 41/41；已有 42 controls 记录明确引用 root 原执行 chunk `c9a39d`/exit 0，写记录不是新执行。本审查没有重跑这些控制。
4. **已有实际清理控制。** 只读 helper 和原件。PG 持续会话 1.1151292 秒后拒绝，11 份 receipts 不变，两个受控客户端 exit 0/自然退出；短暂会话后四库普通 DROP、七库原已 Absent，最终 11 Absent。SQL 持续会话 2.5781141 秒后拒绝、精确 receipt 不变；短暂会话自然结束后一个精确数据库普通 DROP/Absent。Helper 固定 terminal Failed summary、精确回执范围及已有证据哈希；最终再核对原 summary/entry/raw reports。注入会话控制只证明清理状态机，不是新的 native 业务回归或成功 Matrix。原瞬时会话的具体类型未被原 count 捕获，不作归因。
5. **失败原件仍为失败。** 当前只读核对 PG 旧 summary SHA `EEF84ED7…`、Status=Failed、stage=owned-database-cleanup、精确会话错误；其 34 个 entry 均 Success 且原 raw report 与绑定哈希匹配。SQL 旧 summary SHA `8259FAEA…`、Status=Failed、stage=wms-business，WMS entry Success=false；原 WMS TRX SHA `5AD83882…` 与绑定哈希相同。清理记录未将这些 Failed 改称 Passed；receipt 后续变为 Absent 是独立清理结果。
6. **WMS fixture 自动合并。** 比较 `a7d819c8^ → a7d819c8` 及当前 HEAD：只添加 `IInterceptor[]` params、两分支 `AddInterceptors`、partial 和 `ITestOutputHelper`。原八测试正文无变化；WP6 role selection、`OwnedTestDatabase.FromEnvironment`/`VerifyAsync`、owner 验证在迁移前的顺序、provider migration profile、tenant context 和 task-owned Dispose 不删除数据库均保持。空 params 保持旧调用契约；新 BUG163 trace/fault 参数均经统一 Create 注入当前 provider options。
7. **已交付 BUG163 的复用边界。** `LpnService.cs`、`LabelJobService.cs`、`WmsProductionLocalTransactionTests.cs` 的 Git blob 与已交付远端 main 精确相同；只核对新 partial 的 fixture/输出/拦截器交互及名称，未重新审查整套生产修复。BUG163 原两库 25/25 和合并后各 3/3 保留其原执行来源；它们不能代替当前合并 fixture 下的最终 WP6 25 项执行。旧 BUG163 审查原始 raw hash 不被改写为本次工作区 hash；新 partial 的 LF 字节 hash 可重现旧审查 hash `1A5A2D02…`，远端 blob 一致性另有明确核对。
8. **精确清单。** 比较 `a7d819c8 → e940825f` 的 JSON：62 entries 中仅 `wms-business` 改变，其他 61 项对象完全不变。WMS 的 25 个独立 required names 等于两个 SourceDefinitions 的 `[WmsProductionFact]` 方法集合，包含原 8＋新增 BUG163 17；Filter 精确按 required names 组成，CaseCommand 仍为 `--filter`＋同一 Filter。保留原两库 8-case provenance，新增两库 BUG163 25-case provenance；四份原 TRX 哈希匹配，两份新增原 TRX 各25P/0其他，精确覆盖全部25名称。这是历史来源核对，不是当前 Matrix。
9. **准备证明。** 只读已有 entry 510 控制、results 469 assertions/61 archived reports、62-entry manifest 和四份 plan 记录；分别为 PG Matrix34、SQL Matrix37、PG/SQL Application各17，共105个适用入口。准备、计划和 checkpoint 都明确 NativeExecution=false/FinalAcceptance=false 或 RemoteDelivery=false，不宣称真实执行。
10. **归档与文档。** 新 post-merge 目录37文件=36份 originals＋manifest；本次只读逐份核对36份 archive 及原来源的字节长度/SHA，无差异，manifest SHA `5309C7BD…`。既有 checkpoint 保留索引字节核对。两库 BUG163 green/execution 历史目录相对 origin/main 无差异。新增公共归档定向秘密模式搜索无匹配，未把此限定检查宣称全仓安全审计。四项目记忆、WP6/plan/runbook、新 BUG README 顶部保留历史且区分 Closed BUG163 与仍 Open 的父#134/WP6；新增顶部12个本地链接均存在。相对 BUG163 合并前 WP6 的四记忆文件仅10行新增、0删除，未覆写原失败时点。未发现事实性阻断；保留的旧待交付/待清理段落已由新顶部明确标为历史。

## 旧审查复用与限定补充

- `wp6-review-fixtures-results.md` 初审30个源码指纹中21个与当前原始字节完全相同。9个差异中7个属于已有 role-selection 修复，其后续冻结清单11文件当前全部 SHA/长度匹配；余下 WMS fixture 和 manifest 已在本次增量核对。初审 Results/parser 及 runtime 选择实现结论继续适用，不将旧选择单元运行称为当前整套二进制的业务执行。
- `wp6-review-lifecycle-entries.md` 的 runtime dependency binding P2 已在原后续解决；Entries 与对应控制脚本当前仍精确等于该冻结哈希。Lifecycle 的未改动所有权/备份恢复部分复用旧审查；其新增等待独立适用本次核对。
- `wp6-review-application-process.md` 初始17项中14项原字节未变；A1/A2 修复后的 Application、SignalR 及 Application 离线控制均与原后续冻结哈希精确一致。Process/HTTP/Baseline、snapshot/replay 等未变实现复用原结论，不重新审查。
- **ApplicationProbe lock 限定补充：** 初审记录 `6155B974…` 与当前 RuntimeProbe lock 的真实字节相同，可用该文件重建旧依赖图；当前 Application lock 是 `2D64365A…`，不能称其旧哈希未变。静态逐包对象比较，只新增9项，无删除，共有条目的 type/requested/resolved/contentHash/dependencies 全部不变：SignalR.Client、Connections.Abstractions、Http.Connections.Client/Common、SignalR.Client.Core/Common/Protocols.Json、Extensions.Features 为8.0.12，System.Threading.Channels为8.0.0。Application csproj仍明确SignalR8.0.12；旧A1补充审查明确用该8.0.12 XML核对 API；既有 `wp6-application-probe-build.json` 记录 NewProjectLockGenerated=true、restore/build exit0。该真实依赖增量经本次静态核对无实质风险，旧锁文件本身不作为当前字节绑定依据；当前运行还须使用真实当前构建。
- `wp6-review-runner-root.json` 冻结的主 runner SHA 当前精确匹配，原入口选择、失败保留、cleanup 后才 Success 结论继续适用。原各分区报告及补充记录未改写。
- 当前 root 已报告七个项目完成新的真实构建、PG core/WMS入口已 Passed、其余继续执行；这是 root 的进度信息，本审查没有把它认证为终态矩阵。Application 会真实重建相关项目/发布当前 API，后续 SQL 同产物复用需逐字节证据，最终恢复审计、归档、远端包含性仍是 root 的交付范围。

## 本次核对的源码和文档 SHA-256

以下均为当前工作区实际文件字节；其中两份生产服务仅核对与交付 main 的 blob 一致性，不表示本次重新审查业务实现。

| 工作区相对路径 | SHA-256 |
| --- | --- |
| scripts/database-compatibility/DatabaseCompatibilityLifecycle.psm1 | D96CC5903DABA60CB242D715E80173C955C0191E854044F5C1F412ED1977796C |
| scripts/database-compatibility/Test-DatabaseCompatibilityLifecycle.ps1 | E21E851B91DDFD3553A5096DCE6EFE6EFA11684E4868466E0A018C23537F4EA7 |
| scripts/database-compatibility/Test-DatabaseCompatibilityCleanup.ps1 | 4B7A76B3617121A82A9C917B758141E7B92FCBE0197C9F9F1A114F6911407966 |
| CP6.Tests/WmsProductionSqlServerTests.cs | 39D64CAE58653A4C11780D5716FD01A3563205E379BAAED16E5955E433301F34 |
| CP6.Tests/WmsProductionLocalTransactionTests.cs | 90B84E39F5857415CB859F27EFB0EF8C1A8048EEC80A1D86587811BFD6D1392F |
| CP6.Core/Services/Wms/LpnService.cs | 43C63948728DD9A833D4061E2E11A90E539AA32E5BCA143DFE54D4B1FD10B0F3 |
| CP6.Core/Services/Wms/LabelJobService.cs | AF769BC6F9B558FEB201EDA9CA925E815E23AD2D943406554BD2AC233857028E |
| CP6.Tests/Infra/WmsProductionFactAttribute.cs | F8EB2111639F00C526529C0F2D44A151C5AA32C2DB8E38BF82DD71C8AEABFF66 |
| eng/database-compatibility/OwnedTestDatabase.cs | 98625EA55407AA8168F008383F3EFC4010D29E79D6A1E00580F4908CA4AD23BA |
| eng/database-compatibility/required-cases.json | 9E4BDCCCD79198FF199F39D14FAF8CCA140FBD120FCD98F254167730FB231FFE |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/packages.lock.json | 2D64365AFA0098C3AB9D8CDEEE4C75584D89708C85A1AAB5B07D99D1ED22F83B |
| docs/audits/2026-10-03-bug-163-wms-local-transactions/README.md | E40BB480466BDC5DCFA00B591BD354CD9926F3F5B25830010FD699311970ADF5 |
| docs/audits/database-compatibility/WP6-IMPLEMENTATION.md | 2C998B98E0985FD0803A387945E7D327A3703948A2BFDA353487EAF942FA8C03 |
| docs/devops/DATABASE-COMPATIBILITY.md | E8491E1BB5E04592CC006864CCE92C6E10990307E0EF30AA8AF1645461E92A74 |
| docs/superpowers/plans/2026-10-02-database-compatibility.md | B35126812654904928FF8ACEA26B1BDE685069E47DC6250B7C8AA4EE6B30434B |
| docs/project-memory/PROJECT_STATE.md | A52217592CDE8163762581788A135DD7154B35E4823D027A83A582449FCC1D3F |
| docs/project-memory/05-Completed.md | 2B159C90560BF8CD1EAD8EDE9DA7C32A5788613F225AE35D6116B3573CCA8502 |
| docs/project-memory/06-Todo.md | 70BDBA1FDEDCA9E6F51D531B20F3F00207A1B09E5D6C06FCBC8F25646B6FE708 |
| docs/project-memory/CHANGELOG-AI.md | 97E542012FC1E8A5DB0AFD0221DB15A09D8FEBD069D74728779118E5352BA3C5 |

关键旧结论复用的当前指纹：

| 工作区相对路径 | SHA-256 |
| --- | --- |
| scripts/Test-Cp6DatabaseCompatibility.ps1 | 690CE5F116E6678DC7B97AEA428C44F304CA89E79ACF7180FA405FDF0D3BDF09 |
| scripts/database-compatibility/DatabaseCompatibilityEntries.psm1 | 5CF53766FEAC54CEF69C6F1768C75D508EC99EA1DB34D80E85DF52647AF94299 |
| scripts/database-compatibility/Test-DatabaseCompatibilityEntries.ps1 | 1987DC5CAD3D5CADEC373677E81045734FBBDEE3FC0BABB314027AB83F13116C |
| scripts/database-compatibility/DatabaseCompatibilityResults.psm1 | AD66A8EDA85E1283649AF010043D85B1D1744008837EB01E529FB7D79491D9C1 |
| scripts/database-compatibility/Test-DatabaseCompatibilityResults.ps1 | 875152820D5715FDAD57408B8D856261BAFDAC769715971721703324337E17EB |
| scripts/database-compatibility/DatabaseCompatibilityApplication.psm1 | EE12DD7410221E66A3FAE553631E8603A80323B43630D849384DCF6B51817350 |
| scripts/database-compatibility/Test-DatabaseCompatibilityApplication.ps1 | 5A6E73416A5535DA6F143701E0B1E8BC70457F0F6A4BF00ABA48AECF46344A25 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/SignalRVerification.cs | 0E0A16B900C48CD751E0B368248ECD202648D48A8576C58D4E05FDCCFA2964B4 |

## 可审计的已有原件

下列 tmp 路径均相对 `D:\CP6`；JSON/TRX 控制结果为原执行者已生成的证据，本次仅阅读与哈希核对。

| 文件 | SHA-256 |
| --- | --- |
| tmp/wp6-review-fixtures-results.md | C18EA7CD9B5F5FF1EB2C5221DF80E87A9CF8E41283640649B81058256B006836 |
| tmp/wp6-review-fixtures-results-followup.md | 8490CB285536CA031E7E1107C8940183A30123F1EB1AC5B49F21134CF1127ABB |
| tmp/wp6-review-application-process.md | A191AB6186F47C478A2A44F31AF4C94DED85A9257D526BD3F4FA7A1DC4B4BCDE |
| tmp/wp6-review-lifecycle-entries.md | 2A34B8EFB9CFACF20A67F8159B6455214F304324D60AADD52A1A05F39BF03453 |
| tmp/wp6-review-runner-root.json | 33E501481D25132EE40288DDBE211DB6FC3D650DDE3F838F3A68E945756BBC88 |
| tmp/wp6-cleanup-wait-offline-red.json | D37EBAE8DC7F55490F5E2749E92C10877075DBF7FDF6CFD5FDFACECBF67C5C59 |
| tmp/wp6-cleanup-wait-offline-green.json | 1477881C952B0E3D7DC082292B2D592627756A797E6DE0D1D20E0D61FAC202D4 |
| tmp/wp6-cleanup-wait-regression-record.json | 76B123EEF606506CF1C30894E5A9A0BEB2FAD3FC444D4E6148B367FFE509B720 |
| tmp/Complete-Wp6CadMatrixCleanup.ps1 | C68FE11851D9D84164BEBC58470E45731644586D98B79121F6A031B8665F6B50 |
| tmp/Complete-Wp6WmsSqlCleanup.ps1 | 2788B1F69A396D2A36764B0CAEAE76A3C50F2625A74F781AB2B48E2AB93A1EB0 |
| tmp/wp6-pg-cad-matrix-cleanup-recovery.json | D9A4055B48D461743B4CEA31CCA57B3D874329CEA06EF158CF0CB01E79874C20 |
| tmp/wp6-sql-wms-cleanup-recovery.json | 8D45DC7FF688703326CF58B57CDE6987096E27144FBE0CCE7EE26B0EAD781C30 |
| tmp/wp6-bug163-main-integration.json | 7326F29481379D905CE721D7D8BF93EB983C8E5EC1637B6E73897B18802D1B10 |
| tmp/wp6-wms-final-preparation.json | 3A95C4994A478B75106188886487483B407BBDDD8A62AD668BFB0512192D3C25 |
| tmp/wp6-wms-final-entry-offline.json | B67231411F70DF0F8E8BAA03A2F7F2A156FA05D81FD7E66E96231153DDD0BF5A |
| tmp/wp6-wms-final-results-offline.json | CE74BC8772D53BFB3BF9DFCC3403920577368C9AD164E551DF28EEEB93F21FE9 |
| tmp/wp6-wms-final-plan-verification.json | B356FCF187361917214580B5996A78826BA815D3E6F52995B929B83B8FCF19A1 |
| tmp/wp6-wms-final-checkpoint.json | 5AE1BADD58491DC98FD5F5B0A4D297DB3113018F0E9142AF16483D17B7737D19 |

`git diff --check` 针对本次源码/契约增量无输出、exit 0；这只是差异空白检查，没有被记作构建或测试。所有结论只适用于上述实际源码、已有证据及明示边界；root 需独立核对最终输入、真实运行结果和远端交付。
