# WP02-08 #66 / #67 独立定向复查

结论：**PASS（focused）**。固定候选 `2e659e382e95988ee06799cd2feb0d580dcd8323` 的两项原 P2 已修复；本次没有发现新的可执行 P1/P2，可交 COORD 继续串行集成。结论只适用于下面固定输入及修复范围，不表示远端已合并或 Issue 已关闭。

- 审查时间：2026-10-14 07:49:16 UTC
- 工作树：`D:/CP6.Platform-worktrees/wp02-08-corrected-20261014`，核验 HEAD 精确匹配且 tracked/untracked 状态均为空。
- 原候选：`3e4c078434ff4ae066f20d83e134fb268663700c`；两分支基线：`e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`。
- #66：`6e580bb4f77d943dcdaeeafd3450891f791aef9e`；#67：`f17b8669b9dde1826bb231358a0106d16232640b`；两者与原候选均是最终候选祖先。
- 聚合实测源码：`17886458022bcbbb49d63890e52eccd897ac7256`；此后只增加任务证据。
- 原初审 [review-WP02-08.md](C:/Users/tt/CP6-planning-reports/20261014-wave6-integration/review-WP02-08.md) 保留原 R1/R2；没有覆盖、删除或改写初审结论。

## 修复核对

**#66 / 原 R1：已解决。** [OutboxLifecycleV2.cs:50](D:/CP6.Platform-worktrees/wp02-08-corrected-20261014/src/CP6.Platform.EntityFramework/OutboxV2/OutboxLifecycleV2.cs:50) 将 Blocked 分支接入现有 `SaveSwitch`。根状态更新、原 mutation 输入/输出回执及审计属于同一调用方事务；保存结果读取更新后的控制版本。第 27 行先执行原 tuple 回放，再检查当前根状态/版本；第 81–93 行保证相同 tuple 返回原结果，变化 tuple 冲突，回放不走更新或 SaveChanges。handover 后来已解决也不会把旧 mutation 重新解释成 advance，新 mutation 才能继续推进。

[回归测试:326](D:/CP6.Platform-worktrees/wp02-08-corrected-20261014/tests/CP6.Platform.WP0208.PostgreSqlTests/OutboxTransactionTests.cs:326) 在真实 PostgreSQL COMMIT 后模拟丢 ACK，核对原结果、真实另一事务留下的回执、原序列化 tuple、变化 tuple 冲突、原 tuple 重试不增加 native/binding/claim/mutation/send-attempt/audit/seal/DLQ 写入，以及新 mutation 推进。原 rollback、drain 和两个 epoch/completion 锁序回归随 #66 GREEN 覆盖。Snapshot 本身未包含根行；根写入路径、Blocked 状态断言与原控制版本回放另外审查，不将 Snapshot 夸大为全库无写证明。

**#67 / 原 R2：已解决。** [OutboxDispatcherV2.cs:60](D:/CP6.Platform-worktrees/wp02-08-corrected-20261014/src/CP6.Platform.EntityFramework/OutboxV2/OutboxDispatcherV2.cs:60) 在 send admission 前读取单调时钟；[第 73 行](D:/CP6.Platform-worktrees/wp02-08-corrected-20261014/src/CP6.Platform.EntityFramework/OutboxV2/OutboxDispatcherV2.cs:73) 在 `await Write` 返回及消息读取后扣减已耗预算。Write 第 104–109 行的两个 `await using` 在方法返回前完成事务/连接上下文释放，因此 commit ACK 和异步 dispose 延迟均消耗窗口。DB `clock_timestamp` 仍用于原租约门禁；此前开始的单调窗口只作保守缩减，不更换 DB 时钟权威。

预算耗尽时在 publisher 调用前返回 Expired；第 76–79 行把剩余预算传给 linked cancellation，并显式检查已取消 token。新测试分别覆盖 commit 延迟超过租约、消耗剩余预算、短延迟正向发布及 commit 后取消拒发。延迟注入位于真实 `TransactionCommittedAsync`；并非模拟数据库提交成功。独立 dispose 延迟没有单独注入测试，其包含关系由上述 await/释放顺序确认。

原 send-attempt 的 claim/mutation/ticket 保留；超时/未知结果继续保守返回 OutcomeUnknown，不创建新 mutation 或重新发送。重复 Dispatch 遇到既有 send intent 仍拒发；原 `RecoverMutationV2` 与 completion tuple 查询未改变。publisher 入口实际查询 PostgreSQL route advisory lock 为零；timeout probe 的 `EntryVerified` 在 dispatcher 外断言，避免其 catch 掩盖探针失败。两个原 ambiguous/unconfirmed completion 回归也包含于 #67 GREEN。

## 留存运行与输入证据

本次只读检查源码、Git 历史、清单、TRX 和日志；**没有运行 dotnet test、启动/连接/停止 PostgreSQL、执行 Git 变更或远端操作**。依据仓库验证复用规则与 verification-before-completion 技能，核验实际输入后复用既有成功结果，不把新 SHA 称为新执行。

| 留存运行 | 实际结果 | 复用范围 |
|---|---:|---|
| #66 RED | 1 失败 | 原实现精确回放返回 Conflict，原失败保留 |
| #66 GREEN | 5/5 | 新 Blocked 恢复及相关 lifecycle 回归 |
| #67 RED | 2 失败 / 1 通过 | 过期仍调用 publisher、运输重新取得约 5.011 秒；短延迟正向通过 |
| #67 cancellation RED | 1 失败 | commit 后已取消仍调用 publisher |
| #67 GREEN | 7/7 | 四项新用例及原不确定交付/DB 过期回归 |
| #67 probe follow-up | 1/1 | 加强外部 EntryVerified 断言后的 timeout/no-lock 用例 |
| 聚合 smoke | 3/3 | 最终组合源码/DLL 的 Blocked 恢复、timeout/no-lock、短延迟发布 |

共 12 个不同的成功相关用例，5 个新增；7 份 TRX 均零跳过。实际解析的失败消息与上述缺陷吻合。全部 7 份 TRX SHA-256 同时匹配运行清单和 summary；全部 7 份原始日志存在且哈希匹配运行清单。

[聚合清单](D:/CP6.Platform-worktrees/wp02-08-corrected-20261014/docs/development/evidence/WP02-08/coord-corrections/integrated/smoke-inputs.json) 的 **142 源码、4 依赖、39 产物** 与最终工作树逐字节哈希全部吻合。#66 GREEN 所在独立工作树及 #67 green-probe 所在独立工作树，各自 142/4/39 项同样全部吻合。历史各运行与聚合清单的源码差异只涉及两个修复源码及共享测试文件；无额外依赖/项目配置变更。RED 生产源码哈希与原候选 CRLF 检出字节一致。历史 RED 二进制哈希作为当次证据保留，没有重建或宣称等同于当前 GREEN 二进制。

聚合两个关键 DLL：

| 产物 | SHA-256 |
|---|---|
| CP6.Platform.EntityFramework.dll | `6c417c4f832f0b707eba03585c68917c4f03bb3cf7f82fd7baf8b556171bf304` |
| CP6.Platform.WP0208.PostgreSqlTests.dll | `ce85532011f61d5514720aed04d96e552ebedfc5d18e1e230856f7cb592eacc1` |

运行记录为 SDK 8.0.424、.NET 8.0.30、EF 8.0.30、Npgsql 8.0.8、provider 8.0.11、PostgreSQL 18.6。实际 assets 中 EF/Npgsql/provider 版本与报告一致。完整机器可读核验结果在 [review-WP02-08-fixes.json](C:/Users/tt/CP6-planning-reports/20261014-wave6-integration/review-WP02-08-fixes.json)。

原 153 个成功用例保持其原候选/程序集身份，只复用未变化的 architecture/unit/PG 范围；本次没有把它们改称最终候选的全套重跑。原 evidence 目录除 REPORT 的缺失历史日志说明外未改写旧证据；原“不可用 assets 失败日志已保留”的表述已如实缩小，没有伪造缺失日志。

## 资源及准入边界

[停机证据](D:/CP6.Platform-worktrees/wp02-08-corrected-20261014/docs/development/evidence/WP02-08/coord-corrections/integrated/resource-stop.json) 记录任务 owner `dcdfb04dda48edc0d52d638b5e7bd2f0`、system ID `7696392773160066692`、数据库 `cp6_wp0208_20261014_dcdfb04d` / OID 16385、端口 55428、零外来连接后 smart stop、postmaster PID 已无、端口未监听，数据/日志保留。此处是对留存停机证据的复核；本 reviewer 没有另行连接或更改资源。

生产 diff 限于 OutboxLifecycleV2/OutboxDispatcherV2；原 mutation 恢复、no-lock 发送与既有 wire/public contract 边界保持。没有源码编辑、临时修复、远端写、Actions、共享状态、DB 操作或 worktree 清理。

本结论只解决原 #66/#67 的候选准入阻塞。Main/CRM 真实 Owner/writer/host 采用、真实 broker/network E2E、原 60 business AC、生产迁移/切换、包发布及部署仍为 UNPROVEN/NOT_RUN。COORD 负责最终远端 SHA/合并检查及与后续 WP02-07/WP02-25 的集成；本报告不代替该检查。

