## 优先级

P0 — WMS 原生 SQL Server 验收被阻断，事务原子性路径需要修复。关联父任务 #134。

## 环境

Windows 本机 SQL Server `localhost\KOUSQLSERVER`，Windows 集成登录；.NET 8，连接池关闭，MARS 关闭。
已确认远端 main：`ce49012d37da4b1b01f074d5b5b7fd227780e656`。
原 WP6 运行 HEAD：`06eaa7110f3c2162ee44e32caf46d684ebd0fba5`，实际输入另有原生 runner 清理修复与精确哈希绑定。

## 复现步骤

1. 在独立 owner-marked SQL Server 库中运行最新 WP6 Matrix；runner 明确生成 `Pooling=False` 的本地业务连接。
2. 核心业务组 25/25 通过，随后运行 `CP6.Tests.WmsProductionSqlServerTests` 原有八项。
3. `Lpn_UsesCompositeSerialIdentity_AndMovesSplitsMergesWholeTree` 调用 `LpnService.CreateAsync`，在第二个原生查询打开连接时触发事务提升。

## 预期与实际结果

预期：同一 DbContext/数据库上的 LPN 创建、树操作、事件和幂等回执保持本地事务原子性，可在两种 Provider 上执行，不依赖隐式分布式事务或连接池偶然复用。

实际：WMS 7/8 Passed，LPN 一项 Failed。`System.NotSupportedException: Implicit distributed transactions have not been enabled`；栈包含 SqlClient `PSPEPromote`/`Enlist` 和 `LpnService.CreateAsync:77`，测试位置 `WmsProductionSqlServerTests:812`。

源码已确认 Create/Unpack/Split/Merge 四处使用自建 ambient TransactionScope，查询间 EF 可关闭/重开连接；Pack/Move 已使用 DbContext 本地事务。相邻 LabelJobService 的 Claim/Complete 存在同种自建 ambient 模式，尚待同一根因的原生定向确认；不能仅凭静态相似声称已复现。

## 证据与临时绕行

原运行 `D:\CP6\tmp\wp6-sql-formal-matrix-cad-cleanup-fix` 保持 Failed：核心 25/25，WMS 7/8，后35入口未执行，零跳过。
原 summary SHA256：`8259FAEA4EEEF0786705CB4501F1072F79D0CA7DC116606B9EEB5364A7205F50`。
WMS 原 TRX SHA256：`5AD8388203E589148B68795BFBC56C2478BC0D95EF9646C6A3A5D06C042A2870`。
只有一个该次自有 SQL 库留待根因回归/核对后按精确 receipt 清理；没有改动现有业务库。
PostgreSQL 新 Matrix 34/34、11库 AbsentVerified 为该次原结果，不能代称修复后的双库最终验收。

## 完成条件

- [x] 原始失败和无连接池原生 RED 回归保留，确认物理连接/事务生命周期根因；Label 同类路径需实际复现后才纳入修复。
- [x] 使用适当的同 DbContext/连接事务边界，验证 LPN 全树操作、事件/回执回滚和已有调用方事务；保持既有外部业务契约。
- [x] 所有受影响原生场景在 SQL Server/PostgreSQL 零失败/跳过，不依赖开启全局 DTC 或改变测试连接池来获得通过。
- [ ] 一次任务级完整 diff 审查、必要专项验证和四份项目状态文档闭环，按正常受保护 PR 交付远端 main。
- [ ] 核对远端包含修复与必要合并后冒烟，精确清理自有测试库后才关闭本 Issue；父任务 #134 仍需 WP6 最新完整验收。

普通验证仅本地执行；不触发 GitHub Actions、不修改分支保护、不切换现有环境、不生产部署。

## 本地验收进展（2026-10-03）

有效种子未修复代码17项：SQL Server 3P/14F（11项实际DTC、3项已有本地事务冲突）；PostgreSQL 10P/7F。Label Claim/Complete/Fail已经实际复现相同根因，因此纳入本Issue的六处事务边界修复。原始、辅助入口预检拒绝及两次测试种子错误全部保留，只有最终有效种子运行计入RED。

修复后原8＋新17，两库各25/25、零失败/跳过。四个独立命令trace证明没有自建ambient、事务内唯一物理连接；2492个源码输入与378个运行文件两库相同，SQL实际构建后PG逐字节核对复用。构建0error/1个原有xUnit2017warning，无全局DTC或连接池绕行。原有全树流程、写入后故障/取消、回执、调用方本地/预打开ambient事务专项通过；显式enlist分支只做源码审查，不扩大为独立原生验收。

一次集中事务源码审查无P0/P1/P2阻断，文档/归档增量复核进行中。公共111原件清单SHA256 D453875FA92DD45ECD01D9D02FE7773CAD518E080DA9038B3264B32EC6AD770E，修复分支codex/bug-163-wms-local-transactions，审计入口docs/audits/2026-10-03-bug-163-wms-local-transactions/README.md。正常远端交付、合并后冒烟和两个修复库及原WP6 SQL失败库清理尚待完成；Issue保持Open。父Issue134/WP6最终双库Matrix/Application和恢复验收另行推进。