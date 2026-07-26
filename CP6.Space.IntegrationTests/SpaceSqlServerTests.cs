using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceSqlServerTests
{
    private const string ContentHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string WmsHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [SqlServerFact]
    public async Task Migration_creates_only_design_tables_and_separate_history()
    {
        await WithDatabaseAsync(async context =>
        {
            var tables = await ReadTableNamesAsync(context);

            Assert.Contains("Space_Model", tables);
            Assert.Contains("Space_ModelVersion", tables);
            Assert.Contains(SpaceContext.MigrationsHistoryTable, tables);
            Assert.DoesNotContain("__EFMigrationsHistory", tables);
            Assert.DoesNotContain("Space_Site", tables);
        });
    }

    [SqlServerFact]
    public async Task Tenant_site_and_version_number_constraints_are_enforced()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        await WithDatabaseAsync(
            async context =>
            {
                var model = SpaceModel.Create(tenantId, siteId);
                var coordinator = new SpaceModelVersionCoordinator(
                    new TestExecutionContext(tenantId, actorId));
                context.Models.Add(model);
                await context.SaveChangesAsync();

                var version = coordinator.CreateDraft(model, 1, "Draft");
                context.Versions.Add(version);
                await context.SaveChangesAsync();

                context.Models.Add(SpaceModel.Create(tenantId, siteId));
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());
                context.ChangeTracker.Clear();

                var duplicateVersion =
                    SpaceModelVersion.CreateDraft(tenantId, model.Id, 1, "Duplicate");
                context.Versions.Add(duplicateVersion);
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());
            },
            tenantId,
            actorId);
    }

    [SqlServerFact]
    public async Task Same_site_id_is_isolated_between_tenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await using (var first = CreateContext(
                                 connectionString,
                                 tenantA,
                                 Guid.NewGuid()))
                {
                    first.Models.Add(SpaceModel.Create(tenantA, siteId));
                    await first.SaveChangesAsync();
                }

                await using (var second = CreateContext(
                                 connectionString,
                                 tenantB,
                                 Guid.NewGuid()))
                {
                    second.Models.Add(SpaceModel.Create(tenantB, siteId));
                    await second.SaveChangesAsync();
                    Assert.Single(await second.Models.ToListAsync());
                }
            });
    }

    [SqlServerFact]
    public async Task RowVersion_rejects_the_second_concurrent_model_update()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                Guid modelId;
                await using (var seed = CreateContext(connectionString, tenantId, actorId))
                {
                    var model = SpaceModel.Create(tenantId, Guid.NewGuid());
                    seed.Models.Add(model);
                    await seed.SaveChangesAsync();
                    modelId = model.Id;
                }

                await using var first = CreateContext(connectionString, tenantId, actorId);
                await using var second = CreateContext(connectionString, tenantId, actorId);
                var a = await first.Models.SingleAsync(x => x.Id == modelId);
                var b = await second.Models.SingleAsync(x => x.Id == modelId);

                a.BeginCutover(Guid.NewGuid());
                await first.SaveChangesAsync();

                b.BeginCutover(Guid.NewGuid());
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                    () => second.SaveChangesAsync());
            });
    }

    [SqlServerFact]
    public async Task Published_version_cannot_be_mutated_through_the_context()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                Guid versionId;
                await using (var seed = CreateContext(connectionString, tenantId, actorId))
                {
                    var model = SpaceModel.Create(tenantId, Guid.NewGuid());
                    seed.Models.Add(model);
                    await seed.SaveChangesAsync();

                    var version = SpaceModelVersion.CreateDraft(
                        tenantId, model.Id, 1, "Published");
                    version.BeginValidation();
                    version.MarkReady(ContentHash, "space-v1", WmsHash);
                    version.BeginPublishing();
                    version.MarkPublished(actorId, DateTime.UtcNow);
                    seed.Versions.Add(version);
                    await seed.SaveChangesAsync();

                    model.SetPublishedVersion(version, ContentHash);
                    await seed.SaveChangesAsync();
                    versionId = version.Id;
                }

                await using var context = CreateContext(connectionString, tenantId, actorId);
                var published = await context.Versions.SingleAsync(x => x.Id == versionId);
                context.Remove(published);

                await Assert.ThrowsAsync<SpaceVersionStateException>(
                    () => context.SaveChangesAsync());
            });
    }

    private static async Task WithDatabaseAsync(
        Func<SpaceContext, Task> action,
        Guid? tenantId = null,
        Guid? actorId = null)
    {
        await WithDatabaseAsync(
            async (context, _) => await action(context),
            tenantId,
            actorId);
    }

    private static async Task WithDatabaseAsync(
        Func<SpaceContext, string, Task> action,
        Guid? tenantId = null,
        Guid? actorId = null)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE01_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;

        await using var context = CreateContext(
            connectionString,
            tenantId ?? Guid.NewGuid(),
            actorId ?? Guid.NewGuid());

        try
        {
            await context.Database.MigrateAsync();
            await action(context, connectionString);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        Guid tenantId,
        Guid actorId)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(
            options,
            new TestExecutionContext(tenantId, actorId),
            new TestClock());
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        SpaceContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT [name] FROM sys.tables ORDER BY [name]";
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
