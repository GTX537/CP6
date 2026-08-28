using System.Diagnostics;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceReleaseRehearsalRecoverySqlServerTests(
    ITestOutputHelper output)
{
    [SqlServerFact]
    public async Task Checksum_backup_restore_preserves_published_and_wms_state()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var suffix = Guid.NewGuid().ToString("N");
        var databaseName = $"CP6SpaceGaRecovery_{suffix}";
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = databaseName,
            TrustServerCertificate = true,
        }.ConnectionString;
        var backupPath = Path.Combine(
            Path.GetTempPath(),
            $"cp6-space-ga-recovery-{suffix}.bak");
        var publishedHash = new string('a', 64);
        const int expectedWmsWrites = 1;

        try
        {
            await using (var context = CreateContext(connectionString))
            {
                await context.Database.MigrateAsync();
                await context.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE dbo.SpaceGaReleaseRehearsalProbe
                    (
                        Id int NOT NULL PRIMARY KEY,
                        PublishedHash char(64) NOT NULL,
                        WmsWrites int NOT NULL
                    );
                    """);
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT dbo.SpaceGaReleaseRehearsalProbe
                        (Id, PublishedHash, WmsWrites)
                    VALUES (1, {publishedHash}, {expectedWmsWrites});
                    """);
            }

            await ExecuteMasterAsync(
                baseConnection,
                $"BACKUP DATABASE [{databaseName}] TO DISK=@path " +
                "WITH INIT,CHECKSUM;",
                backupPath);

            await using (var context = CreateContext(connectionString))
            {
                await context.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE dbo.SpaceGaReleaseRehearsalProbe
                    SET PublishedHash = REPLICATE('b', 64), WmsWrites = 2
                    WHERE Id = 1;
                    """);
            }

            var stopwatch = Stopwatch.StartNew();
            await ExecuteMasterAsync(
                baseConnection,
                $"""
                ALTER DATABASE [{databaseName}]
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                RESTORE DATABASE [{databaseName}] FROM DISK=@path
                    WITH REPLACE,CHECKSUM;
                ALTER DATABASE [{databaseName}] SET MULTI_USER;
                DBCC CHECKDB ([{databaseName}]) WITH NO_INFOMSGS;
                """,
                backupPath);
            stopwatch.Stop();

            await using var restored = new SqlConnection(connectionString);
            await restored.OpenAsync();
            await using var command = restored.CreateCommand();
            command.CommandText =
                "SELECT PublishedHash, WmsWrites " +
                "FROM dbo.SpaceGaReleaseRehearsalProbe WHERE Id = 1;";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(publishedHash, reader.GetString(0).Trim());
            Assert.Equal(expectedWmsWrites, reader.GetInt32(1));
            Assert.False(await reader.ReadAsync());
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(240));
            Assert.True(new FileInfo(backupPath).Length > 0);

            output.WriteLine(
                "SPACE_GA_MANUAL_RECOVERY_SECONDS={0}",
                stopwatch.Elapsed.TotalSeconds);
            output.WriteLine(
                "SPACE_GA_RECOVERY_BACKUP_BYTES={0}",
                new FileInfo(backupPath).Length);
        }
        finally
        {
            await DropDatabaseAsync(baseConnection, databaseName);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
    }

    private static CP6Context CreateContext(string connectionString)
    {
        var tenant = new TenantContext
        {
            CurrentTenantId = Guid.Parse(
                "11111111-1111-1111-1111-111111111111"),
        };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(connectionString)
            .Options;
        return new CP6Context(options, tenant);
    }

    private static async Task ExecuteMasterAsync(
        string baseConnection,
        string sql,
        string backupPath)
    {
        var masterConnection = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = "master",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 300;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@path", backupPath);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(
        string baseConnection,
        string databaseName)
    {
        var masterConnection = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = "master",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}]
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
