using System.Net;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record OwnedExportIntent(string SchemaVersion, string DatasetSchemaVersion, Guid TenantId,
        Guid ContactId, Guid RequestedBy, Guid ReaderSubjectId, string Dataset, string View, string[] FieldIds,
        string ReadPurpose, string RecordedThrough, string? OccurrenceFromUtc, string? OccurrenceToUtc, string Format);

    private sealed record OwnedExportRequest(string ContractVersion, Guid EventId, Guid RequestId, string SourceOwner,
        string Canonicalization, string IntentDigest, OwnedExportIntent Intent, string SourcePermissionWatermark);

    private static OwnedExportIntent IntentFor(Guid tenant, Guid contact, Guid subject) => new("CONTACT-HISTORY-INTENT/1", "CONTACT-HISTORY-SCHEMA/1",
        tenant, contact, subject, subject, "CONTACT_HISTORY", "AS_RECORDED", ExportFields, "SALES_FOLLOWUP", ExportCut,
        "2026-01-01T00:00:00.000Z", "2026-10-01T00:00:00.000Z", "CSV_VIEW");

    private static string ExportHash(byte[] body) => Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    private static OwnedExportRequest OriginalExport(OwnedExportIntent intent)
    {
        var canonical = ExportCanonical(intent);
        return new("CONTACT-HISTORY-EXPORT/1.1", Guid.NewGuid(), Guid.NewGuid(), "CRM_CONTACT", "CONTACT-EXPORT-CANON-1",
            ExportHash([.. "CONTACT-EXPORT-CANON-1\n"u8, .. canonical]), intent, ExportAccess);
    }

    private static byte[] ExportCanonical(object intent)
    {
        var fields = JsonSerializer.SerializeToElement(intent, ExportJson).EnumerateObject().ToDictionary(x => x.Name, x => x.Value);
        return JsonSerializer.SerializeToUtf8Bytes(new SortedDictionary<string, JsonElement>(fields, StringComparer.Ordinal),
            new JsonSerializerOptions(ExportJson) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static async Task SeedContactHistory(Harness h, Guid contact, bool large = false)
    {
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_contact_history_export_v11.sources(tenant_id,contact_id,privacy_revision,retained)
            VALUES({h.Tenant},{contact},1,true);
            INSERT INTO cp6_contact_history_export_v11.access(tenant_id,contact_id,subject_id,watermark,field_ids,purposes,
              can_read,can_request,can_download,valid_until,recorded_through,coverage)
            VALUES({h.Tenant},{contact},{h.User},{ExportAccess},{ExportFields},ARRAY['SALES_FOLLOWUP'],true,true,true,
              clock_timestamp()+interval '20 minutes','2026-11-01T00:00:00Z','COMPLETE');
            """);
        var first = Guid.NewGuid();
        await Add(first, 1, "2026-08-02T01:00:00Z", "2026-02-02T01:00:00Z", "旧修订");
        await Add(first, 2, "2026-09-29T08:30:00Z", "2026-02-02T01:00:00Z", "联系人,一号");
        await Add(first, 3, "2026-09-29T08:30:00.001Z", "2026-02-02T01:00:00Z", "登记截点之后");
        await Add(Guid.NewGuid(), 1, "2026-08-02T01:00:00Z", "2026-10-01T00:00:00Z", "发生区间右端点");
        await Add(Guid.NewGuid(), 1, "2026-08-02T01:00:00Z", "2026-02-02T01:00:00Z", "另一视图", "CORRECTED_INTERPRETATION");
        if (large)
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_contact_history_export_v11.history(tenant_id,contact_id,subject_id,watermark,purpose,view,
                  fact_id,revision,recorded_at,occurred_at,values_json)
                SELECT {h.Tenant},{contact},{h.User},{ExportAccess},'SALES_FOLLOWUP','AS_RECORDED',gen_random_uuid(),1,
                  '2026-02-02T01:00:00Z','2026-02-02T01:00:00Z',jsonb_build_object('contact.displayName',repeat('确',1800),'contact.lifecycle','ACTIVE')
                  FROM generate_series(1,240)
                """);

        Task<int> Add(Guid fact, long revision, string recorded, string occurred, string name, string view = "AS_RECORDED") =>
            h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_contact_history_export_v11.history(tenant_id,contact_id,subject_id,watermark,purpose,view,
                  fact_id,revision,recorded_at,occurred_at,values_json)
                VALUES({h.Tenant},{contact},{h.User},{ExportAccess},'SALES_FOLLOWUP',{view},{fact},{revision},{recorded}::timestamptz,
                  {occurred}::timestamptz,jsonb_build_object('contact.displayName',{name}::text,'contact.lifecycle','ACTIVE',
                    'relationships.periods','2026-02','activities.summary','模拟来电'))
                """);
    }

    private static JsonElement ExportOriginalBody(JsonElement envelope)
    {
        var bytes = envelope.GetProperty("body").GetBytesFromBase64();
        using var body = JsonDocument.Parse(bytes);
        return body.RootElement.Clone();
    }

    [Fact]
    public void Contact_history_G05_fixed_CRM_intent_vector_and_order()
    {
        var intent = IntentFor(Guid.Parse("b0000000-0000-4000-8000-000000000001"), Guid.Parse("c1111111-1111-4111-8111-111111111111"),
            Guid.Parse("a1111111-1111-4111-8111-111111111111"));
        var original = OriginalExport(intent);
        const string expected = "d2bb0c2ca661031e987b9890ccb8b8249cabc58e94c5cbaa1b936b81189ebc14";
        var main = JsonSerializer.Deserialize<CP6.Core.Services.Platform.ContactHistoryIntentV11>(
            JsonSerializer.SerializeToUtf8Bytes(intent, ExportJson), ExportJson)!;
        Assert.Equal(expected, original.IntentDigest);
        Assert.Equal(expected, CP6.Core.Services.Platform.ContactHistoryExportWireV11.IntentDigest(main));
        Assert.NotEqual(expected, CP6.Core.Services.Platform.ContactHistoryExportWireV11.IntentDigest(main with { OccurrenceToUtc = null }));
        Assert.NotEqual(expected, CP6.Core.Services.Platform.ContactHistoryExportWireV11.IntentDigest(main with { FieldIds = ExportFields.Reverse().ToArray() }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Contact_history_G05_original_allocation_survives_unread_reply_restart_and_concurrent_replay()
    {
        await using var h = await Seed();
        await using var other = await Seed();
        var contact = Guid.NewGuid();
        await SeedContactHistory(h, contact);
        var original = OriginalExport(IntentFor(h.Tenant, contact, h.User));
        await using var setup = await ExportServiceFixture.Create(h, other: other);
        int firstPid;
        await using (var first = await setup.Start())
        {
            firstPid = first.ProcessId;
            using var call = setup.Http("/requests", original);
            using var ignored = await first.SendHeaders(call);
            Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);
            // 接收头后丢弃实际响应体，以未取得原 Job 的调用方重启服务。
        }

        await using var next = await setup.Start();
        Assert.NotEqual(firstPid, next.ProcessId);
        var recovered = await setup.Json(next, $"/requests/{original.RequestId:D}");
        var duplicate = await Task.WhenAll(setup.Json(next, "/requests", original),
            setup.Json(next, "/requests", original with { Intent = original.Intent with { RecordedThrough = "2026-09-29T08:30:00Z" } }));
        Assert.All(duplicate, reply => Assert.Equal(recovered.GetProperty("body").GetString(), reply.GetProperty("body").GetString()));
        var ownerJob = ExportOriginalBody(recovered);
        Assert.Equal("ACCEPTED", ownerJob.GetProperty("state").GetString());
        Assert.Equal(original.IntentDigest, ownerJob.GetProperty("intentDigest").GetString());
        var recoveredIntent = ownerJob.GetProperty("intent").Deserialize<OwnedExportIntent>(ExportJson)!;
        Assert.Equal(ExportFields, recoveredIntent.FieldIds);
        Assert.Equal(original.Intent, recoveredIntent with { FieldIds = original.Intent.FieldIds });
        var newOriginal = OriginalExport(original.Intent);
        var initialRace = await Task.WhenAll(setup.Json(next, "/requests", newOriginal), setup.Json(next, "/requests", newOriginal));
        Assert.Equal(initialRace[0].GetProperty("body").GetString(), initialRace[1].GetProperty("body").GetString());
        var changed = OriginalExport(original.Intent with { FieldIds = ExportFields.Reverse().ToArray() }) with { RequestId = original.RequestId };
        await setup.Json(next, "/requests", changed, 409);
        await setup.Json(next, "/requests", original with { RequestId = Guid.NewGuid() }, 409);
        await setup.Json(next, "/requests", original with { IntentDigest = new string('0', 64) }, 409);
        await setup.Json(next, "/requests", OriginalExport(original.Intent with { TenantId = Guid.NewGuid() }), 403);
        await setup.Json(next, "/requests", original, 403, tenant: Guid.NewGuid());
        await setup.Json(next, "/requests", OriginalExport(original.Intent with { RequestedBy = Guid.NewGuid() }), 400);
        await setup.Json(next, "/dataset", tenant: other.Tenant, credential: other.Tenant);
        await setup.Json(next, $"/requests/{original.RequestId:D}", expected: 404, tenant: other.Tenant, credential: other.Tenant);
        await setup.Json(next, "/requests", original, 403, tenant: other.Tenant, credential: other.Tenant);

        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET can_request=false
             WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """);
        Assert.Equal(recovered.GetProperty("body").GetString(), (await setup.Json(next, "/requests", original)).GetProperty("body").GetString());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET can_request=true
             WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """);

        var denied = OriginalExport(original.Intent with { ReadPurpose = "REFUSÉ" });
        var submittedDecline = denied with { Intent = denied.Intent with { ReadPurpose = denied.Intent.ReadPurpose.Normalize(NormalizationForm.FormD) } };
        var refusal = await setup.Json(next, "/requests", submittedDecline);
        Assert.Equal(JsonValueKind.Null, refusal.GetProperty("body").ValueKind);
        var decision = refusal.GetProperty("declined");
        Assert.Equal("BEFORE_JOB_ALLOCATION", decision.GetProperty("finalityDomain").GetString());
        Assert.Equal(denied.IntentDigest, decision.GetProperty("intentDigest").GetString());
        var declineOriginal = (await ExportPublications(setup, next, denied.RequestId)).Single();
        Assert.Equal("DECLINED", declineOriginal.GetProperty("kind").GetString());
        Assert.Equal(decision.GetRawText(), ExportOriginalBody(declineOriginal).GetRawText());
        Assert.Equal(decision.GetProperty("evidenceRef").GetString(),
            (await CheckExportOriginal(setup, next, declineOriginal)).GetProperty("evidenceRef").GetString());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET purposes=ARRAY['SALES_FOLLOWUP','REFUSÉ']
             WHERE tenant_id={h.Tenant} AND contact_id={contact} AND subject_id={h.User}
            """);
        Assert.Equal(decision.GetRawText(), (await setup.Json(next, "/requests", denied)).GetProperty("declined").GetRawText());
        var count = await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_contact_history_export_v11.requests WHERE tenant_id={h.Tenant} AND job_id IS NOT NULL
            """).SingleAsync();
        Assert.Equal(2, count);
        var files = await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_contact_history_export_v11.artifacts WHERE tenant_id={h.Tenant}
            """).SingleAsync();
        Assert.Equal(2, files);
        Assert.Equal(0, await other.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_contact_history_export_v11.requests WHERE tenant_id={other.Tenant}
            """).SingleAsync());
        WriteExportEvidence("allocation-request.json", JsonSerializer.SerializeToUtf8Bytes(original, ExportJson));
        WriteExportEvidence("allocation-job-accepted.json", recovered.GetProperty("body").GetBytesFromBase64());
        WriteExportEvidence("decline-request.json", JsonSerializer.SerializeToUtf8Bytes(submittedDecline, ExportJson));
        WriteExportEvidence("decline-original.json", declineOriginal.GetProperty("body").GetBytesFromBase64());
        WriteExportEvidence("decline-proof.json", declineOriginal);
        WriteExportEvidence("decline-authority.json", await setup.Json(next, "/authority"));
        WriteExportEvidence("allocation-observations.json", new { firstPid, restartedPid = next.ProcessId,
            repeatedResultsIdentical = true, jobs = count, artifacts = files, terminalDeclines = 1 });
    }

    private static async Task<JsonElement[]> ExportPublications(ExportServiceFixture setup, CP6.Tests.Platform.AuthenticationApiProcess app, Guid request) =>
        (await setup.Json(app, $"/requests/{request:D}/results")).GetProperty("originals").EnumerateArray().ToArray();

    private static Task<JsonElement> CheckExportOriginal(ExportServiceFixture setup, CP6.Tests.Platform.AuthenticationApiProcess app,
        JsonElement envelope, int expected = 200) => setup.Json(app, "/proofs/authenticate", new
        {
            eventId = envelope.GetProperty("eventId").GetGuid(),
            bodySha256 = envelope.GetProperty("bodySha256").GetString()
        }, expected);

    private static void WriteExportEvidence(string name, object value) =>
        WriteExportEvidence(name, JsonSerializer.SerializeToUtf8Bytes(value, ExportJson));

    private static void WriteExportEvidence(string name, byte[] bytes)
    {
        var root = Environment.GetEnvironmentVariable("CP6_CONTACT_EXPORT_EVIDENCE");
        if (string.IsNullOrWhiteSpace(root)) return;
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, name), bytes);
    }
}
