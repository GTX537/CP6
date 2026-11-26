using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringProductMappingService
{
    private static void ValidateScope(EngineeringMappingCandidateV1 candidate)
    {
        if (candidate.LegalEntity is { } legal)
        {
            if (!ValidText(legal.Key, 64) || legal.Key != legal.Key.Trim() || !ValidText(legal.Evidence, 8000, true))
                throw Error("ENGINEERING_LEGAL_ENTITY_INPUT_INVALID", 400);
            Revision(legal.Version);
        }
        if (candidate.TechnicalScope is { } scope)
        {
            ValidateTechnicalReference(scope.RevisionRef, EngineeringRevisionValuesV1.RevisionOwner);
            ValidateTechnicalReference(scope.ConfigurationRef, EngineeringRevisionValuesV1.ConfigurationOwner);
        }
        if (candidate.LegacyMappingRef is { } original) MappingRevision(original);
    }

    private static void ValidateTechnicalReference(EngineeringRevisionRefV1? reference, string issuer)
    {
        if (reference == null) throw Error("ENGINEERING_SOURCE_REFERENCE_INVALID", 400);
        SourceReference(new(reference.Owner, reference.Id, reference.Version, reference.Digest), issuer);
    }

    private static async Task RequireRevisionRead(CP6Context db, OperationsAccessV1 root, Guid revisionId, CancellationToken ct)
    {
        var access = await root.SourceAsync(OperationsOwnerV1.EngineeringRevision, ct);
        access.RequireEngineeringRevisionSourceAccess(db);
        await access.RequireEngineeringRevision(revisionId, ct);
        root.AtCommit(token => access.RequireEngineeringRevision(revisionId, token));
    }

    private static async Task HoldScopeSources(CP6Context db, OperationsAccessV1 access, EngineeringMappingCandidateV1 candidate, CancellationToken ct)
    {
        if (candidate.LegalEntity is { } legal)
            await CommercialEngineeringMappingParticipantV1.HoldLegalEntity(db, legal, ct);
        if (candidate.TechnicalScope is { } scope)
        {
            await RequireRevisionRead(db, access, scope.RevisionRef.Id, ct);
            var (reference, draft) = await new EngineeringRevisionStoreV1(db).HoldCurrentDraft(scope.RevisionRef.Id, ct);
            if (scope.RevisionRef != reference || draft.ProductRef != candidate.EngineeringRef
                || draft.Configuration is not { } configuration || configuration.Reference != scope.ConfigurationRef)
                throw Error("ENGINEERING_MAPPING_TECHNICAL_SCOPE_CHANGED", 412);
            if (configuration.Input.Applicability != "APPLIES")
                throw Error("ENGINEERING_MAPPING_TECHNICAL_SCOPE_UNPROVEN");
        }
    }

    private static async Task VerifyLegacyReference(EngineeringProductStoreV1 store, OperationsAccessV1 access,
        EngineeringMappingCandidateV1 candidate, long? expected, CancellationToken ct)
    {
        if (candidate.LegacyMappingRef is not { } reference) return;
        var version = MappingRevision(reference);
        if (expected == null || version > expected) throw Error("ENGINEERING_LEGACY_MAPPING_MISMATCH");
        var prior = await store.ReadMapping(reference.Id, version, ct);
        await RequireRead(store.Context, access, prior, ct);
        if (prior.Reference != reference || prior.State == "DISABLED" || prior.Candidate.EngineeringRef.Id != candidate.EngineeringRef.Id
            || prior.Candidate.BusinessRef == null || prior.Candidate.BusinessRef.Id != candidate.BusinessRef?.Id
            || prior.Candidate.SiteRef == null || prior.Candidate.SiteRef.Id != candidate.SiteRef?.Id)
            throw Error("ENGINEERING_LEGACY_MAPPING_MISMATCH");
    }

    private static async Task RequireConsistentLegalEntity(EngineeringProductStoreV1 store, EngineeringSourceRefV1 product,
        string key, CancellationToken ct)
    {
        foreach (var row in await store.ProductMappings(product, false, ct))
        {
            var value = await store.ReadMapping(row.Id, row.Revision, ct);
            if (value.ConfirmedLegalScope is { } confirmed && confirmed.LegalEntity.Key != key)
                throw Error("ENGINEERING_PRODUCT_LEGAL_SCOPE_CONFLICT", 409);
        }
    }

    internal static void RequireUsageScope(EngineeringMappingVersionV1 mapping, EngineeringSourceRefV1? site, EngineeringRevisionRefV1? revision)
    {
        if (revision != null && mapping.Candidate.RequiresScopeReview
            && (site != mapping.Candidate.SiteRef || mapping.ConfirmedTechnicalScope is { } technical && technical.RevisionRef != revision))
            throw Error("ENGINEERING_MAPPING_USAGE_SCOPE_MISMATCH");
    }

    internal static async Task<(EngineeringIdentitySiteScopeV1[] Mappings, EngineeringConfirmedLegalScopeV1[] LegalScopes,
        EngineeringLegacyMappingConfirmationV1[] History)> ReadProductFactsAsync(CP6Context db, OperationsAccessV1 productAccess,
        EngineeringSourceRefV1 product, CancellationToken ct)
    {
        productAccess.RequireProductSourceAccess(db);
        // 原产品读取在部署前向索引之前仍能披露固定原件。
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT to_regprocedure('cp6_engineering_product_v1.mapping_business_reference(bytea)') IS NOT NULL AS "Value"
            """).SingleAsync(ct)) return ([], [], []);
        var store = new EngineeringProductStoreV1(db);
        await store.Fence(false, ct);
        var rows = await store.ProductMappings(product, true, ct);
        if (rows.Length == 0) return ([], [], []);

        OperationsAccessV1 engineering;
        try { engineering = await productAccess.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct); }
        catch (OrderPriceCorrectionException error) when (error.Status == 403) { return ([], [], []); }

        var maps = new List<EngineeringIdentitySiteScopeV1>();
        var scopes = new List<EngineeringConfirmedLegalScopeV1>();
        var history = new List<EngineeringLegacyMappingConfirmationV1>();
        foreach (var row in rows)
        {
            var value = await store.ReadMapping(row.Id, row.Revision, ct);
            if (value.ConfirmedIdentitySite is not { } identity) continue;
            try
            {
                if (value.IsCurrent)
                {
                    var observed = await Observe(db, engineering, store, value, ct);
                    if (observed.CanAdoptIdentitySite)
                    {
                        maps.Add(identity);
                        if (value.ConfirmedLegalScope is { } legal) scopes.Add(legal);
                    }
                }
                else await RequireRead(db, engineering, value, ct);
                if (value.LegacyConfirmation is { } original) history.Add(original);
            }
            catch (OrderPriceCorrectionException error) when (error.Status == 403)
            {
                // 产品读取资格不授予工程映射及其关联原件的披露权。
            }
        }
        return (maps.ToArray(), scopes.ToArray(), history.ToArray());
    }
}
