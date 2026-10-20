using CP6.Core.Services.Sys;

namespace CP6.Tests;

public class FieldPermissionDecisionV1Tests
{
    private static readonly Guid Tenant = Guid.Parse("b15891bf-33a8-492f-8ea8-c2eb8a2f42ba");
    private static readonly FieldPermissionVersionsV1 Versions = new(
        FieldPermissionEvaluatorV1.PolicyVersion, "registry-1", "mapping-1", "permissions-1");
    private static readonly FieldPermissionRoleV1[] Roles = [new(1, "role-A-1", true), new(2, "role-B-1", true)];

    private static FieldPermissionRowV1 Row(int role, int access, string field = "Cost")
        => new(Guid.NewGuid(), role, role == 1 ? "role-A-1" : "role-B-1", "order", field, access);

    private static FieldPermissionConstraintsV1 Allow(string field, FieldPermissionUseV1 use)
        => new(field, use, FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.Allowed,
            FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.NotApplicable,
            FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.Allowed);

    private static FieldPermissionInputV1 Input(IEnumerable<FieldPermissionRowV1>? rows = null,
        string[]? fields = null, bool registryComplete = true, bool roleDataComplete = true,
        FieldPermissionVersionsV1? versions = null, IEnumerable<FieldPermissionRoleV1>? roles = null,
        IEnumerable<FieldPermissionMappingV1>? mappings = null,
        IEnumerable<FieldPermissionConstraintsV1>? constraints = null)
    {
        fields ??= ["Cost", "Name"];
        return new(Tenant, "user:fixture", "order", Versions, versions ?? Versions,
            registryComplete, fields, roleDataComplete, roles ?? Roles, rows ?? [],
            mappings ?? fields.SelectMany(f => Enum.GetValues<FieldPermissionUseV1>()
                .Select(u => new FieldPermissionMappingV1(f, u, "$." + f, true, []))),
            constraints ?? fields.SelectMany(f => Enum.GetValues<FieldPermissionUseV1>().Select(u => Allow(f, u))));
    }

    [Theory]
    [InlineData(0, 3, 3)]
    [InlineData(0, 2, 2)]
    [InlineData(2, 3, 2)]
    [InlineData(1, 3, 1)]
    [InlineData(0, 0, 1)]
    public void Aggregates_actual_rows_before_materializing_registered_defaults(int a, int b, int expected)
    {
        var rows = new List<FieldPermissionRowV1>();
        if (a != 0) rows.Add(Row(1, a));
        if (b != 0) rows.Add(Row(2, b));
        var decision = FieldPermissionEvaluatorV1.Evaluate(Input(rows));
        Assert.True(decision.IsAvailable, string.Join(",", decision.Errors));
        var field = decision.Fields["Cost"];
        Assert.Equal(expected, field.Access);
        Assert.Equal(rows.Count == 0 ? FieldPermissionOriginV1.REGISTERED_DEFAULT : FieldPermissionOriginV1.EXPLICIT_AGGREGATE, field.Origin);
        Assert.Equal(rows.Count, field.Sources.Count);
        Assert.Equal(expected < 3, field.CanRead);
        Assert.Equal(expected == 1, field.CanWrite);
        Assert.Equal(expected < 3, field.CanExport);
        Assert.Equal(expected < 3, field.CanDownload);
        Assert.Equal(FieldPermissionOriginV1.REGISTERED_DEFAULT, decision.Fields["Name"].Origin);
    }

    [Fact]
    public void Effective_role_identity_keeps_the_complete_int_domain()
    {
        foreach(var id in new[]{int.MinValue,-1,0,1,int.MaxValue})
        {
            var role=new FieldPermissionRoleV1(id,"current-role",true);
            var row=Row(id,2) with{RoleVersion=role.Version};
            var decision=FieldPermissionEvaluatorV1.Evaluate(Input([row],roles:[role]));
            Assert.True(decision.IsAvailable,string.Join(",",decision.Errors));
            Assert.Equal(2,decision.Fields["Cost"].Access);
            Assert.False(decision.Fields["Cost"].CanWrite);
        }
    }

    [Fact]
    public void Incomplete_registry_roles_unknown_fields_access_and_versions_never_default_to_allow()
    {
        var bad = new[]
        {
            Input(registryComplete: false), Input(roleDataComplete: false),
            Input(versions: Versions with { RegistryVersion = "" }),
            Input(versions: Versions with { MappingVersion = "mapping-2" }),
            Input(versions: Versions with { SourcePermissionVersion = "permissions-2" }),
            Input(versions: Versions with { PolicyVersion = "other-policy" }),
            Input([Row(1, 0)]), Input([Row(1, 4)]), Input([Row(1, 1, "Unknown")]),
            Input([Row(1, 1) with { Resource = "other-resource" }]),
            Input([Row(3, 1)]), Input([Row(1, 1) with { RoleVersion = "stale" }]),
            Input(roles: [Roles[0] with { IsEffective = false }]),
            Input(roles: [Roles[0] with { Version = "" }]),
            Input(fields: ["Cost", "Cost"])
        };
        foreach (var input in bad)
        {
            var decision = FieldPermissionEvaluatorV1.Evaluate(input);
            Assert.False(decision.IsAvailable);
            Assert.NotEmpty(decision.Errors);
            Assert.Empty(decision.Fields);
        }
    }

    [Fact]
    public void Conflicting_same_identity_rows_reject_but_identical_repetition_is_idempotent()
    {
        var a = Row(1, 2);
        foreach (var rows in new[] { new[] { a, a with { Access = 1 } }, new[] { a, Row(1, 3) }, new[] { a, a with { Field = "Name" } } })
            Assert.Contains("CONFLICTING_STORED_ROW", FieldPermissionEvaluatorV1.Evaluate(Input(rows)).Errors);
        var repeated = FieldPermissionEvaluatorV1.Evaluate(Input([a, a, Row(2, 3)]));
        Assert.True(repeated.IsAvailable);
        Assert.Equal(2, repeated.Fields["Cost"].Access);
        Assert.Equal(2, repeated.Fields["Cost"].Sources.Count);
        Assert.False(FieldPermissionEvaluatorV1.Evaluate(Input(roles: [Roles[0], Roles[0] with { Version = "other" }])).IsAvailable);
    }

    [Fact]
    public void Read_write_export_download_intersect_their_own_constraints()
    {
        var checks = Enum.GetValues<FieldPermissionUseV1>().Select(u => Allow("Cost", u)).ToArray();
        checks[1] = checks[1] with { Action = FieldPermissionCheckV1.Denied };
        checks[2] = checks[2] with { Purpose = FieldPermissionCheckV1.Unavailable };
        checks[3] = checks[3] with { Object = FieldPermissionCheckV1.Denied };
        var field = FieldPermissionEvaluatorV1.Evaluate(Input(fields: ["Cost"], constraints: checks)).Fields["Cost"];
        Assert.Equal(1, field.Access);
        Assert.True(field.CanRead);
        Assert.False(field.CanWrite);
        Assert.False(field.CanExport);
        Assert.False(field.CanDownload);
        Assert.Contains("ACTION_DENIED", field.Uses[FieldPermissionUseV1.Write].Reasons);
        Assert.Equal(FieldPermissionStatusV1.Unavailable, field.Uses[FieldPermissionUseV1.Export].Status);
        Assert.Contains("OBJECT_DENIED", field.Uses[FieldPermissionUseV1.Download].Reasons);
    }

    [Fact]
    public void Each_applicable_restriction_is_required_and_no_object_right_means_no_fields()
    {
        var allowed = Allow("Cost", FieldPermissionUseV1.Read);
        foreach (var restriction in new[]
        {
            allowed with { Action = FieldPermissionCheckV1.NotApplicable },
            allowed with { Object = FieldPermissionCheckV1.NotApplicable },
            allowed with { Online = FieldPermissionCheckV1.Unavailable },
            allowed with { Projection = FieldPermissionCheckV1.Denied },
            allowed with { Pii = FieldPermissionCheckV1.Denied },
            allowed with { Business = FieldPermissionCheckV1.Denied }
        })
        {
            var field = FieldPermissionEvaluatorV1.Evaluate(Input(fields: ["Cost"], constraints: [restriction])).Fields["Cost"];
            Assert.False(field.CanRead);
            Assert.False(field.CanWrite);
            Assert.False(field.CanExport);
            Assert.False(field.CanDownload);
        }
        var noObject = Enum.GetValues<FieldPermissionUseV1>().Select(u => Allow("Cost", u) with { Object = FieldPermissionCheckV1.Denied });
        Assert.All(FieldPermissionEvaluatorV1.Evaluate(Input(fields: ["Cost"], constraints: noObject)).Fields["Cost"].Uses.Values,
            use => Assert.Equal(FieldPermissionStatusV1.Denied, use.Status));
    }

    [Fact]
    public void Missing_unapproved_mapping_and_unknown_or_hidden_dependencies_cannot_leak()
    {
        foreach (var mapping in new[]
        {
            new FieldPermissionMappingV1("Name", FieldPermissionUseV1.Read, "$.Name", false, []),
            new FieldPermissionMappingV1("Name", FieldPermissionUseV1.Read, "$.Name", true, ["Unknown"]),
            new FieldPermissionMappingV1("Name", FieldPermissionUseV1.Read, "$.Name", true, ["Cost"])
        })
        {
            var input = Input([Row(1, 3)], mappings: [mapping, new("Cost", FieldPermissionUseV1.Read, "$.Cost", true, [])]);
            var decision = FieldPermissionEvaluatorV1.Evaluate(input);
            Assert.False(decision.IsAvailable && decision.Fields["Name"].CanRead);
        }
        var missing = FieldPermissionEvaluatorV1.Evaluate(Input(mappings: [])).Fields["Name"];
        Assert.All(missing.Uses.Values, use => Assert.Equal(FieldPermissionStatusV1.Unavailable, use.Status));
        var cycle = FieldPermissionEvaluatorV1.Evaluate(Input(mappings:
            [new("Name", FieldPermissionUseV1.Read, "$.Name", true, ["Cost"]),
             new("Cost", FieldPermissionUseV1.Read, "$.Cost", true, ["Name"])]));
        Assert.False(cycle.Fields["Name"].CanRead);
        Assert.Equal(FieldPermissionStatusV1.Unavailable, cycle.Fields["Name"].Uses[FieldPermissionUseV1.Read].Status);
    }

    [Fact]
    public void Null_dependency_is_unavailable_instead_of_an_exception_or_allow()
    {
        var decision = FieldPermissionEvaluatorV1.Evaluate(Input(fields: ["Name"], mappings:
            [new("Name", FieldPermissionUseV1.Read, "$.Name", true, [null!])]));
        Assert.False(decision.Fields["Name"].CanRead);
        Assert.Contains("UNKNOWN_DEPENDENCY", decision.Fields["Name"].Uses[FieldPermissionUseV1.Read].Reasons);
    }

    [Fact]
    public void Dependencies_use_the_matching_purpose_and_input_order_does_not_change_results()
    {
        var rows = new[] { Row(1, 2), Row(2, 3) };
        var mappings = new List<FieldPermissionMappingV1>();
        var constraints = new List<FieldPermissionConstraintsV1>();
        foreach (var use in Enum.GetValues<FieldPermissionUseV1>())
        {
            mappings.Add(new("Name", use, "$.Name", true, ["Cost"]));
            mappings.Add(new("Cost", use, "$.Cost", true, []));
            constraints.Add(Allow("Name", use));
            constraints.Add(Allow("Cost", use) with { Purpose = use == FieldPermissionUseV1.Download
                ? FieldPermissionCheckV1.Denied : FieldPermissionCheckV1.Allowed });
        }
        var first = FieldPermissionEvaluatorV1.Evaluate(Input(rows, mappings: mappings, constraints: constraints));
        var reverse = FieldPermissionEvaluatorV1.Evaluate(Input(rows.Reverse(), ["Name", "Cost"],
            roles: Roles.Reverse(), mappings: mappings.AsEnumerable().Reverse(), constraints: constraints.AsEnumerable().Reverse()));
        Assert.True(first.Fields["Name"].CanRead);
        Assert.True(first.Fields["Name"].CanExport);
        Assert.False(first.Fields["Name"].CanWrite);
        Assert.False(first.Fields["Name"].CanDownload);
        foreach (var field in first.Fields.Keys)
        foreach (var use in Enum.GetValues<FieldPermissionUseV1>())
        {
            Assert.Equal(first.Fields[field].Uses[use].Status, reverse.Fields[field].Uses[use].Status);
            Assert.Equal(first.Fields[field].Uses[use].Reasons, reverse.Fields[field].Uses[use].Reasons);
        }
    }

    [Fact]
    public void Conflicting_mapping_or_use_constraints_never_choose_the_first_allow()
    {
        var allow = Allow("Cost", FieldPermissionUseV1.Read);
        var conflicts = new[]
        {
            Input(constraints: [allow, allow with { Action = FieldPermissionCheckV1.Denied }]),
            Input(mappings: [new("Cost", FieldPermissionUseV1.Read, "$.value", true, []),
                             new("Name", FieldPermissionUseV1.Read, "$.value", true, [])]),
            Input(mappings: [new("Cost", FieldPermissionUseV1.Read, "$.value", true, []),
                             new("Cost", FieldPermissionUseV1.Read, "$.value", true, ["Name"])]),
            Input(mappings: [new("Unknown", FieldPermissionUseV1.Read, "$.value", true, [])])
        };
        foreach (var input in conflicts)
        {
            var result = FieldPermissionEvaluatorV1.Evaluate(input);
            Assert.False(result.IsAvailable);
            Assert.Empty(result.Fields);
        }
        var unknown = FieldPermissionEvaluatorV1.Evaluate(Input(constraints: [allow with { Action = (FieldPermissionCheckV1)99 }]));
        Assert.Equal(FieldPermissionStatusV1.Unavailable, unknown.Fields["Cost"].Uses[FieldPermissionUseV1.Read].Status);
    }

    [Fact]
    public void Snapshot_and_results_do_not_retain_mutable_collections_and_calculation_is_repeatable()
    {
        var fields = new[] { "Cost" };
        var rows = new List<FieldPermissionRowV1> { Row(1, 3) };
        var roles = Roles.ToList();
        var deps = new List<string>();
        var maps = new List<FieldPermissionMappingV1> { new("Cost", FieldPermissionUseV1.Read, "$.Cost", true, deps) };
        var constraints = new List<FieldPermissionConstraintsV1> { Allow("Cost", FieldPermissionUseV1.Read) };
        var input = Input(rows, fields, roles: roles, mappings: maps, constraints: constraints);
        fields[0] = "Changed";
        rows.Clear(); roles.Clear(); maps.Clear(); constraints.Clear(); deps.Add("Unknown");
        var first = FieldPermissionEvaluatorV1.Evaluate(input);
        var second = FieldPermissionEvaluatorV1.Evaluate(input);
        Assert.True(first.IsAvailable);
        Assert.Equal(3, first.Fields["Cost"].Access);
        Assert.False(second.Fields["Cost"].CanRead);
        Assert.Empty(first.Fields["Cost"].Mappings[0].Dependencies);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)input.Fields)[0] = "Changed");
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, FieldPermissionFieldV1>)first.Fields).Clear());
        Assert.Equal(first.Fields["Cost"].Uses[FieldPermissionUseV1.Read].Reasons, second.Fields["Cost"].Uses[FieldPermissionUseV1.Read].Reasons);
    }
}
