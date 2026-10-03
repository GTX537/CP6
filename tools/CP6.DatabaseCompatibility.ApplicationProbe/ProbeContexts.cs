using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.DatabaseCompatibility.Testing;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.ApplicationProbe;

internal static partial class AppProbe
{
    private static DbContextOptions<T> Options<T>(OwnedTestDatabase owned, DatabaseContextKind kind, DbConnection? connection = null)
        where T : DbContext
    {
        var profile = DatabaseMigrationProfile.For(owned.Database, kind);
        var builder = new DbContextOptionsBuilder<T>();
        if (connection is null)
            DatabaseContextOptions.Configure(builder, owned.Database, owned.ConnectionString,
                profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        else DatabaseContextOptions.Configure(builder, owned.Database, connection,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        return builder.Options;
    }

    private static CP6Context Core(OwnedTestDatabase owned, Guid tenant)
        => new(Options<CP6Context>(owned, DatabaseContextKind.Core), new TenantContext { CurrentTenantId = tenant });

    private static DbContext Context(OwnedTestDatabase owned, DatabaseContextKind kind, DbConnection? connection) => kind switch
    {
        DatabaseContextKind.Core => new CP6Context(Options<CP6Context>(owned, kind, connection)),
        DatabaseContextKind.Space => new SpaceContext(Options<SpaceContext>(owned, kind, connection), new ProbeExecution(), new SystemSpaceClock()),
        DatabaseContextKind.IdentityPriority => new IdentityMessagingContext(Options<IdentityMessagingContext>(owned, kind, connection)),
        DatabaseContextKind.ErpIntegration => new ErpIntegrationContext(Options<ErpIntegrationContext>(owned, kind, connection)),
        _ => throw new ProbeFailure("context-kind-known")
    };

    private static async Task<List<HistoryState>> ReadHistoriesAsync(OwnedTestDatabase owned,
        DbConnection? connection, DbTransaction? transaction, CancellationToken ct)
    {
        var result = new List<HistoryState>();
        foreach (var kind in Enum.GetValues<DatabaseContextKind>())
        {
            var profile = DatabaseMigrationProfile.For(owned.Database, kind);
            // SQL Priority/ERP schemas are installed by the immutable Core history.
            await using var db = Context(owned, profile.MigrationOwner, connection);
            if (transaction is not null) await db.Database.UseTransactionAsync(transaction, ct);
            var known = db.Database.GetMigrations().ToArray();
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
            Require(known.Length > 0 && applied.SequenceEqual(known, StringComparer.Ordinal) && pending.Length == 0,
                "history-exact-and-no-pending-" + kind);
            result.Add(new(kind.ToString(), profile.MigrationOwner.ToString(), known, applied, pending));
        }
        return result;
    }

    private sealed class ProbeExecution : ISpaceExecutionContext
    {
        public Guid TenantId => TenantContext.DefaultTenant;
        public Guid ActorId => Guid.Empty;
    }
}

internal sealed record HistoryState(string Context, string MigrationOwner, string[] Known, string[] Applied, string[] Pending);
