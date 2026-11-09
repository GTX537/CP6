using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

internal sealed class RmaSubmitHttpFactAttribute : FactAttribute
{
    internal const string Variable = "CP6_RMA_SUBMIT_FIXTURE";
    public RmaSubmitHttpFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) is null)
            Skip = "Run the owned PostgreSQL RMA submit fixture.";
    }
}

public sealed partial class RmaTenantReadHttpPostgresTests
{
    [RmaSubmitHttpFact]
    public async Task Submit_post_keeps_the_original_native_transaction_and_IfMatch_replay()
    {
        var fixture = JsonSerializer.Deserialize<Fixture>(Environment.GetEnvironmentVariable(RmaSubmitHttpFactAttribute.Variable)!)!;
        var id = Guid.NewGuid();
        var createKey = Guid.NewGuid();
        var a = await Seed(fixture.Connection, fixture.A.TenantId, id, "submit-A", shared: false, seedApplication: false);
        var b = await Seed(fixture.Connection, fixture.B.TenantId, id, "submit-B", shared: false, seedApplication: false);
        await using var host = await Start(fixture.Connection, Configuration(fixture, draft: true, submit: true));
        var aToken = await Login(host, a);
        var bToken = await Login(host, b);
        var aDraft = await DraftCommand(host, aToken, a, id, createKey, "A 原草稿");
        var bDraft = await DraftCommand(host, bToken, b, id, createKey, "B 原草稿");
        using var aCreate = await Request(host, aToken, HttpMethod.Post, Path, aDraft);
        using var bCreate = await Request(host, bToken, HttpMethod.Post, Path, bDraft);
        await RequireDraftStatus(aCreate, HttpStatusCode.Created);
        await RequireDraftStatus(bCreate, HttpStatusCode.Created);
        var draftEtag = aCreate.Headers.ETag!.ToString();
        Assert.Equal(draftEtag, bCreate.Headers.ETag!.ToString());

        var key = Guid.NewGuid();
        var command = new RmaApplicationSubmitV1(key, 1);
        var aBefore = await ReadSubmission(fixture.Connection, a.Tenant, id, key);
        var bBefore = await ReadSubmission(fixture.Connection, b.Tenant, id, key);
        await ExpectSubmit(host, aToken, id, command, null, HttpStatusCode.PreconditionRequired, "RMA_IF_MATCH_REQUIRED");
        await ExpectSubmit(host, aToken, id, command, "\"wrong\"", HttpStatusCode.PreconditionFailed, "RMA_APPLICATION_CHANGED");
        await SetShipmentQuantity(fixture.Connection, b.Tenant, bDraft.Lines.Single().OutboundLineId, 4m);
        await ExpectSubmit(host, bToken, id, command, draftEtag, HttpStatusCode.PreconditionFailed, "RMA_SOURCE_CHANGED");
        await SetShipmentQuantity(fixture.Connection, b.Tenant, bDraft.Lines.Single().OutboundLineId, 3m);
        await AssertUnchangedSubmission(fixture.Connection, a.Tenant, id, key, aBefore);
        await AssertUnchangedSubmission(fixture.Connection, b.Tenant, id, key, bBefore);

        await using var control = new NpgsqlConnection(fixture.Connection);
        await control.OpenAsync();
        await InjectSubmitJournalFailure(control, a.Tenant, id, key);
        try
        {
            await ExpectSubmit(host, aToken, id, command, draftEtag, HttpStatusCode.ServiceUnavailable, "RMA_TENANT_BINDING_UNAVAILABLE");
            await AssertUnchangedSubmission(fixture.Connection, a.Tenant, id, key, aBefore);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DROP TRIGGER w372_fixture_fail_journal ON cp6_main_owner_v1.operations;
                DROP FUNCTION public.w372_fixture_fail_journal();
                """, control);
            await cleanup.ExecuteNonQueryAsync();
        }

        var first = await Task.WhenAll(SubmitRequest(host, aToken, id, command, draftEtag),
            SubmitRequest(host, bToken, id, command, draftEtag));
        string submittedEtag;
        using (var aResponse = first[0])
        using (var bResponse = first[1])
        {
            await RequireDraftStatus(aResponse, HttpStatusCode.OK);
            await RequireDraftStatus(bResponse, HttpStatusCode.OK);
            submittedEtag = aResponse.Headers.ETag!.ToString();
            Assert.Equal(submittedEtag, bResponse.Headers.ETag!.ToString());
            Assert.NotEqual(draftEtag, submittedEtag);
        }

        var result = new RmaApplicationResultV1(key, id, 2, "SUBMITTED", submittedEtag);
        await AssertCommittedSubmission(fixture.Connection, a, id, command, draftEtag, aBefore, result);
        await AssertCommittedSubmission(fixture.Connection, b, id, command, draftEtag, bBefore, result);
        await ExpectSubmit(host, bToken, id, new(Guid.NewGuid(), 2), submittedEtag, HttpStatusCode.Conflict, "RMA_APPLICATION_NOT_DRAFT");

        var denied = new RmaApplicationSubmitV1(Guid.NewGuid(), 2);
        await using (var permissions = Context(fixture.Connection, a.Tenant))
        await using (var hold = await permissions.Database.BeginTransactionAsync())
        {
            await permissions.Database.ExecuteSqlRawAsync("SELECT cp6_authentication_v1.acquire()");
            await permissions.Sys_RoleActions.Where(row => row.RoleId == Role && row.ActionCode == "add").ExecuteDeleteAsync();
            var holder = await permissions.Database.SqlQueryRaw<int>("SELECT pg_catalog.pg_backend_pid() AS \"Value\"").SingleAsync();
            var pending = SubmitRequest(host, aToken, id, denied, submittedEtag);
            await RequireOrdinaryRootWait(control, fixture.A.Login, holder, pending);
            await hold.CommitAsync();
            using var refused = await pending;
            await RequireDraftStatus(refused, HttpStatusCode.Forbidden);
            Assert.Contains("ACTION_FORBIDDEN", await refused.Content.ReadAsStringAsync());
        }
        var afterDenial = await ReadSubmission(fixture.Connection, a.Tenant, id, denied.OperationId);
        Assert.Equal(2, afterDenial.Version);
        Assert.Equal(2L, afterDenial.VersionCount);
        Assert.Null(afterDenial.Input);
        using (var replay = await SubmitRequest(host, aToken, id, command, draftEtag))
        {
            await RequireDraftStatus(replay, HttpStatusCode.OK);
            Assert.Equal(submittedEtag, replay.Headers.ETag!.ToString());
            Assert.Equal(result, await DraftData<RmaApplicationResultV1>(replay));
        }
        await ExpectSubmit(host, aToken, id, command, submittedEtag, HttpStatusCode.Conflict, "ORIGINAL_OPERATION_INPUT_CONFLICT");
        await AssertCommittedSubmission(fixture.Connection, a, id, command, draftEtag, aBefore, result);

        using var current = await Request(host, aToken, HttpMethod.Get, $"{Path}/{id:D}");
        using var historical = await Request(host, aToken, HttpMethod.Get, $"{Path}/{id:D}?version=1");
        await RequireDraftStatus(current, HttpStatusCode.OK);
        await RequireDraftStatus(historical, HttpStatusCode.OK);
        Assert.Equal(submittedEtag, current.Headers.ETag!.ToString());
        Assert.Equal(draftEtag, historical.Headers.ETag!.ToString());
        Assert.Equal("1.12345678", (await DraftData<RmaApplicationViewV1>(current)).Lines.Single().RequestedQuantity);
        Assert.Equal("DRAFT", (await DraftData<RmaApplicationViewV1>(historical)).State);
        await Expect(host, aToken, HttpMethod.Post, $"{Path}/operations/{key:D}/close", HttpStatusCode.ServiceUnavailable,
            "RMA_TENANT_ENTRY_UNAVAILABLE");
    }

    private static async Task<HttpResponseMessage> SubmitRequest(AuthenticationApiProcess host, string token, Guid id,
        RmaApplicationSubmitV1 command, string? ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Path}/{id:D}/submit") { Content = JsonContent.Create(command) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await host.Send(request);
    }

    private static async Task ExpectSubmit(AuthenticationApiProcess host, string token, Guid id,
        RmaApplicationSubmitV1 command, string? ifMatch, HttpStatusCode status, string code)
    {
        using var response = await SubmitRequest(host, token, id, command, ifMatch);
        await RequireDraftStatus(response, status);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    private static async Task SetShipmentQuantity(string connection, Guid tenant, Guid line, decimal quantity)
    {
        await using var db = Context(connection, tenant);
        Assert.Equal(1, await db.OutboundOrderDetails.Where(row => row.Id == line)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ShippedQty, quantity)));
    }

    private sealed record SubmissionState(int Version, long VersionCount, string Draft, string Current,
        Guid? Actor, string? Contract, byte[]? Input, string? Digest, byte[]? Result, Guid[]? Objects);

    private static async Task<SubmissionState> ReadSubmission(string connection, Guid tenant, Guid id, Guid operation)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        var schema = "t_" + tenant.ToString("N");
        await using var select = new NpgsqlCommand($"""
            SELECT h.version,
              (SELECT count(*) FROM {schema}.versions v WHERE v.tenant_id=h.tenant_id AND v.id=h.id),
              v1.payload::text,latest.payload::text,j.actor_id,j.contract,j.input_bytes,j.input_sha256,j.result_bytes,j.object_ids
            FROM {schema}.heads h
            JOIN {schema}.versions v1 ON v1.tenant_id=h.tenant_id AND v1.id=h.id AND v1.version=1
            JOIN {schema}.versions latest ON latest.tenant_id=h.tenant_id AND latest.id=h.id AND latest.version=h.version
            LEFT JOIN cp6_main_owner_v1.operations j ON j.tenant_id=h.tenant_id AND j.owner='Main.Wms.RmaApplication.v1'
                AND j.operation_id=@operation
            WHERE h.id=@id;
            """, db);
        select.Parameters.AddWithValue("id", id);
        select.Parameters.AddWithValue("operation", operation);
        await using var row = await select.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        return new(row.GetInt32(0), row.GetInt64(1), row.GetString(2), row.GetString(3),
            row.IsDBNull(4) ? null : row.GetGuid(4), row.IsDBNull(5) ? null : row.GetString(5),
            row.IsDBNull(6) ? null : row.GetFieldValue<byte[]>(6), row.IsDBNull(7) ? null : row.GetString(7),
            row.IsDBNull(8) ? null : row.GetFieldValue<byte[]>(8), row.IsDBNull(9) ? null : row.GetFieldValue<Guid[]>(9));
    }

    private static async Task AssertUnchangedSubmission(string connection, Guid tenant, Guid id, Guid operation, SubmissionState before)
    {
        var current = await ReadSubmission(connection, tenant, id, operation);
        Assert.Equal(before, current);
        Assert.Equal(1, current.Version);
        Assert.Equal(1L, current.VersionCount);
        Assert.Null(current.Result);
    }

    private static async Task AssertCommittedSubmission(string connection, Example example, Guid id,
        RmaApplicationSubmitV1 command, string ifMatch, SubmissionState before, RmaApplicationResultV1 expected)
    {
        var current = await ReadSubmission(connection, example.Tenant, id, command.OperationId);
        Assert.Equal(before.Draft, current.Draft);
        Assert.Equal(2, current.Version);
        Assert.Equal(2L, current.VersionCount);
        var draft = JsonSerializer.Deserialize<RmaApplicationViewV1>(before.Draft)!;
        var submitted = JsonSerializer.Deserialize<RmaApplicationViewV1>(current.Current)!;
        Assert.NotNull(submitted.SubmittedAt);
        Assert.Equal(draft.Reason, submitted.Reason);
        Assert.Equal(draft.Remarks, submitted.Remarks);
        Assert.Equal(draft.Lines.Single().RequestedQuantity, submitted.Lines.Single().RequestedQuantity);
        Assert.Equal("SUBMITTED", submitted.State);
        Assert.Equal(example.Original.RequestedBy, current.Actor);
        Assert.Equal(RmaApplicationOwnerV1.Contract, current.Contract);
        var input = MainOwnerOperationJournalV1.Encode(new { Id = id, Command = command, IfMatch = ifMatch });
        Assert.Equal(input, current.Input);
        Assert.Equal(MainOwnerOperationJournalV1.Digest(input), current.Digest);
        Assert.Equal(MainOwnerOperationJournalV1.Encode(expected), current.Result);
        Assert.Equal(new[] { id }, current.Objects);
    }

    private static async Task InjectSubmitJournalFailure(NpgsqlConnection control, Guid tenant, Guid id, Guid operation)
    {
        var schema = "t_" + tenant.ToString("N");
        await using var ddl = new NpgsqlCommand($"""
            CREATE FUNCTION public.w372_fixture_fail_journal() RETURNS trigger
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fixture$
            BEGIN
              IF NOT EXISTS (SELECT FROM {schema}.heads WHERE id='{id:D}' AND version=2)
                 OR NOT EXISTS (SELECT FROM {schema}.versions WHERE id='{id:D}' AND version=2 AND state='SUBMITTED')
              THEN RAISE EXCEPTION 'W372_FIXTURE_SUBMISSION_MISSING'; END IF;
              RAISE EXCEPTION 'W372_FIXTURE_JOURNAL_FAILURE_AFTER_CAS_AND_VERSION' USING ERRCODE='23514';
            END $fixture$;
            REVOKE ALL ON FUNCTION public.w372_fixture_fail_journal() FROM PUBLIC;
            CREATE TRIGGER w372_fixture_fail_journal BEFORE INSERT ON cp6_main_owner_v1.operations
              FOR EACH ROW WHEN (NEW.tenant_id='{tenant:D}' AND NEW.operation_id='{operation:D}')
              EXECUTE FUNCTION public.w372_fixture_fail_journal();
            """, control);
        await ddl.ExecuteNonQueryAsync();
    }
}
