using System.Net;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

internal sealed class RmaOperationsHttpFactAttribute : FactAttribute
{
    internal const string Variable = "CP6_RMA_OPERATIONS_FIXTURE";
    public RmaOperationsHttpFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) is null)
            Skip = "Run the owned PostgreSQL RMA original operation fixture.";
    }
}

public sealed partial class RmaTenantReadHttpPostgresTests
{
    private static string OriginalPath(Guid id) => $"{Path}/operations/{id:D}";
    private static string ClosePath(Guid id) => OriginalPath(id) + "/close";

    [RmaOperationsHttpFact]
    public async Task Original_operations_use_current_authority_and_serialize_close_with_late_writes()
    {
        var fixture = JsonSerializer.Deserialize<Fixture>(Environment.GetEnvironmentVariable(RmaOperationsHttpFactAttribute.Variable)!)!;
        var id = Guid.NewGuid();
        var create = Guid.NewGuid();
        var a = await Seed(fixture.Connection, fixture.A.TenantId, id, "operations-A", shared: false, seedApplication: false);
        var b = await Seed(fixture.Connection, fixture.B.TenantId, id, "operations-B", shared: false, seedApplication: false);
        var other = await GrantCancelAndAddOtherActor(fixture.Connection, a);
        await using var host = await Start(fixture.Connection, Configuration(fixture, draft: true, submit: true, operations: true));
        var aToken = await Login(host, a);
        var bToken = await Login(host, b);
        var otherToken = await Login(host, other);
        var aDraft = await DraftCommand(host, aToken, a, id, create, "A 原操作");
        var bDraft = await DraftCommand(host, bToken, b, id, create, "B 原操作");
        using var aCreation = await Request(host, aToken, HttpMethod.Post, Path, aDraft);
        using var bCreation = await Request(host, bToken, HttpMethod.Post, Path, bDraft);
        await RequireDraftStatus(aCreation, HttpStatusCode.Created);
        await RequireDraftStatus(bCreation, HttpStatusCode.Created);
        var originalA = await DraftData<RmaApplicationResultV1>(aCreation);
        var originalB = await DraftData<RmaApplicationResultV1>(bCreation);
        await AssertOriginal(host, aToken, HttpMethod.Get, create, new(create, "COMMITTED", originalA));
        await AssertOriginal(host, bToken, HttpMethod.Post, create, new(create, "COMMITTED", originalB));
        await Expect(host, otherToken, HttpMethod.Get, OriginalPath(create), HttpStatusCode.Forbidden, "ORIGINAL_OPERATION_FORBIDDEN");
        await Expect(host, otherToken, HttpMethod.Post, ClosePath(create), HttpStatusCode.Forbidden, "ORIGINAL_OPERATION_FORBIDDEN");

        var draftKey = Guid.NewGuid();
        var lateDraft = aDraft with { OperationId = draftKey, ApplicationId = Guid.NewGuid() };
        await AssertOriginal(host, aToken, HttpMethod.Get, draftKey, new(draftKey, "UNKNOWN"));
        await Expect(host, bToken, HttpMethod.Post, ClosePath(draftKey), HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
        await AssertOriginal(host, bToken, HttpMethod.Get, draftKey, new(draftKey, "UNKNOWN"));
        await using var control = new NpgsqlConnection(fixture.Connection);
        await control.OpenAsync();

        var draftRace = await RaceAtOriginal(control, a.Tenant, fixture.A.Login, draftKey, "CLOSED",
            () => Request(host, aToken, HttpMethod.Post, ClosePath(draftKey)),
            () => Request(host, aToken, HttpMethod.Post, Path, lateDraft));
        using (var close = draftRace.First)
        using (var late = draftRace.Second)
        {
            await RequireDraftStatus(close, HttpStatusCode.OK);
            Assert.Equal(new RmaApplicationOperationV1(draftKey, "CLOSED"), await DraftData<RmaApplicationOperationV1>(close));
            await RequireDraftStatus(late, HttpStatusCode.Conflict);
            Assert.Contains("ORIGINAL_OPERATION_CLOSED", await late.Content.ReadAsStringAsync());
        }
        await AssertAggregateRows(control, a.Tenant, lateDraft.ApplicationId, 0, 0);
        await AssertRawClosed(control, a, draftKey);
        await AssertOriginal(host, aToken, HttpMethod.Get, draftKey, new(draftKey, "CLOSED"));
        await AssertOriginal(host, aToken, HttpMethod.Post, draftKey, new(draftKey, "CLOSED"));
        await AssertRawClosed(control, a, draftKey);
        await AssertOriginal(host, bToken, HttpMethod.Get, draftKey, new(draftKey, "UNKNOWN"));

        var submitKey = Guid.NewGuid();
        var submit = new RmaApplicationSubmitV1(submitKey, 1);
        var closeWins = await RaceAtOriginal(control, a.Tenant, fixture.A.Login, submitKey, "CLOSED",
            () => Request(host, aToken, HttpMethod.Post, ClosePath(submitKey)),
            () => SubmitRequest(host, aToken, id, submit, originalA.Etag));
        using (var close = closeWins.First)
        using (var late = closeWins.Second)
        {
            await RequireDraftStatus(close, HttpStatusCode.OK);
            await RequireDraftStatus(late, HttpStatusCode.Conflict);
            Assert.Contains("ORIGINAL_OPERATION_CLOSED", await late.Content.ReadAsStringAsync());
        }
        await AssertAggregateRows(control, a.Tenant, id, 1, 1);
        await AssertRawClosed(control, a, submitKey);
        await AssertOriginal(host, bToken, HttpMethod.Get, submitKey, new(submitKey, "UNKNOWN"));

        var submitWins = await RaceAtOriginal(control, b.Tenant, fixture.B.Login, submitKey, "COMMITTED",
            () => SubmitRequest(host, bToken, id, submit, originalB.Etag),
            () => Request(host, bToken, HttpMethod.Post, ClosePath(submitKey)));
        using (var commit = submitWins.First)
        using (var close = submitWins.Second)
        {
            await RequireDraftStatus(commit, HttpStatusCode.OK);
            await RequireDraftStatus(close, HttpStatusCode.OK);
            var result = await DraftData<RmaApplicationResultV1>(commit);
            Assert.Equal(new RmaApplicationOperationV1(submitKey, "COMMITTED", result),
                await DraftData<RmaApplicationOperationV1>(close));
            await AssertOriginal(host, bToken, HttpMethod.Get, submitKey, new(submitKey, "COMMITTED", result));
            var bOriginal = await ReadSubmission(fixture.Connection, b.Tenant, id, submitKey);
            Assert.Equal(MainOwnerOperationJournalV1.Encode(result), bOriginal.Result);
        }
        await AssertAggregateRows(control, b.Tenant, id, 1, 2);
        await AssertRawClosed(control, a, submitKey);

        await using (var db = Context(fixture.Connection, a.Tenant))
            await db.WmsRoleScopes.Where(row => row.RoleId == Role).ExecuteDeleteAsync();
        await Expect(host, aToken, HttpMethod.Get, OriginalPath(create), HttpStatusCode.Forbidden, "OBJECT_FORBIDDEN");
        await Expect(host, aToken, HttpMethod.Post, ClosePath(create), HttpStatusCode.Forbidden, "OBJECT_FORBIDDEN");
        await AssertOriginal(host, aToken, HttpMethod.Get, submitKey, new(submitKey, "CLOSED"));
        await using (var db = Context(fixture.Connection, a.Tenant))
            await db.Sys_RoleActions.Where(row => row.RoleId == Role && row.ActionCode == "query").ExecuteDeleteAsync();
        await Expect(host, aToken, HttpMethod.Get, OriginalPath(submitKey), HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
        await Expect(host, aToken, HttpMethod.Post, ClosePath(submitKey), HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
        var cancelOnly = Guid.NewGuid();
        await AssertOriginal(host, aToken, HttpMethod.Post, cancelOnly, new(cancelOnly, "CLOSED"));
        await AssertRawClosed(control, a, cancelOnly);
    }

    private static async Task<Example> GrantCancelAndAddOtherActor(string connection, Example example)
    {
        await using var db = Context(connection, example.Tenant);
        var menu = await db.Sys_Menus.SingleAsync(row => row.MenuKey == Menu && row.Enable);
        if (!await db.Sys_MenuActions.AnyAsync(row => row.MenuId == menu.MenuId && row.ActionCode == "cancel"))
            db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "cancel", ActionName = "cancel" });
        db.Sys_RoleActions.Add(new() { MenuId = menu.MenuId, RoleId = Role, ActionCode = "cancel" });
        var other = example with { UserName = "rma-operations-other", Password = "Rma!" + Guid.NewGuid().ToString("N") };
        db.Sys_Users.Add(new() { Id = Guid.NewGuid(), UserName = other.UserName, Enable = true, RoleId = Role,
            Password = new BCryptPasswordHasher(4).Hash(other.Password) });
        await db.SaveChangesAsync();
        return other;
    }

    private static async Task AssertOriginal(AuthenticationApiProcess host, string token, HttpMethod method,
        Guid id, RmaApplicationOperationV1 expected)
    {
        using var response = await Request(host, token, method, method == HttpMethod.Get ? OriginalPath(id) : ClosePath(id));
        await RequireDraftStatus(response, HttpStatusCode.OK);
        Assert.Equal(expected, await DraftData<RmaApplicationOperationV1>(response));
    }

    private static async Task AssertAggregateRows(NpgsqlConnection control, Guid tenant, Guid id, long heads, long versions)
    {
        var schema = "t_" + tenant.ToString("N");
        await using var select = new NpgsqlCommand($"""
            SELECT (SELECT count(*) FROM {schema}.heads WHERE id=@id),
                   (SELECT count(*) FROM {schema}.versions WHERE id=@id)
            """, control);
        select.Parameters.AddWithValue("id", id);
        await using var row = await select.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(heads, row.GetInt64(0));
        Assert.Equal(versions, row.GetInt64(1));
    }

    private static async Task AssertRawClosed(NpgsqlConnection control, Example example, Guid key)
    {
        await using var select = new NpgsqlCommand("""
            SELECT actor_id,contract,state,input_bytes,input_sha256,result_bytes,object_ids
            FROM cp6_main_owner_v1.operations
            WHERE tenant_id=@tenant AND owner='Main.Wms.RmaApplication.v1' AND operation_id=@key
            """, control);
        select.Parameters.AddWithValue("tenant", example.Tenant);
        select.Parameters.AddWithValue("key", key);
        await using var row = await select.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(example.Original.RequestedBy!.Value, row.GetGuid(0));
        Assert.Equal(RmaApplicationOwnerV1.Contract, row.GetString(1));
        Assert.Equal("CLOSED", row.GetString(2));
        Assert.True(row.IsDBNull(3));
        Assert.True(row.IsDBNull(4));
        Assert.Equal(MainOwnerOperationJournalV1.Encode(new RmaApplicationOperationV1(key, "CLOSED")), row.GetFieldValue<byte[]>(5));
        Assert.Empty(row.GetFieldValue<Guid[]>(6));
        Assert.False(await row.ReadAsync());
    }

    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RaceAtOriginal(
        NpgsqlConnection control, Guid tenant, string login, Guid operation, string state,
        Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        await using var setup = new NpgsqlCommand($"""
            CREATE FUNCTION public.w375_fixture_original_barrier() RETURNS trigger
            LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $fixture$
            BEGIN PERFORM pg_catalog.pg_advisory_xact_lock(375031200,1); RETURN NEW; END $fixture$;
            REVOKE ALL ON FUNCTION public.w375_fixture_original_barrier() FROM PUBLIC;
            CREATE TRIGGER w375_fixture_original_barrier BEFORE INSERT ON cp6_main_owner_v1.operations
              FOR EACH ROW WHEN (NEW.tenant_id='{tenant:D}' AND NEW.operation_id='{operation:D}' AND NEW.state='{state}')
              EXECUTE FUNCTION public.w375_fixture_original_barrier();
            SELECT pg_catalog.pg_advisory_lock(375031200,1);
            """, control);
        await setup.ExecuteNonQueryAsync();
        await using var release = new NpgsqlCommand("SELECT pg_catalog.pg_advisory_unlock(375031200,1)", control);
        var released = false;
        try
        {
            var firstRequest = first();
            var held = await WaitOriginalBlock(control, login, control.ProcessID, firstRequest);
            var lateRequest = second();
            var late = await WaitOriginalBlock(control, login, held, lateRequest);
            output.WriteLine($"Native RMA {state} ordinary PID {held} held auth root while ordinary PID {late} waited");
            await release.ExecuteNonQueryAsync();
            released = true;
            return (await firstRequest, await lateRequest);
        }
        finally
        {
            if (!released) await release.ExecuteNonQueryAsync();
            await using var cleanup = new NpgsqlCommand("""
                DROP TRIGGER w375_fixture_original_barrier ON cp6_main_owner_v1.operations;
                DROP FUNCTION public.w375_fixture_original_barrier();
                """, control);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static async Task<int> WaitOriginalBlock(NpgsqlConnection control, string login, int blocker, Task request)
    {
        await using var query = new NpgsqlCommand("""
            SELECT pid FROM pg_catalog.pg_stat_activity
            WHERE usename=@login AND @blocker=ANY(pg_catalog.pg_blocking_pids(pid))
            """, control);
        query.Parameters.AddWithValue("login", login);
        query.Parameters.AddWithValue("blocker", blocker);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!request.IsCompleted)
        {
            if (await query.ExecuteScalarAsync(limit.Token) is int pid) return pid;
            await Task.Delay(30, limit.Token);
        }
        throw new InvalidOperationException("The original operation completed before its actual PostgreSQL wait was observed.");
    }
}
