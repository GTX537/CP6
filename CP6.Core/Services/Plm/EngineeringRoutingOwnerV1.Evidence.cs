using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRoutingOwnerV1
{
    internal static RoutingInputValidationV1 InputValidation(List<string> findings, List<RoutingCatalogueObservationV1> catalogues)
    {
        // 工程组合、独立技术审批和生产资格继续由原 Owner 裁决。
        var inputs = findings.Where(code => code.Split(':', 2)[0] is not ("MANUFACTURING_VIEW_OWNER_REQUIRED"
            or "CONTROLLED_DOCUMENT_OWNER_REQUIRED" or "RESOURCE_QUALIFICATION_REQUIRED"
            or "TOOL_APPROVAL_AND_PHYSICAL_QUALIFICATION_REQUIRED")).Distinct(StringComparer.Ordinal).ToArray();
        var blocked = inputs.Any(code => code.Split(':', 2)[0] is "DEPENDENCY_MISSING" or "DEPENDENCY_CYCLE_OR_UNRESOLVED"
            or "DEPENDENCY_ORDER_CONFLICT" or "ALTERNATIVE_RELATION_CONFLICT" or "ALTERNATIVE_OPTION_MISMATCH"
            or "PARAMETER_LIMIT_CONFLICT" or "PARAMETER_DIMENSION_MISMATCH" or "PARAMETER_EVIDENCE_MISMATCH"
            or "PARAMETER_UNIT_PRECISION_CONFLICT");
        return new(blocked ? "BLOCK" : inputs.Length == 0 ? "PASS" : "UNKNOWN", inputs, catalogues.ToArray());
    }

    private static RoutingCatalogueObservationV1 ObserveCatalogue(Guid occurrence, ResourceCapabilitySnapshotV1 value,
        DateTimeOffset at, List<string> findings)
    {
        var state = "UNKNOWN";
        if (value.Body.Source.Kind == "MACHINE")
        {
            var effective = value.Body.Capabilities.Any(item => ResourceCapabilityValuesV1.Time(item.ValidFrom) <= at
                && (item.ValidTo == null || at < ResourceCapabilityValuesV1.Time(item.ValidTo)));
            if (!effective) findings.Add("RESOURCE_CAPABILITY_NOT_EFFECTIVE:" + occurrence);
            if (value.Approval == null) findings.Add("RESOURCE_CAPABILITY_APPROVAL_REQUIRED:" + occurrence);
            if (effective && value.Approval != null && value.Missing.All(code => code == "RESOURCE_QUALIFICATION_REQUIRED"))
                state = "APPROVED";
        }
        else if (value.Missing.All(code => code == "RESOURCE_QUALIFICATION_REQUIRED"))
            state = "CATALOGUE_VERIFIED";
        return new(occurrence, value.Reference, value.Body.TimeUnit, value.Body.TimeBasis, value.Approval?.OperationId, state);
    }

    private static void CheckAlternative(Guid occurrence, RoutingAlternativeV1 alternative, ConfigurationRuleSnapshotV1 rule,
        EngineeringProductRefV1 product, List<string> findings)
    {
        if (!rule.IsActive || rule.State != "APPROVED" || rule.Body.ProductRef != product || rule.Missing!.Length != 0)
            findings.Add("ALTERNATIVE_RULE_CHANGED:" + occurrence);
        var definition = rule.Body.Definitions.SingleOrDefault(item => item.Name == alternative.When.Name);
        if (definition == null || definition.Type != alternative.When.Type
            || !definition.AllowedValues.Any(value => ConfigurationRuleValuesV1.Same(value, alternative.When)))
            findings.Add("ALTERNATIVE_OPTION_MISMATCH:" + occurrence);
    }

    private static void CheckParameterEvidence(Guid occurrence, RoutingParameterV1 parameter, QualitySpecificationSourceProofV1 proof,
        bool hasSite, List<string> findings)
    {
        var path = ":" + occurrence + ":" + parameter.Name;
        if (!hasSite || proof.Availability != "AVAILABLE" || proof.Approval != parameter.Evidence!.Approval)
        {
            findings.Add("PARAMETER_EVIDENCE_UNAVAILABLE" + path);
            return;
        }

        var requirement = proof.Requirements.SingleOrDefault(item => item.Id == parameter.Evidence.RequirementId);
        if (requirement == null || requirement.Applicability != "APPLIES" || requirement.Unit == null || parameter.Name != requirement.Characteristic
            || parameter.UnitRef != requirement.Unit || parameter.Method != requirement.Method
            || !EngineeringRoutingValuesV1.SameNumber(parameter.Value, requirement.Target)
            || !EngineeringRoutingValuesV1.SameNumber(parameter.Minimum, requirement.LowerLimit)
            || !EngineeringRoutingValuesV1.SameNumber(parameter.Maximum, requirement.UpperLimit))
            findings.Add("PARAMETER_EVIDENCE_MISMATCH" + path);
    }

    private async Task<(UomProfileVersionV1 Value, bool Current)> ParameterUnit(OperationsAccessV1 root, UomProfileRefV1 reference,
        bool current, CancellationToken ct)
    {
        var access = await root.SourceAsync(OperationsOwnerV1.UomProfile, ct);
        var store = new UomProfileStoreV1(db);
        await store.Fence(false, ct);
        async Task<UomProfileVersionV1> Read(UomProfileRefV1 exact)
        {
            await access.RequireUomProfile(exact.Id, ct);
            access.AtCommit(token => access.RequireUomProfile(exact.Id, token));
            var result = await store.Read(exact.Id, Revision(exact.Version), ct);
            if (result.Reference != exact) throw Error("ROUTING_UNIT_SOURCE_CHANGED", 412);
            return result;
        }

        var value = await Read(reference);
        if (current)
        {
            await UomProfileOwnerV1.CurrentExact(access, store, reference, ct);
            await UomProfileOwnerV1.CurrentBase(access, store, value, ct);
            return (value, true);
        }

        var live = value.IsCurrent && value.Profile.Enabled == true;
        if (value.Profile.BaseRef is { } baseRef)
        {
            var basis = await Read(baseRef);
            live = live && basis.IsCurrent && basis.Profile.Enabled == true;
        }
        return (value, live);
    }
}
