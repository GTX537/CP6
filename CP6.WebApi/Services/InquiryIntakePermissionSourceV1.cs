using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

internal sealed record InquiryPermissionFactsV1(string[] Permissions, string[] Fields, Guid[] Departments, bool Supervisor);

/// <summary>Current human intake projection facts, never a service principal or a business
/// decision. Caller holds Main's authentication root. CRM must intersect these signed facts
/// with the actual resource, PII state, current authorization and final transaction fence.</summary>
internal static class InquiryIntakePermissionSourceV1
{
    internal const string Resource = "crm-intake";
    internal const string MappingVersion = "MainInquiryIntakeReadFieldsV1";
    // Exact R3V15 intake source field -> signed field name. No lead-edit/export/download use.
    internal static IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(new[]
        { "contactName", "email", "phone", "company", "need", "riskCategories" });

    internal static async Task<InquiryPermissionFactsV1> ReadAsync(CP6Context db, Guid subject,
        string purpose, string[] required, UserPermissionContext permissions,
        Func<AuthorizationCatalogSnapshotV1> capture, CancellationToken ct)
    {
        var catalog = capture();
        if (!catalog.IsSealed || !catalog.Fields.TryGetValue(Resource, out var definitions)
            || !definitions.Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(Fields.Order(StringComparer.Ordinal)))
            throw new CurrentPermissionUnavailableException();

        // Same current human supervisor semantics as Main CrmOidcDirectory, with a complete
        // healthy department tree. No user/collaborator shortcut and no all-tenant UUID grant.
        var scope = permissions.DataScopes.GetValueOrDefault("crm-lead", 1);
        if (scope is < 1 or > 5) throw new CurrentPermissionUnavailableException();
        var organization = await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var healthy = organization.Values.Where(x => IamUserAdministrationV1.DepartmentAvailable(x.Id, organization)).ToArray();
        var own = healthy.SingleOrDefault(x => x.Id == permissions.DeptId);
        var departments = healthy.Where(x => scope switch
        {
            2 => own is not null && x.Id == own.Id,
            3 => own is not null && x.Path.StartsWith(own.Path, StringComparison.Ordinal),
            4 => permissions.CustomDeptIds.GetValueOrDefault("crm-lead", []).Contains(x.Id),
            5 => true,
            _ => false
        }).Select(x => x.Id).Order().ToArray();
        if (!permissions.ActionKeys.Contains("crm-lead:assign") || departments.Length == 0)
            throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        // Frozen wire limits do not authorize truncating/remapping the real department set.
        if (departments.Length > 256)
            throw new InquiryPermissionExceptionV1(422, "inquiry_projection_scope_not_representable");

        var source = await new CurrentFieldPermissionSourceReaderV1(db, new HttpContextAccessor(), () => catalog)
            .ReadTrustedSubjectAsync(Resource, subject, ct);
        if (source.TenantId != db.CurrentTenantId || source.SubjectId != subject
            || !source.Roles.Select(x => x.RoleId).Order().SequenceEqual(permissions.RoleIds.Distinct().Order()))
            throw new CurrentPermissionUnavailableException();
        // Reuse the accepted stored-MIN then registered-default calculation. Inspect only
        // source Access; empty use constraints intentionally cannot grant a business use.
        var facts = source.Evaluate(source.Versions(MappingVersion), MappingVersion, [], []);
        if (!facts.IsAvailable) throw new CurrentPermissionUnavailableException();
        var pii = permissions.ActionKeys.Contains("crm-lead:view-pii");
        var readable = pii ? Fields.Where(x => facts.Fields[x].Access is 1 or 2).ToArray() : [];
        if (purpose is "intake:release" or "intake:reject" && readable.Length != Fields.Count)
            throw new InquiryPermissionExceptionV1(403, "insufficient_field_scope");
        var actions = required.ToList();
        if (pii && !actions.Contains("crm-lead:view-pii")) actions.Add("crm-lead:view-pii");
        // Optional UI capabilities remain read-purpose facts. CRM intersects each action's
        // current resource scope; a write still obtains its own fresh purpose-bound proof.
        if (purpose is "intake:list" or "intake:detail" && readable.Length == Fields.Count)
            foreach (var action in new[] { "crm-lead:add", "crm-lead:edit" })
                if (permissions.ActionKeys.Contains(action)) actions.Add(action);
        return new(actions.ToArray(), readable, departments, true);
    }
}
