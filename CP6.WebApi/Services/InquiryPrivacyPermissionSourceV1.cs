using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.Services;

/// <summary>Current human export/download source facts, not an artifact or business decision.
/// The caller holds Main's authentication root and has checked exact Site/Form and actions.
/// CRM must still check the actual submission/job/proof, PII state and final current fence.</summary>
internal static class InquiryPrivacyPermissionSourceV1
{
    internal static async Task<InquiryPermissionFactsV1> ReadAsync(CP6Context db, Guid subject,
        string purpose, string[] required, UserPermissionContext permissions,
        Func<AuthorizationCatalogSnapshotV1> capture, CancellationToken ct)
    {
        var use = purpose switch
        {
            "privacy:export" => FieldPermissionUseV1.Export,
            "privacy:download" => FieldPermissionUseV1.Download,
            _ => throw new InquiryPermissionExceptionV1(400, "unsupported_projection_purpose")
        };
        var mapping = use == FieldPermissionUseV1.Export ? "MainInquiryPrivacyExportFieldsV1" : "MainInquiryPrivacyDownloadFieldsV1";
        var catalog = capture();
        if (!catalog.IsSealed || !catalog.Fields.TryGetValue(InquiryIntakePermissionSourceV1.Resource, out var intake)
            || !intake.Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(InquiryIntakePermissionSourceV1.Fields.Order(StringComparer.Ordinal))
            || !catalog.Fields.TryGetValue(InquiryPrivacyFieldCatalogV1.Resource, out var privacy)
            || !InquiryPrivacyFieldCatalogV1.Matches(privacy.Select(x => x.Name)))
            throw new CurrentPermissionUnavailableException();
        var leadDepartments = await InquiryIntakePermissionSourceV1.ManagedDepartmentsAsync(db, permissions, "crm-lead", ct);
        var privacyDepartments = await InquiryIntakePermissionSourceV1.ManagedDepartmentsAsync(db, permissions, InquiryPrivacyFieldCatalogV1.Resource, ct);
        var departments = leadDepartments.Intersect(privacyDepartments).Order().ToArray();
        if (!permissions.ActionKeys.Contains("crm-lead:assign") || departments.Length == 0
            || required.Any(x => !permissions.ActionKeys.Contains(x)))
            throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        if (departments.Length > 256)
            throw new InquiryPermissionExceptionV1(422, "inquiry_projection_scope_not_representable");

        async Task RequireFields(string resource, IEnumerable<string> selected)
        {
            var fields = selected.ToArray();
            var source = await new CurrentFieldPermissionSourceReaderV1(db, new HttpContextAccessor(), () => catalog)
                .ReadTrustedSubjectAsync(resource, subject, ct);
            if (source.TenantId != db.CurrentTenantId || source.SubjectId != subject
                || !source.Roles.Select(x => x.RoleId).Order().SequenceEqual(permissions.RoleIds.Distinct().Order()))
                throw new CurrentPermissionUnavailableException();
            // Versioned mappings apply only to this signed source field list and exact use.
            // Action, source object scope, live Bearer and view-pii were checked above/by issuer.
            // No foreign projection is consumed here. Actual PII/business state belongs to the
            // CRM Owner, hence is explicitly outside this source decision (not implicitly granted).
            var facts = source.Evaluate(source.Versions(mapping), mapping,
                fields.Select(x => new FieldPermissionMappingV1(x, use, "fieldPermissions/" + x, true, [])),
                fields.Select(x => new FieldPermissionConstraintsV1(x, use,
                    FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.Allowed,
                    FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.NotApplicable,
                    FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.NotApplicable)));
            if (!facts.IsAvailable) throw new CurrentPermissionUnavailableException();
            if (fields.Any(x => !facts.Fields[x].Uses[use].Allowed))
                throw new InquiryPermissionExceptionV1(403, "insufficient_field_scope");
        }
        // Use complete trusted source rows; never borrow the intake readable-field result.
        await RequireFields(InquiryIntakePermissionSourceV1.Resource, InquiryIntakePermissionSourceV1.Fields);
        await RequireFields(InquiryPrivacyFieldCatalogV1.Resource, ["exportArtifact"]);
        return new(required, [.. InquiryIntakePermissionSourceV1.Fields, "exportArtifact"], departments, true);
    }
}
