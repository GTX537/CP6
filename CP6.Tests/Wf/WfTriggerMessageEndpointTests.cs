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
using CP6.WebApi.Controllers.Integration;
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
