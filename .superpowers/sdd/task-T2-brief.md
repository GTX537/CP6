## Task T2: reaper AttemptCount 不再对「已抢占但未执行」的 job 误计一次重试

> **票2。** 缺陷：`WfServiceJobService.ScanOnceAsync` 的 reaper（`:55-68`）对**所有**过期租约 job 无脑 `AttemptCount++`。但一个 job 变成 `Running` 只需 lease 抢占成功（`:81-85` 保存 `Status=Running`），**执行体尚未跑**（`AttemptCount++` 在 `:107`，且直到成功/退避/失败才 SaveChanges）。若 worker 在「抢到 lease」与「真正调 executor」之间崩溃（例如状态闸 DB 查询期），这次尝试**从未执行**，reaper 却烧掉一次重试配额——极端 infra 抖动下 job 可在从未真正跑过的情况下耗尽 `MaxAttempts`。修法=**把「尝试计数」的持久化前移到 executor 调用之前**（先 `AttemptCount++` 并 SaveChanges，标记「这次尝试已开始」），**reaper 不再自增**（只重置 lease 回 Pending）。这样：崩溃于执行中→计数已持久化（记 1 次，正确）；崩溃于执行前→计数未自增（记 0 次，正确）。

**Files:**
- Modify: `CP6.Core/Services/Wf/WfServiceJobService.cs:55-68`（reaper 去掉 `AttemptCount++`）、`:106-136`（执行段：把 `AttemptCount++` 前移 + 立即 SaveChanges）
- Modify: `CP6.Tests/Wf/ServiceJobScanTests.cs:237-270`（`Reaper_ResetsExpiredLease_Only` 断言从 `AttemptCount==2` 改为 `==1`）
- Test: `CP6.Tests/Wf/ServiceJobScanTests.cs`（新增 `Reaper_ClaimedButNeverExecuted_DoesNotBurnAttempt`）

- [ ] **Step 1: 改既有 reaper 测试的断言 + 新增「抢占未执行不计数」测试（先证明会 FAIL）**

  a. 改 `ServiceJobScanTests.cs:258-263` 的 A 分支断言（reaper 不再自增，故过期租约 job 的 `AttemptCount` **保持不变**）：

```csharp
        var ja = await db.Wf_ServiceJobs.SingleAsync(j => j.Id == a.Id);
        Assert.Equal(ServiceJobStatus.Pending, ja.Status);
        Assert.Equal(1, ja.AttemptCount);          // reaper 只重置 lease，不再 ++（原持久化的尝试计数保持）
        Assert.Null(ja.LockedBy);
        Assert.Null(ja.LockedAtUtc);
        Assert.Null(ja.LockExpiresAtUtc);
```

  并把 A 分支上方注释（`:241`）从「应重置 Pending + AttemptCount++」改为「应重置 Pending（不 ++；计数由执行段前移持久化负责）」。

  b. 新增测试（放在 `Reaper_ResetsExpiredLease_Only` 之后）：

```csharp
    [Fact]
    public async Task Reaper_ClaimedButNeverExecuted_DoesNotBurnAttempt()
    {
        using var db = NewDb();
        // 场景：worker 抢到 lease（Status=Running, AttemptCount=0）后、在真正执行前就崩溃。
        // lease 过期 → reaper 回收，但因从未执行，AttemptCount 必须仍为 0（不烧配额）。
        var j = new Wf_ServiceJob { Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(),
            NodeId = "svc", Kind = ServiceKind.WebApi, Status = ServiceJobStatus.Running,
            AttemptCount = 0, MaxAttempts = 4, NextAttemptAtUtc = T0.AddHours(1),
            LockedBy = "deadWorker", LockedAtUtc = T0.AddMinutes(-10), LockExpiresAtUtc = T0.AddMinutes(-1),
            CreateDate = DateTime.UtcNow };
        db.Wf_ServiceJobs.Add(j);
        await db.SaveChangesAsync();

        var eng = Engine(db);
        await new WfServiceJobService(db, eng, Array.Empty<IServiceTaskExecutor>()).ScanOnceAsync(T0, "w1");

        var reclaimed = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Pending, reclaimed.Status);
        Assert.Equal(0, reclaimed.AttemptCount);   // 从未执行 → 不计数
        Assert.Null(reclaimed.LockedBy);
    }
```

  > `T0` / `NewDb()` / `Engine(db)` 为该测试类既有脚手架（见文件顶部与 `:113`）。

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceJobScanTests`。预期：`Reaper_ResetsExpiredLease_Only`（新断言）与 `Reaper_ClaimedButNeverExecuted_DoesNotBurnAttempt` 均 FAIL（当前 reaper 会把两者都 ++）。

- [ ] **Step 3: 实现**

  a. reaper 去掉自增。`WfServiceJobService.cs:60-67` 的 foreach 改为：

```csharp
        foreach (var j in expired)
        {
            // reaper 只回收过期租约、复位为 Pending 重投；**不自增 AttemptCount**——尝试计数由执行段
            // 在调 executor 之前持久化（见下 ⑤）。这样「抢到 lease 但从未执行就崩溃」不会烧掉重试配额。
            j.Status = ServiceJobStatus.Pending;
            j.LockedBy = null;
            j.LockedAtUtc = null;
            j.LockExpiresAtUtc = null;
        }
```

  b. 执行段把 `AttemptCount++` 前移并**立即持久化**。当前 `:106-107`：

```csharp
                // ⑤ 执行
                job.AttemptCount++;
                ServiceTaskResult result;
```

  改为（在 `job.AttemptCount++` 后立刻 SaveChanges，把「本次尝试已开始」落库，随后再解析/执行）：

```csharp
                // ⑤ 执行：先把「本次尝试已开始」持久化（AttemptCount++ 立即入库），再调 executor。
                //    崩溃于 executor 期间 → 计数已落库（记 1 次）；崩溃于此保存之前 → 计数未增（记 0 次）。
                //    reaper 因此无需（也不再）自增，杜绝「抢占未执行」误烧配额（票2）。
                job.AttemptCount++;
                await _db.SaveChangesAsync(ct);
                ServiceTaskResult result;
```

  > 其余分支（成功 `:139-145`、退避 `:148-157`、失败 `:158-167`）不改——它们读的仍是同一个已自增的 `job.AttemptCount`，行为不变。退避测试（`:178-197`）种子 `AttemptCount=0`→执行→`==1`，仍成立。

- [ ] **Step 4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceJobScanTests`，全绿（含退避/状态闸/timer 既有用例——它们的计数期望不变）。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "fix(wfs-service-task): T2 尝试计数前移到执行前持久化，reaper 不再对抢占未执行的 job 误烧重试配额"
```

---

## Global Constraints（每个 Task 都遵守）

- **测试基线不回归：**
  - 后端：`dotnet test CP6.Tests/CP6.Tests.csproj` 全绿——基线 **1509 测试**（5 skip = SQLite 既知限制）。`--filter Wf` 既有 Wf 测试字节等价（除本计划显式改动的测试断言外）。
  - 前端：`npm run test`（vitest run）**320 全绿** + `npm run type-check` 通过。**type-check 须大堆**（vue-tsc 内存密集）：
    - Bash 工具：`NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`
    - PowerShell：`$env:NODE_OPTIONS='--max-old-space-size=8192'; npm run type-check`
- **EF 迁移 clean：**`dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 报无 pending（本计划**不新增迁移**——无实体/DbSet 改动）。
- **零跨模块污染：**只碰 `CP6.Core/Services/Wf/**`、`CP6.WebApi/{Program.cs,Middleware,Seed}`、`cp6.web/src/views/oa/designer/**`、`cp6.web/src/utils/signalr.ts`、对应 `CP6.Tests/Wf/**`。**绝不碰** `views/space/**`、`Services/*Space*`、任何 Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核 diff。
- **零硬编码色：**前端一切颜色走 Design System token（`var(--cp-danger)` 等，见 `cp6.web/src/styles/tokens.css`），禁十六进制字面量。
- **i18n 五语齐全：**任何新增文案键必须五语齐全 `ZhCN/ZhTW/En/Ja/Ko`，加进 `I18nOaServiceTaskScreenSeed.cs`，运行期 SeedLangs 幂等去重。
- **TDD 节奏：**先写失败测试→跑验证 FAIL→最小实现→跑验证 PASS→本地 commit（**不 push**）。提交信息风格：`fix(wfs-service-task): <中文描述>`。
- **独立性：**11 个 Task 互不依赖，可任意顺序 / 并行执行。建议顺序见文末「执行顺序」。

