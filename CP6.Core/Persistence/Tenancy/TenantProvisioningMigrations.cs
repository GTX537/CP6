using System.Security.Cryptography;
using System.Text;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed class TenantProvisioningMigrations
{
    internal const string ErpVersion = "20261002151345_PostgreSqlErpIntegrationBaselineV1";
    internal const string IdentityVersion = "20261002151342_PostgreSqlIdentityPriorityBaselineV1";
    internal string Erp { get; }
    internal string Identity { get; }
    internal string Hash { get; }

    internal TenantProvisioningMigrations(NpgsqlConnection connection)
    {
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var erp = DatabaseMigrationProfile.For(database, DatabaseContextKind.ErpIntegration);
        var identity = DatabaseMigrationProfile.For(database, DatabaseContextKind.IdentityPriority);
        // Generation opens no connection; legacy baseline configuration requires public. The real
        // control connection remains on its explicit, separately checked pg_catalog path.
        var generation = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { SearchPath = "public" }.ConnectionString;
        using var erpContext = new ErpIntegrationContext(DatabaseContextOptions.Configure(
            new DbContextOptionsBuilder<ErpIntegrationContext>(), database, generation,
            erp.MigrationsAssembly, erp.HistoryTable, erp.HistorySchema).Options);
        using var identityContext = new IdentityMessagingContext(DatabaseContextOptions.Configure(
            new DbContextOptionsBuilder<IdentityMessagingContext>(), database, generation,
            identity.MigrationsAssembly, identity.HistoryTable, identity.HistorySchema).Options);
        Erp = Script(erpContext, ErpVersion);
        Identity = Script(identityContext, IdentityVersion);
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Erp + "\n--identity--\n" + Identity)));
    }

    private static string Script(DbContext context, string version)
    {
        if (!context.Database.GetMigrations().SequenceEqual([version]))
            throw new TenantProvisioningException("migration-version-rejected");
        return context.GetService<IMigrator>().GenerateScript("0", version, MigrationsSqlGenerationOptions.NoTransactions);
    }
}
