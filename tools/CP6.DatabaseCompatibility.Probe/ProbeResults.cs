using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.DatabaseCompatibility.Probe;

internal sealed record ProbeCheck(string Name, string Status, string Detail);

internal sealed class ProbeReport
{
    public string Scope { get; } = "DB-COMPAT-01 WP1 limited-table experiment; not full migrations or production acceptance";
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    public string? SourceSha { get; init; }
    public string SourceState { get; init; } = "SourceSha identifies base HEAD; experiment input includes the uncommitted task working tree.";
    public string? InputFingerprintSha256 { get; private set; }
    public string Provider { get; init; } = "SelfTest";
    public string? DatabaseVersion { get; set; }
    public string? Schema { get; set; }
    public string? OwnershipId { get; set; }
    public Dictionary<string, string> Dependencies { get; } = new();
    public Dictionary<string, string> ResolvedDirectPackages { get; } = new();
    public Dictionary<string, string> SourceInputSha256 { get; } = new();
    public Dictionary<string, string> RuntimeAssemblySha256 { get; } = new();
    public List<ProbeCheck> Checks { get; } = new();

    public void Add(string name, string status, string detail)
    {
        Checks.Add(new(name, status, detail));
        Console.WriteLine($"{status}: {name}: {detail}");
    }

    public async Task CheckAsync(string name, Func<Task<string>> action)
    {
        try { Add(name, "Passed", await action()); }
        catch (Exception exception) { Add(name, "Failed", SafeErrors.Describe(exception)); }
    }

    public void RecordDependencies()
    {
        foreach (var type in new[]
        {
            typeof(Microsoft.EntityFrameworkCore.DbContext), typeof(Npgsql.NpgsqlConnection),
            typeof(Microsoft.Data.SqlClient.SqlConnection), typeof(Dapper.SqlMapper),
            typeof(CP6.Platform.EntityFramework.Cp6OutboxMessage),
            typeof(CP6.Core.EFDbContext.CP6Context), typeof(CP6.Space.Infrastructure.SpaceContext), typeof(ProbeReport),
            typeof(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder)
        })
        {
            var assembly = type.Assembly;
            Dependencies[assembly.GetName().Name!] =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString() ?? "unknown";
            RuntimeAssemblySha256[assembly.GetName().Name!] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "CP6.Core"))) root = root.Parent;
        if (root is null) return;
        var probe = Path.Combine(root.FullName, "tools", "CP6.DatabaseCompatibility.Probe");
        var files = Directory.EnumerateFiles(probe, "*", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file) == "packages.lock.json")
            .Concat(Directory.EnumerateFiles(Path.Combine(root.FullName, "CP6.Core", "Persistence"), "*.cs"))
            .Concat(new[] { "CP6.Core/EFDbContext/CP6Context.cs", "CP6.Core/Services/CrmIdentity/IdentityMessagingContext.cs", "CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs", "CP6.Space.Infrastructure/SpaceContext.cs", "CP6.Core/CP6.Core.csproj", "CP6.Core/packages.lock.json", "CP6.Space.Infrastructure/CP6.Space.Infrastructure.csproj", "CP6.Space.Infrastructure/packages.lock.json" }.Select(file => Path.Combine(root.FullName, file)))
            .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
            SourceInputSha256[Path.GetRelativePath(root.FullName, file).Replace('\\', '/')] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        var lockPath = Path.Combine(probe, "packages.lock.json");
        if (File.Exists(lockPath))
        {
            using var packageLock = JsonDocument.Parse(File.ReadAllText(lockPath));
            foreach (var package in packageLock.RootElement.GetProperty("dependencies").GetProperty("net8.0").EnumerateObject())
                if (package.Value.GetProperty("type").GetString() == "Direct")
                    ResolvedDirectPackages[package.Name] = package.Value.GetProperty("resolved").GetString()!;
        }
        var inputs = SourceInputSha256.Concat(RuntimeAssemblySha256).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}\n");
        InputFingerprintSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(inputs))));
    }
}

internal static class Expect
{
    public static void True(bool condition, string safeDetail)
    {
        if (!condition) throw new ProbeAssertionException(safeDetail);
    }

    public static void Token(byte[] token)
    {
        True(token.Length == 8, "Concurrent token must contain exactly 8 bytes.");
        var encoded = Convert.ToBase64String(token);
        True(encoded.Length == 12 && encoded.EndsWith('='), "8-byte Base64 representation must retain its 12-character contract.");
        True(Convert.FromBase64String(encoded).SequenceEqual(token), "Base64 must round-trip the opaque bytes exactly.");
    }

    public static void Changed(byte[] before, byte[] after)
    {
        Token(before);
        Token(after);
        True(!before.SequenceEqual(after), "A successful database write must replace the token.");
    }
}

internal sealed class ProbeAssertionException(string detail) : Exception(detail) { }

internal static class SafeErrors
{
    // Database exception messages may contain endpoints, database names, SQL or credential material.
    public static string Describe(Exception exception) => exception switch
    {
        ProbeAssertionException assertion => assertion.Message,
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => "DbUpdateConcurrencyException",
        Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: not null } update =>
            $"DbUpdateException / {Describe(update.InnerException!)}",
        Npgsql.PostgresException postgres => $"PostgresException SQLSTATE={postgres.SqlState}",
        Microsoft.Data.SqlClient.SqlException sql => $"SqlException Number={sql.Number}",
        _ => exception.GetType().Name
    };
}
