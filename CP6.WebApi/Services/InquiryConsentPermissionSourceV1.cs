using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.Services;

/// <summary>Exact human consent mutation source. No PII, service or intake decision alias.
/// These facts still require the CRM Owner's exact resource and current transaction checks.</summary>
internal static class InquiryConsentPermissionSourceV1
{
    internal const string Resource = "crm-privacy";
    internal const string MappingVersion = "MainInquiryConsentFieldsV1";
    internal static IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(new[] { "consent" });

    internal static async Task<InquiryPermissionFactsV1> ReadAsync(CP6Context db, Guid subject,
        string[] required, UserPermissionContext permissions, Func<AuthorizationCatalogSnapshotV1> capture, CancellationToken ct)
    {
        var catalog = capture();
        if (!catalog.IsSealed || !catalog.Fields.TryGetValue(Resource, out var definitions)
            || !definitions.Select(x => x.Name).SequenceEqual(Fields))
            throw new CurrentPermissionUnavailableException();
        var managed = await InquiryIntakePermissionSourceV1.ManagedDepartmentsAsync(db, permissions, "crm-lead", ct);
        var privacy = await InquiryIntakePermissionSourceV1.ManagedDepartmentsAsync(db, permissions, Resource, ct);
        var departments = managed.Intersect(privacy).Order().ToArray();
        if (!permissions.ActionKeys.Contains("crm-lead:assign") || departments.Length == 0)
            throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        if (departments.Length > 256)
            throw new InquiryPermissionExceptionV1(422, "inquiry_projection_scope_not_representable");

        var source = await new CurrentFieldPermissionSourceReaderV1(db, new HttpContextAccessor(), () => catalog)
            .ReadTrustedSubjectAsync(Resource, subject, ct);
        if (source.TenantId != db.CurrentTenantId || source.SubjectId != subject
            || !source.Roles.Select(x => x.RoleId).Order().SequenceEqual(permissions.RoleIds.Distinct().Order()))
            throw new CurrentPermissionUnavailableException();
        // Accepted stored-MIN then registered default; read-only cannot authorize mutation.
        // Empty use constraints grant no business use. Only this exact purpose maps Access=1.
        var facts = source.Evaluate(source.Versions(MappingVersion), MappingVersion, [], []);
        if (!facts.IsAvailable) throw new CurrentPermissionUnavailableException();
        if (facts.Fields["consent"].Access != 1)
            throw new InquiryPermissionExceptionV1(403, "insufficient_field_scope");
        return new(required, ["consent"], departments, true);
    }
}
