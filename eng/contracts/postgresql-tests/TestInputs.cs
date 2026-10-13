using System.Text.Json;
using System.Security.Cryptography;
using CP6.Persistence.PostgreSql.Contracts;

namespace CP6.ExactValues.PostgreSqlTests;

internal static class TestInputs
{
    internal static string Repository
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            throw new InvalidOperationException("REPOSITORY_NOT_FOUND");
        }
    }

    internal static JsonElement Read(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository, relativePath)));
        return document.RootElement.Clone();
    }

    internal static readonly IReadOnlyDictionary<string, JsonElement> Vectors = LoadVectors();
    internal static readonly JsonElement Mapping = Read(MappingPath);
    // A draft may be experimentally verified before approval. It never writes the accepted s01 input file.
    // Lifecycle/evidence records distinguish the exact draft identity from adopted mapping identity.
    internal static string MappingPath => File.Exists(Path.Combine(Repository, "eng/contracts/s01/postgresql-mappings.json"))
        ? "eng/contracts/s01/postgresql-mappings.json" : "docs/development/evidence/S01-10/postgresql-mappings.proposed.json";
    internal static InventoryMapping[] Inventory => Mapping.GetProperty("inventory_mappings").EnumerateArray().Select(x => new InventoryMapping(x)).ToArray();

    private static IReadOnlyDictionary<string, JsonElement> LoadVectors()
    {
        VerifyPinnedFile("eng/contracts/s01/vectors.json", "b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01");
        VerifyPinnedFile("eng/contracts/s01/profile-inputs.json", "2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e");
        var admission = Read("docs/development/evidence/wave4-dispatch-20261013/S01-10-admission.json");
        var expected = admission.GetProperty("mapped_vectors").EnumerateArray().ToArray();
        if (expected.Length != 56 || expected.Select(x => x.GetProperty("id").GetString()).Distinct().Count() != 56)
            throw new InvalidOperationException("ASSIGNED_VECTOR_SET_CHANGED");
        var package = Read("eng/contracts/s01/vectors.json");
        if (package.GetProperty("package_version").GetString() != "CP6-S01-CONTRACT-INPUTS/1.0.0") throw new InvalidOperationException("PACKAGE_VERSION_CHANGED");
        var all = package.GetProperty("cases").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in expected)
        {
            var id = item.GetProperty("id").GetString()!;
            var vector = all[id];
            if (vector.GetProperty("operation").GetString() != "lexical") throw new InvalidOperationException("UNKNOWN_VECTOR_OPERATION:" + id);
            if (item.GetProperty("profile_id").GetString() != vector.GetProperty("profile_id").GetString()) throw new InvalidOperationException("ASSIGNED_PROFILE_MISMATCH:" + id);
            result.Add(id, vector);
        }
        var groups = result.Values.GroupBy(x => x.GetProperty("profile_id").GetString()!.Split('-')[1]).ToDictionary(x => x.Key, x => x.Count());
        if (groups["credit"] != 25 || groups["inv"] != 9 || groups["stableid"] != 22) throw new InvalidOperationException("VECTOR_COUNTS_CHANGED");
        return result;
    }

    private static void VerifyPinnedFile(string path, string expected)
    {
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Repository, path)))).ToLowerInvariant();
        if (actual != expected) throw new InvalidOperationException("PINNED_INPUT_BYTES_CHANGED:" + path);
    }
}

internal sealed class InventoryMapping(JsonElement json)
{
    internal string Table => json.GetProperty("table").GetString()!;
    internal string Column => json.GetProperty("logical_column").GetString()!;
    internal string Key => Table + "." + Column;
    internal string Digits => json.GetProperty("digits_column").GetString()!;
    internal string Scale => json.GetProperty("scale_column").GetString()!;
    internal string Wire => json.GetProperty("wire_column").GetString()!;
    internal bool Nullable => json.GetProperty("nullable").GetBoolean();
    internal int MaximumScale => json.GetProperty("storage_scale_range")[1].GetInt32();
    internal int OriginalMaximumScale => json.GetProperty("source_scale_range")[1].GetInt32();
    internal InventoryStorageProfile Profile => Enum.Parse<InventoryStorageProfile>(json.GetProperty("profile").GetString()!, ignoreCase: false);
    internal InventoryNumericRule Rule => Enum.Parse<InventoryNumericRule>(json.GetProperty("numeric_rule").GetString()!, ignoreCase: false);
}
