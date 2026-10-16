namespace CP6.Core.Services.Sys;

// Local calculation types, not a published wire schema or an authorization credential.
internal enum FieldPermissionUseV1 { Read, Write, Export, Download }
internal enum FieldPermissionCheckV1 { Unavailable, Allowed, Denied, NotApplicable }
internal enum FieldPermissionStatusV1 { Unavailable, Allowed, Denied }
internal enum FieldPermissionOriginV1 { REGISTERED_DEFAULT, EXPLICIT_AGGREGATE }

internal sealed record FieldPermissionVersionsV1(
    string PolicyVersion, string RegistryVersion, string MappingVersion, string SourcePermissionVersion);

internal sealed record FieldPermissionRoleV1(int RoleId, string Version, bool IsEffective);
internal sealed record FieldPermissionRowV1(
    Guid Id, int RoleId, string RoleVersion, string Resource, string Field, int Access);

/// <summary>Dependencies are registered source fields required for this same use.</summary>
internal sealed class FieldPermissionMappingV1
{
    public FieldPermissionMappingV1(string field, FieldPermissionUseV1 use, string apiPath,
        bool ownerApproved, IEnumerable<string> dependencies)
    {
        Field = field;
        Use = use;
        ApiPath = apiPath;
        OwnerApproved = ownerApproved;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
    }

    public string Field { get; }
    public FieldPermissionUseV1 Use { get; }
    public string ApiPath { get; }
    public bool OwnerApproved { get; }
    public IReadOnlyList<string> Dependencies { get; }
}

/// <summary>
/// Each use needs its own action/object result. Optional restrictions must be explicitly
/// Allowed, Denied or NotApplicable; an omitted/unknown check never implies permission.
/// The caller must evaluate complete grant clauses before providing these intersections.
/// </summary>
internal sealed record FieldPermissionConstraintsV1(
    string Field, FieldPermissionUseV1 Use,
    FieldPermissionCheckV1 Action, FieldPermissionCheckV1 Object,
    FieldPermissionCheckV1 Online, FieldPermissionCheckV1 Projection,
    FieldPermissionCheckV1 Pii, FieldPermissionCheckV1 Purpose, FieldPermissionCheckV1 Business);

/// <summary>
/// Immutable snapshot for one tenant/subject/resource decision. Versions are opaque, exact
/// source identities supplied by a trusted caller; this calculator cannot authenticate them.
/// RegistryComplete covers the entire resource domain; RoleDataComplete covers effective
/// membership and all actual stored rows for this resource, including explicit legacy 1s.
/// Capture these inputs consistently before construction; concurrent capture is the caller's job.
/// </summary>
internal sealed class FieldPermissionInputV1
{
    public FieldPermissionInputV1(Guid tenantId, string subjectRef, string resource,
        FieldPermissionVersionsV1 expectedVersions, FieldPermissionVersionsV1 versions,
        bool registryComplete, IEnumerable<string>? fields, bool roleDataComplete,
        IEnumerable<FieldPermissionRoleV1>? roles, IEnumerable<FieldPermissionRowV1>? rows,
        IEnumerable<FieldPermissionMappingV1>? mappings, IEnumerable<FieldPermissionConstraintsV1>? constraints)
    {
        TenantId = tenantId;
        SubjectRef = subjectRef;
        Resource = resource;
        ExpectedVersions = expectedVersions;
        Versions = versions;
        RegistryComplete = registryComplete && fields is not null;
        Fields = Array.AsReadOnly(fields?.ToArray() ?? []);
        RoleDataComplete = roleDataComplete && roles is not null && rows is not null;
        Roles = Array.AsReadOnly(roles?.ToArray() ?? []);
        Rows = Array.AsReadOnly(rows?.ToArray() ?? []);
        Mappings = Array.AsReadOnly(mappings?.ToArray() ?? []);
        Constraints = Array.AsReadOnly(constraints?.ToArray() ?? []);
    }

    public Guid TenantId { get; }
    public string SubjectRef { get; }
    public string Resource { get; }
    public FieldPermissionVersionsV1 ExpectedVersions { get; }
    public FieldPermissionVersionsV1 Versions { get; }
    public bool RegistryComplete { get; }
    public IReadOnlyList<string> Fields { get; }
    public bool RoleDataComplete { get; }
    public IReadOnlyList<FieldPermissionRoleV1> Roles { get; }
    public IReadOnlyList<FieldPermissionRowV1> Rows { get; }
    public IReadOnlyList<FieldPermissionMappingV1> Mappings { get; }
    public IReadOnlyList<FieldPermissionConstraintsV1> Constraints { get; }
}

internal sealed class FieldPermissionUseDecisionV1
{
    internal FieldPermissionUseDecisionV1(FieldPermissionStatusV1 status, IEnumerable<string> reasons)
    {
        Status = status;
        Reasons = Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    public FieldPermissionStatusV1 Status { get; }
    public bool Allowed => Status == FieldPermissionStatusV1.Allowed;
    public IReadOnlyList<string> Reasons { get; }
}

internal sealed class FieldPermissionFieldV1
{
    internal FieldPermissionFieldV1(string field, int access, FieldPermissionOriginV1 origin,
        IEnumerable<FieldPermissionRowV1> sources, IEnumerable<FieldPermissionMappingV1> mappings,
        IDictionary<FieldPermissionUseV1, FieldPermissionUseDecisionV1> uses)
    {
        Field = field;
        Access = access;
        Origin = origin;
        Sources = Array.AsReadOnly(sources.ToArray());
        Mappings = Array.AsReadOnly(mappings.ToArray());
        Uses = new System.Collections.ObjectModel.ReadOnlyDictionary<FieldPermissionUseV1, FieldPermissionUseDecisionV1>(
            new Dictionary<FieldPermissionUseV1, FieldPermissionUseDecisionV1>(uses));
    }

    public string Field { get; }
    /// <summary>Stored-row MIN/default result, before the independent use restrictions.</summary>
    public int Access { get; }
    public FieldPermissionOriginV1 Origin { get; }
    public IReadOnlyList<FieldPermissionRowV1> Sources { get; }
    public IReadOnlyList<FieldPermissionMappingV1> Mappings { get; }
    public IReadOnlyDictionary<FieldPermissionUseV1, FieldPermissionUseDecisionV1> Uses { get; }
    public bool CanRead => Uses[FieldPermissionUseV1.Read].Allowed;
    public bool CanWrite => Uses[FieldPermissionUseV1.Write].Allowed;
    public bool CanExport => Uses[FieldPermissionUseV1.Export].Allowed;
    public bool CanDownload => Uses[FieldPermissionUseV1.Download].Allowed;
}

/// <summary>
/// Deterministic calculation only. IsAvailable describes source validation, never a blanket
/// allow: callers must use the field AND use result. This does not authorize an Owner commit,
/// freeze current permissions, issue a download ticket, or implement masking/redaction.
/// </summary>
internal sealed class FieldPermissionDecisionV1
{
    internal FieldPermissionDecisionV1(FieldPermissionInputV1 input,
        IEnumerable<FieldPermissionFieldV1> fields, IEnumerable<string> errors)
    {
        Input = input;
        Fields = new System.Collections.ObjectModel.ReadOnlyDictionary<string, FieldPermissionFieldV1>(
            fields.ToDictionary(f => f.Field, StringComparer.Ordinal));
        Errors = Array.AsReadOnly(errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    public FieldPermissionInputV1 Input { get; }
    public bool IsAvailable => Errors.Count == 0;
    public IReadOnlyDictionary<string, FieldPermissionFieldV1> Fields { get; }
    public IReadOnlyList<string> Errors { get; }
}
