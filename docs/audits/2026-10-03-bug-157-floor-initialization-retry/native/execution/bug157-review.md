**BUG157 任务级静态源码审查 — 2026-10-03**

结论：三份源码候选未发现实质阻断。P0/P1/P2 问题均为 0；无需源码修正。此结论覆盖创建楼层重试、相关调用链及七项新增回归测试，不替代 root 的最终交付 diff、文档和远端主线核对。

审查分支为 `codex/bug-157-floor-initialization-retry`，工作树为 `D:\CP6\tmp\worktrees\bug-157-floor-initialization-retry`。基线及审查时 HEAD 均为 `2e1f90c629340944d95fadd7aee428f639304ed6`；三源码改动尚未提交。

**适用源码指纹**

| 文件 | SHA-256 |
|---|---|
| [SpaceDesignV1Service.cs](D:/CP6/tmp/worktrees/bug-157-floor-initialization-retry/CP6.Space.Infrastructure/SpaceDesignV1Service.cs) | `11696A35B0FAAD742EBBF06C40619AFBFB1F38DE82D0BB6280B4059A8CC3ECAB` |
| [SpaceVersionCloneSqlServerTests.cs](D:/CP6/tmp/worktrees/bug-157-floor-initialization-retry/CP6.Space.IntegrationTests/SpaceVersionCloneSqlServerTests.cs) | `FC65F18FDE602A34E2CE819CE26A2C538948472C24517A59EED1D02E56D80CF1` |
| [SpaceVersionCloneSqlServerTests.FloorRecoveryTests.cs](D:/CP6/tmp/worktrees/bug-157-floor-initialization-retry/CP6.Space.IntegrationTests/SpaceVersionCloneSqlServerTests.FloorRecoveryTests.cs) | `611E4F06C7E48E528840EE717633CD18CB2E51AB96A1AE8BDF348017943EC6D1` |

三份实际文件哈希与 PostgreSQL 精确回归的 `source-inputs.json` 一致。该结果的源码输入清单哈希为 `91F63672201055365B038346475C73FE993E0D92F44F5EA597A87C55596F86C8`，运行物清单哈希为 `A3A775D9A459B6D813B33CA6C5D8A417C397C468E496BE61789FFEC9AC0FEF1B`。

**源码检查结果**

- **重试边界正确。** [服务入口第 211 行](D:/CP6/tmp/worktrees/bug-157-floor-initialization-retry/CP6.Space.Infrastructure/SpaceDesignV1Service.cs:211)拒绝 caller explicit、ambient、enlisted transaction，并在关系数据库入口拒绝未保存变更，避免保存或清除调用者的数据。最多三次尝试，仅接收既有 classifier 的 `CanRetryTransaction`；业务冲突、未知失败和乐观并发异常不会因此成为通用重试。
- **恢复顺序正确。** attempt 自己持有 Serializable 事务，异常路径以 `CancellationToken.None` 回滚；外层恢复发生在 attempt 的 `await using` 退出之后。[第 243 行](D:/CP6/tmp/worktrees/bug-157-floor-initialization-retry/CP6.Space.Infrastructure/SpaceDesignV1Service.cs:243)再次确认事务已解除，然后 `ChangeTracker.Clear()`。耗尽后保留最终异常；每次新尝试之前检查取消。
- **协议完整重放。** 清理后重新查询 model、重新检查站点访问权限、重新计算标准化输入并读取幂等记录；取得楼层初始化锁后再读 replay，随后检查 Draft 状态、content revision 和重复 floor code。相同 key、相同输入可返回胜者回放；不同 key、旧 expected revision 得到 `ConcurrencyConflict`。楼层、revision 增量及幂等记录仍位于同一事务内。
- **调用链与 guard 相容。** [HTTP controller 第 170 行](D:/CP6/tmp/worktrees/bug-157-floor-initialization-retry/CP6.WebApi/Controllers/Space/SpaceDesignV1Controller.cs:170)直接调用服务，没有创建 caller 事务。模板初始化在查询及调用创建楼层之前清空跟踪状态；[StartBlankOnceAsync 第 149 行](D:/CP6/tmp/worktrees/bug-157-floor-initialization-retry/CP6.Space.Infrastructure/EfSpaceVersionClone.cs:149)持有的事务在返回调用者前已结束并退出 `await using`。未发现 clean-context guard 破坏现有模板初始化流程。
- **回归断言可信。** 两项协调测试对 PostgreSQL 建立真实 Serializable snapshot，并要求观察原生 `40001`；同时检查不同 key 的业务冲突、同 key 的回放，以及最终唯一楼层、唯一幂等记录和 revision `1`。原有并发测试的一成功、一 `ConcurrencyConflict` 断言保留。interceptor plumbing 仍保留 fixture 的原生失败观察器。
- **注入控制定位准确。** 未知失败、取消、耗尽三项测试使用真实数据库事务，但在写入前注入异常。源码已明确区分这些控制与协调回归的真实 PostgreSQL `40001`；它们不能称为原生死锁复现。

**已独立读取的历史执行证据**

| 证据 | 观察结果 |
|---|---|
| [原 WP6 PostgreSQL TRX](D:/CP6/tmp/wp6-pg-formal-matrix-validation-fix/entries/space-clone/private/trx/results.trx) | 16 项中 15 Passed、1 Failed；原并发楼层用例的异常链包含 `PostgresException: 40001`。 |
| [确定性 native RED 结果](D:/CP6/tmp/bug157-pg-coordinated-native-red-v2/result.json)及对应 TRX | 两项协调回归均 Failed；两项异常链均包含真实 `40001`，原生观察器亦记录该 SQLSTATE。 |
| [PostgreSQL 精确 GREEN 结果](D:/CP6/tmp/bug157-pg-clone-exact-green/result.json)、[用例集合核对](D:/CP6/tmp/bug157-pg-clone-exact-green/exact-case-set-audit.json)及对应 TRX | 23/23 Passed，0 Failed、0 Other、进程 exit `0`；`Success=true`、`ExactCaseSetMatches=true`，源码及运行物未变化。 |
| [先前类级运行](D:/CP6/tmp/bug157-pg-clone-green/result.json) | 实际 26/26 Passed，但 expected 为 23，runner 的 `Success=false`。该失败记录保留，未升级为门禁通过。 |

root 随后通知 SQL Server 精确 clone 为 23/23、PostgreSQL 与 SQL Server design/publish 各为 41/41，均为 0 Other、exit `0`，且源码与运行物指纹未变化。这些新增结果由 root 执行和核验，本审查未独立读取其新证据文件。

**验证限制与交付判断**

本审查严格只读，仅检查 Git 差异、源码、调用链、哈希及已有结果；没有运行 .NET、数据库、API、GitHub Actions，没有修改文件、提交或启动其他代理。

ambient/enlisted guard 分支仅完成静态核对；新增自动化直接覆盖显式 caller transaction 和未保存 caller changes。SQL Server 协调测试验证对应业务语义，并未要求产生原生死锁。注入控制发生在写入前，不能单独证明第二次 SaveChanges 后发生原生失败的场景。

源码审查通过，无实质阻断。root 可继续完成最终证据与文档归档、完整交付 diff 和仓库要求的合并及远端核对；新增 README、`.gitattributes` 和归档材料不在本次三源码审查范围内。
