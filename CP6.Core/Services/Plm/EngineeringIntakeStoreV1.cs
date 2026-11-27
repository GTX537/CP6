using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.ErpIntegration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;

namespace CP6.Core.Services.Plm;

internal sealed class EngineeringIntakeStoreV1(CP6Context db, IDataProtectionProvider protection)
{
    internal sealed record Slot(Guid CaseId, long CaseRevision, long CurrentEs, Guid CurrentRequestKey, Guid ExecutionGeneration);
    internal sealed record Coverage(Guid Generation, string EvidenceKey);
    internal sealed record ProtectedOriginal(byte[] Bytes, EngineeringIntakeTrustV1 Trust);
    internal sealed record Received(EngineeringIntakeRequestV1 Envelope, byte[] Bytes, string BodyHash, string SourceBodyHash,
        DateTimeOffset ReceivedAt)
    {
        internal EngineeringIntakeSourceLocationV1 Source => new(Envelope.CaseNaturalKey, Envelope.CaseId, Envelope.RequestKey,
            Envelope.SubmissionVersion, Envelope.SourceBinding, BodyHash);
    }
    internal sealed record Terminal(EngineeringIntakeResultV1 Result, EngineeringIntakeOriginalItemV1 Item,
        Guid? SourcePackageId, long? SourcePackageRevision);

    private sealed record RequestRow(Guid CaseId, long SubmissionVersion, Guid EventId, string SourceDigest, string SourceBodyHash,
        string BodyHash, byte[] Cipher, DateTimeOffset FirstReceivedAt);
    private sealed record ResultRow(string ReceiptId, string Outcome, Guid? EngineeringId, long? EngineeringRevision,
        Guid? SourcePackageId, long? SourcePackageRevision, string BodyHash, byte[] Cipher);

    private Guid Tenant => db.CurrentTenantId;

    internal Task<DateTimeOffset> NowAsync(CancellationToken ct) =>
        db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    internal static DateTimeOffset Milliseconds(DateTimeOffset time) =>
        DateTimeOffset.FromUnixTimeMilliseconds(time.ToUnixTimeMilliseconds());

    internal async Task<Coverage?> ReadCoverageAsync(EngineeringIntakeScopeV1 scope, CancellationToken ct) =>
        (await db.Database.SqlQuery<Coverage>($"""
            SELECT generation AS "Generation", evidence_key AS "EvidenceKey" FROM cp6_engineering_intake_v1.coverage
            WHERE tenant_id={Tenant} AND authority={scope.Authority} AND legal_entity_key={scope.LegalEntityKey} AND enabled FOR SHARE
            """).ToArrayAsync(ct)).SingleOrDefault();

    internal Task HoldCaseAsync(EngineeringCaseNaturalKeyV1 key, CancellationToken ct) =>
        LockAsync(CommercialCanonicalV1.Digest("HO-ENG-CASE-LOCK-1", key), ct);

    internal Task HoldRequestAsync(Guid key, CancellationToken ct) =>
        LockAsync(CommercialCanonicalV1.Digest("HO-ENG-REQUEST-LOCK-1", new { Tenant, key }), ct);

    private Task LockAsync(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended({key},0))", ct);

    internal async Task<Slot?> ReadSlotAsync(EngineeringCaseNaturalKeyV1 key, CancellationToken ct) =>
        (await db.Database.SqlQuery<Slot>($"""
            SELECT case_id AS "CaseId", case_revision AS "CaseRevision", current_es AS "CurrentEs",
                current_request_key AS "CurrentRequestKey", execution_generation AS "ExecutionGeneration"
            FROM cp6_engineering_intake_v1.cases WHERE tenant_id={Tenant} AND authority={key.TargetAuthority}
                AND legal_entity_key={key.LegalEntityKey} AND opportunity_id={key.OpportunityId} FOR SHARE
            """).ToArrayAsync(ct)).SingleOrDefault();

    internal Task<bool> ConflictingIdentityAsync(EngineeringIntakeRequestV1 request, bool initial, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT (
                ({initial} AND EXISTS (SELECT FROM cp6_engineering_intake_v1.cases
                    WHERE tenant_id={Tenant} AND case_id={request.CaseId}))
                OR EXISTS (SELECT FROM cp6_engineering_intake_v1.requests
                    WHERE tenant_id={Tenant} AND event_id={request.EventId})
            ) AS "Value"
            """).SingleAsync(ct);

    internal async Task<Received?> ReadRequestAsync(Guid key, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<RequestRow>($"""
            SELECT case_id AS "CaseId", submission_version AS "SubmissionVersion", event_id AS "EventId",
                source_digest AS "SourceDigest", source_body_hash AS "SourceBodyHash", body_hash AS "BodyHash",
                original_cipher AS "Cipher", first_received_at AS "FirstReceivedAt"
            FROM cp6_engineering_intake_v1.requests WHERE tenant_id={Tenant} AND request_key={key}
            """).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var raw = Unseal(key, RequestContract, row.Cipher, row.BodyHash);
        var request = ReadStored<EngineeringIntakeRequestV1>(raw.Bytes);
        if (request.TenantId != Tenant || request.RequestKey != key || request.CaseId != row.CaseId
            || request.SubmissionVersion != Version(row.SubmissionVersion) || request.EventId != row.EventId
            || request.SourceBinding.SourceDigest != row.SourceDigest || Hash(SourceBytes(raw.Bytes)) != row.SourceBodyHash
            || raw.Trust.TenantId != Tenant || raw.Trust.ContractVersion != RequestContract
            || raw.Trust.Producer != request.Producer || raw.Trust.Epoch != request.Epoch || raw.Trust.VerifiedAt != row.FirstReceivedAt)
            throw OriginalUnavailable();
        return new(request, raw.Bytes, row.BodyHash, row.SourceBodyHash, row.FirstReceivedAt);
    }

    internal async Task<Terminal> ReadTerminalAsync(Received original, CancellationToken ct)
    {
        var request = original.Envelope;
        var key = request.RequestKey;
        var row = await db.Database.SqlQuery<ResultRow>($"""
            SELECT receipt_id AS "ReceiptId", outcome AS "Outcome", engineering_id AS "EngineeringId",
                engineering_revision AS "EngineeringRevision", source_package_id AS "SourcePackageId",
                source_package_revision AS "SourcePackageRevision", body_hash AS "BodyHash", original_cipher AS "Cipher"
            FROM cp6_engineering_intake_v1.intake_results WHERE tenant_id={Tenant} AND request_key={key}
            """).SingleOrDefaultAsync(ct) ?? throw OriginalUnavailable();
        var raw = Unseal(key, IntakeContract, row.Cipher, row.BodyHash);
        var result = ReadStored<EngineeringIntakeResultV1>(raw.Bytes);
        var engineering = row.EngineeringId is { } id && row.EngineeringRevision is { } rev
            ? new EngineeringIntakeRefV1(id.ToString("D"), Version(rev)) : null;
        if (result.TenantId != Tenant || result.SchemaVersion != IntakeContract || result.ReceiptId != row.ReceiptId
            || result.Outcome != row.Outcome || result.Engineering != engineering || result.Source != ResultSource(request)
            || result.Sequence != new EngineeringIntakeSequenceV1("1", null, null) || raw.Trust.TenantId != Tenant
            || raw.Trust.ContractVersion != IntakeContract || raw.Trust.Epoch != result.Epoch || raw.Trust.Producer != result.Producer)
            throw OriginalUnavailable();
        var item = new EngineeringIntakeOriginalItemV1(IntakeContract, result.ReceiptId, result.Sequence.Ordinal,
            Convert.ToBase64String(raw.Bytes), row.BodyHash, raw.Trust);
        return new(result, item, row.SourcePackageId, row.SourcePackageRevision);
    }

    internal Task InsertRequestAsync(EngineeringIntakeRequestV1 request, byte[] bytes, EngineeringIntakeTrustV1 trust, CancellationToken ct)
    {
        var cipher = Seal(request.RequestKey, RequestContract, new(bytes, trust));
        var sourceHash = Hash(SourceBytes(bytes));
        var bodyHash = Hash(bytes);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_intake_v1.requests
               (tenant_id,request_key,case_id,submission_version,event_id,source_digest,source_body_hash,body_hash,original_cipher,first_received_at)
            VALUES ({Tenant},{request.RequestKey},{request.CaseId},{CommercialWireV1.Revision(request.SubmissionVersion)},{request.EventId},
               {request.SourceBinding.SourceDigest},{sourceHash},{bodyHash},{cipher},{trust.VerifiedAt})
            """, ct);
    }

    internal async Task CompleteAsync(EngineeringIntakeRequestV1 request, Slot? slot, EngineeringIntakeResultV1 result,
        byte[] bytes, EngineeringIntakeTrustV1 trust, Guid? packageId, long? packageRevision, CancellationToken ct)
    {
        var cipher = Seal(request.RequestKey, IntakeContract, new(bytes, trust));
        var bodyHash = Hash(bytes);
        var id = result.Engineering is null ? (Guid?)null : Guid.Parse(result.Engineering.EngineeringRequestId);
        var revision = result.Engineering is null ? (long?)null : CommercialWireV1.Revision(result.Engineering.EngineeringRevision);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_intake_v1.intake_results
                (tenant_id,request_key,receipt_id,outcome,engineering_id,engineering_revision,source_package_id,source_package_revision,body_hash,original_cipher)
            VALUES ({Tenant},{request.RequestKey},{result.ReceiptId},{result.Outcome},{id},{revision},{packageId},{packageRevision},{bodyHash},{cipher})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_intake_v1.outbox(tenant_id,request_key) VALUES ({Tenant},{request.RequestKey})
            """, ct);
        var generation = Guid.NewGuid();
        var natural = request.CaseNaturalKey;
        if (slot is null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_intake_v1.cases
                    (tenant_id,case_id,authority,legal_entity_key,opportunity_id,case_revision,current_es,current_request_key,execution_generation)
                VALUES ({Tenant},{request.CaseId},{natural.TargetAuthority},{natural.LegalEntityKey},{natural.OpportunityId},
                    1,1,{request.RequestKey},{generation})
                """, ct);
        }
        else if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_engineering_intake_v1.cases SET case_revision=case_revision+1,current_es=current_es+1,
                current_request_key={request.RequestKey},execution_generation={generation}
            WHERE tenant_id={Tenant} AND case_id={slot.CaseId} AND case_revision={slot.CaseRevision}
                AND current_es={slot.CurrentEs} AND current_request_key={slot.CurrentRequestKey}
                AND execution_generation={slot.ExecutionGeneration}
            """, ct) != 1) throw Failure("STALE_NEXT_CONTEXT", 409);
    }

    internal static EngineeringIntakeResultSourceV1 ResultSource(EngineeringIntakeRequestV1 request) =>
        new(request.CaseNaturalKey, request.CaseId, request.SubmissionVersion, request.RequestKey, request.SourceBinding);

    private IDataProtector Protector(Guid key, string stream) =>
        protection.CreateProtector(SourceOwner, Tenant.ToString("D"), key.ToString("D"), stream);

    private byte[] Seal(Guid key, string stream, ProtectedOriginal value) =>
        Protector(key, stream).Protect(JsonSerializer.SerializeToUtf8Bytes(value, WireJson));

    private ProtectedOriginal Unseal(Guid key, string stream, byte[] cipher, string bodyHash)
    {
        try
        {
            var original = JsonSerializer.Deserialize<ProtectedOriginal>(Protector(key, stream).Unprotect(cipher), WireJson);
            return original is { Bytes: not null, Trust: not null } && Hash(original.Bytes) == bodyHash
                ? original : throw OriginalUnavailable();
        }
        catch (Exception error) when (error is CryptographicException or JsonException) { throw OriginalUnavailable(); }
    }

    private static T ReadStored<T>(byte[] bytes) where T : class
    {
        try { return Read<T>(bytes); }
        catch (CP6.Core.Services.Erp.OrderPriceCorrectionException) { throw OriginalUnavailable(); }
    }

    internal static CP6.Core.Services.Erp.OrderPriceCorrectionException OriginalUnavailable() =>
        Failure("ENGINEERING_INTAKE_ORIGINAL_UNPROVEN", 503);
}
