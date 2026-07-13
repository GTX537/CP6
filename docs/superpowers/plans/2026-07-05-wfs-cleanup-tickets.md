# WFS 收尾票清理 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 消化 WFS ServiceTask（已并 main，merge `fb90d75`）live QA 留下的 11 张收尾票。这些票各自独立、无 spec、方案均已裁定；本计划把每票落成一个可由零上下文工程师逐任务执行的 Task（失败测试→跑确认失败→最小实现→跑通过→commit）。11 票覆盖：后端配置优先级 / 异步引擎重试计数正确性 / 租约安全护栏 / 模板求值限制 / 校验值域 / 结构化错误码 / 前端目录重试 / 定时器变体 UI / 错误边视觉 / 韩文译文润色 / SignalR CSRF 放行。

**Architecture:** ServiceTask 引擎已就位——单一 `serviceTask` 节点类型 + `IServiceTaskExecutor` 注册表 + `Wf_ServiceJob` 异步停泊台账 + `WfServiceJobScanWorker`（20s 周期、lease 抢占、reaper 回收）。sync=内联乐观一击；async/timer=停泊 token + 入队，worker 到点执行→`ResumeServiceTokenAsync`（幂等）/耗尽→`FailServiceTokenAsync`→错误边或 Suspend。前端设计器（`cp6.web/src/views/oa/designer`）已有 3 调色板入口 + 按 kind 切换属性面板 + 错误边复选。本计划**不新增架构**，只修既有实现的 11 处缺陷/缺口。

**Tech Stack:** .NET 8 / EF Core（SQL Server 生产，SQLite 测试）/ xUnit（`CP6.Tests/Wf`）/ Vue3 + Vue Flow（`cp6.web/src/views/oa/designer`）/ vitest（前端 spec 与源码同目录 `*.spec.ts`）/ SignalR（`/hubs/notify`）。

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

---

## Task T1: Local.json 配置优先级修正（env vars 覆盖连接串被静默吞）

> **票1。** 缺陷：`appsettings.Local.json` 通过 `AddJsonFile` 追加到配置源链**末尾**——而 `WebApplication.CreateBuilder(args)` 已经把环境变量源加在前面。ASP.NET 后加的源优先级更高，故 **Local.json 覆盖了环境变量**，与注释宣称的「env vars 最后（覆盖 Local）」相反。结果：生产/容器里用 `ConnectionStrings__DefaultConnection` 环境变量覆盖连接串会被 Local.json 静默吞掉。修法=把 Local.json 源**插到环境变量源之前**，恢复标准 ASP.NET 优先级（env vars 最高），并同步改注释。

**Files:**
- Modify: `CP6.WebApi/Program.cs:16-20`（`AddJsonFile("appsettings.Local.json", ...)` 处）

**说明（为何不能只写测试）：** 配置源顺序在 `Program.cs` 顶层构建期生效，无法用 xUnit 对 `CP6.WebApi` 主机做单元断言（无 `WebApplicationFactory` 脚手架，且引入它会拖起全量 DI）。本 Task 用**可复现的手工验证脚本**替代自动化测试——构造一个最小 `ConfigurationBuilder` 复刻真实源顺序，断言 env 胜出。

- [ ] **Step 1: 写验证脚本（复刻源顺序，先证明当前顺序 env 落败）** — 在 scratchpad 建 `verify-config-order.csx`（或临时控制台），复刻「先 env、后 Local.json」的错误顺序，断言 Local.json 值胜出（即缺陷成立）：

```csharp
// 复刻当前（错误）顺序：CreateBuilder 已加 env vars，再 AddJsonFile(Local) 追加到末尾 → Local 胜出
using Microsoft.Extensions.Configuration;
System.Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "FROM_ENV");
System.IO.File.WriteAllText("appsettings.Local.json",
    "{\"ConnectionStrings\":{\"DefaultConnection\":\"FROM_LOCAL\"}}");
var wrong = new ConfigurationBuilder()
    .AddEnvironmentVariables()                               // CreateBuilder 顺序：env 先
    .AddJsonFile("appsettings.Local.json", optional: true)   // 当前代码：Local 追加在后 → 覆盖 env
    .Build();
System.Console.WriteLine($"WRONG order winner = {wrong.GetConnectionString("DefaultConnection")}");
// 预期输出 FROM_LOCAL —— 证明缺陷（env 被吞）
```

  跑 `dotnet script`（或 `dotnet run` 临时控制台），确认打印 `FROM_LOCAL`（缺陷成立）。

- [ ] **Step 2: 实现修法** — `Program.cs:16-20` 改为把 Local.json 源**插到环境变量源之前**（不要简单 `AddJsonFile`，那样只会追加到末尾）。当前代码：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 本地凭证覆盖（appsettings.Local.json 在 .gitignore，绝不入仓库）。
// 加载顺序：appsettings.json → appsettings.{Env}.json → appsettings.Local.json → env vars
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
```

  替换为：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 本地凭证覆盖（appsettings.Local.json 在 .gitignore，绝不入仓库）。
// 优先级（低→高）：appsettings.json → appsettings.{Env}.json → appsettings.Local.json → env vars → 命令行。
// 关键：CreateBuilder 已把 env vars/命令行源加在链尾（高优先级）。若用 AddJsonFile 追加，Local.json 会落到
// 更后、反而覆盖 env vars —— 容器里 ConnectionStrings__* 环境变量会被静默吞。故把 Local.json 源**插到 env vars 源之前**，
// 恢复标准 ASP.NET 优先级（env vars 最高）。
var localJsonSource = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
{
    Path = "appsettings.Local.json",
    Optional = true,
    ReloadOnChange = true,
};
localJsonSource.ResolveFileProvider();
// 注意：Sources 是 IList<IConfigurationSource>，没有 List<T>.FindIndex——手写循环找 env vars 源下标。
var envVarIdx = -1;
for (var i = 0; i < builder.Configuration.Sources.Count; i++)
    if (builder.Configuration.Sources[i] is Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource)
    { envVarIdx = i; break; }
if (envVarIdx >= 0)
    builder.Configuration.Sources.Insert(envVarIdx, localJsonSource);   // 插到 env vars 之前 → env 仍最高
else
    builder.Configuration.Sources.Add(localJsonSource);                 // 兜底（理论不达）
```

- [ ] **Step 3: 验证修法** — 在 Step 1 的脚本追加「正确顺序」断言并跑，确认 env 胜出：

```csharp
var srcs = new ConfigurationBuilder();
srcs.Sources.Add(new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
    { Path = "appsettings.Local.json", Optional = true });
srcs.Sources.Add(new Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource());
var fixedCfg = srcs.Build();
System.Console.WriteLine($"FIXED order winner = {fixedCfg.GetConnectionString("DefaultConnection")}");
// 预期输出 FROM_ENV —— env vars 恢复最高优先级
```

- [ ] **Step 4: 编译闸 + commit**
```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
git add -A && git commit -m "fix(wfs-service-task): T1 Local.json 配置源插到 env vars 之前，恢复环境变量最高优先级（修复容器连接串被静默吞）"
```

---

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

## Task T4: ServiceVarsHelper 点路径限制文档化 + 校验报错（含点键名/数组下标不支持）

> **票4。** 缺陷：`ServiceVarsHelper.ResolveDotPath`（`:128-158`）用 `path.Split('.')` 逐段导航，故 (a) 键名**本身含点**（如 `{"a.b":1}`）无法表达取值；(b) **数组下标**（如 `$.items[0]`）不被支持——`current["items[0]"]` 返回 null，模板静默求值为空串，用户无从察觉。方案裁定（YAGNI）：**不实现转义/下标**，改为「文档化限制 + 设计期校验报错」。修法=(1) 在 helper 补明确 XML 文档说明限制；(2) 加静态探测 `ContainsUnsupportedSubscript`；(3) 在 `FlowSchemaValidator` 的 serviceTask 分支扫描 `ServicePath`/`ServiceParamsJson` 中的模板 token，若含下标语法 `[...]` → `E-WF-016`（设计期即拦，不留到运行期静默失败）。

**Files:**
- Modify: `CP6.Core/Services/Wf/ServiceVarsHelper.cs`（补文档 + `ContainsUnsupportedSubscript`）
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs:85-95`（serviceTask 分支加模板下标校验）
- Test: `CP6.Tests/Wf/ServiceVarsHelperTests.cs`（新增探测用例）、`CP6.Tests/Wf/ServiceTaskValidatorTests.cs`（新增下标→E-WF-016）

- [ ] **Step 1: 写失败测试**

  a. `ServiceVarsHelperTests.cs` 追加：

```csharp
    [Fact]
    public void ContainsUnsupportedSubscript_DetectsArrayIndex()
    {
        Assert.True(ServiceVarsHelper.ContainsUnsupportedSubscript("$.items[0]"));
        Assert.True(ServiceVarsHelper.ContainsUnsupportedSubscript("/o/{lines[2]}"));
        Assert.False(ServiceVarsHelper.ContainsUnsupportedSubscript("$.orderId"));
        Assert.False(ServiceVarsHelper.ContainsUnsupportedSubscript("/o/{orderId}"));
        // 字面 JSON 数组值（非模板下标）不误报：
        Assert.False(ServiceVarsHelper.ContainsUnsupportedSubscript("{\"list\":[1,2,3]}"));
    }
```

  b. `ServiceTaskValidatorTests.cs` 追加（脚手架仿该文件既有用例：构造含 serviceTask 的 `FlowSchema` 调 `FlowSchemaValidator.Validate`）：

```csharp
    [Fact]
    public void WebApi_PathWithArraySubscript_E_WF_016()
    {
        var schema = new FlowSchema {
            Nodes = {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.WebApi,
                    ServiceConnectorName = "erpEcho", ServicePath = "/o/{lines[0]}" },   // 下标非法
                new FlowNode { Id = "e", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "e" } },
        };
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(schema));
    }
```

  > 若 `ServiceTaskValidatorTests.cs` 里已有构造 `FlowSchema` 的 helper（如 `Node(...)`/`Edge(...)`），复用之，别自造重复脚手架——先读该文件顶部。

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "ServiceVarsHelperTests|ServiceTaskValidatorTests"`。

- [ ] **Step 3: 实现**

  a. `ServiceVarsHelper.cs` 类级 XML 文档（`:29-31` 的 `<summary>`）追加限制说明，并新增探测方法（放在 `MergeOutputVars` 之后、`ResolveDotPath` 之前）：

```csharp
    /// <summary>
    /// 探测模板 token 是否含**不支持**的数组下标语法（`[...]`）。点路径求值（<see cref="ResolveValue"/>）
    /// 仅支持嵌套对象的逐段导航（`$.a.b`）——**不支持**数组下标（`$.items[0]`），也**无法**表达含点的键名
    /// （`{"a.b":1}` 与嵌套 `a.b` 二义，按嵌套解析）。这两类由设计期校验拦截（<c>FlowSchemaValidator</c>），
    /// 运行期遇到则静默求值为空串。本方法只对 `$.`/`{...}` 模板 token 内的 `[`/`]` 报真，避开字面 JSON 数组值。
    /// </summary>
    public static bool ContainsUnsupportedSubscript(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // $.path[...]  ——  $. 后跟标识符/点，直到出现下标括号
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\$\.[A-Za-z0-9_.]*[\[\]]")) return true;
        // {placeholder[...]}  ——  花括号占位内出现下标括号
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\{[A-Za-z0-9_.]*[\[\]][^}]*\}")) return true;
        return false;
    }
```

  b. `FlowSchemaValidator.cs` serviceTask 分支（`:85-95`）在 `bad` 判定里追加下标检查。把 `:88-93` 的 `bool bad = ...` 表达式尾部加一项：

```csharp
            var kind = (n.ServiceKind ?? string.Empty).Trim();
            bool bad =
                !KnownServiceKinds.Contains(kind)
                || (kind == ServiceKind.DataWriteback && string.IsNullOrWhiteSpace(n.ServiceActionName))
                || (kind == ServiceKind.WebApi && (string.IsNullOrWhiteSpace(n.ServiceConnectorName) || string.IsNullOrWhiteSpace(n.ServicePath)))
                || (kind == ServiceKind.Timer && (string.IsNullOrWhiteSpace(n.ServiceDelayMode) || string.IsNullOrWhiteSpace(n.ServiceDelayValue)))
                || ServiceVarsHelper.ContainsUnsupportedSubscript(n.ServicePath)         // 票4：路径模板不得含数组下标
                || ServiceVarsHelper.ContainsUnsupportedSubscript(n.ServiceParamsJson)   // 票4：参数模板不得含数组下标
                || !schema.Edges.Any(e => e.From == n.Id && e.IsError != true);   // P2-3：无非错误出边
            if (bad) { errs.Add("E-WF-016"); break; }
```

- [ ] **Step 4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "ServiceVarsHelperTests|ServiceTaskValidatorTests"`。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "fix(wfs-service-task): T4 ServiceVarsHelper 点路径限制文档化 + 设计期拦数组下标模板（E-WF-016）"
```

---

## Task T5: FlowSchemaValidator 补 ServiceMode 值域校验（sync|async）

> **票5。** 缺陷：spec §6.1 明列「`ServiceMode ∈ {sync,async}`（timer 规整为 async）」，但 `FlowSchemaValidator` 的 serviceTask 分支（`:85-95`）只校验 `ServiceKind`，**从不校验 `ServiceMode`**——用户把 `serviceMode` 手填成非法值（如 `"batch"`）能通过保存，运行期 `ServiceTaskNodeHandler` 的 mode 解析按未知值走默认，行为不可预期。修法=值域检查一行 + 一测。`ServiceMode` 常量在 `WfStatus.cs:65-69`（`Sync="sync"`/`Async="async"`）。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs:10-11`（加 `KnownServiceModes` 集合）、`:85-95`（分支加 mode 校验）
- Test: `CP6.Tests/Wf/ServiceTaskValidatorTests.cs`（新增 mode 非法/合法各一）

- [ ] **Step 1: 写失败测试** — `ServiceTaskValidatorTests.cs` 追加（复用该文件既有 FlowSchema 构造脚手架）：

```csharp
    [Fact]
    public void ServiceMode_Invalid_E_WF_016()
    {
        var schema = new FlowSchema {
            Nodes = {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.DataWriteback,
                    ServiceActionName = "sampleWriteback", ServiceMode = "batch" },   // 非法 mode
                new FlowNode { Id = "e", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "e" } },
        };
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(schema));
    }

    [Fact]
    public void ServiceMode_SyncOrAsync_Or_Null_Passes()
    {
        foreach (var mode in new string?[] { null, "sync", "async" })
        {
            var schema = new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "s", Type = "start" },
                    new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.DataWriteback,
                        ServiceActionName = "sampleWriteback", ServiceMode = mode },
                    new FlowNode { Id = "e", Type = "end" },
                },
                Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "e" } },
            };
            Assert.DoesNotContain("E-WF-016", FlowSchemaValidator.Validate(schema));
        }
    }
```

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceTaskValidatorTests`（`ServiceMode_Invalid_E_WF_016` FAIL）。

- [ ] **Step 3: 实现**

  a. `FlowSchemaValidator.cs:10-11` 之后加常量集合：

```csharp
    // 服务任务合法 mode（spec §6.1；timer 由 handler 规整为 async，此处只校验用户显式填值）。
    // 用序数比较对齐运行期语义（ServiceMode 常量为小写 "sync"/"async"）。
    private static readonly HashSet<string> KnownServiceModes =
        new(new[] { ServiceMode.Sync, ServiceMode.Async }, StringComparer.Ordinal);
```

  b. serviceTask 分支 `bool bad = ...`（`:88-93`）追加一项（放在 kind 检查之后）：

```csharp
                || (!string.IsNullOrWhiteSpace(n.ServiceMode) && !KnownServiceModes.Contains(n.ServiceMode.Trim()))  // 票5：ServiceMode 值域
```

  > 注意：`ServiceMode` 可为 null（不填=按 kind 默认），故仅在**非空**时校验值域。

- [ ] **Step 4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceTaskValidatorTests`。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "fix(wfs-service-task): T5 FlowSchemaValidator 补 ServiceMode 值域校验（sync|async → E-WF-016）"
```

---

## Task T6: E-WF-018 异步路径错误码结构化（去自由文本，仅结构化码 + 机读明细）

> **票6。** 缺陷：异步执行路径的 E-WF-018 错误把**本地化中文散文**拼进 `Error` 串——`WfServiceJobService.cs:117` `Fail($"E-WF-018 动作/连接器未注册:{key}")`、`WebApiExecutor.cs:35/40/44` 同款（`"E-WF-018 ActionRefJson 为空…"` 等）。这些串落进 `Wf_ServiceJob.LastError` 与错误路由，前端拿到无法按码 i18n（i18n seed 里 `E-WF-018` 是一个可翻译键），且中文散文与真实 detail（连接器名）混在一起不可解析。修法=统一为**结构化格式 `E-WF-018|<机读明细>`**（管道前=可翻译码，管道后=机读明细 token，无本地化散文）。前端/i18n 取 `|` 前的码翻译，`|` 后作诊断明细。

**Files:**
- Modify: `CP6.Core/Services/Wf/WfServiceJobService.cs:117`
- Modify: `CP6.Core/Services/Wf/Executors/WebApiExecutor.cs:34-44`
- Test: `CP6.Tests/Wf/WebApiExecutorTests.cs`（断言结构化格式，无中文散文）

- [ ] **Step 1: 写失败测试** — `WebApiExecutorTests.cs` 追加（该类已有 `FakeConn` 脚手架，仿之）：

```csharp
    [Fact]
    public async Task UnknownConnector_Fails_WithStructuredCode_NoProse()
    {
        // actionRef 引用未注册连接器 → 结构化 "E-WF-018|<connectorName>"，无中文散文
        var node = new FlowNode { Id = "n", Type = "serviceTask", ServiceKind = ServiceKind.WebApi,
            ServiceConnectorName = "ghost", ServicePath = "/x" };
        var ctx = new ServiceTaskContext {
            InstanceId = System.Guid.NewGuid(), TokenId = System.Guid.NewGuid(), NodeId = "n",
            StarterId = System.Guid.Empty, JobId = System.Guid.NewGuid(), AttemptNo = 1,
            ActorId = System.Guid.Empty, NowUtc = System.DateTime.UtcNow,
            ActionRefJson = ServiceTaskActionRef.Snapshot(node),
        };
        var exec = new CP6.Core.Services.Wf.Executors.WebApiExecutor(System.Array.Empty<IWfConnector>());
        var r = await exec.ExecuteAsync(ctx);

        Assert.False(r.Success);
        Assert.StartsWith("E-WF-018", r.Error);        // 码在最前
        Assert.Contains("|", r.Error);                 // 结构化分隔
        Assert.Contains("ghost", r.Error!);            // 机读明细=连接器名
        Assert.DoesNotContain("未注册", r.Error!);      // 无本地化中文散文
        Assert.DoesNotContain("连接器", r.Error!);
    }

    [Fact]
    public async Task EmptyActionRef_Fails_WithStructuredCode()
    {
        var ctx = new ServiceTaskContext {
            InstanceId = System.Guid.NewGuid(), TokenId = System.Guid.NewGuid(), NodeId = "n",
            StarterId = System.Guid.Empty, JobId = System.Guid.NewGuid(), AttemptNo = 1,
            ActorId = System.Guid.Empty, NowUtc = System.DateTime.UtcNow, ActionRefJson = null,
        };
        var r = await new CP6.Core.Services.Wf.Executors.WebApiExecutor(System.Array.Empty<IWfConnector>()).ExecuteAsync(ctx);
        Assert.False(r.Success);
        Assert.StartsWith("E-WF-018|", r.Error);
        Assert.DoesNotContain("为空", r.Error!);
    }
```

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter WebApiExecutorTests`。

- [ ] **Step 3: 实现**（约定：`Error = "E-WF-018|<detail>"`，detail 为无空格机读 token）

  a. `WebApiExecutor.cs:34-44`——把三处 `Fail("E-WF-018 …中文…")` 改为结构化：

```csharp
        if (string.IsNullOrEmpty(ctx.ActionRefJson))
            return ServiceTaskResult.Fail("E-WF-018|actionRefEmpty");

        ServiceTaskActionRef r;
        try { r = ServiceTaskActionRef.Parse(ctx.ActionRefJson); }
        catch (System.Exception ex)
        { return ServiceTaskResult.Fail($"E-WF-018|parseError:{ex.GetType().Name}"); }

        var connectorName = r.ConnectorName;
        if (string.IsNullOrEmpty(connectorName) || !_connectors.TryGetValue(connectorName, out var connector))
            return ServiceTaskResult.Fail($"E-WF-018|{connectorName}");
```

  b. `WfServiceJobService.cs:117`——把 `Fail($"E-WF-018 动作/连接器未注册:{key}")` 改为：

```csharp
                    result = ServiceTaskResult.Fail($"E-WF-018|{key}");
```

  > 幂等/退避/路由逻辑不变；`LastError` 现存的是结构化码而非散文，前端可按 `Error.Split('|')[0]` 取码 i18n。

- [ ] **Step 4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter WebApiExecutorTests`。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "fix(wfs-service-task): T6 E-WF-018 异步路径改结构化码 E-WF-018|detail（去本地化散文，前端可按码翻译）"
```

---

## Task T7: 服务目录（service-catalog）加载失败的重试边界过窄 → 加显式重试

> **票7。** 缺陷：`NodePropertyPanel.vue:86-99` 用 `watch(isServiceTask, immediate)` + `catalogLoaded` 标记拉服务目录；失败时把 `catalogLoaded=false` 允许「下次重试」——但**该 watch 只在 `isServiceTask` 由 false→true 跳变时再触发**。当用户停在 serviceTask 节点（`isServiceTask` 恒为 true）时目录加载失败，动作/连接器下拉将**永久空白**，除非切到别的节点再切回。重试边界过窄。修法=(1) 把加载抽成 `loadCatalog()`；(2) 目录为空且已加载失败时，在下拉旁露一个「重试」链接（用户主动重拉）；(3) 保留原 `watch(immediate)` 首拉。新增 i18n 键 `oa.designer.svc.reloadCatalog`（五语）。

**Files:**
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue:81-99`（抽 `loadCatalog` + 暴露重试）、template 服务任务段加重试链接
- Modify: `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`（加 `oa.designer.svc.reloadCatalog` 键，五语）

- [ ] **Step 1: 实现——脚本区抽 `loadCatalog` + 失败态** — `NodePropertyPanel.vue:81-99` 替换为：

```typescript
// ── 服务目录（C-T3）：serviceTask 节点的动作/连接器下拉数据源 ────────
const catalog = ref<ServiceCatalog>({ actions: [], connectors: [] })
const catalogLoaded = ref(false)
const catalogFailed = ref(false)   // 票7：加载失败态，驱动模板露「重试」

async function loadCatalog() {
  catalogFailed.value = false
  try {
    catalog.value = await designerApi.getServiceCatalog()
    catalogLoaded.value = true
  } catch {
    catalogFailed.value = true      // HTTP interceptor 已 toast；此处标失败让用户可主动重试
  }
}

// 首拉：进入 serviceTask 节点时若未成功加载过，拉一次（组件被 Vue 复用无 :key，onMounted 只跑一次不可靠，
// 故用 watch(immediate)）。票7：失败后不再依赖 isServiceTask 跳变——模板提供显式「重试」入口调 loadCatalog。
watch(
  isServiceTask,
  (v) => { if (v && !catalogLoaded.value) void loadCatalog() },
  { immediate: true },
)
```

- [ ] **Step 2: 实现——template 服务任务段加重试链接** — 在服务任务段的「服务类型」下拉之后（`NodePropertyPanel.vue:371` `</el-form-item>` 之后）插入失败重试提示：

```vue
          <!-- 票7：目录加载失败时露显式重试（否则停在 serviceTask 节点将永久空下拉）-->
          <el-alert
            v-if="catalogFailed"
            type="warning"
            :closable="false"
            show-icon
            style="margin-bottom: 8px"
          >
            <template #title>
              <el-button link type="primary" size="small" @click="loadCatalog">
                {{ t('oa.designer.svc.reloadCatalog') }}
              </el-button>
            </template>
          </el-alert>
```

- [ ] **Step 3: 加 i18n 键** — `I18nOaServiceTaskScreenSeed.cs` 在「前端校验消息」段（`:50` 那条之前或之后）加：

```csharp
        new() { LangKey = "oa.designer.svc.reloadCatalog",     ZhCN = "重新加载服务目录", ZhTW = "重新載入服務目錄", En = "Reload service catalog", Ja = "サービスカタログを再読み込み", Ko = "서비스 카탈로그 다시 불러오기" },
```

  > 该 seed 已 `.Concat` 进 `Program.cs` i18n 链（E-T2 完成），无需再改 Program.cs；运行期 SeedLangs 幂等去重。

- [ ] **Step 4: 验证** — 前端类型检查 + 构建（组件改动无独立 vitest，靠 type-check/build 兜）：
```bash
cd cp6.web
NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
npm run build
```
  预期：type-check 无 TS 错、build 成功。

- [ ] **Step 5: commit**
```bash
git add -A && git commit -m "fix(wfs-service-task): T7 服务目录加载失败露显式重试（修复停在 serviceTask 节点时下拉永久空白）"
```

---

## Task T8: 定时器（timer）到点动作补 webApi 连接器/路径变体 UI（spec §5.3 缺口）

> **票8。** 缺陷：spec §5.3 明列 timer「可选动作（无 / 回写动作 / **webApi 连接器**）」，运行期 `ServiceTaskActionRef.Snapshot`（`:65-73`）也支持 timer + `ConnectorName` → `actionKind="webApi"`（到点外呼）。但 `NodePropertyPanel.vue` 的 timer 分支（`:442-469`）**只提供「到点动作」下拉（=`serviceActionName`，dataWriteback 动作）**，没有连接器/路径入口——「定时到点发一个 webApi」在设计器**无法配置**。更棘手：`serviceKind` 切换清理 watch（`:56-68`）在 `kind !== 'webApi'` 时**清空 `serviceConnectorName/servicePath`**——若 timer 分支直接加连接器字段，会被这个 watch 立刻清掉。修法=(1) timer 分支加「到点动作类型」选择（none / dataWriteback / webApi），据选择显示 动作下拉 或 连接器+路径；(2) 重写清理 watch，使 timer 分支保留其合法字段、只清跨类残留。

**Files:**
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue:48-68`（清理 watch 重写）、`:442-469`（timer 分支补变体 UI）
- Modify: `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`（加 timer 动作类型三选项键，五语）

- [ ] **Step 1: 加 i18n 键** — `I18nOaServiceTaskScreenSeed.cs` 在 timer 段（`:39` `svc.timerAction` 之后）加：

```csharp
        new() { LangKey = "oa.designer.svc.timerActionKind",       ZhCN = "到点动作类型",   ZhTW = "到點動作類型",   En = "On-Fire Action Type", Ja = "発火時アクション種別", Ko = "실행 시 액션 유형" },
        new() { LangKey = "oa.designer.svc.timerActionKind.none",  ZhCN = "无（纯等待）",   ZhTW = "無（純等待）",   En = "None (pure wait)",    Ja = "なし（待機のみ）",     Ko = "없음(대기만)" },
        new() { LangKey = "oa.designer.svc.timerActionKind.write", ZhCN = "数据回写动作",   ZhTW = "資料回寫動作",   En = "Data-writeback action", Ja = "データ書き戻しアクション", Ko = "데이터 기록 액션" },
        new() { LangKey = "oa.designer.svc.timerActionKind.api",   ZhCN = "接口调用",       ZhTW = "介面呼叫",       En = "API call",            Ja = "API呼び出し",          Ko = "API 호출" },
```

- [ ] **Step 2: 重写清理 watch** — `NodePropertyPanel.vue:56-68` 的 `watch(() => local.value.serviceKind, ...)` 替换为（区分 timer：timer 合法保留 connector/path **和** actionName，二者由「到点动作类型」互斥控制，切走 timer 或 kind 才清）：

```typescript
watch(
  () => local.value.serviceKind,
  (kind) => {
    if (syncing.value) return
    if (local.value.type !== 'serviceTask') return
    // dataWriteback：无连接器/路径/到点。webApi：无到点动作。timer：connector/path/action 均可能合法
    //（由「到点动作类型」互斥控制，见 timerActionKind），故切到 timer 时不清，切离 timer 才由目标 kind 规则清。
    if (kind === 'dataWriteback') {
      local.value.serviceConnectorName = undefined
      local.value.servicePath = undefined
    } else if (kind === 'webApi') {
      local.value.serviceActionName = undefined
    }
    // kind === 'timer'：不在此清理；由 timerActionKind 切换负责清非选中变体（见下）。
  },
)

// 票8：timer「到点动作类型」——从当前已填字段派生，切换时清非选中变体的残留（防 Snapshot 优先级误外呼）。
const timerActionKind = computed<'none' | 'write' | 'api'>({
  get: () => local.value.serviceConnectorName ? 'api'
           : local.value.serviceActionName ? 'write'
           : 'none',
  set: (v) => {
    if (v === 'api') {
      local.value.serviceActionName = undefined            // 互斥：webApi 变体清回写动作
    } else if (v === 'write') {
      local.value.serviceConnectorName = undefined         // 互斥：回写变体清连接器/路径
      local.value.servicePath = undefined
    } else {
      local.value.serviceConnectorName = undefined         // none：全清
      local.value.servicePath = undefined
      local.value.serviceActionName = undefined
    }
  },
})
```

  > `computed` 已在 `:2` import。`Snapshot` 的优先级（timer + ConnectorName 优先判 webApi，见 `ServiceTaskActionRef.cs:65-73`）要求：选 write/none 时必须清空 `serviceConnectorName`，否则到点会静默外呼——上面的 setter 已保证。

- [ ] **Step 3: timer 分支补变体 UI** — `NodePropertyPanel.vue:442-469` 的 timer `<template>` 内，把原「到点动作」下拉（`:459-468`）替换为「类型选择 + 按类型渲染」：

```vue
          <!-- 定时器：延时模式 / 延时值 / 到点动作（none | 回写 | webApi 变体，票8）-->
          <template v-else-if="local.serviceKind === 'timer'">
            <el-form-item :label="t('oa.designer.svc.delayMode')">
              <el-radio-group v-model="local.serviceDelayMode">
                <el-radio value="duration">{{ t('oa.designer.svc.delayMode.duration') }}</el-radio>
                <el-radio value="untilDate">{{ t('oa.designer.svc.delayMode.untilDate') }}</el-radio>
                <el-radio value="untilExpr">{{ t('oa.designer.svc.delayMode.untilExpr') }}</el-radio>
              </el-radio-group>
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.delayValue')">
              <el-input
                v-model="local.serviceDelayValue"
                :placeholder="t('oa.designer.svc.delayValueHint')"
                clearable
              />
            </el-form-item>

            <!-- 到点动作类型（互斥） -->
            <el-form-item :label="t('oa.designer.svc.timerActionKind')">
              <el-select v-model="timerActionKind" style="width: 100%">
                <el-option value="none"  :label="t('oa.designer.svc.timerActionKind.none')" />
                <el-option value="write" :label="t('oa.designer.svc.timerActionKind.write')" />
                <el-option value="api"   :label="t('oa.designer.svc.timerActionKind.api')" />
              </el-select>
            </el-form-item>

            <!-- 回写变体：动作下拉 -->
            <el-form-item v-if="timerActionKind === 'write'" :label="t('oa.designer.svc.timerAction')">
              <el-select v-model="local.serviceActionName" style="width: 100%" clearable>
                <el-option
                  v-for="a in catalog.actions"
                  :key="a.name"
                  :value="a.name"
                  :label="a.label || a.name"
                />
              </el-select>
            </el-form-item>

            <!-- webApi 变体：连接器 + 路径（票8 补齐 spec §5.3 缺口） -->
            <template v-else-if="timerActionKind === 'api'">
              <el-form-item :label="t('oa.designer.svc.connector')">
                <el-select v-model="local.serviceConnectorName" style="width: 100%" clearable>
                  <el-option
                    v-for="c in catalog.connectors"
                    :key="c.name"
                    :value="c.name"
                    :label="c.label || c.name"
                  />
                </el-select>
              </el-form-item>
              <el-form-item :label="t('oa.designer.svc.path')">
                <el-input
                  v-model="local.servicePath"
                  :placeholder="t('oa.designer.svc.pathHint')"
                  clearable
                />
              </el-form-item>
            </template>
          </template>
```

- [ ] **Step 4: 验证**
```bash
cd cp6.web
NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
npm run build
```
  预期：type-check/build 全绿。

- [ ] **Step 5: commit**
```bash
git add -A && git commit -m "fix(wfs-service-task): T8 定时器到点动作补 webApi 连接器/路径变体 UI + 互斥清理（补 spec §5.3 缺口，防 Snapshot 误外呼）"
```

---

## Task T9: 错误边（IsError）画布视觉区分（danger 虚线，Design System token）

> **票9。** 缺陷：错误边（`FlowEdge.IsError`）在 Vue Flow 画布上与普通边**零视觉区分**——`schemaToGraph`（`designerModel.ts:84-90`）把 `isError` 只塞进 `edge.data`，不设 `style/class`，渲染成默认灰边。用户在画布上无从辨认哪条是失败边。修法=在 `schemaToGraph` 建边时，`isError===true` 的边加 danger 色虚线 `style`（用 `var(--cp-danger)`，token 已定义于 `tokens.css:14` `#E5484D`；**禁硬编码色**）。属性面板切换复选后经 `graphToSchema→父→schemaToGraph` 重建，样式随之刷新。

**Files:**
- Modify: `cp6.web/src/views/oa/designer/designerModel.ts:84-90`（`schemaToGraph` 建边加条件 style）
- Test: `cp6.web/src/views/oa/designer/designerModel.serviceTask.spec.ts`（新增：isError 边带 danger style）

- [ ] **Step 1: 写失败 vitest** — `designerModel.serviceTask.spec.ts` 追加：

```typescript
import { schemaToGraph } from './designerModel'

describe('error edge visual', () => {
  it('isError edge gets danger dashed style; normal edge does not', () => {
    const g = schemaToGraph({
      nodes: [
        { id: 'svc', type: 'serviceTask' } as any,
        { id: 'end', type: 'end' } as any,
        { id: 'h', type: 'approval' } as any,
      ],
      edges: [
        { from: 'svc', to: 'end' },              // 普通边
        { from: 'svc', to: 'h', isError: true }, // 失败边
      ],
    } as any)

    const normal = g.edges.find(e => e.target === 'end')!
    const err = g.edges.find(e => e.target === 'h')!

    // 普通边无自定义 stroke；失败边用 danger token 虚线
    expect((err.style as any)?.stroke).toBe('var(--cp-danger)')
    expect((err.style as any)?.strokeDasharray).toBeTruthy()
    expect((normal as any).style?.stroke).toBeUndefined()
    // data.isError round-trip 不受影响
    expect((err.data as any)?.isError).toBe(true)
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npx vitest run src/views/oa/designer/designerModel.serviceTask.spec.ts`。

- [ ] **Step 3: 实现** — `designerModel.ts:84-90` 的 `edges` map 替换为：

```typescript
  const edges: VFEdge[] = (schema.edges ?? []).map(e => ({
    id: `${e.from}__${e.to}`,
    source: e.from,
    target: e.to,
    data: { condition: e.condition, ccUsers: e.ccUsers, isError: e.isError },
    label: e.condition || undefined,
    // 票9：失败边（IsError）用 danger 虚线视觉区分。颜色走 Design System token（禁硬编码色）。
    ...(e.isError === true
      ? { style: { stroke: 'var(--cp-danger)', strokeWidth: 2, strokeDasharray: '6 4' }, class: 'edge-error', animated: false }
      : {}),
  }))
```

  > `graphToSchema`（`:113-118`）只读 `data.isError`，不读 `style`——round-trip 无损，样式纯呈现层。

- [ ] **Step 4: 跑验证 PASS + type-check**
```bash
cd cp6.web
npx vitest run src/views/oa/designer/designerModel.serviceTask.spec.ts
NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
```

- [ ] **Step 5: commit**
```bash
git add -A && git commit -m "fix(wfs-service-task): T9 错误边画布视觉区分（danger 虚线，Design System token）"
```

---

## Task T10: 韩文（Ko）译文润色（live QA 记录的别扭词条）

> **票10。** live QA 记录 `I18nOaServiceTaskScreenSeed.cs` 的部分 Ko 词条别扭（Konglish 直译 / 词义偏差）。修法=按上下文润色下列词条的 `Ko` 字段（**只改 Ko，其他四语不动**）。下表为定案替换；执行前对照 live QA 记录确认无遗漏（若 QA 记录了本表未列的词条，一并按同风格润色）。

| LangKey（行号） | 现 Ko | 改为 Ko | 理由 |
|---|---|---|---|
| `oa.designer.svc.title`（:13） | 서비스 태스크 | 서비스 작업 | 「태스크」Konglish 音译，「작업」为地道韩文「任务/作业」 |
| `oa.designer.svc.kind.dataWriteback`（:15） | 데이터 기록 | 데이터 쓰기 | 「기록」=记录，偏离「回写」；「쓰기」=写入，贴 writeback |
| `oa.designer.svc.action`（:20） | 액션 | 동작 | 「액션」音译，属性标签用地道「동작」 |
| `oa.designer.svc.timerAction`（:39） | 실행 시 액션 | 실행 시 동작 | 同上，去 Konglish「액션」 |
| `oa.designer.svc.errorEdge`（:46） | 실패 엣지 | 실패 분기 | 「엣지」音译 edge；流程图语境「분기」（分支）更达意 |
| `oa.designer.svc.errorEdgeHint`（:47） | …이 엣지로 진행됩니다 | …이 분기로 진행됩니다 | 与上「분기」一致 |

**Files:**
- Modify: `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`（上表 6 处的 `Ko =` 字段）

- [ ] **Step 1: 逐条改 Ko 字段** — 按上表精确替换。示例（`:13`）：

```csharp
        new() { LangKey = "oa.designer.svc.title",              ZhCN = "服务任务",       ZhTW = "服務任務",       En = "Service Task",   Ja = "サービスタスク",   Ko = "서비스 작업" },
```

  （`:15`）：

```csharp
        new() { LangKey = "oa.designer.svc.kind.dataWriteback", ZhCN = "数据回写",       ZhTW = "資料回寫",       En = "Data Writeback", Ja = "データ書き戻し",   Ko = "데이터 쓰기" },
```

  其余 4 处（`:20`/`:39`/`:46`/`:47`）同法只改 `Ko =`，ZhCN/ZhTW/En/Ja 保持不动。

- [ ] **Step 2: 编译验证**（seed 是静态数据，靠编译 + 键唯一性兜；无逻辑测试）：
```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
```
  预期：编译成功。（键未变、仅值变，运行期 SeedLangs 覆盖式幂等，无重复键风险。）

- [ ] **Step 3: commit**
```bash
git add -A && git commit -m "fix(wfs-service-task): T10 韩文译文润色（去 Konglish 音译，服务任务面板 6 词条）"
```

---

## Task T11: SignalR 通知 hub 被 CSRF 中间件 403 拦截 → 放行 hub 路径

> **票11。** 缺陷：`CsrfMiddleware`（`:25-41`）对所有非安全方法（POST/PUT/PATCH/DELETE）校验双提交 token，仅豁免 `/api/auth/login`（`:31`）。SignalR 连接的 **negotiate 是 POST**（`/hubs/notify/negotiate`），浏览器 SignalR 客户端（`cp6.web/src/utils/signalr.ts:18` `.withUrl('/hubs/notify')`）默认不带 `X-CSRF-Token` 头 → negotiate 收 403（`E-SEC-010`）→ 实时通知（`NotificationBell`/dashboard 推送）连不上。hub 路由注册于 `Program.cs:2520-2522`（`/hubs/notify`、`/hubs/mes`、`/hubs/wms`）。
>
> **本 Task 允许诊断分支**：先按 Step A 现场诊断确认 403 来源，再据结果走 **B1（豁免 hub 路径，推荐）** 或 **B2（negotiate 携带 CSRF token）**。两方案均在下方写实；执行时二选一。

**Files:**
- （诊断）无
- （B1）Modify: `CP6.WebApi/Middleware/CsrfMiddleware.cs:29-38`
- （B1）Test: `CP6.Tests/Wf/CsrfHubExemptionTests.cs`（或 `CP6.Tests` 现有中间件测试目录，执行时 Glob `**/*Csrf*Tests.cs` 确认；无则新建于 `CP6.Tests/Wf/`）
- （B2）Modify: `cp6.web/src/utils/signalr.ts:15-24`

### Step A — 现场诊断（确认 403 来自 CSRF、且发生在 negotiate）

- [ ] **A1** 起后端 + 前端（隔离库；QA 登录 admin/123456）。浏览器开发者工具 Network 过滤 `negotiate`，观察 `/hubs/notify/negotiate` 请求：
  - 若状态 **403** 且响应体含 `E-SEC-010` → CSRF 拦截确认，进 B1 或 B2。
  - 若 403 但非 `E-SEC-010`（如 401 认证）→ 非本票范畴，另查认证。
  - 若 `Security:Csrf:Enabled=false`（开发默认可能关）→ 在开启 CSRF 的环境（QA/生产配置）复现后再修。
- [ ] **A2** 确认 negotiate 是唯一被拦的请求（WS upgrade 是 GET=安全方法，不被 CSRF 拦）。据此定：**放行 hub 路径的 negotiate 即足够**。

### Step B1 —（推荐）CsrfMiddleware 豁免 hub 路径

> 理由：hub negotiate 不改服务端业务状态（仅协商传输），且 hub 自身经 JWT/cookie 认证；放行 negotiate 安全。复用既有 `PathMatches` 段边界匹配，避免 `/hubs/notifyxxx` 误豁免。

- [ ] **B1-1: 写失败测试** — `CsrfHubExemptionTests.cs`：

```csharp
using CP6.WebApi.Middleware;
using Xunit;

public class CsrfHubExemptionTests
{
    [Theory]
    [InlineData("/hubs/notify", true)]
    [InlineData("/hubs/notify/negotiate", true)]
    [InlineData("/hubs/mes/negotiate", true)]
    [InlineData("/hubs/wms", true)]
    [InlineData("/api/oa/designer/save", false)]   // 业务写请求仍受 CSRF 约束
    [InlineData("/hubsxxx/notify", false)]          // 段边界：非 /hubs/ 前缀不豁免
    public void HubPaths_AreExempt(string path, bool expectExempt)
        => Assert.Equal(expectExempt, CsrfMiddleware.IsExempt(path));
}
```

- [ ] **B1-2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter CsrfHubExemptionTests`（`IsExempt` 不存在）。

- [ ] **B1-3: 实现** — `CsrfMiddleware.cs:29-38` 把内联豁免判断抽成可测的 `IsExempt`，并加 `/hubs` 前缀：

```csharp
        if (_enabled)
        {
            var path = ctx.Request.Path.Value ?? "";
            if (!IsExempt(path) && UnsafeMethods.Contains(ctx.Request.Method.ToUpperInvariant()))
            {
                var cookie = ctx.Request.Cookies[AuthCookieWriter.CsrfCookie];
                var header = ctx.Request.Headers["X-CSRF-Token"].ToString();
                if (string.IsNullOrEmpty(cookie) || cookie != header)
                    throw new BizException("E-SEC-010", 403);   // 403 Forbidden：CSRF 校验失败（spec §5.3）
            }
        }
        await _next(ctx);
    }

    /// <summary>CSRF 豁免路径（段边界匹配，杜绝同前缀误豁免）：
    /// ① 登录端点（登录时尚无 csrf cookie）；② SignalR hub 路径（negotiate 是 POST 但不改业务状态，
    /// hub 自身经 JWT/cookie 认证；票11：否则实时通知 negotiate 被 403 拦）。</summary>
    internal static bool IsExempt(string path)
        => PathMatches(path, "/api/auth/login")
           || PathMatches(path, "/hubs");
```

  > 保留原 `PathMatches`（`:44-46`）。`/hubs` 前缀经 `PathMatches` 段边界匹配覆盖 `/hubs`、`/hubs/notify`、`/hubs/mes/negotiate` 等，但不误豁免 `/hubsxxx`。

- [ ] **B1-4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter CsrfHubExemptionTests`。

- [ ] **B1-5: 现场复验** — 重起后端，浏览器确认 `/hubs/notify/negotiate` 返回 200、SignalR `[SignalR] Connected`、通知角标实时更新。

- [ ] **B1-6: 编译 + 全量 CSRF 相关闸 + commit**
```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Csrf
git add -A && git commit -m "fix(wfs-service-task): T11 CsrfMiddleware 豁免 /hubs 路径（修复 SignalR negotiate 被 CSRF 403 拦截）"
```

### Step B2 —（备选）SignalR negotiate 携带 CSRF token

> 仅当团队要求 hub 也走 CSRF（不豁免）时选此。`@microsoft/signalr` 浏览器客户端的 `headers` 选项作用于 negotiate 的 XHR（WS upgrade 是 GET 不需）。从非 httpOnly 的 `cp6_csrf` cookie 读值注入头。

- [ ] **B2-1: 实现** — `cp6.web/src/utils/signalr.ts:15-24` 的 `getConnection` 改为读 csrf cookie 注入 negotiate 头：

```typescript
function readCookie(name: string): string {
  const m = document.cookie.match(new RegExp('(?:^|; )' + name.replace(/([.$?*|{}()[\]\\/+^])/g, '\\$1') + '=([^;]*)'))
  return m ? decodeURIComponent(m[1]!) : ''
}

export function getConnection(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      // 票11-B2：negotiate 是 POST，须带 X-CSRF-Token 头过 CsrfMiddleware 双提交校验（cp6_csrf 非 httpOnly，可读）。
      .withUrl('/hubs/notify', { headers: { 'X-CSRF-Token': readCookie('cp6_csrf') } })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()
  }
  return connection
}
```

- [ ] **B2-2: 验证** — `cd cp6.web && NODE_OPTIONS=--max-old-space-size=8192 npm run type-check && npm run build`；浏览器复验 negotiate 200 + Connected。
- [ ] **B2-3: commit** — `git commit -m "fix(wfs-service-task): T11 SignalR negotiate 携带 X-CSRF-Token 头（过 CSRF 双提交校验）"`

> **注：** `cp6_csrf` cookie 名以 `AuthCookieWriter.CsrfCookie` 常量为权威（执行时 Grep 确认字面值，勿硬猜）。若两 hub（mes/wms）也报同问题，B1 已一并覆盖；B2 需在 `mesHub.ts`/`wmsHub.ts` 同法各加。

---

## DoD / 验收

逐票完成后跑全量闸，全绿方可交付：

- [ ] **后端全量：** `dotnet test CP6.Tests/CP6.Tests.csproj` — 1509 测试全绿（5 skip=SQLite 既知），含本计划新增：`Reaper_ClaimedButNeverExecuted_DoesNotBurnAttempt`、`WfConnectorLeaseGuardTests`、`ContainsUnsupportedSubscript_*`、`WebApi_PathWithArraySubscript_E_WF_016`、`ServiceMode_*`、`UnknownConnector_Fails_WithStructuredCode_NoProse`、`CsrfHubExemptionTests`。
- [ ] **后端 Wf 闸：** `dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf` 全绿（`Reaper_ResetsExpiredLease_Only` 断言已由 T2 更新为 `AttemptCount==1`）。
- [ ] **EF clean：** `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` — 报无 pending（本计划零实体改动）。
- [ ] **前端：**
```bash
cd cp6.web
npm run test                                               # vitest 320 全绿（+ designerModel.serviceTask.spec.ts 新增 error edge 用例）
NODE_OPTIONS=--max-old-space-size=8192 npm run type-check  # 无 TS 错
npm run build                                              # 构建成功
```
- [ ] **零硬编码色：** `git diff <base>..HEAD -- 'cp6.web/**'` 无十六进制颜色字面量（T9 用 `var(--cp-danger)`）。
- [ ] **零跨模块污染：** `git diff --stat <base>..HEAD` 无 `views/space`、`*Space*`、Space 迁移文件。
- [ ] **i18n 五语齐全：** 新增键 `oa.designer.svc.reloadCatalog`（T7）、`oa.designer.svc.timerActionKind[.none|.write|.api]`（T8）五语齐全；T10 Ko 润色只动 Ko。
- [ ] **T11 现场复验：** SignalR `/hubs/notify/negotiate` 返回 200，浏览器控制台 `[SignalR] Connected`，通知角标实时更新。

### 执行顺序（建议；11 票互不依赖，可并行）

后端纯逻辑先行（T2/T4/T5/T6 同属 `CP6.Core/Services/Wf`，注意避免同文件并发编辑：T4/T5 均改 `FlowSchemaValidator.cs`，T2/T6 均改 `WfServiceJobService.cs`——同文件的票串行做）→ T1（Program.cs 配置）→ T3（连接器护栏，改 IWfConnector + Program.cs）→ 前端 T7/T8/T9（T7/T8 同改 `NodePropertyPanel.vue` 与 seed，串行）→ T10（seed Ko）→ T11（CSRF，独立）。收尾跑 DoD 全量闸。

> **同文件冲突提示：** `FlowSchemaValidator.cs`（T4+T5）、`WfServiceJobService.cs`（T2+T6）、`NodePropertyPanel.vue`（T7+T8）、`I18nOaServiceTaskScreenSeed.cs`（T7+T8+T10）各被多票触碰——这些票**必须串行**（一票 commit 后再起下一票），不可并发子代理同时改。
>
> **T4/T5 互保提示：** 两票都改 `FlowSchemaValidator.cs` 的同一个 `bool bad = ...` 表达式，且各自代码块只展示了"本票视角"的最终形态——**后跑的票不可整块照抄**，必须在当前文件实际内容上**追加自己那一行**并保留先跑票已加的行（T4=两行 `ContainsUnsupportedSubscript`，T5=一行 `KnownServiceModes`）。

---

## 波①完成记录(2026-07-12/13,fable 终审 Ready=Yes)

11 票全过逐票审查+fable 全支终审;T8 经 Critical 修复轮(票面自带纯 computed 缺陷→backing ref);终审两 Important 已修(8aae00b:CSRF 豁免注释真实论据化+timerActionKind 组 Ko 对齐 T10 风格);后端 1826→1843 绿/前端 390→401 绿/EF clean。

**跟踪票(终审记档):**
1. T3 护栏 fail-open:IWfConnector.MaxCallDuration 默认 null=放行,未来连接器不声明即绕过——对非 demo 连接器要求显式声明或启动期对 null 打 warning。
2. claim 环不闸 MaxAttempts(pre-existing):持续崩溃于 executor 期间的 job 无限重投且 AttemptCount 可见超 MaxAttempts——claim 时对 AttemptCount>=MaxAttempts 直接失败路由。
3. ServiceMode「未填」判定口径统一:校验层 Trim+IsNullOrWhiteSpace vs 运行期不 trim 且 ?? 只认 null(" sync "/"" 静默按 async)+前端 clearable 产出——三层对齐。
4. /hubs 前瞻守卫:反射测试「hub 类不得声明非 Subscribe 语义公有方法」,防未来写方法静默继承 CSRF 豁免。
5. 同步路径错误码统一票:ServiceTaskNodeHandler 同源中文散文+:75 裸 ex.Message(连 E-WF-018 码都无)。
6. T7 UX 润色候选:目录重试钮无防连点(票面样例同缺);reloadCatalog 键组注释语义。
7. T8 stale-api 残留(fail-safe 已裁定可接受):外部清字段(撤销)后 ref 残留旧模式,如未来有 undo 信号可同步。

**部署硬步骤**:双镜像重建(前后端都动)+**每已存在租户库跑一次 `docs/seeds/wfs-svc-ko-i18n-fix.sql`**(SeedLangs insert-only,改常量对已部署库不生效——T10 纠错);T1 生效后「publish 删 Local.json」绕行可退役(保留亦无害)。
