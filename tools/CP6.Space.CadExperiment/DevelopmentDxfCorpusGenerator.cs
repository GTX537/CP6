using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CP6.Space.CadExperiment;

public sealed record DevelopmentCadCorpusReport(
    int SchemaVersion,
    string GeneratorVersion,
    string OutputDirectory,
    string ManifestPath,
    int SampleCount,
    long TotalSizeBytes,
    IReadOnlyList<string> CadVersions,
    IReadOnlyDictionary<string, int> LayoutFamilies);

public static class DevelopmentDxfCorpusGenerator
{
    public const string GeneratorVersion = "1.0.0";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions JsonLineOptions = new(CadExperimentJson.Options)
    {
        WriteIndented = false
    };

    private static readonly SamplePlan[] Plans =
    [
        new("L1-DEV-001", "L1-RegularRectangular", "01-regular-small-racks.dxf", "AC1009", "Small regular warehouse", 1),
        new("L1-DEV-002", "L1-RegularRectangular", "02-regular-wide-docks.dxf", "AC1015", "Wide warehouse with loading docks", 2),
        new("L1-DEV-003", "L1-RegularRectangular", "03-regular-compact-storage.dxf", "AC1027", "Compact dense storage", 3),
        new("L1-DEV-004", "L1-RegularRectangular", "04-regular-cross-aisle.dxf", "AC1032", "Regular warehouse with cross aisle", 4),
        new("L2-DEV-001", "L2-MultiFloor", "05-two-floor-overlap.dxf", "AC1015", "Two floors sharing coordinates", 1),
        new("L2-DEV-002", "L2-MultiFloor", "06-mezzanine-storage.dxf", "AC1021", "Warehouse with storage mezzanine", 2),
        new("L2-DEV-003", "L2-MultiFloor", "07-split-level-layout.dxf", "AC1027", "Split-level logistics layout", 3),
        new("L2-DEV-004", "L2-MultiFloor", "08-three-floor-layout.dxf", "AC1032", "Three-floor warehouse layout", 4),
        new("L3-DEV-001", "L3-NonOrthogonal", "09-angled-rack-layout.dxf", "AC1009", "Angled rack field", 1),
        new("L3-DEV-002", "L3-NonOrthogonal", "10-l-shaped-building.dxf", "AC1021", "L-shaped warehouse", 2),
        new("L3-DEV-003", "L3-NonOrthogonal", "11-trapezoid-site.dxf", "AC1027", "Trapezoid site layout", 3),
        new("L3-DEV-004", "L3-NonOrthogonal", "12-diagonal-aisles.dxf", "AC1032", "Diagonal aisle network", 4),
        new("L4-DEV-001", "L4-Comprehensive", "13-automated-warehouse.dxf", "AC1015", "Automated warehouse", 1),
        new("L4-DEV-002", "L4-Comprehensive", "14-cold-storage-zones.dxf", "AC1021", "Cold storage zones", 2),
        new("L4-DEV-003", "L4-Comprehensive", "15-mixed-use-fulfillment.dxf", "AC1027", "Mixed-use fulfillment center", 3),
        new("L4-DEV-004", "L4-Comprehensive", "16-high-bay-layout.dxf", "AC1032", "High-bay warehouse", 4),
        new("L5-DEV-001", "L5-NoisyNonStandard", "17-noisy-layer-names.dxf", "AC1015", "Noisy and unknown layers", 1),
        new("L5-DEV-002", "L5-NoisyNonStandard", "18-block-attribute-edge-cases.dxf", "AC1021", "Block attribute edge cases", 2),
        new("L5-DEV-003", "L5-NoisyNonStandard", "19-curves-hatch-dimension.dxf", "AC1027", "Curves, hatch and dimensions", 3),
        new("L5-DEV-004", "L5-NoisyNonStandard", "20-xref-and-text-noise.dxf", "AC1032", "XRef and text noise", 4)
    ];

    public static async Task<DevelopmentCadCorpusReport> GenerateAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(outputDirectory);
        var seedDirectory = Path.Combine(root, "seeds");
        Directory.CreateDirectory(seedDirectory);

        var samples = new List<CadDatasetSample>();
        var expectedElements = new List<object>();
        var expectedIssueSamples = new List<object>();
        var providerIr = new List<object>();
        var caseIndex = new List<object>();
        long totalSizeBytes = 0;

        foreach (var plan in Plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var drawing = BuildDrawing(plan);
            var relativePath = $"seeds/{plan.FileName}";
            var sourcePath = Path.Combine(seedDirectory, plan.FileName);
            await File.WriteAllTextAsync(
                sourcePath,
                drawing.Serialize(),
                Utf8WithoutBom,
                cancellationToken);

            var hash = await DatasetAuditor.ComputeSha256Async(sourcePath, cancellationToken);
            var probe = DxfProbe.Inspect(sourcePath);
            if (probe.Errors.Count > 0)
            {
                throw new InvalidDataException(
                    $"Generated sample {plan.SampleId} failed DXF probing: "
                    + string.Join("; ", probe.Errors));
            }

            var sizeBytes = new FileInfo(sourcePath).Length;
            totalSizeBytes += sizeBytes;
            samples.Add(new CadDatasetSample(
                plan.SampleId,
                plan.LayoutFamily,
                "DevelopmentSeed",
                relativePath,
                hash,
                1));
            expectedElements.Add(new
            {
                sampleId = plan.SampleId,
                expectedId = $"{plan.SampleId}-FLOOR-01",
                type = "Floor",
                sourceRefs = new[] { new { layer = PrimaryWallLayer(plan) } },
                attributes = new { unit = "mm", synthetic = true }
            });
            expectedIssueSamples.Add(new
            {
                sampleId = plan.SampleId,
                issues = ExpectedIssues(plan)
            });
            providerIr.Add(new
            {
                schemaVersion = 1,
                sampleId = plan.SampleId,
                sourceHashRef = $"manifest:{plan.SampleId}",
                features = new
                {
                    cadVersion = probe.CadVersion,
                    entityCount = probe.EntityCount,
                    entityTypes = probe.EntityTypeCounts,
                    layers = probe.LayerCounts.Keys.OrderBy(value => value).ToArray()
                },
                excluded = new[]
                {
                    "rawFile", "filePath", "titleBlock", "user", "tenantName"
                }
            });
            caseIndex.Add(new
            {
                plan.SampleId,
                plan.LayoutFamily,
                plan.Title,
                plan.CadVersion,
                sourceFile = relativePath,
                sourceSha256 = hash,
                sizeBytes,
                probe.EntityCount,
                entityTypes = probe.EntityTypeCounts,
                layers = probe.LayerCounts,
                developmentFocus = DevelopmentFocus(plan)
            });
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["expectedElements"] = "expected-elements.jsonl",
            ["expectedIssues"] = "expected-issues.json",
            ["providerIr"] = "provider-ir.jsonl",
            ["layerMapping"] = "layer-mapping.json",
            ["license"] = "LICENSE.md"
        };
        var manifest = new
        {
            datasetName = "CP6 Space Synthetic Development CAD Corpus",
            datasetVersion = "2.0.0",
            schemaVersion = 1,
            createdAtUtc = "2026-08-02T00:00:00Z",
            generator = $"CP6.Space.CadExperiment/{GeneratorVersion}",
            purpose = "DevelopmentSeed",
            countsTowardReleaseGate = false,
            unit = "Millimeter",
            coordinateSystem = "FloorLocal-ZUp",
            mappingProfileVersion = "space-cad-mapping-v1",
            ruleSetVersion = "space-v1",
            expectedAnswerVersion = "2.0.0",
            license = "CP6-Synthetic-Development-Only",
            samples,
            files
        };

        await CadExperimentJson.WriteAsync(
            Path.Combine(root, "manifest.json"),
            manifest,
            cancellationToken);
        await WriteJsonLinesAsync(
            Path.Combine(root, "expected-elements.jsonl"),
            expectedElements,
            cancellationToken);
        await CadExperimentJson.WriteAsync(
            Path.Combine(root, "expected-issues.json"),
            new { schemaVersion = 1, samples = expectedIssueSamples },
            cancellationToken);
        await WriteJsonLinesAsync(
            Path.Combine(root, "provider-ir.jsonl"),
            providerIr,
            cancellationToken);
        await CadExperimentJson.WriteAsync(
            Path.Combine(root, "layer-mapping.json"),
            LayerMapping(),
            cancellationToken);
        await CadExperimentJson.WriteAsync(
            Path.Combine(root, "case-index.json"),
            new { schemaVersion = 1, generatorVersion = GeneratorVersion, samples = caseIndex },
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "LICENSE.md"),
            LicenseText,
            Utf8WithoutBom,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "README.md"),
            BuildReadme(),
            Utf8WithoutBom,
            cancellationToken);

        var report = new DevelopmentCadCorpusReport(
            1,
            GeneratorVersion,
            root,
            Path.Combine(root, "manifest.json"),
            samples.Count,
            totalSizeBytes,
            Plans.Select(plan => plan.CadVersion).Distinct().OrderBy(value => value).ToArray(),
            Plans.GroupBy(plan => plan.LayoutFamily[..2])
                .ToDictionary(group => group.Key, group => group.Count()));
        await CadExperimentJson.WriteAsync(
            Path.Combine(root, "generation-report.json"),
            report with
            {
                OutputDirectory = ".",
                ManifestPath = "manifest.json"
            },
            cancellationToken);
        return report;
    }

    private static DxfDrawing BuildDrawing(SamplePlan plan)
    {
        var drawing = new DxfDrawing(plan.CadVersion);
        drawing.AddStandardLayers();
        drawing.AddRackBlock(includeAttribute: plan.LayoutFamily.StartsWith("L4") || plan.Variant == 2);
        switch (plan.LayoutFamily[..2])
        {
            case "L1":
                AddRegularLayout(drawing, plan.Variant);
                break;
            case "L2":
                AddMultiFloorLayout(drawing, plan.Variant);
                break;
            case "L3":
                AddNonOrthogonalLayout(drawing, plan.Variant);
                break;
            case "L4":
                AddComprehensiveLayout(drawing, plan.Variant);
                break;
            case "L5":
                AddNoisyLayout(drawing, plan.Variant);
                break;
            default:
                throw new InvalidOperationException($"Unknown layout family {plan.LayoutFamily}.");
        }

        drawing.Text("ANNOTATION", 500, -1200, plan.SampleId, 350);
        return drawing;
    }

    private static void AddRegularLayout(DxfDrawing drawing, int variant)
    {
        var width = 24_000 + variant * 2_000;
        var depth = 14_000 + variant * 1_000;
        drawing.ClosedPolyline("WALL", [(0, 0), (width, 0), (width, depth), (0, depth)]);
        drawing.Line("DOOR", width / 2d - 1_000, 0, width / 2d + 1_000, 0);
        drawing.ClosedPolyline(
            "DOCK",
            [(width - 5_000, 0), (width - 1_000, 0), (width - 1_000, 1_500), (width - 5_000, 1_500)]);

        var columns = variant >= 3 ? 5 : 4;
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                drawing.Insert(
                    "RACK",
                    "RACK_UNIT",
                    3_000 + column * 4_500,
                    3_500 + row * 5_000,
                    attributes: null);
            }
        }

        drawing.Line("AISLE", 1_500, depth / 2d, width - 1_500, depth / 2d);
        if (variant == 4)
        {
            drawing.Line("AISLE", width / 2d, 1_500, width / 2d, depth - 1_500);
        }
    }

    private static void AddMultiFloorLayout(DxfDrawing drawing, int variant)
    {
        var floors = variant == 4 ? 3 : 2;
        var width = 22_000 + variant * 1_000;
        var depth = 13_000;
        for (var floor = 0; floor < floors; floor++)
        {
            var elevation = floor * (variant == 3 ? 4_500 : 6_000);
            var wallLayer = $"F{floor + 1:00}_WALL";
            var rackLayer = $"F{floor + 1:00}_RACK";
            drawing.EnsureLayer(wallLayer, 7);
            drawing.EnsureLayer(rackLayer, 3);
            drawing.ClosedPolyline(
                wallLayer,
                [(0, 0), (width, 0), (width, depth), (0, depth)],
                elevation);
            for (var rack = 0; rack < 3; rack++)
            {
                drawing.ClosedPolyline(
                    rackLayer,
                    [
                        (3_000 + rack * 5_500, 3_000),
                        (6_500 + rack * 5_500, 3_000),
                        (6_500 + rack * 5_500, 4_200),
                        (3_000 + rack * 5_500, 4_200)
                    ],
                    elevation);
            }

            drawing.Text(wallLayer, 500, depth - 800, $"FLOOR {floor + 1}", 400, elevation);
        }

        drawing.Line("STAIR", width - 3_000, 2_000, width - 1_000, 5_000);
        drawing.Circle("COLUMN", width - 2_000, 3_500, 350);
    }

    private static void AddNonOrthogonalLayout(DxfDrawing drawing, int variant)
    {
        IReadOnlyList<(double X, double Y)> boundary = variant switch
        {
            2 => [(0, 0), (26_000, 0), (26_000, 8_000), (17_000, 8_000), (17_000, 18_000), (0, 18_000)],
            3 => [(0, 0), (28_000, 2_500), (24_000, 18_000), (3_000, 16_000)],
            4 => [(0, 2_000), (25_000, 0), (30_000, 15_000), (5_000, 19_000)],
            _ => [(0, 0), (27_000, 0), (25_000, 17_000), (2_000, 19_000)]
        };
        drawing.ClosedPolyline("WALL", boundary);
        var rotation = 12 + variant * 7;
        for (var rack = 0; rack < 6; rack++)
        {
            drawing.Insert(
                "RACK",
                "RACK_UNIT",
                4_000 + rack * 3_200,
                4_000 + (rack % 2) * 4_500,
                rotation,
                attributes: null);
        }

        drawing.Line("AISLE", 2_500, 3_000, 23_000, 12_000);
        drawing.Line("AISLE", 4_000, 14_500, 21_000, 5_500);
        drawing.Arc("DOOR", 4_000, 1_000, 1_200, 0, 85);
    }

    private static void AddComprehensiveLayout(DxfDrawing drawing, int variant)
    {
        var width = 36_000;
        var depth = 24_000;
        drawing.ClosedPolyline("WALL", [(0, 0), (width, 0), (width, depth), (0, depth)]);
        for (var x = 6_000; x <= 30_000; x += 8_000)
        {
            drawing.Circle("COLUMN", x, 8_000, 300);
            drawing.Circle("COLUMN", x, 18_000, 300);
        }

        for (var rack = 0; rack < 8; rack++)
        {
            var attributes = new Dictionary<string, string>
            {
                ["RACK_ID"] = $"R-{variant:00}-{rack + 1:00}"
            };
            drawing.Insert(
                "RACK",
                "RACK_UNIT",
                3_000 + (rack % 4) * 6_000,
                3_500 + (rack / 4) * 6_000,
                attributes: attributes);
        }

        drawing.ClosedPolyline(
            "EQUIP_CONVEYOR",
            [(3_000, 17_000), (28_000, 17_000), (28_000, 18_500), (3_000, 18_500)]);
        drawing.ClosedPolyline(
            "EQUIP_CHARGER",
            [(30_000, 18_000), (34_000, 18_000), (34_000, 22_000), (30_000, 22_000)]);
        drawing.ClosedPolyline(
            "DOCK",
            [(5_000, 0), (11_000, 0), (11_000, 1_800), (5_000, 1_800)]);
        drawing.Line("DOOR", 17_000, 0, 20_000, 0);

        if (variant >= 2)
        {
            drawing.HatchRectangle("ZONE", 1_000, 19_500, 9_000, 23_000);
        }

        if (variant >= 3)
        {
            drawing.Ellipse("SAFETY", 29_000, 9_000, 3_000, 0, 0.45);
        }

        if (variant == 4)
        {
            drawing.Spline(
                "ROUTE",
                [(2_000, 21_000), (10_000, 20_000), (20_000, 22_500), (34_000, 20_500)]);
        }
    }

    private static void AddNoisyLayout(DxfDrawing drawing, int variant)
    {
        drawing.EnsureLayer("A01", 7);
        drawing.EnsureLayer("X-NOISE", 8);
        drawing.EnsureLayer("RACKS_old_FINAL", 2);
        drawing.ClosedPolyline("A01", [(0, 0), (24_000, 0), (24_000, 14_000), (0, 14_000)]);
        drawing.Insert("A01", "RACK_UNIT", 4_000, 3_000, attributes: null);
        drawing.Insert(
            "RACKS_old_FINAL",
            "RACK_UNIT",
            10_000,
            3_000,
            attributes: variant == 2
                ? new Dictionary<string, string> { ["RACK_ID"] = "DUPLICATE-LABEL" }
                : null);
        drawing.Line("X-NOISE", -2_000, -2_000, 28_000, 17_000);
        drawing.Text("X-NOISE", 1_000, 13_000, "SYNTHETIC NOTE - NOT CUSTOMER DATA", 300);

        if (variant >= 2)
        {
            drawing.Insert(
                "RACKS_old_FINAL",
                "RACK_UNIT",
                15_000,
                3_000,
                17,
                new Dictionary<string, string> { ["RACK_ID"] = "RACK ?" });
            drawing.MText("X-NOISE", 1_000, 11_500, "Legacy text\\Pwith multiple lines", 4_000, 280);
        }

        if (variant >= 3)
        {
            drawing.Ellipse("X-NOISE", 7_000, 9_000, 3_000, 700, 0.35);
            drawing.Spline(
                "X-NOISE",
                [(11_000, 8_000), (13_000, 11_500), (17_000, 7_500), (21_000, 10_000)]);
            drawing.HatchRectangle("X-NOISE", 18_000, 9_000, 22_000, 12_000);
            drawing.Dimension("DIMENSION", 2_000, 1_000, 8_000, 1_000, 5_000, 500);
        }

        if (variant == 4)
        {
            drawing.AddXrefBlock("SYNTHETIC_XREF", "missing-synthetic-reference.dwg");
            drawing.Insert("X-NOISE", "SYNTHETIC_XREF", 2_000, 2_000, attributes: null);
            drawing.Text("X-NOISE", 1_000, 10_500, "?? unknown encoding marker ??", 250);
        }
    }

    private static string PrimaryWallLayer(SamplePlan plan) => plan.LayoutFamily[..2] switch
    {
        "L2" => "F01_WALL",
        "L5" => "A01",
        _ => "WALL"
    };

    private static object[] ExpectedIssues(SamplePlan plan)
    {
        if (!plan.LayoutFamily.StartsWith("L5", StringComparison.Ordinal))
        {
            return [];
        }

        var issues = new List<object>
        {
            new { severity = "Warning", code = "SPACE_CAD_UNKNOWN_LAYER", sourceLayer = "X-NOISE", minimumCount = 1 },
            new { severity = "Info", code = "SPACE_CAD_SYNTHETIC_NOISE", minimumCount = 1 }
        };
        if (plan.Variant == 4)
        {
            issues.Add(new
            {
                severity = "Warning",
                code = "SPACE_CAD_UNRESOLVED_XREF",
                sourceBlock = "SYNTHETIC_XREF",
                expectedCount = 1
            });
        }

        return issues.ToArray();
    }

    private static string[] DevelopmentFocus(SamplePlan plan) => plan.LayoutFamily[..2] switch
    {
        "L1" => ["boundary", "rack-grid", "door", "dock", "aisle"],
        "L2" => ["floor-layer", "elevation", "multi-floor", "stairs"],
        "L3" => ["non-orthogonal-boundary", "rotation", "diagonal-aisle", "arc"],
        "L4" => ["blocks", "attributes", "equipment", "columns", "zones", "curves"],
        "L5" => ["unknown-layer", "text-noise", "missing-attribute", "xref", "unsupported-entity"],
        _ => []
    };

    private static object LayerMapping() => new
    {
        schemaVersion = 1,
        profileId = "space-cad-mapping-v1",
        rules = new object[]
        {
            new { pattern = "WALL|F01_WALL|F02_WALL|F03_WALL", semanticType = "Wall" },
            new { pattern = "RACK|F01_RACK|F02_RACK|F03_RACK", semanticType = "Rack" },
            new { pattern = "AISLE", semanticType = "Aisle" },
            new { pattern = "DOOR", semanticType = "Door" },
            new { pattern = "DOCK", semanticType = "Dock" },
            new { pattern = "COLUMN", semanticType = "Column" },
            new { pattern = "EQUIP_CONVEYOR", semanticType = "Equipment", subtype = "Conveyor" },
            new { pattern = "EQUIP_CHARGER", semanticType = "Equipment", subtype = "ChargingStation" },
            new { pattern = "STAIR", semanticType = "VerticalCirculation", subtype = "Stair" },
            new { pattern = "ZONE", semanticType = "Zone" }
        },
        unknownLayerPolicy = "CreateIssue",
        missingBlockAttributePolicy = "CreateIssue",
        unresolvedXrefPolicy = "CreateIssue"
    };

    private static async Task WriteJsonLinesAsync(
        string path,
        IEnumerable<object> values,
        CancellationToken cancellationToken)
    {
        var lines = values.Select(value => JsonSerializer.Serialize(value, JsonLineOptions));
        await File.WriteAllTextAsync(
            path,
            string.Join('\n', lines) + "\n",
            Utf8WithoutBom,
            cancellationToken);
    }

    private static string BuildReadme()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CP6 Space synthetic development CAD corpus v2.0.0");
        builder.AppendLine();
        builder.AppendLine("This package contains 20 deterministic ASCII DXF drawings created for CP6 development.");
        builder.AppendLine("It contains no customer, supplier, site, personal, address, title-block, or equipment-serial data.");
        builder.AppendLine();
        builder.AppendLine("## Boundaries");
        builder.AppendLine();
        builder.AppendLine("- `purpose=DevelopmentSeed` and `countsTowardReleaseGate=false` are intentional.");
        builder.AppendLine("- The files may be used for parser, mapping, issue, UI, regression, and demo development.");
        builder.AppendLine("- Header coverage does not replace licensed vendor fidelity testing against native DWG files.");
        builder.AppendLine("- The unresolved XRef in L5-DEV-004 is synthetic and intentional.");
        builder.AppendLine("- Re-run generation with `generate-dev-corpus --output <directory>`.");
        builder.AppendLine();
        builder.AppendLine("## Matrix");
        builder.AppendLine();
        builder.AppendLine("| Sample | Family | DXF header | Scenario |");
        builder.AppendLine("|---|---|---|---|");
        foreach (var plan in Plans)
        {
            builder.AppendLine($"| {plan.SampleId} | {plan.LayoutFamily} | {plan.CadVersion} | {plan.Title} |");
        }

        return builder.ToString();
    }

    private const string LicenseText =
        """
        # CP6 synthetic development CAD asset statement

        Every DXF file and companion record in this directory is generated specifically for
        CP6 from source code in this repository. No drawing is copied from a customer,
        supplier, public download, previous employer, real warehouse, or third-party CAD
        library.

        The package may be stored, copied, parsed, transformed, rendered, and modified inside
        the CP6 project for development, automated tests, demonstrations, and technical trials.

        This package is not formal release-gate evidence. It does not prove native DWG
        compatibility, vendor SDK licensing, SaaS processing rights, real-world fidelity,
        de-identification of customer drawings, or the required independent golden holdout.
        `countsTowardReleaseGate` must remain `false`.
        """;

    private sealed record SamplePlan(
        string SampleId,
        string LayoutFamily,
        string FileName,
        string CadVersion,
        string Title,
        int Variant);

    private sealed class DxfDrawing
    {
        private readonly string _cadVersion;
        private readonly Dictionary<string, int> _layers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Action<StringBuilder>> _blocks = [];
        private readonly List<Action<StringBuilder>> _entities = [];
        private int _nextHandle = 0x100;
        private int _nextDimensionBlock = 1;

        public DxfDrawing(string cadVersion)
        {
            _cadVersion = cadVersion;
        }

        public void AddStandardLayers()
        {
            EnsureLayer("0", 7);
            EnsureLayer("WALL", 7);
            EnsureLayer("RACK", 3);
            EnsureLayer("AISLE", 4);
            EnsureLayer("DOOR", 2);
            EnsureLayer("DOCK", 5);
            EnsureLayer("COLUMN", 6);
            EnsureLayer("EQUIP_CONVEYOR", 1);
            EnsureLayer("EQUIP_CHARGER", 30);
            EnsureLayer("STAIR", 4);
            EnsureLayer("ZONE", 8);
            EnsureLayer("SAFETY", 1);
            EnsureLayer("ROUTE", 4);
            EnsureLayer("DIMENSION", 2);
            EnsureLayer("ANNOTATION", 7);
        }

        public void EnsureLayer(string name, int color)
        {
            _layers.TryAdd(name, color);
        }

        public void AddRackBlock(bool includeAttribute)
        {
            _blocks.Add(builder =>
            {
                Pair(builder, 0, "BLOCK");
                Pair(builder, 5, NextHandle());
                Pair(builder, 8, "0");
                Pair(builder, 2, "RACK_UNIT");
                Pair(builder, 70, "0");
                Point(builder, 10, 0, 0, 0);
                BlockLine(builder, 0, 0, 3_500, 0);
                BlockLine(builder, 3_500, 0, 3_500, 1_200);
                BlockLine(builder, 3_500, 1_200, 0, 1_200);
                BlockLine(builder, 0, 1_200, 0, 0);
                if (includeAttribute)
                {
                    Pair(builder, 0, "ATTDEF");
                    Pair(builder, 5, NextHandle());
                    Pair(builder, 8, "0");
                    Pair(builder, 2, "RACK_ID");
                    Pair(builder, 3, "Rack identifier");
                    Pair(builder, 1, "UNSET");
                    Point(builder, 10, 300, 400, 0);
                    Pair(builder, 40, "250");
                    Pair(builder, 70, "0");
                }

                Pair(builder, 0, "ENDBLK");
                Pair(builder, 5, NextHandle());
                Pair(builder, 8, "0");
            });
        }

        public void AddXrefBlock(string name, string relativePath)
        {
            _blocks.Add(builder =>
            {
                Pair(builder, 0, "BLOCK");
                Pair(builder, 5, NextHandle());
                Pair(builder, 8, "0");
                Pair(builder, 2, name);
                Pair(builder, 70, "4");
                Point(builder, 10, 0, 0, 0);
                Pair(builder, 1, relativePath);
                Pair(builder, 0, "ENDBLK");
                Pair(builder, 5, NextHandle());
                Pair(builder, 8, "0");
            });
        }

        public void Line(
            string layer,
            double x1,
            double y1,
            double x2,
            double y2,
            double z = 0)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "LINE", layer);
                Point(builder, 10, x1, y1, z);
                Point(builder, 11, x2, y2, z);
            });
        }

        public void Circle(string layer, double x, double y, double radius, double z = 0)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "CIRCLE", layer);
                Point(builder, 10, x, y, z);
                Pair(builder, 40, Number(radius));
            });
        }

        public void Arc(
            string layer,
            double x,
            double y,
            double radius,
            double startAngle,
            double endAngle)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "ARC", layer);
                Point(builder, 10, x, y, 0);
                Pair(builder, 40, Number(radius));
                Pair(builder, 50, Number(startAngle));
                Pair(builder, 51, Number(endAngle));
            });
        }

        public void ClosedPolyline(
            string layer,
            IReadOnlyList<(double X, double Y)> points,
            double elevation = 0)
        {
            EnsureLayer(layer, 7);
            if (_cadVersion == "AC1009")
            {
                _entities.Add(builder =>
                {
                    BeginEntity(builder, "POLYLINE", layer);
                    Pair(builder, 66, "1");
                    Point(builder, 10, 0, 0, elevation);
                    Pair(builder, 70, "1");
                    foreach (var point in points)
                    {
                        BeginEntity(builder, "VERTEX", layer);
                        Point(builder, 10, point.X, point.Y, elevation);
                    }

                    BeginEntity(builder, "SEQEND", layer);
                });
                return;
            }

            _entities.Add(builder =>
            {
                BeginEntity(builder, "LWPOLYLINE", layer);
                Pair(builder, 90, points.Count.ToString(CultureInfo.InvariantCulture));
                Pair(builder, 70, "1");
                if (elevation != 0)
                {
                    Pair(builder, 38, Number(elevation));
                }

                foreach (var point in points)
                {
                    Pair(builder, 10, Number(point.X));
                    Pair(builder, 20, Number(point.Y));
                }
            });
        }

        public void Insert(
            string layer,
            string blockName,
            double x,
            double y,
            double rotation = 0,
            IReadOnlyDictionary<string, string>? attributes = null)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "INSERT", layer);
                Pair(builder, 2, blockName);
                Point(builder, 10, x, y, 0);
                if (rotation != 0)
                {
                    Pair(builder, 50, Number(rotation));
                }

                if (attributes is null || attributes.Count == 0)
                {
                    return;
                }

                Pair(builder, 66, "1");
                var offset = 0d;
                foreach (var (tag, value) in attributes)
                {
                    BeginEntity(builder, "ATTRIB", layer);
                    Pair(builder, 2, tag);
                    Pair(builder, 1, value);
                    Point(builder, 10, x + 300, y + 400 + offset, 0);
                    Pair(builder, 40, "250");
                    Pair(builder, 70, "0");
                    offset += 300;
                }

                BeginEntity(builder, "SEQEND", layer);
            });
        }

        public void Text(
            string layer,
            double x,
            double y,
            string value,
            double height,
            double z = 0)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "TEXT", layer);
                Point(builder, 10, x, y, z);
                Pair(builder, 40, Number(height));
                Pair(builder, 1, value);
            });
        }

        public void MText(
            string layer,
            double x,
            double y,
            string value,
            double width,
            double height)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "MTEXT", layer);
                Point(builder, 10, x, y, 0);
                Pair(builder, 40, Number(height));
                Pair(builder, 41, Number(width));
                Pair(builder, 1, value);
            });
        }

        public void Ellipse(
            string layer,
            double centerX,
            double centerY,
            double majorX,
            double majorY,
            double ratio)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "ELLIPSE", layer);
                Point(builder, 10, centerX, centerY, 0);
                Point(builder, 11, majorX, majorY, 0);
                Pair(builder, 40, Number(ratio));
                Pair(builder, 41, "0");
                Pair(builder, 42, Number(Math.PI * 2));
            });
        }

        public void Spline(string layer, IReadOnlyList<(double X, double Y)> controlPoints)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "SPLINE", layer);
                Point(builder, 210, 0, 0, 1);
                Pair(builder, 70, "8");
                Pair(builder, 71, "3");
                Pair(builder, 72, "8");
                Pair(builder, 73, controlPoints.Count.ToString(CultureInfo.InvariantCulture));
                Pair(builder, 74, "0");
                foreach (var knot in new[] { 0d, 0d, 0d, 0d, 1d, 1d, 1d, 1d })
                {
                    Pair(builder, 40, Number(knot));
                }

                foreach (var point in controlPoints)
                {
                    Point(builder, 10, point.X, point.Y, 0);
                }
            });
        }

        public void HatchRectangle(
            string layer,
            double x1,
            double y1,
            double x2,
            double y2)
        {
            EnsureLayer(layer, 7);
            _entities.Add(builder =>
            {
                BeginEntity(builder, "HATCH", layer);
                Point(builder, 10, 0, 0, 0);
                Point(builder, 210, 0, 0, 1);
                Pair(builder, 2, "SOLID");
                Pair(builder, 70, "1");
                Pair(builder, 71, "0");
                Pair(builder, 91, "1");
                Pair(builder, 92, "2");
                Pair(builder, 72, "0");
                Pair(builder, 73, "1");
                Pair(builder, 93, "4");
                foreach (var point in new[] { (x1, y1), (x2, y1), (x2, y2), (x1, y2) })
                {
                    Pair(builder, 10, Number(point.Item1));
                    Pair(builder, 20, Number(point.Item2));
                }

                Pair(builder, 97, "0");
                Pair(builder, 75, "0");
                Pair(builder, 76, "1");
                Pair(builder, 98, "0");
            });
        }

        public void Dimension(
            string layer,
            double x1,
            double y1,
            double x2,
            double y2,
            double textX,
            double textY)
        {
            EnsureLayer(layer, 7);
            var blockName = $"*D{_nextDimensionBlock++}";
            _blocks.Add(builder =>
            {
                Pair(builder, 0, "BLOCK");
                Pair(builder, 5, NextHandle());
                Pair(builder, 8, layer);
                Pair(builder, 2, blockName);
                Pair(builder, 70, "1");
                Point(builder, 10, 0, 0, 0);
                BlockLine(builder, x1, y1, x2, y2);
                Pair(builder, 0, "ENDBLK");
                Pair(builder, 5, NextHandle());
                Pair(builder, 8, layer);
            });
            _entities.Add(builder =>
            {
                BeginEntity(builder, "DIMENSION", layer);
                Pair(builder, 2, blockName);
                Point(builder, 10, textX, textY, 0);
                Point(builder, 11, textX, textY, 0);
                Pair(builder, 70, "0");
                Pair(builder, 1, "<>");
                Point(builder, 13, x1, y1, 0);
                Point(builder, 14, x2, y2, 0);
            });
        }

        public string Serialize()
        {
            var builder = new StringBuilder();
            Pair(builder, 0, "SECTION");
            Pair(builder, 2, "HEADER");
            Pair(builder, 9, "$ACADVER");
            Pair(builder, 1, _cadVersion);
            Pair(builder, 9, "$INSUNITS");
            Pair(builder, 70, "4");
            Pair(builder, 0, "ENDSEC");

            Pair(builder, 0, "SECTION");
            Pair(builder, 2, "TABLES");
            Pair(builder, 0, "TABLE");
            Pair(builder, 2, "LAYER");
            Pair(builder, 70, _layers.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var (name, color) in _layers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Pair(builder, 0, "LAYER");
                Pair(builder, 2, name);
                Pair(builder, 70, "0");
                Pair(builder, 62, color.ToString(CultureInfo.InvariantCulture));
                Pair(builder, 6, "CONTINUOUS");
            }

            Pair(builder, 0, "ENDTAB");
            Pair(builder, 0, "ENDSEC");

            Pair(builder, 0, "SECTION");
            Pair(builder, 2, "BLOCKS");
            foreach (var block in _blocks)
            {
                block(builder);
            }

            Pair(builder, 0, "ENDSEC");
            Pair(builder, 0, "SECTION");
            Pair(builder, 2, "ENTITIES");
            foreach (var entity in _entities)
            {
                entity(builder);
            }

            Pair(builder, 0, "ENDSEC");
            Pair(builder, 0, "EOF");
            return builder.ToString();
        }

        private void BeginEntity(StringBuilder builder, string type, string layer)
        {
            Pair(builder, 0, type);
            Pair(builder, 5, NextHandle());
            Pair(builder, 8, layer);
        }

        private void BlockLine(
            StringBuilder builder,
            double x1,
            double y1,
            double x2,
            double y2)
        {
            BeginEntity(builder, "LINE", "0");
            Point(builder, 10, x1, y1, 0);
            Point(builder, 11, x2, y2, 0);
        }

        private string NextHandle() => (_nextHandle++).ToString("X", CultureInfo.InvariantCulture);

        private static void Point(
            StringBuilder builder,
            int xCode,
            double x,
            double y,
            double z)
        {
            Pair(builder, xCode, Number(x));
            Pair(builder, xCode + 10, Number(y));
            Pair(builder, xCode + 20, Number(z));
        }

        private static string Number(double value) =>
            value.ToString("0.###############", CultureInfo.InvariantCulture);

        private static void Pair(StringBuilder builder, int code, string value)
        {
            builder.Append(code.ToString(CultureInfo.InvariantCulture));
            builder.Append('\n');
            builder.Append(value);
            builder.Append('\n');
        }
    }
}
