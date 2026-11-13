using CP6.Core.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

public sealed class RmaTenantReadModelTests
{
    [Fact]
    public void Request_read_model_reuses_own_mapping_and_separates_other_schema_version_and_design_time()
    {
        var a = Binding(Guid.NewGuid());
        var b = Binding(Guid.NewGuid());
        using var first = new NpgsqlConnection();
        using var second = new NpgsqlConnection();
        using var modelA = Open(first, a);
        using var againA = Open(second, a);
        using var modelB = Open(first, b);
        using var anotherLayout = Open(first, Binding(a.TenantId, layout: "test-other-layout"));
        using var anotherModel = Open(first, Binding(a.TenantId, model: "test-other-model"));

        Assert.Equal(RmaTenantReadContext.Provider, modelA.Database.ProviderName);
        Assert.Same(modelA.Model, againA.Model);
        Assert.NotSame(modelA.Model, modelB.Model);
        Assert.NotSame(modelA.Model, anotherLayout.Model);
        Assert.NotSame(modelA.Model, anotherModel.Model);
        Assert.NotSame(modelA.Model, modelA.GetService<IDesignTimeModel>().Model);
        Assert.Equal(new[] { "heads", "versions" }, Tables(modelA, a.SchemaName));
        Assert.Equal(new[] { "heads", "versions" }, Tables(modelB, b.SchemaName));
        Assert.Equal(new[] { "TenantId", "Id" }, modelA.Model.FindEntityType(typeof(RmaTenantHead))!
            .FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(new[] { "TenantId", "Id", "Version" }, modelA.Model.FindEntityType(typeof(RmaTenantVersion))!
            .FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Rma_read_v2_uses_explicit_compatibility_views_and_keeps_v1_tables()
    {
        using var connection = new NpgsqlConnection();
        var v1 = Binding(Guid.NewGuid());
        using var oldModel = Open(connection, v1);
        using var newModel = Open(connection, Binding(v1.TenantId, model: "rma-application-core-tenant-v2"));
        using var otherTenant = Open(connection, Binding(Guid.NewGuid()));

        var head = newModel.Model.FindEntityType(typeof(RmaTenantHead))!;
        var version = newModel.Model.FindEntityType(typeof(RmaTenantVersion))!;
        Assert.Equal("application_heads_v2", head.GetViewName());
        Assert.Equal("application_versions_v2", version.GetViewName());
        Assert.Equal(v1.SchemaName, head.GetViewSchema());
        Assert.Equal(v1.SchemaName, version.GetViewSchema());
        Assert.Equal(new[] { "heads", "versions" }, Tables(oldModel, v1.SchemaName));
        Assert.Equal(new[] { "heads", "versions" }, Tables(otherTenant, otherTenant.Binding.SchemaName));
        Assert.Contains($"FROM {v1.SchemaName}.application_heads_v2", newModel.Heads.ToQueryString());
        Assert.Contains($"FROM {otherTenant.Binding.SchemaName}.versions", otherTenant.Versions.ToQueryString());
    }

    private static RmaTenantReadContext Open(NpgsqlConnection connection, TenantStorageBindingV1 binding) =>
        new(RmaTenantReadContext.Options(connection).Options, binding);

    private static string[] Tables(RmaTenantReadContext context, string schema)
    {
        var tables = context.Model.GetRelationalModel().Tables.ToArray();
        Assert.All(tables, table => Assert.Equal(schema, table.Schema));
        return tables.Select(table => table.Name).Order(StringComparer.Ordinal).ToArray();
    }

    private static TenantStorageBindingV1 Binding(Guid tenant, string layout = TenantStorageBindingV1.SupportedLayout,
        string model = RmaTenantStorageV1.CoreReadProfile) =>
        new(tenant, TenantRmaReadStore.Host, "fixture", layout, model, "fixture", "fixture", "fixture",
            [], TenantStorageState.Ready, RmaTenantStorageV1.CoreReadMigration);
}
