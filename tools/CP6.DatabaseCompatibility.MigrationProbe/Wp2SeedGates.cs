using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;

internal static class Wp2SeedGates
{
    private const int OverrideTenant = 987654321;
    private const string CustomGlobal = "WP2-CUSTOM-GLOBAL-JA";
    private const string CustomTenant = "WP2-CUSTOM-TENANT-JA";
    private const string CustomAdmin = "WP2-CUSTOM-ADMIN";

    public static async Task<string> RunAsync(DbConnection connection, bool pg, string mode, string statePath)
    {
        var lang = Table(pg, "Sys_Langs");
        var users = Table(pg, "Sys_Users");
        var key = Quote(pg, "LangKey"); var tenant = Quote(pg, "TenantId");
        var ja = Quote(pg, "Ja"); var username = Quote(pg, "UserName");
        var nickname = Quote(pg, "NickName"); var password = Quote(pg, "Password");
        Require(mode is "prepare" or "capture" or "verify", "Seed gate mode must be prepare, capture or verify.");
        if (mode == "capture")
        {
            Require(!File.Exists(statePath), "Seed evidence state already exists; it must not be overwritten.");
            await using var transaction = await connection.BeginTransactionAsync();
            Require(await connection.QuerySingleAsync<string>($"SELECT {ja} FROM {lang} WHERE {key}=@key AND {tenant} IS NULL",
                new { key = "预算编制" }, transaction) == CustomGlobal, "Upgrade capture must retain the existing global translation fixture.");
            Require(await connection.QuerySingleAsync<string>($"SELECT {ja} FROM {lang} WHERE {key}=@key AND {tenant}=@tenant",
                new { key = "预算编制", tenant = OverrideTenant }, transaction) == CustomTenant, "Upgrade capture must retain the existing tenant translation fixture.");
            Require(await connection.QuerySingleAsync<string>($"SELECT {nickname} FROM {users} WHERE {username}=@admin",
                new { admin = "admin" }, transaction) == CustomAdmin, "Upgrade capture must retain the existing admin fixture.");
            Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {Table(pg, "Sys_Menus")} WHERE {Quote(pg, "MenuId")}=708 AND {Quote(pg, "MenuKey")}=@key AND {Quote(pg, "RoutePath")}=@route",
                new { key = "pur-reconcile", route = "/pur/reconcile" }, transaction) == 1, "Upgrade capture must retain the corrected menu 708 key.");
            var stored = await connection.QuerySingleAsync<string>($"SELECT {password} FROM {users} WHERE {username}=@admin", new { admin = "admin" }, transaction);
            Require(stored.StartsWith("$2", StringComparison.Ordinal), "Upgrade capture requires an existing hashed admin password.");
            var counts = await CountsAsync(connection, pg, transaction);
            var state = new SeedState(connection.Database, pg ? "PostgreSql" : "SqlServer", DateTime.UtcNow, counts, Digest(stored));
            await transaction.RollbackAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            return $"Captured all {counts.Count} post-upgrade table counts and retained existing translation/admin fixtures without modifying business rows; first-initialization acceptance is separate.";
        }
        if (mode == "prepare")
        {
            Require(!File.Exists(statePath), "Seed evidence state already exists; it must not be overwritten.");
            await using var transaction = await connection.BeginTransactionAsync();
            var budgetRows = await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {lang} WHERE {key} IN (@one,@two) AND {tenant} IS NULL",
                new { one = "预算编制", two = "执行分析" }, transaction);
            Require(budgetRows == 2, "Both canonical fresh budget translations must exist exactly once.");
            Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {Table(pg, "Sys_Menus")} WHERE {Quote(pg, "MenuId")}=708 AND {Quote(pg, "MenuKey")}=@key AND {Quote(pg, "RoutePath")}=@route",
                new { key = "pur-reconcile", route = "/pur/reconcile" }, transaction) == 1,
                "First actual initialization must create the purchase reconcile menu with its stable key (BUG #137 original steps).");
            Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {users} WHERE {username}=@admin", new { admin = "admin" }, transaction) == 1,
                "Fresh seed must have exactly one admin account.");
            var storedPassword = await connection.QuerySingleAsync<string>($"SELECT {password} FROM {users} WHERE {username}=@admin", new { admin = "admin" }, transaction);
            Require(storedPassword.StartsWith("$2", StringComparison.Ordinal), "The actual initializer must hash the initial password before exit.");
            Require(await connection.ExecuteAsync($"UPDATE {lang} SET {ja}=@custom WHERE {key}=@key AND {tenant} IS NULL", new { custom = CustomGlobal, key = "预算编制" }, transaction) == 1,
                "Global translation fixture must update one existing seed.");
            Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {lang} WHERE {tenant}=@tenant", new { tenant = OverrideTenant }, transaction) == 0,
                "The isolated override tenant fixture must be unused.");
            var copiedColumns = new[] { "LangKey", "Status", "UpdatedBy", "UpdatedAt", "ZhCN", "ZhTW", "En", "Ko" };
            var columns = string.Join(",", copiedColumns.Select(column => Quote(pg, column)));
            Require(await connection.ExecuteAsync($"INSERT INTO {lang}({tenant},{ja},{columns}) SELECT @tenant,@custom,{columns} FROM {lang} WHERE {key}=@key AND {tenant} IS NULL",
                new { tenant = OverrideTenant, custom = CustomTenant, key = "预算编制" }, transaction) == 1, "Tenant translation fixture must copy one canonical row.");
            Require(await connection.ExecuteAsync($"UPDATE {users} SET {nickname}=@custom WHERE {username}=@admin", new { custom = CustomAdmin, admin = "admin" }, transaction) == 1,
                "Admin fixture must update one account.");
            var counts = await CountsAsync(connection, pg, transaction);
            var state = new SeedState(connection.Database, pg ? "PostgreSql" : "SqlServer", DateTime.UtcNow, counts, Digest(storedPassword));
            await transaction.CommitAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            return $"Captured all {counts.Count} table row counts after actual first initialization; prepared translation and admin retention fixtures; password digest only.";
        }
        var previous = JsonSerializer.Deserialize<SeedState>(await File.ReadAllTextAsync(statePath)) ?? throw new InvalidOperationException("Missing seed state.");
        Require(previous.Database == connection.Database && previous.Provider == (pg ? "PostgreSql" : "SqlServer"), "Seed state must describe this exact owned database and provider.");
        var actual = await CountsAsync(connection, pg);
        var changed = actual.Where(pair => !previous.TableCounts.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key}: {previous.TableCounts.GetValueOrDefault(pair.Key)} -> {pair.Value}").ToArray();
        var auditDetail = "";
        if (changed.Any(change => change.Contains("Sys_FieldAuditLogs", StringComparison.Ordinal)))
        {
            var audit = await connection.QuerySingleAsync<AuditRow>(pg
                ? "SELECT \"EntityName\",\"EntityKey\",\"Operation\",\"Changes\" FROM public.\"Sys_FieldAuditLogs\" ORDER BY \"ChangedAt\" DESC LIMIT 1"
                : "SELECT TOP(1) EntityName,EntityKey,Operation,Changes FROM dbo.Sys_FieldAuditLogs ORDER BY ChangedAt DESC");
            using var parsed = JsonDocument.Parse(audit.Changes);
            var fields = parsed.RootElement.EnumerateArray().Select(item => item.EnumerateObject().FirstOrDefault(property => property.Name.Equals("field", StringComparison.OrdinalIgnoreCase)).Value.GetString());
            auditDetail = $"; latest audit metadata only: {audit.EntityName}, operation={audit.Operation}, fields={string.Join(",", fields)}";
            if (audit.EntityName == "Sys_Menu")
            {
                var menuName = await connection.QuerySingleAsync<string>($"SELECT {Quote(pg, "RoutePath")} FROM {Table(pg, "Sys_Menus")} WHERE {Quote(pg, "MenuId")}=@menu", new { menu = int.Parse(audit.EntityKey) });
                auditDetail += $", menu route={menuName}";
            }
        }
        Require(actual.Count == previous.TableCounts.Count && actual.All(pair => previous.TableCounts.TryGetValue(pair.Key, out var count) && count == pair.Value),
            "Repeated actual initialization must preserve every table count, including exact history counts. Changes: " + string.Join("; ", changed) + auditDetail);
        Require(await connection.QuerySingleAsync<string>($"SELECT {ja} FROM {lang} WHERE {key}=@key AND {tenant} IS NULL", new { key = "预算编制" }) == CustomGlobal,
            "Repeated initialization must retain the manually changed ordinary global translation.");
        Require(await connection.QuerySingleAsync<string>($"SELECT {ja} FROM {lang} WHERE {key}=@key AND {tenant}=@tenant", new { key = "预算编制", tenant = OverrideTenant }) == CustomTenant,
            "Repeated initialization must retain the tenant translation override.");
        Require(await connection.QuerySingleAsync<string>($"SELECT {nickname} FROM {users} WHERE {username}=@admin", new { admin = "admin" }) == CustomAdmin,
            "Repeated initialization must retain the admin's nickname.");
        Require(Digest(await connection.QuerySingleAsync<string>($"SELECT {password} FROM {users} WHERE {username}=@admin", new { admin = "admin" })) == previous.AdminPasswordDigest,
            "Repeated initialization must retain the stored admin password hash exactly.");
        return $"All {actual.Count} actual table counts and migration histories unchanged; global/tenant translations, admin nickname and password retained. Forced canonical label policy is outside this assertion.";
    }

    private static async Task<SortedDictionary<string, long>> CountsAsync(DbConnection connection, bool pg, DbTransaction? transaction = null)
    {
        var tables = await connection.QueryAsync<TableRow>(pg
            ? "SELECT schemaname AS \"Schema\",tablename AS \"Name\" FROM pg_tables WHERE schemaname NOT LIKE 'pg_%' AND schemaname<>'information_schema'"
            : "SELECT s.name AS [Schema],t.name AS [Name] FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0", transaction: transaction);
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
            counts[$"{table.Schema}.{table.Name}"] = await connection.QuerySingleAsync<long>($"SELECT {(pg ? "COUNT(*)" : "COUNT_BIG(*)")} FROM {Quote(pg, table.Schema)}.{Quote(pg, table.Name)}", transaction: transaction);
        return counts;
    }
    private static string Table(bool pg, string name) => $"{Quote(pg, pg ? "public" : "dbo")}.{Quote(pg, name)}";
    private static string Quote(bool pg, string name) => pg ? $"\"{name.Replace("\"", "\"\"")}\"" : $"[{name.Replace("]", "]]")}]";
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
    private sealed record TableRow(string Schema, string Name);
    private sealed record AuditRow(string EntityName, string EntityKey, int Operation, string Changes);
    private sealed record SeedState(string Database, string Provider, DateTime CapturedUtc, SortedDictionary<string, long> TableCounts, string AdminPasswordDigest);
}
