using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Migrations;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Crm.LegacyExit.Tests;

public class LegacyExitSqlTests
{
    [Fact]
    public async Task SimulatedUpgradeRetainsTwentyPopulatedTablesAndCurrentIdentityErpIsolation()
    {
        await using var fixture = await LegacyExitSqlFixture.CreateAsync();
        await fixture.SeedAllLegacyTablesAsync();
        var before = await fixture.ReadLegacyEvidenceAsync();
        Assert.Equal(20, before.Count);
        Assert.All(before, row => Assert.Equal(1L, row.Rows));

        // The isolated database contains the current model plus the historical
        // foundation tables. Its synthetic history positions EF immediately
        // before the new, data-preserving model retirement migration.
        await using (var db = fixture.Context(Guid.NewGuid()))
        {
            var history = db.GetService<IHistoryRepository>();
            await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
            foreach (var id in db.GetService<IMigrationsAssembly>().Migrations.Keys.Where(x => x != LegacyExitModelTests.ExitMigration))
                await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(id, "8.0.30")));
            await db.Database.MigrateAsync();
            Assert.Contains(LegacyExitModelTests.ExitMigration, await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using (var db = fixture.Context(tenantA))
        {
            db.CrmServiceTokenRecords.Add(new CrmServiceTokenRecord { Issuer = "urn:c04b:simulation", Jti = Guid.NewGuid().ToString(),
                ClientId = "c04b-simulated-client", TenantId = tenantA, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5) });
            foreach (var tenant in new[] { tenantA, tenantB })
                db.CrmIdentitySnapshots.Add(new CrmIdentitySnapshot { TenantId = tenant, AggregateId = "simulation-member",
                    EventType = "simulation-only", Version = 1, PayloadJson = "{}", PayloadSha256 = new string('0', 64), UpdatedAtUtc = DateTimeOffset.UtcNow });
            db.BusinessPartners.Add(new BusinessPartner { BpCd = "C04B-SIM", BpName = "Simulated business partner",
                BaseCd = "SIM", CrmAccountId = accountId, CustomerFlg = true });
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Context(tenantA))
        {
            Assert.Single(await db.CrmServiceTokenRecords.Where(x => x.TenantId == tenantA).ToListAsync());
            Assert.Single(await db.CrmIdentitySnapshots.Where(x => x.TenantId == tenantA).ToListAsync());
            var partner = Assert.Single(await db.BusinessPartners.ToListAsync());
            Assert.Equal(tenantA, partner.TenantId);
            Assert.Equal(accountId, partner.CrmAccountId);
        }
        await using (var db = fixture.Context(tenantB))
        {
            Assert.Empty(await db.BusinessPartners.ToListAsync());
            Assert.Empty(await db.CrmServiceTokenRecords.Where(x => x.TenantId == tenantB).ToListAsync());
            Assert.Single(await db.CrmIdentitySnapshots.Where(x => x.TenantId == tenantB).ToListAsync());
        }
        var after = await fixture.ReadLegacyEvidenceAsync();
        Assert.Equal(before, after);
        await fixture.WriteEvidenceAsync(after);
    }
}

internal sealed record LegacyTableEvidence(string Table, long Rows, string RowSha256, string SchemaSha256);

internal sealed class LegacyExitSqlFixture : IAsyncDisposable
{
    private const string Prefix = "CP6_C04B_Exit_Test_";
    private readonly string admin;
    private bool created;
    private Guid databaseGuid;
    internal string Name { get; } = Prefix + Guid.NewGuid().ToString("N");
    internal string ConnectionString { get; private set; } = "";
    private LegacyExitSqlFixture(string admin) => this.admin = admin;

    internal CP6Context Context(Guid tenant) => new(new DbContextOptionsBuilder<CP6Context>()
        .UseSqlServer(ConnectionString, options => options.CommandTimeout(120)).Options,
        new TenantContext { CurrentTenantId = tenant });

    internal static async Task<LegacyExitSqlFixture> CreateAsync()
    {
        var input = Environment.GetEnvironmentVariable("C04B_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(input)) throw new InvalidOperationException("C04B_TEST_SQL_CONNECTION is required; SQL acceptance cannot skip.");
        var builder = new SqlConnectionStringBuilder(input) { Pooling = false };
        if (!string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("C04B fixture admin must select master.");
        var fixture = new LegacyExitSqlFixture(builder.ConnectionString);
        await using var admin = new SqlConnection(fixture.admin);
        await admin.OpenAsync();
        using (var identity = new SqlCommand("SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128));", admin))
            if (!string.Equals((string?)await identity.ExecuteScalarAsync(), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("C04B SQL fixture must use the local machine.");
        using (var create = new SqlCommand($"CREATE DATABASE [{fixture.Name}];", admin))
            await create.ExecuteNonQueryAsync();
        fixture.created = true;
        builder.InitialCatalog = fixture.Name;
        fixture.ConnectionString = builder.ConnectionString;
        try
        {
            fixture.databaseGuid = await fixture.ScalarAsync<Guid>("SELECT database_guid FROM sys.database_recovery_status WHERE database_id=DB_ID();");
            await using var db = fixture.Context(Guid.NewGuid());
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(new CrmFoundation().UpOperations))
                await fixture.ExecuteAsync(command.CommandText);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var c = new SqlConnection(ConnectionString);
        await c.OpenAsync();
        return c;
    }
    private async Task ExecuteAsync(string sql)
    {
        await using var c = await OpenAsync();
        using var command = new SqlCommand(sql, c) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var c = await OpenAsync();
        using var command = new SqlCommand(sql, c) { CommandTimeout = 120 };
        return (T)(await command.ExecuteScalarAsync())!;
    }
    private static string Id(string value) => "[" + value.Replace("]", "]]") + "]";

    internal async Task SeedAllLegacyTablesAsync()
    {
        var pending = LegacyExitModelTests.Tables.ToHashSet(StringComparer.Ordinal);
        var inserted = new HashSet<string>(StringComparer.Ordinal);
        var syntheticId = Guid.NewGuid().ToString();
        var syntheticTenant = Guid.NewGuid().ToString();
        while (pending.Count > 0)
        {
            var progress = false;
            foreach (var table in pending.OrderBy(x => x).ToArray())
            {
                await using var c = await OpenAsync();
                var parents = new List<string>();
                using (var dependencies = new SqlCommand("SELECT DISTINCT OBJECT_NAME(f.referenced_object_id) FROM sys.foreign_key_columns f JOIN sys.columns c ON c.object_id=f.parent_object_id AND c.column_id=f.parent_column_id WHERE f.parent_object_id=OBJECT_ID(@table) AND c.is_nullable=0;", c))
                {
                    dependencies.Parameters.AddWithValue("@table", "dbo."+table);
                    await using var reader = await dependencies.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) parents.Add(reader.GetString(0));
                }
                if (parents.Any(x => !inserted.Contains(x))) continue;
                var columns = new List<string>();
                var values = new List<string>();
                using (var query = new SqlCommand("SELECT c.name,t.name,c.max_length FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id WHERE c.object_id=OBJECT_ID(@table) AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0 AND t.name NOT IN ('timestamp','rowversion') ORDER BY c.column_id;", c))
                {
                    query.Parameters.AddWithValue("@table", "dbo."+table);
                    await using var reader = await query.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var name = reader.GetString(0); var type = reader.GetString(1); var length = reader.GetInt16(2);
                        columns.Add(Id(name));
                        values.Add(type switch
                        {
                            "uniqueidentifier" => "'"+(name == "TenantId" ? syntheticTenant : syntheticId)+"'",
                            "nvarchar" or "nchar" => "LEFT(N'C04B-SIMULATED',"+(length < 0 ? 14 : length/2)+")",
                            "varchar" or "char" => "LEFT('C04B-SIMULATED',"+(length < 0 ? 14 : length)+")",
                            "datetime2" or "datetime" or "smalldatetime" or "date" or "datetimeoffset" => "'2026-09-16T00:00:00'",
                            "bit" or "int" or "smallint" or "tinyint" or "bigint" or "decimal" or "numeric" or "float" or "real" or "money" => "0",
                            "varbinary" or "binary" => "0x00",
                            _ => throw new InvalidOperationException("Unreviewed synthetic column type.")
                        });
                    }
                }
                using var insert = new SqlCommand("INSERT dbo."+Id(table)+" ("+string.Join(',', columns)+") VALUES ("+string.Join(',', values)+");", c);
                await insert.ExecuteNonQueryAsync();
                inserted.Add(table); pending.Remove(table); progress = true;
            }
            if (!progress) throw new InvalidOperationException("Synthetic seed dependency cycle or external FK needs explicit review.");
        }
    }

    internal async Task<List<LegacyTableEvidence>> ReadLegacyEvidenceAsync()
    {
        var result = new List<LegacyTableEvidence>();
        foreach (var table in LegacyExitModelTests.Tables)
        {
            var count = await ScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo."+Id(table)+";");
            var rows = await ScalarAsync<string>("SELECT (SELECT * FROM dbo."+Id(table)+" ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES);");
            var schema = await ScalarAsync<string>("SELECT (SELECT c.column_id,c.name,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_computed,c.default_object_id FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo."+table+"') ORDER BY c.column_id FOR JSON PATH);");
            result.Add(new(table, count, Hash(rows), Hash(schema)));
        }
        return result;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal async Task WriteEvidenceAsync(List<LegacyTableEvidence> tables)
    {
        var path = Environment.GetEnvironmentVariable("C04B_SQL_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("C04B_SQL_EVIDENCE_PATH must bind a new evidence file.");
        var report = new { format="CP6.C04B.SimulatedSqlAcceptance.v1", observedAtUtc=DateTimeOffset.UtcNow,
            dataOrigin="synthetic", environment="local-sql-server", database=Name, databaseGuid,
            migration=LegacyExitModelTests.ExitMigration, legacyTables=tables, legacySchemaAndRowsUnchanged=true,
            identityTokenRoundtrip=true, twoTenantProjectionRoundtrip=true, erpExternalAccountAssociationRoundtrip=true,
            erpOtherTenantInvisible=true, productionAcceptance=false, originalDatabasesModified=false };
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions { WriteIndented=true });
    }

    public async ValueTask DisposeAsync()
    {
        if (!created) return;
        if (!Name.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(Name[Prefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside owned C04B fixture.");
        await using var c = new SqlConnection(admin);
        await c.OpenAsync();
        using var binding = new SqlCommand("SELECT database_guid FROM sys.database_recovery_status WHERE database_id=DB_ID(@name);", c);
        binding.Parameters.AddWithValue("@name", Name);
        var currentGuid = (Guid?)await binding.ExecuteScalarAsync();
        if (databaseGuid != Guid.Empty && currentGuid != databaseGuid)
            throw new InvalidOperationException("Refusing cleanup of a replaced test database.");
        using var drop = new SqlCommand($"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}];", c) { CommandTimeout=60 };
        await drop.ExecuteNonQueryAsync();
        created = false;
    }
}
