using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Core.Services.ErpIntegration.CommercialProtectionStoreV1;
using static CP6.Core.Services.ErpIntegration.CommercialProtectionV1;

namespace CP6.Core.Services.ErpIntegration;

/// <summary>Main 原语。所属 Owner 决定接线和可信授权，并在同一原生事务参与。</summary>
public sealed partial class CommercialProtectionOwnerV1(IDbContextFactory<ErpIntegrationContext> database,
    IDataProtectionProvider protection, ICommercialAuthorityGateV1 authority, ICommercialOwnerOriginalsV1 originals,
    TimeProvider? clock = null)
{
    private readonly CommercialProtectionStoreV1 store = new(database, protection, clock);

    private sealed record SourceRecord(IcParticipantV1 Participant, CommercialRouteV1 Route, byte[] Original,
        string SourceGeneration, string SharedWriterPolicy, IcSourceControlRefV1[] Controls);
    private sealed record ControlRecord(IcSourceControlRefV1 Reference, Guid? RevocationId, byte[]? Revocation,
        DateTimeOffset? RequestedAt, DateTimeOffset? SettledAt);
    private sealed record GrantOriginal(IcProtectionPrepareV1 Input, IcProtectionGrantV1? Grant, string? Denial);
    private sealed record GrantRecord(string State, GrantOriginal Original, IcExecutionTerminalV1? Terminal);
    private sealed record RevocationOriginal(string ExpectedWatermark, IcProtectionRevocationV1 Decision);

    /// <summary>在源 Owner 确认事实的外层事务内登记准确原件及同源控制闭包。</summary>
    public async Task PublishWithinAsync(NpgsqlConnection db, NpgsqlTransaction tx, CommercialOriginalV1 original,
        IcSourcePublicationV1 publication, CancellationToken ct = default)
    {
        await store.RequireNativeAsync(db, tx, ct);
        var tenant = original.Route.TenantId;
        var subject = Subject(original);
        var purpose = subject is IcHandoffSubjectV1 ? MainIntakeNewEffect : CrmResultProjection;
        var participant = new IcParticipantV1(publication.ParticipantAuthority, subject, purpose);
        RequireParticipant(tenant, null, participant);
        _ = CommercialWireV1.Revision(publication.SourceGeneration);
        _ = CommercialWireV1.RequireKey(publication.SharedWriterPolicy);
        var subjectKey = NaturalSubjectKey(participant);
        var controls = new List<IcSourceControlRefV1>
        {
            new("SUBJECT", publication.ParticipantAuthority, subjectKey, publication.SubjectWatermark),
            new("PURPOSE_POLICY", publication.ParticipantAuthority, publication.PurposePolicyKey, publication.PurposeWatermark)
        };
        if (subject is IcOwnerResultSubjectV1 result)
            controls.Add(new("PRODUCER_STREAM", publication.ParticipantAuthority, ProducerStreamControlKey(tenant, result),
                publication.StreamWatermark ?? throw Failure("SOURCE_CONTROLS_UNPROVEN")));
        else if (publication.StreamWatermark is not null) throw Failure("SOURCE_CONTROLS_UNPROVEN");
        foreach (var control in controls) RequireControl(control);

        await authority.HoldCurrentAsync(db, tx, new(IcAuthorityActionV1.PublishSource, tenant, publication.ParticipantAuthority,
            Participant: participant, Original: original, Publication: publication), ct);
        var record = new SourceRecord(participant, original.Route, original.OriginalBytes(), publication.SourceGeneration,
            publication.SharedWriterPolicy, Ordered(controls));
        await WithinSavepointAsync(tx, async () =>
        {
            await SaveSourceAsync(db, tx, tenant, subjectKey, record, ct);
            return true;
        }, ct);
    }

    private async Task SaveSourceAsync(NpgsqlConnection db, NpgsqlTransaction tx, Guid tenant, string subjectKey,
        SourceRecord record, CancellationToken ct)
    {
        var participant = record.Participant;
        foreach (var coordinate in record.Controls)
        {
            await using var insert = ControlCommand(db, tx, """
                INSERT INTO erp_integration.ic_source_control_v1(tenant_id,authority,kind,control_key,watermark)
                VALUES(@tenant,@authority,@kind,@key,@watermark) ON CONFLICT DO NOTHING
                """, tenant, coordinate);
            insert.Parameters.AddWithValue("watermark", coordinate.Watermark);
            await insert.ExecuteNonQueryAsync(ct);
            var existing = await ControlAsync(db, tx, tenant, coordinate, true, ct);
            if (existing.Reference != coordinate || existing.RevocationId is not null) throw Failure("SOURCE_CONTROL_CONFLICT");
        }
        var key = SourceAddress(participant, subjectKey);
        await using var command = SourceCommand(db, tx, """
            INSERT INTO erp_integration.ic_source_publication_v1(tenant_id,participant,subject_key,purpose,original_cipher)
            VALUES(@tenant,@participant,@subject,@purpose,@cipher) ON CONFLICT DO NOTHING
            """, tenant, participant, subjectKey);
        command.Parameters.AddWithValue("cipher", store.Seal(tenant, "source", key, record));
        if (await command.ExecuteNonQueryAsync(ct) == 0)
        {
            var existing = await SourceAsync(db, tx, tenant, participant, ct);
            if (existing is null || !Same(existing, record)) throw Failure("SUBJECT_BINDING_CONFLICT");
        }
    }

    public Task<CommercialOriginalV1?> ReadSourceOriginalAsync(Guid tenant, IcParticipantV1 participant, CancellationToken ct = default)
    {
        RequireParticipant(tenant, null, participant);
        return store.RunAsync(async (db, tx) =>
        {
            await authority.HoldCurrentAsync(db, tx, new(IcAuthorityActionV1.ReadSourceOriginal, tenant,
                participant.ParticipantAuthority, Participant: participant), ct);
            var record = await SourceAsync(db, tx, tenant, participant, ct);
            return record is null ? null : RequireRegisteredSource(record, participant);
        }, ct);
    }

    public Task<IcProtectionReadV1> PrepareAsync(IcProtectionPrepareV1 input, CancellationToken ct = default)
    {
        RequireExecution(input.Execution);
        var participant = new IcParticipantV1(input.ParticipantAuthority, input.Subject, input.Purpose);
        RequireParticipant(input.Execution.TenantId, input.Execution.OperationKind, participant);
        CheckEvidence(input.ConsumerRegistration);
        _ = CommercialWireV1.RequireKey(input.ExpectedControlWatermark);
        if (input.SchemaVersion != Contract || input.ProtectionKey != ProtectionKey(input.Execution, participant) ||
            input.AdmissionDeadline != Milliseconds(input.AdmissionDeadline)) throw Failure("PROTECTION_BINDING_CONFLICT");
        return store.RunAsync(async (db, tx) =>
        {
            var tenant = input.Execution.TenantId;
            await authority.HoldCurrentAsync(db, tx, new(IcAuthorityActionV1.ReadProtection, tenant, input.ParticipantAuthority,
                participant, input.Execution, input.ConsumerRegistration), ct);
            await LockKeyAsync(db, tx, "ic-grant", tenant, input.ProtectionKey, ct);
            var previous = await GrantAsync(db, tx, tenant, input.ProtectionKey, ct);
            if (previous is not null)
            {
                if (!Same(previous.Original.Input, input)) throw Failure("PROTECTION_KEY_CONFLICT");
                return await ViewGrantAsync(db, tx, previous, ct);
            }

            await authority.HoldCurrentAsync(db, tx, new(IcAuthorityActionV1.Prepare, tenant, input.ParticipantAuthority,
                participant, input.Execution, input.ConsumerRegistration, Prepare: input), ct);
            var source = await SourceAsync(db, tx, tenant, participant, ct);
            var now = await store.NowAsync(db, tx, ct);
            if (source is null) return new(input.ProtectionKey, "UNPROVEN", null, null, null, false, now);
            RequireRegisteredSource(source, participant);
            var held = await ControlsAsync(db, tx, tenant, source.Controls, true, ct);
            now = await store.NowAsync(db, tx, ct);
            var root = held.Single(control => control.Reference.Kind == "SUBJECT");
            var denial = Denial(input, source.Controls, held, root.Reference.Watermark, now);
            var grant = denial is null ? CreateGrant(input, source, held, root.Reference.Watermark, now) : null;
            var saved = new GrantOriginal(input, grant, denial);
            await SaveGrantAsync(db, tx, tenant, participant, saved, ct);
            return new(input.ProtectionKey, grant is null ? "DENIED" : "GRANTED", grant, denial, null,
                held.Any(control => control.RevocationId is not null), now);
        }, ct);
    }

    /// <summary>信任登记路由给出的来源 Authority，并在披露前核当前授权和原保护绑定。</summary>
    public Task<IcProtectionReadV1> ReadProtectionAsync(string sourceAuthority, string key, IcExecutionRefV1 execution,
        CancellationToken ct = default)
    {
        RequireExecution(execution);
        _ = CommercialWireV1.RequireKey(sourceAuthority);
        if (!CommercialWireV1.IsDigest(key)) throw Failure("PROTECTION_BINDING_CONFLICT");
        return store.RunAsync(async (db, tx) =>
        {
            await authority.HoldCurrentAsync(db, tx, new(IcAuthorityActionV1.ReadProtection, execution.TenantId,
                sourceAuthority, Execution: execution), ct);
            var record = await GrantAsync(db, tx, execution.TenantId, key, ct);
            if (record is null || record.Original.Input.ParticipantAuthority != sourceAuthority)
                return new(key, "ABSENT", null, null, null, false, await store.NowAsync(db, tx, ct));
            await authority.HoldCurrentAsync(db, tx, new(IcAuthorityActionV1.ReadProtection, execution.TenantId,
                sourceAuthority, Participant(record.Original.Input), execution, record.Original.Input.ConsumerRegistration), ct);
            return record.Original.Input.Execution == execution
                ? await ViewGrantAsync(db, tx, record, ct) : throw Failure("PROTECTION_BINDING_CONFLICT");
        }, ct);
    }

    public async Task<IcProtectionReadV1> SettleAsync(string sourceAuthority, string protectionKey, IcExecutionRefV1 execution,
        IcExecutionTerminalV1 proposed, CancellationToken ct = default)
    {
        RequireExecution(execution);
        if (!CommercialWireV1.IsDigest(protectionKey) || proposed.Execution != execution) throw Failure("TERMINAL_BINDING_CONFLICT");
        RequireTerminal(proposed);
        // 当前访问许可与远端读原终态分离，远端查证始终在源锁外。
        var prior = await ReadProtectionAsync(sourceAuthority, protectionKey, execution, ct);
        if (prior.Knowledge == "SETTLED")
        {
            if (!Same(prior.Terminal, proposed)) throw Failure("TERMINAL_BINDING_CONFLICT");
            return prior;
        }
        if (prior.Knowledge != "GRANTED") return prior;

        var verified = await originals.ReadTerminalAsync(execution, ct);
        if (verified is null || !Same(verified, proposed)) return prior;
        RequireTerminal(verified);
        return await store.RunAsync(async (db, tx) =>
        {
            var tenant = execution.TenantId;
            var grant = prior.Grant!;
            await authority.HoldCurrentAsync(db, tx, new(IcAuthorityActionV1.Settle, tenant, grant.ParticipantAuthority,
                new(grant.ParticipantAuthority, grant.Subject, grant.Purpose), execution, verified.ConsumerEvidence), ct);
            await LockKeyAsync(db, tx, "ic-grant", tenant, protectionKey, ct);
            var record = await GrantAsync(db, tx, tenant, protectionKey, ct) ?? throw Failure("ORIGINAL_UNPROVEN");
            if (record.Original.Input.Execution != execution || !Same(record.Original.Grant, grant)) throw Failure("TERMINAL_BINDING_CONFLICT");
            if (record.Terminal is not null)
            {
                if (!Same(record.Terminal, verified)) throw Failure("TERMINAL_BINDING_CONFLICT");
                return await ViewGrantAsync(db, tx, record, ct);
            }

            var held = await ControlsAsync(db, tx, tenant, grant.ProtectedControls, true, ct);
            await using var update = Command(db, tx, """
                UPDATE erp_integration.ic_grant_v1 SET state='SETTLED',terminal_cipher=@terminal
                 WHERE tenant_id=@tenant AND protection_key=@key AND state='GRANTED'
                """, ("tenant", tenant), ("key", protectionKey), ("terminal", store.Seal(tenant, "terminal", protectionKey, verified)));
            if (await update.ExecuteNonQueryAsync(ct) != 1) throw Failure("ORIGINAL_UNPROVEN");
            foreach (var control in held.Where(x => x.RevocationId is not null && x.SettledAt is null))
                _ = await ResolveRevocationAsync(db, tx, tenant, control, ct);
            return await ViewGrantAsync(db, tx, record with { State = "SETTLED", Terminal = verified }, ct);
        }, ct);
    }

    public Task<IcProtectionRevocationReadV1> RevokeAsync(Guid tenant, IcSourceControlRefV1 control,
        IcProtectionRevocationV1 request, CancellationToken ct = default)
    {
        RequireControl(control);
        if (tenant == Guid.Empty || request.RevocationId == Guid.Empty || request.NextControlWatermark == control.Watermark)
            throw Failure("REVOCATION_INVALID");
        _ = CommercialWireV1.RequireKey(request.ReasonCode);
        _ = CommercialWireV1.RequireKey(request.NextControlWatermark);
        CheckEvidence(request.Decision);
        return store.RunAsync(async (db, tx) =>
        {
            await authority.HoldCurrentAsync(db, tx, new(IcAuthorityActionV1.Revoke, tenant, control.Authority,
                Control: control, Evidence: request.Decision, Revocation: request), ct);
            var current = await ControlAsync(db, tx, tenant, control, true, ct);
            var frozen = new RevocationOriginal(control.Watermark, request);
            var purpose = ControlAddress(control);
            if (current.RevocationId is not null)
            {
                var existing = store.Open<RevocationOriginal>(tenant, "revocation", purpose, current.Revocation!);
                if (!Same(existing, frozen)) throw Failure("REVOCATION_KEY_CONFLICT");
                return await ResolveRevocationAsync(db, tx, tenant, current, ct);
            }
            if (current.Reference != control) throw Failure("SOURCE_CONTROL_CONFLICT");
            var at = await store.NowAsync(db, tx, ct);
            var cipher = store.Seal(tenant, "revocation", purpose, frozen);
            await using var sql = ControlCommand(db, tx, """
                UPDATE erp_integration.ic_source_control_v1 SET revocation_id=@id,revocation_bytes=@bytes,requested_at=@at
                 WHERE tenant_id=@tenant AND authority=@authority AND kind=@kind AND control_key=@key
                """, tenant, control);
            sql.Parameters.AddWithValue("id", request.RevocationId);
            sql.Parameters.AddWithValue("bytes", cipher);
            sql.Parameters.AddWithValue("at", at);
            await sql.ExecuteNonQueryAsync(ct);
            return await ResolveRevocationAsync(db, tx, tenant, current with
            { RevocationId = request.RevocationId, Revocation = cipher, RequestedAt = at }, ct);
        }, ct);
    }

    private async Task<IcProtectionRevocationReadV1> ResolveRevocationAsync(NpgsqlConnection db, NpgsqlTransaction tx,
        Guid tenant, ControlRecord control, CancellationToken ct)
    {
        var lookup = control.Reference;
        await using var query = ControlCommand(db, tx, """
            SELECT g.protection_key FROM erp_integration.ic_control_grant_v1 m
            JOIN erp_integration.ic_grant_v1 g USING (tenant_id,protection_key)
            WHERE m.tenant_id=@tenant AND m.authority=@authority AND m.kind=@kind AND m.control_key=@key AND g.state='GRANTED'
            ORDER BY g.protection_key
            """, tenant, lookup);
        var keys = new List<string>();
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) keys.Add(reader.GetString(0));

        var at = control.SettledAt;
        var watermark = at is null ? null : lookup.Watermark;
        if (keys.Count == 0 && at is null)
        {
            var original = store.Open<RevocationOriginal>(tenant, "revocation", ControlAddress(lookup), control.Revocation!);
            at = await store.NowAsync(db, tx, ct);
            watermark = original.Decision.NextControlWatermark;
            await using var change = ControlCommand(db, tx, """
                UPDATE erp_integration.ic_source_control_v1 SET watermark=@watermark,settled_at=@at
                 WHERE tenant_id=@tenant AND authority=@authority AND kind=@kind AND control_key=@key AND settled_at IS NULL
                """, tenant, lookup);
            change.Parameters.AddWithValue("watermark", watermark);
            change.Parameters.AddWithValue("at", at.Value);
            if (await change.ExecuteNonQueryAsync(ct) != 1) throw Failure("SOURCE_CONTROL_CONFLICT");
        }
        return new(control.RevocationId!.Value, "BLOCKED", at is null ? "PENDING_TERMINAL" : "SETTLED",
            keys.ToArray(), watermark, control.RequestedAt!.Value, at);
    }

    private static string? Denial(IcProtectionPrepareV1 input, IcSourceControlRefV1[] declared, ControlRecord[] held,
        string watermark, DateTimeOffset now)
    {
        if (held.Any(x => x.RevocationId is not null)) return "SOURCE_NEW_GRANTS_BLOCKED";
        if (input.ExpectedControlWatermark != watermark || !declared.SequenceEqual(held.Select(x => x.Reference))) return "SOURCE_CONTROL_CONFLICT";
        return now >= input.AdmissionDeadline ? "ADMISSION_DEADLINE_EXCEEDED" : null;
    }

    private static IcProtectionGrantV1 CreateGrant(IcProtectionPrepareV1 input, SourceRecord source,
        ControlRecord[] held, string watermark, DateTimeOffset now)
    {
        var evidence = new IcEvidenceRefV1(input.ParticipantAuthority, input.ProtectionKey, source.SourceGeneration, "owner-original");
        var grant = new IcProtectionGrantV1(input.ProtectionKey, input.Execution, input.ParticipantAuthority, input.Subject, input.Purpose,
            source.SourceGeneration, watermark, "GRANTED", now, now.AddMinutes(5), false, source.SharedWriterPolicy,
            held.Select(x => x.Reference).ToArray(), "", evidence);
        var digest = GrantDigest(grant);
        return grant with { GrantDigest = digest, GrantEvidence = evidence with { ProofRef = digest } };
    }

    private static IcParticipantV1 Participant(IcProtectionPrepareV1 input) => new(input.ParticipantAuthority, input.Subject, input.Purpose);

    private async Task<IcProtectionReadV1> ViewGrantAsync(NpgsqlConnection db, NpgsqlTransaction tx, GrantRecord record, CancellationToken ct)
    {
        var input = record.Original.Input;
        var tenant = input.Execution.TenantId;
        var source = await SourceAsync(db, tx, tenant, Participant(input), ct) ?? throw Failure("ORIGINAL_UNPROVEN");
        var blocked = (await ControlsAsync(db, tx, tenant, source.Controls, false, ct)).Any(x => x.RevocationId is not null);
        return new(input.ProtectionKey, record.State, record.Original.Grant, record.Original.Denial,
            record.Terminal, blocked, await store.NowAsync(db, tx, ct));
    }

    private static CommercialOriginalV1 RequireRegisteredSource(SourceRecord record, IcParticipantV1 participant)
    {
        if (record.Participant != participant) throw Failure("SUBJECT_BINDING_CONFLICT");
        var raw = CommercialOriginalV1.Capture(record.Route, participant.Subject.OriginalBodyHash, record.Original);
        if (Subject(raw) != participant.Subject) throw Failure("ORIGINAL_UNPROVEN");
        return raw;
    }

    private async Task SaveGrantAsync(NpgsqlConnection db, NpgsqlTransaction tx, Guid tenant, IcParticipantV1 participant,
        GrantOriginal original, CancellationToken ct)
    {
        var input = original.Input;
        await using var insert = SourceCommand(db, tx, """
            INSERT INTO erp_integration.ic_grant_v1(tenant_id,protection_key,participant,subject_key,purpose,consumer,
                operation_kind,operation_id,execution_generation,state,original_cipher)
            VALUES(@tenant,@key,@participant,@subject,@purpose,@consumer,@operation,@id,@generation,@state,@cipher)
            ON CONFLICT DO NOTHING
            """, tenant, participant, NaturalSubjectKey(participant));
        insert.Parameters.AddWithValue("key", input.ProtectionKey);
        insert.Parameters.AddWithValue("consumer", input.Execution.ConsumerAuthority);
        insert.Parameters.AddWithValue("operation", input.Execution.OperationKind);
        insert.Parameters.AddWithValue("id", input.Execution.OperationId);
        insert.Parameters.AddWithValue("generation", CommercialWireV1.Revision(input.Execution.ExecutionGeneration));
        insert.Parameters.AddWithValue("state", original.Grant is null ? "DENIED" : "GRANTED");
        insert.Parameters.AddWithValue("cipher", store.Seal(tenant, "grant", input.ProtectionKey, original));
        if (await insert.ExecuteNonQueryAsync(ct) != 1) throw Failure("PROTECTION_KEY_CONFLICT");
        if (original.Grant is null) return;
        foreach (var control in original.Grant.ProtectedControls)
        {
            await using var map = ControlCommand(db, tx, """
                INSERT INTO erp_integration.ic_control_grant_v1(tenant_id,authority,kind,control_key,protection_key)
                VALUES(@tenant,@authority,@kind,@key,@grant)
                """, tenant, control);
            map.Parameters.AddWithValue("grant", input.ProtectionKey);
            await map.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<GrantRecord?> GrantAsync(NpgsqlConnection db, NpgsqlTransaction tx, Guid tenant, string key, CancellationToken ct)
    {
        await using var sql = Command(db, tx, """
            SELECT state,original_cipher,terminal_cipher FROM erp_integration.ic_grant_v1 WHERE tenant_id=@tenant AND protection_key=@key
            """, ("tenant", tenant), ("key", key));
        await using var row = await sql.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        var original = store.Open<GrantOriginal>(tenant, "grant", key, row.GetFieldValue<byte[]>(1));
        if (original.Input.ProtectionKey != key || original.Input.Execution.TenantId != tenant ||
            original.Grant is not null && original.Grant.GrantDigest != GrantDigest(original.Grant)) throw Failure("ORIGINAL_UNPROVEN");
        return new(row.GetString(0), original, row.IsDBNull(2) ? null : store.Open<IcExecutionTerminalV1>(tenant, "terminal", key, row.GetFieldValue<byte[]>(2)));
    }

    private static string SourceAddress(IcParticipantV1 participant, string key) => participant.ParticipantAuthority + "/" + participant.Purpose + "/" + key;

    private async Task<SourceRecord?> SourceAsync(NpgsqlConnection db, NpgsqlTransaction tx, Guid tenant, IcParticipantV1 participant, CancellationToken ct)
    {
        var key = NaturalSubjectKey(participant);
        await using var sql = SourceCommand(db, tx, """
            SELECT original_cipher FROM erp_integration.ic_source_publication_v1
             WHERE tenant_id=@tenant AND participant=@participant AND subject_key=@subject AND purpose=@purpose
            """, tenant, participant, key);
        return await sql.ExecuteScalarAsync(ct) is byte[] encoded ? store.Open<SourceRecord>(tenant, "source", SourceAddress(participant, key), encoded) : null;
    }

    private static NpgsqlCommand SourceCommand(NpgsqlConnection db, NpgsqlTransaction tx, string sql, Guid tenant,
        IcParticipantV1 participant, string key) => Command(db, tx, sql, ("tenant", tenant), ("participant", participant.ParticipantAuthority),
        ("subject", key), ("purpose", participant.Purpose));

    private static void RequireControl(IcSourceControlRefV1 item)
    {
        if (item.Kind is not ("SUBJECT" or "PURPOSE_POLICY" or "PRODUCER_STREAM")) throw Failure("SOURCE_CONTROLS_UNPROVEN");
        foreach (var field in new[] { item.Authority, item.Key, item.Watermark }) _ = CommercialWireV1.RequireKey(field);
    }

    private static IcSourceControlRefV1[] Ordered(IEnumerable<IcSourceControlRefV1> controls) => controls
        .OrderBy(x => x.Authority, StringComparer.Ordinal).ThenBy(x => x.Kind, StringComparer.Ordinal)
        .ThenBy(x => x.Key, StringComparer.Ordinal).ToArray();

    private static string ControlAddress(IcSourceControlRefV1 item) => item.Authority + "/" + item.Kind + "/" + item.Key;

    private static async Task<ControlRecord[]> ControlsAsync(NpgsqlConnection db, NpgsqlTransaction tx, Guid tenant,
        IEnumerable<IcSourceControlRefV1> controls, bool forUpdate, CancellationToken ct)
    {
        var result = new List<ControlRecord>();
        foreach (var control in Ordered(controls))
            result.Add(await ControlAsync(db, tx, tenant, control, forUpdate, ct));
        return result.ToArray();
    }

    private static async Task<ControlRecord> ControlAsync(NpgsqlConnection db, NpgsqlTransaction tx, Guid tenant,
        IcSourceControlRefV1 reference, bool forUpdate, CancellationToken ct)
    {
        await using var sql = ControlCommand(db, tx, """
            SELECT watermark,revocation_id,revocation_bytes,requested_at,settled_at FROM erp_integration.ic_source_control_v1
             WHERE tenant_id=@tenant AND authority=@authority AND kind=@kind AND control_key=@key
            """ + (forUpdate ? " FOR UPDATE" : ""), tenant, reference);
        await using var row = await sql.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) throw Failure("SOURCE_CONTROLS_UNPROVEN");
        return new(reference with { Watermark = row.GetString(0) }, row.IsDBNull(1) ? null : row.GetGuid(1),
            row.IsDBNull(2) ? null : row.GetFieldValue<byte[]>(2), row.IsDBNull(3) ? null : new(row.GetDateTime(3)),
            row.IsDBNull(4) ? null : new(row.GetDateTime(4)));
    }

    private static NpgsqlCommand ControlCommand(NpgsqlConnection db, NpgsqlTransaction tx, string sql, Guid tenant, IcSourceControlRefV1 reference) =>
        Command(db, tx, sql, ("tenant", tenant), ("authority", reference.Authority), ("kind", reference.Kind), ("key", reference.Key));
}
