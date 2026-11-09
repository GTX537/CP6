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
