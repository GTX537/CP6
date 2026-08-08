using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceExcelMappingSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 15, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Sql_migration_persists_rowversion_append_only_versions_and_tenant_scope()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenantA = Guid.NewGuid();
            Guid profileId;
            await using (var fixture = CreateContext(connectionString, tenantA))
            {
                var service = new SpaceExcelMappingService(
                    fixture.Context,
                    fixture.Execution,
                    fixture.Clock);
                var system = await service.GetProfileAsync(
                    SpaceExcelMappingService.SystemStandardProfileId);
                var created = await service.SaveProfileAsync(
                    new(
                        null,
                        "SQL mapping",
                        system.Definition,
                        CopyFromProfileId: system.Id,
                        CopyFromVersion: 1),
                    "sql-mapping-v1");
                Assert.False(string.IsNullOrEmpty(created.Profile.RowVersion));
                profileId = created.Profile.Id;

                var updated = await service.SaveProfileAsync(
                    new(
                        profileId,
                        "SQL mapping",
                        system.Definition with { UnknownColumnPolicy = "Ignore" },
                        created.Profile.RowVersion),
                    "sql-mapping-v2");
                Assert.Equal(2, updated.Profile.Version);
                Assert.NotEqual(
                    created.Profile.RowVersion,
                    updated.Profile.RowVersion);
            }

            await using (var verify = CreateContext(connectionString, tenantA))
            {
                Assert.Equal(
                    2,
                    await verify.Context.ExcelMappingProfileVersions.CountAsync(
                        item => item.ProfileId == profileId));
                Assert.Equal(
                    new[] { 1, 2 },
                    await verify.Context.ExcelMappingProfileVersions
                        .Where(item => item.ProfileId == profileId)
                        .OrderBy(item => item.Version)
                        .Select(item => item.Version)
                        .ToArrayAsync());
            }

            await using (var tenantB = CreateContext(
                connectionString,
                Guid.NewGuid()))
            {
                Assert.Empty(await tenantB.Context.ExcelMappingProfiles.ToListAsync());
                Assert.Empty(await tenantB.Context.ExcelMappingProfileVersions.ToListAsync());
            }
        });
    }

    private static async Task WithDatabaseAsync(Func<string, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6_Space_E03S02_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var migration = CreateContext(
            connectionString,
            Guid.NewGuid());
        try
        {
            await migration.Context.Database.MigrateAsync();
            await action(connectionString);
        }
        finally
        {
            await migration.Context.Database.EnsureDeletedAsync();
        }
    }

    private static ContextFixture CreateContext(
        string connectionString,
        Guid tenantId)
    {
        var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
        var clock = new FixedClock();
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            execution,
            clock);
        return new ContextFixture(context, execution, clock);
    }

    private sealed record ContextFixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        FixedClock Clock) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
