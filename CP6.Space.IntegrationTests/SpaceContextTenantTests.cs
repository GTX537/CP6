using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceContextTenantTests
{
    private static readonly DateTime Now =
        new(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Query_filter_prevents_cross_tenant_reads()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var first = CreateContext(root, database, tenantA))
        {
            first.Models.Add(SpaceModel.Create(tenantA, Guid.NewGuid()));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext(root, database, tenantB);
        Assert.Empty(await second.Models.ToListAsync());
        Assert.Single(await second.Models.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Save_rejects_cross_tenant_entities()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();

        await using var context = CreateContext(root, database, tenantA);
        context.Models.Add(SpaceModel.Create(Guid.NewGuid(), Guid.NewGuid()));

        await Assert.ThrowsAsync<SpaceTenantScopeException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_fails_closed_without_verified_tenant()
    {
        var root = new InMemoryDatabaseRoot();
        var options = NewOptions(root, Guid.NewGuid().ToString("N"));
        await using var context = new SpaceContext(
            options,
            new TestExecutionContext(Guid.Empty, Guid.NewGuid()),
            new FixedClock());
        context.Models.Add(SpaceModel.Create(Guid.NewGuid(), Guid.NewGuid()));

        await Assert.ThrowsAsync<SpaceTenantScopeException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_stamps_utc_audit_fields()
    {
        var root = new InMemoryDatabaseRoot();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var context = new SpaceContext(
            NewOptions(root, Guid.NewGuid().ToString("N")),
            new TestExecutionContext(tenantId, actorId),
            new FixedClock());
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        context.Models.Add(model);

        await context.SaveChangesAsync();

        Assert.Equal(Now, model.CreatedAtUtc);
        Assert.Equal(actorId, model.CreatedBy);
    }

    [Fact]
    public void Ef_model_uses_frozen_tables_filters_and_constraints()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(
            new InMemoryDatabaseRoot(),
            Guid.NewGuid().ToString("N"),
            tenantId);

        var model = context.Model.FindEntityType(typeof(SpaceModel))!;
        var version = context.Model.FindEntityType(typeof(SpaceModelVersion))!;

        Assert.Equal("Space_Model", model.GetTableName());
        Assert.Equal("Space_ModelVersion", version.GetTableName());
        Assert.NotNull(model.GetQueryFilter());
        Assert.NotNull(version.GetQueryFilter());
        Assert.Contains(
            model.GetIndexes(),
            x => x.IsUnique &&
                 x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "SiteId" }));
        Assert.Contains(
            model.GetIndexes(),
            x => x.IsUnique &&
                 x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "ActiveDraftVersionId" }));
        Assert.Contains(
            version.GetIndexes(),
            x => x.IsUnique &&
                 x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "ModelId", "VersionNo" }));
        Assert.Contains(
            version.GetForeignKeys(),
            x => x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "ModelId" }));
        Assert.True(model.FindProperty(nameof(SpaceModel.RowVersion))!.IsConcurrencyToken);
        Assert.True(version.FindProperty(nameof(SpaceModelVersion.RowVersion))!.IsConcurrencyToken);
    }

    private static SpaceContext CreateContext(
        InMemoryDatabaseRoot root,
        string database,
        Guid tenantId) =>
        new(
            NewOptions(root, database),
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock());

    private static DbContextOptions<SpaceContext> NewOptions(
        InMemoryDatabaseRoot root,
        string database) =>
        new DbContextOptionsBuilder<SpaceContext>()
            .UseInMemoryDatabase(database, root)
            .Options;

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
