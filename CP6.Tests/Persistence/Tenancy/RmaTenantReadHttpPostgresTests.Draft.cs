using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

internal sealed class RmaDraftHttpFactAttribute : FactAttribute
{
    internal const string Variable = "CP6_RMA_DRAFT_FIXTURE";
    public RmaDraftHttpFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) is null)
            Skip = "Run the owned PostgreSQL RMA draft fixture.";
    }
}

public sealed partial class RmaTenantReadHttpPostgresTests
{
    private static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web);

    [RmaDraftHttpFact]
    public async Task Draft_post_uses_native_ordinary_transaction_and_recovers_the_original()
    {
        var fixture = JsonSerializer.Deserialize<Fixture>(Environment.GetEnvironmentVariable(RmaDraftHttpFactAttribute.Variable)!)!;
        var id = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var a = await Seed(fixture.Connection, fixture.A.TenantId, id, "draft-A", shared: false, seedApplication: false);
        var b = await Seed(fixture.Connection, fixture.B.TenantId, id, "draft-B", shared: false, seedApplication: false);
        await using var host = await Start(fixture.Connection, Configuration(fixture, draft: true));
        var aToken = await Login(host, a);
        var bToken = await Login(host, b);
        var aCommand = await DraftCommand(host, aToken, a, id, operation, "A 发货退回");
        var bCommand = await DraftCommand(host, bToken, b, id, operation, "B 发货退回");
        var initial = await Task.WhenAll(Request(host, aToken, HttpMethod.Post, Path, aCommand),
            Request(host, bToken, HttpMethod.Post, Path, bCommand));

        string etag;
        using (var first = initial[0])
        {
            await RequireDraftStatus(first, HttpStatusCode.Created);
            etag = first.Headers.ETag!.ToString();
            // The first caller loses its response body. Only the stored result can answer the retry.
        }
        using (var response = initial[1])
        {
            await RequireDraftStatus(response, HttpStatusCode.Created);
            Assert.Equal(etag, response.Headers.ETag!.ToString());
        }

        var originalResult = new RmaApplicationResultV1(operation, id, 1, "DRAFT", etag);
        await AssertCommittedDraft(fixture.Connection, a, aCommand, originalResult);
        await AssertCommittedDraft(fixture.Connection, b, bCommand, originalResult);
        await AssertHostedDraft(host, aToken, aCommand, etag);
        await AssertHostedDraft(host, bToken, bCommand, etag);

        var stale = aCommand with { ApplicationId = Guid.NewGuid(), OperationId = Guid.NewGuid(), SourceVersion = "stale" };
        await Expect(host, aToken, HttpMethod.Post, Path, HttpStatusCode.PreconditionFailed, "RMA_SOURCE_CHANGED", stale);
        await AssertEmptyDraft(fixture.Connection, a.Tenant, stale);

        var broken = aCommand with { ApplicationId = Guid.NewGuid(), OperationId = Guid.NewGuid() };
        await using var control = new NpgsqlConnection(fixture.Connection);
        await control.OpenAsync();
        await InjectDraftJournalFailure(control, a.Tenant, broken);
        try
        {
            using var response = await Request(host, aToken, HttpMethod.Post, Path, broken);
            await RequireDraftStatus(response, HttpStatusCode.ServiceUnavailable);
            await AssertEmptyDraft(fixture.Connection, a.Tenant, broken);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DROP TRIGGER w364_fixture_fail_journal ON cp6_main_owner_v1.operations;
                DROP FUNCTION public.w364_fixture_fail_journal();
                """, control);
            await cleanup.ExecuteNonQueryAsync();
        }

        var denied = aCommand with { ApplicationId = Guid.NewGuid(), OperationId = Guid.NewGuid() };
        await using (var permissions = Context(fixture.Connection, a.Tenant))
        await using (var hold = await permissions.Database.BeginTransactionAsync())
        {
            await permissions.Database.ExecuteSqlRawAsync("SELECT cp6_authentication_v1.acquire()");
            await permissions.Sys_RoleActions.Where(row => row.RoleId == Role && row.ActionCode == "add").ExecuteDeleteAsync();
            var holder = await permissions.Database.SqlQueryRaw<int>("SELECT pg_catalog.pg_backend_pid() AS \"Value\"").SingleAsync();
            var request = Request(host, aToken, HttpMethod.Post, Path, denied);
            await RequireOrdinaryRootWait(control, fixture.A.Login, holder, request);
            await hold.CommitAsync();
            using var response = await request;
            await RequireDraftStatus(response, HttpStatusCode.Forbidden);
            Assert.Contains("ACTION_FORBIDDEN", await response.Content.ReadAsStringAsync());
        }
        await AssertEmptyDraft(fixture.Connection, a.Tenant, denied);

        using (var replay = await Request(host, aToken, HttpMethod.Post, Path, aCommand))
        {
            await RequireDraftStatus(replay, HttpStatusCode.Created);
            Assert.Equal(etag, replay.Headers.ETag!.ToString());
            Assert.Equal(originalResult, await DraftData<RmaApplicationResultV1>(replay));
        }
        await Expect(host, aToken, HttpMethod.Post, Path, HttpStatusCode.Conflict, "ORIGINAL_OPERATION_INPUT_CONFLICT",
            aCommand with { Reason = "changed" });
        await AssertCommittedDraft(fixture.Connection, a, aCommand, originalResult);
        await AssertHostedDraft(host, bToken, bCommand, etag);
        await Expect(host, aToken, HttpMethod.Post, $"{Path}/{id:D}/submit", HttpStatusCode.ServiceUnavailable,
            "RMA_TENANT_ENTRY_UNAVAILABLE", new RmaApplicationSubmitV1(Guid.NewGuid(), 1));
        await Expect(host, aToken, HttpMethod.Post, $"{Path}/operations/{operation:D}/close", HttpStatusCode.ServiceUnavailable,
            "RMA_TENANT_ENTRY_UNAVAILABLE");
    }

    private static async Task<RmaApplicationCreateV1> DraftCommand(AuthenticationApiProcess host, string token,
        Example example, Guid id, Guid operation, string reason)
    {
        using var response = await Request(host, token, HttpMethod.Get, $"{Path}/sources/{example.OutboundId:D}");
        await RequireDraftStatus(response, HttpStatusCode.OK);
        var source = await DraftData<RmaApplicationSourceV1>(response);
        return new(operation, id, source.Id, source.Version, reason, "RETURN_ONLY",
            [new(source.Lines.Single().Id, "1.12345678")], "原备注");
    }

    private static async Task<T> DraftData<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Deserialize<T>(DraftJson)!;

    private static async Task RequireDraftStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
            Assert.True(false, $"RMA draft expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task AssertHostedDraft(AuthenticationApiProcess host, string token, RmaApplicationCreateV1 command, string etag)
    {
        using var response = await Request(host, token, HttpMethod.Get, $"{Path}/{command.ApplicationId:D}");
        await RequireDraftStatus(response, HttpStatusCode.OK);
        var view = await DraftData<RmaApplicationViewV1>(response);
        Assert.Equal(etag, response.Headers.ETag!.ToString());
        Assert.Equal(command.OutboundId, view.OutboundId);
        Assert.Equal(command.Reason, view.Reason);
        Assert.Equal("1.12345678", view.Lines.Single().RequestedQuantity);
        Assert.Equal("NOT_GRANTED", view.Authorization);
    }

    private static async Task AssertCommittedDraft(string connection, Example example, RmaApplicationCreateV1 command,
        RmaApplicationResultV1 result)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        var schema = "t_" + example.Tenant.ToString("N");
        await using var select = new NpgsqlCommand($"""
            SELECT h.actor_id,h.outbound_id,h.version,v.state,v.payload::text,j.contract,j.state,j.input_bytes,
                   j.input_sha256,j.result_bytes,j.object_ids
            FROM {schema}.heads h JOIN {schema}.versions v USING(tenant_id,id,version)
            JOIN cp6_main_owner_v1.operations j ON j.tenant_id=h.tenant_id AND j.actor_id=h.actor_id
            WHERE h.id=@id AND j.operation_id=@operation AND j.owner='Main.Wms.RmaApplication.v1'
            """, db);
        select.Parameters.AddWithValue("id", command.ApplicationId);
        select.Parameters.AddWithValue("operation", command.OperationId);
        await using var row = await select.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(example.Original.RequestedBy!.Value, row.GetGuid(0));
        Assert.Equal(command.OutboundId, row.GetGuid(1));
        Assert.Equal(1, row.GetInt32(2));
        Assert.Equal("DRAFT", row.GetString(3));
        var payload = JsonSerializer.Deserialize<RmaApplicationViewV1>(row.GetString(4))!;
        Assert.Equal(command.Reason, payload.Reason);
        Assert.Equal(command.Lines.Single().Quantity, payload.Lines.Single().RequestedQuantity);
        Assert.Equal(RmaApplicationOwnerV1.Contract, row.GetString(5));
        Assert.Equal("COMMITTED", row.GetString(6));
        var input = MainOwnerOperationJournalV1.Encode(command);
        Assert.Equal(input, row.GetFieldValue<byte[]>(7));
        Assert.Equal(MainOwnerOperationJournalV1.Digest(input), row.GetString(8));
        Assert.Equal(MainOwnerOperationJournalV1.Encode(result), row.GetFieldValue<byte[]>(9));
        Assert.Equal(new[] { command.ApplicationId }, row.GetFieldValue<Guid[]>(10));
        Assert.False(await row.ReadAsync());
    }

    private static async Task AssertEmptyDraft(string connection, Guid tenant, RmaApplicationCreateV1 command)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        var schema = "t_" + tenant.ToString("N");
        await using var count = new NpgsqlCommand($"""
            SELECT (SELECT count(*) FROM {schema}.heads WHERE id=@id)
                 + (SELECT count(*) FROM {schema}.versions WHERE id=@id)
                 + (SELECT count(*) FROM cp6_main_owner_v1.operations
                    WHERE tenant_id=@tenant AND operation_id=@operation AND owner='Main.Wms.RmaApplication.v1')
            """, db);
        count.Parameters.AddWithValue("id", command.ApplicationId);
        count.Parameters.AddWithValue("tenant", tenant);
        count.Parameters.AddWithValue("operation", command.OperationId);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    private static async Task InjectDraftJournalFailure(NpgsqlConnection control, Guid tenant, RmaApplicationCreateV1 command)
    {
        var schema = "t_" + tenant.ToString("N");
        await using var ddl = new NpgsqlCommand($"""
            CREATE FUNCTION public.w364_fixture_fail_journal() RETURNS trigger
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fixture$
            BEGIN
              IF NOT EXISTS (SELECT FROM {schema}.heads WHERE id='{command.ApplicationId:D}')
                 OR NOT EXISTS (SELECT FROM {schema}.versions WHERE id='{command.ApplicationId:D}' AND version=1)
              THEN RAISE EXCEPTION 'W364_FIXTURE_AGGREGATE_MISSING'; END IF;
              RAISE EXCEPTION 'W364_FIXTURE_JOURNAL_FAILURE_AFTER_BOTH_ROWS' USING ERRCODE='23514';
            END $fixture$;
            REVOKE ALL ON FUNCTION public.w364_fixture_fail_journal() FROM PUBLIC;
            CREATE TRIGGER w364_fixture_fail_journal BEFORE INSERT ON cp6_main_owner_v1.operations
              FOR EACH ROW WHEN (NEW.tenant_id='{tenant:D}' AND NEW.operation_id='{command.OperationId:D}')
              EXECUTE FUNCTION public.w364_fixture_fail_journal();
            """, control);
        await ddl.ExecuteNonQueryAsync();
    }

    private async Task RequireOrdinaryRootWait(NpgsqlConnection control, string login, int holder, Task request)
    {
        await using var wait = new NpgsqlCommand("""
            SELECT pid FROM pg_catalog.pg_stat_activity
            WHERE usename=@login AND @holder=ANY(pg_catalog.pg_blocking_pids(pid))
              AND query LIKE '%lock_draft%'
            """, control);
        wait.Parameters.AddWithValue("login", login);
        wait.Parameters.AddWithValue("holder", holder);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        while (!request.IsCompleted)
        {
            if (await wait.ExecuteScalarAsync(limit.Token) is int ordinary)
            {
                output.WriteLine($"Native draft ordinary PID {ordinary} waited for auth root holder {holder}");
                return;
            }
            await Task.Delay(50, limit.Token);
        }
        Assert.Fail("The actual ordinary draft transaction did not wait for the original auth root.");
    }
}
