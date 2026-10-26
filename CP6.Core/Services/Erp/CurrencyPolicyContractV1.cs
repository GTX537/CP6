using System.Text.Json.Serialization;

namespace CP6.Core.Services.Erp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CurrencyPolicyRefV1(string Owner, Guid Id, string Version, string Digest);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CurrencyPolicyInputV1
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public int? MinorDigits { get; init; }
    public int? PriceDigits { get; init; }
    public string Rounding { get; init; } = "";
    public bool? Enabled { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CurrencyPolicyCommandV1
{
    public Guid OperationId { get; init; }
    public string Action { get; init; } = "";
    public Guid PolicyId { get; init; }
    public string? ExpectedVersion { get; init; }
    public string? ExpectedAuthorityVersion { get; init; }
    public Guid? DepartmentId { get; init; }
    public CurrencyPolicyInputV1? Policy { get; init; }
}

public sealed record CurrencyPolicyScopeV1(Guid TenantId, Guid ActorId);
public sealed record CurrencyPolicyDepartmentV1(Guid Id, string Code, string Name);
public sealed record CurrencyPolicyCreationContextV1(Guid TenantId, Guid ActorId, string AuthorityVersion,
    bool AllowsUnbound, CurrencyPolicyDepartmentV1[] Departments);
public sealed record CurrencyPolicyVersionV1(Guid TenantId, CurrencyPolicyRefV1 Reference, bool IsCurrent,
    CurrencyPolicyInputV1 Policy, string OriginalJson);
public sealed record CurrencyPolicyCommandResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record CurrencyPolicyOperationV1(Guid OperationId, string State, CurrencyPolicyCommandResultV1? Result = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CurrencyQuantizationRequestV1(CurrencyPolicyRefV1 PolicyRef, string Kind, string Value);
public sealed record CurrencyQuantizationResultV1(CurrencyPolicyRefV1 PolicyRef, string Kind,
    string Value, string RoundedValue, string Observation = "CURRENT_TRANSACTION_ONLY");

internal sealed record CurrencyPolicyStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId, CurrencyPolicyInputV1 Policy);
