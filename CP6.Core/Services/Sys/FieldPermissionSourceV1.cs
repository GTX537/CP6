namespace CP6.Core.Services.Sys;

/// <summary>
/// Internal captured facts, not an authorization credential or commit fence. Owner mappings and
/// independent use constraints remain the trusted consumer's responsibility. Never send this
/// object to a browser and accept it back as authority.
/// </summary>
internal sealed class FieldPermissionSourceV1(
    Guid tenant, Guid subject, string resource, string registryVersion, string sourceVersion,
    IEnumerable<string> fields, IEnumerable<FieldPermissionRoleV1> roles, IEnumerable<FieldPermissionRowV1> rows)
{
    public Guid TenantId { get; } = tenant;
    public Guid SubjectId { get; } = subject;
    public string Resource { get; } = resource;
    public string RegistryVersion { get; } = registryVersion;
    public string SourcePermissionVersion { get; } = sourceVersion;
    public IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(fields.ToArray());
    public IReadOnlyList<FieldPermissionRoleV1> Roles { get; } = Array.AsReadOnly(roles.ToArray());
    public IReadOnlyList<FieldPermissionRowV1> Rows { get; } = Array.AsReadOnly(rows.ToArray());

    public FieldPermissionVersionsV1 Versions(string mappingVersion)
        => new(FieldPermissionEvaluatorV1.PolicyVersion, RegistryVersion, mappingVersion, SourcePermissionVersion);

    public FieldPermissionDecisionV1 Evaluate(FieldPermissionVersionsV1 expected, string mappingVersion,
        IEnumerable<FieldPermissionMappingV1> mappings, IEnumerable<FieldPermissionConstraintsV1> constraints)
        => FieldPermissionEvaluatorV1.Evaluate(new(TenantId, SubjectId.ToString("D"), Resource,
            expected, Versions(mappingVersion), true, Fields, true, Roles, Rows, mappings, constraints));
}
