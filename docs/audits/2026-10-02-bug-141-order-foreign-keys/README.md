# BUG #141：恢复四条订单模型外键

[Issue #141](https://github.com/GTX537/CP6/issues/141) 是保留的 SQL Server 迁移链没有安装模型已声明的四条订单外键。独立分支 `codex/bug-141-order-foreign-keys` 从远端确认的 `main` 基线 `2d9747360a38dfc519ddebea7aced06a9466b879` 创建；该基线已含 BUG139 索引修复。此次新增 SQL-only 前向迁移 `20261002184500_RestoreMissingOrderModelForeignKeys`，不改实体、模型、snapshot、旧迁移或已有数据。

本条记录本地自动化时点：旧链实际 RED **4 Failed / 5 Passed / 0 Skipped**；最终相关测试 **37 Passed / 0 Failed / 0 Skipped**，包含 12 项本次 FK 契约与 25 项原索引回归。一次独立完整任务审查已通过；真实 SQL 安装、已有孤儿拒绝、级联、重复执行与整体事务回滚及远端交付仍 Pending。Issue Open，不宣称 WP2 其他结构通过或 PostgreSQL API 开启。

## 原定义与缺口

[五月 Designer](../../../CP6.Core/Migrations/20260502225006_AddBpAndFscPA110.Designer.cs#L4133) 已声明 required、业务键关联和删除级联。保留的 [订单恢复迁移](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs#L8) 明示原始 AddOrderTables migration 丢失，恢复最小 raw 表结构但没有这些 FK；后续旧链也未补。不能从当前 snapshot 推断无法恢复的原迁移或已有业务库曾安装它们。

冻结 `d6074aaad3098b61adaeda902b92bf24b3eb0c04` [snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L20587) 与当前 [模型](../../../CP6.Core/EFDbContext/CP6Context.cs#L1512) 的四个合同一致；snapshot Git blob 保持 `21ed77b4056ee44b4f44e61de13d5d34774d6934`。[独立模型断言](../../../CP6.Tests/Persistence/OrderModelForeignKeyRepairTests.cs#L31) 核对各表/schema、required、非 unique 关系、非 PK principal key、有序物理列、Cascade 和无 TenantId；未将当前 helper 当作定义 oracle。

| FK 名称 | 子表及有序列 | 主表及同序列 |
| --- | --- | --- |
| FK_T_OrderDetail_T_Order_WebOrderNo | dbo.T_OrderDetail (WebOrderNo) | dbo.T_Order (WebOrderNo) |
| FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd | dbo.T_OrderMaterial (WebOrderNo, WebOrderDetailNo, ProductCd) | dbo.T_OrderDetail (WebOrderNo, WebOrderDetailNo, ProductCd) |
| FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd | dbo.T_OrderProcess (WebOrderNo, WebOrderDetailNo, ProductCd) | dbo.T_OrderDetail (WebOrderNo, WebOrderDetailNo, ProductCd) |
| FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd | dbo.T_OrderProcessNote (WebOrderNo, WebOrderDetailNo, ProductCd) | dbo.T_OrderDetail (WebOrderNo, WebOrderDetailNo, ProductCd) |

模型删除行为为 CASCADE，SQL 更新行为保持 NO ACTION。关系是 Order→Detail→Material/Process/ProcessNote 的树。主对象已有 `T_Order.AK_T_Order_WebOrderNo` 和 Detail 全局 `UX_T_OrderDetail_OrderProduct`；后者是合法的 standalone unique index。另一个 Quotation AK 的唯一索引别名不属于本 BUG。此次不新建 unique、换候选键或添加 tenant 列。

## 前向 guard

Public [OrderModelForeignKeyRepairSql.CreateStatements()](../../../CP6.Core/Persistence/OrderModelForeignKeyRepairSql.cs#L18) 返回只读 4 个 SQL statement；[迁移](../../../CP6.Core/Migrations/20261002184500_RestoreMissingOrderModelForeignKeys.cs#L13) 每句单独 SqlOperation，默认同一 EF 事务，不拼接 DECLARE scope。main 的 Core 137→138；已额外安装 WP2 generation 的隔离库按正常发现追加，不能更改已应用的历史。

缺失约束使用 `ALTER TABLE ... WITH CHECK ADD CONSTRAINT ... FOREIGN KEY ... ON DELETE CASCADE ON UPDATE NO ACTION`，验证已有行并默认启用、可信。孤儿数据的原生 `547` 不捕获、不忽略；整项 EF migration 必须回滚，不删除、补写旧行或 NOCHECK。

已有对象先在 `dbo` schema 的 `sys.objects` 按约束名查询，避免只查预期 child 的 FK 而漏掉同名不同对象。只有 `sys.foreign_keys` 中的正确 schema/child/principal、精确列数/ordinal/两侧物理列名、delete=1/update=0、enabled、trusted、`is_not_for_replication=0` 全成立才原样保留；缺失 JOIN 行明确 NULL 拒绝，列名比较用 BIN2。错误同名非 FK 或不同定义报 `51041`，不 DROP、ALTER 既有对象或重建。另一 provider 和 Down 均拒绝。

定向计划检查依据 [sys.foreign_keys](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-foreign-keys-transact-sql?view=sql-server-ver16) 与 [sys.foreign_key_columns](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-foreign-key-columns-transact-sql?view=sql-server-ver16) 的官方目录含义核对旗标和映射序号；这不是实际 SQL 场景或完整任务审查的替代。

## 本地执行证据与边界

实际 `dotnet restore CP6.Tests/CP6.Tests.csproj --locked-mode --disable-build-servers -p:BuildInParallel=false -m:1 -nodeReuse:false -v:quiet` exit 0，没有改锁文件或 NuGet 信任策略。RED 只执行 `OrderModelForeignKeyRepairTests`：4 个独立 frozen 合同与 legacy136 oracle 通过，4 个完整脚本 CREATE 断言实际失败。[原始 TRX](bug-141-red.trx) 与 [log](bug-141-red.log) 留存。

新增迁移后同时执行 FK 和 `OrderModelIndexRepairTests`，首轮 [37/37 TRX](bug-141-green.trx) 与 [log](bug-141-green.log) 有一项 xUnit2013 collection-size 提示。仅把相同单元素断言从 `Assert.Equal(1, Count)` 改为 `Assert.Single`，没有生产源码变更；最终 [37/37 TRX](bug-141-green-verified.trx) 和 [log](bug-141-green-verified.log) 无 warning/error，零跳过。两轮均保留，未改写首轮记录。

命令统一使用 `dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter ... --logger trx --disable-build-servers -m:1 -nodeReuse:false -p:BuildInParallel=false --verbosity quiet`，独立结果目录和文件名见[验证清单](verification.json)。完整脚本/模型使用 Lazy 缓存；初始 RED 断言保持。旧索引回归只把永久 137 总数断言收窄为具体 RepairMigration 必须发现，保留旧136 oracle、21 个 frozen 定义和原 guard/provider/Down 检查。

编译 API SHA-256 为 `F40298E0417BF7A408E06D9C8F0F47BC59D4F6FD58F6591CAB22FDB1DD57ADFB`，同目录 Core 为 `2F2B357394A9684211C79538707248261FED26602CC6C8AA369604676C70E34E`。这些只是本地自动化产物，未启动 API 或打开 DB 连接；负责人后续真实验收须绑定实际二进制和功能源。文档归档不重复 unchanged source 编译。

原件留在忽略目录 `tmp/bug-141-tests`，逐字节归档且目录 `-text` 属性保留 SHA-256。当前必需真实场景仍待验：4 个孤儿写入/已有孤儿安装拒绝、4 关系原生级联、同名错误对象/列/动作/旗标拒绝、最后命令冲突的完整迁移回滚及所有原数据/token/index/history 保留。没有以 metadata 或脚本匹配自动推定这些行为通过。

`wp1_wiring` 于 `2026-10-02T19:01:01Z` 完成独立 staged 17 文件整项审查，无 blocker/P1/P2；4 源 SHA、6 原件字节/hash 和 snapshot 原 blob 与独立读取一致。后续仅文档/证据变化做增量核对，不重启完整代码审查。当前未 push/PR 或关闭 Issue。未触发 Actions、修改 workflow/保护或执行生产部署；取消项 0。WP2 其他模型与原始 DDL 门禁仍按父任务独立验收。
