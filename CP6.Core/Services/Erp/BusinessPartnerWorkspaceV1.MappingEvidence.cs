using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private sealed record DecisionSubject(string LegalEntityKey, Guid AccountId, string Action, string? ExpectedMappingRevision,
        Guid? TargetBpId, Guid? CorrectionContextId, string Reason);
    private sealed record DecisionEvidenceRequest(Guid ActorId, DecisionSubject Subject, MappingSourceReference[] Evidence);
    private sealed record EvidenceRecord(MappingSourceReference Reference, byte[] Original);
    private sealed record MappingEvidence(string GuardKey, string GuardVersion, long GuardGeneration, string AuthorityHash, string SubjectHash,
        MappingSourceReference[] Evidence, byte[] Original, byte[] ReceivedTrust);
    private sealed record DecisionBindingResponse(Guid? BindingId, string SubjectHash, MappingSourceReference[] Evidence,
        string Availability, string? BindingHash, string? ExpiresAt);

    public async Task<HttpResult> BindMappingDecision(JsonElement input, CancellationToken ct)
    {
        var fields = Exact(input, "subject", "evidence");
        var subject = ParseDecisionSubject(fields["subject"]);
        var legal = subject.LegalEntityKey;
        var account = subject.AccountId;
        var evidence = SourceReferences(fields["evidence"]);
        if (evidence.Length == 0 || evidence.Any(x => x.Purpose != DecisionEvidencePurpose)) throw Invalid("DECISION_EVIDENCE_REQUIRED");
        var subjectHash = Hash(subject);
        HttpResult Unproven(string availability = "UNPROVEN") =>
            new(new DecisionBindingResponse(null, subjectHash, [], availability, null, null));

        var check = await owner.ExecuteAsync(Resource, MappingAction, false, async (access, token) =>
        {
            await MappingDecisionAccess(access, subject, token);
            return new MappingBridgeCheck(access.ReadScopeVersion, access.ActorId, await MappingAuthority(legal, DecisionEvidencePurpose, token));
        }, ct);
        if (check.Binding is null) return Unproven();
        var request = Canonical(new DecisionEvidenceRequest(check.Actor, subject, evidence));
        var original = await mappingAuthority!.ReadAsync(check.Binding, "DECISION_EVIDENCE", request, ct);
        if (original is null) return Unproven();
        MappingEvidence approved;
        try
        {
            var raw = ParseOriginal(original.Body);
            if (raw.TryGetProperty("availability", out var status) && status.ValueKind == JsonValueKind.String &&
                status.GetString() == "NOT_AUTHORIZED") return Unproven("NOT_AUTHORIZED");
            var expected = Exact(raw, "availability", "subjectHash", "guardKey", "guardVersion", "records");
            if (Token(expected["availability"], 20) != "VERIFIED" || Digest(expected["subjectHash"]) != subjectHash) return Unproven();
            var entries = Array(expected["records"], 50).Select(ParseEvidenceRecord).ToArray();
            if (!entries.Select(x => x.Reference).SequenceEqual(evidence)) return Unproven();
            approved = new(Token(expected["guardKey"], 160), Token(expected["guardVersion"], 120), 0, MappingAuthorityHash(check.Binding),
                subjectHash, evidence, original.Body, original.ReceivedTrust);
        }
        catch (Exception ex) when (ex is OrderPriceCorrectionException or JsonException or FormatException or InvalidOperationException)
        { return Unproven(); }

        return await owner.ExecuteAsync(Resource, MappingAction, true, async (access, token) =>
        {
            var scope = await MappingDecisionAccess(access, subject, token);
            var generation = await EvidenceGuardGeneration(legal, account, DecisionEvidencePurpose, approved.GuardKey,
                approved.GuardVersion, approved.AuthorityHash, token);
            if (access.ActorId != check.Actor || access.ReadScopeVersion != check.Permission || generation is null)
                return Unproven();
            var id = Guid.NewGuid();
            var expiry = MappingExpires();
            var hash = Hash(new { bindingId = id, subjectHash, evidence, expiresAt = Instant(expiry) });
            await SaveMappingArtifact("BINDING", id, access, scope, account, expiry, hash, approved with { GuardGeneration = generation.Value }, token);
            return new HttpResult(new DecisionBindingResponse(id, subjectHash, evidence, "VERIFIED", hash, Instant(expiry)));
        }, ct);
    }

    private static EvidenceRecord ParseEvidenceRecord(JsonElement raw)
    {
        var fields = Exact(raw, "reference", "originalBodyBase64");
        var reference = SourceReference(fields["reference"]);
        var original = DecodeBody(fields["originalBodyBase64"]);
        if (RawDigest(original) != reference.Digest) throw Invalid("DECISION_EVIDENCE_UNPROVEN");
        return new(reference, original);
    }

    private static byte[] DecodeBody(JsonElement encoded)
        => Convert.FromBase64String(Token(encoded, 2 * 1024 * 1024));

    private async Task<Scope> MappingDecisionAccess(OperationsAccessV1 access, DecisionSubject subject, CancellationToken ct)
    {
        var scope = await MappingAccess(access, subject.LegalEntityKey, false, ct);
        await access.ActionAsync(Resource, MappingRead, ct);
        if (subject.Action == "CORRECT_CURRENT") await access.ActionAsync(Resource, MappingCorrect, ct);
        var current = await MappingRevisionAt(scope.Key, subject.AccountId,
            (await ReadMappingSlot(scope.Key, subject.AccountId, false, ct))?.Revision, ct);
        if (OptionalRevision(current?.Revision) != subject.ExpectedMappingRevision) throw Stale();
        await ViewMappingHead(access, scope.Key, subject.AccountId, current, ct);
        if (subject.TargetBpId is { } target && (await VisibleAsync(access, target, false, ct)).Head?.Legal != scope.Key)
            throw Missing();
        if (subject.CorrectionContextId is { } correction)
            await RequireMappingCorrection(access, scope.Key, subject.AccountId, correction, current, ct);
        return scope;
    }

    private static DecisionSubject ParseDecisionSubject(JsonElement value)
    {
        var fields = Exact(value, "legalEntityKey", "accountId", "action", "expectedMappingRevision", "targetBpId", "correctionContextId", "reason");
        var result = new DecisionSubject(LegalKey(Token(fields["legalEntityKey"], 64)), Uuid(fields["accountId"]),
            Token(fields["action"], 20), OptionalRevision(OptionalVersion(fields["expectedMappingRevision"])),
            OptionalId(fields["targetBpId"]), OptionalId(fields["correctionContextId"]), Text(fields["reason"], 200));
        if (result.Action is not ("LINK" or "REPLACE" or "UNLINK" or "CORRECT_CURRENT") ||
            (result.Action == "CORRECT_CURRENT") != result.CorrectionContextId.HasValue ||
            result.Action is "LINK" or "REPLACE" && result.TargetBpId is null ||
            result.Action == "UNLINK" && result.TargetBpId is not null)
            throw Invalid();
        return result;
    }

    private async Task<long?> EvidenceGuardGeneration(string legal, Guid account, string purpose, string guardKey,
        string guardVersion, string authorityHash, CancellationToken ct)
    {
        var authority = await MappingAuthority(legal, purpose, ct);
        if (authority is null || authorityHash != MappingAuthorityHash(authority)) return null;
        return (await db.Database.SqlQuery<long?>($"""
            SELECT generation AS "Value" FROM main_bp.mapping_evidence_guards
             WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account} AND purpose={purpose}
               AND guard_key={guardKey} AND guard_version={guardVersion} AND authority_hash={authorityHash} AND active FOR SHARE
            """).ToListAsync(ct)).SingleOrDefault();
    }

    private async Task RequireDecisionBinding(OperationsAccessV1 access, DecisionSubject subject, MappingSourceReference[] evidence,
        Guid? binding, MappingSourceReference[] correctionEvidence, CancellationToken ct)
    {
        if (evidence.Length == 0 && binding is null && subject.Action is not ("REPLACE" or "UNLINK" or "CORRECT_CURRENT"))
            return;
        var legal = subject.LegalEntityKey;
        var account = subject.AccountId;
        if (binding is null || evidence.Length == 0 || evidence.Any(x => x.Purpose != DecisionEvidencePurpose) ||
            correctionEvidence.Any(required => !evidence.Contains(required)))
            throw Rejected("DECISION_EVIDENCE_REQUIRED");
        var saved = await FindMappingArtifact("BINDING", binding.Value, ct);
        EnsureArtifact(saved, access, legal, account, Now);
        var approved = OpenArtifact<MappingEvidence>("BINDING", saved!);
        var subjectHash = Hash(subject);
        var hash = Hash(new { bindingId = binding, subjectHash, evidence, expiresAt = Instant(saved!.Expires) });
        if (saved.Hash != hash || approved.SubjectHash != subjectHash || !approved.Evidence.SequenceEqual(evidence) ||
            approved.Original.Length == 0 || approved.ReceivedTrust.Length == 0 ||
            await EvidenceGuardGeneration(legal, account, DecisionEvidencePurpose, approved.GuardKey, approved.GuardVersion, approved.AuthorityHash, ct)
                != approved.GuardGeneration)
            throw Rejected("DECISION_EVIDENCE_UNPROVEN", 409);
    }
}
