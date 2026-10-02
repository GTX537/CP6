# BUG #139：恢复订单模型遗漏的查询索引

[Issue #139](https://github.com/GTX537/CP6/issues/139) 是 SQL Server 历史迁移遗漏：模型已有 21 个非唯一查询索引，旧 Core 136 条迁移安装后目录中不存在。修复只添加 SQL Server 前向迁移 `20261002175000_RestoreMissingOrderModelIndexes`，不改模型、snapshot、历史迁移或已有数据；两个同定义异名的唯一索引不重复创建。

独立分支 `codex/bug-139-order-indexes` 从核对过的远端 `main` 基线 `d507f389fdcadd8e2fc5dcb9e40f752cde613703` 创建。定义 oracle 是保持原字节的 `d6074aaad3098b61adaeda902b92bf24b3eb0c04` Core snapshot，其 Git blob 仍为 `21ed77b4056ee44b4f44e61de13d5d34774d6934`。功能源码提交 `083e9c4dcdb3d5b1aad3b57c32cb337f86859361` 已通过真实 SQL 安装、重复执行、冲突拒绝、原数据保留和一次独立完整任务审查。本条记录本地验收时点；后续仅归档与状态文档修改，复用相同功能输入的结果。Issue 保持 Open，正常 PR、远端 main 核对及关闭尚待交付。

## 根因与定义

[最早保留的 Designer](../../../CP6.Core/Migrations/20260502225006_AddBpAndFscPA110.Designer.cs#L1634) 已含 `OrderType, IsDeleted` 与 `Status, IsDeleted`，但对应 Up 不建订单表。[订单恢复迁移](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs#L8) 明示原始订单 migration 丢失，恢复最小结构；其 [订单查询索引段](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs#L51) 只有 CustomerCd、OrderDate、McOrderNo。后续旧链未补齐此次 21 项。当前 [模型](../../../CP6.Core/EFDbContext/CP6Context.cs#L1508) 和 [snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L1866) 一直保留它们，因此不能依靠下一次模型差异 scaffold 自动生成。

全部定义为 `dbo` schema、NONCLUSTERED、非唯一、无 filter、无 INCLUDE、各 key 升序；下表顺序就是索引 key 顺序。[回归](../../../CP6.Tests/Persistence/OrderModelIndexRepairTests.cs#L111) 对每项从未改 snapshot 解析实际表、名称、物理列和这些 facets，独立核对 helper 清单。

| 表 | 索引名 | 有序 key |
| --- | --- | --- |
| T_Order | IX_T_Order_OrderType_IsDeleted | OrderType, IsDeleted |
| T_Order | IX_T_Order_Status_IsDeleted | Status, IsDeleted |
| T_OrderDetail | IX_T_OrderDetail_ApprovalStatus_IsDeleted | ApprovalStatus, IsDeleted |
| T_OrderDetail | IX_T_OrderDetail_CustomerDeliveryDate_IsDeleted | CustomerDeliveryDate, IsDeleted |
| T_OrderDetail | IX_T_OrderDetail_HaibaiNo2_HaibaiNo3 | HaibaiNo2, HaibaiNo3 |
| T_OrderDetail | IX_T_OrderDetail_ItemCd | ItemCd |
| T_OrderDetail | IX_T_OrderDetail_McTransferFlg_IsDeleted | McTransferFlg, IsDeleted |
| T_OrderDetail | IX_T_OrderDetail_ProductCatBig_ProductCatMid_ProductCatSml | ProductCatBig, ProductCatMid, ProductCatSml |
| T_OrderMaterial | IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_SortOrder | WebOrderNo, WebOrderDetailNo, SortOrder |
| T_OrderProcess | IX_T_OrderProcess_ProcessCd | ProcessCd |
| T_OrderProcess | IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_SortOrder | WebOrderNo, WebOrderDetailNo, SortOrder |
| T_PlateMold | IX_T_PlateMold_BaseCd_IsDeleted | BaseCd, IsDeleted |
| T_PlateMold | IX_T_PlateMold_PlaceCd | PlaceCd |
| T_PlateMold | IX_T_PlateMold_ProcessCd | ProcessCd |
| T_PlateMold | IX_T_PlateMold_RepresentativeProductCd | RepresentativeProductCd |
| T_PlateMold | IX_T_PlateMold_StDate_EndDate | StDate, EndDate |
| T_PlateMold | IX_T_PlateMold_TypeClass | TypeClass |
| T_SheetUnitPrice | IX_T_SheetUnitPrice_BaseCd_CustomerCd_IsDeleted | BaseCd, CustomerCd, IsDeleted |
| T_SheetUnitPrice | IX_T_SheetUnitPrice_RevisionDate | RevisionDate |
| T_SheetUnitPriceEstimate | IX_T_SheetUnitPriceEstimate_BaseCd_CustomerCd_IsDeleted | BaseCd, CustomerCd, IsDeleted |
| T_SheetUnitPriceEstimate | IX_T_SheetUnitPriceEstimate_RevisionDate | RevisionDate |

`IX_T_Order_WebOrderNo` 已由 `AK_T_Order_WebOrderNo` 实现；`UX_OrderDetail_OrderProduct` 已由 `UX_T_OrderDetail_OrderProduct` 实现。这两个唯一索引不在新增清单。

## 前向行为与可调用入口

[OrderModelIndexRepairSql.CreateStatements()](../../../CP6.Core/Persistence/OrderModelIndexRepairSql.cs#L36) 是 public 静态入口，返回只读的 21 个 SQL statement。[迁移](../../../CP6.Core/Migrations/20261002175000_RestoreMissingOrderModelIndexes.cs#L13) 将每句作为独立命令，保留 EF 默认同一事务；不要把它们直接拼成共享 DECLARE scope 的单一 batch。常规 `Database.Migrate()` 在旧 Core 136 库追加第 137 条；兼容任务的临时库已额外应用 generation forward 时，按迁移 ID 追加记录，不更改旧 history。

表缺失或同名索引定义不同会 `THROW 51039`。同名已有对象必须同时满足：type 2、非唯一且非 PK/唯一约束、无 filter、无 INCLUDE、精确 key 数量/顺序/物理列名、升序、启用、非 hypothetical、`IGNORE_DUP_KEY=0`；只有一致才原样保留。列名比较使用 BIN2；任何冲突不 DROP、重建或修正已有对象。另一 provider 在生成操作前拒绝；Down 拒绝回退，保持前向迁移政策。初次安装需读取表创建索引，SQL Server 原生锁与执行耗时仍须部署窗口评估。

## 本地单元验证

实际执行 `dotnet restore CP6.Tests/CP6.Tests.csproj --locked-mode --disable-build-servers -p:BuildInParallel=false -m:1 -nodeReuse:false -v:quiet` 成功，未改依赖锁、NuGet 信任策略或项目文件。单元验证仅运行 `OrderModelIndexRepairTests`，该阶段未连接数据库或启动 API；后续真实初始化由任务负责人执行。

旧生产源码上的 [RED 原始 TRX](bug-139-red.trx) 为 **21 Failed / 1 Passed / 0 Skipped**，21 项均先成功核对 frozen snapshot 定义，再在旧生成脚本中缺少 CREATE 时失败；[原始 log](bug-139-red.log) 保留。新增迁移后 [GREEN 原始 TRX](bug-139-green.trx) 为 **25 Passed / 0 Failed / 0 Skipped**，包含原 22 项及迁移发现/事务、完整 guard facets、provider/Down 拒绝；[原始 log](bug-139-green.log) 保留。GREEN 只将相同模型/脚本计算缓存以减少重复计算，没有删改原 21 项断言。编译及此定向执行日志无 warning/error。

两轮命令使用 `dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter FullyQualifiedName~CP6.Tests.Persistence.OrderModelIndexRepairTests --logger trx --disable-build-servers -m:1 -nodeReuse:false -p:BuildInParallel=false --verbosity quiet`，分别指定独立 RED/GREEN TRX 名称和结果目录。完整本机原件留在忽略目录 `tmp/bug-139-tests`；归档逐字节复制，[验证清单](verification.json) 记录输入、计数及 SHA-256，目录属性禁止换行改写。

本轮编译 API SHA-256 为 `4CF42E4D68980CB0D5C1D0F819E2CBD48B428749DF5CDEE0268369A7F6D2A5BE`，Core 为 `15E865AD90929B017680D07BA7FFD155238EFB10AEAB36816B65E800DA1E4076`。真实 SQL 两次初始化使用同一 API 二进制；这些仍是本地验证产物，未作生产候选、环境切换或部署。源码没有后续变化，文档提交不重复编译或业务测试。

## 真实 SQL 原步骤与保留证据

任务负责人在准确 owner 标记的隔离库 `CP6Compat_WP2_20261002_36ef9cae`、SQL Server `16.0.1000.6` 执行真实 compiled application DatabaseInit。[首次报告](wp2-init-bug139-index-upgrade-first.json)和[重复报告](wp2-init-bug139-index-upgrade-repeat.json)均退出 0、有完成标记、无 HTTP 监听。库已有 WP2 generation forward 的 Core 137 条记录，本次按正常迁移发现追加至 **138**；Space 保持 47。未更改已应用历史；独立 BUG139 base 上 registry 为 137 的单元断言只适用于其 136 条旧链。

[升级前](bug-139-sql-before-upgrade.json)、[升级后](bug-139-sql-after-upgrade.json)与[重复后](bug-139-sql-after-repeat.json)是三个原始 native capture。[升级比较](bug-139-sql-compare-before-upgrade-to-after-upgrade.json) **25/25**：352 张表的行数和 PK 有序 JSON 内容摘要状态原样保留，只排除 Core history 的预期 +1；全部 1,357 个已有索引的已捕获 metadata 相等，只新增上述 21 个 type 2、非唯一、无 filter/INCLUDE、升序有序 keys 的索引。[重复比较](bug-139-sql-compare-after-upgrade-to-after-repeat.json) **25/25**：全部 352 表、Core history 138 行及 1,378 个索引的捕获字段保持一致。

capture 不返回业务行或凭据。352 表中 25 张非空表有内容 SHA-256，327 张空表为 0 行、NULL digest（JSON 省略该 NULL 字段）；比较同时检查 count 和 digest 状态，不把它们宣称为 352 个非 NULL hash。索引证据涵盖 capture 中的 ID、type、unique/PK/unique-constraint、disabled、IGNORE_DUP_KEY、filter、keys、INCLUDE 与方向；未扩展宣称未采集的物理选项等价。

## 实际 guard 与失败边界

[实际 EF8 生成脚本](bug-139-actual-ef8-migration.sql) SHA-256 为 `789C7380E28EB141E112110899EC009FC2CA649B48974066E088C7856F4FEE15`。[native guard 汇总](bug-139-native-guards.json) **8/8**：同名正确保留；wrong column、unique、filter、INCLUDE、DESC、disabled 和最后一条冲突均按预期拒绝。七个负场景实际报 `51039`，每场景后的 352 表内容/count、history 和全部索引捕获 metadata 恢复比较均 **25/25**。晚冲突先在事务中删除 21 个测试索引，由实际 guard 创建前 20 个，再在最后一个 wrong-column 定义处拒绝；断开失败连接后，外层事务完整回滚并恢复原 21 个索引。

8 个实际 SQL 输入、native log 和恢复 compare JSON 原样归档；较大的 8 份逐场景完整 capture 留在忽略目录，逐个来源路径与 SHA-256 列于[验证清单](verification.json)。汇总中 correct-existing、wrong-column、unexpected-unique 的三次已完整成功执行明确为复用，不称 `ResumeFirstThree` 新执行。

[尝试边界](native-attempt-boundaries.json)区分两项早期失败。首次 correct 执行及 capture/compare 成功，但空 stdout pipeline 未创建 log，后续 Get-FileHash 失败；错误原文仅来自 root 工具会话，没有伪造原始异常文件或精确异常时间，相邻 capture 时间另列。改用 `File.WriteAllText(empty)` 后 correct 场景真实重执行并留下完整证据。[初次 filter 输入](bug-139-native-guard-unexpected-filter.sql)的[原始 1934 输出](bug-139-native-guard-unexpected-filter.log)属于 QUOTED_IDENTIFIER setup 失败，未执行到 guard；[原恢复比较](bug-139-sql-compare-after-repeat-to-guard-unexpected-filter-evidence.json)仍 25/25。修正 SET 选项后的[实际输入](bug-139-native-guard-unexpected-filter-correct-set-options.sql)和[51039 输出](bug-139-native-guard-unexpected-filter-correct-set-options.log)才计入 guard 验收。

原样保留的 [Capture](Capture-Bug139SqlState.ps1)、[Compare](Compare-Bug139SqlState.ps1)与[Test](Test-Bug139SqlGuards.ps1)是本机隔离库复现工具，含 loopback、owned receipt/SQL marker 前置检查和拒绝覆盖旧证据的保护。它们引用忽略目录的独立输入，使用 Windows 集成认证；档案不包含 receipt、凭据或应用 stdout/stderr。复现必须另行准备授权隔离输入与新证据路径，不能把档案中的库名当作现有环境操作授权。

## 审查与交付范围

`wp1_wiring` 独立审查 `d507f389..083e9c4d` 全部 14 个任务文件，核对原 snapshot blob 和 21 项定义，无 P1/P2/实质阻塞。确认时点 `2026-10-02T18:14:16Z`，实际审查此前已完成，未伪造更早的精确完成秒。非阻塞后续是 WP2 generation 合入后收窄测试中固定 137 的 registry 总数断言；本分支期望仍成立。

没有触发 Actions、修改 workflows 或分支保护；取消项 0。本地验收完成，正常 PR、远端 main 包含性及 Issue 关闭由任务负责人继续；当前未 push/PR，Issue Open。DB-COMPAT-01 其他 WP2–WP6 结构/业务门禁仍独立待验，包含后续发现的历史结构缺口；本 BUG 不宣称 WP2 全目录通过或 PostgreSQL API 已开启。
