namespace CP6.WebApi.Filters;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SpaceAuditOperationAttribute(
    string action,
    string resourceType) : Attribute
{
    public string Action { get; } = action;
    public string ResourceType { get; } = resourceType;
    public string? ResourceIdArgument { get; init; }
    public string? SiteIdArgument { get; init; }
    public string? PermissionCode { get; init; }
    public bool AuditRead { get; init; }
}
