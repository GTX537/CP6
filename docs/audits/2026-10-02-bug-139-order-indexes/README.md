# BUG #139：恢复订单模型遗漏的查询索引

[Issue #139](https://github.com/GTX537/CP6/issues/139) 是 SQL Server 历史迁移遗漏：模型已有 21 个非唯一查询索引，旧 Core 136 条迁移安装后目录中不存在。修复只添加 SQL Server 前向迁移 `20261002175000_RestoreMissingOrderModelIndexes`，不改模型、snapshot、历史迁移或已有数据；两个同定义异名的唯一索引不重复创建。

独立分支 `codex/bug-139-order-indexes` 从核对过的远端 `main` 基线 `d507f389fdcadd8e2fc5dcb9e40f752cde613703` 创建。定义 oracle 是保持原字节的 `d6074aaad3098b61adaeda902b92bf24b3eb0c04` Core snapshot，其 Git blob 仍为 `21ed77b4056ee44b4f44e61de13d5d34774d6934`。本条记录本地时点：真实 SQL 安装、重复执行、冲突拒绝和原数据保留验证及独立任务审查待负责人执行；Issue 保持 Open，未远端交付。

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

## 本地验证与待验收范围

实际执行 `dotnet restore CP6.Tests/CP6.Tests.csproj --locked-mode --disable-build-servers -p:BuildInParallel=false -m:1 -nodeReuse:false -v:quiet` 成功，未改依赖锁、NuGet 信任策略或项目文件。仅运行 `OrderModelIndexRepairTests`，未连接数据库或启动 API。

旧生产源码上的 [RED 原始 TRX](bug-139-red.trx) 为 **21 Failed / 1 Passed / 0 Skipped**，21 项均先成功核对 frozen snapshot 定义，再在旧生成脚本中缺少 CREATE 时失败；[原始 log](bug-139-red.log) 保留。新增迁移后 [GREEN 原始 TRX](bug-139-green.trx) 为 **25 Passed / 0 Failed / 0 Skipped**，包含原 22 项及迁移发现/事务、完整 guard facets、provider/Down 拒绝；[原始 log](bug-139-green.log) 保留。GREEN 只将相同模型/脚本计算缓存以减少重复计算，没有删改原 21 项断言。编译及此定向执行日志无 warning/error。

两轮命令使用 `dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter FullyQualifiedName~CP6.Tests.Persistence.OrderModelIndexRepairTests --logger trx --disable-build-servers -m:1 -nodeReuse:false -p:BuildInParallel=false --verbosity quiet`，分别指定独立 RED/GREEN TRX 名称和结果目录。完整本机原件留在忽略目录 `tmp/bug-139-tests`；归档逐字节复制，[验证清单](verification.json) 记录输入、计数及 SHA-256，目录属性禁止换行改写。

本轮编译 API SHA-256 为 `4CF42E4D68980CB0D5C1D0F819E2CBD48B428749DF5CDEE0268369A7F6D2A5BE`，Core 为 `15E865AD90929B017680D07BA7FFD155238EFB10AEAB36816B65E800DA1E4076`。它们只是本地测试产物；未作生产候选、环境切换或部署。当前脚本/metadata 检查不替代真实 SQL 对安装、同名保留、各种冲突拒绝、事务回滚及数据保持的验收。

没有触发 Actions、修改 workflows 或分支保护；取消项 0。未 push、创建 PR 或关闭 Issue；任务负责人完成真实验证与一次独立完整 diff 审查后，才按正常流程交付。DB-COMPAT-01 的其他 WP2–WP6 验收及 PostgreSQL API 开启不属于本 BUG 结论。
