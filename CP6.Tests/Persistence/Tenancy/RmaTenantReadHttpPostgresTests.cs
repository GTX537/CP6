using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Tests.Persistence.Tenancy;

internal sealed class RmaReadHttpFactAttribute : FactAttribute
{
    internal const string Variable = "CP6_RMA_READ_FIXTURE";
    public RmaReadHttpFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) is null)
            Skip = "Run the owned PostgreSQL RMA HTTP fixture.";
    }
}

public sealed partial class RmaTenantReadHttpPostgresTests(ITestOutputHelper output)
{
    private const string Path = "api/wms/rma/native-applications-v1";
    private const int Role = 4371;
    private const string Menu = "wms-rma";

    [RmaReadHttpFact]
    public async Task Real_bearer_current_and_historical_RMA_use_two_tenants_and_live_authority()
    {
        var fixture = JsonSerializer.Deserialize<Fixture>(Environment.GetEnvironmentVariable(RmaReadHttpFactAttribute.Variable)!)!;
        var app = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffff0");
        var a = await Seed(fixture.Connection, fixture.A.TenantId, app, "A", shared: false);
        var b = await Seed(fixture.Connection, fixture.B.TenantId, app, "B", shared: false);
        var listA = await SeedList(fixture.Connection, a, hiddenEvery: 3, otherActorEvery: 7);
        var listB = await SeedList(fixture.Connection, b, hiddenEvery: 4, otherActorEvery: 11);
        Assert.True(listA.Length > 100 && listB.Length > 100);
        await using var host = await Start(fixture.Connection, Configuration(fixture));
        var aToken = await Login(host, a);
        var bToken = await Login(host, b);
        await AssertView(host, aToken, $"{Path}/{app:D}", a.Current, masked: false);
        await AssertView(host, bToken, $"{Path}/{app:D}", b.Current, masked: false);
        await AssertView(host, aToken, $"{Path}/{app:D}?version=1", a.Original, masked: false);
        await AssertView(host, bToken, $"{Path}/{app:D}?version=1", b.Original, masked: false);
        await Expect(host, aToken, HttpMethod.Get, $"{Path}/{app:D}?version=3",
            HttpStatusCode.NotFound, "RMA_APPLICATION_VERSION_NOT_FOUND");
        await Expect(host, aToken, HttpMethod.Get, $"{Path}/{Guid.NewGuid():D}", HttpStatusCode.NotFound, "RMA_APPLICATION_NOT_FOUND");
        await Expect(host, aToken, HttpMethod.Get, $"{Path}/sources/{a.OutboundId:D}", HttpStatusCode.OK);

        await AssertList(host, aToken, 1, listA[..50]);
        await AssertList(host, bToken, 1, listB[..50]);
        await AssertList(host, aToken, 2, listA[50..100]);
        await AssertList(host, bToken, 2, listB[50..100]);
        await AssertList(host, aToken, 3, listA[100..]);
        await Expect(host, aToken, HttpMethod.Get, $"{Path}?page=0", HttpStatusCode.BadRequest, "RMA_PAGE_INVALID");
        await Expect(host, aToken, HttpMethod.Get, $"{Path}/operations/{Guid.NewGuid():D}",
            HttpStatusCode.ServiceUnavailable, "RMA_TENANT_ENTRY_UNAVAILABLE");
        var op = Guid.NewGuid();
        await Expect(host, aToken, HttpMethod.Post, Path, HttpStatusCode.ServiceUnavailable, "RMA_TENANT_ENTRY_UNAVAILABLE",
            new RmaApplicationCreateV1(op, app, a.OutboundId, "source", "reason", "RETURN_ONLY", [], null));
        await Expect(host, aToken, HttpMethod.Post, $"{Path}/{app:D}/submit",
            HttpStatusCode.ServiceUnavailable, "RMA_TENANT_ENTRY_UNAVAILABLE", new RmaApplicationSubmitV1(op, 2));
        await Expect(host, aToken, HttpMethod.Post, $"{Path}/operations/{op:D}/close",
            HttpStatusCode.ServiceUnavailable, "RMA_TENANT_ENTRY_UNAVAILABLE");

        await using (var wrongBinding = await Start(fixture.Connection, Configuration(fixture, swap: true)))
        {
            var token = await Login(wrongBinding, b);
            await Expect(wrongBinding, token, HttpMethod.Get, $"{Path}/{app:D}", HttpStatusCode.ServiceUnavailable, "RMA_TENANT_BINDING_UNAVAILABLE");
        }

        await using (var db = Context(fixture.Connection, a.Tenant))
        {
            foreach (var field in new[] { "ReturnReason", "Remarks", "OperatorCd" })
                db.Sys_RoleFieldPerms.Add(new() { RoleId = Role, ResourceKey = Menu, FieldName = field, Access = 3 });
            await db.SaveChangesAsync();
            await AssertView(host, aToken, $"{Path}/{app:D}", a.Current, masked: true);
            await AssertList(host, aToken, 2, listA[50..100], masked: true);
            await db.WmsRoleScopes.Where(x => x.RoleId == Role).ExecuteDeleteAsync();
            await AssertList(host, aToken, 1, []);
            await Expect(host, aToken, HttpMethod.Get, $"{Path}/{app:D}", HttpStatusCode.Forbidden, "OBJECT_FORBIDDEN");
            await db.Sys_RoleActions.Where(x => x.RoleId == Role && x.ActionCode == "query").ExecuteDeleteAsync();
            await Expect(host, aToken, HttpMethod.Get, $"{Path}/{app:D}", HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
            await Expect(host, aToken, HttpMethod.Get, Path, HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
        }
        await AssertView(host, bToken, $"{Path}/{app:D}", b.Current, masked: false);
        await AssertList(host, bToken, 2, listB[50..100]);
        await using (var db = Context(fixture.Connection, b.Tenant))
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_rma_tenant_v1.tenant_migrations
                SET state='Provisioning',operation_id=NULL,migration_id=NULL,script_sha256=NULL,result_json=NULL
                WHERE tenant_id={b.Tenant}
                """);
        await Expect(host, bToken, HttpMethod.Get, $"{Path}/{app:D}", HttpStatusCode.ServiceUnavailable, "RMA_TENANT_BINDING_UNAVAILABLE");

        var copied = await Seed(fixture.CloneConnection, fixture.A.TenantId, app, "CLONE", shared: false);
        await using (var clone = await Start(fixture.CloneConnection, Configuration(fixture)))
        {
            var token = await Login(clone, copied);
            await Expect(clone, token, HttpMethod.Get, $"{Path}/{app:D}", HttpStatusCode.ServiceUnavailable, "RMA_TENANT_BINDING_UNAVAILABLE");
        }

        var shared = await Seed(fixture.LegacyConnection, fixture.A.TenantId, app, "SHARED", shared: true);
        await using var legacy = await Start(fixture.LegacyConnection, null);
        var sharedToken = await Login(legacy, shared);
        await AssertView(legacy, sharedToken, $"{Path}/{app:D}", shared.Current, masked: false);
        await AssertList(legacy, sharedToken, 1, [shared.Current]);
    }

    private async Task<AuthenticationApiProcess> Start(string connection, Dictionary<string, string>? config)
    {
        var host = await AuthenticationApiProcess.Start(connection, config);
        output.WriteLine($"Owned RMA HTTP process {host.ProcessId} at {host.Origin}");
        return host;
    }

    private static Dictionary<string, string> Configuration(Fixture fixture, bool swap = false, bool draft = false, bool submit = false)
    {
        var config = new Dictionary<string, string>
        {
            ["RmaReadTenancy__Enabled"] = "true",
            ["RmaReadTenancy__InstallationId"] = fixture.InstallationId.ToString("D"),
            ["RmaReadTenancy__Revision"] = "rma-http-fixture",
            ["RmaReadTenancy__SslMode"] = "Disable"
        };
        if (draft) config["RmaReadTenancy__DraftEnabled"] = "true";
        if (submit) config["RmaReadTenancy__SubmitEnabled"] = "true";
        var logins = swap ? new[] { fixture.B, fixture.A } : [fixture.A, fixture.B];
        var ids = new[] { fixture.A.TenantId, fixture.B.TenantId };
        for (var i = 0; i < ids.Length; i++)
        {
            var prefix = $"RmaReadTenancy__Tenants__{i}__";
            config[prefix + "TenantId"] = ids[i].ToString("D");
            config[prefix + "Login"] = logins[i].Login;
            config[prefix + "Password"] = logins[i].Password;
            config[prefix + "CredentialVersion"] = "test-v1";
        }
        return config;
    }

    private static async Task<string> Login(AuthenticationApiProcess host, Example example)
    {
        using var response = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { example.UserName, example.Password, TenantCode = example.Tenant.ToString("N") })
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Real RMA fixture login returned {(int)response.StatusCode}.");
        return response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("cp6_at=", StringComparison.Ordinal))
            .Split(';', 2)[0][7..];
    }

    private static async Task<HttpResponseMessage> Request(AuthenticationApiProcess host, string token, HttpMethod method,
        string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await host.Send(request);
    }

    private static async Task AssertView(AuthenticationApiProcess host, string token, string path,
        RmaApplicationViewV1 expected, bool masked)
    {
        using var response = await Request(host, token, HttpMethod.Get, path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"RMA GET expected 200, got {(int)response.StatusCode}: {body}");
        Assert.Equal(expected.Etag, response.Headers.ETag?.ToString());
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(body);
        var actual = json.RootElement.GetProperty("data").Deserialize<RmaApplicationViewV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.OutboundId, actual.OutboundId);
        Assert.Equal(expected.OutboundNumber, actual.OutboundNumber);
        Assert.Equal(expected.Lines.Single().RequestedQuantity, actual.Lines.Single().RequestedQuantity);
        Assert.Equal(expected.Etag, actual.Etag);
        Assert.Equal(masked ? null : expected.Reason, actual.Reason);
        Assert.Equal(masked ? null : expected.Remarks, actual.Remarks);
        Assert.Equal(masked ? null : expected.RequestedBy, actual.RequestedBy);
    }

    private static async Task Expect(AuthenticationApiProcess host, string token, HttpMethod method, string path,
        HttpStatusCode status, string? code = null, object? body = null)
    {
        using var response = await Request(host, token, method, path, body);
        Assert.Equal(status, response.StatusCode);
        if (code is not null) Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertList(AuthenticationApiProcess host, string token, int page,
        IReadOnlyList<RmaApplicationViewV1> expected, bool masked = false)
    {
        using var response = await Request(host, token, HttpMethod.Get, $"{Path}?page={page}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"RMA list page {page} expected 200, got {(int)response.StatusCode}: {body}");
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(body);
        var actual = json.RootElement.GetProperty("data").Deserialize<RmaApplicationViewV1[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(expected.Select(v => v.Id), actual.Select(v => v.Id));
        Assert.Equal(expected.Select(v => v.Version), actual.Select(v => v.Version));
        Assert.Equal(expected.Select(v => v.OutboundId), actual.Select(v => v.OutboundId));
        Assert.Equal(expected.Select(v => masked ? null : v.RequestedBy), actual.Select(v => v.RequestedBy));
        Assert.Equal(expected.Select(v => masked ? null : v.Reason), actual.Select(v => v.Reason));
        Assert.Equal(expected.Select(v => masked ? null : v.Remarks), actual.Select(v => v.Remarks));
    }

    private static async Task<RmaApplicationViewV1[]> SeedList(string connection, Example example, int hiddenEvery, int otherActorEvery)
    {
        await using var db = Context(connection, example.Tenant);
        await db.Sys_RoleDataScopes.Where(scope => scope.RoleId == Role && scope.ResourceKey == Menu)
            .ExecuteUpdateAsync(set => set.SetProperty(scope => scope.ScopeType, 1));
        var hidden = new OutboundOrder
        {
            Id = Guid.NewGuid(), OutboundNo = example.Current.OutboundNumber + "-HIDDEN", OutboundType = OutboundType.Shipping,
            Status = OutboundOrderStatus.Completed, CustomerCd = "C1", WarehouseCd = "W9", Creator = example.UserName
        };
        db.OutboundOrders.Add(hidden);
        await db.SaveChangesAsync();

        var foreignActor = Guid.NewGuid();
        var rows = Enumerable.Range(1, 200).Select(i =>
        {
            var actor = i % otherActorEvery == 0 ? foreignActor : example.Original.RequestedBy!.Value;
            var source = i % hiddenEvery == 0 ? hidden.Id : example.OutboundId;
            var version = i % 2 + 1;
            return (version == 1 ? example.Original : example.Current) with
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{i:x12}"), RequestedBy = actor, OutboundId = source,
                OutboundNumber = source == hidden.Id ? hidden.OutboundNo : example.Current.OutboundNumber
            };
        }).ToArray();
        var history = rows.Select(row => row with { Version = 1, State = "DRAFT", SubmittedAt = null })
            .Concat(rows.Where(row => row.Version == 2)).ToArray();

        var schema = "t_" + example.Tenant.ToString("N");
        await using var raw = new NpgsqlConnection(connection);
        await raw.OpenAsync();
        await using var tx = await raw.BeginTransactionAsync();
        await using var seed = new NpgsqlCommand($"""
            INSERT INTO "{schema}".heads (tenant_id,id,actor_id,outbound_id,version)
            SELECT @tenant,id,actor,outbound,version
            FROM unnest(@ids,@actors,@outbounds,@versions) AS head(id,actor,outbound,version);
            INSERT INTO "{schema}".versions (tenant_id,id,version,state,payload)
            SELECT @tenant,id,version,state,CAST(payload AS jsonb)
            FROM unnest(@history_ids,@history_versions,@states,@payloads) AS version(id,version,state,payload);
            """, raw, tx);
        seed.Parameters.AddWithValue("tenant", example.Tenant);
        seed.Parameters.AddWithValue("ids", rows.Select(row => row.Id).ToArray());
        seed.Parameters.AddWithValue("actors", rows.Select(row => row.RequestedBy!.Value).ToArray());
        seed.Parameters.AddWithValue("outbounds", rows.Select(row => row.OutboundId).ToArray());
        seed.Parameters.AddWithValue("versions", rows.Select(row => row.Version).ToArray());
        seed.Parameters.AddWithValue("history_ids", history.Select(row => row.Id).ToArray());
        seed.Parameters.AddWithValue("history_versions", history.Select(row => row.Version).ToArray());
        seed.Parameters.AddWithValue("states", history.Select(row => row.State).ToArray());
        seed.Parameters.AddWithValue("payloads", history.Select(row => JsonSerializer.Serialize(row)).ToArray());
        await seed.ExecuteNonQueryAsync();
        await tx.CommitAsync();

        return rows.Where(row => row.OutboundId == example.OutboundId && row.RequestedBy == example.Original.RequestedBy)
            .Append(example.Current).ToArray();
    }

    private static CP6Context Context(string connection, Guid tenant)
    {
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),
            new DatabaseOptions(DatabaseProvider.PostgreSql), connection);
        return new CP6Context(options.Options, new TenantContext { CurrentTenantId = tenant });
    }

    private static async Task<Example> Seed(string connection, Guid tenant, Guid id, string marker, bool shared,
        bool seedApplication = true)
    {
        await using var db = Context(connection, tenant);
        var password = "Rma!" + Guid.NewGuid().ToString("N");
        var user = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = "rma-" + marker, RoleId = Role, Enable = true,
            Password = new BCryptPasswordHasher(4).Hash(password)
        };
        db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = marker });
        db.Sys_Roles.Add(new() { RoleId = Role, RoleName = "RMA read fixture" });
        db.Sys_Users.Add(user);
        var menu = await db.Sys_Menus.SingleAsync(x => x.MenuKey == Menu && x.Enable);
        foreach (var action in new[] { "query", "add" })
        {
            if (!await db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == action))
                db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            db.Sys_RoleActions.Add(new() { MenuId = menu.MenuId, RoleId = Role, ActionCode = action });
        }
        db.Sys_RoleDataScopes.Add(new() { RoleId = Role, ResourceKey = Menu, ScopeType = 5 });
        db.WmsRoleScopes.Add(new() { RoleId = Role, WarehouseCd = "W1" });
        var outbound = new OutboundOrder
        {
            Id = Guid.NewGuid(), OutboundNo = "W356-" + marker, OutboundType = OutboundType.Shipping,
            Status = OutboundOrderStatus.Completed, CustomerCd = "C1", WarehouseCd = "W1", Creator = user.UserName
        };
        var line = new OutboundOrderDetail
        {
            Id = Guid.NewGuid(), OutboundNo = outbound.OutboundNo, LineNo = 1, ProductCd = "P1", RequiredQty = 3m,
            AllocatedQty = 3m, ShippedQty = 3m, ShipTxnNo = "SHIP-" + marker, UnitCd = "EA", LocationCd = "L1", Creator = user.UserName
        };
        db.OutboundOrders.Add(outbound);
        db.OutboundOrderDetails.Add(line);
        await db.SaveChangesAsync();

        var original = new RmaApplicationViewV1(id, 1, "DRAFT", user.Id, DateTimeOffset.UtcNow, outbound.Id,
            outbound.OutboundNo, outbound.CustomerCd, "fixture-source", marker + "-reason", "RETURN_ONLY",
            [new(new(line.Id, 1, "P1", "3.00000000", "EA", "W1", "L1", null, line.ShipTxnNo!), "1.12345678")],
            marker + "-remarks", null);
        var current = original with { Version = 2, State = "SUBMITTED", SubmittedAt = DateTimeOffset.UtcNow };

        var example = new Example(tenant, user.UserName, password, outbound.Id, original, current);
        if (!seedApplication) return example;

        var schema = shared ? "cp6_rma_application_v1" : "t_" + tenant.ToString("N");
        await using var raw = new NpgsqlConnection(connection);
        await raw.OpenAsync();
        await using var tx = await raw.BeginTransactionAsync();
        await using var seed = new NpgsqlCommand($"""
            INSERT INTO "{schema}".heads (tenant_id,id,actor_id,outbound_id,version) VALUES (@tenant,@id,@actor,@outbound,2);
            INSERT INTO "{schema}".versions (tenant_id,id,version,state,payload)
                VALUES (@tenant,@id,1,'DRAFT',CAST(@original AS jsonb)),(@tenant,@id,2,'SUBMITTED',CAST(@current AS jsonb));
            """, raw, tx);
        seed.Parameters.AddWithValue("tenant", tenant);
        seed.Parameters.AddWithValue("id", id);
        seed.Parameters.AddWithValue("actor", user.Id);
        seed.Parameters.AddWithValue("outbound", outbound.Id);
        seed.Parameters.AddWithValue("original", JsonSerializer.Serialize(original));
        seed.Parameters.AddWithValue("current", JsonSerializer.Serialize(current));
        await seed.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return example;
    }

    private sealed record Fixture(string Connection, string CloneConnection, string LegacyConnection, Guid InstallationId, LoginBinding A, LoginBinding B);
    private sealed record LoginBinding(Guid TenantId, string Login, string Password);
    private sealed record Example(Guid Tenant, string UserName, string Password, Guid OutboundId, RmaApplicationViewV1 Original, RmaApplicationViewV1 Current);
}
