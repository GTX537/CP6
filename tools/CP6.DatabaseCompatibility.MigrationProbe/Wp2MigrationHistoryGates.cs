using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

internal static class Wp2MigrationHistoryGates
{
    private const string InvalidPrefix = "Applied migration history must be an exact prefix of this provider/context migration chain.";

    public static void RequireSupportedPrefix(IReadOnlyList<string> applied, IReadOnlyList<string> available)
    {
        Require(available.Count > 0 && applied.Count <= available.Count
            && applied.SequenceEqual(available.Take(applied.Count), StringComparer.Ordinal), InvalidPrefix);
    }

    public static void RequireExact(IReadOnlyList<string> applied, IReadOnlyList<string> available)
    {
        RequireSupportedPrefix(applied, available);
        Require(applied.SequenceEqual(available, StringComparer.Ordinal), "Applied migration history must exactly match this provider/context migration chain.");
    }

    // The caller has verified the loopback database, task name and owner before any write.
    public static async Task<string> VerifyNegativeControlAsync(CP6Context context, DbConnection connection, bool pg)
    {
        Require(ReferenceEquals(context.Database.GetDbConnection(), connection) && context.Database.CurrentTransaction is null,
            "History negative control must use the verified idle caller connection.");
        var available = context.Database.GetMigrations().ToArray();
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        RequireExact(before, available);
        const string unknown = "20991231235959_Wp2RejectedHistoryIdentity";
        Require(!available.Contains(unknown, StringComparer.Ordinal), "The negative history identity must be unknown to this migration assembly.");
        var profile = DatabaseMigrationProfile.For(new DatabaseOptions(pg ? DatabaseProvider.PostgreSql : DatabaseProvider.SqlServer), DatabaseContextKind.Core);
        var history = pg ? $"\"public\".\"{profile.HistoryTable}\"" : "[dbo].[__EFMigrationsHistory]";
        var id = pg ? "\"MigrationId\"" : "[MigrationId]";
        var version = pg ? "\"ProductVersion\"" : "[ProductVersion]";
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await context.Database.UseTransactionAsync(transaction);
            try
            {
                Require(await connection.ExecuteAsync($"INSERT INTO {history}({id},{version}) VALUES(@identity,@product)",
                    new { identity = unknown, product = "8.0.30" }, transaction) == 1, "History negative control must insert exactly one unknown identity.");
                var corrupted = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
                Require(corrupted.Length == before.Length + 1 && corrupted.Contains(unknown, StringComparer.Ordinal),
                    "The actual Core history API must observe the transaction-only unknown identity.");
                var rejected = false;
                try { RequireSupportedPrefix(corrupted, available); }
                catch (MigrationAssertionException exception) when (exception.Message == InvalidPrefix) { rejected = true; }
                Require(rejected, "The exact pre-migration history guard must reject the actual unknown identity.");
            }
            finally
            {
                await context.Database.UseTransactionAsync(null);
                await transaction.RollbackAsync();
            }
        }
        var after = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(after.SequenceEqual(before, StringComparer.Ordinal), "Rollback must restore every original history identity exactly.");
        RequireExact(after, available);
        return $"Actual Core history rejected one unknown provider/context identity before migration; rollback retained the exact {before.Length} original identities. No migration or business-row write occurred.";
    }

    private static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
}
