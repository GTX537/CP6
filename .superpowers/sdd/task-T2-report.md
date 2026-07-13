# Task T2 报告：reaper AttemptCount 不再对「抢占未执行」的 job 误计一次重试

Status: **DONE**

## 缺陷核实证据（改前当前代码）

计划写于 07-05，行号已漂移，先核实缺陷仍在：

- **reaper 段**（`WfServiceJobService.cs` 改前 `:60-67`）确实对所有过期租约无脑自增：
  ```csharp
  foreach (var j in expired) {
      j.Status = ServiceJobStatus.Pending;
      j.AttemptCount++;              // ← 缺陷：无条件烧配额
      j.LockedBy = null; ...
  }
  ```
- **执行段计数**（改前 `:106-107`）`job.AttemptCount++` 后**不立即持久化**——直到成功(`:144`)/退避(`:156`)/失败(`:166`)才 SaveChanges。
- **lease 抢占**（`:81-85`）保存 `Status=Running` 是独立的一次 SaveChanges。

→ 结论：job 若在「抢到 lease（已落库 Running, AttemptCount 未增）」与「执行段自增+持久化」之间崩溃（如状态闸 DB 查询期），本次尝试**从未执行**，但 reaper 会在租约过期后 `AttemptCount++`，烧掉一次配额。极端 infra 抖动下可在从未真正跑过的情况下耗尽 MaxAttempts。**缺陷确认仍在。**

## 修法

1. reaper 去掉 `j.AttemptCount++`，只重置 lease 回 Pending。
2. 执行段 `job.AttemptCount++` 后**立即 `await _db.SaveChangesAsync(ct)`**，把「本次尝试已开始」落库，再调 executor。
   - 崩溃于 executor 期间 → 计数已落库（记 1 次，正确）
   - 崩溃于此保存之前 → 计数未增（记 0 次，正确）
3. 后续成功/退避/失败三分支不改——读的仍是同一已自增的 `job.AttemptCount`，行为不变。

零迁移（无实体/DbSet 改动）。

## TDD 红绿

**Step 1（RED）** 改 `Reaper_ResetsExpiredLease_Only` 断言 `AttemptCount==2` → `==1`（计划钦定），并新增 `Reaper_ClaimedButNeverExecuted_DoesNotBurnAttempt`（AttemptCount=0 抢占后崩溃 → reaper 回收后仍须 0）。

跑 `--filter ServiceJobScanTests`：
```
Failed CP6.Tests.Wf.ServiceJobScanTests.Reaper_ClaimedButNeverExecuted_DoesNotBurnAttempt
  Expected: 0  Actual: 1
Failed CP6.Tests.Wf.ServiceJobScanTests.Reaper_ResetsExpiredLease_Only
  Expected: 1  Actual: 2
Failed!  - Failed: 2, Passed: 8, Skipped: 0, Total: 10
```

**Step 3（实现）→ Step 4（GREEN）** 跑 `--filter Wf`：
```
Passed!  - Failed: 0, Passed: 189, Skipped: 0, Total: 189
```

**全量后端**：
```
Passed!  - Failed: 0, Passed: 1827, Skipped: 5, Total: 1832
```
基线 1826 passed → 1827（+1 新测试），零回归，断言改动不减数。

## 变更文件
- `CP6.Core/Services/Wf/WfServiceJobService.cs`（reaper 去自增；执行段计数前移+立即 SaveChanges）
- `CP6.Tests/Wf/ServiceJobScanTests.cs`（断言 2→1；新增 Reaper_ClaimedButNeverExecuted_DoesNotBurnAttempt）

## 疑虑
- 工作树内另有一处**非本任务**的预存改动 `.superpowers/sdd/task-2-brief.md`（Space Task 2 的 brief 被清空，属 Space 域），已按 Global Constraints「绝不碰 Space」排除出本次提交。
