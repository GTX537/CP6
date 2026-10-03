using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.DatabaseCompatibility.Testing;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

public static class Wp2SqlUpgradeGates
{
    private const string LegacyEnd = "20260916130000_RetireLegacyCrmModel";
    private const string Upgrade = "20261002151353_CrmIdentityTenantGenerationV1";
    private static readonly string[] ExpectedForwards = [Upgrade,
        "20261002175000_RestoreMissingOrderModelIndexes",
        "20261002184500_RestoreMissingOrderModelForeignKeys",
        "20261002193500_RestoreQuotationAuditColumnCapacity"];
    private const string HistoryTable = "[dbo].[__EFMigrationsHistory]";
    private const string SnapshotTable = "[crm_identity].[Snapshot]";
    private const string GenerationTable = "[dbo].[CrmIdentityTenantGenerations]";
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // The caller verifies the task database owner before invoking this gate.
    // This is a populated, isolated SQL upgrade gate, not a production-data audit.
    public static async Task<string> VerifyAsync(DbConnection connection, CP6Context context)
    {
        Require(connection is SqlConnection && connection.State == ConnectionState.Open && context.Database.IsSqlServer(),
            "Populated upgrade requires an open SQL Server connection and SQL Core context.");
        if (Wp6MigrationOwnership.IsRequested)
            await Wp6MigrationOwnership.VerifyAsync(connection, pg: false, DatabaseFixtureRole.SqlUpgrade);
        else Require(Regex.IsMatch(connection.Database, "\\ACP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}\\z"),
            "Populated upgrade requires the already owner-verified dedicated WP2 database.");
        Require(ReferenceEquals(context.Database.GetDbConnection(), connection), "Upgrade context must use the exact verified connection.");
        Require(context.Database.CurrentTransaction is null && !context.ChangeTracker.HasChanges(),
            "Upgrade must start without an EF transaction or unsaved caller changes.");

        var appliedBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var available = context.Database.GetMigrations().ToArray();
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        Require(appliedBefore.Length == 136 && appliedBefore[^1] == LegacyEnd,
            "Expected SQL Core history 136 with RetireLegacyCrmModel last before the upgrade.");
        var finalCount = 136 + ExpectedForwards.Length;
        Require(available.Length == finalCount && available.Take(136).SequenceEqual(appliedBefore)
            && pending.SequenceEqual(ExpectedForwards), "Only the exact generation/index/FK/quotation-capacity forward set may be pending after the supported legacy Core136 chain.");
        Require(!await GenerationExistsAsync(connection), "Generation table must not exist before the real forward migration.");
        Require(await connection.QuerySingleAsync<int>(Command("SELECT CASE WHEN OBJECT_ID(N'crm_identity.trg_CrmIdentitySnapshot_TenantGeneration') IS NULL THEN 0 ELSE 1 END")) == 0,
            "The new generation trigger must not already exist on the legacy database.");

        var originalCounts = await CountsAsync(connection);
        Require(originalCounts.TryGetValue(HistoryTable, out var historyCount) && historyCount == 136,
            "The physical canonical SQL Core history must contain exactly 136 rows.");
        var historyRowsBefore = (await connection.QueryAsync<HistoryRow>(Command($"SELECT [MigrationId],[ProductVersion] FROM {HistoryTable} ORDER BY [MigrationId]"))).ToArray();
        Require(historyRowsBefore.Select(row => row.MigrationId).SequenceEqual(appliedBefore),
            "The verified Core context must read the canonical physical history.");
        var original = await CaptureAsync(connection);
        Require(original.LanguageCount > 0 && original.AdminCount == 1,
            "Upgrade input must be a populated seeded database with language rows and exactly one admin.");
        var originalTenants = original.Snapshots.Select(row => row.TenantId).Distinct().Count();
        var tenant = Guid.NewGuid();
        var aggregates = new[] { "user:" + Guid.NewGuid().ToString("D"), "user:" + Guid.NewGuid().ToString("D") };
        var fixtureAttempted = false;
        var upgradeApplied = false;
        string? result = null;
        try
        {
            Require(await connection.QuerySingleAsync<int>(Command($"SELECT COUNT(*) FROM {SnapshotTable} WHERE [TenantId]=@Tenant", new { Tenant = tenant })) == 0,
                "The independent upgrade fixture tenant must be unused.");
            fixtureAttempted = true;
            await using (var setup = await connection.BeginTransactionAsync())
            {
                for (var index = 0; index < aggregates.Length; index++)
                {
                    var version = index == 0 ? 7 : 11;
                    var deleted = index == 1;
                    var payload = Payload(tenant, aggregates[index], version, deleted, "before-upgrade 升级 Ω 😀");
                    Require(await connection.ExecuteAsync(Command($"""
                        INSERT INTO {SnapshotTable} ([TenantId],[AggregateId],[EventType],[Version],[PayloadJson],
                            [PayloadSha256],[IsDeleted],[UpdatedAtUtc])
                        VALUES (@Tenant,@Aggregate,@EventType,@Version,@Payload,@PayloadHash,@Deleted,@Updated)
                        """, new { Tenant = tenant, Aggregate = aggregates[index], EventType = IdentityEventContracts.UserChanged,
                            Version = version, Payload = payload, PayloadHash = Hash(payload), Deleted = deleted,
                            Updated = new DateTimeOffset(2026, 10, 2, 12, 0, index, TimeSpan.Zero) }, setup)) == 1,
                        "Upgrade setup must insert each actual Snapshot fixture exactly once.");
                }
                await setup.CommitAsync();
            }

            var beforeMigration = await CaptureAsync(connection);
            var fixtureCounts = await CountsAsync(connection);
            Require(beforeMigration.Snapshots.Length == original.Snapshots.Length + 2,
                "Baseline must include exactly the two independently populated Snapshot rows.");
            AssertSnapshots(original.Snapshots, beforeMigration.Snapshots.Where(row => row.TenantId != tenant).ToArray(),
                "Fixture setup must preserve every pre-existing Snapshot field and token.");
            AssertCriticalDigests(original, beforeMigration, "Fixture setup");
            AssertCounts(originalCounts, fixtureCounts, snapshotDelta: 2);
            foreach (var aggregate in aggregates)
            {
                var row = beforeMigration.Snapshots.Single(value => value.TenantId == tenant && value.AggregateId == aggregate);
                Require(row.RowVersion.Length == 8 && row.RowVersion.Any(value => value != 0),
                    "The legacy SQL provider must generate an actual nonempty eight-byte Snapshot token.");
                Require(row.PayloadSha256 == Hash(row.PayloadJson), "Fixture payload hash must describe its exact stored UTF-8 JSON.");
            }

            await context.Database.MigrateAsync();
            var appliedAfter = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Require(appliedAfter.Length == finalCount && appliedAfter.Take(136).SequenceEqual(appliedBefore)
                && appliedAfter.Skip(136).SequenceEqual(ExpectedForwards) && !(await context.Database.GetPendingMigrationsAsync()).Any(),
                "The real upgrade must append exactly the four known migrations and leave no pending migration.");
            upgradeApplied = true;
            var historyRowsAfter = (await connection.QueryAsync<HistoryRow>(Command($"SELECT [MigrationId],[ProductVersion] FROM {HistoryTable} ORDER BY [MigrationId]"))).ToArray();
            Require(historyRowsAfter.Length == finalCount && historyRowsAfter.Take(136).SequenceEqual(historyRowsBefore)
                && historyRowsAfter.Skip(136).Select(row => row.MigrationId).SequenceEqual(ExpectedForwards),
                "The physical history must append only the known forward set and preserve every original ID/ProductVersion pair.");
            Require(await GenerationExistsAsync(connection), "The real upgrade must create the generation table.");
            Require(await connection.QuerySingleAsync<int>(Command("""
                SELECT COUNT(*) FROM sys.triggers
                WHERE object_id=OBJECT_ID(N'crm_identity.trg_CrmIdentitySnapshot_TenantGeneration')
                  AND parent_id=OBJECT_ID(N'crm_identity.Snapshot') AND is_disabled=0
                """)) == 1, "The real migration must install the enabled Snapshot generation trigger.");

            var afterMigration = await CaptureAsync(connection);
            AssertSnapshots(beforeMigration.Snapshots, afterMigration.Snapshots,
                "Migration must retain exact Snapshot payload, version, deletion state, timestamp and every eight-byte token.");
            AssertCriticalDigests(beforeMigration, afterMigration, "Migration");
            var seeded = await GenerationsAsync(connection);
            var tenants = beforeMigration.Snapshots.Select(row => row.TenantId).Distinct().ToHashSet();
            Require(seeded.Count == tenants.Count && seeded.Keys.ToHashSet().SetEquals(tenants)
                && seeded.Values.All(value => value == 1) && seeded[tenant] == 1,
                "Migration must seed each actual Snapshot tenant once at generation one, including the tenant with two fixture rows.");
            var upgradedCounts = await CountsAsync(connection);
            AssertCounts(fixtureCounts, upgradedCounts, historyDelta: ExpectedForwards.Length, generationCount: tenants.Count);

            var afterCommittedUpdate = await VerifyCommittedUpdateAsync(connection, context, tenant, aggregates[0], beforeMigration);
            AssertCriticalDigests(afterMigration, afterCommittedUpdate, "Committed fixture update");
            AssertCounts(upgradedCounts, await CountsAsync(connection), generationCount: tenants.Count);
            await VerifyRollbackAsync(connection, context, tenant, aggregates[1], afterCommittedUpdate);
            AssertCriticalDigests(afterMigration, await CaptureAsync(connection), "Rolled-back fixture update");
            AssertCounts(upgradedCounts, await CountsAsync(connection), generationCount: tenants.Count);

            result = $"SQL Core 136 -> {finalCount} appended only {string.Join(", ", ExpectedForwards)}; all {originalCounts.Count} pre-existing table counts retained (history +{ExpectedForwards.Length}; Snapshot fixture +2). "
                + $"All {beforeMigration.Snapshots.Length} captured Snapshot rows retained exact fields and eight-byte tokens through migration. "
                + $"All {beforeMigration.LanguageCount} language rows and the admin password/metadata digests retained. "
                + $"Tenant generations seeded once at 1; actual EF Snapshot save advanced 1 -> 2 and returned a new token; a second EF save reached 3 inside its transaction, then rollback restored generation 2 and exact Snapshot bytes. "
                + $"Migration-input Snapshot SHA256={beforeMigration.SnapshotDigest}; language SHA256={beforeMigration.LanguageDigest}; admin SHA256={beforeMigration.AdminDigest}.";
        }
        finally
        {
            if (fixtureAttempted)
            {
                Require(context.Database.CurrentTransaction is null, "Fixture transactions must be disposed before upgrade cleanup.");
                await using (var cleanup = await connection.BeginTransactionAsync())
                {
                    await connection.ExecuteAsync(Command($"DELETE FROM {SnapshotTable} WHERE [TenantId]=@Tenant AND [AggregateId] IN @Aggregates",
                        new { Tenant = tenant, Aggregates = aggregates }, cleanup));
                    if (await GenerationExistsAsync(connection, cleanup))
                        await connection.ExecuteAsync(Command($"""
                            DELETE FROM {GenerationTable} WHERE [TenantId]=@Tenant
                            AND NOT EXISTS(SELECT 1 FROM {SnapshotTable} WHERE [TenantId]=@Tenant)
                            """, new { Tenant = tenant }, cleanup));
                    await cleanup.CommitAsync();
                }
                Require(await connection.QuerySingleAsync<int>(Command($"SELECT COUNT(*) FROM {SnapshotTable} WHERE [TenantId]=@Tenant AND [AggregateId] IN @Aggregates",
                    new { Tenant = tenant, Aggregates = aggregates })) == 0, "Exact upgrade Snapshot fixtures must be absent after cleanup.");
                if (await GenerationExistsAsync(connection))
                    Require(await connection.QuerySingleAsync<int>(Command($"SELECT COUNT(*) FROM {GenerationTable} WHERE [TenantId]=@Tenant", new { Tenant = tenant })) == 0,
                        "The independent fixture generation must be absent after cleanup; other tenants are never deleted.");
                if (upgradeApplied)
                {
                    var final = await CaptureAsync(connection);
                    AssertSnapshots(original.Snapshots, final.Snapshots, "Upgrade cleanup must leave all original Snapshot rows exact.");
                    AssertCriticalDigests(original, final, "Upgrade cleanup");
                    AssertCounts(originalCounts, await CountsAsync(connection), historyDelta: ExpectedForwards.Length, generationCount: originalTenants);
                    AssertGenerations(original.Snapshots.Select(row => row.TenantId).Distinct().ToDictionary(value => value, _ => 1L),
                        await GenerationsAsync(connection), "Cleanup must retain the seeded generation of every original Snapshot tenant.");
                }
            }
        }
        return result! + " Exact temporary Snapshot and tenant-generation fixtures removed; final original data/counts rechecked. Checks cover this captured task database and these critical fields, not all business payloads or production upgrade acceptance.";
    }

    private static async Task<Capture> VerifyCommittedUpdateAsync(DbConnection connection, CP6Context context,
        Guid tenant, string aggregate, Capture before)
    {
        var old = before.Snapshots.Single(row => row.TenantId == tenant && row.AggregateId == aggregate);
        var expectedGenerations = before.Snapshots.Select(row => row.TenantId).Distinct()
            .ToDictionary(value => value, value => value == tenant ? 2L : 1L);
        var fixture = await context.CrmIdentitySnapshots.IgnoreQueryFilters().SingleAsync(row => row.TenantId == tenant && row.AggregateId == aggregate);
        try
        {
            await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                fixture.Version = old.Version + 1;
                fixture.PayloadJson = Payload(tenant, aggregate, fixture.Version, old.IsDeleted, "committed-upgrade-update 更新 Ω 😀");
                fixture.PayloadSha256 = Hash(fixture.PayloadJson);
                fixture.UpdatedAtUtc = old.UpdatedAtUtc.AddSeconds(1);
                Require(await context.SaveChangesAsync() == 1, "Actual post-migration EF update must save exactly one Snapshot.");
                var inside = await SnapshotsAsync(connection, transaction.GetDbTransaction());
                var saved = inside.Single(row => row.TenantId == tenant && row.AggregateId == aggregate);
                var returnedToken = fixture.RowVersion;
                Require(saved.Version == old.Version + 1 && saved.PayloadJson == fixture.PayloadJson
                    && saved.PayloadSha256 == Hash(saved.PayloadJson) && saved.RowVersion.Length == 8
                    && !saved.RowVersion.SequenceEqual(old.RowVersion)
                    && returnedToken is { Length: 8 } && returnedToken.SequenceEqual(saved.RowVersion),
                    "EF must persist the changed Snapshot and materialize its new eight-byte database-generated token with the trigger installed.");
                AssertGenerations(expectedGenerations, await GenerationsAsync(connection, transaction.GetDbTransaction()),
                    "The actual Snapshot update must advance only its own tenant generation in the same transaction.");
                await transaction.CommitAsync();
            }
            var after = await CaptureAsync(connection);
            AssertSnapshots(before.Snapshots.Where(row => row.TenantId != tenant || row.AggregateId != aggregate).ToArray(),
                after.Snapshots.Where(row => row.TenantId != tenant || row.AggregateId != aggregate).ToArray(),
                "A committed fixture update must not alter other Snapshot rows or their tokens.");
            AssertGenerations(expectedGenerations, await GenerationsAsync(connection),
                "Committed Snapshot update must retain generation two and preserve all other tenants at one.");
            return after;
        }
        finally { context.Entry(fixture).State = EntityState.Detached; }
    }

    private static async Task VerifyRollbackAsync(DbConnection connection, CP6Context context,
        Guid tenant, string aggregate, Capture before)
    {
        var old = before.Snapshots.Single(row => row.TenantId == tenant && row.AggregateId == aggregate);
        var generationsBefore = await GenerationsAsync(connection);
        var fixture = await context.CrmIdentitySnapshots.IgnoreQueryFilters().SingleAsync(row => row.TenantId == tenant && row.AggregateId == aggregate);
        try
        {
            await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                fixture.Version = old.Version + 1;
                fixture.PayloadJson = Payload(tenant, aggregate, fixture.Version, old.IsDeleted, "must-rollback 回滚 Ω 😀");
                fixture.PayloadSha256 = Hash(fixture.PayloadJson);
                fixture.UpdatedAtUtc = old.UpdatedAtUtc.AddSeconds(2);
                Require(await context.SaveChangesAsync() == 1, "Rollback control must really execute one EF Snapshot update.");
                var inside = (await SnapshotsAsync(connection, transaction.GetDbTransaction()))
                    .Single(row => row.TenantId == tenant && row.AggregateId == aggregate);
                Require(inside.Version == old.Version + 1 && inside.PayloadJson == fixture.PayloadJson
                    && inside.RowVersion.Length == 8 && !inside.RowVersion.SequenceEqual(old.RowVersion)
                    && (await GenerationsAsync(connection, transaction.GetDbTransaction()))[tenant] == 3,
                    "Rollback control must observe the changed payload/token and generation three before rollback.");
                await transaction.RollbackAsync();
            }
            AssertSnapshots(before.Snapshots, (await CaptureAsync(connection)).Snapshots,
                "Rollback must restore all Snapshot fields and exact original token bytes.");
            var generationsAfter = await GenerationsAsync(connection);
            AssertGenerations(generationsBefore, generationsAfter,
                "Rollback must restore every tenant generation, including the fixture tenant at two.");
        }
        finally { context.Entry(fixture).State = EntityState.Detached; }
    }

    private static async Task<Capture> CaptureAsync(DbConnection connection)
    {
        var snapshots = await SnapshotsAsync(connection);
        var languages = (await connection.QueryAsync<LanguageRow>(Command("""
            SELECT [Id],[TenantId],[LangKey],[Status],[UpdatedBy],[UpdatedAt],[ZhCN],[ZhTW],[En],[Ja],[Ko]
            FROM [dbo].[Sys_Langs] ORDER BY [Id]
            """))).ToArray();
        var admins = (await connection.QueryAsync<AdminRow>(Command("""
            SELECT [Id],[TenantId],[UserName],[Password],[NickName],[RoleId],[Enable],[AuthenticationEpoch],
                [PasswordChangedAt],[MustChangePassword],[TwoFactorEnabled],[IsPlatformAdmin]
            FROM [dbo].[Sys_Users] WHERE [UserName]=@Admin ORDER BY [Id]
            """, new { Admin = "admin" }))).ToArray();
        Require(admins.Length == 1 && admins[0].Password.StartsWith("$2", StringComparison.Ordinal),
            "Populated upgrade requires the actual seeded admin password hash; its value is never reported.");
        var adminDigest = Hash(JsonSerializer.Serialize(admins.Select(row => new
        {
            row.Id, row.TenantId, row.UserName, row.NickName, row.RoleId, row.Enable, row.AuthenticationEpoch,
            row.PasswordChangedAt, row.MustChangePassword, row.TwoFactorEnabled, row.IsPlatformAdmin,
            PasswordDigest = Hash(row.Password)
        })));
        return new Capture(snapshots, languages.Length, admins.Length, Hash(JsonSerializer.Serialize(snapshots)),
            Hash(JsonSerializer.Serialize(languages)), adminDigest);
    }

    private static async Task<SnapshotRow[]> SnapshotsAsync(DbConnection connection, DbTransaction? transaction = null)
    {
        var rows = (await connection.QueryAsync<SnapshotRow>(Command($"""
            SELECT [TenantId],[AggregateId],[EventType],[Version],[PayloadJson],[PayloadSha256],
                [IsDeleted],[UpdatedAtUtc],[RowVersion] FROM {SnapshotTable}
            """, transaction: transaction))).OrderBy(row => row.TenantId).ThenBy(row => row.AggregateId, StringComparer.Ordinal).ToArray();
        Require(rows.All(row => row.RowVersion is { Length: 8 }), "Every captured actual SQL Snapshot token must be eight bytes.");
        return rows;
    }

    private static void AssertSnapshots(IReadOnlyCollection<SnapshotRow> expected, IReadOnlyCollection<SnapshotRow> actual, string detail)
    {
        var byKey = actual.ToDictionary(row => (row.TenantId, row.AggregateId));
        Require(expected.Count == byKey.Count && expected.All(row => byKey.TryGetValue((row.TenantId, row.AggregateId), out var value)
            && row.EventType == value.EventType && row.Version == value.Version && row.PayloadJson == value.PayloadJson
            && row.PayloadSha256 == value.PayloadSha256 && row.IsDeleted == value.IsDeleted
            && row.UpdatedAtUtc.EqualsExact(value.UpdatedAtUtc) && row.RowVersion.SequenceEqual(value.RowVersion)), detail);
    }

    private static void AssertCriticalDigests(Capture expected, Capture actual, string operation)
        => Require(expected.LanguageCount == actual.LanguageCount && expected.LanguageDigest == actual.LanguageDigest
            && expected.AdminCount == actual.AdminCount && expected.AdminDigest == actual.AdminDigest,
            $"{operation} must preserve all captured language fields and admin password/metadata digests.");

    private static async Task<SortedDictionary<string, long>> CountsAsync(DbConnection connection)
    {
        var tables = await connection.QueryAsync<TableRow>(Command("""
            SELECT s.name AS [Schema],t.name AS [Name] FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0
            """));
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var name = Quote(table.Schema) + "." + Quote(table.Name);
            counts.Add(name, await connection.QuerySingleAsync<long>(Command($"SELECT COUNT_BIG(*) FROM {name}")));
        }
        return counts;
    }

    private static void AssertCounts(IReadOnlyDictionary<string, long> expected, IReadOnlyDictionary<string, long> actual,
        long snapshotDelta = 0, long historyDelta = 0, long? generationCount = null)
    {
        var required = expected.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (snapshotDelta != 0) required[SnapshotTable] += snapshotDelta;
        if (historyDelta != 0) required[HistoryTable] += historyDelta;
        if (generationCount is { } generation) required[GenerationTable] = generation;
        var changed = required.Where(pair => !actual.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => pair.Key).Concat(actual.Keys.Where(key => !required.ContainsKey(key))).ToArray();
        Require(required.Count == actual.Count && changed.Length == 0,
            "Upgrade must preserve the complete table set/counts except its explicit fixture/history/generation deltas. Changed tables: " + string.Join(",", changed));
    }

    private static async Task<Dictionary<Guid, long>> GenerationsAsync(DbConnection connection, DbTransaction? transaction = null)
        => (await connection.QueryAsync<GenerationRow>(Command($"SELECT [TenantId],[Generation] FROM {GenerationTable}", transaction: transaction)))
            .ToDictionary(row => row.TenantId, row => row.Generation);
    private static void AssertGenerations(IReadOnlyDictionary<Guid, long> expected, IReadOnlyDictionary<Guid, long> actual, string detail)
        => Require(expected.Count == actual.Count
            && expected.All(pair => actual.TryGetValue(pair.Key, out var value) && value == pair.Value), detail);
    private static async Task<bool> GenerationExistsAsync(DbConnection connection, DbTransaction? transaction = null)
        => await connection.QuerySingleAsync<int>(Command("SELECT CASE WHEN OBJECT_ID(N'dbo.CrmIdentityTenantGenerations') IS NULL THEN 0 ELSE 1 END", transaction: transaction)) == 1;
    private static string Payload(Guid tenant, string aggregate, int version, bool deleted, string phase)
        => JsonSerializer.Serialize(new { tenantId = tenant, aggregateId = aggregate, version, deleted, phase }, PayloadOptions);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Quote(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    private static CommandDefinition Command(string sql, object? args = null, DbTransaction? transaction = null)
        => new(sql, args, transaction, commandTimeout: 30);
    private static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
    private sealed record Capture(SnapshotRow[] Snapshots, int LanguageCount, int AdminCount, string SnapshotDigest, string LanguageDigest, string AdminDigest);
    private sealed record SnapshotRow(Guid TenantId, string AggregateId, string EventType, int Version, string PayloadJson,
        string PayloadSha256, bool IsDeleted, DateTimeOffset UpdatedAtUtc, byte[] RowVersion);
    private sealed record LanguageRow(int Id, int? TenantId, string LangKey, string Status, string? UpdatedBy, DateTime? UpdatedAt,
        string? ZhCN, string? ZhTW, string? En, string? Ja, string? Ko);
    private sealed record AdminRow(Guid Id, Guid TenantId, string UserName, string Password, string? NickName, int? RoleId,
        bool Enable, Guid AuthenticationEpoch, DateTime? PasswordChangedAt, bool MustChangePassword, bool TwoFactorEnabled, bool IsPlatformAdmin);
    private sealed record GenerationRow(Guid TenantId, long Generation);
    private sealed record HistoryRow(string MigrationId, string ProductVersion);
    private sealed record TableRow(string Schema, string Name);
}
