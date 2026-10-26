using System.Text.Json.Serialization;

namespace CP6.Core.Services.Erp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UomProfileRefV1(string Owner, Guid Id, string Version, string Digest);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UomProfileInputV1
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string DimensionKey { get; init; } = "";
    public int? QuantityScale { get; init; }
    public string? PackMultiple { get; init; }
    public string Mode { get; init; } = "";
    public UomProfileRefV1? BaseRef { get; init; }
    public string Numerator { get; init; } = "";
    public string Denominator { get; init; } = "";
    public bool? Enabled { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UomProfileCommandV1
{
    public Guid OperationId { get; init; }
    public string Action { get; init; } = "";
    public Guid ProfileId { get; init; }
    public string? ExpectedVersion { get; init; }
    public string? ExpectedAuthorityVersion { get; init; }
    public Guid? DepartmentId { get; init; }
    public UomProfileInputV1? Profile { get; init; }
}

public sealed record UomProfileScopeV1(Guid TenantId, Guid ActorId);
public sealed record UomProfileDepartmentV1(Guid Id, string Code, string Name);
public sealed record UomProfileCreationContextV1(Guid TenantId, Guid ActorId, string AuthorityVersion,
    bool AllowsUnbound, UomProfileDepartmentV1[] Departments);
public sealed record UomProfileVersionV1(Guid TenantId, UomProfileRefV1 Reference, bool IsCurrent,
    UomProfileInputV1 Profile, string OriginalJson);
public sealed record UomProfileCommandResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record UomProfileOperationV1(Guid OperationId, string State, UomProfileCommandResultV1? Result = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UomConversionRequestV1(UomProfileRefV1 From, UomProfileRefV1 To, string Quantity);
public sealed record UomConversionResultV1(UomProfileRefV1 From, UomProfileRefV1 To,
    string Quantity, string ConvertedQuantity, string Observation = "CURRENT_TRANSACTION_ONLY");

internal sealed record UomProfileStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId, UomProfileInputV1 Profile);
