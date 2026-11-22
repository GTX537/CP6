using System.Text.Json;
using Npgsql;
using static CP6.Core.Services.Platform.ContactHistoryExportWireV11;

namespace CP6.Core.Services.Platform;

internal sealed partial class ContactHistoryExportStoreV11
{
    private const string OriginalColumns = "event_id,request_id,kind,stream_digest,revision,body,body_sha256,key_id,signature,ordinal";

    internal async Task AddOriginalAsync(HistoryExportOriginal value, CancellationToken ct)
    {
        await using var cmd = Command("""
            INSERT INTO cp6_contact_history_export_v11.originals(tenant_id,request_id,event_id,kind,stream_digest,revision,
                body,body_sha256,key_id,signature)
            VALUES(@t,@request,@event,@kind,@stream,@revision,@body,@hash,@key,@sign)
            """, ("request", value.Request), ("event", value.Event), ("kind", value.Kind), ("stream", value.Stream),
            ("revision", value.Revision), ("body", value.Body), ("hash", value.BodyHash), ("key", value.KeyId), ("sign", value.Signature));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal Task<HistoryExportOriginal?> EventAsync(Guid id, CancellationToken ct) => OneAsync(
        Command($"SELECT {OriginalColumns} FROM cp6_contact_history_export_v11.originals WHERE tenant_id=@t AND event_id=@event", ("event", id)), ct);

    internal Task<HistoryExportOriginal?> HeadAsync(Guid request, string stream, CancellationToken ct) => OneAsync(
        Command($"""
            SELECT {OriginalColumns} FROM cp6_contact_history_export_v11.originals WHERE tenant_id=@t AND request_id=@request
             AND stream_digest=@stream ORDER BY revision DESC LIMIT 1
            """, ("request", request), ("stream", stream)), ct);

    internal Task<HistoryExportOriginal?> RevisionAsync(Guid request, string stream, long revision, CancellationToken ct) => OneAsync(
        Command($"""
            SELECT {OriginalColumns} FROM cp6_contact_history_export_v11.originals WHERE tenant_id=@t AND request_id=@request
             AND stream_digest=@stream AND revision=@revision
            """, ("request", request), ("stream", stream), ("revision", revision)), ct);

    internal async Task<HistoryExportOriginal[]> PageAsync(Guid request, long after, CancellationToken ct)
    {
        await using var cmd = Command($"""
            SELECT {OriginalColumns} FROM cp6_contact_history_export_v11.originals WHERE tenant_id=@t AND request_id=@request
              AND ordinal>@after ORDER BY ordinal LIMIT 16
            """, ("request", request), ("after", after));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<HistoryExportOriginal>();
        while (await reader.ReadAsync(ct)) rows.Add(Original(reader));
        return [.. rows];
    }

    private static async Task<HistoryExportOriginal?> OneAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var cmd = command;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Original(reader) : null;
    }

    private static HistoryExportOriginal Original(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
        reader.GetString(3), reader.GetInt64(4), reader.GetFieldValue<byte[]>(5), reader.GetString(6), reader.GetString(7),
        reader.GetString(8), reader.GetInt64(9));

    internal async Task ReportConflictAsync(HistoryExportOriginal seen, Guid conflict, string alternateHash, CancellationToken ct)
    {
        await using var cmd = Command("""
            INSERT INTO cp6_contact_history_export_v11.conflicts(tenant_id,request_id,event_id,conflict_id,alternate_hash)
            VALUES(@t,@request,@event,@conflict,@hash) ON CONFLICT(tenant_id,conflict_id) DO NOTHING
            """, ("request", seen.Request), ("event", seen.Event), ("conflict", conflict), ("hash", alternateHash));
        if (await cmd.ExecuteNonQueryAsync(ct) == 1) return;
        cmd.CommandText = """
            SELECT 1 FROM cp6_contact_history_export_v11.conflicts WHERE tenant_id=@t AND conflict_id=@conflict
             AND request_id=@request AND event_id=@event AND alternate_hash=@hash
            """;
        if (await cmd.ExecuteScalarAsync(ct) is null) throw Conflict("CONTACT_EXPORT_CONFLICT_ID_BOUND");
    }

    internal async Task<Guid[]> ConflictsAsync(Guid request, string[] streams, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT c.conflict_id FROM cp6_contact_history_export_v11.conflicts c
              JOIN cp6_contact_history_export_v11.originals o USING(tenant_id,event_id)
             WHERE c.tenant_id=@t AND c.request_id=@request AND c.resolved_by IS NULL AND o.stream_digest=ANY(@streams)
            """, ("request", request), ("streams", streams));
        await using var rows = await cmd.ExecuteReaderAsync(ct);
        var values = new List<Guid>();
        while (await rows.ReadAsync(ct)) values.Add(rows.GetGuid(0));
        return [.. values];
    }

    internal async Task ResolveAsync(Guid request, Guid newEvent, Guid[] ids, CancellationToken ct)
    {
        await using var cmd = Command("""
            UPDATE cp6_contact_history_export_v11.conflicts SET resolved_by=@event
             WHERE tenant_id=@t AND request_id=@request AND conflict_id=ANY(@ids) AND resolved_by IS NULL
            """, ("request", request), ("event", newEvent), ("ids", ids));
        if (await cmd.ExecuteNonQueryAsync(ct) != ids.Length) throw Conflict("CONTACT_EXPORT_CONFLICT_CHANGED");
    }

    internal async Task AddPermitAsync(HistoryExportPermit permit, CancellationToken ct)
    {
        var binding = Bytes(permit.Binding);
        await using var cmd = Command("""
            INSERT INTO cp6_contact_history_export_v11.permits(tenant_id,permit_id,request_id,service_token_jti,binding,binding_hash,valid_from,valid_until,access_revision)
            VALUES(@t,@id,@request,@token,@binding,@hash,@from,@until,@accessRevision)
            """, ("id", permit.Id), ("request", permit.Binding.Artifact.RequestId), ("binding", binding), ("hash", Hash(binding)),
            ("from", permit.From), ("until", permit.Until), ("token", caller.TokenId), ("accessRevision", permit.AccessRevision));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<HistoryExportPermit> PermitAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = Command("""
            SELECT binding,binding_hash,valid_from,valid_until,request_id,access_revision FROM cp6_contact_history_export_v11.permits
             WHERE tenant_id=@t AND permit_id=@id AND service_token_jti=@token
               AND valid_from<=clock_timestamp() AND clock_timestamp()<valid_until FOR SHARE
            """, ("id", id), ("token", caller.TokenId));
        await using var row = await cmd.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) throw Denied();
        var raw = row.GetFieldValue<byte[]>(0);
        if (Hash(raw) != row.GetString(1)) throw Unavailable();
        var value = JsonSerializer.Deserialize<ContactHistoryReadV11>(raw, WireJson) ?? throw Unavailable();
        if (value.Artifact.RequestId != row.GetGuid(4) || caller.TenantId != value.Subject.TenantId) throw Unavailable();
        return new(id, value, new(row.GetDateTime(2)), new(row.GetDateTime(3)), row.GetInt64(5));
    }
}
