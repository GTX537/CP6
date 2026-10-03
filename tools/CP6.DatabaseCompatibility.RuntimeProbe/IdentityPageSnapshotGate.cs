using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class IdentityPageSnapshotGate
{
    public static async Task<string> ConsistentPageAsync(RuntimeFixture fixture)
    {
        ProbeAssert.Require(fixture.IsPostgreSql, "The actual MVCC page consistency gate is scoped only to PostgreSQL.");
        await fixture.VerifyOwnerAsync();
        var tenant = Guid.NewGuid();
        var nonce = Guid.NewGuid().ToString("N");
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var keys = Path.GetFullPath(Path.Combine(temporaryRoot, "CP6Wp3IdentityPage_" + nonce));
        RequireExactDirectory(temporaryRoot, keys, nonce);
        ProbeAssert.Require(!Directory.Exists(keys), "The page consistency key directory must use a fresh exact task nonce.");
        var contracts = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "contracts", "events", "platform"));
        var runtime = new CrmIdentityRuntime(new CrmIdentityOptions
        {
            Enabled = true,
            Issuer = "https://identity.cp6.test",
            Tenants = new() { [tenant] = "us" },
            ProjectionReaderClientIds = ["wp3-native-reader"]
        }, new IdentityEventValidator(Cp6ContractBundle.Load(contracts), "https://identity.cp6.test"), contractDirectory: contracts);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = budget.Token;
        var barrier = new GenerationBarrier();
        CP6Context? readerContext = null;
        CP6Context? writerContext = null;
        Task<IdentityVersionPage>? pageTask = null;
        try
        {
            Directory.CreateDirectory(keys);
            var protection = DataProtectionProvider.Create(new DirectoryInfo(keys),
                options => options.SetApplicationName("CP6.WP3.IdentityPage." + nonce));
            await using (var setup = fixture.Core())
            {
                setup.CrmIdentityBootstrapStates.Add(new()
                {
                    TenantId = tenant,
                    ContractBundleSha256 = runtime.ContractBundleSha256,
                    CompletedAtUtc = runtime.Clock.GetUtcNow()
                });
                setup.CrmIdentitySnapshots.AddRange(Snapshot(runtime, tenant, 1, 1), Snapshot(runtime, tenant, 2, 1));
                await setup.SaveChangesAsync(cancellationToken);
            }
            var profile = DatabaseMigrationProfile.For(fixture.Database, DatabaseContextKind.Core);
            var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), fixture.Database,
                fixture.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema)
                .AddInterceptors(barrier).Options;
            readerContext = new CP6Context(options);
            writerContext = fixture.Core();
            await readerContext.Database.OpenConnectionAsync(cancellationToken);
            await writerContext.Database.OpenConnectionAsync(cancellationToken);
            var readerConnection = readerContext.Database.GetDbConnection();
            var writerConnection = writerContext.Database.GetDbConnection();
            var readerSession = await SessionAsync(readerConnection, cancellationToken);
            var writerSession = await SessionAsync(writerConnection, cancellationToken);
            ProbeAssert.Require(readerSession > 0 && writerSession > 0 && readerSession != writerSession &&
                !ReferenceEquals(readerConnection, writerConnection),
                "Reader and concurrent writer must use two distinct actual PostgreSQL physical sessions.");
            var originalGeneration = await writerContext.CrmIdentityTenantGenerations.AsNoTracking()
                .Where(value => value.TenantId == tenant).Select(value => value.Generation).SingleAsync(cancellationToken);
            ProbeAssert.Require(originalGeneration > 0, "The valid native page fixture must have a committed durable generation.");
            barrier.Context = readerContext;
            var reader = new IdentitySnapshotReader(readerContext, runtime, protection);
            pageTask = reader.ReadVersionsAsync(tenant, null, 1, cancellationToken);
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            ProbeAssert.Require(barrier.Isolation == IsolationLevel.RepeatableRead &&
                ReferenceEquals(barrier.Connection, readerConnection),
                "The barrier must pause the actual Reader generation SELECT in its real RepeatableRead transaction.");

            var row = await writerContext.CrmIdentitySnapshots.SingleAsync(value =>
                value.TenantId == tenant && value.AggregateId == "permission:role:1", cancellationToken);
            var changed = Snapshot(runtime, tenant, 1, 2);
            row.Version = changed.Version;
            row.PayloadJson = changed.PayloadJson;
            row.PayloadSha256 = changed.PayloadSha256;
            row.UpdatedAtUtc = changed.UpdatedAtUtc;
            await writerContext.SaveChangesAsync(cancellationToken);
            ProbeAssert.Require(writerContext.Database.CurrentTransaction is null,
                "The writer's actual implicit SaveChanges transaction must finish before the paused Reader resumes.");
            var committedGeneration = await writerContext.CrmIdentityTenantGenerations.AsNoTracking()
                .Where(value => value.TenantId == tenant).Select(value => value.Generation).SingleAsync(cancellationToken);
            ProbeAssert.Require(committedGeneration > originalGeneration &&
                (await writerContext.CrmIdentitySnapshots.AsNoTracking().SingleAsync(value =>
                    value.TenantId == tenant && value.AggregateId == "permission:role:1", cancellationToken)).Version == 2,
                "The other real session must observe committed version2 and generation advance before releasing the Reader barrier.");
            barrier.Resume();
            var page = await pageTask;
            ProbeAssert.Require(page.Boundary == originalGeneration.ToString(CultureInfo.InvariantCulture) &&
                page.Items.Length == 1 && page.Items[0].AggregateId == "permission:role:1" &&
                page.Items[0].Version == 1 && page.NextCursor is not null,
                "One actual Reader page must retain old generation and old returned-row version despite the intervening committed write.");
            try
            {
                await reader.ReadVersionsAsync(tenant, page.NextCursor, 1, cancellationToken);
                throw new ProbeAssertionException("The next page must reject the prior cursor after the real concurrent commit.");
            }
            catch (IdentityReadException exception) when (exception.Message == "C02_SNAPSHOT_BOUNDARY_CHANGED") { }
            return $"PostgreSQL actual Reader RepeatableRead: session{readerSession} paused after generation SELECT; distinct session{writerSession} committed returned-row version2 and generation advance; same page retained generation{originalGeneration}/version1 and next cursor rejected changed boundary. SQL Server was not executed by this gate.";
        }
        finally
        {
            barrier.Resume();
            budget.Cancel();
            if (pageTask is not null)
            {
                try { await pageTask; }
                catch { /* Preserve the case's original failure after the actual Reader transaction has finished. */ }
            }
            try
            {
                if (readerContext is not null) await readerContext.DisposeAsync();
                if (writerContext is not null) await writerContext.DisposeAsync();
                await using var cleanup = fixture.Core();
                await using var transaction = await cleanup.Database.BeginTransactionAsync();
                await cleanup.CrmIdentitySnapshots.Where(value => value.TenantId == tenant).ExecuteDeleteAsync();
                await cleanup.CrmIdentityBootstrapStates.Where(value => value.TenantId == tenant).ExecuteDeleteAsync();
                await cleanup.CrmIdentityTenantGenerations.Where(value => value.TenantId == tenant).ExecuteDeleteAsync();
                await transaction.CommitAsync();
                ProbeAssert.Require(!await cleanup.CrmIdentitySnapshots.AnyAsync(value => value.TenantId == tenant) &&
                    !await cleanup.CrmIdentityBootstrapStates.AnyAsync(value => value.TenantId == tenant) &&
                    !await cleanup.CrmIdentityTenantGenerations.AnyAsync(value => value.TenantId == tenant),
                    "The exact owned page fixture tenant must be absent after snapshot/bootstrap/generation cleanup.");
            }
            finally
            {
                RequireExactDirectory(temporaryRoot, keys, nonce);
                if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
                ProbeAssert.Require(!Directory.Exists(keys), "The exact page nonce key directory must be removed without archiving keys.");
            }
        }
    }

    private static async Task<int> SessionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        command.CommandTimeout = 15;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static CrmIdentitySnapshot Snapshot(CrmIdentityRuntime runtime, Guid tenant, int role, int version)
    {
        var aggregate = "permission:role:" + role.ToString(CultureInfo.InvariantCulture);
        var data = new PermissionIdentityData(tenant, "role:" + role.ToString(CultureInfo.InvariantCulture), true, version, []);
        var envelope = IdentityEventContracts.Create(tenant, aggregate, version, IdentityEventContracts.PermissionChanged,
            data, "us", "wp3-page-correlation", "wp3-page-command", runtime.Clock);
        ProbeAssert.Require(runtime.Validator.Validate(envelope).IsValid, "The page fixture must pass the pinned identity event validator.");
        var payload = JsonSerializer.Serialize(data, IdentityEventContracts.Json);
        return new()
        {
            TenantId = tenant, AggregateId = aggregate, EventType = IdentityEventContracts.PermissionChanged,
            Version = version, PayloadJson = payload, PayloadSha256 = IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(payload)),
            UpdatedAtUtc = runtime.Clock.GetUtcNow()
        };
    }

    private static void RequireExactDirectory(string temporaryRoot, string keys, string nonce)
        => ProbeAssert.Require(Path.IsPathFullyQualified(keys) &&
            string.Equals(keys, Path.GetFullPath(Path.Combine(temporaryRoot, "CP6Wp3IdentityPage_" + nonce)), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetDirectoryName(keys), temporaryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
            "Recursive key cleanup must remain in the verified absolute page task nonce directory.");

    private sealed class GenerationBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int paused;
        public CP6Context? Context { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IsolationLevel? Isolation { get; private set; }
        public DbConnection? Connection { get; private set; }
        public void Resume() => resumed.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, Context) ||
                !command.CommandText.TrimStart().StartsWith("SELECT ", StringComparison.Ordinal) ||
                !command.CommandText.Contains("\"CrmIdentityTenantGenerations\"", StringComparison.Ordinal) ||
                !command.CommandText.Contains("\"Generation\"", StringComparison.Ordinal) ||
                Interlocked.CompareExchange(ref paused, 1, 0) != 0)
                return result;
            Isolation = command.Transaction?.IsolationLevel;
            Connection = command.Connection;
            Reached.TrySetResult();
            await resumed.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
