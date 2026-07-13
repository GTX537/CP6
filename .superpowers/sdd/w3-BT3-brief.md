### Task B-T3: WfTriggerWorker（BackgroundService）+ DI

**Files:**
- Create: `CP6.WebApi/BackgroundServices/WfTriggerWorker.cs`
- Modify: `CP6.WebApi/Program.cs`（`AddHostedService`，放 WfServiceJobScanWorker 注册同块）

- [ ] **Step 1: 实现** — 照 `WfServiceJobScanWorker.cs` 逐字克隆（骨架 + TenantScopeRunner 租户切换现状写法，spec §6），差异仅：无 workerId（抢占靠 RowVersion+NextDueUtc 前移，无 lease）、间隔 30s、日志文案：

```csharp
// CP6.WebApi/BackgroundServices/WfTriggerWorker.cs
using CP6.Core.Services.Wf;

namespace CP6.WebApi.BackgroundServices;

/// <summary>流程触发器 timer 扫描（spec §3.2）。逐租户 scope 切换照 TenantScopeRunner 现状口径（spec §6）；
/// 多实例安全：抢占 = Wf_FlowTrigger.RowVersion 乐观并发 + NextDueUtc 前移 + 占坑唯一键，无需 lease。</summary>
public class WfTriggerWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);   // cron 最小粒度 1min，30s 扫描足够
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WfTriggerWorker> _logger;

    public WfTriggerWorker(IServiceScopeFactory scopeFactory, ILogger<WfTriggerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wf 触发器扫描 Worker 启动");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, ct) =>
                    {
                        var svc = sp.GetRequiredService<IFlowTriggerService>();
                        var n = await svc.ScanTimersOnceAsync(ct);
                        if (n > 0) _logger.LogInformation("Wf 触发器扫描处理租户 {Tenant} {Count} 条", tenantId, n);
                    }, _logger, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Wf 触发器扫描异常"); }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("Wf 触发器扫描 Worker 停止"); }
    }
}
```

- [ ] **Step 2: DI** — `Program.cs` WfServiceJobScanWorker 注册同块追加：

```csharp
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.WfTriggerWorker>();   // 事件触发 start：timer 扫描
```

- [ ] **Step 3: 编译 + Wf 闸 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): B-T3 WfTriggerWorker 逐租户扫描(照 TenantScopeRunner 现状口径)+DI"
```

---


---
## 附: 现状锚点(worker骨架/租户切换)
| 租户切换现状写法（spec §6 照抄对象） | `CP6.WebApi/BackgroundServices/TenantScopeRunner.cs`：`ForEachTenantAsync(scopeFactory, body, logger, ct)` —— 先开 scope 用 `ITenantEnumerator.ListActiveAsync()` 取启用租户；**逐租户 `CreateScope()` → `scope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantId = tenantId`（setter 赋值即切换）→ 跑 body**；单租户异常记日志跳过继续。service 层**零租户感知**（查询不带 TenantId 条件，全靠 `CP6Context` 全局过滤读 scoped `ITenantContext`）。 |
| worker 骨架 | `WfServiceJobScanWorker.cs`（52 行）：`Interval=20s`；进程级 `_workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}"`；`ExecuteAsync` while 循环内 `TenantScopeRunner.ForEachTenantAsync(...)`，`catch (OperationCanceledException) when (...) { throw; } catch (Exception ex) { _logger.LogError(...) }`，`await Task.Delay(Interval, stoppingToken)`。 |
| lease/乐观并发写法 | `WfServiceJobService.cs:80-86`：置字段后 `try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { await _db.Entry(job).ReloadAsync(ct); continue; }`。常量 `BatchSize=50`、`Trunc` 截 1000。 |
