# BUG #141：恢复四条订单模型外键

[Issue #141](https://github.com/GTX537/CP6/issues/141) 是保留的 SQL Server 迁移链没有安装模型已声明的四条订单外键。独立分支 `codex/bug-141-order-foreign-keys` 从远端确认的 `main` 基线 `2d9747360a38dfc519ddebea7aced06a9466b879` 创建；该基线已含 BUG139 索引修复。此次新增 SQL-only 前向迁移 `20261002184500_RestoreMissingOrderModelForeignKeys`，不改实体、模型、snapshot、旧迁移或已有数据。

本地及真实隔离 SQL 验证已通过：旧链实际 RED **4 Failed / 5 Passed / 0 Skipped**，相关测试 **37 Passed / 0 Failed / 0 Skipped**；同一 compiled API 首次/重复初始化 exit 0、无 HTTP，升级/重复原生比较各 **9/9**，最终 guard **11/11**，实际订单写入/孤儿拒绝/四条级联 gate **2/2**。一次独立完整任务审查已通过。正常远端交付仍 Pending，Issue Open，不宣称 WP2 全结构、全部业务或 PostgreSQL API 开启。

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

编译 API SHA-256 为 `F40298E0417BF7A408E06D9C8F0F47BC59D4F6FD58F6591CAB22FDB1DD57ADFB`，同目录 Core 为 `2F2B357394A9684211C79538707248261FED26602CC6C8AA369604676C70E34E`。此前自动化阶段没有启动应用或打开 DB 连接；下述负责人实际安装复用了同一 API 二进制和功能源码 `73946e96478f7eef1fc85ad8c310d49694471270`。本次证据归档不重新编译或连接 DB。

自动化原件留在忽略目录 `tmp/bug-141-tests`，逐字节归档且目录 `-text` 属性保留 SHA-256。源码提交时真实安装/重复、孤儿拒绝、级联、guard 与事务回滚仍 Pending；以下保留实际执行与失败，闭合本 BUG 的代表性隔离验证。没有以 metadata 或脚本匹配自动推定数据库行为通过。

`wp1_wiring` 于 `2026-10-02T19:01:01Z` 完成独立 staged 17 文件整项审查，无 blocker/P1/P2；4 源 SHA、6 原件字节/hash 和 snapshot 原 blob 与独立读取一致。后续仅文档/证据变化做增量核对，不重启完整代码审查。当前未 push/PR 或关闭 Issue。未触发 Actions、修改 workflow/保护或执行生产部署；取消项 0。WP2 其他模型与原始 DDL 门禁仍按父任务独立验收。

## 真实迁移、重复与原数据保留

负责人在有明确 owner/task 的隔离 SQL 库使用同一 F40298E0… API 执行 [首次初始化](wp2-init-bug141-fk-upgrade-first.json)（19:06:13–19:06:19 UTC）与[重复初始化](wp2-init-bug141-fk-upgrade-repeat.json)（19:06:32–19:06:37 UTC），两次 exit 0、完成消息成立、未观察 HTTP listener。库此前已有 WP2 generation，因此本 BUG 正常追加使 Core **138→139**，Space **47** 不变；不是修改已应用历史，也不是独立 main 的永久迁移总数。

[升级前](bug-141-sql-before-upgrade.json)、[升级后](bug-141-sql-after-upgrade.json)与[重复后](bug-141-sql-after-repeat.json)原生捕获均含全部 **352** 表按有序主键排序、UTF-16 编码并规范化空数组的 SHA-256/行数，以及索引、外键和 Core 历史。捕获 metadata 不直接归档行数据或密码哈希。实际[升级比较](bug-141-sql-compare-before-upgrade-to-after-upgrade.json) **9/9**：除 Core history 追加一行外所有表行数/摘要不变；原 **1,378** 个索引和 **198** 个外键的对象身份及完整捕获定义不变，仅新增四个 frozen FK，外键总数 **202**。实际[重复比较](bug-141-sql-compare-after-upgrade-to-after-repeat.json) **9/9**，352 表、历史与所有索引/FK 不变。

[实际 EF8 migration SQL](bug-141-generated-migration.sql) SHA-256 为 `17940B38B5CEE24A4F3E97257301372409F759846A786D656D1F187F023EE481`。它在源提交前生成，但四个功能源文件 SHA 与 `73946e9` 相同，未借提交 SHA 改变重复编译。完整原件、来源路径及大文件清单见[验证清单](verification.json)。未归档 receipt、seedstate、应用 stdout/stderr 或直接密码哈希；初始化报告保留其输出 hash。

## 原生 guard 与保留的 setup 失败

[最终 guard 汇总](bug-141-native-guards-verified.json) **11/11**：正确已有定义原样重放、列错误、删除动作错误、disabled、untrusted、NFR+untrusted 组合、错误 principal、dbo 同名非 FK 对象、第四命令晚冲突、真实已有孤儿 `547`、缺四条关系安装。所有输入/log/单项结果、12 份完整状态恢复比较均逐字节归档；各场景回滚后 **9/9** 恢复 352 表摘要/行数、history、索引和所有 FK。晚冲突先撤去前三条关系，真实 guard 先创建它们，再于第四命令 `51041` 拒绝；外层事务全部撤销。已有孤儿安装的代表性第四关系真实报 `547`，没有删除、补写孤儿或 NOCHECK。

首轮只执行 6/11，实际 **5 Passed / 1 Failed**、Complete=false，保留[原汇总](bug-141-native-guards.json)、[原失败 case](bug-141-native-guard-not-for-replication.json)、[执行 log](bug-141-native-guard-not-for-replication.log)及[恢复比较](bug-141-sql-compare-after-repeat-to-guard-not-for-replication-evidence.json)。失败为夹具期 `51003`：要求 trusted 的 NOT FOR REPLICATION 原生 FK 实际为 disabled=0、untrusted=1、NFR=1，未进入 migration guard。它不是 guard RED，更不是通过。

[原生诊断 SQL](bug-141-nfr-fixture-diagnostic.sql)及 [log](bug-141-nfr-fixture-diagnostic.log)确认 `WITH CHECK CHECK` 后仍为 0/1/1。修正场景标签为 `not-for-replication-untrusted`，真实 guard 报 `51041` 并恢复。它只证明该组合定义被拒绝，**不独立证明 NFR=1、is_not_trusted=0 的单一旗标分支**。最终汇总哈希核对并复用首轮五个成功执行，另六个场景新执行；未把五次复用写成重跑。

静态复现资产包括 [Capture](Capture-Bug141SqlState.ps1)、[Compare](Compare-Bug141SqlState.ps1)、[首轮 harness](Test-Bug141SqlGuards.ps1)、[剩余场景 harness](Test-Bug141SqlGuards-Remaining.ps1)及[独立 frozen 缺口清单](wp2-sql138-missing-model-keys-fks.json)。脚本绑定 loopback、特定隔离库和 ignored owner receipt，使用集成认证；只有脚本和公开 receipt hash 归档，receipt 内容没有归档。其生成的 guard SQL含显式外层事务与回滚，不能当作可直接执行的生产迁移。各场景完整大捕获与订单写入后捕获共 13 份留在 ignored tmp，清单保留逐文件 hash/bytes；三份主捕获和对应比较已归档。证据不承诺 DBTS、identity/sequence 全局计数回滚。

## 实际关系行为与适用边界

父任务独立 WP2 probe 的[旧 Core136 负对照](wp2-migration-sql-old136-order-relations-negative-control.json)真实失败：第一条缺失 FK 未拒绝其孤儿，fixture 回滚；[PG 参考](wp2-migration-pg-order-relations-reference.json) **2/2**。修复后 SQL [关系行为](wp2-migration-sql-restored-four-fks-order-write.json) **2/2**：实际 EF 写入合法六行 Order 图，四个原生 FK 各拒绝独立孤儿且错误约束身份精确，detail/parent 删除实际覆盖四条级联；fixture 全事务回滚。随后[原生写入前后比較](bug-141-sql-compare-after-repeat-to-after-order-writes.json) **9/9**，全352表摘要/历史/索引/FK恢复。

[执行输入清单](wp2-order-relations-input.json)保留该 probe 编译源/二进制身份；[归档 Order gate 源](Wp2OrderRelationWriteGates.cs)与该输入 hash 一致，属于父任务尚未提交的 WP2 外部验证源，**不是本 BUG 产品代码或 standalone 可编译项目**。这些关系 gate 不检查 migration currency；实际本 BUG migration currency 与数据保留由同 compiled main API 初始化及上述原生比较证明。PG 参考不是普通 PG API 启用、全部订单业务或 WP2 全阶段验收。

SQL 全模型目录另发现六个报价 Creator/Modifier 历史容量漂移，仍属独立 WP2 阻塞；本 BUG 不改造或掩盖它。当前仅本 BUG 本地及代表性真实 SQL 验证通过，正常 PR、远端 main 包含性及 Issue 关闭由负责人后续完成。

原完整17文件审查 scope SHA-256 `d6ff67c158dbd56f3b3e641564080c493a233f2999f807a786beab17fcd6ed73`，四功能源 scope `42a61eb683fa1fc91dc0d0d8c4efe8fc20a09d9728970cca335c1f9f2bf7b46e`；这些摘要只对应原审查时点，未替换成当前新增证据 scope。本次仅做原始字节/hash、相对链接、敏感内容和 staged blob 增量核对，复用同一功能源的审查及验证。
