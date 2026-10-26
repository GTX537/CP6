using CP6.Core.Services.Erp;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringProductRefV1(string Owner, Guid Id, string Version, string Digest);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringProductInputV1
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Definition { get; init; } = "";
    public string IdentificationSource { get; init; } = "";
    public bool? Enabled { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringProductCommandV1
{
    public Guid OperationId { get; init; }
    public string Action { get; init; } = "";
    public Guid ProductId { get; init; }
    public string? ExpectedVersion { get; init; }
    public string? ExpectedAuthorityVersion { get; init; }
    public Guid? DepartmentId { get; init; }
    public EngineeringProductInputV1? Product { get; init; }
}

public sealed record EngineeringProductScopeV1(Guid TenantId, Guid ActorId);
public sealed record EngineeringProductDepartmentV1(Guid Id, string Code, string Name);
public sealed record EngineeringProductCreationContextV1(Guid TenantId, Guid ActorId, string AuthorityVersion,
    bool AllowsUnbound, EngineeringProductDepartmentV1[] Departments);
public sealed record EngineeringProductVersionV1(Guid TenantId, EngineeringProductRefV1 Reference, bool IsCurrent,
    EngineeringProductInputV1 Product, string OriginalJson);
public sealed record EngineeringProductCommandResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record EngineeringProductOperationV1(Guid OperationId, string State, EngineeringProductCommandResultV1? Result = null);

internal sealed record EngineeringProductStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId, EngineeringProductInputV1 Product);
