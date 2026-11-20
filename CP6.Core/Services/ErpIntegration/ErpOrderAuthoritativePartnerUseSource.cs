using System.Data.Common;
using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

internal sealed class ErpOrderAuthoritativePartnerUseSource(BusinessPartnerMappingAuthorityV1 verifier, TimeProvider clock) : IErpOrderPartnerUseSource
{
    private sealed class CurrentMapping
    {
        public long Revision { get; init; }
        public string Code { get; init; } = "";
        public string SemanticHash { get; init; } = "";
        public string AccountAuthority { get; init; } = "";
        public string AccountVersion { get; init; } = "";
        public Guid Scope { get; init; }
        public long Position { get; init; }
    }

    public async Task<BpUseMappingProof?> ReadAsync(CP6Context db, DbTransaction transaction, ErpOrderRequested original,
        Guid bp, string legalEntityKey, CancellationToken ct)
    {
        BpUseScopeGate.RequireTransaction(db, transaction, serializable: true);
        if (original.TenantId != db.CurrentTenantId || !await db.Database.SqlQuery<bool>($"""
            SELECT main_bp.use_scope_held({original.TenantId},{legalEntityKey},false) AS "Value"
            """).SingleAsync(ct)) throw BpUseScopeGate.LockUnproven();

        var rows = await db.Database.SqlQuery<CurrentMapping>($"""
            SELECT r.revision AS "Revision",r.business_partner_key AS "Code",r.semantic_hash AS "SemanticHash",
                   g.authority_hash AS "AccountAuthority",a.identity_version AS "AccountVersion",
                   s.scope_identity AS "Scope", d.position AS "Position"
              FROM main_bp.mapping_slots s JOIN main_bp.mapping_revisions r USING(tenant_id,legal_entity_key,account_id,revision)
              JOIN main_bp.mapping_reverse_claims c USING(tenant_id,legal_entity_key,account_id,revision,bp_id)
              JOIN main_bp.mapping_use_guards g USING(tenant_id,legal_entity_key,account_id,revision,semantic_hash)
              JOIN main_bp.mapping_account_observations a ON a.tenant_id=r.tenant_id AND a.observation_id=r.identity_observation_id
              JOIN main_bp.mapping_owner_decisions d ON d.tenant_id=s.tenant_id AND d.legal_entity_key=s.legal_entity_key
                   AND d.account_id=s.account_id AND d.position=s.decision_position AND d.mapping_revision=r.revision
             WHERE s.tenant_id={original.TenantId} AND s.legal_entity_key={legalEntityKey} AND s.account_id={original.AccountId}
               AND r.bp_id={bp} AND r.business_partner_key={original.BusinessPartnerKey} AND NOT s.owner_conflict AND g.active
               AND g.account_business_version=a.identity_version AND g.authority_hash=a.authority_hash
             FOR SHARE OF s,c,g
            """).ToListAsync(ct);
        if (rows is not [var current]) return null;
        var account = await BusinessPartnerWorkspaceV1.ReadMappingAuthority(db, legalEntityKey, "ACCOUNT_IDENTITY", ct);
        var decision = await BusinessPartnerWorkspaceV1.ReadMappingAuthority(db, legalEntityKey, "ACC_MAPPING_AUTHORITY", ct);
        if (account is null || decision is null || account.OrganizationId != decision.OrganizationId ||
            current.AccountAuthority != BusinessPartnerWorkspaceV1.MappingAuthorityHash(account)) return null;
        var signed = await db.Database.SqlQuery<BusinessPartnerWorkspaceV1.OwnerDecisionRow>($"""
            SELECT position AS "Position",mapping_revision AS "MappingRevision",decision_id AS "DecisionId",scope_epoch AS "ScopeEpoch",
                authority_hash AS "AuthorityHash",digest AS "Digest",decision_body AS "Body",proof_body AS "Proof"
              FROM main_bp.mapping_owner_decisions
             WHERE tenant_id={original.TenantId} AND legal_entity_key={legalEntityKey} AND account_id={original.AccountId} AND position={current.Position}
            """).SingleOrDefaultAsync(ct);
        if (signed is null || !BusinessPartnerWorkspaceV1.VerifyCurrentOwnerSelection(signed, decision, verifier, original.AccountId,
            current.Scope, current.Revision, current.Code, current.AccountVersion, clock.GetUtcNow())) return null;

        var snapshot = new BpUseMapping(original.AccountId, legalEntityKey, current.Revision.ToString(CultureInfo.InvariantCulture),
            "LINKED", current.Code, current.SemanticHash);
        return new(transaction, original.TenantId, bp, current.Revision, snapshot, false);
    }
}
