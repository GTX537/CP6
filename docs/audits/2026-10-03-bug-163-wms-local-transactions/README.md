# BUG163：WMS 本地事务回归

## 2026-10-04：正常交付与清理完成

Issue163 已 Closed；[PR164](https://github.com/GTX537/CP6/pull/164)正常合入远端 main `6322ac152cab7d462719169fb0301f49e9ee39ba`，核对候选包含性、完整树及122个远端文件 Git blob。合并后实际 SQL/PG 各3/3；两库此前25/25按真实执行来源保留。两个修复测试库及原 WP6 SQL 失败库均已普通DROP并确认不存在，持续/短暂 SQL 会话控制通过且自然结束，原失败记录未改写。

[36份交付原件](native/post-merge/manifest.json) SHA256 `5309C7BDFB36DEED152CAB0C7B600420D9D768D1170B3E648CE112AABFD0F7FB`，包含工作流核对、完整PR文件、远端main、实际合并后冒烟、三库清理、会话控制、审查追加核对及Issue关闭读回；下方原111份归档不变。所有验证本地，Actions启动/取消均0，无工作流触发或分支保护变更；生产门禁保留。WP6已接入修复，父Issue134继续Open，当前版本全套Matrix/Application与恢复验收仍待完成。

下文为BUG163合并前本地验证时点的记录；其中Open、远端待交付与待清理描述保留历史来源。

[Issue163](https://github.com/GTX537/CP6/issues/163) 当前 Open；基线为远端 main `ce49012d37da4b1b01f074d5b5b7fd227780e656`。修复已完成双库本地验证及一次集中事务审查；正常远端交付、合并后冒烟、精确自有库清理仍待完成。父任务 [Issue134](https://github.com/GTX537/CP6/issues/134) 的 WP6 最终整体验收仍待完成。

## 根因与改动

原 WP6 SQL Matrix 在核心25/25后，WMS原八项为7P/1F；`LpnService.CreateAsync` 第二次打开无连接池连接时产生 SqlClient `PSPEPromote`/`Enlist` 与隐式分布式事务异常。LPN Create/Unpack/Split/Merge 和 Label Claim/Complete 自建 ambient Required 事务，EF 查询间可能关闭并重开物理连接。标签三个领取/完成/失败定向用例已实际复现相同 DTC 异常；已有 DbContext 本地事务也实际产生 root/nested ambient 冲突。

独立执行时，六个命令改为在同一 DbContext/连接中拥有 ReadCommitted 本地事务，覆盖业务数据、闭包、序列号、事件、映射查询和操作回执；第二次 SaveChanges 后才提交，异常/取消由异步释放未提交事务回滚。调用方已有本地或显式 enlist 事务时保留其所有权；调用方已有 ambient 事务时保留 Required 加入及失败 abort 语义，原生测试采用调用方预先打开的同一连接。显式 enlist 分支仅保留参与行为，没有独立原生用例，不扩大验收范围。

公开接口、DTO、Pack/Move 实现和数据库配置未改变。[EF Core 事务文档](https://learn.microsoft.com/en-us/ef/core/saving/transactions)说明手动事务可覆盖多次保存与查询；本项目的真实双库用例验证上述具体行为。

## 实际证据

| 执行范围 | SQL Server | PostgreSQL |
| --- | --- | --- |
| 未修复代码，有效种子新增17项 | 3P/14F | 10P/7F |
| 修复后，原8项＋新增17项 | 25P/0F/0skip | 25P/0F/0skip |

SQL 有效 RED 的14项失败为11项实际 DTC异常及3项调用方本地事务冲突。PG的7项为独立命令本地事务诊断及调用方事务问题；故障注入控制与实际 DTC 错误分别记录。原有全树用例覆盖 Pack/Move/Split/Merge/Unpack、序列号复合身份及移动幂等回放。新增用例覆盖六个事务路径、真实业务保存后的故障、完整数据库状态回滚、取消、本地调用方提交权，以及 ambient 回滚/abort。

两个 Provider 明确 `Pooling=False`，SQL MARS=False；未开启全局 DTC。四个独立命令的原生 trace 均无 ambient，事务内命令使用唯一物理连接。两库使用同一真实成功构建（SDK 10.0.302，目标 .NET8）；2492个源码输入及完整运行目录逐字节相同。构建0 error、1个原有 `PermissionsRelationalTests.cs` xUnit2017 warning，该文件不在修复范围，不声称0 warning。

[精确核对](native/execution/bug163-exact-local-verification.json)逐项核对25个用例名、原始 TRX、退出码、零跳过、当前源码/运行时字节和物理连接诊断。[一次集中审查](native/execution/bug163-review.md)无 P0/P1/P2 阻断，只读审查未启动额外构建或数据库执行。

## 原件与失败记录

[归档清单](native/manifest.json)保留111个公共原件，SHA256 `D453875FA92DD45ECD01D9D02FE7773CAD518E080DA9038B3264B32EC6AD770E`。原 WP6 SQL Failed summary SHA256 `8259FAEA4EEEF0786705CB4501F1072F79D0CA7DC116606B9EEB5364A7205F50`未改写；该轮后35入口未执行，一个原自有 SQL 库仍待清理。

第一次辅助 runner 在构建成功后因 PowerShell 通用连接字符串 builder 属性适配而拒绝执行，未启动原生测试，修正为显式 setter/getter 后复用真实构建。随后首轮测试种子遇到全局 ProductCd 唯一键重复，第二轮又遇到 ItemCd 长度限制；两次原失败保留，最终改用满足模型长度的独立随机商品编号后，才计入有效 RED。原私有连接 receipt 保留，PG的明确关闭连接池设置在任何测试前完成；用户配置未改动。

归档只包含核对过的公共报告、TRX、日志、实际执行 helper 及七份冻结模块源码，并按原件字节核对；私有凭据、连接/owner receipt、数据库状态、JWT/Cookie 和备份排除。

## 复现入口与验收边界

隔离空库需符合原 WP4 WMS fixture 的 owner 标记协议。通过 `CP6_WMS_TEST_PROVIDER`、`CP6_WMS_TEST_CONNECTION`、`CP6_TEST_DATABASE_OWNER` 显式选择 Provider/测试连接；连接指向独立本地测试库，关闭连接池。执行 `dotnet test CP6.Tests/CP6.Tests.csproj --filter FullyQualifiedName~CP6.Tests.WmsProductionSqlServerTests --logger trx`。显式配置使这些用例成为必需项，不能以缺少数据库跳过。

归档中的 [runner](native/execution/Invoke-Bug163Tests.ps1)记录精确源码、完整运行文件、退出状态和原始 TRX；其绝对路径是该次本机执行证据，迁移到其他工作区时按环境调整入口。真实原件、局部控制、构建复用与后续交付分别陈述。本记录不代表生产验收、既有数据搬迁、完整 WP6 Matrix/Application 或已有环境切换；远程 Actions、生产门禁与分支保护均未改变。
