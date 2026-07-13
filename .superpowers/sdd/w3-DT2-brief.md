### Task D-T2: message 外呼端点（`[AllowAnonymous]` + 自定义过滤器 + 白名单 + 64KB + 幂等头）

> **spec §3.4 全文落点。** 过滤器仿 `RequirePlatformAdminAttribute`（`IAsyncAuthorizationFilter` + `RequestServices` 服务定位 + `context.Result` 短路）；跨租户定位仿 `RefreshTokenService` 的 `IgnoreQueryFilters`（key 绑定单触发器单租户）；验过 key 后**切租户上下文**（对齐 TenantScopeRunner 的 `ITenantContext.CurrentTenantId` setter 口径）。

**Files:**
- Create: `CP6.Core/Auth/WfTriggerApiKeyAttribute.cs`
- Create: `CP6.WebApi/Controllers/Oa/FlowTriggerFireController.cs`
- Test: `CP6.Tests/Wf/WfTriggerMessageEndpointTests.cs`

- [ ] **Step 1: 写失败测试**（过滤器与控制器直接构造调用，不起 Host：`DefaultHttpContext` + `RequestServices` = 手搭 `ServiceCollection`{CP6Context(SQLite harness)、ITenantContext=TenantContext}；`AuthorizationFilterContext` 带 `RouteData{ id }`。若 `CP6.Tests.csproj` 尚未引用 `CP6.WebApi`，加 ProjectReference——控制器类型需要）

```csharp
// CP6.Tests/Wf/WfTriggerMessageEndpointTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Controllers.Oa;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class WfTriggerMessageEndpointTests
{
    // ── 脚手架 ──

    private static ServiceProvider NewSp(SqliteConnection conn)
    {
        var services = new ServiceCollection();
        services.AddScoped<CP6Context>(_ => Ctx(conn));
        services.AddSingleton<ITenantContext, TenantContext>();
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid TriggerId, string RawKey, Guid TenantId)> SeedMessageTriggerAsync(
        SqliteConnection conn, bool enabled = true, int type = WfTriggerType.Message,
        string configJson = "{\"varsSchema\":[\"orderNo\"]}")
    {
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var raw = WfApiKeyHelper.NewRawKey();
        var trig = NewTrigger("fk-trig", type, starter, enabled, configJson);
        trig.ApiKeyHash = WfApiKeyHelper.HashOf(raw);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        return (trig.Id, raw, trig.TenantId);
    }

    private static async Task<(AuthorizationFilterContext Ctx, DefaultHttpContext Http)> RunFilterAsync(
        IServiceProvider sp, Guid id, string? apiKey, string? idemKey)
    {
        var http = new DefaultHttpContext { RequestServices = sp };
        if (apiKey != null) http.Request.Headers["X-Api-Key"] = apiKey;
        if (idemKey != null) http.Request.Headers["Idempotency-Key"] = idemKey;
        var routeData = new RouteData();
        routeData.Values["id"] = id.ToString();
        var actx = new AuthorizationFilterContext(
            new ActionContext(http, routeData, new ActionDescriptor()),
            new List<IFilterMetadata>());
        await new WfTriggerApiKeyAttribute().OnAuthorizationAsync(actx);
        return (actx, http);
    }

    /// <summary>控制器直接构造：过滤器已放行的前提（trigger 塞 Items、幂等头就位、body 就位）。</summary>
    private static FlowTriggerFireController NewController(
        SqliteConnection conn, CP6Context db, Wf_FlowTrigger trigger, string idemKey, string body)
    {
        var http = new DefaultHttpContext { RequestServices = NewSp(conn) };
        http.Items[WfTriggerApiKeyAttribute.ItemKey] = trigger;
        http.Request.Headers["Idempotency-Key"] = idemKey;
        var bytes = Encoding.UTF8.GetBytes(body);
        http.Request.Body = new MemoryStream(bytes);
        http.Request.ContentLength = bytes.Length;
        return new FlowTriggerFireController(Service(db))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static string ResultJson(IActionResult r) => JsonSerializer.Serialize(((ObjectResult)r).Value);

    // ── 过滤器 ──

    [Fact]
    public async Task Filter_UnknownId_404()
    {
        using var conn = NewSqliteWithSchema();
        var (actx, _) = await RunFilterAsync(NewSp(conn), Guid.NewGuid(), "any", "ik-1");
        var nf = Assert.IsType<NotFoundObjectResult>(actx.Result);
        Assert.Contains("404", ResultJson(nf));
    }

    [Fact]
    public async Task Filter_DisabledTrigger_404_SameShapeAsUnknown()
    {
        using var conn = NewSqliteWithSchema();
        var (id, raw, _) = await SeedMessageTriggerAsync(conn, enabled: false);
        var sp = NewSp(conn);

        var (disabledCase, _) = await RunFilterAsync(sp, id, raw, "ik-1");
        var (unknownCase, _) = await RunFilterAsync(sp, Guid.NewGuid(), raw, "ik-1");

        var a = Assert.IsType<NotFoundObjectResult>(disabledCase.Result);
        var b = Assert.IsType<NotFoundObjectResult>(unknownCase.Result);
        Assert.Equal(ResultJson(b), ResultJson(a));        // 停用与不存在响应体逐字段相同（spec §3.4）
    }

    [Fact]
    public async Task Filter_WrongKey_401()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        var (actx, _) = await RunFilterAsync(NewSp(conn), id, "wrong-key", "ik-1");
        var obj = Assert.IsType<ObjectResult>(actx.Result);
        Assert.Equal(401, obj.StatusCode);
    }

    [Fact]
    public async Task Filter_MissingIdempotencyKey_400()
    {
        using var conn = NewSqliteWithSchema();
        var (id, raw, _) = await SeedMessageTriggerAsync(conn);
        var (actx, _) = await RunFilterAsync(NewSp(conn), id, raw, idemKey: null);
        Assert.IsType<BadRequestObjectResult>(actx.Result);
    }

    [Fact]
    public async Task Filter_Valid_SetsTenant_StashesTrigger_NoResult()
    {
        using var conn = NewSqliteWithSchema();
        var (id, raw, tenantId) = await SeedMessageTriggerAsync(conn);
        var sp = NewSp(conn);

        var (actx, http) = await RunFilterAsync(sp, id, raw, "ik-1");

        Assert.Null(actx.Result);                          // 放行
        var stashed = Assert.IsType<Wf_FlowTrigger>(http.Items[WfTriggerApiKeyAttribute.ItemKey]);
        Assert.Equal(id, stashed.Id);
        Assert.Equal(tenantId, sp.GetRequiredService<ITenantContext>().CurrentTenantId);   // 租户已切
    }

    [Fact]
    public async Task Filter_NonMessageType_404()
    {
        using var conn = NewSqliteWithSchema();
        var (id, raw, _) = await SeedMessageTriggerAsync(conn, type: WfTriggerType.Timer);
        var (actx, _) = await RunFilterAsync(NewSp(conn), id, raw, "ik-1");
        Assert.IsType<NotFoundObjectResult>(actx.Result);  // 端点只服务 message 型
    }

    // ── 控制器 ──

    [Fact]
    public async Task Fire_FirstCall_201_WithInstanceId_SchemaFiltered()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        using var db = Ctx(conn);
        var trig = await db.Wf_FlowTriggers.SingleAsync(t => t.Id == id);

        var c = NewController(conn, db, trig, "ik-1", "{\"orderNo\":\"PO-1\",\"evil\":\"x\"}");
        var r = await c.Fire(id, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(r);
        Assert.Equal(201, obj.StatusCode);
        Assert.Contains("instanceId", ResultJson(obj));
        var inst = await db.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Contains("\"orderNo\":\"PO-1\"", inst.VarsJson);   // 白名单保留
        Assert.DoesNotContain("evil", inst.VarsJson);             // 白名单外丢弃（防变量注入）
    }

    [Fact]
    public async Task Fire_SameIdempotencyKey_200_SameInstance()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        using var db = Ctx(conn);
        var trig = await db.Wf_FlowTriggers.SingleAsync(t => t.Id == id);

        var r1 = await NewController(conn, db, trig, "ik-1", "{\"orderNo\":\"PO-1\"}").Fire(id, CancellationToken.None);
        var r2 = await NewController(conn, db, trig, "ik-1", "{\"orderNo\":\"PO-1\"}").Fire(id, CancellationToken.None);

        Assert.Equal(201, ((ObjectResult)r1).StatusCode);
        var ok = Assert.IsType<OkObjectResult>(r2);                // 200 幂等重放
        Assert.Equal(ResultJson((ObjectResult)r1), ResultJson(ok));   // 同 instanceId
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_OversizeBody_400()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        using var db = Ctx(conn);
        var trig = await db.Wf_FlowTriggers.SingleAsync(t => t.Id == id);
        var big = "{\"orderNo\":\"" + new string('x', 65 * 1024) + "\"}";   // >64KB

        var r = await NewController(conn, db, trig, "ik-1", big).Fire(id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_NonObjectBody_400()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        using var db = Ctx(conn);
        var trig = await db.Wf_FlowTriggers.SingleAsync(t => t.Id == id);

        var r = await NewController(conn, db, trig, "ik-1", "[1,2]").Fire(id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter WfTriggerMessageEndpointTests`）。

- [ ] **Step 3: 实现过滤器**

```csharp
// CP6.Core/Auth/WfTriggerApiKeyAttribute.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Auth;

/// <summary>message 触发器外呼闸（spec §3.4）：X-Api-Key SHA-256 常量时间校验 + Idempotency-Key 必填
/// + 404 不区分「不存在/停用」。验过 key 后按触发器租户切 ITenantContext（AllowAnonymous 无 JWT 租户）。
/// 特性不能构造注入，用 RequestServices 服务定位（仿 RequirePlatformAdminAttribute）。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WfTriggerApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string ItemKey = "WfTrigger.Fire.Trigger";
    public const int MaxIdempotencyKeyLength = 200;   // 进唯一索引键列（映射表④）

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetService<CP6Context>();
        if (db == null)
        {
            context.Result = new ObjectResult(new { code = 500, message = "服务未注册" }) { StatusCode = 500 };
            return;
        }

        static IActionResult NotFound404() => new NotFoundObjectResult(new { code = 404, message = "trigger not found" });

        if (!Guid.TryParse(context.RouteData.Values["id"]?.ToString(), out var id))
        {
            context.Result = NotFound404();
            return;
        }

        // 跨租户按 Id 定位（key 绑定单触发器单租户，IgnoreQueryFilters 仿 RefreshTokenService 先例）
        var trigger = await db.Wf_FlowTriggers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && t.TriggerType == WfTriggerType.Message);
        if (trigger == null || !trigger.Enabled)
        {
            context.Result = NotFound404();   // 停用与不存在不区分（spec §3.4）
            return;
        }

        var rawKey = http.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(rawKey) || !WfApiKeyHelper.Verify(rawKey, trigger.ApiKeyHash))
        {
            context.Result = new ObjectResult(new { code = 401, message = "invalid api key" }) { StatusCode = 401 };
            return;
        }

        var idemKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idemKey) || idemKey.Length > MaxIdempotencyKeyLength)
        {
            context.Result = new BadRequestObjectResult(
                new { code = 400, message = $"Idempotency-Key header required (<= {MaxIdempotencyKeyLength} chars)" });
            return;
        }

        // 租户切换：同 scope 的 ITenantContext setter（对齐 TenantScopeRunner 现状口径，spec §6）
        http.RequestServices.GetRequiredService<ITenantContext>().CurrentTenantId = trigger.TenantId;
        http.Items[ItemKey] = trigger;
    }
}
```

- [ ] **Step 4: 实现控制器**

```csharp
// CP6.WebApi/Controllers/Oa/FlowTriggerFireController.cs
using System.Text;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>message 触发器外呼端点（spec §3.4）。
/// 响应：201 新发起 {instanceId} / 200 幂等重放 {instanceId} / 400 缺幂等头·负载超限·非对象 /
/// 401 key 无效 / 404 不存在或未启用（不区分）/ 500 运行时发起失败（E-WF-022/023/024 detail）。</summary>
[ApiController]
[Route("api/oa/flow-triggers")]
public class FlowTriggerFireController : ControllerBase
{
    public const int MaxPayloadBytes = 64 * 1024;   // 64KB 上限防滥用（spec §6）

    private readonly IFlowTriggerService _triggers;

    public FlowTriggerFireController(IFlowTriggerService triggers) { _triggers = triggers; }

    [HttpPost("{id:guid}/fire")]
    [AllowAnonymous]
    [WfTriggerApiKey]
    public async Task<IActionResult> Fire(Guid id, CancellationToken ct)
    {
        var trigger = (Wf_FlowTrigger)HttpContext.Items[WfTriggerApiKeyAttribute.ItemKey]!;
        var idemKey = Request.Headers["Idempotency-Key"].First()!;

        // 64KB：Content-Length 先验 + 实读字节数兜底（chunked 无 Content-Length 时）
        if (Request.ContentLength is > MaxPayloadBytes)
            return BadRequest(new { code = 400, message = "payload too large (64KB max)" });
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            body = await reader.ReadToEndAsync(ct);
        if (Encoding.UTF8.GetByteCount(body) > MaxPayloadBytes)
            return BadRequest(new { code = 400, message = "payload too large (64KB max)" });

        // varsSchema 白名单过滤（防变量注入，spec §2.3/§6）
        string varsJson;
        try
        {
            var cfg = WfTriggerConfig.ParseMessage(trigger.ConfigJson);
            varsJson = WfTriggerVarsMapper.FilterBySchema(body, cfg.VarsSchema);
        }
        catch (JsonException)
        {
            return BadRequest(new { code = 400, message = "body must be a JSON object" });
        }

        var r = await _triggers.FireAsync(trigger, varsJson, WfTriggerType.Message, idemKey, ct);
        if (!r.Success)
            return StatusCode(500, new { code = 500, message = r.Error });
        return r.Replayed
            ? Ok(new { instanceId = r.InstanceId })                          // 200 幂等重放
            : StatusCode(201, new { instanceId = r.InstanceId });            // 201 新发起
    }
}
```

- [ ] **Step 5: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter WfTriggerMessageEndpointTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): D-T2 message 外呼端点 AllowAnonymous+key 常量时间闸+幂等头+64KB+白名单"
```

---


---
## 附: 共享契约(plan全局)
## 共享契约（所有 Task 用这些**精确**名字，前后一致）

- `WfTriggerType`：`Timer=0 / Event=1 / Message=2`（int 常量，`WfStatus.cs`）。
- 实体字段：`Wf_FlowTrigger { FlowKey, TriggerType, ConfigJson, Enabled, EventKey, StarterUserId, NextDueUtc, LastFiredUtc, ApiKeyHash, RowVersion }`；`Wf_TriggerFire { TriggerId, IdempotencyKey, FiredUtc, InstanceId, Source, Error, PayloadHash }`（均继承 BaseTenantEntity）。
- `TriggerFireResult { bool Success; bool Replayed; Guid? InstanceId; string? Error; static Ok(Guid, bool replayed=false); static Fail(string); }`
- `IFlowTriggerService`（spec §3.1 逐字）：
  - `Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson, int source, string idempotencyKey, CancellationToken ct);`
  - `Task<int> ScanTimersOnceAsync(CancellationToken ct);`（实现类测试重载 `ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)`）
- 幂等键口径（spec §2.2）：timer=`$"{trigger.Id}:{dueUtc:O}"`；event=`$"{eventId}:{trigger.Id}"`；message=`Idempotency-Key` 头；手动试发=`$"manual:{Guid.NewGuid():N}"`。
- `WfCronHelper { static bool IsValid(string?); static DateTime? NextUtc(string cron, DateTime afterUtc); static IReadOnlyList<DateTime> PreviewUtc(string cron, DateTime fromUtc, int count); }`
- `IWfTriggerBridgeHook`：
  - `Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName);`（业务入口，写 outbox 台账）
  - `Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName);`（dispatcher 重放入口，不再写新 outbox 行）
- `WfTriggerBridgeResult { bool Success; int MatchedCount; int FiredCount; string? Message; static Ok(int matched, int fired); static Skipped(string); static Failed(string); }`
- `WfTriggerEventPayload(string EventKey, string EventId, string PayloadJson, string? UserName)`（record，outbox 负载契约）。
- `WfTriggerVarsMapper { static string MapVars(Dictionary<string,string>? varsMap, string payloadJson); static string FilterBySchema(string bodyJson, IReadOnlyList<string>? schema); }`
- `WfApiKeyHelper { static string NewRawKey(); static string HashOf(string raw); static bool Verify(string raw, string? storedHash); }`
- `WfTriggerConfig`：`ParseTimer(string)→WfTimerTriggerConfig{Cron,VarsJson}` / `ParseEvent(string)→WfEventTriggerConfig{VarsMap}` / `ParseMessage(string)→WfMessageTriggerConfig{VarsSchema}`。
- 常量（`FlowTriggerService`）：`RecoveryGrace = TimeSpan.FromMinutes(2)`（补跑宽限）、`BatchSize = 50`、`Trunc` 截 1000。
- 错误码：`E-WF-022`（配置无效：cron/eventKey/varsMap/StarterUserId）/ `E-WF-023`（目标流程不可发起）/ `E-WF-024`（运行时发起失败，写 TriggerFire.Error）。message 端点 401/404/400 走 HTTP 语义不占 E-WF 码。
- FireAsync 撞键语义（spec §3.1 引申，全计划统一）：既有行 `InstanceId != null` → `Ok(instanceId, replayed:true)`（幂等成功非错误）；既有行 `InstanceId == null`（占坑未完成**或**上次失败）→ 补跑第二段（成功回填并清 Error / 失败覆写 Error）。timer 补跑扫描只捡 `Error==null` 的占坑行（spec §3.2 原文）；Error 行的重试机会来自 event outbox 重放与 message 客户端重试。


## 附: API key/过滤器先例锚点
| API key 先例 | **无现成 API key 基建**，但三处可复刻：`TwoFactorService.cs:137-149` `Sha256Hex` + `FixedTimeEquals`（`CryptographicOperations.FixedTimeEquals`，先比长度）；`RefreshTokenService.cs:31-33` `NewRaw()`=32 字节随机 Base64Url + `HashOf()`=SHA-256 hex 入库（库内只存哈希）+ 查库 `IgnoreQueryFilters()`（令牌即凭证跨租户定位）；`RequirePlatformAdminAttribute.cs`＝自定义 `IAsyncAuthorizationFilter` 先例（特性经 `RequestServices` 服务定位取依赖，失败设 `context.Result` 短路）。 |
