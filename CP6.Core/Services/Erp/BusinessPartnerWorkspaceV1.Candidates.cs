using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private sealed class NormalizedRow
    {
        public Guid Id { get; init; }
        public long Revision { get; init; }
        public string Name { get; init; } = "";
        public string? Standard { get; init; }
        public string? EinType { get; init; }
        public string? Ein { get; init; }
    }

    private static IdentityCriteria Criteria(BusinessPartner row)
        => new(Code(row), Name(row), row.StdCoCd?.TrimEnd(), row.EinType?.TrimEnd(), row.Ein?.TrimEnd());

    private Task<(CandidateSet Set, string? Issue)> Candidates(OperationsAccessV1 access, Scope scope, BusinessPartner proposed, CancellationToken ct)
        => Candidates(access, scope, Criteria(proposed), ct);

    private async Task<(CandidateSet Set, string? Issue)> Candidates(OperationsAccessV1 access, Scope scope, IdentityCriteria criteria, CancellationToken ct)
    {
        var key = scope.Key;
        var normVersion = BusinessPartnerUnicode15.Version;
        var scopeRevision = Revision(scope.Revision);
        CandidateSet Result(string visibility, CandidateItem[] items) => new(visibility, items,
            Hash(new { legalEntityKey = key, criteria, scopeRevision, items = items.Select(x => x.Reference), visibility }), scopeRevision);
        var restricted = Result("REVIEW_REQUIRED", []);
        if (!scope.Coverage || scope.NormVersion != normVersion || await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(
                SELECT FROM main_bp.heads h LEFT JOIN main_bp.normalized_identity n USING(tenant_id,legal_entity_key,bp_id)
                WHERE h.tenant_id={Tenant} AND h.legal_entity_key={key}
                  AND (n.norm_version IS DISTINCT FROM {normVersion} OR n.revision IS DISTINCT FROM h.revision)
            ) AS "Value"
            """).SingleAsync(ct))
            return (restricted, "REFERENCE_UNPROVEN");

        var name = BusinessPartnerUnicode15.Normalize(criteria.BpName);
        var standard = NormalizedOptional(criteria.StdCoCd);
        var ein = NormalizedOptional(criteria.Ein);
        var rows = await db.Database.SqlQuery<NormalizedRow>($"""
            SELECT n.bp_id AS "Id", n.revision AS "Revision", n.name_norm AS "Name", n.standard_norm AS "Standard",
                   n.ein_type AS "EinType", n.ein_norm AS "Ein"
              FROM main_bp.normalized_identity n
              JOIN public."T_WebBusinessPartner" p ON p."Id"=n.bp_id AND p."TenantId"=n.tenant_id
              WHERE n.tenant_id={Tenant} AND n.legal_entity_key={key} AND n.norm_version={normVersion}
               AND (rtrim(p."BpCd"::text)={criteria.BpCd} OR n.name_hash={Sha(Bytes(name))}
                    OR n.standard_hash={HashOptional(standard)}
                    OR (n.ein_type={criteria.EinType} AND n.ein_hash={HashOptional(ein)}))
            """).ToListAsync(ct);

        // 旧档缺法人时，同名/同号只能要求复核。给普通操作者的结果不含旧档标识或数量。
        var adopted = db.Database.SqlQuery<Guid>($"""SELECT bp_id AS "Value" FROM main_bp.heads WHERE tenant_id={Tenant}""");
        var unidentified = await db.BusinessPartners.AsNoTracking().Where(row => !adopted.Contains(row.Id))
            .Select(row => new { row.BpCd, row.BpName, row.StdCoCd, row.EinType, row.Ein }).ToArrayAsync(ct);
        if (unidentified.Any(row => row.BpCd.TrimEnd() == criteria.BpCd || BusinessPartnerUnicode15.Normalize(row.BpName.TrimEnd()) == name ||
            standard is not null && NormalizedOptional(row.StdCoCd) == standard ||
            ein is not null && row.EinType?.TrimEnd() == criteria.EinType && NormalizedOptional(row.Ein) == ein))
            return (restricted, "DUPLICATE_REVIEW_REQUIRED");

        var canReview = await access.TryPartnerActionAsync("duplicate-review", ct);
        var bases = await BasesAsync(access, key, ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var visibleIds = await access.BusinessPartners(includeDeleted: true).Where(x => ids.Contains(x.Id))
            .Select(x => x.Id).ToArrayAsync(ct);
        var byId = await db.BusinessPartners.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var candidates = new List<CandidateItem>();
        foreach (var match in rows.OrderBy(x => x.Id.ToString("D"), StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(match.Id, out var row)) return (restricted, "REFERENCE_UNPROVEN");
            var reasons = new List<string>();
            if (Code(row) == criteria.BpCd) reasons.Add("CODE_EXACT");
            if (match.Name == name) reasons.Add("NAME_EXACT");
            if (standard is not null && match.Standard == standard) reasons.Add("STANDARD_CODE_EXACT");
            if (ein is not null && criteria.EinType == match.EinType && match.Ein == ein) reasons.Add("TYPED_REGISTRATION_EXACT");
            if (reasons.Count == 0) continue;
            if (candidates.Count == 50 || canReview is null || !visibleIds.Contains(row.Id) ||
                !access.CanReadPartnerField("BpName") || !access.CanReadPartnerField("BpCd") || !access.CanReadPartnerField("Roles") ||
                !bases.Any(x => x.BaseCd == Base(row)))
                return (restricted, "DUPLICATE_REVIEW_REQUIRED");
            var head = await HeadAsync(row.Id, ct);
            if (head is null || head.Revision != match.Revision || !head.RowVersion.SequenceEqual(row.RowVersion ?? []))
                return (restricted, "REFERENCE_UNPROVEN");
            var revision = Revision(head.Revision);
            var evidence = new
            {
                bpId = row.Id, revision, bpCd = Code(row), name = Name(row), roles = RoleSet(row), registration = head.Registration,
                lifecycle = Lifecycle(row), hold = Hold(row), availability = "SELECTABLE", matchReasons = reasons.ToArray()
            };
            candidates.Add(new(new(row.Id, revision, Hash(evidence)), evidence.bpCd, evidence.name, evidence.roles,
                evidence.registration, evidence.lifecycle, evidence.hold, evidence.availability, evidence.matchReasons));
        }
        return (Result("COMPLETE", candidates.ToArray()), null);
    }

    private static Issue[] CandidateDecision(CreateIntent intent, CandidateSet set, string? issue)
    {
        if (issue is not null) return [new("duplicateDecision", issue)];
        if (intent.Decision.Kind == "USE_EXISTING")
            return set.Items.Any(item => item.Reference == intent.Decision.Existing) ? [] : [new("duplicateDecision", "DUPLICATE_REVIEW_REQUIRED")];
        if (set.Items.Any(item => item.MatchReasons.Contains("CODE_EXACT", StringComparer.Ordinal)))
            return [new("bpCd", "BP_CODE_EXISTS")];
        if (!set.Items.Select(item => item.Reference).SequenceEqual(intent.Decision.Acknowledged) ||
            set.Items.Length > 0 && intent.Decision.Reason is null)
            return [new("duplicateDecision", "DUPLICATE_REVIEW_REQUIRED")];
        return [];
    }

    private Task<bool> CodeExists(BusinessPartner row, CancellationToken ct)
        => db.BusinessPartners.AsNoTracking().AnyAsync(partner => partner.BpCd == row.BpCd, ct);
}
