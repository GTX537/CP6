using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;

namespace CP6.Space.StandardWarehouseGenerator;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions JsonLineOptions = new(
        JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var output = Required(args, "--output");
            await GenerateAsync(Path.GetFullPath(output));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task GenerateAsync(string outputDirectory)
    {
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();
        EnsureOutputDirectoryIsEmpty(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(Path.Combine(outputDirectory, "fault-cases"));

        await WriteTextAsync(
            Path.Combine(outputDirectory, "warehouse-standard.dxf"),
            CreateDxf(dataset));
        await WriteTextAsync(
            Path.Combine(outputDirectory, "expected-elements.jsonl"),
            CreateExpectedElements(dataset));
        await WriteTextAsync(
            Path.Combine(outputDirectory, "expected-locations.csv"),
            CreateLocationsCsv(dataset));
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "wms-seed.json"),
            new
            {
                schemaVersion = dataset.SchemaVersion,
                datasetVersion = dataset.DatasetVersion,
                dataSource = "Simulated",
                warehouseCode = dataset.WarehouseCode,
                skus = dataset.Skus,
                inventory = dataset.Inventory,
                pickTasks = dataset.PickTasks,
            });
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "metadata.json"),
            new
            {
                schemaVersion = 1,
                dataset.DatasetVersion,
                sampleId = "STANDARD-WAREHOUSE-001",
                split = "DevelopmentSeed",
                layoutFamily = "L2-MultiFloor",
                unit = "Millimeter",
                coordinateSystem = "FloorLocal-ZUp",
                mappingProfileVersion = "space-cad-mapping-v1",
                ruleSetVersion = "space-v1",
                license = "Synthetic",
                deidentificationEvidence =
                    "Fully generated; contains no customer source data.",
                expectedAnswerVersion = dataset.DatasetVersion,
                dataSource = "Simulated",
            });
        await WriteTextAsync(
            Path.Combine(outputDirectory, "LICENSE.md"),
            """
            # Synthetic acceptance asset license

            This package is generated entirely from CP6 deterministic source code.
            It contains no customer files, customer names, user information, secrets,
            or third-party CAD content. It is intended only for development and
            acceptance testing. The data source must be displayed as `Simulated`.
            """.ReplaceLineEndings("\n") + "\n");
        await WriteTextAsync(
            Path.Combine(outputDirectory, "README.md"),
            """
            # CP6 Space standard warehouse 1.0.0

            Deterministic synthetic acceptance data for E07-S04.

            - 2 floors, 7 zones, 20 aisles, 500 racks and 10,000 locations.
            - 100 SKUs, 5,000 inventory records and 100 pick tasks.
            - All identities and values derive from the fixed seed in `manifest.json`.
            - The package is simulated data and must never be presented as live WMS data.
            - `warehouse-standard.dwg` is intentionally absent while E02-S01's licensed
              DWG converter decision remains blocked. No fake DWG is supplied.
            """.ReplaceLineEndings("\n") + "\n");
        await WritePngAsync(
            Path.Combine(outputDirectory, "floor-1.png"),
            floorLevel: 1);
        await WritePngAsync(
            Path.Combine(outputDirectory, "floor-2.png"),
            floorLevel: 2);
        await File.WriteAllBytesAsync(
            Path.Combine(outputDirectory, "floor-maps.pdf"),
            CreatePdf());

        await WriteFaultCasesAsync(outputDirectory, dataset);
        await WriteManifestAsync(outputDirectory, dataset);
        Console.WriteLine(
            $"Generated {dataset.DatasetVersion} at {outputDirectory}");
        Console.WriteLine($"contentSha256={dataset.ContentSha256}");
    }

    private static async Task WriteFaultCasesAsync(
        string outputDirectory,
        SpaceStandardWarehouseDataset dataset)
    {
        var faultDirectory = Path.Combine(outputDirectory, "fault-cases");
        await WriteTextAsync(
            Path.Combine(faultDirectory, "unknown-layer.dxf"),
            CreateUnknownLayerDxf());
        var first = dataset.Locations[0];
        var second = dataset.Locations[1];
        await WriteTextAsync(
            Path.Combine(faultDirectory, "duplicate-location-code.csv"),
            JoinLines(
                "LogicalId,LocationCode,FloorCode,ZoneCode",
                Csv(first.LogicalId, first.Code, first.FloorCode, first.ZoneCode),
                Csv(second.LogicalId, first.Code, second.FloorCode, second.ZoneCode)));
        await WriteTextAsync(
            Path.Combine(faultDirectory, "missing-location-code.csv"),
            JoinLines(
                "LogicalId,FloorCode,ZoneCode",
                Csv(first.LogicalId, first.FloorCode, first.ZoneCode)));
        await WriteJsonAsync(
            Path.Combine(
                faultDirectory,
                "coordinate-out-of-bounds.json"),
            new
            {
                expectedErrorCode = "SPACE_GEOMETRY_OUT_OF_BOUNDS",
                floorCode = "F1",
                floorBoundsMm = new
                {
                    minX = 0,
                    minY = 0,
                    maxX = 140_000,
                    maxY = 120_000,
                },
                location = new
                {
                    logicalId =
                        SpaceStandardWarehouseDatasetGenerator
                            .CreateDeterministicId("fault:out-of-bounds"),
                    locationCode = "F1-FAULT-OUT-OF-BOUNDS",
                    xMm = 150_000,
                    yMm = 130_000,
                    zMm = 500,
                },
            });
        await WriteTextAsync(
            Path.Combine(faultDirectory, "corrupt-input.dxf"),
            "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nRACK\n10\nnot-a-number\n");
        await WriteJsonAsync(
            Path.Combine(faultDirectory, "wms-timeout.json"),
            new
            {
                simulator = "standard-wms-simulator-v1",
                mode = "Timeout",
                delayMilliseconds = 250,
                errorCode = "SPACE_WMS_RETRYABLE",
                expectedResult = "TimeoutException",
            });
    }

    private static async Task WriteManifestAsync(
        string outputDirectory,
        SpaceStandardWarehouseDataset dataset)
    {
        var files = Directory
            .EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(path =>
                !string.Equals(
                    Path.GetFileName(path),
                    "manifest.json",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetRelativePath(outputDirectory, path), StringComparer.Ordinal)
            .Select(path => new
            {
                path = Path.GetRelativePath(outputDirectory, path)
                    .Replace('\\', '/'),
                sha256 = ComputeFileHash(path),
                bytes = new FileInfo(path).Length,
            })
            .ToArray();
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "manifest.json"),
            new
            {
                schemaVersion = dataset.SchemaVersion,
                datasetName = "CP6 Space Standard Warehouse",
                datasetVersion = dataset.DatasetVersion,
                compatibleSpecVersion = dataset.CompatibleSpecVersion,
                generatedAtUtc = dataset.GeneratedAtUtc,
                maintainerRole = "Space QA",
                purpose = "DevelopmentSeed",
                countsTowardReleaseGate = false,
                license = "Synthetic",
                dataSource = "Simulated",
                counts = dataset.Counts,
                generator = new
                {
                    version = dataset.GeneratorVersion,
                    randomSeed = dataset.RandomSeed,
                    contentSha256 = dataset.ContentSha256,
                    usesUnfixedRandomness = false,
                },
                readiness = new
                {
                    dxf = "Ready",
                    pngUnderlays = "Ready",
                    pdfUnderlay = "Ready",
                    wmsSeed = "Ready",
                    faultCases = "Ready",
                    dwg = "BlockedByE02S01LicensedConverterDecision",
                },
                missingRequiredArtifacts = new[]
                {
                    "warehouse-standard.dwg",
                },
                files,
            });
    }

    private static void EnsureOutputDirectoryIsEmpty(string outputDirectory)
    {
        if (Directory.Exists(outputDirectory) &&
            Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new InvalidOperationException(
                "The output directory must be empty so the manifest cannot "
                + "include stale artifacts.");
        }
    }

    private static string CreateDxf(
        SpaceStandardWarehouseDataset dataset)
    {
        var lines = new List<string>
        {
            "0", "SECTION", "2", "HEADER",
            "9", "$ACADVER", "1", "AC1027",
            "9", "$INSUNITS", "70", "4",
            "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
        };
        var handle = 0x100;
        foreach (var floor in dataset.Floors)
        {
            AddRectangle(
                lines,
                handle++,
                "FLOOR",
                floor.OriginXmm,
                floor.OriginYmm,
                floor.WidthMm,
                floor.DepthMm);
        }
        foreach (var zone in dataset.Zones)
        {
            AddRectangle(
                lines,
                handle++,
                "ZONE",
                zone.MinXmm,
                zone.MinYmm,
                zone.MaxXmm - zone.MinXmm,
                zone.MaxYmm - zone.MinYmm);
        }
        foreach (var aisle in dataset.Aisles)
        {
            AddLine(
                lines,
                handle++,
                "AISLE",
                aisle.StartXmm,
                aisle.StartYmm,
                aisle.EndXmm,
                aisle.EndYmm);
        }
        foreach (var rack in dataset.Racks)
        {
            AddRectangle(
                lines,
                handle++,
                "RACK",
                rack.Xmm - (rack.WidthMm / 2m),
                rack.Ymm - (rack.DepthMm / 2m),
                rack.WidthMm,
                rack.DepthMm);
        }
        foreach (var location in dataset.Locations)
        {
            lines.AddRange(
            [
                "0", "POINT",
                "5", (handle++).ToString("X"),
                "8", "LOCATION",
                "10", Invariant(location.Xmm),
                "20", Invariant(location.Ymm),
                "30", Invariant(location.Zmm),
            ]);
        }
        lines.AddRange(["0", "ENDSEC", "0", "EOF"]);
        return string.Join('\n', lines) + "\n";
    }

    private static string CreateExpectedElements(
        SpaceStandardWarehouseDataset dataset)
    {
        var lines = new List<string>(
            dataset.Counts.Floors +
            dataset.Counts.Zones +
            dataset.Counts.Aisles +
            dataset.Counts.Racks +
            dataset.Counts.Locations);
        var handle = 0x100;
        foreach (var floor in dataset.Floors)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                expectedId = floor.ExpectedId,
                type = "Floor",
                code = floor.Code,
                parentExpectedId = (string?)null,
                sourceRefs = new
                {
                    layer = "FLOOR",
                    handle = (handle++).ToString("X"),
                },
                geometry = new
                {
                    kind = "Rectangle",
                    xMm = floor.OriginXmm,
                    yMm = floor.OriginYmm,
                    floor.WidthMm,
                    floor.DepthMm,
                    zMm = floor.OriginZmm,
                },
                tolerance = Tolerance("polygon"),
            }, JsonLineOptions));
        }
        foreach (var zone in dataset.Zones)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                expectedId = zone.ExpectedId,
                type = "Zone",
                code = zone.Code,
                parentExpectedId = zone.FloorExpectedId,
                sourceRefs = new
                {
                    layer = "ZONE",
                    handle = (handle++).ToString("X"),
                },
                geometry = new
                {
                    kind = "Rectangle",
                    minXmm = zone.MinXmm,
                    minYmm = zone.MinYmm,
                    maxXmm = zone.MaxXmm,
                    maxYmm = zone.MaxYmm,
                },
                attributes = new { zone.ZoneType },
                tolerance = Tolerance("polygon"),
            }, JsonLineOptions));
        }
        foreach (var aisle in dataset.Aisles)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                expectedId = aisle.ExpectedId,
                type = "Aisle",
                code = aisle.Code,
                parentExpectedId = aisle.ZoneExpectedId,
                sourceRefs = new
                {
                    layer = "AISLE",
                    handle = (handle++).ToString("X"),
                },
                geometry = new
                {
                    kind = "Line",
                    aisle.StartXmm,
                    aisle.StartYmm,
                    aisle.EndXmm,
                    aisle.EndYmm,
                },
                tolerance = Tolerance("line"),
            }, JsonLineOptions));
        }
        foreach (var rack in dataset.Racks)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                expectedId = rack.ExpectedId,
                type = "Rack",
                code = rack.Code,
                parentExpectedId = rack.AisleExpectedId,
                sourceRefs = new
                {
                    layer = "RACK",
                    handle = (handle++).ToString("X"),
                },
                geometry = new
                {
                    kind = "Box",
                    rack.Xmm,
                    rack.Ymm,
                    rack.Zmm,
                    rack.WidthMm,
                    rack.DepthMm,
                    rack.HeightMm,
                    rack.RotationDegrees,
                },
                attributes = new
                {
                    rack.Columns,
                    rack.Levels,
                    rack.Depths,
                },
                tolerance = Tolerance("box"),
            }, JsonLineOptions));
        }
        foreach (var location in dataset.Locations)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                expectedId = location.ExpectedId,
                type = "Location",
                code = location.Code,
                parentExpectedId = $"rack:{location.RackCode}",
                logicalId = location.LogicalId,
                sourceRefs = new
                {
                    layer = "LOCATION",
                    handle = (handle++).ToString("X"),
                },
                geometry = new
                {
                    kind = "Box",
                    location.Xmm,
                    location.Ymm,
                    location.Zmm,
                    location.WidthMm,
                    location.DepthMm,
                    location.HeightMm,
                },
                attributes = new
                {
                    location.FloorCode,
                    location.ZoneCode,
                    location.AisleCode,
                    location.RackCode,
                    location.Column,
                    location.Level,
                    location.Depth,
                    location.IsActive,
                },
                tolerance = Tolerance("box"),
            }, JsonLineOptions));
        }
        return string.Join('\n', lines) + "\n";
    }

    private static object Tolerance(string kind) =>
        new
        {
            kind,
            pointDistanceMm = 1m,
            angleDegrees = 0.1m,
            polygonIou = 0.98m,
            areaRelativeError = 0.001m,
        };

    private static string CreateLocationsCsv(
        SpaceStandardWarehouseDataset dataset)
    {
        var rows = new List<string>(dataset.Locations.Count + 1)
        {
            "ExpectedId,LogicalId,LocationCode,FloorCode,FloorLevel,ZoneCode,"
            + "ZoneType,AisleCode,RackCode,Column,Level,Depth,Xmm,Ymm,Zmm,"
            + "WidthMm,DepthMm,HeightMm,IsActive,DataSource",
        };
        rows.AddRange(dataset.Locations.Select(location => Csv(
            location.ExpectedId,
            location.LogicalId,
            location.Code,
            location.FloorCode,
            location.FloorLevel,
            location.ZoneCode,
            location.ZoneType,
            location.AisleCode,
            location.RackCode,
            location.Column,
            location.Level,
            location.Depth,
            location.Xmm,
            location.Ymm,
            location.Zmm,
            location.WidthMm,
            location.DepthMm,
            location.HeightMm,
            location.IsActive,
            "Simulated")));
        return string.Join('\n', rows) + "\n";
    }

    private static string CreateUnknownLayerDxf() =>
        JoinLines(
            "0", "SECTION", "2", "HEADER",
            "9", "$INSUNITS", "70", "4",
            "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "LINE", "5", "BAD1", "8", "MYSTERY_RACK_LAYER",
            "10", "1000", "20", "1000", "11", "5000", "21", "1000",
            "0", "ENDSEC", "0", "EOF");

    private static void AddRectangle(
        ICollection<string> lines,
        int handle,
        string layer,
        decimal x,
        decimal y,
        decimal width,
        decimal depth)
    {
        lines.Add("0");
        lines.Add("LWPOLYLINE");
        lines.Add("5");
        lines.Add(handle.ToString("X"));
        lines.Add("8");
        lines.Add(layer);
        lines.Add("90");
        lines.Add("4");
        lines.Add("70");
        lines.Add("1");
        AddVertex(lines, x, y);
        AddVertex(lines, x + width, y);
        AddVertex(lines, x + width, y + depth);
        AddVertex(lines, x, y + depth);
    }

    private static void AddLine(
        ICollection<string> lines,
        int handle,
        string layer,
        decimal x1,
        decimal y1,
        decimal x2,
        decimal y2)
    {
        lines.Add("0");
        lines.Add("LINE");
        lines.Add("5");
        lines.Add(handle.ToString("X"));
        lines.Add("8");
        lines.Add(layer);
        lines.Add("10");
        lines.Add(Invariant(x1));
        lines.Add("20");
        lines.Add(Invariant(y1));
        lines.Add("11");
        lines.Add(Invariant(x2));
        lines.Add("21");
        lines.Add(Invariant(y2));
    }

    private static void AddVertex(
        ICollection<string> lines,
        decimal x,
        decimal y)
    {
        lines.Add("10");
        lines.Add(Invariant(x));
        lines.Add("20");
        lines.Add(Invariant(y));
    }

    private static async Task WritePngAsync(string path, int floorLevel)
    {
        const int width = 1_024;
        const int height = 512;
        var pixels = new byte[height * (1 + (width * 4))];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + (width * 4));
            pixels[row] = 0;
            for (var x = 0; x < width; x++)
                SetPixel(pixels, width, x, y, 248, 250, 252, 255);
        }
        for (var aisle = 0; aisle < 10; aisle++)
        {
            var x = 80 + (aisle * 90);
            FillRect(
                pixels,
                width,
                height,
                x,
                70,
                46,
                360,
                floorLevel == 1 ? (byte)37 : (byte)15,
                floorLevel == 1 ? (byte)99 : (byte)118,
                floorLevel == 1 ? (byte)235 : (byte)110,
                255);
        }
        FillRect(pixels, width, height, 20, 20, 8, 8, 220, 38, 38, 255);
        FillRect(pixels, width, height, 996, 20, 8, 8, 220, 38, 38, 255);
        FillRect(pixels, width, height, 20, 484, 8, 8, 220, 38, 38, 255);

        await using var compressed = new MemoryStream();
        await using (var zlib = new ZLibStream(
            compressed,
            CompressionLevel.Optimal,
            leaveOpen: true))
        {
            await zlib.WriteAsync(pixels);
        }
        await using var png = new MemoryStream();
        await png.WriteAsync(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        await File.WriteAllBytesAsync(path, png.ToArray());
    }

    private static void SetPixel(
        byte[] pixels,
        int width,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        var index = (y * (1 + (width * 4))) + 1 + (x * 4);
        pixels[index] = red;
        pixels[index + 1] = green;
        pixels[index + 2] = blue;
        pixels[index + 3] = alpha;
    }

    private static void FillRect(
        byte[] pixels,
        int width,
        int height,
        int left,
        int top,
        int rectWidth,
        int rectHeight,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        for (var y = top; y < Math.Min(top + rectHeight, height); y++)
            for (var x = left; x < Math.Min(left + rectWidth, width); x++)
                SetPixel(pixels, width, x, y, red, green, blue, alpha);
    }

    private static void WriteChunk(
        Stream stream,
        string type,
        byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcMaterial = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcMaterial, 0);
        data.CopyTo(crcMaterial, typeBytes.Length);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcMaterial));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xffffffffu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        return ~crc;
    }

    private static byte[] CreatePdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] "
            + "/Contents 4 0 R /Resources << /Font << /F1 7 0 R >> >> >>",
            StreamObject(PdfPageContent(1)),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] "
            + "/Contents 6 0 R /Resources << /Font << /F1 7 0 R >> >> >>",
            StreamObject(PdfPageContent(2)),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n%CP6\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(
                output,
                $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Length + 1}\n");
        WriteAscii(output, "0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++)
            WriteAscii(output, $"{offsets[index]:0000000000} 00000 n \n");
        WriteAscii(
            output,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\n"
            + $"startxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static string PdfPageContent(int floorLevel)
    {
        var color = floorLevel == 1 ? "0.15 0.39 0.92" : "0.06 0.46 0.43";
        var builder = new StringBuilder()
            .Append("BT /F1 20 Tf 40 550 Td (CP6 Space Standard Warehouse - Floor ")
            .Append(floorLevel)
            .Append(" - Simulated) Tj ET\n")
            .Append(color)
            .Append(" rg\n");
        for (var aisle = 0; aisle < 10; aisle++)
            builder.Append(55 + (aisle * 75)).Append(" 80 36 420 re f\n");
        builder.Append("1 0 0 rg 35 45 8 8 re f 799 45 8 8 re f 35 530 8 8 re f\n");
        return builder.ToString();
    }

    private static string StreamObject(string content) =>
        $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n"
        + $"{content}endstream";

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }

    private static string Csv(params object?[] values) =>
        string.Join(
            ',',
            values.Select(value =>
            {
                var text = value switch
                {
                    null => string.Empty,
                    IFormattable formattable => formattable.ToString(
                        null,
                        System.Globalization.CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty,
                };
                return text.Contains('"') ||
                       text.Contains(',') ||
                       text.Contains('\r') ||
                       text.Contains('\n')
                    ? $"\"{text.Replace("\"", "\"\"")}\""
                    : text;
            }));

    private static string Invariant(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string JoinLines(params string[] lines) =>
        string.Join('\n', lines) + "\n";

    private static async Task WriteTextAsync(string path, string content) =>
        await File.WriteAllTextAsync(
            path,
            content.ReplaceLineEndings("\n"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static async Task WriteJsonAsync<T>(string path, T value) =>
        await WriteTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonOptions) + "\n");

    private static string ComputeFileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static string Required(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        if (index < 0 || index + 1 >= args.Length ||
            string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"Required option missing: {option}");
        }
        return args[index + 1];
    }
}
