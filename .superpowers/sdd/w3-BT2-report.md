# Task B-T2 报告：`ScanTimersOnceAsync` 占坑两段式

**STATUS: DONE（8/8 新测绿；全量 1923 passed / 5 skipped；migration clean）**

## 交付
- 实现 `CP6.Core/Services/Wf/FlowTriggerService.cs` 的 `ScanTimersOnceAsync(DateTime nowUtc, CancellationToken)`（替换 A-T2 的 `NotImplementedException` 占位）。
- 新测 `CP6.Tests/Wf/FlowTriggerTimerScanTests.cs`（8 剧本，逐字转录 brief）。

## TDD 证据
### RED（Step 2）
`dotnet test --filter FlowTriggerTimerScanTests` → `Failed: 8, Passed: 0`，全部 `System.NotImplementedException : B-T2`（栈顶 `FlowTriggerService.cs:119`）。

### GREEN（Step 4）
`dotnet test --filter FlowTriggerTimerScanTests` → `Passed! Failed: 0, Passed: 8, Total: 8`。
覆盖：到期发火+NextDue 前移+LastFired、未到期跳过、停用/非 Timer 类型过滤、双 worker 只发一次（RowVersion/占坑唯一键让位）、崩溃恢复补跑（不丢发不双发）、宽限期内不抢跑、misfire 只补最近、坏 cron 记 E-WF-022 停摆不空转。

## 闸门
1. `--filter Wf` → `Passed: 267, Failed: 0`。
2. `dotnet test CP6.slnx` → `Passed: 1923, Skipped: 5, Failed: 0`（= 基线 1915 + 8 新测）。
3. `dotnet ef migrations has-pending-model-changes` → `No changes have been made to the model since the last migration`（零新迁移）。
4. `git status --short` → 仅 `FlowTriggerService.cs`（M）+ `FlowTriggerTimerScanTests.cs`（??）。

## 集成决策（偏离 brief 提供的实现块——一处，已定位并最小修正）

**问题**：brief 的实现块逐字落地后，3 个「实发到期 timer」剧本（DueTimer / Misfire / TwoWorkers 的 A 侧）在**实例计数断言**处失败：`ScanTimersOnceAsync` 返回值 n==1 正确，但 `Wf_FlowInstances.CountAsync()==0`，占坑行被回填 `E-WF-024: ...expected to affect 1 row(s), but actually affected 0`。

**根因**（诊断脚本实证）：第一段 SaveChanges UPDATE 触发器行后，SQLite 测试基座的 `AFTER UPDATE trg_Wf_FlowTrigger_RowVersion` 触发器在**库内**把 RowVersion 刷成新 randomblob；但 `HasTrigger` 关闭了 RETURNING，EF **追踪实例内存中的 RowVersion 仍是旧令牌**。第二段 `FireAsync` 通过 identity-map 复用同一陈旧追踪实例、再写触达 `LastFiredUtc` → `WHERE RowVersion IS NULL` 与库内新令牌不匹配 → 0 行 → `DbUpdateConcurrencyException` → FireAsync 兜底写 `E-WF-024`、不建实例。此为**测试基座特有产物**：生产 SQL Server 于第一段 SaveChanges 后由 OUTPUT 自动回读新 RowVersion，第二段令牌鲜活，无冲突。该 RowVersion 触发器是本波为 TwoWorkers 抢占测试专门追加到 `Wf_FlowTrigger` 的（harness 注释自陈）。

**最小修正**：第一段成功提交后、调 `FireAsync` 前，`_db.Entry(trig).State = EntityState.Detached;`。使 FireAsync 内既有的 `FirstOrDefaultAsync(t => t.Id == ...)` 落库重查到带**当前** RowVersion 的鲜活实例，第二段写触达即匹配。修正只落在本方法（brief 列明文件），不动 A-T2 的 `FireAsync`（其自有测试不受影响）；生产路径下 detach 幂等无害（FireAsync 本就无条件重查该行）。TwoWorkers 的 B 侧不受影响——其在第一段即撞 RowVersion 让位、根本不进第二段。

## NextUtc 返回 null 集成说明（brief 上下文点名）
B-T1 审查修复令 `WfCronHelper.NextUtc` 对「语法合法但永不匹配」的 cron（Year>=9999 哨兵）返回 null，与语法非法同归 null。brief 的实现块对 `next == null` 已有统一分支：`NextDueUtc=null` + 记 `E-WF-022` + 不占坑不空转。故本任务**无需额外处理**——两类 null 都走同一「停摆等人工修复」语义，与 `BadCron_MarksError_DoesNotSpin` 剧本一致。此为已被 brief 覆盖，非新决策，仅备注。

## 自审
- 幂等键口径 `$"{trigger.Id}:{dueUtc:O}"`（旧 NextDueUtc）——DueTimer/Misfire 断言精确匹配。
- 两段各自幂等：占坑靠 `(TenantId,TriggerId,IdempotencyKey)` 唯一键；完成靠 `InstanceId!=null` 状态闸。
- 补跑扫描只捡 `Source==Timer && InstanceId==null && Error==null && FiredUtc < now-RecoveryGrace`（spec §3.2 原文，Error 行不重扫）。
- misfire：`NextUtc(cron, nowUtc)` 从当前时刻起算严格未来 → 跨过的历史到期点只补最近一次、NextDue 直推未来，同 nowUtc 二次扫零动作（Misfire 剧本 n2==0 验证）。
- 逐条重查契约（FireAsync 失败路径 `ChangeTracker.Clear` 使批量实体失联）已遵循。
- 引擎零改动；零新迁移；surgical git add。

## 关切
- detach 修正是为 SQLite 测试基座的 RowVersion 触发器而设，生产 SQL Server 路径为无害冗余重查（一次已有的 DB 往返，无额外开销）。若后续 C-T/worker 波在真库做集成测试，可确认 OUTPUT 回读路径下第二段无冲突（预期如此）。
