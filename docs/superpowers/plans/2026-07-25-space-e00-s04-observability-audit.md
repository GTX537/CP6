# Space E00-S04 Observability and Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为全部 Space API、发布链路和后台任务建立失败关闭的 Tenant/Actor 边界、统一执行上下文、可按 CorrelationId 查询的脱敏审计账本，以及可关闭的查询和指标出口。

**Architecture:** 在认证之后通过 Space 专用中间件建立不可变执行上下文；高风险 MVC Action Filter 在业务动作前后追加审计事件。审计账本落在现有 `CP6Context`，Space 发布事件在 `T_IntegrationEvent` 保存稳定 JobId/PublishAttemptId，重试 Worker 从持久化事件恢复上下文并产生新的 RunId/TraceId。

**Tech Stack:** .NET 8、ASP.NET Core Middleware/MVC Filters、Entity Framework Core 8、SQL Server、xUnit、Moq、prometheus-net、Vue 3、TypeScript、Vitest。

---

## 0. 执行约束与文件映射

实施目录固定为：

```text
D:\CP6\tmp\worktrees\space-e00-inventory
```

不得修改：

```text
D:\CP6
D:\CP6\tmp\worktrees\space-volume1
```

当前分支包含累计且未提交的 E00-S01～S03 变更。下面每个 Task 都保留 commit 检查点，但在用户明确授权累计 E00 暂存/提交之前，不执行 `git add` 或 `git commit`；未获授权时把该步骤标记为“跳过（未授权）”，继续测试和实施。

开始实施前重新确认旁路工作区基线：

```text
D:\CP6                         normal=256, all=368
D:\CP6\tmp\worktrees\space-volume1  normal=19, all=79
```

结束时用同一统计命令复核，数值必须不因本任务变化。

### 0.1 新建文件

| 文件 | 单一职责 |
|---|---|
| `CP6.Core/Services/Space/Observability/ISpaceExecutionContext.cs` | 执行上下文只读契约和管理边界 |
| `CP6.Core/Services/Space/Observability/SpaceExecutionContext.cs` | 不可变上下文值对象 |
| `CP6.Core/Services/Space/Observability/SpaceExecutionContextAccessor.cs` | AsyncLocal 作用域、派生和冲突保护 |
| `CP6.Core/Services/Space/Observability/SpaceErrorSanitizer.cs` | 稳定错误分类和无正文指纹 |
| `CP6.Core/Services/Space/Observability/SpaceObservabilityOptions.cs` | 查询、指标开关 |
| `CP6.Core/Services/Space/Observability/SpaceAuditContracts.cs` | 审计写入事实、证据白名单和 Writer 契约 |
| `CP6.Core/Services/Space/Observability/SpaceAuditDbContextFactory.cs` | 独立短生命周期 CP6Context |
| `CP6.Core/Services/Space/Observability/SpaceAuditWriter.cs` | 只追加、脱敏审计写入 |
| `CP6.Core/Services/Space/Observability/ISpaceAuditQueryService.cs` | 审计和安全事件查询契约 |
| `CP6.Core/Services/Space/Observability/SpaceAuditQueryService.cs` | 租户内分页、时间线和安全投影 |
| `CP6.Core/Services/Space/Observability/ISpaceAuditMetricsSnapshotProvider.cs` | 指标快照契约 |
| `CP6.Core/Services/Space/Observability/SpaceAuditMetricsSnapshotProvider.cs` | 跨租户、无租户标签的账本聚合 |
| `CP6.Entity/DomainModels/Space/Space_AuditEvent.cs` | append-only 审计实体 |
| `CP6.Entity/DTOs/Space/SpaceAuditDtos.cs` | 查询、时间线和指标 DTO |
| `CP6.WebApi/Middleware/SpaceExecutionContextMiddleware.cs` | `/api/space` 身份、租户、Correlation/Trace 边界 |
| `CP6.WebApi/Filters/SpaceAuditActionFilter.cs` | Space 变更端点前置/结果审计 |
| `CP6.WebApi/Controllers/Space/SpaceAuditController.cs` | `space:audit:read` 查询 API |
| `CP6.WebApi/Observability/SpaceAuditMetricsCollector.cs` | Prometheus gauge 注册 |
| `CP6.WebApi/Seed/SpaceAuditPermissionSeed.cs` | MenuId 906 的 `space-audit:read` 逐租户种子 |
| `CP6.Tests/Space/SpaceExecutionContextTests.cs` | 上下文作用域和派生单测 |
| `CP6.Tests/Space/SpaceErrorSanitizerTests.cs` | 错误正文不泄漏单测 |
| `CP6.Tests/Space/SpaceExecutionContextMiddlewareTests.cs` | HTTP 边界单测 |
| `CP6.Tests/Space/SpaceAuditLedgerTests.cs` | 模型、租户过滤和 append-only 单测 |
| `CP6.Tests/Space/SpaceAuditWriterTests.cs` | Writer 脱敏和失败语义单测 |
| `CP6.Tests/Space/SpaceAuditActionFilterTests.cs` | 高风险 Action 前后审计单测 |
| `CP6.Tests/Space/SpaceAuditQueryServiceTests.cs` | 查询窗口、租户和安全 DTO 单测 |
| `CP6.Tests/Space/SpaceAuditPermissionSeedTests.cs` | 权限种子幂等单测 |
| `CP6.Tests/Space/SpaceAuditMetricsSnapshotProviderTests.cs` | 跨租户指标聚合单测 |
| `CP6.Tests/Space/SpaceBinReconciliationWorkerTests.cs` | 巡检 System Actor 和摘要审计单测 |
| `CP6.Tests/Space/SpaceObservabilityChainTests.cs` | HTTP→Adapter→Outbox→Job→Audit 验收链路 |
| `docs/space/reports/e00-s04-observability-audit.md` | 实施结果、回滚和验证报告 |

### 0.2 修改文件

| 文件 | 修改目的 |
|---|---|
| `CP6.Entity/DomainModels/Integration/IntegrationEvent.cs` | 增加 JobId、PublishAttemptId |
| `CP6.Core/EFDbContext/CP6Context.cs` | DbSet、索引、append-only 守卫 |
| `CP6.Core/Services/Space/LocationPublishService.cs` | 复用上下文 CorrelationId，生成 PublishAttemptId |
| `CP6.Core/Services/Integration/BridgeHookBase.cs` | 持久化链路字段；Space 持久化失败日志脱敏 |
| `CP6.Core/Services/Integration/SpaceBridgeHook.cs` | JobId、PublishAttemptId 和安全错误码 |
| `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs` | 重试使用 `evt.CorrelationId` |
| `CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs` | 恢复 System 上下文、审计每次尝试 |
| `CP6.WebApi/BackgroundServices/SpaceBinReconciliationWorker.cs` | System 上下文、汇总审计、无明细正文日志 |
| `CP6.WebApi/Controllers/Space/LocationPublishController.cs` | 旧事件路由精确权限和安全 DTO |
| `CP6.WebApi/Filters/OperLogFilter.cs` | Space 请求禁止序列化 ActionArguments |
| `CP6.Core/Auth/RequirePermissionAttribute.cs` | 审计权限拒绝返回稳定码 |
| `CP6.WebApi/Seed/I18nSpaceScreenSeed.cs` | E00-S04 稳定错误码词条 |
| `CP6.WebApi/Program.cs` | DI、Filter、中间件、Seed、Metrics |
| `CP6.WebApi/appsettings.json` | `SpaceObservability` 开关 |
| `docs/seeds/space-roleaction-seed.sql` | SQL 对照种子加入 audit read |
| `docs/seeds/space-menu-seed-2.sql` | MenuId 906 的 MenuKey 收敛为 `space-audit` |
| `CP6.Tests/LocationPublishServiceTests.cs` | 发布上下文测试装配和断言 |
| `CP6.Tests/SpaceBridgeHookTests.cs` | Job/PublishAttempt/脱敏断言 |
| `CP6.Tests/IntegrationEventDispatcherTests.cs` | 重试 CorrelationId 断言 |
| `CP6.Tests/IntegrationEventRetryWorkerTests.cs` | Worker 上下文和错误脱敏断言 |
| `CP6.Tests/Space/SpacePermissionAttributeTests.cs` | 审计 GET 精确权限例外 |
| `CP6.Tests/RequirePermissionFilterTests.cs` | 稳定审计拒绝码 |
| `CP6.Tests/OperLogFilterTests.cs` | Space 操作日志正文为空 |
| `cp6.web/src/types/space/scene.ts` | 安全事件 VO |
| `cp6.web/src/views/space/lifecycle/SpaceEventsView.vue` | 移除 LastError 全文弹窗 |
| `cp6.web/src/views/space/lifecycle/__tests__/SpaceEventsView.spec.ts` | 安全字段 UI 回归 |
| `docs/seeds/space-i18n-seed-2.sql` | 事件页安全列标签 |

Migration 由 EF 命令生成以下三个路径：

```text
CP6.Core/Migrations/<timestamp>_SpaceE00S04ObservabilityAudit.cs
CP6.Core/Migrations/<timestamp>_SpaceE00S04ObservabilityAudit.Designer.cs
CP6.Core/Migrations/CP6ContextModelSnapshot.cs
```

## Task 1: 执行上下文值对象、作用域和错误脱敏

**Files:**

- Create: `CP6.Core/Services/Space/Observability/ISpaceExecutionContext.cs`
- Create: `CP6.Core/Services/Space/Observability/SpaceExecutionContext.cs`
- Create: `CP6.Core/Services/Space/Observability/SpaceExecutionContextAccessor.cs`
- Create: `CP6.Core/Services/Space/Observability/SpaceErrorSanitizer.cs`
- Test: `CP6.Tests/Space/SpaceExecutionContextTests.cs`
- Test: `CP6.Tests/Space/SpaceErrorSanitizerTests.cs`

- [ ] **Step 1: 写上下文作用域和冲突保护失败测试**

```csharp
using CP6.Core.Services.Space.Observability;

namespace CP6.Tests.Space;

public class SpaceExecutionContextTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Correlation = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Push_exposes_context_and_restores_previous_value()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var outer = SpaceExecutionContext.ForUser(Tenant, "u-1", "alice", Correlation, "trace-a");
        var inner = outer with { TraceId = "trace-b", RunId = Guid.NewGuid() };

        using (accessor.Push(outer))
        {
            Assert.Same(outer, accessor.Current);
            using (accessor.Push(inner))
                Assert.Same(inner, accessor.Current);
            Assert.Same(outer, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Enrich_keeps_identity_and_sets_optional_identifiers_once()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var attempt = Guid.NewGuid();
        var job = Guid.NewGuid();
        using var scope = accessor.Push(
            SpaceExecutionContext.ForUser(Tenant, "u-1", "alice", Correlation, "trace-a"));

        accessor.Enrich(jobId: job, publishAttemptId: attempt);

        Assert.Equal(Tenant, accessor.Current!.TenantId);
        Assert.Equal(Correlation, accessor.Current.CorrelationId);
        Assert.Equal(job, accessor.Current.JobId);
        Assert.Equal(attempt, accessor.Current.PublishAttemptId);
        Assert.Throws<InvalidOperationException>(
            () => accessor.Enrich(publishAttemptId: Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: 运行测试并确认因类型不存在而失败**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceExecutionContextTests" --no-restore
```

Expected: FAIL，编译错误指出 `CP6.Core.Services.Space.Observability` 或 `SpaceExecutionContextAccessor` 不存在。

- [ ] **Step 3: 实现不可变上下文和只读/管理双契约**

```csharp
namespace CP6.Core.Services.Space.Observability;

public interface ISpaceExecutionContext
{
    Guid CorrelationId { get; }
    string TraceId { get; }
    Guid TenantId { get; }
    string ActorType { get; }
    string ActorId { get; }
    string? ActorName { get; }
    string? OrganizationContextId { get; }
    Guid? JobId { get; }
    Guid? RunId { get; }
    Guid? PublishAttemptId { get; }
}

public interface ISpaceExecutionContextAccessor
{
    ISpaceExecutionContext? Current { get; }
    ISpaceExecutionContext RequireCurrent();
}

public interface ISpaceExecutionContextManager
{
    IDisposable Push(SpaceExecutionContext context);
    void Enrich(Guid? jobId = null, Guid? runId = null, Guid? publishAttemptId = null, string? traceId = null);
}
```

```csharp
namespace CP6.Core.Services.Space.Observability;

public sealed record SpaceExecutionContext(
    Guid CorrelationId,
    string TraceId,
    Guid TenantId,
    string ActorType,
    string ActorId,
    string? ActorName,
    string? OrganizationContextId = null,
    Guid? JobId = null,
    Guid? RunId = null,
    Guid? PublishAttemptId = null) : ISpaceExecutionContext
{
    public const string UserActor = "User";
    public const string SystemActor = "System";

    public static SpaceExecutionContext ForUser(
        Guid tenantId, string actorId, string? actorName, Guid correlationId, string traceId,
        string? organizationContextId = null)
        => Validate(new(
            correlationId, traceId, tenantId, UserActor, actorId, actorName,
            OrganizationContextId: organizationContextId));

    public static SpaceExecutionContext ForSystem(
        Guid tenantId, string actorId, Guid correlationId, string traceId,
        Guid? jobId = null, Guid? runId = null, Guid? publishAttemptId = null)
        => Validate(new(
            correlationId, traceId, tenantId, SystemActor, actorId, actorId,
            OrganizationContextId: null,
            JobId: jobId,
            RunId: runId,
            PublishAttemptId: publishAttemptId));

    private static SpaceExecutionContext Validate(SpaceExecutionContext value)
    {
        if (value.TenantId == Guid.Empty) throw new ArgumentException("TenantId is required");
        if (value.CorrelationId == Guid.Empty) throw new ArgumentException("CorrelationId is required");
        if (string.IsNullOrWhiteSpace(value.TraceId)) throw new ArgumentException("TraceId is required");
        if (string.IsNullOrWhiteSpace(value.ActorId)) throw new ArgumentException("ActorId is required");
        return value;
    }
}
```

`SpaceExecutionContextAccessor`：

```csharp
namespace CP6.Core.Services.Space.Observability;

public sealed class SpaceExecutionContextAccessor :
    ISpaceExecutionContextAccessor,
    ISpaceExecutionContextManager
{
    private readonly AsyncLocal<SpaceExecutionContext?> _current = new();

    public ISpaceExecutionContext? Current => _current.Value;

    public ISpaceExecutionContext RequireCurrent()
        => _current.Value
           ?? throw new InvalidOperationException("SPACE_EXECUTION_CONTEXT_REQUIRED");

    public IDisposable Push(SpaceExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = _current.Value;
        _current.Value = context;
        return new RestoreScope(this, previous);
    }

    public void Enrich(
        Guid? jobId = null,
        Guid? runId = null,
        Guid? publishAttemptId = null,
        string? traceId = null)
    {
        var value = _current.Value
            ?? throw new InvalidOperationException("SPACE_EXECUTION_CONTEXT_REQUIRED");
        _current.Value = value with
        {
            JobId = Merge(value.JobId, jobId),
            RunId = Merge(value.RunId, runId),
            PublishAttemptId = Merge(value.PublishAttemptId, publishAttemptId),
            TraceId = Merge(value.TraceId, traceId),
        };
    }

    private static T? Merge<T>(T? current, T? incoming) where T : struct
    {
        if (incoming is null) return current;
        if (current is null || EqualityComparer<T>.Default.Equals(current.Value, incoming.Value))
            return incoming;
        throw new InvalidOperationException("SPACE_EXECUTION_CONTEXT_CONFLICT");
    }

    private static string Merge(string current, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming) || current == incoming) return current;
        throw new InvalidOperationException("SPACE_EXECUTION_CONTEXT_CONFLICT");
    }

    private sealed class RestoreScope : IDisposable
    {
        private SpaceExecutionContextAccessor? _owner;
        private readonly SpaceExecutionContext? _previous;

        public RestoreScope(SpaceExecutionContextAccessor owner, SpaceExecutionContext? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null) owner._current.Value = _previous;
        }
    }
}
```

- [ ] **Step 4: 写并运行错误脱敏测试**

```csharp
using CP6.Core.Services.Space.Observability;

namespace CP6.Tests.Space;

public class SpaceErrorSanitizerTests
{
    [Fact]
    public void Classify_does_not_copy_exception_message_or_stack()
    {
        var ex = new InvalidOperationException("Bearer secret-token request-body");
        var safe = SpaceErrorSanitizer.Classify(ex, "SPACE_ADAPTER_FAILURE");
        var serialized = $"{safe.ReasonCode}|{safe.ExceptionType}|{safe.Fingerprint}";

        Assert.Equal("SPACE_ADAPTER_FAILURE", safe.ReasonCode);
        Assert.Equal(nameof(InvalidOperationException), safe.ExceptionType);
        Assert.Matches("^[A-F0-9]{64}$", safe.Fingerprint);
        Assert.DoesNotContain("secret-token", serialized);
        Assert.DoesNotContain("request-body", serialized);
    }
}
```

实现：

```csharp
using System.Security.Cryptography;
using System.Text;

namespace CP6.Core.Services.Space.Observability;

public sealed record SpaceSafeError(string ReasonCode, string ExceptionType, string Fingerprint);

public static class SpaceErrorSanitizer
{
    public static SpaceSafeError Classify(Exception exception, string reasonCode)
    {
        var type = exception.GetType().Name;
        var material = $"{exception.GetType().FullName}|{exception.HResult}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return new SpaceSafeError(reasonCode, type, hash);
    }

    public static string ToStorageCode(Exception exception, string reasonCode)
    {
        var safe = Classify(exception, reasonCode);
        return $"{safe.ReasonCode}:{safe.ExceptionType}:{safe.Fingerprint}";
    }
}
```

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceExecutionContextTests|FullyQualifiedName~SpaceErrorSanitizerTests" --no-restore
```

Expected: PASS，3 个测试通过。

- [ ] **Step 5: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.Core/Services/Space/Observability/ISpaceExecutionContext.cs CP6.Core/Services/Space/Observability/SpaceExecutionContext.cs CP6.Core/Services/Space/Observability/SpaceExecutionContextAccessor.cs CP6.Core/Services/Space/Observability/SpaceErrorSanitizer.cs CP6.Tests/Space/SpaceExecutionContextTests.cs CP6.Tests/Space/SpaceErrorSanitizerTests.cs
git commit -m "feat(space): add execution context primitives"
```

## Task 2: Space HTTP 失败关闭边界

**Files:**

- Create: `CP6.WebApi/Middleware/SpaceExecutionContextMiddleware.cs`
- Modify: `CP6.WebApi/Program.cs:293-301`
- Modify: `CP6.WebApi/Program.cs:2721-2729`
- Test: `CP6.Tests/Space/SpaceExecutionContextMiddlewareTests.cs`

- [ ] **Step 1: 写认证、Tenant、Actor、外部主体和 Header 失败测试**

测试直接构造 `DefaultHttpContext`，不启动 TestServer。共用 helper 必须给有效身份填入：

```csharp
private static DefaultHttpContext Context(
    string path = "/api/space/site",
    bool authenticated = true,
    string? tenant = "11111111-1111-1111-1111-111111111111",
    string? actor = "22222222-2222-2222-2222-222222222222",
    params Claim[] extra)
{
    var claims = new List<Claim>();
    if (tenant is not null) claims.Add(new("tenant_id", tenant));
    if (actor is not null) claims.Add(new(ClaimTypes.NameIdentifier, actor));
    claims.Add(new(ClaimTypes.Name, "alice"));
    claims.AddRange(extra);
    var identity = authenticated
        ? new ClaimsIdentity(claims, "TestAuth")
        : new ClaimsIdentity(claims);
    return new DefaultHttpContext
    {
        User = new ClaimsPrincipal(identity),
        Request = { Path = path, Method = HttpMethods.Get }
    };
}
```

测试用例：

```csharp
[Theory]
[InlineData(false, "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "SPACE_AUTHENTICATION_REQUIRED", 401)]
[InlineData(true, null, "22222222-2222-2222-2222-222222222222", "SPACE_TENANT_CONTEXT_REQUIRED", 403)]
[InlineData(true, "bad", "22222222-2222-2222-2222-222222222222", "SPACE_TENANT_CONTEXT_REQUIRED", 403)]
[InlineData(true, "11111111-1111-1111-1111-111111111111", null, "SPACE_ACTOR_CONTEXT_REQUIRED", 403)]
public async Task Invalid_boundary_fails_closed(
    bool authenticated, string? tenant, string? actor, string code, int status)
{
    var context = Context(authenticated: authenticated, tenant: tenant, actor: actor);
    var middleware = MakeMiddleware(_ => Task.CompletedTask);
    var error = await Assert.ThrowsAsync<BizException>(() => Invoke(middleware, context));
    Assert.Equal(code, error.Code);
    Assert.Equal(status, error.HttpStatus);
    Assert.False(string.IsNullOrWhiteSpace(context.Response.Headers["X-Correlation-ID"]));
}

[Fact]
public async Task External_subject_is_denied_even_with_identity()
{
    var context = Context(extra: new[] { new Claim("subject_type", "external") });
    var error = await Assert.ThrowsAsync<BizException>(
        () => Invoke(MakeMiddleware(_ => Task.CompletedTask), context));
    Assert.Equal("SPACE_EXTERNAL_SUBJECT_DENIED", error.Code);
}

[Fact]
public async Task Organization_context_requires_explicit_internal_subject()
{
    var denied = Context(extra: new[] { new Claim("organization_context_id", "org-1") });
    var error = await Assert.ThrowsAsync<BizException>(
        () => Invoke(MakeMiddleware(_ => Task.CompletedTask), denied));
    Assert.Equal("SPACE_EXTERNAL_SUBJECT_DENIED", error.Code);

    var allowed = Context(extra: new[]
    {
        new Claim("organization_context_id", "org-1"),
        new Claim("subject_type", "internal"),
    });
    await Invoke(MakeMiddleware(_ => Task.CompletedTask), allowed);
}

[Theory]
[InlineData("not-a-guid")]
[InlineData("00000000-0000-0000-0000-000000000000")]
public async Task Invalid_inbound_correlation_returns_safe_generated_id(string value)
{
    var context = Context();
    context.Request.Headers["X-Correlation-ID"] = value;
    var error = await Assert.ThrowsAsync<BizException>(
        () => Invoke(MakeMiddleware(_ => Task.CompletedTask), context));
    Assert.Equal("SPACE_CORRELATION_ID_INVALID", error.Code);
    Assert.Equal(400, error.HttpStatus);
    Assert.True(Guid.TryParse(context.Response.Headers["X-Correlation-ID"], out var generated));
    Assert.NotEqual(Guid.Empty, generated);
}

[Fact]
public async Task Valid_inbound_correlation_is_propagated()
{
    var expected = Guid.NewGuid();
    var context = Context();
    context.Request.Headers["X-Correlation-ID"] = expected.ToString();
    ISpaceExecutionContext? seen = null;
    var accessor = new SpaceExecutionContextAccessor();
    var tenant = new TenantContext
    {
        CurrentTenantId = Guid.Parse(context.User.FindFirstValue("tenant_id")!)
    };
    var middleware = new SpaceExecutionContextMiddleware(
        _ =>
        {
            seen = accessor.Current;
            return Task.CompletedTask;
        },
        NullLogger<SpaceExecutionContextMiddleware>.Instance);

    await middleware.InvokeAsync(context, tenant, accessor);

    Assert.Equal(expected.ToString(), context.Response.Headers["X-Correlation-ID"]);
    Assert.False(string.IsNullOrWhiteSpace(context.Response.Headers["X-Trace-ID"]));
    Assert.Equal(expected, seen!.CorrelationId);
    Assert.Null(accessor.Current);
}
```

失败用例的完整调用 helper：

```csharp
private static SpaceExecutionContextMiddleware MakeMiddleware(RequestDelegate next)
    => new(next, NullLogger<SpaceExecutionContextMiddleware>.Instance);

private static Task Invoke(SpaceExecutionContextMiddleware middleware, DefaultHttpContext context)
{
    var tenant = new TenantContext();
    var claim = context.User.FindFirstValue("tenant_id");
    if (Guid.TryParse(claim, out var parsed) && parsed != Guid.Empty)
        tenant.CurrentTenantId = parsed;
    var accessor = new SpaceExecutionContextAccessor();
    return middleware.InvokeAsync(context, tenant, accessor);
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceExecutionContextMiddlewareTests" --no-restore
```

Expected: FAIL，`SpaceExecutionContextMiddleware` 不存在。

- [ ] **Step 3: 实现中间件**

实现：

```csharp
public async Task InvokeAsync(
    HttpContext context,
    ITenantContext tenantContext,
    ISpaceExecutionContextManager manager)
{
    if (!context.Request.Path.StartsWithSegments("/api/space"))
    {
        await _next(context);
        return;
    }

    var (correlationId, invalidCorrelation) = ReadCorrelation(context.Request.Headers["X-Correlation-ID"]);
    context.Response.Headers["X-Correlation-ID"] = correlationId.ToString();
    if (invalidCorrelation)
        throw new BizException("SPACE_CORRELATION_ID_INVALID", 400);

    using var ownedActivity = Activity.Current is null ? StartActivity("Space.Http") : null;
    var traceId = Activity.Current?.TraceId.ToHexString()
        ?? throw new BizException("SPACE_TRACE_CONTEXT_REQUIRED", 500);
    context.Response.Headers["X-Trace-ID"] = traceId;

    if (context.User.Identity?.IsAuthenticated != true)
        throw new BizException("SPACE_AUTHENTICATION_REQUIRED", 401);

    var tenantClaim = context.User.FindFirstValue("tenant_id");
    if (!Guid.TryParse(tenantClaim, out var tenantId)
        || tenantId == Guid.Empty
        || tenantContext.CurrentTenantId != tenantId)
        throw new BizException("SPACE_TENANT_CONTEXT_REQUIRED", 403);

    var actorClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(actorClaim, out var actorId) || actorId == Guid.Empty)
        throw new BizException("SPACE_ACTOR_CONTEXT_REQUIRED", 403);

    var subjectType = context.User.FindFirstValue("subject_type");
    var organization = context.User.FindFirstValue("organization_context_id");
    var external = string.Equals(subjectType, "external", StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(organization)
            && !string.Equals(subjectType, "internal", StringComparison.OrdinalIgnoreCase));
    if (external)
        throw new BizException("SPACE_EXTERNAL_SUBJECT_DENIED", 403);

    var snapshot = SpaceExecutionContext.ForUser(
        tenantId,
        actorId.ToString(),
        context.User.FindFirstValue(ClaimTypes.Name),
        correlationId,
        traceId,
        organization);

    using var execution = manager.Push(snapshot);
    using var logScope = _logger.BeginScope(new Dictionary<string, object?>
    {
        ["TenantId"] = snapshot.TenantId,
        ["ActorType"] = snapshot.ActorType,
        ["ActorId"] = snapshot.ActorId,
        ["CorrelationId"] = snapshot.CorrelationId,
        ["TraceId"] = snapshot.TraceId,
    });
    await _next(context);
}
```

辅助函数：

```csharp
private static (Guid CorrelationId, bool Invalid) ReadCorrelation(StringValues values)
{
    if (values.Count == 0) return (Guid.NewGuid(), false);
    if (values.Count == 1
        && Guid.TryParse(values[0], out var parsed)
        && parsed != Guid.Empty)
        return (parsed, false);
    return (Guid.NewGuid(), true);
}

private static Activity StartActivity(string name)
    => new Activity(name).SetIdFormat(ActivityIdFormat.W3C).Start();
```

中间件构造器：

```csharp
public SpaceExecutionContextMiddleware(
    RequestDelegate next,
    ILogger<SpaceExecutionContextMiddleware> logger)
{
    _next = next;
    _logger = logger;
}
```

缺失 Header 时生成新 GUID；多值、空 GUID、非法字符串均用服务端新 GUID 写响应 Header 后抛 400。中间件仅停止自己创建的 Activity。

- [ ] **Step 4: 注册 DI 和正确中间件顺序**

在 `Program.cs` 的 Tenant/权限服务注册附近加入：

```csharp
builder.Services.AddScoped<SpaceExecutionContextAccessor>();
builder.Services.AddScoped<ISpaceExecutionContextAccessor>(
    sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
builder.Services.AddScoped<ISpaceExecutionContextManager>(
    sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
```

在 `BizExceptionMiddleware` 之后、CSRF 和授权之前加入：

```csharp
app.UseMiddleware<CP6.WebApi.Middleware.BizExceptionMiddleware>();
app.UseMiddleware<CP6.WebApi.Middleware.SpaceExecutionContextMiddleware>();
app.UseMiddleware<CP6.WebApi.Middleware.CsrfMiddleware>();
app.UseMiddleware<CP6.WebApi.Middleware.MustChangePasswordMiddleware>();
app.UseAuthorization();
```

这样 Space 中间件抛出的 `BizException` 会进入现有本地化 envelope，且上下文覆盖授权过滤器和 endpoint。

- [ ] **Step 5: 运行边界与既有安全中间件测试**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceExecutionContextMiddlewareTests|FullyQualifiedName~SecurityMiddlewareTests" --no-restore
```

Expected: PASS；Space 边界测试和既有 CSRF/改密测试均通过。

- [ ] **Step 6: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.WebApi/Middleware/SpaceExecutionContextMiddleware.cs CP6.WebApi/Program.cs CP6.Tests/Space/SpaceExecutionContextMiddlewareTests.cs
git commit -m "feat(space): fail closed at the HTTP context boundary"
```

## Task 3: 审计实体、IntegrationEvent 链路列和 Migration

**Files:**

- Create: `CP6.Entity/DomainModels/Space/Space_AuditEvent.cs`
- Modify: `CP6.Entity/DomainModels/Integration/IntegrationEvent.cs:1-65`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs:160-164`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs:2028-2038`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs:2287-2347`
- Create: `CP6.Core/Migrations/<timestamp>_SpaceE00S04ObservabilityAudit.cs`
- Create: `CP6.Core/Migrations/<timestamp>_SpaceE00S04ObservabilityAudit.Designer.cs`
- Modify: `CP6.Core/Migrations/CP6ContextModelSnapshot.cs`
- Test: `CP6.Tests/Space/SpaceAuditLedgerTests.cs`

- [ ] **Step 1: 写模型、租户过滤和 append-only 失败测试**

```csharp
[Fact]
public async Task Audit_row_is_tenant_scoped_and_uses_utc_timestamp()
{
    var tenant = new TenantContext { CurrentTenantId = TenantA };
    await using var db = NewDb(tenant);
    db.SpaceAuditEvents.Add(NewEvent(Guid.Empty, DateTime.UtcNow));
    await db.SaveChangesAsync();

    var row = await db.SpaceAuditEvents.SingleAsync();
    Assert.Equal(TenantA, row.TenantId);
    Assert.Equal(DateTimeKind.Utc, row.OccurredAtUtc.Kind);
}

[Theory]
[InlineData(EntityState.Modified)]
[InlineData(EntityState.Deleted)]
public async Task Audit_rows_reject_mutation(EntityState state)
{
    var tenant = new TenantContext { CurrentTenantId = TenantA };
    await using var db = NewDb(tenant);
    var row = NewEvent(TenantA, DateTime.UtcNow);
    db.SpaceAuditEvents.Add(row);
    await db.SaveChangesAsync();
    db.Entry(row).State = state;

    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    Assert.Equal("SPACE_AUDIT_APPEND_ONLY", error.Message);
}
```

- [ ] **Step 2: 运行测试并确认模型不存在**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceAuditLedgerTests" --no-restore
```

Expected: FAIL，`Space_AuditEvent` 或 `SpaceAuditEvents` 不存在。

- [ ] **Step 3: 创建审计实体并扩展 IntegrationEvent**

`Space_AuditEvent` 完整属性：

```csharp
[Table("Space_AuditEvent")]
public sealed class Space_AuditEvent : BaseTenantEntity
{
    public DateTime OccurredAtUtc { get; set; }
    [Required, MaxLength(16)] public string ActorType { get; set; } = "";
    [Required, MaxLength(100)] public string ActorId { get; set; } = "";
    [MaxLength(100)] public string? ActorName { get; set; }
    [MaxLength(100)] public string? OrganizationContextId { get; set; }
    [Required, MaxLength(100)] public string Action { get; set; } = "";
    [Required, MaxLength(64)] public string ResourceType { get; set; } = "";
    [MaxLength(128)] public string? ResourceId { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? VersionId { get; set; }
    public Guid? FloorId { get; set; }
    [Required, MaxLength(16)] public string Outcome { get; set; } = "";
    [MaxLength(100)] public string? ReasonCode { get; set; }
    public string? AuthorizationEvidenceJson { get; set; }
    [Column(TypeName = "char(64)")] public string? BeforeHash { get; set; }
    [Column(TypeName = "char(64)")] public string? AfterHash { get; set; }
    public Guid CorrelationId { get; set; }
    [Required, MaxLength(64)] public string TraceId { get; set; } = "";
    public Guid? JobId { get; set; }
    public Guid? RunId { get; set; }
    public Guid? PublishAttemptId { get; set; }
    public int? AttemptNo { get; set; }
    [MaxLength(32)] public string? ClientType { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }
    [MaxLength(256)] public string? UserAgent { get; set; }
}
```

在 `IntegrationEvent` 增加：

```csharp
public Guid? JobId { get; set; }
public Guid? PublishAttemptId { get; set; }
```

- [ ] **Step 4: 配置 DbSet、索引和值约束**

在 `CP6Context` 增加：

```csharp
public DbSet<Space_AuditEvent> SpaceAuditEvents => Set<Space_AuditEvent>();
```

`OnModelCreating` 增加：

```csharp
modelBuilder.Entity<Space_AuditEvent>(e =>
{
    e.Property(x => x.AuthorizationEvidenceJson).HasColumnType("nvarchar(max)");
    e.Property(x => x.OccurredAtUtc).HasConversion(
        value => value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    e.HasIndex(x => new { x.TenantId, x.OccurredAtUtc });
    e.HasIndex(x => new { x.TenantId, x.CorrelationId, x.OccurredAtUtc });
    e.HasIndex(x => new { x.TenantId, x.PublishAttemptId, x.OccurredAtUtc });
    e.HasIndex(x => new { x.TenantId, x.JobId, x.RunId });
    e.HasCheckConstraint("CK_Space_AuditEvent_Tenant", "[TenantId] <> '00000000-0000-0000-0000-000000000000'");
    e.HasCheckConstraint("CK_Space_AuditEvent_Correlation", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
    e.HasCheckConstraint("CK_Space_AuditEvent_ActorType", "[ActorType] IN ('User','System')");
    e.HasCheckConstraint("CK_Space_AuditEvent_Outcome", "[Outcome] IN ('Started','Succeeded','Failed','Denied')");
});

modelBuilder.Entity<IntegrationEvent>(e =>
{
    e.HasIndex(x => new { x.TenantId, x.CorrelationId });
    e.HasIndex(x => new { x.TenantId, x.JobId });
    e.HasIndex(x => new { x.TenantId, x.PublishAttemptId });
});
```

在两个 SaveChanges 重载最前面调用：

```csharp
private void RejectSpaceAuditMutation()
{
    if (ChangeTracker.Entries<Space_AuditEvent>()
        .Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        throw new InvalidOperationException("SPACE_AUDIT_APPEND_ONLY");
}
```

`OccurredAtUtc` 由 Writer 保证 UTC；Ledger 测试 helper 必须传入 `DateTime.SpecifyKind(value, DateTimeKind.Utc)`，不得用本地时间冒充。

- [ ] **Step 5: 生成 Migration 并检查 SQL**

Run:

```powershell
dotnet ef migrations add SpaceE00S04ObservabilityAudit --project CP6.Core --startup-project CP6.WebApi --context CP6Context --output-dir Migrations
dotnet ef migrations script --project CP6.Core --startup-project CP6.WebApi --context CP6Context --idempotent --output tmp/space-e00-s04-migration.sql
```

Expected:

- Migration 只创建 `Space_AuditEvent`。
- 只向 `T_IntegrationEvent` 增加 `JobId`、`PublishAttemptId` 两个 nullable uniqueidentifier 列。
- 创建设计中的 7 个索引和 4 个 check constraint。
- 不出现 `DROP TABLE Space_AuditEvent` 之外的业务表删除；`Down` 仅回退本次新增 schema，运行时回滚策略不执行 `Down`。

- [ ] **Step 6: 运行 Ledger 测试和模型构建**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceAuditLedgerTests|FullyQualifiedName~SpacePersistenceTests" --no-restore
dotnet build CP6.slnx --no-restore
```

Expected: PASS；解决方案构建无新增警告或错误。

- [ ] **Step 7: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.Entity/DomainModels/Space/Space_AuditEvent.cs CP6.Entity/DomainModels/Integration/IntegrationEvent.cs CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/*_SpaceE00S04ObservabilityAudit.cs CP6.Core/Migrations/*_SpaceE00S04ObservabilityAudit.Designer.cs CP6.Core/Migrations/CP6ContextModelSnapshot.cs CP6.Tests/Space/SpaceAuditLedgerTests.cs
git commit -m "feat(space): add append-only audit ledger"
```

## Task 4: 审计 Writer 与高风险 Action Filter

**Files:**

- Create: `CP6.Core/Services/Space/Observability/SpaceAuditContracts.cs`
- Create: `CP6.Core/Services/Space/Observability/SpaceAuditDbContextFactory.cs`
- Create: `CP6.Core/Services/Space/Observability/SpaceAuditWriter.cs`
- Create: `CP6.WebApi/Filters/SpaceAuditActionFilter.cs`
- Modify: `CP6.WebApi/Filters/OperLogFilter.cs`
- Modify: `CP6.WebApi/Program.cs:20-28`
- Modify: `CP6.WebApi/Program.cs:293-301`
- Test: `CP6.Tests/Space/SpaceAuditWriterTests.cs`
- Test: `CP6.Tests/Space/SpaceAuditActionFilterTests.cs`
- Modify: `CP6.Tests/OperLogFilterTests.cs`

- [ ] **Step 1: 写 Writer 脱敏、独立 Context 和失败返回测试**

最小测试集合：

```csharp
[Fact]
public async Task Writer_maps_context_and_only_serializes_typed_evidence()
{
    using var execution = _manager.Push(UserContext());
    var input = new SpaceAuditEventInput(
        Action: "space.floor.publish",
        ResourceType: "Floor",
        ResourceId: FloorId.ToString(),
        Outcome: SpaceAuditOutcome.Started,
        Evidence: new SpaceAuditEvidence(
            PermissionCode: "space-publish:publish",
            AuthorizationResult: "Allowed",
            ItemCount: 3,
            Status: "Pending",
            ExceptionType: null,
            ErrorFingerprint: null));

    Assert.True(await _writer.TryAppendAsync(input));

    await using var assertDb = _factory.CreateDbContext();
    var row = await assertDb.SpaceAuditEvents.SingleAsync();
    Assert.Equal(UserContext().CorrelationId, row.CorrelationId);
    Assert.Contains("space-publish:publish", row.AuthorizationEvidenceJson);
    Assert.DoesNotContain("requestBody", row.AuthorizationEvidenceJson);
}

[Fact]
public async Task Writer_failure_returns_false_and_log_contains_no_exception_message()
{
    using var execution = _manager.Push(UserContext());
    var writer = WriterWithFactoryThatThrows(
        new InvalidOperationException("secret request body bearer-token"));

    var result = await writer.TryAppendAsync(BasicInput());

    Assert.False(result);
    Assert.DoesNotContain("secret", _capturedLog);
    Assert.DoesNotContain("bearer-token", _capturedLog);
}
```

- [ ] **Step 2: 运行 Writer 测试并确认失败**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceAuditWriterTests" --no-restore
```

Expected: FAIL，审计契约和 Writer 尚不存在。

- [ ] **Step 3: 实现类型化证据和独立 Context 工厂**

契约：

```csharp
public static class SpaceAuditOutcome
{
    public const string Started = "Started";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Denied = "Denied";
}

public sealed record SpaceAuditEvidence(
    string? PermissionCode = null,
    string? AuthorizationResult = null,
    int? ItemCount = null,
    string? Status = null,
    string? ExceptionType = null,
    string? ErrorFingerprint = null);

public sealed record SpaceAuditEventInput(
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome,
    string? ReasonCode = null,
    Guid? SiteId = null,
    Guid? VersionId = null,
    Guid? FloorId = null,
    SpaceAuditEvidence? Evidence = null,
    string? BeforeHash = null,
    string? AfterHash = null,
    int? AttemptNo = null,
    string? ClientType = null,
    string? IpAddress = null,
    string? UserAgent = null);

public interface ISpaceAuditWriter
{
    Task<bool> TryAppendAsync(SpaceAuditEventInput input, CancellationToken ct = default);
}

public interface ISpaceAuditDbContextFactory
{
    CP6Context CreateDbContext();
}
```

工厂使用当前 scope 的 `DbContextOptions<CP6Context>`、`ITenantContext`、`ICurrentUserAccessor` 新建 `CP6Context`。Writer 显式从 `ISpaceExecutionContextAccessor.RequireCurrent()` 映射 Tenant/Actor/Correlation/Trace/Job/Run/PublishAttempt；字符串分别截断到实体上限，Evidence 序列化后超过 8192 字符时改存：

```json
{"status":"EvidenceTruncated"}
```

Writer 捕获非取消异常时只记录 `ReasonCode`、异常类型和 `SpaceErrorSanitizer` 指纹，不把异常对象作为 ILogger 的首参数。

工厂实现：

```csharp
public sealed class SpaceAuditDbContextFactory : ISpaceAuditDbContextFactory
{
    private readonly DbContextOptions<CP6Context> _options;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserAccessor _user;

    public SpaceAuditDbContextFactory(
        DbContextOptions<CP6Context> options,
        ITenantContext tenant,
        ICurrentUserAccessor user)
    {
        _options = options;
        _tenant = tenant;
        _user = user;
    }

    public CP6Context CreateDbContext() => new(_options, _tenant, _user);
}
```

Writer 实现：

```csharp
public sealed class SpaceAuditWriter : ISpaceAuditWriter
{
    private readonly ISpaceAuditDbContextFactory _factory;
    private readonly ISpaceExecutionContextAccessor _execution;
    private readonly ILogger<SpaceAuditWriter> _logger;

    public SpaceAuditWriter(
        ISpaceAuditDbContextFactory factory,
        ISpaceExecutionContextAccessor execution,
        ILogger<SpaceAuditWriter> logger)
    {
        _factory = factory;
        _execution = execution;
        _logger = logger;
    }

    public async Task<bool> TryAppendAsync(
        SpaceAuditEventInput input,
        CancellationToken ct = default)
    {
        try
        {
            var context = _execution.RequireCurrent();
            var evidence = input.Evidence is null
                ? null
                : JsonSerializer.Serialize(input.Evidence);
            if (evidence?.Length > 8192)
                evidence = """{"status":"EvidenceTruncated"}""";

            await using var db = _factory.CreateDbContext();
            db.SpaceAuditEvents.Add(new Space_AuditEvent
            {
                Id = Guid.NewGuid(),
                TenantId = context.TenantId,
                OccurredAtUtc = DateTime.UtcNow,
                ActorType = Limit(context.ActorType, 16)!,
                ActorId = Limit(context.ActorId, 100)!,
                ActorName = Limit(context.ActorName, 100),
                OrganizationContextId = Limit(context.OrganizationContextId, 100),
                Action = Limit(input.Action, 100)!,
                ResourceType = Limit(input.ResourceType, 64)!,
                ResourceId = Limit(input.ResourceId, 128),
                SiteId = input.SiteId,
                VersionId = input.VersionId,
                FloorId = input.FloorId,
                Outcome = Limit(input.Outcome, 16)!,
                ReasonCode = Limit(input.ReasonCode, 100),
                AuthorizationEvidenceJson = evidence,
                BeforeHash = Limit(input.BeforeHash, 64),
                AfterHash = Limit(input.AfterHash, 64),
                CorrelationId = context.CorrelationId,
                TraceId = Limit(context.TraceId, 64)!,
                JobId = context.JobId,
                RunId = context.RunId,
                PublishAttemptId = context.PublishAttemptId,
                AttemptNo = input.AttemptNo,
                ClientType = Limit(input.ClientType, 32),
                IpAddress = Limit(input.IpAddress, 64),
                UserAgent = Limit(input.UserAgent, 256),
                Creator = Limit(context.ActorId, 100),
                CreateDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var safe = SpaceErrorSanitizer.Classify(ex, "SPACE_AUDIT_WRITE_FAILED");
            _logger.LogError(
                "Space audit append failed {ReasonCode} {ErrorType} {Fingerprint}",
                safe.ReasonCode, safe.ExceptionType, safe.Fingerprint);
            return false;
        }
    }

    private static string? Limit(string? value, int maxLength)
        => string.IsNullOrEmpty(value)
            ? value
            : value.Length <= maxLength ? value : value[..maxLength];
}
```

- [ ] **Step 4: 写高风险 Filter 失败关闭测试**

```csharp
[Fact]
public async Task Mutation_does_not_call_action_when_started_audit_fails()
{
    var writer = new StubWriter(false);
    var filter = new SpaceAuditActionFilter(writer);
    var actionCalled = false;
    var context = MakeActionContext(HttpMethods.Post, "LocationPublish", "PublishFloor");

    await filter.OnActionExecutionAsync(context, () =>
    {
        actionCalled = true;
        return Task.FromResult(MakeExecuted(context));
    });

    Assert.False(actionCalled);
    var result = Assert.IsType<ObjectResult>(context.Result);
    Assert.Equal(503, result.StatusCode);
    Assert.Equal("SPACE_AUDIT_UNAVAILABLE", Message(result));
}

[Fact]
public async Task Successful_mutation_with_result_audit_failure_becomes_outcome_unknown()
{
    var writer = new SequenceWriter(true, false);
    var filter = new SpaceAuditActionFilter(writer);
    var context = MakeActionContext(HttpMethods.Put, "LocationPublish", "Deactivate");

    var executed = await RunFilter(filter, context, new OkObjectResult(new { ok = true }));

    var result = Assert.IsType<ObjectResult>(executed.Result);
    Assert.Equal(503, result.StatusCode);
    Assert.Equal("SPACE_OPERATION_OUTCOME_UNKNOWN", Message(result));
}
```

测试 helper 必须构造真实的 Space 命名空间 Controller，使 Filter 的范围谓词被命中：

```csharp
private static ActionExecutingContext MakeActionContext(
    string method,
    string controller,
    string action)
{
    var http = new DefaultHttpContext();
    http.Request.Method = method;
    http.Request.Path = $"/api/space/{action}";
    var descriptor = new ControllerActionDescriptor
    {
        RouteValues = new Dictionary<string, string?>
        {
            ["controller"] = controller,
            ["action"] = action,
        }
    };
    var actionContext = new ActionContext(http, new RouteData(), descriptor);
    return new ActionExecutingContext(
        actionContext,
        new List<IFilterMetadata>(),
        new Dictionary<string, object?>(),
        new CP6.WebApi.Controllers.Space.AuditFilterProbeController());
}

private static async Task<ActionExecutedContext> RunFilter(
    SpaceAuditActionFilter filter,
    ActionExecutingContext context,
    IActionResult result)
{
    ActionExecutedContext? captured = null;
    await filter.OnActionExecutionAsync(context, () =>
    {
        captured = new ActionExecutedContext(
            context,
            new List<IFilterMetadata>(),
            context.Controller)
        {
            Result = result
        };
        return Task.FromResult(captured);
    });
    return captured!;
}
```

同一测试文件用块命名空间声明探针：

```csharp
namespace CP6.WebApi.Controllers.Space
{
    internal sealed class AuditFilterProbeController : ControllerBase { }
}
```

- [ ] **Step 5: 实现并全局注册 `SpaceAuditActionFilter`**

Filter 实现：

```csharp
public sealed class SpaceAuditActionFilter : IAsyncActionFilter
{
    private readonly ISpaceAuditWriter _writer;

    public SpaceAuditActionFilter(ISpaceAuditWriter writer) => _writer = writer;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        var isMutation = context.Controller.GetType().Namespace
                == "CP6.WebApi.Controllers.Space"
            && (method is "POST" or "PUT" or "PATCH" or "DELETE");
        if (!isMutation)
        {
            await next();
            return;
        }

        var controller = context.ActionDescriptor.RouteValues["controller"] ?? "Space";
        var action = context.ActionDescriptor.RouteValues["action"] ?? "Unknown";
        var resourceType = $"{controller}.{action}";
        var request = context.HttpContext.Request;

        var started = await _writer.TryAppendAsync(new SpaceAuditEventInput(
            Action: $"space.http.{method.ToLowerInvariant()}",
            ResourceType: resourceType,
            ResourceId: request.Path.Value,
            Outcome: SpaceAuditOutcome.Started,
            ClientType: "Web",
            IpAddress: context.HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent: request.Headers["User-Agent"].ToString()),
            context.HttpContext.RequestAborted);
        if (!started)
        {
            context.Result = Error(503, "SPACE_AUDIT_UNAVAILABLE");
            return;
        }

        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch (Exception ex)
        {
            var safe = SpaceErrorSanitizer.Classify(ex, "SPACE_ACTION_FAILED");
            await _writer.TryAppendAsync(new SpaceAuditEventInput(
                $"space.http.{method.ToLowerInvariant()}",
                resourceType,
                request.Path.Value,
                SpaceAuditOutcome.Failed,
                safe.ReasonCode,
                Evidence: new SpaceAuditEvidence(
                    ExceptionType: safe.ExceptionType,
                    ErrorFingerprint: safe.Fingerprint),
                ClientType: "Web"),
                context.HttpContext.RequestAborted);
            throw;
        }

        var status = StatusCodeOf(executed.Result);
        var outcome = status is 401 or 403
            ? SpaceAuditOutcome.Denied
            : executed.Exception is not null || status >= 400
                ? SpaceAuditOutcome.Failed
                : SpaceAuditOutcome.Succeeded;
        var safeError = executed.Exception is null
            ? null
            : SpaceErrorSanitizer.Classify(executed.Exception, "SPACE_ACTION_FAILED");

        var appended = await _writer.TryAppendAsync(new SpaceAuditEventInput(
            $"space.http.{method.ToLowerInvariant()}",
            resourceType,
            request.Path.Value,
            outcome,
            safeError?.ReasonCode,
            Evidence: safeError is null
                ? new SpaceAuditEvidence(Status: status.ToString())
                : new SpaceAuditEvidence(
                    Status: status.ToString(),
                    ExceptionType: safeError.ExceptionType,
                    ErrorFingerprint: safeError.Fingerprint),
            ClientType: "Web"),
            context.HttpContext.RequestAborted);

        if (!appended
            && outcome == SpaceAuditOutcome.Succeeded
            && executed.Exception is null)
            executed.Result = Error(503, "SPACE_OPERATION_OUTCOME_UNKNOWN");
    }

    private static int StatusCodeOf(IActionResult? result) => result switch
    {
        ObjectResult value => value.StatusCode ?? 200,
        StatusCodeResult value => value.StatusCode,
        _ => 200,
    };

    private static ObjectResult Error(int status, string code)
        => new(new { code = status, message = code, data = (object?)null })
        {
            StatusCode = status
        };
}
```

行为顺序：

1. 用 Action=`space.http.<method>`、ResourceType=`<Controller>.<Action>` 写 `Started`。
2. 失败时直接设置 `{ code = 503, message = "SPACE_AUDIT_UNAVAILABLE", data = null }`，不调用 `next`。
3. 调用 Action。
4. Action 抛异常或返回 4xx/5xx 时追加 `Failed`；403 使用 `Denied`。
5. 成功时追加 `Succeeded`。
6. 成功结果的最终审计失败时，把结果替换为 `503 SPACE_OPERATION_OUTCOME_UNKNOWN`。
7. Filter 不读取 `ActionArguments`，不序列化 Model，不记录请求体。

注册：

```csharp
builder.Services.AddScoped<SpaceAuditActionFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<OperLogFilter>();
    options.Filters.AddService<SpaceAuditActionFilter>();
});
builder.Services.AddScoped<ISpaceAuditDbContextFactory, SpaceAuditDbContextFactory>();
builder.Services.AddScoped<ISpaceAuditWriter, SpaceAuditWriter>();
```

- [ ] **Step 6: 运行 Writer/Filter 和控制器回归测试**

在运行前先给 `OperLogFilterTests` 增加：

```csharp
[Fact]
public async Task Space_mutation_never_serializes_action_arguments()
{
    var transport = ConnectedTransport();
    var filter = new OperLogFilter(_db, transport.Object, BuildConfig());
    var context = MakeExecutingContext(
        method: "POST",
        path: "/api/space/floor/11111111-1111-1111-1111-111111111111/publish",
        arguments: new Dictionary<string, object?>
        {
            ["request"] = new { Secret = "request-body-secret" }
        });

    await filter.OnActionExecutionAsync(context, () => SuccessfulExecution(context));

    transport.Verify(x => x.PublishAsync(It.Is<Sys_OperLog>(
        log => log.RequestBody == null)), Times.Once);
}
```

`OperLogFilter` 在序列化 ActionArguments 前计算：

```csharp
var method = context.HttpContext.Request.Method;
var path = context.HttpContext.Request.Path.Value ?? "";
var isSpace = context.HttpContext.Request.Path.StartsWithSegments("/api/space");
```

请求体条件改为：

```csharp
if (!isSpace && (method is "POST" or "PUT" or "PATCH" or "DELETE"))
```

Space 的 Kafka/DB 降级异常输出使用 `SpaceErrorSanitizer` 的异常类型和指纹；不输出 `ex.Message`。非 Space 现有行为保持不变。

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceAuditWriterTests|FullyQualifiedName~SpaceAuditActionFilterTests|FullyQualifiedName~SpacePermissionAttributeTests|FullyQualifiedName~OperLogFilterTests" --no-restore
```

Expected: PASS；Filter 未改变现有控制器权限特性，Space 的 `Sys_OperLog.RequestBody` 恒为 null。

- [ ] **Step 7: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.Core/Services/Space/Observability/SpaceAuditContracts.cs CP6.Core/Services/Space/Observability/SpaceAuditDbContextFactory.cs CP6.Core/Services/Space/Observability/SpaceAuditWriter.cs CP6.WebApi/Filters/SpaceAuditActionFilter.cs CP6.WebApi/Filters/OperLogFilter.cs CP6.WebApi/Program.cs CP6.Tests/Space/SpaceAuditWriterTests.cs CP6.Tests/Space/SpaceAuditActionFilterTests.cs CP6.Tests/OperLogFilterTests.cs
git commit -m "feat(space): fail closed on required audit writes"
```

## Task 5: 发布、Adapter 和 Outbox 标识传播

**Files:**

- Modify: `CP6.Core/Services/Space/LocationPublishService.cs:20-46`
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs:49-105`
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs:115-168`
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs:172-204`
- Modify: `CP6.Core/Services/Integration/BridgeHookBase.cs:30-90`
- Modify: `CP6.Core/Services/Integration/SpaceBridgeHook.cs:27-78`
- Modify: `CP6.Tests/LocationPublishServiceTests.cs:17-36`
- Modify: `CP6.Tests/SpaceBridgeHookTests.cs`

- [ ] **Step 1: 写发布链路稳定标识失败测试**

在 `LocationPublishServiceTests` 的 helper 中建立用户执行上下文，并增加：

```csharp
[Fact]
public async Task Publish_reuses_execution_correlation_and_persists_attempt_and_job()
{
    using var db = Db();
    var correlation = Guid.NewGuid();
    var accessor = NewAccessorWithUserContext(correlation);
    SeedPublishableFloor(db, out var floorId);
    var service = MakePublishSvc(db, execution: accessor);

    await service.PublishFloorAsync(floorId, null, "alice");

    var evt = await db.IntegrationEvents.SingleAsync();
    Assert.Equal(correlation, evt.CorrelationId);
    Assert.NotNull(evt.PublishAttemptId);
    Assert.NotNull(evt.JobId);
    Assert.Equal(evt.PublishAttemptId, accessor.Current!.PublishAttemptId);
    Assert.Equal(evt.JobId, accessor.Current.JobId);
}
```

在 `SpaceBridgeHookTests` 增加抛出含敏感正文的 Consumer：

```csharp
[Fact]
public async Task Publish_failure_persists_only_sanitized_error()
{
    using var db = Db();
    var accessor = NewAccessorWithPublishAttempt();
    var hook = new SpaceBridgeHook(
        db, NullLogger<SpaceBridgeHook>.Instance,
        new ThrowingConsumer("secret response body"), accessor, accessor);

    var result = await hook.OnLocationPublishedAsync(Batch(), accessor.Current!.CorrelationId);

    var evt = await db.IntegrationEvents.SingleAsync();
    Assert.False(result.Success);
    Assert.StartsWith("SPACE_ADAPTER_FAILURE:", evt.LastError);
    Assert.DoesNotContain("secret response body", evt.LastError);
    Assert.NotNull(evt.JobId);
    Assert.Equal(accessor.Current.PublishAttemptId, evt.PublishAttemptId);
}
```

- [ ] **Step 2: 运行定向测试并确认失败**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~LocationPublishServiceTests|FullyQualifiedName~SpaceBridgeHookTests" --no-restore
```

Expected: FAIL，新构造参数、JobId 或 PublishAttemptId 断言失败。

- [ ] **Step 3: 改造 `LocationPublishService`**

构造注入：

```csharp
private readonly ISpaceExecutionContextAccessor _execution;
private readonly ISpaceExecutionContextManager _executionManager;
```

每个会投递事件的方法在任何状态翻转或 Adapter 调用前执行：

```csharp
var context = _execution.RequireCurrent();
var publishAttemptId = Guid.NewGuid();
_executionManager.Enrich(publishAttemptId: publishAttemptId);
```

三处调用统一替换为：

```csharp
await _hook.OnLocationPublishedAsync(batch, context.CorrelationId);
```

不得从 `user`、默认租户或批号反推上下文。`AdoptAsync` 不产生 IntegrationEvent，但仍由 HTTP Filter 完成高风险审计。

- [ ] **Step 4: 扩展 Bridge 持久化并安全处理 Space 错误**

`BridgeHookBase.PersistEventAsync` 增加两个尾部可选参数：

```csharp
Guid? jobId = null,
Guid? publishAttemptId = null
```

映射：

```csharp
JobId = jobId,
PublishAttemptId = publishAttemptId,
CreateDate = sourceModule == "SPACE" ? DateTime.UtcNow : DateTime.Now,
```

持久化异常日志分支：

```csharp
if (sourceModule == "SPACE")
{
    var safe = SpaceErrorSanitizer.Classify(ex, "SPACE_OUTBOX_PERSIST_FAILED");
    Logger.LogError(
        "[BridgeHookBase] Space event persistence failed {ReasonCode} {ErrorType} {Fingerprint} {Hook} {SourceNo} {CorrelationId}",
        safe.ReasonCode, safe.ExceptionType, safe.Fingerprint, hookName, sourceNo, correlationId);
}
else
{
    Logger.LogError(ex,
        "[BridgeHookBase] IntegrationEvent persistence failed for {Hook} {SourceNo}",
        hookName, sourceNo);
}
```

`SpaceBridgeHook` 注入只读 accessor 和 manager。入口先校验：

```csharp
var context = _execution.RequireCurrent();
if (context.CorrelationId != correlationId)
    throw new InvalidOperationException("SPACE_EXECUTION_CONTEXT_CONFLICT");

Guid? jobId = context.JobId;
if (persistEvent)
{
    jobId = Guid.NewGuid();
    _executionManager.Enrich(jobId: jobId);
    context = _execution.RequireCurrent();
}
```

异常分支：

```csharp
var safe = SpaceErrorSanitizer.Classify(ex, "SPACE_ADAPTER_FAILURE");
error = $"{safe.ReasonCode}:{safe.ExceptionType}:{safe.Fingerprint}";
```

首次路径调用 `PersistEventAsync` 时传 `jobId` 和 `context.PublishAttemptId`。`persistEvent:false` 的 Worker 重试沿用 Worker 已恢复的 JobId，不生成新值。返回 `Message` 仅允许安全错误码。

- [ ] **Step 5: 更新测试装配并运行发布回归**

所有直接 new `LocationPublishService` 和 `SpaceBridgeHook` 的测试显式传入一个已 Push 用户上下文的 `SpaceExecutionContextAccessor`。不得给生产构造器增加绕过上下文的可选参数。

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~LocationPublishServiceTests|FullyQualifiedName~SpaceBridgeHookTests|FullyQualifiedName~SpaceMasterServiceTests" --no-restore
```

Expected: PASS；既有发布、停用、再发布行为保持不变，新标识断言通过。

- [ ] **Step 6: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.Core/Services/Space/LocationPublishService.cs CP6.Core/Services/Integration/BridgeHookBase.cs CP6.Core/Services/Integration/SpaceBridgeHook.cs CP6.Tests/LocationPublishServiceTests.cs CP6.Tests/SpaceBridgeHookTests.cs
git commit -m "feat(space): propagate publish identity into the outbox"
```

## Task 6: 重试 Worker 恢复上下文并保持 CorrelationId

**Files:**

- Modify: `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs:15-73`
- Modify: `CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs:49-111`
- Modify: `CP6.Tests/IntegrationEventDispatcherTests.cs`
- Modify: `CP6.Tests/IntegrationEventRetryWorkerTests.cs`

- [ ] **Step 1: 写 Dispatcher 原 CorrelationId 测试**

```csharp
[Fact]
public async Task Dispatch_space_route_reuses_persisted_correlation()
{
    var expected = Guid.NewGuid();
    var space = new Mock<ISpaceBridgeHook>();
    space.Setup(x => x.OnLocationPublishedAsync(
            It.IsAny<LocationPublishBatch>(), expected, false))
        .ReturnsAsync(new SpaceBridgeResult { Success = true });
    var dispatcher = NewDispatcher(space.Object);

    var ok = await dispatcher.DispatchAsync(new IntegrationEvent
    {
        SourceModule = "SPACE",
        TargetModule = "WMS",
        HookName = "OnLocationPublishedAsync",
        CorrelationId = expected,
        PayloadJson = """{"batchNo":"LPUB-1","items":[]}"""
    });

    Assert.True(ok);
    space.VerifyAll();
}
```

- [ ] **Step 2: 写 Worker System Actor、RunId 和脱敏错误测试**

在 Worker 测试 provider 注册 `SpaceExecutionContextAccessor` 双契约、Stub Writer，以及 scoped recording dispatcher。不要从 root provider 读取 scoped accessor；Dispatcher 在 Worker 创建的同一 tenant scope 中读取：

```csharp
private sealed class RecordingDispatcher : IIntegrationEventDispatcher
{
    private readonly ISpaceExecutionContextAccessor _accessor;
    private readonly ConcurrentBag<ISpaceExecutionContext> _seen;
    private readonly Exception? _failure;

    public RecordingDispatcher(
        ISpaceExecutionContextAccessor accessor,
        ConcurrentBag<ISpaceExecutionContext> seen,
        Exception? failure)
    {
        _accessor = accessor;
        _seen = seen;
        _failure = failure;
    }

    public Task<bool> DispatchAsync(IntegrationEvent evt, CancellationToken ct = default)
    {
        _seen.Add(_accessor.RequireCurrent());
        if (_failure is not null) throw _failure;
        return Task.FromResult(true);
    }
}
```

最终断言：

```csharp
var current = Assert.Single(seen);
Assert.Equal("System", current.ActorType);
Assert.Equal("space-worker:integration-event-retry", current.ActorId);
Assert.Equal(evt.CorrelationId, current.CorrelationId);
Assert.Equal(evt.JobId, current.JobId);
Assert.Equal(evt.PublishAttemptId, current.PublishAttemptId);
Assert.NotNull(current.RunId);
Assert.StartsWith("SPACE_ADAPTER_FAILURE:", saved.LastError);
Assert.DoesNotContain("secret adapter response", saved.LastError);
Assert.Contains(auditInputs, x => x.Outcome == SpaceAuditOutcome.Started);
Assert.Contains(auditInputs, x => x.Outcome == SpaceAuditOutcome.Failed && x.AttemptNo == saved.Attempts);
```

Provider 注册：

```csharp
services.AddSingleton(seen);
services.AddScoped<SpaceExecutionContextAccessor>();
services.AddScoped<ISpaceExecutionContextAccessor>(
    sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
services.AddScoped<ISpaceExecutionContextManager>(
    sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
services.AddScoped<IIntegrationEventDispatcher>(sp => new RecordingDispatcher(
    sp.GetRequiredService<ISpaceExecutionContextAccessor>(),
    sp.GetRequiredService<ConcurrentBag<ISpaceExecutionContext>>(),
    new InvalidOperationException("secret adapter response")));
```

- [ ] **Step 3: 运行 Dispatcher/Worker 测试并确认失败**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~IntegrationEventDispatcherTests|FullyQualifiedName~IntegrationEventRetryWorkerTests" --no-restore
```

Expected: FAIL；Dispatcher 仍传新 GUID，Worker 尚未建立 Space System 上下文。

- [ ] **Step 4: 修改 Dispatcher 使用事件关联编号**

`DispatchContext` 增加 `IntegrationEvent Event`，Space route 改为：

```csharp
var r = await ctx.Space.OnLocationPublishedAsync(
    p,
    ctx.Event.CorrelationId,
    persistEvent: false);
```

构造上下文时传入当前 `evt`。其他路由行为不变。

- [ ] **Step 5: 修改 Worker 的 Space 分支**

仅当 `evt.SourceModule == "SPACE"` 时：

1. 要求 `JobId`、`PublishAttemptId`、非空 CorrelationId 存在；旧存量 Space 事件缺字段时以 `evt.Id` 作为 JobId、生成一次 PublishAttemptId 并保存，随后保持稳定。
2. 生成新 RunId。
3. 显式启动 W3C Activity。
4. Push：

```csharp
SpaceExecutionContext.ForSystem(
    tenantId,
    "space-worker:integration-event-retry",
    evt.CorrelationId,
    Activity.Current!.TraceId.ToHexString(),
    evt.JobId,
    runId,
    evt.PublishAttemptId)
```

5. 在调用 Dispatcher 前 `TryAppendAsync(Started)`；失败时不调用 Adapter，保存 `LastError = "SPACE_AUDIT_UNAVAILABLE"` 并按现有 backoff 延后。
6. 每次成功/失败/死信追加结果事件，`AttemptNo = evt.Attempts`。
7. Space 异常使用 `SpaceErrorSanitizer.ToStorageCode(ex, "SPACE_ADAPTER_FAILURE")`；非 Space 分支保持现有行为。

结构化日志只写标识和安全码，不把异常对象、Payload 或 `LastError` 原文作为参数。

- [ ] **Step 6: 运行重试和死信回归**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~IntegrationEventDispatcherTests|FullyQualifiedName~IntegrationEventRetryWorkerTests|FullyQualifiedName~IntegrationEventRetryDeadLetterE2ETests" --no-restore
```

Expected: PASS；现有非 Space 事件重试状态机不变。

- [ ] **Step 7: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.Core/Services/Integration/IntegrationEventDispatcher.cs CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs CP6.Tests/IntegrationEventDispatcherTests.cs CP6.Tests/IntegrationEventRetryWorkerTests.cs
git commit -m "fix(space): preserve correlation across outbox retries"
```

## Task 7: 巡检 Worker 的 System 上下文和摘要审计

**Files:**

- Modify: `CP6.WebApi/BackgroundServices/SpaceBinReconciliationWorker.cs`
- Test: `CP6.Tests/Space/SpaceBinReconciliationWorkerTests.cs`

- [ ] **Step 1: 写每租户上下文和摘要审计失败测试**

测试使用 InMemory 数据库创建两个启用租户，每租户各放一条已发布 Space_Location 和 inactive WmsBin。Stub Writer 捕获输入：

```csharp
[Fact]
public async Task ProcessOnce_creates_distinct_system_context_per_tenant_and_audits_counts()
{
    await using var provider = BuildProviderWithTwoTenantsAndDrifts();
    var writer = provider.GetRequiredService<RecordingAuditWriter>();
    var worker = new SpaceBinReconciliationWorker(
        provider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<SpaceBinReconciliationWorker>.Instance);

    await worker.ProcessOnceAsync();

    Assert.Equal(2, writer.Events.Count(x => x.Action == "space.reconciliation.scan"
        && x.Outcome == SpaceAuditOutcome.Succeeded));
    Assert.All(writer.Contexts, c =>
    {
        Assert.Equal("System", c.ActorType);
        Assert.Equal("space-worker:bin-reconciliation", c.ActorId);
        Assert.NotNull(c.JobId);
        Assert.NotNull(c.RunId);
        Assert.NotEqual(Guid.Empty, c.CorrelationId);
    });
    Assert.Equal(2, writer.Contexts.Select(x => x.CorrelationId).Distinct().Count());
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceBinReconciliationWorkerTests" --no-restore
```

Expected: FAIL；Worker 尚未解析上下文管理器和 Writer。

- [ ] **Step 3: 建立巡检执行上下文并收敛日志**

每个 TenantScopeRunner body 开头：

```csharp
using var activity = new Activity("Space.BinReconciliation")
    .SetIdFormat(ActivityIdFormat.W3C)
    .Start();
var context = SpaceExecutionContext.ForSystem(
    tenantId,
    "space-worker:bin-reconciliation",
    Guid.NewGuid(),
    activity.TraceId.ToHexString(),
    jobId: Guid.NewGuid(),
    runId: Guid.NewGuid());
using var execution = manager.Push(context);
```

审计写入：

```csharp
await writer.TryAppendAsync(new SpaceAuditEventInput(
    "space.reconciliation.scan",
    "SpaceBin",
    null,
    SpaceAuditOutcome.Started,
    ClientType: "Worker"), c);

var drifts = await SpaceBinDriftScanner.ScanAsync(db, c);

await writer.TryAppendAsync(new SpaceAuditEventInput(
    "space.reconciliation.scan",
    "SpaceBin",
    null,
    SpaceAuditOutcome.Succeeded,
    Evidence: new SpaceAuditEvidence(ItemCount: drifts.Count, Status: "Completed"),
    ClientType: "Worker"), c);
```

审计失败允许巡检继续，但记录安全运维日志。删除逐条输出 `LocationCode` 的错误日志，替换为：

```csharp
_logger.LogWarning(
    "[SpaceBinDrift] tenant={TenantId} correlation={CorrelationId} driftCount={DriftCount}",
    tenantId, context.CorrelationId, drifts.Count);
```

- [ ] **Step 4: 运行 Worker 和租户作用域测试**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceBinReconciliationWorkerTests|FullyQualifiedName~TenantScopeRunnerTests|FullyQualifiedName~SpaceBinDriftScannerTests" --no-restore
```

Expected: PASS；两租户上下文不串线。

- [ ] **Step 5: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.WebApi/BackgroundServices/SpaceBinReconciliationWorker.cs CP6.Tests/Space/SpaceBinReconciliationWorkerTests.cs
git commit -m "feat(space): audit reconciliation worker runs"
```

## Task 8: 审计查询、权限种子和旧事件安全投影

**Files:**

- Create: `CP6.Entity/DTOs/Space/SpaceAuditDtos.cs`
- Create: `CP6.Core/Services/Space/Observability/ISpaceAuditQueryService.cs`
- Create: `CP6.Core/Services/Space/Observability/SpaceAuditQueryService.cs`
- Create: `CP6.Core/Services/Space/Observability/SpaceObservabilityOptions.cs`
- Create: `CP6.WebApi/Controllers/Space/SpaceAuditController.cs`
- Create: `CP6.WebApi/Seed/SpaceAuditPermissionSeed.cs`
- Modify: `CP6.WebApi/Controllers/Space/LocationPublishController.cs:1-103`
- Modify: `CP6.Core/Auth/RequirePermissionAttribute.cs:31-42`
- Modify: `CP6.WebApi/Seed/I18nSpaceScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs:460-480`
- Modify: `CP6.WebApi/Program.cs:920-970`
- Modify: `CP6.WebApi/appsettings.json`
- Modify: `docs/seeds/space-menu-seed-2.sql`
- Modify: `docs/seeds/space-roleaction-seed.sql`
- Test: `CP6.Tests/Space/SpaceAuditQueryServiceTests.cs`
- Test: `CP6.Tests/Space/SpaceAuditPermissionSeedTests.cs`
- Modify: `CP6.Tests/Space/SpacePermissionAttributeTests.cs`
- Modify: `CP6.Tests/RequirePermissionFilterTests.cs`

- [ ] **Step 1: 写查询租户隔离、窗口和脱敏失败测试**

```csharp
[Fact]
public async Task Timeline_returns_only_current_tenant_and_space_integration_events()
{
    SeedAudit(TenantA, Correlation, "space.floor.publish", "Succeeded");
    SeedAudit(TenantB, Correlation, "space.floor.publish", "Failed");
    SeedIntegration(TenantA, Correlation, source: "SPACE", lastError: "secret raw error");
    SeedIntegration(TenantA, Correlation, source: "ERP", lastError: "other module");
    await _db.SaveChangesAsync();

    var rows = await _service.GetTimelineAsync(Correlation);

    Assert.Contains(rows, x => x.Kind == "Audit");
    Assert.Contains(rows, x => x.Kind == "IntegrationEvent");
    Assert.DoesNotContain(rows, x => x.TenantId == TenantB);
    Assert.DoesNotContain(rows, x => x.SafeErrorCode?.Contains("secret") == true);
    Assert.DoesNotContain(rows, x => x.ResourceType == "ERP");
}

[Fact]
public async Task Query_caps_page_size_and_rejects_over_31_day_window()
{
    var page = await _service.QueryAsync(new SpaceAuditQueryDto(Page: 1, PageSize: 999));
    Assert.Equal(100, page.PageSize);

    var error = await Assert.ThrowsAsync<BizException>(() =>
        _service.QueryAsync(new SpaceAuditQueryDto(
            FromUtc: DateTime.UtcNow.AddDays(-32),
            ToUtc: DateTime.UtcNow)));
    Assert.Equal("SPACE_AUDIT_QUERY_RANGE_INVALID", error.Code);
}
```

- [ ] **Step 2: 定义 DTO 和查询服务**

DTO 至少包含：

```csharp
public sealed record SpaceAuditQueryDto(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Action = null,
    string? Outcome = null,
    Guid? CorrelationId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record SpaceAuditEventDto(
    Guid EventId,
    Guid TenantId,
    DateTime OccurredAtUtc,
    string ActorType,
    string ActorId,
    string? ActorName,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome,
    string? ReasonCode,
    Guid CorrelationId,
    string TraceId,
    Guid? JobId,
    Guid? RunId,
    Guid? PublishAttemptId,
    int? AttemptNo,
    SpaceAuditEvidenceDto? AuthorizationEvidence);

public sealed record SpaceAuditEvidenceDto(
    string? PermissionCode,
    string? AuthorizationResult,
    int? ItemCount,
    string? Status,
    string? ExceptionType,
    string? ErrorFingerprint);

public sealed record SpaceAuditPageDto(
    IReadOnlyList<SpaceAuditEventDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record SpaceAuditTimelineItemDto(
    string Kind,
    Guid TenantId,
    DateTime OccurredAtUtc,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome,
    string? SafeErrorCode,
    Guid CorrelationId,
    string? TraceId,
    Guid? JobId,
    Guid? RunId,
    Guid? PublishAttemptId,
    int? AttemptNo);

public sealed record SpacePublishEventDto(
    Guid Id,
    string HookName,
    string SourceNo,
    string TargetModule,
    string Status,
    int Attempts,
    DateTime CreateDate,
    Guid CorrelationId,
    Guid? JobId,
    Guid? PublishAttemptId,
    string? SafeErrorCode);
```

查询服务方法：

```csharp
Task<SpaceAuditPageDto> QueryAsync(SpaceAuditQueryDto query, CancellationToken ct = default);
Task<IReadOnlyList<SpaceAuditTimelineItemDto>> GetTimelineAsync(Guid correlationId, CancellationToken ct = default);
Task<IReadOnlyList<SpacePublishEventDto>> GetPublishEventsAsync(int page, int pageSize, CancellationToken ct = default);
```

所有查询使用 scoped `CP6Context` 的租户过滤；不得调用 `IgnoreQueryFilters`。`AuthorizationEvidenceJson` 必须反序列化到 `SpaceAuditEvidenceDto`，数据库 JSON 中的额外属性被忽略，不得以 `JsonElement` 或原字符串透传。Timeline 只合并 `SourceModule == "SPACE"` 的 IntegrationEvent。IntegrationEvent 错误只取第一个 `:` 前的稳定 ReasonCode；没有稳定格式的历史原文返回 `SPACE_LEGACY_ERROR_REDACTED`。历史 IntegrationEvent 的 `CreateDate` 若为 `Unspecified`，按服务器本地时区解释后转换 UTC；新 Space 事件由 Bridge 写 `DateTime.UtcNow`。

- [ ] **Step 3: 写 Controller 和精确权限反射测试**

Controller：

```csharp
[ApiController]
[Route("api/space/audit")]
[Authorize]
public sealed class SpaceAuditController : ControllerBase
{
    private readonly ISpaceAuditQueryService _query;
    private readonly ISpaceAuditWriter _writer;
    private readonly SpaceObservabilityOptions _options;

    public SpaceAuditController(
        ISpaceAuditQueryService query,
        ISpaceAuditWriter writer,
        IOptions<SpaceObservabilityOptions> options)
    {
        _query = query;
        _writer = writer;
        _options = options.Value;
    }

    [HttpGet("events")]
    [RequirePermission("space-audit", "read")]
    public async Task<IActionResult> Query(
        [FromQuery] SpaceAuditQueryDto query,
        CancellationToken ct)
    {
        if (!_options.AuditQueryEnabled) return Disabled();
        var data = await _query.QueryAsync(query, ct);
        await _writer.TryAppendAsync(new SpaceAuditEventInput(
            "space.audit.read",
            "SpaceAuditEvent",
            query.CorrelationId?.ToString(),
            SpaceAuditOutcome.Succeeded,
            Evidence: new SpaceAuditEvidence(
                PermissionCode: "space-audit:read",
                AuthorizationResult: "Allowed",
                ItemCount: data.Items.Count),
            ClientType: "Web"), ct);
        return Ok(new { code = 0, message = "OK", data });
    }

    [HttpGet("timeline/{correlationId:guid}")]
    [RequirePermission("space-audit", "read")]
    public async Task<IActionResult> Timeline(Guid correlationId, CancellationToken ct)
    {
        if (!_options.AuditQueryEnabled) return Disabled();
        var data = await _query.GetTimelineAsync(correlationId, ct);
        await _writer.TryAppendAsync(new SpaceAuditEventInput(
            "space.audit.timeline.read",
            "Correlation",
            correlationId.ToString(),
            SpaceAuditOutcome.Succeeded,
            Evidence: new SpaceAuditEvidence(
                PermissionCode: "space-audit:read",
                AuthorizationResult: "Allowed",
                ItemCount: data.Count),
            ClientType: "Web"), ct);
        return Ok(new { code = 0, message = "OK", data });
    }

    private static ObjectResult Disabled()
        => new(new { code = 404, message = "SPACE_AUDIT_QUERY_DISABLED", data = (object?)null })
        {
            StatusCode = 404
        };
}
```

审计读取的结果审计采用 `TryAppendAsync`，失败时继续返回已经授权且已脱敏的查询结果，并由 Writer 产生安全运维日志。

更新 `SpacePermissionAttributeTests`：

- Controller 数量从 9 改为 10。
- `AllowedReadPermissions` 精确包含：
  - `LocationPublishController.ListEvents = space-audit:read`
  - `SpaceAuditController.Query = space-audit:read`
  - `SpaceAuditController.Timeline = space-audit:read`
- 其他 GET 仍禁止 `RequirePermission`。
- 变更白名单加入 `space-audit:read`，但只允许上述 GET 使用。

- [ ] **Step 4: 旧发布事件路由改用安全查询**

`LocationPublishController` 移除直接 `CP6Context` 查询，注入 `ISpaceAuditQueryService`。`ListEvents`：

```csharp
[HttpGet("publish/events")]
[RequirePermission("space-audit", "read")]
public async Task<IActionResult> ListEvents(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    CancellationToken ct = default)
{
    var data = await _auditQuery.GetPublishEventsAsync(page, pageSize, ct);
    await _auditWriter.TryAppendAsync(new SpaceAuditEventInput(
        "space.integration-event.read",
        "IntegrationEvent",
        null,
        SpaceAuditOutcome.Succeeded,
        Evidence: new SpaceAuditEvidence(
            PermissionCode: "space-audit:read",
            AuthorizationResult: "Allowed",
            ItemCount: data.Count),
        ClientType: "Web"), ct);
    return Ok2(data);
}
```

Controller 构造器增加 `ISpaceAuditQueryService _auditQuery` 和 `ISpaceAuditWriter _auditWriter`。不得返回 `LastError` 或 `PayloadJson`；读取审计失败不阻断安全 DTO 返回。

- [ ] **Step 5: 返回稳定权限拒绝码**

`RequirePermissionAttribute` 的拒绝消息：

```csharp
var message = _menu == "space-audit" && _action == "read"
    ? "SPACE_AUDIT_READ_FORBIDDEN"
    : $"无权限：{_menu}:{_action}";
if (context.HttpContext.Request.Path.StartsWithSegments("/api/space"))
{
    var writer = context.HttpContext.RequestServices.GetService<ISpaceAuditWriter>();
    if (writer is not null)
        await writer.TryAppendAsync(new SpaceAuditEventInput(
            "space.permission.check",
            context.ActionDescriptor.DisplayName ?? "SpaceAction",
            context.HttpContext.Request.Path.Value,
            SpaceAuditOutcome.Denied,
            _menu == "space-audit" && _action == "read"
                ? "SPACE_AUDIT_READ_FORBIDDEN"
                : "SPACE_PERMISSION_DENIED",
            Evidence: new SpaceAuditEvidence(
                PermissionCode: $"{_menu}:{_action}",
                AuthorizationResult: "Denied"),
            ClientType: "Web"),
            context.HttpContext.RequestAborted);
}
context.Result = new ObjectResult(new { code = 403, message })
{
    StatusCode = StatusCodes.Status403Forbidden
};
```

在 `RequirePermissionFilterTests` 注册 recording Writer，增加 `SPACE_AUDIT_READ_FORBIDDEN`、普通 Space 权限的 `SPACE_PERMISSION_DENIED` 和 `Denied` 事件断言，并保证非 Space 普通权限仍返回原消息且不写 Space 审计。Writer 失败不改变 403。

- [ ] **Step 6: 实现逐租户权限种子**

`SpaceAuditPermissionSeed`：

- 使用 Space 父菜单 MenuId 900 和事件/审计菜单 MenuId 906。
- 900 不存在时幂等创建 `MenuKey="space"` 的父菜单；906 不存在时创建，已存在时只把 `MenuKey` 收敛为 `space-audit` 并保持 `RoutePath="/space/events"`。
- 每个租户显式写 `TenantId`，确保 `RoleId=1` 具有 900、906 的 `Sys_RoleMenu`，不授普通角色。
- 每个 `Sys_Tenants.Id` 插入 `Sys_MenuAction(MenuId=906, ActionCode="read")`。
- 只给 `RoleId=1` 插入 `Sys_RoleAction`；不授普通角色。
- 查询使用 `IgnoreQueryFilters()` 并显式写 TenantId。
- 重复运行不新增行。

测试独立硬编码 oracle：

```csharp
Assert.Equal("space-audit", db.Sys_Menus.Single(x => x.MenuId == 906).MenuKey);
Assert.True(db.Sys_Menus.Any(x => x.MenuId == 900 && x.MenuKey == "space"));
Assert.All(new[] { TenantA, TenantB }, tenant =>
{
    Assert.True(db.Sys_RoleMenus.IgnoreQueryFilters().Any(
        x => x.TenantId == tenant && x.RoleId == 1 && x.MenuId == 906));
    Assert.True(db.Sys_MenuActions.IgnoreQueryFilters().Any(
        x => x.TenantId == tenant && x.MenuId == 906 && x.ActionCode == "read"));
    Assert.True(db.Sys_RoleActions.IgnoreQueryFilters().Any(
        x => x.TenantId == tenant && x.RoleId == 1 && x.MenuId == 906 && x.ActionCode == "read"));
});
```

在 `Program.cs` 数据库启动种子区调用 `SpaceAuditPermissionSeed.EnsureSeeded(db)`。同步更新两个 SQL 对照文件，避免 C# 和手工部署脚本分叉。

- [ ] **Step 7: 注册 Options、服务和错误词条**

`SpaceObservabilityOptions`：

```csharp
public sealed class SpaceObservabilityOptions
{
    public const string SectionName = "SpaceObservability";
    public bool AuditQueryEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; } = true;
}
```

配置：

```json
"SpaceObservability": {
  "AuditQueryEnabled": true,
  "MetricsEnabled": true
}
```

DI：

```csharp
builder.Services.Configure<SpaceObservabilityOptions>(
    builder.Configuration.GetSection(SpaceObservabilityOptions.SectionName));
builder.Services.AddScoped<ISpaceAuditQueryService, SpaceAuditQueryService>();
```

`I18nSpaceScreenSeed` 增加本设计第 9 节错误码以及 `SPACE_AUDIT_QUERY_RANGE_INVALID`、`SPACE_AUDIT_QUERY_DISABLED` 的五语安全文案。

- [ ] **Step 8: 运行查询、权限和种子测试**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceAuditQueryServiceTests|FullyQualifiedName~SpaceAuditPermissionSeedTests|FullyQualifiedName~SpacePermissionAttributeTests|FullyQualifiedName~RequirePermissionFilterTests" --no-restore
```

Expected: PASS；审计 GET 是唯一允许带精确权限的 Space 只读端点。

- [ ] **Step 9: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.Entity/DTOs/Space/SpaceAuditDtos.cs CP6.Core/Services/Space/Observability/ISpaceAuditQueryService.cs CP6.Core/Services/Space/Observability/SpaceAuditQueryService.cs CP6.Core/Services/Space/Observability/SpaceObservabilityOptions.cs CP6.WebApi/Controllers/Space/SpaceAuditController.cs CP6.WebApi/Seed/SpaceAuditPermissionSeed.cs CP6.WebApi/Controllers/Space/LocationPublishController.cs CP6.Core/Auth/RequirePermissionAttribute.cs CP6.WebApi/Seed/I18nSpaceScreenSeed.cs CP6.WebApi/Program.cs CP6.WebApi/appsettings.json docs/seeds/space-menu-seed-2.sql docs/seeds/space-roleaction-seed.sql CP6.Tests/Space/SpaceAuditQueryServiceTests.cs CP6.Tests/Space/SpaceAuditPermissionSeedTests.cs CP6.Tests/Space/SpacePermissionAttributeTests.cs CP6.Tests/RequirePermissionFilterTests.cs
git commit -m "feat(space): expose redacted audit evidence"
```

## Task 9: 前端事件页移除原始错误正文

**Files:**

- Modify: `cp6.web/src/types/space/scene.ts:197-207`
- Modify: `cp6.web/src/views/space/lifecycle/SpaceEventsView.vue`
- Modify: `cp6.web/src/views/space/lifecycle/__tests__/SpaceEventsView.spec.ts`
- Modify: `docs/seeds/space-i18n-seed-2.sql`

- [ ] **Step 1: 先把测试数据改为安全 DTO 并写无全文按钮断言**

```ts
function ev(over: Partial<SpaceEventVO>): SpaceEventVO {
  return {
    id: 'e',
    hookName: 'OnLocationPublishedAsync',
    sourceNo: 'PUB-001',
    targetModule: 'WMS',
    status: 'SUCCESS',
    attempts: 1,
    createDate: '2026-07-06T10:00:00Z',
    correlationId: '11111111-1111-1111-1111-111111111111',
    jobId: '22222222-2222-2222-2222-222222222222',
    publishAttemptId: '33333333-3333-3333-3333-333333333333',
    safeErrorCode: null,
    ...over,
  }
}

it('只显示安全错误码和关联编号，不提供原始错误详情弹窗', async () => {
  vi.mocked(publishApi.events).mockResolvedValue({
    code: 0,
    message: '',
    data: [ev({ status: 'FAILED', safeErrorCode: 'SPACE_ADAPTER_FAILURE' })],
  })
  const wrapper = mountView()
  await flushPromises()

  expect(wrapper.text()).toContain('SPACE_ADAPTER_FAILURE')
  expect(wrapper.text()).toContain('11111111-1111-1111-1111-111111111111')
  expect(wrapper.find('[data-testid="raw-error-detail"]').exists()).toBe(false)
})
```

- [ ] **Step 2: 运行前端定向测试并确认类型失败**

Run:

```powershell
npm test -- src/views/space/lifecycle/__tests__/SpaceEventsView.spec.ts
```

Workdir: `D:\CP6\tmp\worktrees\space-e00-inventory\cp6.web`

Expected: FAIL，`SpaceEventVO` 仍要求/使用 `lastError`。

- [ ] **Step 3: 更新安全 VO 和页面**

类型：

```ts
export interface SpaceEventVO {
  id: string
  hookName: string
  sourceNo: string
  targetModule: string
  status: string
  attempts: number
  createDate: string
  correlationId: string
  jobId?: string | null
  publishAttemptId?: string | null
  safeErrorCode?: string | null
}
```

页面：

- 删除 `ElMessageBox`、`useTOr`、`showError` 和 `col-lastError` slot。
- 列改为 `correlationId`（mono）、`publishAttemptId`（mono）、`safeErrorCode`。
- 安全错误码为空显示 `—`。
- 不截取或显示后端未返回字段。

SQL i18n 对照把 `space.events.col.lastError` 替换/新增为：

```text
space.events.col.safeErrorCode
space.events.col.correlationId
space.events.col.publishAttemptId
```

- [ ] **Step 4: 运行事件页、类型和生产构建**

Run:

```powershell
npm test -- src/views/space/lifecycle/__tests__/SpaceEventsView.spec.ts
npm run build-only
```

Workdir: `D:\CP6\tmp\worktrees\space-e00-inventory\cp6.web`

Expected: 定向测试 PASS；Vite production build PASS。

- [ ] **Step 5: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add cp6.web/src/types/space/scene.ts cp6.web/src/views/space/lifecycle/SpaceEventsView.vue cp6.web/src/views/space/lifecycle/__tests__/SpaceEventsView.spec.ts docs/seeds/space-i18n-seed-2.sql
git commit -m "fix(space): remove raw integration errors from the UI"
```

## Task 10: 可关闭的 Space 审计指标

**Files:**

- Create: `CP6.Core/Services/Space/Observability/ISpaceAuditMetricsSnapshotProvider.cs`
- Create: `CP6.Core/Services/Space/Observability/SpaceAuditMetricsSnapshotProvider.cs`
- Create: `CP6.WebApi/Observability/SpaceAuditMetricsCollector.cs`
- Modify: `CP6.Entity/DTOs/Space/SpaceAuditDtos.cs`
- Modify: `CP6.WebApi/Program.cs:575-579`
- Modify: `CP6.WebApi/Program.cs:2737-2740`
- Test: `CP6.Tests/Space/SpaceAuditMetricsSnapshotProviderTests.cs`

- [ ] **Step 1: 写跨租户、无租户标签快照测试**

```csharp
[Fact]
public async Task Metrics_snapshot_aggregates_all_tenants_without_exposing_tenant_dimension()
{
    SeedAudit(TenantA, outcome: "Started");
    SeedAudit(TenantA, outcome: "Succeeded");
    SeedAudit(TenantB, outcome: "Failed");
    await _db.SaveChangesAsync();
    var provider = new SpaceAuditMetricsSnapshotProvider(_db);

    var snapshot = await provider.GetSnapshotAsync();

    Assert.Equal(3, snapshot.Total);
    Assert.Equal(1, snapshot.ByOutcome["Started"]);
    Assert.Equal(1, snapshot.ByOutcome["Succeeded"]);
    Assert.Equal(1, snapshot.ByOutcome["Failed"]);
}
```

Provider 为运维全局指标显式 `IgnoreQueryFilters()`，但 DTO 不包含 TenantId，也不创建 tenant label。

- [ ] **Step 2: 实现快照和 Collector**

DTO：

```csharp
public sealed record SpaceAuditMetricsSnapshot(
    long Total,
    IReadOnlyDictionary<string, long> ByOutcome);
```

Provider：

```csharp
public async Task<SpaceAuditMetricsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
{
    var groups = await _db.SpaceAuditEvents
        .IgnoreQueryFilters()
        .AsNoTracking()
        .GroupBy(x => x.Outcome)
        .Select(g => new { Outcome = g.Key, Count = (long)g.Count() })
        .ToListAsync(ct);
    return new(groups.Sum(x => x.Count), groups.ToDictionary(x => x.Outcome, x => x.Count));
}
```

Collector 注册：

```text
cp6_space_audit_event_total
cp6_space_audit_event_by_outcome{outcome}
```

使用 Gauge，因为值来自持久化账本，进程重启后重新聚合。`Register()` 只注册一次 BeforeCollect callback；异常日志使用安全错误分类，不传异常正文。

- [ ] **Step 3: 绑定开关并只在启用时注册**

DI：

```csharp
builder.Services.AddScoped<ISpaceAuditMetricsSnapshotProvider, SpaceAuditMetricsSnapshotProvider>();
builder.Services.AddSingleton<SpaceAuditMetricsCollector>();
```

启动：

```csharp
if (app.Configuration.GetValue<bool>("SpaceObservability:MetricsEnabled"))
    app.Services.GetRequiredService<SpaceAuditMetricsCollector>().Register();
```

`MetricsEnabled=false` 时不解析 Collector，因此不创建这两个指标；既有 `/metrics` 和 Bridge 指标不受影响。

- [ ] **Step 4: 运行指标快照和构建测试**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceAuditMetricsSnapshotProviderTests" --no-restore
dotnet build CP6.slnx --no-restore
```

Expected: PASS；无 prometheus-net 依赖进入 `CP6.Core`。

- [ ] **Step 5: Commit 检查点**

仅在用户明确授权后运行：

```powershell
git add CP6.Core/Services/Space/Observability/ISpaceAuditMetricsSnapshotProvider.cs CP6.Core/Services/Space/Observability/SpaceAuditMetricsSnapshotProvider.cs CP6.WebApi/Observability/SpaceAuditMetricsCollector.cs CP6.Entity/DTOs/Space/SpaceAuditDtos.cs CP6.WebApi/Program.cs CP6.Tests/Space/SpaceAuditMetricsSnapshotProviderTests.cs
git commit -m "feat(space): add rollback-safe audit metrics"
```

## Task 11: 端到端验收、全量验证和实施报告

**Files:**

- Create: `CP6.Tests/Space/SpaceObservabilityChainTests.cs`
- Create: `docs/space/reports/e00-s04-observability-audit.md`
- Modify only if verification exposes an E00-S04 regression: files already listed in Tasks 1-10

- [ ] **Step 1: 写完整链路验收测试**

测试用同一个 InMemory 数据库和真实 Space 组件完成：

```csharp
[Fact]
public async Task Http_publish_outbox_retry_and_audit_share_one_correlation()
{
    var correlation = Guid.NewGuid();
    var services = BuildServices(
        firstConsumer: new FailOnceThenSucceedConsumer());
    SeedPublishableFloor(services, out var floorId);
    using var requestScope = services.CreateScope();
    var scoped = requestScope.ServiceProvider;

    var http = AuthenticatedSpaceHttpContext(
        tenantId: TenantA,
        actorId: UserA,
        correlationId: correlation);
    var boundary = new SpaceExecutionContextMiddleware(async context =>
    {
        var writer = scoped.GetRequiredService<ISpaceAuditWriter>();
        Assert.True(await writer.TryAppendAsync(new SpaceAuditEventInput(
            "space.http.post",
            "LocationPublish.PublishFloor",
            floorId.ToString(),
            SpaceAuditOutcome.Started,
            ClientType: "Web")));
        var service = scoped.GetRequiredService<ILocationPublishService>();
        await service.PublishFloorAsync(floorId, null, "alice");
        Assert.True(await writer.TryAppendAsync(new SpaceAuditEventInput(
            "space.http.post",
            "LocationPublish.PublishFloor",
            floorId.ToString(),
            SpaceAuditOutcome.Succeeded,
            ClientType: "Web")));
    }, NullLogger<SpaceExecutionContextMiddleware>.Instance);

    await boundary.InvokeAsync(
        http,
        scoped.GetRequiredService<ITenantContext>(),
        scoped.GetRequiredService<ISpaceExecutionContextManager>());

    await using (var assertDb = services.GetRequiredService<ISpaceAuditDbContextFactory>().CreateDbContext())
    {
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(correlation, evt.CorrelationId);
        Assert.NotNull(evt.JobId);
        Assert.NotNull(evt.PublishAttemptId);
        evt.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
        await assertDb.SaveChangesAsync();
    }

    var worker = NewRetryWorker(services);
    await worker.ProcessOnceAsync();

    await using var finalDb = services.GetRequiredService<ISpaceAuditDbContextFactory>().CreateDbContext();
    var finalEvent = await finalDb.IntegrationEvents.SingleAsync();
    var audit = await finalDb.SpaceAuditEvents
        .Where(x => x.CorrelationId == correlation)
        .OrderBy(x => x.OccurredAtUtc)
        .ToListAsync();

    Assert.Equal(IntegrationEventStatus.Success, finalEvent.Status);
    Assert.NotEmpty(audit);
    Assert.All(audit, x => Assert.Equal(TenantA, x.TenantId));
    Assert.Contains(audit, x => x.ActorType == "User");
    Assert.Contains(audit, x => x.ActorType == "System"
        && x.JobId == finalEvent.JobId
        && x.PublishAttemptId == finalEvent.PublishAttemptId
        && x.RunId != null);
    Assert.All(audit, x => Assert.Equal(correlation, x.CorrelationId));
}
```

测试在调用中间件前把 request scope 的 `ITenantContext.CurrentTenantId` 设为 TenantA，等价于既有 TenantMiddleware 已运行。测试不得模拟新的 CorrelationId；失败一次的 Consumer 第一次返回失败、Worker 重试返回成功。Action Filter 的失败关闭行为由 Task 4 独立测试，此处用真实 Writer 写入同样的 HTTP Started/Succeeded 事件，以集中验证跨异步边界的关联闭环。

- [ ] **Step 2: 运行 E00-S04 后端定向测试**

Run:

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SpaceExecutionContext|FullyQualifiedName~SpaceAudit|FullyQualifiedName~SpaceObservability|FullyQualifiedName~SpaceBinReconciliation|FullyQualifiedName~LocationPublishServiceTests|FullyQualifiedName~SpaceBridgeHookTests|FullyQualifiedName~IntegrationEventDispatcherTests|FullyQualifiedName~IntegrationEventRetryWorkerTests" --no-restore
```

Expected: 所有 E00-S04 和受影响既有测试 PASS。

- [ ] **Step 3: 运行完整后端测试**

Run:

```powershell
dotnet test CP6.slnx --no-restore
```

Expected: 既有 SQL-only 测试仍按环境条件跳过；其余测试 PASS。任何新增失败必须修复后重跑。

- [ ] **Step 4: 运行前端定向、完整断言和生产构建**

Run:

```powershell
npm test -- src/views/space/lifecycle/__tests__/SpaceEventsView.spec.ts src/types/space/dataSource.spec.ts
npm test
npm run build-only
```

Workdir: `D:\CP6\tmp\worktrees\space-e00-inventory\cp6.web`

Expected:

- E00-S04 定向测试 PASS。
- 完整 Vitest 的断言全部 PASS。
- 若 unchanged `SpaceCodeRuleView.spec.ts` 仍产生已知 15 个 Element Plus `ElSelect` unhandled rejection，单独复现并在报告中记录；E00-S04 文件不得产生新 unhandled rejection。
- Vite production build PASS。

- [ ] **Step 5: 检查 Migration 和库存报告**

Run:

```powershell
dotnet ef migrations script --project CP6.Core --startup-project CP6.WebApi --context CP6Context --idempotent --output tmp/space-e00-s04-migration.sql
python -m unittest tools/space-inventory/test_space_inventory.py
python tools/space-inventory/space_inventory.py --check
git diff --check
git status --short
```

Expected:

- Migration SQL 只有本任务新增 schema。
- inventory 测试与冻结报告检查 PASS。
- `git diff --check` 无空白错误。
- `git status` 中没有 `bin/`、`obj/`、`dist/`、生成 JS 或 `__pycache__` 等意外产物。

- [ ] **Step 6: 写实施报告**

`docs/space/reports/e00-s04-observability-audit.md` 必须包含：

```markdown
# E00-S04 - Space observability and audit baseline

## Outcome
## Execution context contract
## Fail-closed boundaries
## Audit ledger and redaction
## Publish and retry propagation
## Query permission
## Metrics and configuration
## Migration
## Rollback
## Verification
## Pre-existing unrelated failures
```

报告写明实际测试数量、Migration 名称、配置键、已知基线故障和以下回滚铁律：

- 可关闭 `AuditQueryEnabled` 和 `MetricsEnabled`。
- 不关闭 Tenant/Actor/外部主体校验。
- 不停止高风险审计写入。
- 不删除 `Space_AuditEvent` 表或记录。

- [ ] **Step 7: 最终自审**

逐条对照：

```text
HTTP Tenant/Actor fail closed
external subject denied
valid/missing/invalid X-Correlation-ID
W3C TraceId
append-only audit
typed evidence allowlist
no raw body/payload/exception
space:audit:read
safe legacy events endpoint
HTTP→Adapter→Outbox→Worker→Audit correlation
stable JobId/PublishAttemptId
new RunId/TraceId per retry
query and metrics rollback switches
audit rows retained
E01 SpaceContext untouched
```

检查新代码中不存在：

```powershell
git grep -n "Guid.NewGuid()" -- CP6.Core/Services/Integration/IntegrationEventDispatcher.cs CP6.Core/Services/Space/LocationPublishService.cs
git grep -n "LastError\\|PayloadJson" -- CP6.WebApi/Controllers/Space cp6.web/src/views/space/lifecycle/SpaceEventsView.vue
git grep -n "ex.ToString()" -- CP6.Core/Services/Integration/SpaceBridgeHook.cs CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs
```

Expected:

- Dispatcher 和 LocationPublishService 不再创建链路 CorrelationId；为 PublishAttemptId/JobId 创建 GUID 的位置必须有明确变量名和语义。
- Space API/页面不返回 `LastError` 或 `PayloadJson`。
- Space 错误路径不保存 `Exception.ToString()`。

- [ ] **Step 8: Commit 检查点**

仅在用户明确授权累计 E00 提交后运行：

```powershell
git add CP6.Tests/Space/SpaceObservabilityChainTests.cs docs/space/reports/e00-s04-observability-audit.md
git commit -m "test(space): verify the observability chain"
```

## 完成定义

E00-S04 只有在以下条件全部满足时完成：

1. 设计规范的验收映射均有对应自动化测试。
2. 高风险 Space 动作在前置审计失败时没有调用业务服务或 Adapter。
3. 一次发布及其重试能用同一 CorrelationId 查询到用户和 System Actor 审计。
4. 旧发布事件 API 和前端均不再暴露原始 `LastError`。
5. Migration、完整后端测试、前端定向测试和生产构建通过。
6. 已知基线故障被独立复现且没有归因给 E00-S04。
7. 两个脏工作区的状态计数与开始前一致。
8. 未经用户授权没有执行暂存或提交。
