using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;

namespace CP6.Core.Services.Fin;

public sealed record FinancePolicyIntervalV1([property: JsonRequired] DateOnly From,
    [property: JsonRequired] DateOnly? ToExclusive);
public sealed record FinancePolicyLegV1(string LegKey, string[] DependsOn, CostPolicyRefV1 RuleRef,
    CostPolicyRefV1[] AccountMappingRefs, string[] RequiredEvidenceKeys);
public sealed record FinancePolicyGuardV1(string Kind, string RequirementKey, CostPolicyRefV1 PolicyRef,
    string Applicability, CostPolicyRefV1 BasisRef);

// FIN-GL-01 §11 的专业输入；Scope/Ref/资格/状态由 Main Finance 生成，不接收浏览器自签批准。
public sealed record FinanceAccountingPolicyInputV1(string[] EventTypes, FinancePolicyIntervalV1 Interval,
    string JurisdictionKey, string StandardKey, [property: JsonRequired] int CurrencyScale, string BaseCurrency,
    [property: JsonRequired] int BaseScale, [property: JsonRequired] int FxScale, string Rounding,
    string ConversionBasis, string ZeroTreatment, string ApprovalMode, ApprovalPolicyV2 ApprovalPolicy,
    FinancePolicyLegV1[] LegRules, FinancePolicyGuardV1[] GuardRequirements, CostPolicyRefV1 SourceContractRef,
    CostPolicyRefV1 MatrixRef);

// 双方专业批准的来源登记；保留独立版本，不把 SourceContract 当成已经接收的 GL SourceEnvelope。
public sealed record FinanceSourceContractInputV1(string SourceOwner, string SourceSchema, string[] EventTypes,
    FinancePolicyIntervalV1 Interval, string[] RequiredEvidenceKeys);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinancePolicySubmitV1(Guid CommandId, Guid? PreviousVersionId, string Kind,
    JsonElement Definition, CostPolicyRefV1 ApprovalBasisRef, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinancePolicyDecisionV1(Guid CommandId, string ExpectedRevision, string Role, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinancePolicyControlV1(Guid CommandId, string ExpectedRevision, string Reason);

internal static class FinancePolicyValuesV1
{
    internal const string Owner = "FinancePolicy";
    internal const string Route = "api/fin/policy/owner-v1";
    internal const string View = "accounting-source-view";
    internal static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
        { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static string Action(string kind, string operation) => (kind == "ACCOUNTING_POLICY" ? "accounting-policy-" : "source-contract-") + operation;
    internal static string Encode(object value) => JsonSerializer.Serialize(value, Wire);
    internal static void Input([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid)
    { if (!valid) throw ManufacturingFinanceScopeV1.Error("FINANCE_POLICY_INPUT_INVALID", 422); }
    internal static bool Key(string? value) => !string.IsNullOrEmpty(value) && value.EnumerateRunes().Count() <= 128 && !value.Any(char.IsControl);
    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static void Ref(CostPolicyRefV1? value)
        => Input(value != null && Key(value.Owner) && Key(value.Id) && Key(value.Version) && Key(value.Purpose) && Hash(value.Digest));
    internal static void Keys(string[]? values, bool empty = false)
        => Input(values != null && values.Length <= 256 && (empty || values.Length > 0)
            && values.All(Key) && values.Distinct(StringComparer.Ordinal).Count() == values.Length);
    internal static void Interval(FinancePolicyIntervalV1? value)
        => Input(value != null && (value.ToExclusive == null || value.ToExclusive > value.From));

    internal static T Parse<T>(JsonElement json)
    {
        Input(json.ValueKind == JsonValueKind.Object);
        try { Unique(json); return json.Deserialize<T>(Wire) ?? throw new JsonException(); }
        catch (JsonException) { throw ManufacturingFinanceScopeV1.Error("FINANCE_POLICY_INPUT_INVALID", 422); }
    }
    private static void Unique(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in json.EnumerateObject())
            {
                if (!names.Add(field.Name)) throw ManufacturingFinanceScopeV1.Error("FINANCE_POLICY_DUPLICATE_PROPERTY", 400);
                Unique(field.Value);
            }
        }
        else if (json.ValueKind == JsonValueKind.Array) foreach (var item in json.EnumerateArray()) Unique(item);
    }
    internal static FinancePolicyIntervalV1 Validate(FinancePolicySubmitV1 input)
    {
        Input(input.Kind is "ACCOUNTING_POLICY" or "SOURCE_CONTRACT");
        Ref(input.ApprovalBasisRef);
        Input(input.ApprovalBasisRef.Owner == "Main.Plm.ControlledDocument.v1" && input.ApprovalBasisRef.Purpose == "FINANCE_POLICY_BASIS"
            && Guid.TryParseExact(input.ApprovalBasisRef.Id, "D", out _));
        if (input.Kind == "SOURCE_CONTRACT")
        {
            var body = Parse<FinanceSourceContractInputV1>(input.Definition);
            Input(Key(body.SourceOwner) && Key(body.SourceSchema)); Keys(body.EventTypes); Keys(body.RequiredEvidenceKeys, true); Interval(body.Interval);
            return body.Interval;
        }
        var p = Parse<FinanceAccountingPolicyInputV1>(input.Definition);
        Keys(p.EventTypes); Interval(p.Interval); Ref(p.SourceContractRef); Ref(p.MatrixRef);
        Input(Key(p.JurisdictionKey) && Key(p.StandardKey) && p.CurrencyScale is >= 0 and <= 8 && p.BaseScale is >= 0 and <= 8
            && p.FxScale is >= 0 and <= 12 && p.BaseCurrency is { Length: 3 } && p.BaseCurrency.All(c => c is >= 'A' and <= 'Z')
            && p.Rounding is "HALF_UP" or "HALF_EVEN" && p.ConversionBasis == "LINE" && p.ZeroTreatment is "NO_EFFECT" or "REQUIRE_REVIEW"
            && p.ApprovalMode is "OA_DECISION" or "REGISTERED_AUTOMATION");
        var a = p.ApprovalPolicy;
        Input(a?.Ref != null && Key(a.Ref.Owner) && Key(a.Ref.Id) && Key(a.Ref.Version) && Key(a.Version)
            && a.Digest != null && a.Digest.Algorithm == "sha256" && Key(a.Digest.CanonicalizationVersion) && Hash(a.Digest.Value));
        Input(p.LegRules is { Length: <= 32 } && p.LegRules.All(l => l != null));
        Keys(p.LegRules.Select(l => l.LegKey).ToArray(), true);
        foreach (var leg in p.LegRules)
        {
            Ref(leg.RuleRef); Keys(leg.DependsOn, true); Keys(leg.RequiredEvidenceKeys, true);
            Input(leg.AccountMappingRefs is { Length: <= 256 }); foreach (var reference in leg.AccountMappingRefs) Ref(reference);
            Input(leg.DependsOn.All(key => key != leg.LegKey && p.LegRules.Any(l => l.LegKey == key)));
        }
        var done = new HashSet<string>(StringComparer.Ordinal);
        while (done.Count < p.LegRules.Length)
        {
            var ready = p.LegRules.Where(l => !done.Contains(l.LegKey) && l.DependsOn.All(done.Contains)).ToArray();
            Input(ready.Length > 0); foreach (var leg in ready) done.Add(leg.LegKey);
        }
        Input(p.GuardRequirements is { Length: <= 256 } && p.GuardRequirements.All(g => g != null));
        Keys(p.GuardRequirements.Select(g => g.RequirementKey).ToArray(), true);
        foreach (var guard in p.GuardRequirements)
        {
            Input(guard.Kind is "PERIOD" or "BANK_RECON" or "BUDGET" or "SOURCE" or "CORRECTION"
                && guard.Applicability is "REQUIRED" or "NOT_APPLICABLE"); Ref(guard.PolicyRef); Ref(guard.BasisRef);
        }
        return p.Interval;
    }
}
