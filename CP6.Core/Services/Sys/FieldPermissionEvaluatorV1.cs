namespace CP6.Core.Services.Sys;

internal static class FieldPermissionEvaluatorV1
{
    public const string PolicyVersion = "MainStoredFieldMinThenMaterializeV1";

    public static FieldPermissionDecisionV1 Evaluate(FieldPermissionInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = Validate(input);
        if (errors.Count != 0) return new(input, [], errors);

        var fields = input.Fields.Order(StringComparer.Ordinal).ToArray();
        var rows = input.Rows.Distinct().OrderBy(r => r.RoleId).ThenBy(r => r.Id).ToLookup(r => r.Field, StringComparer.Ordinal);
        // Do not materialize a per-role 1. Only the absence of ALL stored rows in the
        // complete registry domain permits a registered default after aggregation.
        var access = fields.ToDictionary(f => f, f => rows[f].Any() ? rows[f].Min(r => r.Access) : 1, StringComparer.Ordinal);
        var mappings = input.Mappings.OrderBy(m => m.Use).ThenBy(m => m.ApiPath, StringComparer.Ordinal)
            .ToLookup(m => (m.Field, m.Use));
        var constraints = input.Constraints.Distinct().ToDictionary(c => (c.Field, c.Use));
        var uses = new Dictionary<(string, FieldPermissionUseV1), FieldPermissionUseDecisionV1>();

        foreach (var use in Enum.GetValues<FieldPermissionUseV1>())
        {
            var local = new Dictionary<string, FieldPermissionUseDecisionV1>(StringComparer.Ordinal);
            var dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                var mapped = mappings[(field, use)].ToArray();
                constraints.TryGetValue((field, use), out var checks);
                local[field] = EvaluateUse(access[field], use, mapped, checks, access);
                dependencies[field] = mapped.SelectMany(m => m.Dependencies)
                    .Where(d => !string.IsNullOrWhiteSpace(d) && access.ContainsKey(d))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            }

            // Resolve dependency gates without recursion (deep/cyclic graphs must not
            // overflow the stack). A dependency requires the SAME use, never a read-only
            // grant borrowed to authorize export/download/write.
            var remaining = fields.ToHashSet(StringComparer.Ordinal);
            while (remaining.Count != 0)
            {
                var ready = fields.Where(f => remaining.Contains(f)
                    && dependencies[f].All(d => uses.ContainsKey((d, use)))).ToArray();
                if (ready.Length == 0)
                {
                    foreach (var field in remaining)
                        uses[(field, use)] = Intersect([local[field], Failure(FieldPermissionStatusV1.Unavailable, "DEPENDENCY_CYCLE_OR_UNRESOLVED")]);
                    break;
                }
                foreach (var field in ready)
                {
                    var required = new List<FieldPermissionUseDecisionV1> { local[field] };
                    foreach (var dependency in dependencies[field])
                    {
                        var result = uses[(dependency, use)];
                        if (!result.Allowed)
                            required.Add(Failure(result.Status, "DEPENDENCY_" + result.Status.ToString().ToUpperInvariant() + ":" + dependency));
                    }
                    uses[(field, use)] = Intersect(required);
                    remaining.Remove(field);
                }
            }
        }

        return new(input, fields.Select(field => new FieldPermissionFieldV1(field, access[field],
            rows[field].Any() ? FieldPermissionOriginV1.EXPLICIT_AGGREGATE : FieldPermissionOriginV1.REGISTERED_DEFAULT,
            rows[field], Enum.GetValues<FieldPermissionUseV1>().SelectMany(u => mappings[(field, u)]),
            Enum.GetValues<FieldPermissionUseV1>().ToDictionary(u => u, u => uses[(field, u)]))), []);
    }

    private static List<string> Validate(FieldPermissionInputV1 input)
    {
        var errors = new List<string>();
        if (input.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(input.SubjectRef) || string.IsNullOrWhiteSpace(input.Resource))
            errors.Add("DECISION_SCOPE_REQUIRED");
        if (!KnownVersions(input.ExpectedVersions) || !KnownVersions(input.Versions)) errors.Add("SOURCE_VERSIONS_REQUIRED");
        if (input.ExpectedVersions != input.Versions) errors.Add("SOURCE_VERSION_MISMATCH");
        if (input.Versions?.PolicyVersion != PolicyVersion) errors.Add("POLICY_UNSUPPORTED");
        if (!input.RegistryComplete) errors.Add("REGISTRY_INCOMPLETE");
        if (!input.RoleDataComplete) errors.Add("ROLE_DATA_INCOMPLETE");
        var fields = input.Fields.ToHashSet(StringComparer.Ordinal);
        if (input.Fields.Any(string.IsNullOrWhiteSpace) || fields.Count != input.Fields.Count) errors.Add("REGISTRY_FIELDS_INVALID");
        foreach (var role in input.Roles)
            if (role is null || role.RoleId <= 0 || string.IsNullOrWhiteSpace(role.Version) || !role.IsEffective)
                errors.Add("EFFECTIVE_ROLE_INVALID");
        if (errors.Count != 0) return errors;

        if (input.Roles.GroupBy(r => r.RoleId).Any(g => g.Distinct().Count() != 1)) errors.Add("CONFLICTING_ROLE_VERSION");
        var roles = input.Roles.ToLookup(r => r.RoleId);
        foreach (var row in input.Rows)
        {
            if (row is null) { errors.Add("STORED_ROW_INVALID"); continue; }
            if (row.Id == Guid.Empty) errors.Add("STORED_ROW_ID_REQUIRED");
            if (row.Resource != input.Resource) errors.Add("STORED_RESOURCE_MISMATCH");
            if (!fields.Contains(row.Field)) errors.Add("UNKNOWN_STORED_FIELD");
            if (row.Access is < 1 or > 3) errors.Add("INVALID_ACCESS");
            if (!roles.Contains(row.RoleId)) errors.Add("UNKNOWN_ROLE");
            else if (roles[row.RoleId].Any(r => r.Version != row.RoleVersion)) errors.Add("ROLE_VERSION_MISMATCH");
        }
        foreach (var mapping in input.Mappings)
            if (mapping is null || !fields.Contains(mapping.Field) || !Enum.IsDefined(mapping.Use)
                || string.IsNullOrWhiteSpace(mapping.ApiPath)) errors.Add("MAPPING_INVALID");
        foreach (var constraint in input.Constraints)
            if (constraint is null || !fields.Contains(constraint.Field) || !Enum.IsDefined(constraint.Use)) errors.Add("CONSTRAINT_SCOPE_INVALID");
        if (errors.Count != 0) return errors;

        if (input.Rows.GroupBy(r => r.Id).Any(g => g.Distinct().Count() != 1)
            || input.Rows.GroupBy(r => (r.RoleId, r.Resource, r.Field)).Any(g => g.Select(r => r.Access).Distinct().Count() != 1))
            errors.Add("CONFLICTING_STORED_ROW");
        if (input.Constraints.GroupBy(c => (c.Field, c.Use)).Any(g => g.Distinct().Count() != 1)) errors.Add("CONFLICTING_USE_CONSTRAINTS");
        foreach (var group in input.Mappings.GroupBy(m => (m.Use, m.ApiPath)))
        {
            var first = group.First();
            if (group.Any(m => m.Field != first.Field || m.OwnerApproved != first.OwnerApproved
                || !m.Dependencies.Order(StringComparer.Ordinal).SequenceEqual(first.Dependencies.Order(StringComparer.Ordinal), StringComparer.Ordinal)))
                errors.Add("CONFLICTING_MAPPING");
        }
        return errors;
    }

    private static bool KnownVersions(FieldPermissionVersionsV1? versions)
        => versions is not null && !string.IsNullOrWhiteSpace(versions.PolicyVersion)
            && !string.IsNullOrWhiteSpace(versions.RegistryVersion) && !string.IsNullOrWhiteSpace(versions.MappingVersion)
            && !string.IsNullOrWhiteSpace(versions.SourcePermissionVersion);

    private static FieldPermissionUseDecisionV1 EvaluateUse(int access, FieldPermissionUseV1 use,
        FieldPermissionMappingV1[] mappings, FieldPermissionConstraintsV1? constraints, IReadOnlyDictionary<string, int> fields)
    {
        var checks = new List<FieldPermissionUseDecisionV1>();
        if (access == 3) checks.Add(Failure(FieldPermissionStatusV1.Denied, "FIELD_HIDDEN"));
        else if (use == FieldPermissionUseV1.Write && access == 2) checks.Add(Failure(FieldPermissionStatusV1.Denied, "FIELD_READ_ONLY"));
        if (mappings.Length == 0) checks.Add(Failure(FieldPermissionStatusV1.Unavailable, "MAPPING_REQUIRED"));
        foreach (var mapping in mappings)
        {
            if (!mapping.OwnerApproved) checks.Add(Failure(FieldPermissionStatusV1.Unavailable, "MAPPING_UNAPPROVED"));
            if (mapping.Dependencies.Any(d => string.IsNullOrWhiteSpace(d) || !fields.ContainsKey(d)))
                checks.Add(Failure(FieldPermissionStatusV1.Unavailable, "UNKNOWN_DEPENDENCY"));
        }
        if (constraints is null) checks.Add(Failure(FieldPermissionStatusV1.Unavailable, "USE_CONSTRAINTS_REQUIRED"));
        else
        {
            checks.Add(Check(constraints.Action, "ACTION", required: true));
            checks.Add(Check(constraints.Object, "OBJECT", required: true));
            checks.Add(Check(constraints.Online, "ONLINE"));
            checks.Add(Check(constraints.Projection, "PROJECTION"));
            checks.Add(Check(constraints.Pii, "PII"));
            checks.Add(Check(constraints.Purpose, "PURPOSE"));
            checks.Add(Check(constraints.Business, "BUSINESS"));
        }
        return Intersect(checks);
    }

    private static FieldPermissionUseDecisionV1 Check(FieldPermissionCheckV1 check, string name, bool required = false)
        => check switch
        {
            FieldPermissionCheckV1.Allowed => new(FieldPermissionStatusV1.Allowed, []),
            FieldPermissionCheckV1.NotApplicable when !required => new(FieldPermissionStatusV1.Allowed, []),
            FieldPermissionCheckV1.Denied => Failure(FieldPermissionStatusV1.Denied, name + "_DENIED"),
            _ => Failure(FieldPermissionStatusV1.Unavailable, name + "_UNAVAILABLE")
        };

    private static FieldPermissionUseDecisionV1 Failure(FieldPermissionStatusV1 status, string reason) => new(status, [reason]);

    private static FieldPermissionUseDecisionV1 Intersect(IEnumerable<FieldPermissionUseDecisionV1> checks)
    {
        var items = checks.ToArray();
        // Known denial is sufficient to refuse; retain unknown reasons as well.
        var status = items.Any(c => c.Status == FieldPermissionStatusV1.Denied) ? FieldPermissionStatusV1.Denied
            : items.Any(c => c.Status == FieldPermissionStatusV1.Unavailable) ? FieldPermissionStatusV1.Unavailable
            : FieldPermissionStatusV1.Allowed;
        return new(status, items.SelectMany(c => c.Reasons));
    }
}
