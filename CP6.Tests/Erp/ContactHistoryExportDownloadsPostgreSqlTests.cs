using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record ExportReadSubject(Guid TenantId, Guid ReaderSubjectId, Guid ContactId, string ReadPurpose,
        string[] FieldIds, string CurrentAccessEvidence);
    private sealed record ExportReadArtifact(Guid RequestId, string JobId, string ArtifactId, string ArtifactVersion, string IntentDigest,
        string ContentSha256, string Bytes, string Format);
    private sealed record ExportReadBinding(ExportReadSubject Subject, ExportReadArtifact Artifact);

    private static ExportReadBinding ExportRead(OwnedExportRequest request, JsonElement file) => new(
        new(request.Intent.TenantId, request.Intent.ReaderSubjectId, request.Intent.ContactId, request.Intent.ReadPurpose,
            request.Intent.FieldIds, request.SourcePermissionWatermark),
        new(request.RequestId, file.GetProperty("jobId").GetString()!, file.GetProperty("artifactId").GetString()!,
            file.GetProperty("artifactVersion").GetString()!, request.IntentDigest, file.GetProperty("sha256").GetString()!,
            file.GetProperty("bytes").GetString()!, request.Intent.Format));

    [CurrentPermissionPostgresFact]
    public async Task Contact_history_G05_download_binds_exact_bytes_and_rechecks_each_privacy_condition_before_output()
    {
        await using var h = await Seed();
        var contact = Guid.NewGuid();
        await SeedContactHistory(h, contact);
        await using var setup = await ExportServiceFixture.Create(h);
        await using var app = await setup.Start();
        var original = OriginalExport(IntentFor(h.Tenant, contact, h.User));
        var receipt = await setup.Json(app, "/requests", original);
        var publications = await ExportPublications(setup, app, original.RequestId);
        var ready = ExportOriginalBody(publications[3]);
        var binding = ExportRead(original, ready);
        var uri = $"/requests/{original.RequestId:D}/artifacts";

        // 采用 CRM 消费方的顺序，在申请下载许可前读取时钟。
        var observed = DateTimeOffset.UtcNow;
        var permit = await setup.Json(app, "/permits", binding);
        Assert.Equal(JsonSerializer.Serialize(binding, ExportJson), permit.GetProperty("binding").GetRawText());
        var from = permit.GetProperty("validFromUtc").GetDateTimeOffset();
        var until = permit.GetProperty("validUntilUtc").GetDateTimeOffset();
        Assert.True(from <= observed && observed < until);
        Assert.InRange(until - from, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6));
        var bytes = await GetExportBytes(setup, app, permit, binding.Artifact);
        var expected = string.Join(',', ExportFields) + "\r\n\"联系人,一号\",ACTIVE,2026-02,模拟来电\r\n";
        Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
        Assert.Equal(bytes, await GetExportBytes(setup, app, permit, binding.Artifact));
        var interpretation = OriginalExport(original.Intent with { View = "CORRECTED_INTERPRETATION" });
        await setup.Json(app, "/requests", interpretation);
        var corrected = ExportRead(interpretation, ExportOriginalBody((await ExportPublications(setup, app, interpretation.RequestId))[3]));
        var correctedBytes = await GetExportBytes(setup, app, await setup.Json(app, "/permits", corrected), corrected.Artifact);
        Assert.Equal(Encoding.UTF8.GetBytes(string.Join(',', ExportFields) + "\r\n另一视图,ACTIVE,2026-02,模拟来电\r\n"), correctedBytes);

        await setup.Json(app, "/permits", binding with { Artifact = binding.Artifact with { JobId = Guid.NewGuid().ToString("D") } }, 403);
        await setup.Json(app, "/permits", binding with { Artifact = binding.Artifact with { ArtifactVersion = "unknown-version" } }, 404);
        await setup.Json(app, "/permits", binding with { Artifact = binding.Artifact with { ContentSha256 = new string('0', 64) } }, 403);
        await setup.Json(app, "/permits", binding with { Artifact = binding.Artifact with { Bytes = "1" } }, 403);
        await setup.Json(app, "/permits", binding with { Artifact = binding.Artifact with { Format = "PDF" } }, 400);
        await setup.Json(app, "/permits", binding with { Subject = binding.Subject with { ReaderSubjectId = Guid.NewGuid() } }, 403);
        await setup.Json(app, "/permits", binding with { Subject = binding.Subject with { CurrentAccessEvidence = "stale" } }, 403);
        await GetEmptyDownload(setup, app, permit, tenant: Guid.NewGuid());

        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET field_ids=ARRAY['contact.displayName']
             WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """);
        await setup.Json(app, "/permits", binding, 403);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET field_ids={ExportFields},purposes=ARRAY['OTHER_PURPOSE']
             WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """);
        await setup.Json(app, "/permits", binding, 403);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET purposes=ARRAY['SALES_FOLLOWUP']
             WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """);

        var expiryPermit = await setup.Json(app, "/permits", binding);
        var wait = expiryPermit.GetProperty("validUntilUtc").GetDateTimeOffset() - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait + TimeSpan.FromMilliseconds(100));
        var expiryBytes = await GetEmptyDownload(setup, app, expiryPermit);
        var revokePermit = await setup.Json(app, "/permits", binding);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET can_download=false WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """);
        var revokeBytes = await GetEmptyDownload(setup, app, revokePermit);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET can_download=true WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """);
        var restoredOldPermitBytes = await GetEmptyDownload(setup, app, revokePermit);

        var redact = new { artifactId = binding.Artifact.ArtifactId, artifactVersion = binding.Artifact.ArtifactVersion, expectedRevision = "1" };
        var redaction = await setup.Json(app, uri + "/redact", redact);
        Assert.Equal(redaction.GetProperty("eventId").GetGuid(), (await setup.Json(app, uri + "/redact", redact)).GetProperty("eventId").GetGuid());
        var redacted = ExportOriginalBody(redaction);
        var redactedBinding = ExportRead(original, redacted);
        Assert.Equal(binding.Artifact.ArtifactId, redactedBinding.Artifact.ArtifactId);
        Assert.NotEqual(binding.Artifact.ArtifactVersion, redactedBinding.Artifact.ArtifactVersion);
        Assert.NotEqual(binding.Artifact.ContentSha256, redactedBinding.Artifact.ContentSha256);
        Assert.Equal("PARTIAL", redacted.GetProperty("coverage").GetString());
        CheckVersion(redacted, "stateRevision", "1", null, "INITIAL_COMPLETE", "READY");
        var redactedPermit = await setup.Json(app, "/permits", redactedBinding);
        var redactedBytes = await GetExportBytes(setup, app, redactedPermit, redactedBinding.Artifact);
        Assert.Equal(Encoding.UTF8.GetBytes(string.Join(',', ExportFields) + "\r\n,ACTIVE,,\r\n"), redactedBytes);

        var oldReadyPermit = await setup.Json(app, "/permits", binding);
        var revoked = await setup.Json(app, uri + "/revoke", redact);
        CheckVersion(ExportOriginalBody(revoked), "stateRevision", "2", "1", "CONTIGUOUS", "REVOKED");
        Assert.Equal(binding.Artifact.ArtifactVersion, ExportOriginalBody(revoked).GetProperty("artifactVersion").GetString());
        await setup.Json(app, "/permits", binding, 403);
        var oldReadyBytes = await GetEmptyDownload(setup, app, oldReadyPermit);
        var anonymizePermit = await setup.Json(app, "/permits", redactedBinding);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.sources SET retained=false,privacy_revision=2
             WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """);
        var anonymizeBytes = await GetEmptyDownload(setup, app, anonymizePermit);
        await setup.Json(app, "/permits", redactedBinding, 403);

        var authority = await setup.Json(app, "/authority");
        foreach (var publication in publications.Append(redaction).Append(revoked))
            VerifyPublication(publication, h.Tenant, original.RequestId, authority);
        WriteExportEvidence("download-request.json", JsonSerializer.SerializeToUtf8Bytes(original, ExportJson));
        WriteExportEvidence("download-accepted-reply.json", receipt);
        WriteExportEvidence("download-authority.json", authority);
        WriteExportEvidence("download-results.json", publications.Append(redaction).Append(revoked).ToArray());
        WriteExportEvidence("download-job-accepted.json", publications[0].GetProperty("body").GetBytesFromBase64());
        WriteExportEvidence("download-job-running.json", publications[1].GetProperty("body").GetBytesFromBase64());
        WriteExportEvidence("download-job-completed.json", publications[2].GetProperty("body").GetBytesFromBase64());
        WriteExportEvidence("download-artifact-ready.json", publications[3].GetProperty("body").GetBytesFromBase64());
        WriteExportEvidence("download-artifact-redacted.json", redaction.GetProperty("body").GetBytesFromBase64());
        WriteExportEvidence("download-artifact-revoked.json", revoked.GetProperty("body").GetBytesFromBase64());
        WriteExportEvidence("download-permit.json", permit);
        WriteExportEvidence("contact-history.csv", bytes);
        WriteExportEvidence("contact-history-redacted.csv", redactedBytes);
        WriteExportEvidence("download-observations.json", new { observed, from, until,
            exactLength = bytes.Length, contentSha256 = ExportHash(bytes), redactedLength = redactedBytes.Length,
            redactedContentSha256 = ExportHash(redactedBytes), expiryBytes, revokeBytes, restoredOldPermitBytes, oldReadyBytes, anonymizeBytes });
    }

    [CurrentPermissionPostgresFact]
    public async Task Contact_history_G05_revocation_during_real_TLS_stream_stops_further_bytes()
    {
        await using var h = await Seed();
        var contact = Guid.NewGuid();
        await SeedContactHistory(h, contact, large: true);
        await using var setup = await ExportServiceFixture.Create(h, windowSeconds: 45);
        await using var app = await setup.Start();
        var original = OriginalExport(IntentFor(h.Tenant, contact, h.User));
        await setup.Json(app, "/requests", original);
        var ready = ExportOriginalBody((await ExportPublications(setup, app, original.RequestId))[3]);
        var binding = ExportRead(original, ready);
        var permit = await setup.Json(app, "/permits", binding);

        using var call = setup.Http(DownloadPath(permit));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await app.SendHeaders(call, deadline.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var exactLength = long.Parse(binding.Artifact.Bytes, CultureInfo.InvariantCulture);
        Assert.Equal(exactLength, response.Content.Headers.ContentLength);
        await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        var buffer = new byte[16384];
        var receivedBeforeRevoke = await body.ReadAsync(buffer, deadline.Token);
        Assert.True(receivedBeforeRevoke > 0);
        var revoke = Stopwatch.StartNew();
        Assert.Equal(1, await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_contact_history_export_v11.access SET can_download=false WHERE tenant_id={h.Tenant} AND contact_id={contact}
            """, deadline.Token));
        var received = receivedBeforeRevoke;
        var interruption = await Assert.ThrowsAnyAsync<IOException>(async () =>
        {
            int count;
            while ((count = await body.ReadAsync(buffer, deadline.Token)) > 0) received += count;
        });
        Assert.True(received < exactLength);
        Assert.True(DateTimeOffset.UtcNow < permit.GetProperty("validUntilUtc").GetDateTimeOffset());
        var retryBytes = await GetEmptyDownload(setup, app, permit);
        WriteExportEvidence("stream-revocation-observation.json", new { exactLength, receivedBeforeRevoke,
            received, retryBytes, abortedDuringWindow = true, revokeAndObserveMilliseconds = revoke.Elapsed.TotalMilliseconds,
            interruption = interruption.GetType().Name });
    }

    private static string DownloadPath(JsonElement permit) => $"/downloads/{permit.GetProperty("permitId").GetGuid():D}";

    private static async Task<byte[]> GetExportBytes(ExportServiceFixture setup, AuthenticationApiProcess app, JsonElement permit,
        ExportReadArtifact artifact)
    {
        using var request = setup.Http(DownloadPath(permit));
        using var response = await app.Send(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(artifact.Bytes, bytes.Length.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(artifact.ContentSha256, response.Headers.GetValues("X-Content-SHA256").Single());
        Assert.Equal(artifact.ContentSha256, ExportHash(bytes));
        return bytes;
    }

    private static async Task<int> GetEmptyDownload(ExportServiceFixture setup, AuthenticationApiProcess app, JsonElement permit, Guid? tenant = null)
    {
        using var request = setup.Http(DownloadPath(permit), tenant: tenant);
        using var response = await app.Send(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(bytes);
        return bytes.Length;
    }
}
