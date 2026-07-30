using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;

namespace CP6.Tests.Space;

public sealed class SpaceStandardWarehouseAcceptancePackageTests
{
    private static readonly string PackageDirectory = FindPackageDirectory();

    [Fact]
    public void Manifest_counts_and_generator_identity_match_rebuilt_dataset()
    {
        using var manifest = ReadJson("manifest.json");
        var root = manifest.RootElement;
        var counts = root.GetProperty("counts");
        var generator = root.GetProperty("generator");
        var rebuilt = SpaceStandardWarehouseDatasetGenerator.Generate();

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("1.0.0", root.GetProperty("datasetVersion").GetString());
        Assert.Equal("1.0", root.GetProperty("compatibleSpecVersion").GetString());
        Assert.Equal("Synthetic", root.GetProperty("license").GetString());
        Assert.Equal("Simulated", root.GetProperty("dataSource").GetString());
        Assert.Equal(2, counts.GetProperty("floors").GetInt32());
        Assert.Equal(7, counts.GetProperty("zones").GetInt32());
        Assert.Equal(20, counts.GetProperty("aisles").GetInt32());
        Assert.Equal(500, counts.GetProperty("racks").GetInt32());
        Assert.Equal(10_000, counts.GetProperty("locations").GetInt32());
        Assert.Equal(100, counts.GetProperty("skus").GetInt32());
        Assert.Equal(5_000, counts.GetProperty("stockRecords").GetInt32());
        Assert.Equal(100, counts.GetProperty("pickTasks").GetInt32());
        Assert.Equal(200, counts.GetProperty("pickTaskLines").GetInt32());
        Assert.Equal(6, counts.GetProperty("faultCases").GetInt32());
        Assert.Equal(
            rebuilt.ContentSha256,
            generator.GetProperty("contentSha256").GetString());
        Assert.Equal(
            rebuilt.GeneratorVersion,
            generator.GetProperty("version").GetString());
        Assert.Equal(
            rebuilt.RandomSeed,
            generator.GetProperty("randomSeed").GetString());
        Assert.False(
            generator.GetProperty("usesUnfixedRandomness").GetBoolean());
    }

    [Fact]
    public void Every_manifest_file_exists_and_matches_sha256()
    {
        using var manifest = ReadJson("manifest.json");
        var files = manifest.RootElement.GetProperty("files").EnumerateArray();
        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var relativePath = file.GetProperty("path").GetString()!;
            Assert.DoesNotContain("..", relativePath);
            Assert.True(paths.Add(relativePath), $"Duplicate path: {relativePath}");
            var fullPath = Path.GetFullPath(
                Path.Combine(
                    PackageDirectory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.StartsWith(
                Path.GetFullPath(PackageDirectory) + Path.DirectorySeparatorChar,
                fullPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(fullPath), $"Missing package file: {relativePath}");
            Assert.Equal(
                file.GetProperty("bytes").GetInt64(),
                new FileInfo(fullPath).Length);
            Assert.Equal(
                file.GetProperty("sha256").GetString(),
                ComputeSha256(fullPath));
        }

        Assert.Equal(16, paths.Count);
        Assert.Contains("expected-locations.csv", paths);
        Assert.Contains("wms-seed.json", paths);
        Assert.Contains(
            "fault-cases/missing-location-code.csv",
            paths);
        var actualPackagePaths = Directory
            .EnumerateFiles(
                PackageDirectory,
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path
                .GetRelativePath(PackageDirectory, path)
                .Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var expectedPackagePaths = paths
            .Append("manifest.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedPackagePaths, actualPackagePaths);
    }

    [Fact]
    public void Expected_answers_have_exact_counts_and_unique_location_identity()
    {
        var elementLines = File.ReadLines(
                Path.Combine(PackageDirectory, "expected-elements.jsonl"))
            .ToArray();
        Assert.Equal(2 + 7 + 20 + 500 + 10_000, elementLines.Length);
        Assert.All(
            elementLines,
            line =>
            {
                using var element = JsonDocument.Parse(line);
                Assert.False(string.IsNullOrWhiteSpace(
                    element.RootElement.GetProperty("expectedId").GetString()));
                Assert.True(
                    element.RootElement.GetProperty("sourceRefs")
                        .TryGetProperty("handle", out _));
            });

        var locationLines = File.ReadLines(
                Path.Combine(PackageDirectory, "expected-locations.csv"))
            .ToArray();
        Assert.Equal(10_001, locationLines.Length);
        Assert.Equal(
            "ExpectedId,LogicalId,LocationCode,FloorCode,FloorLevel,ZoneCode,"
            + "ZoneType,AisleCode,RackCode,Column,Level,Depth,Xmm,Ymm,Zmm,"
            + "WidthMm,DepthMm,HeightMm,IsActive,DataSource",
            locationLines[0]);
        var values = locationLines
            .Skip(1)
            .Select(line => line.Split(','))
            .ToArray();
        Assert.Equal(10_000, values.Select(row => row[1]).Distinct().Count());
        Assert.Equal(10_000, values.Select(row => row[2]).Distinct().Count());
        Assert.All(values, row => Assert.Equal("Simulated", row[19]));
    }

    [Fact]
    public void Wms_seed_contains_skus_inventory_and_cross_boundary_tasks()
    {
        using var seed = ReadJson("wms-seed.json");
        var root = seed.RootElement;
        var skus = root.GetProperty("skus").EnumerateArray().ToArray();
        var inventory = root.GetProperty("inventory").EnumerateArray().ToArray();
        var tasks = root.GetProperty("pickTasks").EnumerateArray().ToArray();

        Assert.Equal("Simulated", root.GetProperty("dataSource").GetString());
        Assert.Equal(100, skus.Length);
        Assert.Equal(5_000, inventory.Length);
        Assert.Equal(100, tasks.Length);
        Assert.Equal(
            25,
            tasks.Count(task =>
                task.GetProperty("routeKind").GetString() == "CrossFloor"));
        Assert.Equal(
            25,
            tasks.Count(task =>
                task.GetProperty("routeKind").GetString() == "CrossZone"));
        Assert.All(
            tasks,
            task => Assert.Equal(
                2,
                task.GetProperty("lines").GetArrayLength()));
    }

    [Fact]
    public void Dxf_underlays_and_missing_column_fixture_are_structurally_valid()
    {
        var dxfLines = File.ReadAllLines(
            Path.Combine(PackageDirectory, "warehouse-standard.dxf"));
        Assert.Equal(0, dxfLines.Length % 2);
        Assert.Equal("0", dxfLines[^2]);
        Assert.Equal("EOF", dxfLines[^1]);
        var pairs = Enumerable.Range(0, dxfLines.Length / 2)
            .Select(index => (Code: dxfLines[index * 2], Value: dxfLines[(index * 2) + 1]))
            .ToArray();
        Assert.Equal(509, pairs.Count(pair =>
            pair is ("0", "LWPOLYLINE")));
        Assert.Equal(20, pairs.Count(pair => pair is ("0", "LINE")));
        Assert.Equal(10_000, pairs.Count(pair => pair is ("0", "POINT")));

        AssertPng("floor-1.png");
        AssertPng("floor-2.png");
        Assert.StartsWith(
            "%PDF-1.4",
            Encoding.ASCII.GetString(
                File.ReadAllBytes(
                    Path.Combine(PackageDirectory, "floor-maps.pdf")),
                0,
                8));
        var missingColumnLines = File.ReadAllLines(
            Path.Combine(
                PackageDirectory,
                "fault-cases",
                "missing-location-code.csv"));
        Assert.Equal(2, missingColumnLines.Length);
        Assert.Equal("LogicalId,FloorCode,ZoneCode", missingColumnLines[0]);
        Assert.DoesNotContain("LocationCode", missingColumnLines[0]);
    }

    [Fact]
    public void Dwg_gate_is_explicitly_blocked_without_fake_artifact()
    {
        using var manifest = ReadJson("manifest.json");
        var root = manifest.RootElement;

        Assert.Equal(
            "BlockedByE02S01LicensedConverterDecision",
            root.GetProperty("readiness").GetProperty("dwg").GetString());
        Assert.Contains(
            "warehouse-standard.dwg",
            root.GetProperty("missingRequiredArtifacts")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.False(File.Exists(
            Path.Combine(PackageDirectory, "warehouse-standard.dwg")));
    }

    private static void AssertPng(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(PackageDirectory, relativePath));
        Assert.True(bytes.Length > 24);
        Assert.Equal(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            bytes[..8]);
        Assert.Equal(1_024, ReadBigEndianInt32(bytes, 16));
        Assert.Equal(512, ReadBigEndianInt32(bytes, 20));
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) |
        (bytes[offset + 1] << 16) |
        (bytes[offset + 2] << 8) |
        bytes[offset + 3];

    private static JsonDocument ReadJson(string relativePath) =>
        JsonDocument.Parse(
            File.ReadAllText(Path.Combine(PackageDirectory, relativePath)));

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static string FindPackageDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "CP6.slnx")))
        {
            current = current.Parent;
        }
        if (current is null)
        {
            throw new DirectoryNotFoundException(
                "Could not locate the CP6 repository root.");
        }
        var package = Path.Combine(
            current.FullName,
            "CP6.Tests",
            "TestData",
            "Space",
            "Acceptance",
            "v1.0.0");
        if (!Directory.Exists(package))
            throw new DirectoryNotFoundException(package);
        return package;
    }
}
