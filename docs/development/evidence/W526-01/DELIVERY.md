# W526-01 · PGT-021 同库恢复与 Stock 正式包采用

本轮将 PGT-021 按已采用 v11 Core RMA 完整逻辑单元验收为 PASS，正式 PostgreSQL **22 PASS / 5 NOT_RUN**，原询盘 **60/60**。同时完成 Stock #844 的正式 0.10.4 包采用、原失败场景回归，以及可复现的 PostgreSQL 本地构建入口。WP02-02 其余原卡仍未全部完成。

## 范围与实现

基线 `d24348432569fda429cfdfc6cc210b42c8e3e551`，同一分支 `codex/w526-01`、固定 DEV-1；没有新增 worktree。原 W492/W493 建议计划并入本次用户授权的关联闭环，不另占任务/分支。

[S02 原设计](../../../design-handbook/architecture/02-postgresql-tenancy.md) §9 采用同库共享权威和租户 schema。W522–W525 曾将永久 A→候选/B→源路由和跨库 IAM 列为后继，超出了本次恢复原卡；本轮纠正该范围，没有新建永久双库架构，也不要求每类无引用表先制造非空损毁。

[恢复入口](../../../../eng/wp02-02/rma-tenant-archive/live_recovery.py)验证完整同源 v11、Stock effects、外部资产、源 commissioning 和关闭候选的五份原导入回执。源事务使用现有认证门、A 的 Ready 行和业务表锁，补缺失 heads/versions，核对并保留当前共享原件和派送状态，同事务保存操作键及原结果字节。较新/不同业务行、缺依赖、未知消息或不适用 epoch 拒绝；提交答复不明时按原键 recover。额外恢复 advisory lock 已按 Trust Internal Contracts 去除，复用既有认证根。

当前共享 IAM/会话/主密钥不从旧包写回；原 TenantId、Owner、操作键、精度与业务原字节不变。恢复成功只开放原库逻辑单元；隔离候选原回执仍是 `recoveryReady=false`。完整 schema 毁损、其他业务模型恢复、生产迁移与部署不在本次实际范围。[调用说明](../../../../eng/wp02-02/rma-tenant-archive/LIVE-RECOVERY.md)。

## 正式发布与 Main 采用

用户对单次 `GTX537/CP6.Platform/p10-formal-packages.yml` 明确授权；精确源码 `f3fbe4be67d40641f7a8a969d74be65a95541819`、版本0.10.4、受保护环境正常审批。唯一运行 [43378516039](https://github.com/GTX537/CP6.Platform/actions/runs/43378516039) 成功，Windows签名/RFC3161/七包feed原字节回读和Linux独立验签均通过，没有重跑或生产部署。

[正式 publication 原件](formal-package-publication.v1.json) SHA-256：
`acca9c41c4e658ca2db3067a3c48abbf3dda8e4be896307916019f2d36193577`。
Windows制品 `14014929598` ZIP摘要 `08ab483061cdc76af6e91ad50609f535b38c69516a6200c3fed7c6f41bd35e69`；
最终制品 `14015701555` ZIP摘要 `fdb3b30b785ce346d6de26fd802468adcab40944190077bdbf0094f909874ae3`，均与GitHub API digest一致。

Main 四条直接引用及六份受影响锁文件升至0.10.4；还原保留 `NuGet.formal.config` 的 signature=require、源映射和固定签名者，没有放宽信任。实际五个运行依赖来自正式GitHub feed，缓存nupkg摘要逐一等于该发布原件。Main既有ReleaseTool的独立版本未改，未将新包误称新的route epoch能力。

## 实际验证

环境为本机隔离 PostgreSQL 18.6、.NET SDK8.0.424、当前源码编出的WebApi，业务数据及Stock/ERP transport均为明确模拟；业务事务、数据库锁、原生回执和HTTP均实际执行。正式包的构建/验签环境另见publication，不与本地构建混称。

| 行为 | 实际结果 |
|---|---|
| 当前 Tests/WebApi 和兼容性 Probe 的PG构建 | 成功；保留分析器。首次1 GiB heap不足的失败日志保留，调整2 GiB后成功 |
| 默认/PG编译输入核对 | Core仅条件排除280项历史迁移；Tests仅排除原四项历史迁移测试；253个PG编译项全保留。未运行默认全量构建/SQL Server测试 |
| Stock与真实Program回归 | 18 PASS、0 FAIL、0 SKIP：LF/CRLF正确守卫、篡改拒绝、UNKNOWN/重启、三阶段提交答复丢失、双Worker、原件保护、真实WebSocket与密码变更撤权 |
| 原W477同源Stock/ERP Pending | 用本次Main和正式0.10.4：A各实际发送1次并Published，Stock第二次IDLE；原键/payload、B Pending及Owner原图不变 |
| PGT-021恢复 | A原1 head、2 versions完整恢复；原journal/源出库/FileContent/共享原件保持；恢复后普通A读原v1/v2、ETag及精度一致，新草稿201且同键回放一致 |
| B活动及共享保护 | B普通读200、新草稿201均位于CLI运行窗内；只出现该一笔明确合法差量。其余B行/目录、当前共享片/资产、菜单/租户定义及1个DataProtectionKeys原件摘要不变 |
| 当前安全状态 | 备份后的真实IAM禁用和重设密码保持；旧access/refresh仍拒绝。当前Stock/ERP Published保留，恢复自动发送0次 |
| 缺件/故障/未知提交 | 缺journal、缺外部资产拒绝；回执INSERT处注入数据库故障，A写入全部回滚且无成功回执；真实COMMIT后客户端丢答，recover及apply同键返回同一原字节 |
| 同键并发与源较新 | 首次创建回执表的两次并发恢复只产生1条原回执；已新增业务的源明确拒绝，全部原行不变 |

本轮原始日志与自动驱动保存在 `D:/CP6-test-artifacts/W526-01/`：
`build-tests-2g.log`、`probe-build.log`、`compile-scope.json`、`results/stock-and-auth.trx`、
`restored-formal-packages.json`、`exercise526.py`、`DispatchW526.cs`、
`native-05/actual.json`、`native-06/actual.json`、`native-07/actual.json`及各terminal。
native-05为PASS/exit0、33.953秒；06/07分别8.766秒和7.921秒，结束时自有进程为空。

native-05恢复操作 `469bec5d-17af-49f7-b516-1caede57a2fd`；
CLI窗口06:50:57.581010–06:50:58.705787 UTC；
B读取06:50:57.583799–06:50:57.940189，B提交06:50:58.175704–06:50:58.341880。
数量 `1.12345678` 和原源版本按原合同读回。

复用 [W520](../W520-01/DELIVERY.md) 同快照v11与Stock effects、
[W521](../W521-01/DELIVERY.md)五份真实staging导入回执和未改的租户登录/ACL隔离证据。
manifest pin `f25588aa95ccf06bcfd90c016fe5fb0c5b61cb2137b22823232a82da44ade1e8`，
effects pin `778bb5f72a0ebb016a288a4ff5265477a6c8f460fd34378b8c6381470158d764`。
[W523](../W523-01/DELIVERY.md)相同源Pending在0.10.3的guard失败作为红色原证据，未删除/改写。
本轮早期夹具失败（待处理约束事件、用旧Pending断言检查已Published现状、人为长持全局认证门导致读取超时）保留在01/03/04；修正夹具后才计05通过。软件故障注入的非零是预期拒绝，未称成功。

## 审查、集成和剩余范围

执行一次任务级SPEC/QUALITY集中审查，采用相关原生行为及差异检查。成功结果按相同代码/依赖输入复用，提交/合并SHA改变不重新冒称执行。源恢复入口的ERP锁序按现有C03入口校正，并追加定向锁顺序验证；详见本轮审查记录。

按正常PR合并后核远端main包含全部任务提交，再关闭 [Main #844](https://github.com/GTX537/CP6/issues/844)。除用户这一次正式Platform发布，Main普通编译/测试均本地；工作流触发配置未改，不主动启动其他Actions。

PGT-022保留NOT_RUN：现行合法route epoch变化后的旧消息恢复尚缺Owner支持路径；直接改不可变epoch被拒绝不算通过。PGT-024保留NOT_RUN：仍需真正A新模型/B旧模型、兼容绑定、最低共享版本拒绝和应用回退。PGT-011/017/025继续按原卡范围；已有同模型双版本应用或局部Owner证据不冒整体完成。原始不存在的非C03图/未采用业务模型不作为本次v11恢复必须先补造的前置。

分支采用后按官方 `finish` 定向清理本任务本地/远端分支，固定DEV-1保留复用并释放席位；实际main SHA、分支处置和释放回执见本任务PR及 `D:/CP6-test-artifacts/W526-01/main-finish.json`。不清理根工作区用户资料，不覆盖既有环境。
