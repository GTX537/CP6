using System.Globalization;
using System.Text.Json;
using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private const string DecisionSchema = "ACC-MAPPING-DECISION/1", ProofSchema = "ACC-MAPPING-AUTHORITY-PROOF/1",
        IdentityPolicySchema = "ACC-IDENTITY-IMPACT/1", IdentityPolicyEvidenceId = "bp-identity-policy-v1",
        DecisionProofPurpose = "CRM_CURRENT_MAPPING_DECISION";
    private static readonly byte[] IdentityPolicyBytes = CommercialCanonicalV1.Data(new
    {
        schemaVersion = IdentityPolicySchema, version = "1",
        fields = new[] { "displayName", "aliases", "domains", "accountType", "countryCode", "province", "city", "address" },
        normalizationVersion = "ACC-NORM/1"
    });

    private sealed record DecisionScope(string MappingAuthority, Guid ScopeIdentity, string ScopeEpoch, Guid OrganizationId,
        string LegalEntityKey, Guid AccountId, string MappingPurpose = "ACCOUNT_BUSINESS_PARTNER");
    private sealed record DecisionPointer(Guid DecisionId, string Position, string Digest);
    private sealed record DecisionCandidate(Guid OwnerMappingId, string OwnerMappingRevision, string BusinessPartnerKey,
        string BoundAccountBusinessVersion, string CandidateDigest);
    private sealed record DecisionOwnerRef(string Owner, string ContractId, string ContractVersion, string EvidenceId,
        string EvidenceVersion, string ContentDigest);
    private sealed record MappingDecision(string SchemaVersion, DecisionScope Scope, DecisionPointer Decision, string Basis,
        DecisionPointer? Predecessor, DecisionCandidate[] Candidates, string? SelectedCandidateDigest, string DecisionKind,
        string[] CoveredCandidateDigests, string[] CoveredConflictDigests, string CompleteAtWatermark, bool CoversEntireScope,
        string EffectiveFrom, string? EffectiveUntil, DecisionOwnerRef IdentityImpactPolicy, DecisionOwnerRef AuthorityProof);
    private sealed record DecisionProof(string SchemaVersion, string Issuer, DecisionScope Scope, string DecisionDigest,
        string CompleteAtWatermark, string SignedAt, string Purpose, string KeyId, string Algorithm, string SignatureBase64);

    internal sealed class OwnerDecisionRow
    {
        public long Position { get; init; }
        public long MappingRevision { get; init; }
        public Guid DecisionId { get; init; }
        public string ScopeEpoch { get; init; } = "";
        public string AuthorityHash { get; init; } = "";
        public string Digest { get; init; } = "";
        public byte[] Body { get; init; } = [];
        public byte[] Proof { get; init; } = [];
    }

    private Task<OwnerDecisionRow?> GetOwnerDecision(string legal, Guid account, long? position, CancellationToken ct)
        => position is null ? Task.FromResult<OwnerDecisionRow?>(null) : db.Database.SqlQuery<OwnerDecisionRow>($"""
            SELECT position AS "Position",mapping_revision AS "MappingRevision",decision_id AS "DecisionId",scope_epoch AS "ScopeEpoch",
                authority_hash AS "AuthorityHash",digest AS "Digest",decision_body AS "Body",proof_body AS "Proof"
              FROM main_bp.mapping_owner_decisions
             WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account} AND position={position}
            """).SingleOrDefaultAsync(ct);

    private async Task AppendOwnerDecision(string legal, Guid account, MappingSlot slot, MappingRevisionRow current,
        MappingAccount identity, DateTimeOffset at, CancellationToken ct)
    {
        var authority = await MappingAuthority(legal, DecisionAuthorityPurpose, ct);
        var accountAuthority = await MappingAuthority(legal, AccountIdentityPurpose, ct);
        if (authority is null || accountAuthority is null || accountAuthority.OrganizationId != authority.OrganizationId ||
            MappingAuthorityHash(accountAuthority) != identity.AuthorityHash) return;
        var preceding = await GetOwnerDecision(legal, account, slot.DecisionPosition, ct);
        var authorityHash = MappingAuthorityHash(authority);
        // 新 epoch 需要独立的关闭证明和初始完整快照。普通映射动作不能替 Owner 注册新 epoch。
        if (preceding is not null && (preceding.ScopeEpoch != authority.Epoch || preceding.AuthorityHash != authorityHash ||
            DecodeOwnerDecision(preceding, authority) is null)) return;

        var scope = new DecisionScope(authority.Authority, slot.ScopeIdentity, authority.Epoch, authority.OrganizationId, legal, account);
        var position = NextMappingNumber(preceding?.Position ?? 0);
        var id = Guid.NewGuid();
        var pointer = new DecisionPointer(id, Revision(position), "");
        var predecessor = preceding is null ? null : new DecisionPointer(preceding.DecisionId, Revision(preceding.Position), preceding.Digest);
        DecisionCandidate? candidate = null;
        if (current.BpKey is { } code)
        {
            candidate = new(slot.ScopeIdentity, Revision(current.Revision), code, identity.IdentityVersion, "");
            candidate = candidate with { CandidateDigest = CandidateDigest(candidate) };
        }
        var old = preceding is null ? null : DecodeOwnerDecision(preceding, authority)?.SelectedCandidateDigest;
        var covered = new[] { old, candidate?.CandidateDigest }.OfType<string>().Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var watermark = "main-bp:" + slot.ScopeIdentity.ToString("D") + ":" + Revision(position);
        var policy = new DecisionOwnerRef(authority.Authority, "ACC-IDENTITY-IMPACT", "1", IdentityPolicyEvidenceId, "1", RawDigest(IdentityPolicyBytes));
        var decision = new MappingDecision(DecisionSchema, scope, pointer, preceding is null ? "COMPLETE_SNAPSHOT" : "SUCCESSOR", predecessor,
            candidate is null ? [] : [candidate], candidate?.CandidateDigest, candidate is null ? "UNLINK" : "SELECT", covered, [], watermark,
            true, Instant(at), null, policy, new(authority.Authority, "ACC-MAPPING-AUTHORITY-PROOF", "1", id.ToString("D"), "1", ""));
        var digest = DecisionDigest(decision);
        decision = decision with { Decision = pointer with { Digest = digest } };
        var proof = new DecisionProof(ProofSchema, authority.Authority, scope, digest, watermark, Instant(at), DecisionProofPurpose,
            authority.KeyId, "Ed25519", "");
        var signature = mappingAuthority!.SignDecision(authority, DecisionSigningBytes(proof));
        if (signature is null) return;
        proof = proof with { SignatureBase64 = Convert.ToBase64String(signature) };
        var proofBytes = CommercialCanonicalV1.Data(proof);
        decision = decision with { AuthorityProof = decision.AuthorityProof with { ContentDigest = RawDigest(proofBytes) } };
        var body = CommercialCanonicalV1.Data(decision);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_owner_decisions(tenant_id,legal_entity_key,account_id,position,decision_id,authority_hash,
                mapping_revision,scope_epoch,digest,decision_body,proof_body)
            VALUES({Tenant},{legal},{account},{position},{id},{authorityHash},{current.Revision},{scope.ScopeEpoch},{digest},{body},{proofBytes})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.mapping_slots SET decision_position={position}
             WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account}
            """, ct);
    }

    private static string CandidateDigest(DecisionCandidate value) => CommercialCanonicalV1.Digest("ACC-MAPPING-CANDIDATE-1", new
    {
        value.OwnerMappingId, value.OwnerMappingRevision, value.BusinessPartnerKey, value.BoundAccountBusinessVersion
    });

    private static string DecisionDigest(MappingDecision value) => CommercialCanonicalV1.Digest("ACC-MAPPING-DECISION-1", new
    {
        value.SchemaVersion, value.Scope, decision = new { value.Decision.DecisionId, value.Decision.Position }, value.Basis,
        value.Predecessor, value.Candidates, value.SelectedCandidateDigest, value.DecisionKind, value.CoveredCandidateDigests,
        value.CoveredConflictDigests, value.CompleteAtWatermark, value.CoversEntireScope, value.EffectiveFrom, value.EffectiveUntil,
        value.IdentityImpactPolicy
    });

    private static byte[] DecisionSigningBytes(DecisionProof proof)
        => [.. Bytes("ACC-MAPPING-AUTHORITY-PROOF-1\n"), .. CommercialCanonicalV1.Data(new
        {
            proof.SchemaVersion, proof.Issuer, proof.Scope, proof.DecisionDigest, proof.CompleteAtWatermark,
            proof.SignedAt, proof.Purpose, proof.KeyId, proof.Algorithm
        })];

    private MappingDecision? DecodeOwnerDecision(OwnerDecisionRow row, BusinessPartnerMappingAuthorityBindingV1 authority)
        => mappingAuthority is null ? null : DecodeOwnerDecision(row, authority, mappingAuthority);

    private static MappingDecision? DecodeOwnerDecision(OwnerDecisionRow row, BusinessPartnerMappingAuthorityBindingV1 authority,
        BusinessPartnerMappingAuthorityV1 verifier)
    {
        if (row.AuthorityHash != MappingAuthorityHash(authority) || row.ScopeEpoch != authority.Epoch) return null;
        try
        {
            var decision = JsonSerializer.Deserialize<MappingDecision>(row.Body, CommercialCanonicalV1.Json);
            var proof = JsonSerializer.Deserialize<DecisionProof>(row.Proof, CommercialCanonicalV1.Json);
            if (decision is null || proof is null || decision.SchemaVersion != DecisionSchema || decision.Decision.DecisionId != row.DecisionId ||
                decision.Decision.Position != Revision(row.Position) || decision.Decision.Digest != row.Digest ||
                DecisionDigest(decision) != row.Digest || decision.Scope != proof.Scope ||
                decision.Scope.MappingAuthority != authority.Authority || decision.Scope.ScopeEpoch != authority.Epoch ||
                decision.Scope.OrganizationId != authority.OrganizationId || decision.Scope.LegalEntityKey != authority.LegalEntityKey ||
                decision.Scope.MappingPurpose != "ACCOUNT_BUSINESS_PARTNER" || proof.Issuer != authority.Authority ||
                proof.SchemaVersion != ProofSchema || proof.DecisionDigest != row.Digest || proof.CompleteAtWatermark != decision.CompleteAtWatermark ||
                proof.KeyId != authority.KeyId || proof.Algorithm != "Ed25519" || proof.Purpose != DecisionProofPurpose ||
                proof.SignedAt != decision.EffectiveFrom || decision.AuthorityProof.Owner != authority.Authority ||
                decision.AuthorityProof.ContractId != "ACC-MAPPING-AUTHORITY-PROOF" || decision.AuthorityProof.ContractVersion != "1" ||
                decision.AuthorityProof.EvidenceId != row.DecisionId.ToString("D") || decision.AuthorityProof.EvidenceVersion != "1" ||
                decision.AuthorityProof.ContentDigest != RawDigest(row.Proof) ||
                decision.IdentityImpactPolicy != new DecisionOwnerRef(authority.Authority, "ACC-IDENTITY-IMPACT", "1", IdentityPolicyEvidenceId, "1",
                    RawDigest(IdentityPolicyBytes)) ||
                !decision.CoversEntireScope || decision.Candidates.Any(candidate => candidate.CandidateDigest != CandidateDigest(candidate)) ||
                decision.DecisionKind != (decision.Candidates.Length == 0 ? "UNLINK" : "SELECT") ||
                decision.Candidates.Length > 1 || decision.Candidates.SingleOrDefault()?.CandidateDigest != decision.SelectedCandidateDigest ||
                row.Position == 1 && (decision.Predecessor is not null || decision.Basis != "COMPLETE_SNAPSHOT") ||
                row.Position > 1 && (decision.Predecessor?.Position != Revision(row.Position - 1) || decision.Basis != "SUCCESSOR") ||
                !verifier.VerifyDecision(authority, DecisionSigningBytes(proof), Convert.FromBase64String(proof.SignatureBase64)))
                return null;
            return decision;
        }
        catch (Exception e) when (e is JsonException or FormatException or CommercialProtocolException) { return null; }
    }

    internal static bool VerifyCurrentOwnerSelection(OwnerDecisionRow row, BusinessPartnerMappingAuthorityBindingV1 binding,
        BusinessPartnerMappingAuthorityV1 verifier, Guid account, Guid scope, long revision, string code, string accountVersion, DateTimeOffset now)
    {
        var decision = DecodeOwnerDecision(row, binding, verifier);
        if (decision is null || decision.Scope.AccountId != account || decision.Scope.ScopeIdentity != scope ||
            decision.Candidates is not [var chosen] || chosen.OwnerMappingId != scope || chosen.OwnerMappingRevision != Revision(revision) ||
            chosen.BusinessPartnerKey != code || chosen.BoundAccountBusinessVersion != accountVersion) return false;
        try
        {
            var from = MappingUtc(JsonSerializer.SerializeToElement(decision.EffectiveFrom));
            return from <= now && (decision.EffectiveUntil is null || now < MappingUtc(JsonSerializer.SerializeToElement(decision.EffectiveUntil)));
        }
        catch (OrderPriceCorrectionException) { return false; }
    }

    private static DecisionScope ParseDecisionScope(JsonElement original)
    {
        var fields = Exact(original, "mappingAuthority", "scopeIdentity", "scopeEpoch", "organizationId", "legalEntityKey", "accountId", "mappingPurpose");
        if (Token(fields["mappingPurpose"], 64) != "ACCOUNT_BUSINESS_PARTNER") throw Invalid();
        return new(Token(fields["mappingAuthority"], 120), Uuid(fields["scopeIdentity"]), Token(fields["scopeEpoch"], 120),
            Uuid(fields["organizationId"]), LegalKey(Token(fields["legalEntityKey"], 64)), Uuid(fields["accountId"]));
    }

    private static DecisionPointer? ParseDecisionPointer(JsonElement original)
    {
        if (original.ValueKind == JsonValueKind.Null) return null;
        var fields = Exact(original, "decisionId", "position", "digest");
        return new(Uuid(fields["decisionId"]), Revision(Version(fields["position"])), Digest(fields["digest"]));
    }

    public async Task<HttpResult?> QueryMappingDecision(string client, JsonElement input, CancellationToken ct)
    {
        var fields = Exact(input, "scope", "knownDecision");
        var scope = ParseDecisionScope(fields["scope"]);
        var known = ParseDecisionPointer(fields["knownDecision"]);
        HttpResult Knowledge(string value, MappingDecision? found = null) => new(new { decision = found, knowledge = value, asOf = Instant(Now) });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BpUseScopeGate.LockAsync(db, scope.LegalEntityKey, false, ct);
        if (!await CanReadMappingSource(client, scope.LegalEntityKey, "OWNER_DECISION", ct)) return null;
        var authority = await MappingAuthority(scope.LegalEntityKey, DecisionAuthorityPurpose, ct);
        var slot = await ReadMappingSlot(scope.LegalEntityKey, scope.AccountId, false, ct);
        if (slot is null || authority is null || scope.ScopeIdentity != slot.ScopeIdentity || scope.MappingAuthority != authority.Authority ||
            scope.ScopeEpoch != authority.Epoch || scope.OrganizationId != authority.OrganizationId) return Knowledge("UNKNOWN");
        if (slot.OwnerConflict) return Knowledge("CONFLICT");
        var current = await GetOwnerDecision(scope.LegalEntityKey, scope.AccountId, slot.DecisionPosition, ct);
        if (current is null || current.MappingRevision != slot.Revision) return Knowledge("UNKNOWN");
        if (known is not null)
        {
            var pos = long.Parse(known.Position, CultureInfo.InvariantCulture);
            var exact = await GetOwnerDecision(scope.LegalEntityKey, scope.AccountId, pos, ct);
            if (exact is null || exact.Digest != known.Digest || exact.DecisionId != known.DecisionId) return Knowledge("CONFLICT");
        }
        var decision = DecodeOwnerDecision(current, authority);
        return decision is null || decision.Scope != scope ? Knowledge("UNKNOWN") : Knowledge("PROVEN", decision);
    }

    public async Task<byte[]?> ReadMappingDecisionEvidence(string client, string? legalKey, string? accountKey,
        string? evidenceId, CancellationToken ct)
    {
        var legal = LegalKey(legalKey);
        var account = ParseId(accountKey);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BpUseScopeGate.LockAsync(db, legal, false, ct);
        if (!await CanReadMappingSource(client, legal, "OWNER_DECISION", ct)) return null;
        var authority = await MappingAuthority(legal, DecisionAuthorityPurpose, ct);
        var slot = await ReadMappingSlot(legal, account, false, ct);
        if (authority is null || slot?.DecisionPosition is null) return null;
        if (evidenceId == IdentityPolicyEvidenceId) return IdentityPolicyBytes;
        var id = ParseId(evidenceId);
        var position = await db.Database.SqlQuery<long>($"""
            SELECT position AS "Value" FROM main_bp.mapping_owner_decisions
             WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account} AND decision_id={id}
            """).SingleOrDefaultAsync(ct);
        var row = await GetOwnerDecision(legal, account, position, ct);
        return row is not null && DecodeOwnerDecision(row, authority) is not null ? row.Proof : null;
    }

    public async Task<HttpResult?> QueryMappingSnapshot(string client, JsonElement input, CancellationToken ct)
    {
        var fields = Exact(input, "legalEntityKey", "accountId", "knownRevision");
        var legal = LegalKey(Token(fields["legalEntityKey"], 64));
        var account = Uuid(fields["accountId"]);
        _ = OptionalVersion(fields["knownRevision"]);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BpUseScopeGate.LockAsync(db, legal, false, ct);
        if (!await CanReadMappingSource(client, legal, "SNAPSHOT", ct)) return null;
        var slot = await ReadMappingSlot(legal, account, false, ct);
        var revision = await MappingRevisionAt(legal, account, slot?.Revision, ct);
        if (slot is null || revision is null)
            return new HttpResult(new { knowledge = "UNSEEN", current = (object?)null, sourceAsOf = (string?)null, originalEvents = System.Array.Empty<object>() });
        var row = await FindMappingEvent(legal, account, null, revision.Revision, true, ct);
        try
        {
            var original = RequireMappingEvent(row);
            if (original.BpKey != revision.BpKey) throw Rejected("MAPPING_ORIGINAL_UNPROVEN", 503);
            return new HttpResult(new { knowledge = "FOUND", current = MappingRef(legal, account, revision),
                sourceAsOf = Instant(row!.SourceAsOf), originalEvents = new[] { new { row.EventId, bodyBase64 = Convert.ToBase64String(row.Body), row.BodyHash } } });
        }
        catch (OrderPriceCorrectionException)
        {
            return new HttpResult(new { knowledge = "UNPROVEN", current = (object?)null, sourceAsOf = (string?)null, originalEvents = System.Array.Empty<object>() });
        }
    }

    private async Task<bool> CanReadMappingSource(string client, string legal, string purpose, CancellationToken ct)
        => (await db.Database.SqlQuery<bool>($"""
            SELECT r.enabled AS "Value" FROM main_bp.mapping_readers r JOIN main_bp.scopes s USING(tenant_id,legal_entity_key)
             WHERE r.tenant_id={Tenant} AND r.legal_entity_key={legal} AND r.client_id={client} AND r.purpose={purpose} AND r.enabled AND s.enabled
             FOR SHARE OF r,s
            """).ToListAsync(ct)).SingleOrDefault();
}
