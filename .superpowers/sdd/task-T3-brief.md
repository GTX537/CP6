## Task T3: 启动期校验连接器调用时长 < 租约时长（防长调用被 reaper 误重投→重复外呼）

> **票3。** 缺陷：`WfServiceJobService.LeaseDuration = 5min`（`:30`）。若某 `IWfConnector` 的 HTTP 调用耗时可能 > 5min，reaper（`:56-59`）会把仍在执行的 job 复位为 Pending 并重投——同一动作被执行两次（幂等键能兜业务，但仍是设计不变量违背）。当前 `IWfConnector` 契约（`IWfConnector.cs`）**不暴露超时**，无从校验。修法=给 `IWfConnector` 加**可选** `MaxCallDuration`（默认 `null`=未声明/假定安全），并在启动期加一道断言 guard：任何连接器声明的 `MaxCallDuration >= LeaseDuration` → 抛异常快速失败，逼未来真实连接器把超时配在租约内。EchoConnector 返回 `null`，通过。

**Files:**
- Modify: `CP6.Core/Services/Wf/IWfConnector.cs`（加 `TimeSpan? MaxCallDuration => null;` 默认实现成员）
- Create: `CP6.Core/Services/Wf/WfConnectorLeaseGuard.cs`（纯静态校验 + 抛错）
- Modify: `CP6.WebApi/Program.cs`（在 `var app = builder.Build();` 之后、`app.Run()` 之前调用 guard）
- Test: `CP6.Tests/Wf/WfConnectorLeaseGuardTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/WfConnectorLeaseGuardTests.cs
using System;
using CP6.Core.Services.Wf;
using Xunit;

public class WfConnectorLeaseGuardTests
{
    private sealed class SafeConn : IWfConnector {
        public string Name => "safe"; public string DisplayName => "Safe";
        public TimeSpan? MaxCallDuration => TimeSpan.FromMinutes(1);   // < 5min 租约
        public System.Threading.Tasks.Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => System.Threading.Tasks.Task.FromResult(ServiceTaskResult.Ok());
    }
    private sealed class SlowConn : IWfConnector {
        public string Name => "slow"; public string DisplayName => "Slow";
        public TimeSpan? MaxCallDuration => TimeSpan.FromMinutes(6);   // >= 5min 租约 → 非法
        public System.Threading.Tasks.Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => System.Threading.Tasks.Task.FromResult(ServiceTaskResult.Ok());
    }
    private sealed class UndeclaredConn : IWfConnector {
        public string Name => "echo"; public string DisplayName => "Echo";
        // 不覆写 MaxCallDuration → 默认 null → 通过（假定安全，EchoConnector 同款）
        public System.Threading.Tasks.Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => System.Threading.Tasks.Task.FromResult(ServiceTaskResult.Ok());
    }

    [Fact] public void Passes_WhenAllUnderLease()
        => WfConnectorLeaseGuard.Validate(new IWfConnector[] { new SafeConn(), new UndeclaredConn() });

    [Fact] public void Throws_WhenConnectorAtOrOverLease()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => WfConnectorLeaseGuard.Validate(new IWfConnector[] { new SafeConn(), new SlowConn() }));
        Assert.Contains("slow", ex.Message);
        Assert.Contains("MaxCallDuration", ex.Message);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter WfConnectorLeaseGuardTests`（编译失败：`MaxCallDuration`/`WfConnectorLeaseGuard` 不存在）。

- [ ] **Step 3: 实现**

  a. `IWfConnector.cs` 加默认接口成员（放在 `DisplayName` 之后、`CallAsync` 之前）：

```csharp
    /// <summary>本连接器单次调用的上界耗时（含内部重试）。用于启动期校验其 &lt; 租约时长
    /// （<see cref="WfServiceJobService.LeaseDuration"/>），防长调用被 reaper 误判崩溃而重投→重复外呼。
    /// 默认 null = 未声明（假定安全，如 demo EchoConnector）；真实 HTTP 连接器应据 HttpClient.Timeout 如实声明。</summary>
    TimeSpan? MaxCallDuration => null;
```

  b. 新建 `WfConnectorLeaseGuard.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace CP6.Core.Services.Wf;

/// <summary>启动期护栏（票3）：任何连接器声明的单次调用上界 >= 租约时长即抛错快速失败，
/// 逼真实连接器把 HTTP 超时配在租约内，杜绝「长调用未完 → reaper 复位重投 → 重复外呼」。</summary>
public static class WfConnectorLeaseGuard
{
    public static void Validate(IEnumerable<IWfConnector> connectors)
    {
        var lease = WfServiceJobService.LeaseDuration;
        var offenders = (connectors ?? Enumerable.Empty<IWfConnector>())
            .Where(c => c.MaxCallDuration is TimeSpan d && d >= lease)
            .Select(c => $"{c.Name}(MaxCallDuration={c.MaxCallDuration})")
            .ToList();
        if (offenders.Count > 0)
            throw new InvalidOperationException(
                $"WfConnector 租约校验失败：以下连接器 MaxCallDuration >= 租约 {lease}，" +
                $"reaper 会误判崩溃并重投导致重复外呼——请把 HTTP 超时收进租约内：{string.Join(", ", offenders)}");
    }
}
```

  c. `Program.cs` 在 `var app = builder.Build();`（约 `:520` 一带；用 Grep 定位 `var app = builder.Build();`）之后立即加：

```csharp
// 票3：启动期校验已注册连接器的单次调用上界 < 服务任务租约（防长调用被 reaper 重投→重复外呼）。
using (var _leaseScope = app.Services.CreateScope())
{
    CP6.Core.Services.Wf.WfConnectorLeaseGuard.Validate(
        _leaseScope.ServiceProvider.GetServices<CP6.Core.Services.Wf.IWfConnector>());
}
```

  > `GetServices<T>` 需 `using Microsoft.Extensions.DependencyInjection;`（`Program.cs` 已有）。

- [ ] **Step 4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter WfConnectorLeaseGuardTests`。

- [ ] **Step 5: 编译 + Wf 闸 + commit**
```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "fix(wfs-service-task): T3 IWfConnector.MaxCallDuration + 启动期租约护栏（防长调用被 reaper 重投重复外呼）"
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

