using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private const string MappingAction = "mapping", MappingRead = "mapping.read",
        MappingCorrect = "mapping.correct", MappingReconcile = "mapping.reconcile";
    private const string AccountIdentityPurpose = "ACCOUNT_IDENTITY", DecisionEvidencePurpose = "BP_MAPPING_DECISION",
        ProjectionPurpose = "CRM_PROJECTION", DecisionAuthorityPurpose = "ACC_MAPPING_AUTHORITY";
    private static readonly string[] MappingActions = ["LINK", "CREATE_AND_LINK", "REPLACE", "UNLINK", "CONFIRM", "CORRECT_CURRENT"];

    private sealed class MappingAuthorityRow
    {
        public Guid OrganizationId { get; init; }
        public string Authority { get; init; } = "";
        public string Epoch { get; init; } = "";
        public string KeyId { get; init; } = "";
        public byte[] PublicKey { get; init; } = [];
        public string RouteKey { get; init; } = "";
    }

    private sealed class MappingSlot
    {
        public Guid ScopeIdentity { get; init; }
        public long? Revision { get; init; }
        public long Sequence { get; init; }
        public long Generation { get; init; }
        public bool OwnerConflict { get; init; }
        public Guid? ProjectionId { get; init; }
        public long? DecisionPosition { get; init; }
    }

    private sealed class MappingRevisionRow
    {
        public long Revision { get; init; }
        public Guid? BpId { get; init; }
        public string? BpKey { get; init; }
        public string SemanticHash { get; init; } = "";
        public Guid IdentityId { get; init; }
        public DateTimeOffset Recorded { get; init; }
    }

    private sealed class MappingAccount
    {
        public Guid Id { get; init; }
        public Guid Actor { get; init; }
        public string Code { get; init; } = "";
        public string Status { get; init; } = "";
        public string IdentityVersion { get; init; } = "";
        public string IdentityHash { get; init; } = "";
        public string BodyHash { get; init; } = "";
        public string AuthorityHash { get; init; } = "";
        public byte[] Original { get; init; } = [];
        public byte[] Trust { get; init; } = [];
        public DateTimeOffset SourceAsOf { get; init; }
        public DateTimeOffset Expires { get; init; }
    }

    private sealed class MappingArtifact
    {
        public Guid Id { get; init; }
        public Guid Actor { get; init; }
        public string Legal { get; init; } = "";
        public Guid Account { get; init; }
        public long ScopeRevision { get; init; }
        public string Permission { get; init; } = "";
        public string Hash { get; init; } = "";
        public byte[] Body { get; init; } = [];
        public DateTimeOffset Expires { get; init; }
    }

    private sealed class ReverseClaim
    {
        public Guid Account { get; init; }
        public long Revision { get; init; }
    }

    private sealed record MappingCoordinates(string LegalEntityKey, Guid AccountId);
    private sealed record AccountObservation(Guid ObservationId, Guid AccountId, string AccountCode, string AccountStatus,
        string SourceAsOf, string ExpiresAt, string IdentityHash);
    private sealed record MappingReference(Guid AccountId, string LegalEntityKey, string MappingRevision, string State,
        string? BusinessPartnerKey, string SemanticHash);
    private sealed record MappingHeadView(Guid AccountId, string LegalEntityKey, string State, string? MappingRevision,
        Reference? Bp, string? SemanticHash, string? SourceAsOf);
    private sealed record MappingContextRequest(string LegalEntityKey, Guid AccountId, Guid? ObservationId, IdentityCriteria? CandidateCriteria);
    private sealed record MappingContextReference(Guid ContextId, string ContextHash, string ExpiresAt);
    private sealed record MappingContextBody(MappingContextRequest Request, MappingHeadView Head, CandidateSet Candidates);
    private sealed record MappingSourceReference(string Owner, string Id, string Version, string Digest, string Purpose);

    private static Guid? OptionalId(JsonElement raw) => raw.ValueKind == JsonValueKind.Null ? null : Uuid(raw);
    private static long? OptionalVersion(JsonElement raw) => raw.ValueKind == JsonValueKind.Null ? null : Version(raw);
    private static string? OptionalRevision(long? value) => value is { } number ? Revision(number) : null;
    private static long NextMappingNumber(long value) => value < long.MaxValue ? value + 1 : throw Rejected("REVISION_EXHAUSTED", 409);
    private static string RawDigest(byte[] value) => Convert.ToHexString(Sha(value)).ToLowerInvariant();
    private static DateTimeOffset MappingUtc(JsonElement value)
    {
        var raw = Token(value, 40);
        if (!raw.EndsWith('Z') || !DateTimeOffset.TryParseExact(raw, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            throw Invalid();
        return parsed;
    }

    private static JsonElement ParseOriginal(byte[] original) => JsonSerializer.Deserialize<JsonElement>(original, Json);
    private DateTimeOffset MappingExpires() => PgTime(Now.AddMinutes(10));
    private static DateTimeOffset PgTime(DateTimeOffset value) => value.AddTicks(-(value.UtcTicks % 10));
    private static MappingCoordinates MappingScope(JsonElement raw)
    {
        var fields = Exact(raw, "legalEntityKey", "accountId");
        return new(LegalKey(Token(fields["legalEntityKey"], 64)), Uuid(fields["accountId"]));
    }

    private static MappingSourceReference SourceReference(JsonElement input)
    {
        var fields = Exact(input, "owner", "id", "version", "digest", "purpose");
        return new(Token(fields["owner"], 120), Token(fields["id"], 200), Token(fields["version"], 120), Digest(fields["digest"]),
            Token(fields["purpose"], 100));
    }

    private static MappingSourceReference[] SourceReferences(JsonElement input, int limit = 50)
    {
        var result = Array(input, limit).Select(SourceReference).ToArray();
        if (result.GroupBy(x => (x.Owner, x.Id, x.Version)).Any(x => x.Count() != 1)) throw Invalid();
        return result;
    }

    private async Task<Scope> MappingAccess(OperationsAccessV1 access, string legal, bool write, CancellationToken ct)
    {
        var scope = await ScopeAsync(legal, write, ct);
        if (!(await BasesAsync(access, legal, ct)).Any()) throw Missing();
        return scope;
    }

    private async Task<BusinessPartnerMappingAuthorityBindingV1?> MappingAuthority(string legal, string purpose, CancellationToken ct)
    {
        if (mappingAuthority is null) return null;
        return await ReadMappingAuthority(db, legal, purpose, ct);
    }

    internal static async Task<BusinessPartnerMappingAuthorityBindingV1?> ReadMappingAuthority(CP6Context context, string legal, string purpose, CancellationToken ct)
    {
        var tenant = context.CurrentTenantId;
        var row = await context.Database.SqlQuery<MappingAuthorityRow>($"""
            SELECT organization_id AS "OrganizationId",authority AS "Authority",epoch AS "Epoch",key_id AS "KeyId",
                   public_key AS "PublicKey",route_key AS "RouteKey"
              FROM main_bp.mapping_authorities
             WHERE tenant_id={tenant} AND legal_entity_key={legal} AND purpose={purpose} AND enabled FOR SHARE
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : new(tenant, legal, row.OrganizationId, purpose, row.Authority, row.Epoch, row.KeyId, row.PublicKey, row.RouteKey);
    }

    internal static string MappingAuthorityHash(BusinessPartnerMappingAuthorityBindingV1 binding)
        => Hash(new { binding.TenantId, binding.LegalEntityKey, binding.OrganizationId, binding.Purpose,
            binding.Authority, binding.Epoch, binding.KeyId, binding.PublicKey, binding.RouteKey });

    private async Task<MappingSlot?> ReadMappingSlot(string legal, Guid account, bool write, CancellationToken ct)
    {
        var rows = write
            ? await db.Database.SqlQuery<MappingSlot>($"""
                SELECT scope_identity AS "ScopeIdentity",revision AS "Revision",observation_sequence AS "Sequence",
                       generation AS "Generation",owner_conflict AS "OwnerConflict",current_projection_id AS "ProjectionId",
                       decision_position AS "DecisionPosition"
                  FROM main_bp.mapping_slots WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account}
                 FOR UPDATE
                """).ToListAsync(ct)
            : await db.Database.SqlQuery<MappingSlot>($"""
                SELECT scope_identity AS "ScopeIdentity",revision AS "Revision",observation_sequence AS "Sequence",
                       generation AS "Generation",owner_conflict AS "OwnerConflict",current_projection_id AS "ProjectionId",
                       decision_position AS "DecisionPosition"
                  FROM main_bp.mapping_slots WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account}
                """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    private Task<MappingRevisionRow?> MappingRevisionAt(string legal, Guid account, long? revision, CancellationToken ct)
        => revision is null ? Task.FromResult<MappingRevisionRow?>(null) : db.Database.SqlQuery<MappingRevisionRow>($"""
            SELECT revision AS "Revision",bp_id AS "BpId",business_partner_key AS "BpKey",semantic_hash AS "SemanticHash",
                   identity_observation_id AS "IdentityId",recorded_at AS "Recorded"
              FROM main_bp.mapping_revisions
             WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account} AND revision={revision}
            """).SingleOrDefaultAsync(ct);

    private async Task<ReverseClaim?> ReadReverseClaim(string legal, Guid bp, bool write, CancellationToken ct)
    {
        var query = write
            ? db.Database.SqlQuery<ReverseClaim>($"""
                SELECT account_id AS "Account",revision AS "Revision" FROM main_bp.mapping_reverse_claims
                WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND bp_id={bp} FOR UPDATE
                """)
            : db.Database.SqlQuery<ReverseClaim>($"""
                SELECT account_id AS "Account",revision AS "Revision" FROM main_bp.mapping_reverse_claims
                WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND bp_id={bp}
                """);
        return (await query.ToListAsync(ct)).SingleOrDefault();
    }

    private Task<MappingAccount?> ReadAccount(string legal, Guid account, Guid? proof, CancellationToken ct)
        => proof is null ? Task.FromResult<MappingAccount?>(null) : db.Database.SqlQuery<MappingAccount>($"""
            SELECT observation_id AS "Id",actor_id AS "Actor",account_code AS "Code",account_status AS "Status",
                   identity_version AS "IdentityVersion",identity_hash AS "IdentityHash",body_hash AS "BodyHash",authority_hash AS "AuthorityHash",
                   protected_original AS "Original",protected_trust AS "Trust",source_as_of AS "SourceAsOf",expires_at AS "Expires"
              FROM main_bp.mapping_account_observations
             WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account} AND observation_id={proof}
            """).SingleOrDefaultAsync(ct);

    private async Task<MappingAccount?> CurrentMappingAccount(string legal, Guid account, Guid? proof, Guid actor, CancellationToken ct)
    {
        if (proof is null) return null;
        var row = await ReadAccount(legal, account, proof, ct);
        var authority = await MappingAuthority(legal, AccountIdentityPurpose, ct);
        if (row is null || row.Actor != actor || row.Expires <= Now || authority is null ||
            row.AuthorityHash != MappingAuthorityHash(authority))
            throw Rejected("ACCOUNT_IDENTITY_UNPROVEN", 409);
        var bytes = Protect("mapping-account", row.Id).Unprotect(row.Original);
        try
        {
            if (RawDigest(bytes) != row.BodyHash || row.Trust.Length == 0) throw Rejected("ACCOUNT_IDENTITY_UNPROVEN", 409);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        return row;
    }

    private static AccountObservation Observe(Guid account, MappingAccount row)
        => new(row.Id, account, row.Code, row.Status, Instant(row.SourceAsOf), Instant(row.Expires), row.IdentityHash);

    private async Task<MappingHeadView> ViewMappingHead(OperationsAccessV1 access, string legal, Guid account,
        MappingRevisionRow? revision, CancellationToken ct)
    {
        Reference? bp = null;
        if (revision?.BpId is { } bpId)
        {
            var visible = await VisibleAsync(access, bpId, false, ct);
            if (visible.Head?.Legal != legal) throw Missing();
            bp = BpReference(visible.Row, visible.Head, visible.Head.Revision);
        }
        var observed = revision is null ? null : await FindMappingEvent(legal, account, null, revision.Revision, true, ct);
        if (revision is not null && observed is null) throw Rejected("MAPPING_ORIGINAL_UNPROVEN", 503);
        return new(account, legal, revision is null ? "UNSEEN" : revision.BpId is null ? "UNLINKED" : "LINKED",
            OptionalRevision(revision?.Revision), bp, revision?.SemanticHash, observed is null ? null : Instant(observed.SourceAsOf));
    }

    private static MappingReference MappingRef(string legal, Guid account, MappingRevisionRow revision)
        => new(account, legal, Revision(revision.Revision), revision.BpId is null ? "UNLINKED" : "LINKED", revision.BpKey, revision.SemanticHash);

    private Task<MappingArtifact?> FindMappingArtifact(string kind, Guid id, CancellationToken ct)
        => db.Database.SqlQuery<MappingArtifact>($"""
            SELECT context_id AS "Id",actor_id AS "Actor",legal_entity_key AS "Legal",account_id AS "Account",scope_revision AS "ScopeRevision",
                   permission_version AS "Permission",content_hash AS "Hash",protected_body AS "Body",expires_at AS "Expires"
              FROM main_bp.mapping_contexts WHERE tenant_id={Tenant} AND kind={kind} AND context_id={id}
            """).SingleOrDefaultAsync(ct);

    private async Task SaveMappingArtifact<T>(string kind, Guid id, OperationsAccessV1 access, Scope scope, Guid account,
        DateTimeOffset expiry, string hash, T data, CancellationToken ct)
    {
        var plain = Canonical(data);
        byte[] cipher;
        try { cipher = Protect(kind, id).Protect(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_contexts(tenant_id,context_id,kind,actor_id,legal_entity_key,account_id,
                         scope_revision,permission_version,content_hash,protected_body,created_at,expires_at)
            VALUES({Tenant},{id},{kind},{access.ActorId},{scope.Key},{account},{scope.Revision},
                   {access.ReadScopeVersion},{hash},{cipher},{Now},{expiry})
            """, ct);
    }

    private T OpenArtifact<T>(string kind, MappingArtifact saved)
    {
        var plain = Protect(kind, saved.Id).Unprotect(saved.Body);
        try { return JsonSerializer.Deserialize<T>(plain, Json) ?? throw Stale(); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static void EnsureArtifact(MappingArtifact? artifact, OperationsAccessV1 access, string legal, Guid account,
        DateTimeOffset now)
    {
        if (artifact is null || artifact.Actor != access.ActorId || artifact.Legal != legal || artifact.Account != account)
            throw Missing();
        if (artifact.Expires <= now || artifact.Permission != access.ReadScopeVersion) throw Stale();
    }
}
