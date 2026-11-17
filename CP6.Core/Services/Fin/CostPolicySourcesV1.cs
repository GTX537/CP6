using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

public sealed class CostPolicySourcesV1(CP6Context db, QualitySpecificationAuthorityService quality,
    ControlledDocumentOwnerV1 documents, IEnumerable<ICostPolicyExternalSourceV1> external, OperationsOwnerV1 owner)
{
    internal async Task RequireQualificationAsync(LedgerRow ledger, ManufacturingCostMasterStoreV1.VersionRow row,
        OperationsAccessV1 access, CancellationToken ct)
    {
        await Check(ct);
        access.AtCommit(Check);
        async Task Check(CancellationToken token)
        {
            await owner.RequireCurrentFinanceReviewerAsync("policy-review", row.Reviewer!.Value, token);
            if (await new ManufacturingFinanceScopeV1(db, ledger.EnvironmentId)
                .QualificationAsync(ledger.Id, row.Reviewer.Value, "policy-review", token) != row.ReviewQualificationId)
                throw Error("FINANCE_QUALIFICATION_REQUIRED", 403);
        }
    }

    internal async Task<CostPolicySourceOriginalV1[]> ReadAsync(FinanceScopeV1 scope, OperationsAccessV1 access,
        CostPolicyDefinitionV1 definition, DateOnly date, bool adopt, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null || !quality.Owns(db) || !documents.Owns(db))
            throw Error("COST_POLICY_UOW_REQUIRED", 503);
        var result = new List<CostPolicySourceOriginalV1>();
        var nativeOriginals = new Dictionary<CostPolicyRefV1, string>();
        foreach (var (role, reference) in References(definition))
        {
            string original;
            if (reference.Owner == QualitySpecificationValuesV1.Owner)
            {
                if (!role.StartsWith("QUALITY:", StringComparison.Ordinal) || reference.Purpose != "QUALITY_REQUIREMENTS")
                    throw Error("COST_POLICY_SOURCE_ROLE_MISMATCH", 422);
                if (!nativeOriginals.TryGetValue(reference, out original!))
                {
                    var proof = await quality.Source(access, Engineering(reference), null, adopt, ct, scope.EnvironmentId, scope.Mode);
                    original = proof.OriginalSourceJson!;
                    nativeOriginals.Add(reference, original);
                }
            }
            else if (reference.Owner == "Main.Plm.ControlledDocument.v1")
            {
                if (role is "GL_ACCOUNTING_POLICY" or "SOURCE_CONTRACT" || role.StartsWith("QUALITY:", StringComparison.Ordinal)
                    || reference.Purpose != "COST_POLICY_BASIS")
                    throw Error("COST_POLICY_SOURCE_ROLE_MISMATCH", 422);
                if (!nativeOriginals.TryGetValue(reference, out original!))
                {
                    var docAccess = await access.SourceAsync(OperationsOwnerV1.ControlledDocument, ct);
                    docAccess.RequireDocumentFields(false);
                    var source = await documents.ReviewSource(docAccess, Engineering(reference), adopt, ct, scope.EnvironmentId);
                    if (adopt && (source.State != "APPROVED" || source.Missing!.Length != 0))
                        throw Error("COST_POLICY_DOCUMENT_NOT_APPROVED", 422);
                    original = source.OriginalJson;
                    nativeOriginals.Add(reference, original);
                }
            }
            else
            {
                var candidates = external.Where(item => item.Owner == reference.Owner).ToArray();
                if (candidates.Length != 1)
                    throw Error("COST_POLICY_SOURCE_OWNER_UNAVAILABLE", 503);
                var source = await candidates[0].ReadInUowAsync(new(db, scope, access.ActorId, date, adopt,
                    callback => access.AtCommit(callback), definition.SourceContractRef) { Access = access }, reference, role, ct);
                if (source.Reference != reference || source.Scope != scope || source.Role != role
                    || string.IsNullOrWhiteSpace(source.OriginalJson))
                    throw Error("COST_POLICY_SOURCE_IDENTITY_MISMATCH", 422);
                original = source.OriginalJson;
            }
            result.Add(new(reference, scope, role, original));
        }
        return result.ToArray();
    }

    private static EngineeringSourceRefV1 Engineering(CostPolicyRefV1 reference)
        => Guid.TryParseExact(reference.Id, "D", out var id) ? new(reference.Owner, id, reference.Version, reference.Digest)
            : throw Error("COST_POLICY_SOURCE_IDENTITY_MISMATCH", 422);

    internal static IEnumerable<(string Role, CostPolicyRefV1 Reference)> References(CostPolicyDefinitionV1 d)
    {
        yield return ("COST_CENTER_RULE", d.CostCenterRuleRef);
        yield return ("CAPITALIZATION_RULE", d.CapitalizationRuleRef);
        yield return ("SCRAP_RULE", d.ScrapRuleRef);
        if (d.ProvisionalRuleRef != null) yield return ("PROVISIONAL_RULE", d.ProvisionalRuleRef);
        yield return ("GL_ACCOUNTING_POLICY", d.GlAccountingPolicyRef);
        yield return ("SOURCE_CONTRACT", d.SourceContractRef);
        yield return ("APPROVAL_BASIS", d.ApprovalBasisRef);
        foreach (var source in d.QualitySources) yield return ("QUALITY:" + source.Kind, source.Reference);
    }

    internal static void Validate(CostPolicyDefinitionV1 d)
    {
        if (!Text(d.Jurisdiction) || !Text(d.Standard) || d.UnitCostScale is < 0 or > 8
            || d.MaterialMethod is not ("UNIT_PRICE" or "LAYER_AMOUNTS")
            || d.WipMethod is not ("EXACT_TRACE" or "APPROVED_EQUIVALENT_UNITS")
            || d.AllocationMethod is not ("DIRECT_TRACE" or "APPROVED_WEIGHTS" or "RELATIVE_APPROVED_VALUE")
            || d.Residual != "LAST_POSITIVE_WEIGHT"
            || d.TrialTreatment is not ("TRIAL_EXPENSE" or "CAPITALIZE_IF_QUALIFIED" or "WIP_PENDING")
            || d.ZeroTreatment is not ("NO_EFFECT" or "REQUIRE_REVIEW")
            || d.CostCenterRequirement is not ("REQUIRED" or "NOT_APPLICABLE_PROVEN")
            || d.AllowedClasses is not { Length: > 0 and <= 4 }
            || d.AllowedClasses.Any(value => value is not ("ACTUAL" or "STANDARD" or "COMMERCIAL_ESTIMATE" or "PROVISIONAL"))
            || d.AllowedClasses.Distinct(StringComparer.Ordinal).Count() != d.AllowedClasses.Length
            || d.AllowedClasses.Contains("PROVISIONAL") && d.ProvisionalRuleRef == null
            || d.RequiredQualityKinds == null || d.QualitySources == null || d.QualitySources.Length > 249
            || d.RequiredQualityKinds.Any(value => !Text(value))
            || d.RequiredQualityKinds.Distinct(StringComparer.Ordinal).Count() != d.RequiredQualityKinds.Length
            || d.QualitySources.Any(value => value == null)
            || d.QualitySources.Select(value => value.Kind).Distinct(StringComparer.Ordinal).Count() != d.QualitySources.Length
            || !d.RequiredQualityKinds.ToHashSet(StringComparer.Ordinal).SetEquals(d.QualitySources.Select(value => value.Kind)))
            throw Error("COST_POLICY_DEFINITION_INVALID", 422);
        foreach (var (_, r) in References(d))
            if (r == null || !Text(r.Owner) || !Text(r.Id) || !Text(r.Version) || !Text(r.Purpose)
                || r.Digest == null || r.Digest.Length != 64 || r.Digest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                throw Error("COST_POLICY_REFERENCE_INVALID", 422);
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Contains('\0');
}
