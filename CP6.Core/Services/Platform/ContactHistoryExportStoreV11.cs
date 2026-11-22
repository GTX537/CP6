using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using static CP6.Core.Services.Platform.ContactHistoryExportWireV11;

namespace CP6.Core.Services.Platform;

internal sealed record HistoryExportGrant(string Watermark, string[] Fields, string[] Purposes, bool Request,
    bool Download, DateTimeOffset From, DateTimeOffset Until, DateTimeOffset Through, string Coverage, long PrivacyRevision, bool Retained,
    long AccessRevision)
{
    internal bool Allows(ContactHistoryIntentV11 intent) => Purposes.Contains(intent.ReadPurpose, StringComparer.Ordinal) &&
        intent.FieldIds.All(field => Fields.Contains(field, StringComparer.Ordinal));
}

internal sealed record HistoryExportRequest(Guid Id, string Client, Guid SourceEvent, string Epoch, string Digest,
    ContactHistoryIntentV11 Intent, Guid? Job, Guid ReceiptEvent);

internal sealed record HistoryExportFile(Guid Id, string Version, string Digest, int Size, long PrivacyRevision, string Watermark,
    string State, bool Redacted);
internal sealed record HistoryExportOriginal(Guid Event, Guid Request, string Kind, string Stream, long Revision,
    byte[] Body, string BodyHash, string KeyId, string Signature, long Ordinal = 0);
internal sealed record HistoryExportPermit(Guid Id, ContactHistoryReadV11 Binding, DateTimeOffset From, DateTimeOffset Until, long AccessRevision);

internal sealed partial class ContactHistoryExportStoreV11(NpgsqlConnection db, NpgsqlTransaction tx, ContactHistoryCallerV11 caller)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal NpgsqlCommand Command(string sql, params (string Key, object Value)[] parameters)
    {
        var cmd = new NpgsqlCommand(sql, db, tx);
        cmd.Parameters.AddWithValue("t", caller.TenantId);
        foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value);
        return cmd;
    }

    internal async Task<DateTimeOffset> NowAsync(CancellationToken ct)
    {
        await using var cmd = Command("SELECT clock_timestamp()");
        return new((DateTime)(await cmd.ExecuteScalarAsync(ct))!);
    }

    internal async Task<DateTimeOffset> HoldCallerAsync(CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT s."ExpiresAtUtc" FROM crm_identity."ServiceToken" s JOIN public."Sys_Tenants" t ON t."Id"=s."TenantId"
             WHERE s."TenantId"=@t AND s."Issuer"=@issuer AND s."ClientId"=@client AND s."Jti"=@token
               AND s."RevokedAtUtc" IS NULL AND s."ExpiresAtUtc">clock_timestamp() AND @expiry>clock_timestamp()
               AND t."Enable" AND (t."ExpireDate" IS NULL OR t."ExpireDate">clock_timestamp()) FOR SHARE OF s,t
            """, ("issuer", caller.Issuer), ("client", caller.ClientId), ("token", caller.TokenId), ("expiry", caller.ExpiresAt));
        if (await cmd.ExecuteScalarAsync(ct) is not DateTime expiry) throw Denied();
        var at = new DateTimeOffset(expiry);
        return at < caller.ExpiresAt ? at : caller.ExpiresAt;
    }

    internal async Task HoldRequestAsync(Guid request, CancellationToken ct)
    {
        await using var cmd = Command("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",
            ("key", $"CP6.Contact.History.Export/1.1/{caller.TenantId:D}/{request:D}"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<HistoryExportGrant> GrantAsync(ContactHistoryIntentV11 intent, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT a.watermark,a.field_ids,a.purposes,a.can_request,a.can_download,a.valid_from,a.valid_until,a.recorded_through,
                   a.coverage,s.privacy_revision,s.retained,a.access_revision
              FROM cp6_contact_history_export_v11.access a JOIN cp6_contact_history_export_v11.sources s USING(tenant_id,contact_id)
              JOIN public."Sys_Users" u ON (u."TenantId",u."Id")=(a.tenant_id,a.subject_id)
             WHERE a.tenant_id=@t AND a.contact_id=@contact AND a.subject_id=@subject AND u."Enable"
              AND a.can_read AND a.valid_from<=clock_timestamp() AND a.valid_until>clock_timestamp() FOR SHARE OF a,s,u
            """, ("contact", intent.ContactId), ("subject", intent.ReaderSubjectId));
        await using var row = await cmd.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? new(row.GetString(0), row.GetFieldValue<string[]>(1), row.GetFieldValue<string[]>(2),
            row.GetBoolean(3), row.GetBoolean(4), new(row.GetDateTime(5)), new(row.GetDateTime(6)), new(row.GetDateTime(7)),
            row.GetString(8), row.GetInt64(9), row.GetBoolean(10), row.GetInt64(11)) : throw Denied();
    }

    internal async Task<HistoryExportRequest?> RequestAsync(Guid request, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT client_id,source_event_id,epoch,intent_digest,intent_bytes,job_id,receipt_event_id
              FROM cp6_contact_history_export_v11.requests WHERE tenant_id=@t AND request_id=@request
            """, ("request", request));
        await using var row = await cmd.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        if (row.GetString(0) != caller.ClientId) throw Missing();
        var intent = JsonSerializer.Deserialize<ContactHistoryIntentV11>(row.GetFieldValue<byte[]>(4), WireJson) ?? throw Unavailable();
        if (intent.TenantId != caller.TenantId || IntentDigest(intent) != row.GetString(3)) throw Unavailable();
        return new(request, row.GetString(0), row.GetGuid(1), row.GetString(2), row.GetString(3), intent,
            row.IsDBNull(5) ? null : row.GetGuid(5), row.GetGuid(6));
    }

    internal async Task AddRequestAsync(HistoryExportRequest request, CancellationToken ct)
    {
        await using var cmd = Command("""
            INSERT INTO cp6_contact_history_export_v11.requests(tenant_id,request_id,client_id,source_event_id,contact_id,subject_id,
                epoch,intent_digest,intent_bytes,job_id,receipt_event_id,decision)
            VALUES(@t,@request,@client,@event,@contact,@subject,@epoch,@hash,@intent,@job,@receipt,@state)
            """, ("request", request.Id), ("client", request.Client), ("event", request.SourceEvent), ("contact", request.Intent.ContactId),
            ("subject", request.Intent.ReaderSubjectId), ("epoch", request.Epoch), ("hash", request.Digest), ("intent", Bytes(request.Intent)),
            ("receipt", request.ReceiptEvent), ("state", request.Job.HasValue ? "ACCEPTED" : "DECLINED"));
        cmd.Parameters.AddWithValue("job", NpgsqlDbType.Uuid, (object?)request.Job ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<byte[]> CaptureAsync(ContactHistoryIntentV11 intent, HistoryExportGrant grant, bool redact, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT values_json::text FROM (
                SELECT DISTINCT ON (fact_id) fact_id,occurred_at,values_json
                  FROM cp6_contact_history_export_v11.history
                 WHERE tenant_id=@t AND contact_id=@contact AND subject_id=@subject AND watermark=@watermark
                   AND purpose=@purpose AND view=@view AND recorded_at<=@cut
                 ORDER BY fact_id,recorded_at DESC,revision DESC
            ) snapshot WHERE (@from IS NULL OR occurred_at>=@from) AND (@to IS NULL OR occurred_at<@to)
             ORDER BY occurred_at,fact_id
            """, ("contact", intent.ContactId), ("subject", intent.ReaderSubjectId), ("watermark", grant.Watermark),
            ("purpose", intent.ReadPurpose), ("view", intent.View), ("cut", ParseTime(intent.RecordedThrough)));
        cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, intent.OccurrenceFromUtc is { } from ? ParseTime(from) : DBNull.Value);
        cmd.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, intent.OccurrenceToUtc is { } to ? ParseTime(to) : DBNull.Value);
        using var csv = new MemoryStream();
        Row(intent.FieldIds);
        await using var data = await cmd.ExecuteReaderAsync(ct);
        while (await data.ReadAsync(ct))
        {
            using var json = JsonDocument.Parse(data.GetString(0));
            var values = json.RootElement;
            Row(intent.FieldIds.Select(field =>
            {
                if ((redact && field != "contact.lifecycle") || !values.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
                    return "";
                return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Unavailable();
            }));
        }
        return csv.ToArray();

        void Row(IEnumerable<string> cells)
        {
            var line = Utf8.GetBytes(string.Join(',', cells.Select(CsvCell)) + "\r\n");
            if (csv.Length + line.Length > MaximumBytes) throw new ContactHistoryExportExceptionV11("CONTACT_EXPORT_SIZE_LIMIT", 422);
            csv.Write(line);
        }
    }

    private static string CsvCell(string value)
    {
        // 电子表格打开导出时将公式前缀视为文本，列序和选取的原事实仍按确认意图。
        var visible = value.TrimStart();
        if (visible.Length != 0 && visible[0] is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r'))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : '"' + value.Replace("\"", "\"\"") + '"';
    }

    internal async Task AddFileAsync(HistoryExportRequest request, HistoryExportFile file, byte[] payload, CancellationToken ct)
    {
        await using var cmd = Command("""
            INSERT INTO cp6_contact_history_export_v11.artifacts(tenant_id,request_id,job_id,artifact_id,artifact_version,
                digest,byte_length,privacy_revision,source_watermark,state,redacted)
            VALUES(@t,@request,@job,@id,@version,@hash,@len,@privacy,@watermark,@state,@redacted);
            INSERT INTO cp6_contact_history_export_v11.payloads(tenant_id,artifact_id,artifact_version,bytes) VALUES(@t,@id,@version,@payload)
            """, ("request", request.Id), ("job", request.Job!.Value), ("id", file.Id), ("version", file.Version), ("hash", file.Digest),
            ("len", file.Size), ("privacy", file.PrivacyRevision), ("watermark", file.Watermark),
            ("state", file.State), ("redacted", file.Redacted), ("payload", payload));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<HistoryExportFile?> FileAsync(Guid request, Guid artifact, string version, bool write, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT digest,byte_length,privacy_revision,source_watermark,state,redacted FROM cp6_contact_history_export_v11.artifacts
             WHERE tenant_id=@t AND request_id=@request AND artifact_id=@id AND artifact_version=@version
            """ + (write ? " FOR UPDATE" : " FOR SHARE"), ("request", request), ("id", artifact), ("version", version));
        await using var row = await cmd.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? new(artifact, version, row.GetString(0), row.GetInt32(1), row.GetInt64(2),
            row.GetString(3), row.GetString(4), row.GetBoolean(5)) : null;
    }

    internal async Task<HistoryExportFile?> RedactedForAsync(Guid request, Guid artifact, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT artifact_version,digest,byte_length,privacy_revision,source_watermark,state FROM cp6_contact_history_export_v11.artifacts
             WHERE tenant_id=@t AND request_id=@request AND artifact_id=@id AND redacted FOR SHARE
            """, ("request", request), ("id", artifact));
        await using var row = await cmd.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? new(artifact, row.GetString(0), row.GetString(1), row.GetInt32(2), row.GetInt64(3),
            row.GetString(4), row.GetString(5), true) : null;
    }

    internal async Task<byte[]> PayloadAsync(HistoryExportFile file, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT bytes FROM cp6_contact_history_export_v11.payloads WHERE tenant_id=@t AND artifact_id=@id AND artifact_version=@version FOR SHARE
            """, ("id", file.Id), ("version", file.Version));
        if (await cmd.ExecuteScalarAsync(ct) is not byte[] bytes) throw Unavailable();
        if (bytes.Length != file.Size || Hash(bytes) != file.Digest)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            throw Unavailable();
        }
        return bytes;
    }

    internal async Task RevokeFileAsync(HistoryExportFile file, CancellationToken ct)
    {
        await using var cmd = Command("""
            UPDATE cp6_contact_history_export_v11.artifacts SET state='REVOKED'
             WHERE tenant_id=@t AND artifact_id=@id AND artifact_version=@version AND state='READY'
            """, ("id", file.Id), ("version", file.Version));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw Conflict("CONTACT_EXPORT_ARTIFACT_CHANGED");
    }

    internal async Task HoldPayloadAsync(HistoryExportFile file, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT 1 FROM cp6_contact_history_export_v11.payloads
             WHERE tenant_id=@t AND artifact_id=@id AND artifact_version=@version AND octet_length(bytes)=@size FOR SHARE
            """, ("id", file.Id), ("version", file.Version), ("size", file.Size));
        if (await cmd.ExecuteScalarAsync(ct) is null) throw Unavailable();
    }
}
