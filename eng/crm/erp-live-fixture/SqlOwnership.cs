using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CP6.ErpLive.Fixture;

internal static partial class ErpLiveFixture
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string OwnershipSchema = "cp6.c03.erp-live-ownership.v1";
    private const string OwnershipProperty = "CP6.C03FixtureOwner";
    private const string FixtureSchema = "cp6.c03.erp-live-fixture.v1";

    private static SqlConnectionStringBuilder MasterConnection()
    {
        var supplied = Environment.GetEnvironmentVariable("CP6_C03_TEST_SQL");
        if (string.IsNullOrWhiteSpace(supplied)) throw new FixtureException("C03_REAL_SQL_INPUT_MISSING");
        SqlConnectionStringBuilder sql;
        try { sql = new(supplied); }
        catch (ArgumentException) { throw new FixtureException("C03_SQL_CONNECTION_INVALID"); }
        ValidateLocalConnection(sql);
        sql.InitialCatalog = "master";
        sql.MultipleActiveResultSets = false;
        sql.Pooling = false;
        sql.TrustServerCertificate = true;
        sql.ConnectTimeout = 15;
        return sql;
    }

    private static void ValidateLocalConnection(SqlConnectionStringBuilder sql)
    {
        var source = sql.DataSource;
        if (source.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) source = source[4..];
        var host = source.Split('\\', ',')[0];
        if (!new[] { "localhost", "127.0.0.1", "::1", "[::1]", ".", "(local)", Environment.MachineName }
                .Contains(host, StringComparer.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(sql.AttachDBFilename) || !string.IsNullOrEmpty(sql.FailoverPartner))
            throw new FixtureException("C03_REQUIRES_EXPLICIT_LOOPBACK_SQL");
    }

    private static string RequirePrivateDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        // Fixture secrets must not be generated beneath a repository, including a Git worktree.
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, ".git")) || Directory.Exists(Path.Combine(current.FullName, ".git")))
                throw new FixtureException("C03_PRIVATE_FIXTURE_MUST_BE_OUTSIDE_REPOSITORIES");
        return full;
    }

    private static async Task<DatabaseOwnership> ReadOwnershipAsync(string directory, CancellationToken ct)
    {
        var root = RequirePrivateDirectory(directory);
        var owner = JsonSerializer.Deserialize<DatabaseOwnership>(
            await File.ReadAllTextAsync(Path.Combine(root, "ownership.json"), ct), Json);
        if (owner is null || owner.SchemaId != OwnershipSchema ||
            !Regex.IsMatch(owner.Database, "\\ACP6C03Live_[a-f0-9]{32}\\z") ||
            Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) != owner.Database ||
            !Regex.IsMatch(owner.OwnerToken, "\\A[a-f0-9]{64}\\z") ||
            owner.TenantIds is not { Length: 2 } || owner.TenantIds.Any(x => x == Guid.Empty) ||
            owner.TenantIds.Distinct().Count() != 2)
            throw new FixtureException("C03_INVALID_DATABASE_OWNERSHIP");
        var sql = MasterConnection();
        if (!string.Equals(owner.DataSource, sql.DataSource, StringComparison.OrdinalIgnoreCase))
            throw new FixtureException("C03_DATABASE_OWNERSHIP_SERVER_MISMATCH");
        return owner;
    }

    private static string OwnedConnection(DatabaseOwnership owner)
    {
        var sql = MasterConnection();
        if (!string.Equals(owner.DataSource, sql.DataSource, StringComparison.OrdinalIgnoreCase))
            throw new FixtureException("C03_DATABASE_OWNERSHIP_SERVER_MISMATCH");
        sql.InitialCatalog = owner.Database;
        return sql.ConnectionString;
    }

    private static async Task VerifyDatabaseMarkerAsync(DatabaseOwnership owner, CancellationToken ct)
    {
        await using var db = new SqlConnection(OwnedConnection(owner));
        var marker = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT CONVERT(nvarchar(128), value) FROM sys.extended_properties WHERE class=0 AND name=@name",
            new { name = OwnershipProperty }, cancellationToken: ct));
        if (marker != owner.OwnerToken) throw new FixtureException("C03_DATABASE_OWNERSHIP_MARKER_MISMATCH");
    }

    private static async Task<(LiveFixtureDocument Document, DatabaseOwnership Ownership)> ReadFixtureAsync(
        string fixturePath, CancellationToken ct)
    {
        var path = Path.GetFullPath(fixturePath);
        if (Path.GetFileName(path) != "live-fixture.json") throw new FixtureException("C03_OWNED_FIXTURE_DOCUMENT_REQUIRED");
        var directory = Path.GetDirectoryName(path)!;
        var owner = await ReadOwnershipAsync(directory, ct);
        var doc = JsonSerializer.Deserialize<LiveFixtureDocument>(await File.ReadAllTextAsync(path, ct), Json);
        if (doc is null || doc.SchemaId != FixtureSchema || doc.Database != owner.Database ||
            !string.Equals(Path.GetFullPath(doc.CoreContentRoot), directory, StringComparison.OrdinalIgnoreCase) ||
            doc.Tenants.Length != 2 || !doc.Tenants.Select(t => t.Id).Order().SequenceEqual(owner.TenantIds.Order()) ||
            doc.Tenants.Select(t => t.AccountId).Distinct().Count() != 2 || doc.Tenants.Any(t => t.AccountId == Guid.Empty))
            throw new FixtureException("C03_FIXTURE_DOCUMENT_OWNERSHIP_MISMATCH");
        await VerifyDatabaseMarkerAsync(owner, ct);
        return (doc, owner);
    }

    private static void ValidateOrigin(string value, bool httpsOnly)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsLoopback ||
            (httpsOnly ? uri.Scheme != "https" : uri.Scheme is not ("http" or "https")) ||
            uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            value.EndsWith('/'))
            throw new FixtureException(httpsOnly ? "C03_LIVE_REQUIRES_HTTPS_LOOPBACK_ORIGIN" : "C03_DAPR_REQUIRES_LOOPBACK_ENDPOINT");
    }

    private static string RandomSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
        => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json), ct);

    public static async Task WritePrivateDiagnosticAsync(string[] args, Exception error)
    {
        try
        {
            string? directory = args switch
            {
                ["initialize", _, var parent] => parent,
                ["dispatch", var config] => Path.GetDirectoryName(Path.GetFullPath(config)),
                ["snapshot", var fixture, _] => Path.GetDirectoryName(Path.GetFullPath(fixture)),
                ["storage-fault", var fixture, _, _] => Path.GetDirectoryName(Path.GetFullPath(fixture)),
                ["replay", var fixture, _, _, _] => Path.GetDirectoryName(Path.GetFullPath(fixture)),
                ["cleanup", var root] => root,
                _ => null
            };
            if (directory is null) return;
            directory = RequirePrivateDirectory(directory);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "c03-fixture-private-error.log"), error.ToString());
            Console.Error.WriteLine("Private diagnostics were written beneath the supplied private fixture directory.");
        }
        catch { /* Preserve the original safe error; diagnostic failure never echoes private input. */ }
    }
}
