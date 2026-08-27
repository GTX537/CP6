using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment;

public sealed class DevelopmentDxfCadConverter : ICadConverter
{
    public const string ConverterId = "cp6-development-dxf";
    public const string ConverterVersion = "1.1.0";
    public const int MaximumDevelopmentInputBytes = 64 * 1024 * 1024;

    public async Task<SpaceCadConversionResult> ConvertAsync(
        SpaceCadConversionRequest request,
        Stream source,
        ISpaceCadIrSink sink,
        CancellationToken cancellationToken = default)
    {
        SpaceCadConversionContract.ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);
        if (!source.CanRead)
            throw new ArgumentException("The CAD source stream must be readable.", nameof(source));
        if (request.SourceFormat != SpaceCadSourceFormat.Dxf)
        {
            throw new NotSupportedException(
                "The development converter reads DXF only. DWG remains reserved for a licensed adapter.");
        }
        if (!request.ConverterId.Equals(ConverterId, StringComparison.Ordinal)
            || !request.ConverterVersion.Equals(ConverterVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The development converter identity must be {ConverterId}/{ConverterVersion}.");
        }

        var (pairs, actualHash) = await ParsePairsAndHashAsync(
            source,
            cancellationToken);
        if (!actualHash.Equals(request.SourceSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The DXF bytes do not match the requested source SHA-256.");

        var issues = new List<SpaceCadConversionIssueV1>();
        var cadVersion = HeaderValue(pairs, "$ACADVER", 1) ?? "UNKNOWN";
        var unitCode = HeaderValue(pairs, "$INSUNITS", 70);
        var (unit, scale) = Unit(unitCode);
        if (unit == SpaceCadUnit.Unknown)
        {
            issues.Add(new SpaceCadConversionIssueV1(
                "SPACE_CAD_UNIT_UNKNOWN",
                SpaceCadIssueSeverity.Blocking,
                DetailToken: unitCode is null ? "missing" : $"code-{unitCode}"));
        }

        var blockRecords = RecordsInSection(pairs, "BLOCKS");
        var blocks = BuildBlocks(blockRecords);
        var entityRecords = RecordsInSection(pairs, "ENTITIES");
        var entities = BuildEntities(entityRecords, scale, issues);
        var tableRecords = RecordsInSection(pairs, "TABLES");
        var layers = BuildLayers(tableRecords, entities, issues);
        var bounds = UnionBounds(entities.Select(entity => entity.Bounds));
        var document = new SpaceCadIrDocumentV1(
            SpaceCadIrVersions.SchemaVersion,
            actualHash,
            SpaceCadSourceFormat.Dxf,
            cadVersion,
            unit,
            scale,
            SpaceCadIrVersions.CoordinateSystem,
            bounds,
            ConverterId,
            ConverterVersion);
        var summary = new SpaceCadIrSummaryV1(
            layers.LongLength,
            blocks.LongLength,
            entities.LongLength,
            entities.LongCount(entity => entity.IsSupported),
            entities.LongCount(entity => !entity.IsSupported),
            issues.LongCount(issue => issue.Code == "SPACE_CAD_SOURCE_REF_SYNTHESIZED"),
            bounds);

        SpaceCadConversionContract.ValidateDocument(request, document);
        await sink.WriteDocumentAsync(document, cancellationToken);
        foreach (var layer in layers)
            await sink.WriteLayerAsync(layer, cancellationToken);
        foreach (var block in blocks)
            await sink.WriteBlockAsync(block, cancellationToken);
        foreach (var entity in entities)
        {
            SpaceCadConversionContract.ValidateEntity(entity);
            await sink.WriteEntityAsync(entity, cancellationToken);
        }

        var irHash = await sink.CompleteAsync(issues, summary, cancellationToken);
        return new SpaceCadConversionResult(
            actualHash,
            irHash,
            ConverterId,
            ConverterVersion,
            summary,
            issues);
    }

    private static async Task<(IReadOnlyList<DxfPair> Pairs, string Sha256)>
        ParsePairsAndHashAsync(
            Stream source,
            CancellationToken cancellationToken)
    {
        if (source.CanSeek &&
            checked(source.Length - source.Position) > MaximumDevelopmentInputBytes)
        {
            throw InputTooLarge();
        }

        using var bounded = new BoundedHashingReadStream(
            source,
            MaximumDevelopmentInputBytes);
        using var reader = new StreamReader(
            bounded,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 64 * 1024,
            leaveOpen: true);
        var pairs = new List<DxfPair>();
        long lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } groupCodeLine)
        {
            lineNumber++;
            var valueLine = await reader.ReadLineAsync(cancellationToken);
            if (valueLine is null)
            {
                throw new InvalidDataException(
                    "DXF does not contain paired group-code/value lines.");
            }
            lineNumber++;
            if (!int.TryParse(
                    groupCodeLine.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var code))
            {
                throw new InvalidDataException(
                    $"Invalid DXF group code at line {lineNumber - 1}.");
            }
            // Group code 999 is a DXF comment. Validate its UTF-8 bytes but
            // do not retain semantically inert comment text in memory.
            if (code != 999)
                pairs.Add(new DxfPair(code, valueLine.Trim()));
        }
        if (pairs.Count == 0
            || pairs[^1].Code != 0
            || !pairs[^1].Value.Equals("EOF", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DXF does not end with a 0/EOF pair.");
        return (pairs, bounded.CompleteSha256());
    }

    private static InvalidDataException InputTooLarge() =>
        new($"Development DXF input exceeds {MaximumDevelopmentInputBytes} bytes.");

    private sealed class BoundedHashingReadStream(
        Stream inner,
        long maximumBytes) : Stream
    {
        private readonly IncrementalHash _hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _totalBytes;
        private bool _endOfStream;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string CompleteSha256()
        {
            if (!_endOfStream)
                throw new InvalidOperationException("The complete DXF stream was not consumed.");
            return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Record(buffer.AsSpan(offset, read));
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Record(buffer.Span[..read]);
            return read;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken);
            Record(buffer.AsSpan(offset, read));
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _hash.Dispose();
            base.Dispose(disposing);
        }

        private void Record(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                _endOfStream = true;
                return;
            }
            _totalBytes = checked(_totalBytes + bytes.Length);
            if (_totalBytes > maximumBytes)
                throw InputTooLarge();
            _hash.AppendData(bytes);
        }
    }

    private static string? HeaderValue(
        IReadOnlyList<DxfPair> pairs,
        string variable,
        int valueCode)
    {
        var section = SectionPairs(pairs, "HEADER");
        for (var index = 0; index < section.Count - 1; index++)
        {
            if (section[index].Code == 9
                && section[index].Value.Equals(variable, StringComparison.OrdinalIgnoreCase)
                && section[index + 1].Code == valueCode)
                return section[index + 1].Value;
        }
        return null;
    }

    private static (SpaceCadUnit Unit, decimal? Scale) Unit(string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            return (SpaceCadUnit.Unknown, null);
        return code switch
        {
            1 => (SpaceCadUnit.Inch, 25.4m),
            2 => (SpaceCadUnit.Foot, 304.8m),
            4 => (SpaceCadUnit.Millimeter, 1m),
            5 => (SpaceCadUnit.Centimeter, 10m),
            6 => (SpaceCadUnit.Meter, 1000m),
            _ => (SpaceCadUnit.Unknown, null)
        };
    }

    private static IReadOnlyList<DxfPair> SectionPairs(
        IReadOnlyList<DxfPair> pairs,
        string sectionName)
    {
        for (var index = 0; index < pairs.Count - 1; index++)
        {
            if (pairs[index].Code != 0
                || !pairs[index].Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase)
                || pairs[index + 1].Code != 2
                || !pairs[index + 1].Value.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                continue;

            var result = new List<DxfPair>();
            for (index += 2; index < pairs.Count; index++)
            {
                if (pairs[index].Code == 0
                    && pairs[index].Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
                    return result;
                result.Add(pairs[index]);
            }
            throw new InvalidDataException($"DXF section {sectionName} has no ENDSEC marker.");
        }
        return [];
    }

    private static IReadOnlyList<DxfRecord> RecordsInSection(
        IReadOnlyList<DxfPair> pairs,
        string sectionName)
    {
        var section = SectionPairs(pairs, sectionName);
        var records = new List<DxfRecord>();
        string? type = null;
        List<DxfPair>? values = null;
        foreach (var pair in section)
        {
            if (pair.Code == 0)
            {
                if (type is not null && values is not null)
                    records.Add(new DxfRecord(type, values));
                type = pair.Value.ToUpperInvariant();
                values = [];
                continue;
            }
            values?.Add(pair);
        }
        if (type is not null && values is not null)
            records.Add(new DxfRecord(type, values));
        return records;
    }

    private static SpaceCadIrBlockV1[] BuildBlocks(IReadOnlyList<DxfRecord> records)
    {
        var blocks = new List<SpaceCadIrBlockV1>();
        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].Type != "BLOCK")
                continue;
            var block = records[index];
            var name = BoundedToken(block.First(2) ?? $"BLOCK-{blocks.Count + 1}");
            var handle = block.First(5);
            var flags = block.Int(70) ?? 0;
            var entityCount = 0L;
            var cursor = index + 1;
            while (cursor < records.Count && records[cursor].Type != "ENDBLK")
            {
                entityCount++;
                cursor++;
            }
            var xrefPath = block.First(1);
            blocks.Add(new SpaceCadIrBlockV1(
                string.IsNullOrWhiteSpace(handle)
                    ? $"B:{blocks.Count + 1:D8}"
                    : $"H:{handle.ToUpperInvariant()}",
                name,
                (flags & 4) != 0,
                string.IsNullOrWhiteSpace(xrefPath) ? null : HashToken(xrefPath),
                entityCount));
            index = cursor;
        }
        return blocks.OrderBy(block => block.BlockId, StringComparer.Ordinal).ToArray();
    }

    private static SpaceCadIrLayerV1[] BuildLayers(
        IReadOnlyList<DxfRecord> tableRecords,
        IReadOnlyList<SpaceCadIrEntityV1> entities,
        ICollection<SpaceCadConversionIssueV1> issues)
    {
        var counts = entities
            .GroupBy(entity => entity.LayerId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.LongCount(), StringComparer.Ordinal);
        var layers = new Dictionary<string, SpaceCadIrLayerV1>(StringComparer.Ordinal);
        foreach (var record in tableRecords.Where(record => record.Type == "LAYER"))
        {
            var name = BoundedToken(record.First(2) ?? "0");
            var colorIndex = record.Int(62);
            var trueColor = record.Int(420);
            var color = trueColor is { } rgb
                ? $"RGB:#{rgb & 0x00ff_ffff:X6}"
                : colorIndex is { } aci
                    ? $"ACI:{Math.Abs(aci)}"
                    : null;
            var layer = new SpaceCadIrLayerV1(
                name,
                name,
                counts.GetValueOrDefault(name),
                color,
                record.First(6) is { } lineType ? BoundedToken(lineType) : null,
                IsVisible: colorIndex is null or >= 0);
            if (!layers.TryAdd(name, layer))
                throw new InvalidDataException($"Duplicate DXF layer table entry '{name}'.");
        }

        foreach (var (name, count) in counts)
        {
            if (layers.ContainsKey(name))
                continue;
            layers.Add(name, new SpaceCadIrLayerV1(name, name, count));
            issues.Add(new SpaceCadConversionIssueV1(
                "SPACE_CAD_LAYER_METADATA_MISSING",
                SpaceCadIssueSeverity.Warning,
                DetailToken: name));
        }
        return layers.Values
            .OrderBy(layer => layer.LayerId, StringComparer.Ordinal)
            .ToArray();
    }

    private static SpaceCadIrEntityV1[] BuildEntities(
        IReadOnlyList<DxfRecord> records,
        decimal? scale,
        ICollection<SpaceCadConversionIssueV1> issues)
    {
        var result = new List<SpaceCadIrEntityV1>();
        var sourceRefs = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Type is "VERTEX" or "ATTRIB" or "SEQEND")
                continue;

            IReadOnlyList<SpaceCadPointV1>? overridePoints = null;
            IReadOnlyDictionary<string, string>? overrideAttributes = null;
            if (record.Type == "POLYLINE")
            {
                var vertices = new List<SpaceCadPointV1>();
                var cursor = index + 1;
                while (cursor < records.Count && records[cursor].Type == "VERTEX")
                {
                    if (records[cursor].Point(10) is { } point)
                        vertices.Add(Scale(point, scale));
                    cursor++;
                }
                overridePoints = vertices;
                index = cursor < records.Count && records[cursor].Type == "SEQEND"
                    ? cursor
                    : cursor - 1;
            }
            else if (record.Type == "INSERT" && record.Int(66) == 1)
            {
                var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                var cursor = index + 1;
                while (cursor < records.Count && records[cursor].Type == "ATTRIB")
                {
                    var tag = records[cursor].First(2);
                    var value = records[cursor].First(1);
                    if (!string.IsNullOrWhiteSpace(tag) && value is not null)
                    {
                        var key = BoundedToken(tag);
                        if (!attributes.TryAdd(key, BoundedValue(value)))
                        {
                            issues.Add(new SpaceCadConversionIssueV1(
                                "SPACE_CAD_DUPLICATE_ATTRIBUTE_TAG",
                                SpaceCadIssueSeverity.Warning,
                                DetailToken: key));
                        }
                    }
                    cursor++;
                }
                overrideAttributes = attributes;
                index = cursor < records.Count && records[cursor].Type == "SEQEND"
                    ? cursor
                    : cursor - 1;
            }

            var missingHandle = string.IsNullOrWhiteSpace(record.First(5));
            var sourceRef = missingHandle
                ? $"I:{result.Count + 1:D8}:{record.Type}"
                : $"H:{record.First(5)!.ToUpperInvariant()}";
            if (!sourceRefs.Add(sourceRef))
                throw new InvalidDataException($"Duplicate DXF source reference '{sourceRef}'.");
            if (missingHandle)
            {
                issues.Add(new SpaceCadConversionIssueV1(
                    "SPACE_CAD_SOURCE_REF_SYNTHESIZED",
                    SpaceCadIssueSeverity.Warning,
                    sourceRef));
            }

            var entity = ToEntity(
                record,
                sourceRef,
                scale,
                overridePoints,
                overrideAttributes);
            if (!entity.IsSupported)
            {
                issues.Add(new SpaceCadConversionIssueV1(
                    "SPACE_CAD_ENTITY_UNSUPPORTED",
                    SpaceCadIssueSeverity.Warning,
                    sourceRef,
                    entity.RawType));
            }
            result.Add(entity);
        }
        return result.ToArray();
    }

    private static SpaceCadIrEntityV1 ToEntity(
        DxfRecord record,
        string sourceRef,
        decimal? scale,
        IReadOnlyList<SpaceCadPointV1>? overridePoints,
        IReadOnlyDictionary<string, string>? overrideAttributes)
    {
        var closed = record.Type switch
        {
            "LWPOLYLINE" or "POLYLINE" => (record.Int(70) ?? 0) % 2 == 1,
            _ => false
        };
        var (type, supported) = record.Type switch
        {
            "LINE" => (SpaceCadIrEntityType.Line, true),
            "LWPOLYLINE" or "POLYLINE" when closed =>
                (SpaceCadIrEntityType.ClosedPolyline, true),
            "LWPOLYLINE" or "POLYLINE" => (SpaceCadIrEntityType.Polyline, true),
            "CIRCLE" => (SpaceCadIrEntityType.Circle, true),
            "ARC" => (SpaceCadIrEntityType.Arc, true),
            "INSERT" => (SpaceCadIrEntityType.BlockReference, true),
            "TEXT" or "MTEXT" => (SpaceCadIrEntityType.Text, true),
            "HATCH" => (SpaceCadIrEntityType.Hatch, false),
            "SPLINE" => (SpaceCadIrEntityType.Spline, false),
            "ELLIPSE" => (SpaceCadIrEntityType.Ellipse, false),
            "DIMENSION" => (SpaceCadIrEntityType.Dimension, false),
            _ => (SpaceCadIrEntityType.Unknown, false)
        };
        var points = overridePoints ?? Points(record, scale);
        decimal? radius = record.Decimal(40) is { } rawRadius
                          && (record.Type is "CIRCLE" or "ARC")
            ? rawRadius * (scale ?? 1m)
            : null;
        var bounds = Bounds(points, radius);
        var attributes = overrideAttributes is null
            ? Attributes(record)
            : new Dictionary<string, string>(overrideAttributes, StringComparer.Ordinal);
        var transform = record.Type == "INSERT"
            ? InsertTransform(record, scale)
            : SpaceCadAffineTransformV1.Identity;
        return new SpaceCadIrEntityV1(
            sourceRef,
            type,
            record.Type,
            BoundedToken(record.First(8) ?? "0"),
            record.Type == "INSERT" ? BoundedToken(record.First(2) ?? "UNKNOWN") : null,
            points,
            radius,
            record.Type == "ARC" ? record.Decimal(50) : null,
            record.Type == "ARC" ? record.Decimal(51) : null,
            transform,
            bounds,
            closed,
            supported,
            attributes);
    }

    private static IReadOnlyList<SpaceCadPointV1> Points(DxfRecord record, decimal? scale)
    {
        if (record.Type == "LINE")
        {
            return new[] { record.Point(10), record.Point(11) }
                .Where(point => point is not null)
                .Select(point => Scale(point!, scale))
                .ToArray();
        }
        if (record.Type == "LWPOLYLINE")
            return record.RepeatedPoints(10).Select(point => Scale(point, scale)).ToArray();
        if (record.Type is "CIRCLE" or "ARC" or "INSERT" or "TEXT" or "MTEXT")
            return record.Point(10) is { } point ? [Scale(point, scale)] : [];
        return record.RepeatedPoints(10).Select(point => Scale(point, scale)).ToArray();
    }

    private static IReadOnlyDictionary<string, string> Attributes(DxfRecord record)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if ((record.Type is "TEXT" or "MTEXT") && record.First(1) is { } text)
            attributes["text"] = BoundedValue(text);
        return attributes;
    }

    private static SpaceCadAffineTransformV1 InsertTransform(DxfRecord record, decimal? scale)
    {
        var rotation = record.Decimal(50) ?? 0m;
        var radians = decimal.ToDouble(rotation) * Math.PI / 180d;
        var scaleX = record.Decimal(41) ?? 1m;
        var scaleY = record.Decimal(42) ?? 1m;
        var cosine = (decimal)Math.Cos(radians);
        var sine = (decimal)Math.Sin(radians);
        var point = record.Point(10) ?? new SpaceCadPointV1(0, 0, 0);
        point = Scale(point, scale);
        return new SpaceCadAffineTransformV1(
            cosine * scaleX,
            -sine * scaleY,
            sine * scaleX,
            cosine * scaleY,
            point.X,
            point.Y,
            point.Z);
    }

    private static SpaceCadPointV1 Scale(SpaceCadPointV1 point, decimal? scale)
    {
        var factor = scale ?? 1m;
        return new SpaceCadPointV1(point.X * factor, point.Y * factor, point.Z * factor);
    }

    private static SpaceCadBoundsV1? Bounds(
        IReadOnlyList<SpaceCadPointV1> points,
        decimal? radius)
    {
        if (points.Count == 0)
            return null;
        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        if (radius is { } value && points.Count == 1)
        {
            minX -= value;
            minY -= value;
            maxX += value;
            maxY += value;
        }
        return new SpaceCadBoundsV1(minX, minY, maxX, maxY);
    }

    private static SpaceCadBoundsV1? UnionBounds(IEnumerable<SpaceCadBoundsV1?> values)
    {
        var bounds = values.Where(value => value is not null).Cast<SpaceCadBoundsV1>().ToArray();
        return bounds.Length == 0
            ? null
            : new SpaceCadBoundsV1(
                bounds.Min(value => value.MinX),
                bounds.Min(value => value.MinY),
                bounds.Max(value => value.MaxX),
                bounds.Max(value => value.MaxY));
    }

    private static string BoundedToken(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length <= SpaceCadConversionContract.MaximumIdentifierLength)
            return normalized.Length == 0 ? "0" : normalized;
        return $"TOKEN:{HashToken(normalized)}";
    }

    private static string BoundedValue(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= SpaceCadConversionContract.MaximumAttributeValueLength
            ? normalized
            : normalized[..SpaceCadConversionContract.MaximumAttributeValueLength];
    }

    private static string HashToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private sealed record DxfPair(int Code, string Value);

    private sealed record DxfRecord(string Type, IReadOnlyList<DxfPair> Pairs)
    {
        public string? First(int code) =>
            Pairs.FirstOrDefault(pair => pair.Code == code)?.Value;

        public int? Int(int code) =>
            int.TryParse(First(code), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        public decimal? Decimal(int code) =>
            decimal.TryParse(First(code), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        public SpaceCadPointV1? Point(int xCode)
        {
            var x = Decimal(xCode);
            var y = Decimal(xCode + 10);
            if (x is null || y is null)
                return null;
            return new SpaceCadPointV1(x.Value, y.Value, Decimal(xCode + 20) ?? 0);
        }

        public IReadOnlyList<SpaceCadPointV1> RepeatedPoints(int xCode)
        {
            var points = new List<SpaceCadPointV1>();
            for (var index = 0; index < Pairs.Count; index++)
            {
                if (Pairs[index].Code != xCode
                    || !decimal.TryParse(
                        Pairs[index].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var x))
                    continue;
                decimal? y = null;
                decimal z = 0;
                for (var cursor = index + 1; cursor < Pairs.Count; cursor++)
                {
                    if (Pairs[cursor].Code == xCode)
                        break;
                    if (Pairs[cursor].Code == xCode + 10
                        && decimal.TryParse(
                            Pairs[cursor].Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var parsedY))
                        y = parsedY;
                    else if (Pairs[cursor].Code == xCode + 20
                             && decimal.TryParse(
                                 Pairs[cursor].Value,
                                 NumberStyles.Float,
                                 CultureInfo.InvariantCulture,
                                 out var parsedZ))
                        z = parsedZ;
                }
                if (y is not null)
                    points.Add(new SpaceCadPointV1(x, y.Value, z));
            }
            return points;
        }
    }
}

public sealed class DevelopmentCadIrFileSink : ISpaceCadIrSink
{
    private readonly SpaceCadConversionRequest _request;
    private readonly string _outputPath;
    private readonly List<SpaceCadIrLayerV1> _layers = [];
    private readonly List<SpaceCadIrBlockV1> _blocks = [];
    private readonly List<SpaceCadIrEntityV1> _entities = [];
    private SpaceCadIrDocumentV1? _document;

    public DevelopmentCadIrFileSink(
        SpaceCadConversionRequest request,
        string outputPath)
    {
        _request = request;
        _outputPath = outputPath;
    }

    public SpaceCadIrPackageV1? Package { get; private set; }

    public ValueTask WriteDocumentAsync(
        SpaceCadIrDocumentV1 document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_document is not null)
            throw new InvalidOperationException("CAD IR document was already written.");
        SpaceCadConversionContract.ValidateDocument(_request, document);
        _document = document;
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteLayerAsync(
        SpaceCadIrLayerV1 layer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _layers.Add(layer);
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteBlockAsync(
        SpaceCadIrBlockV1 block,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _blocks.Add(block);
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteEntityAsync(
        SpaceCadIrEntityV1 entity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entities.Add(entity);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string> CompleteAsync(
        IReadOnlyList<SpaceCadConversionIssueV1> issues,
        SpaceCadIrSummaryV1 summary,
        CancellationToken cancellationToken = default)
    {
        if (_document is null)
            throw new InvalidOperationException("CAD IR document was not written.");
        if (Package is not null)
            throw new InvalidOperationException("CAD IR sink was already completed.");
        Package = new SpaceCadIrPackageV1(
            _document,
            _layers.ToArray(),
            _blocks.ToArray(),
            _entities.ToArray(),
            issues.ToArray(),
            summary);
        SpaceCadConversionContract.ValidatePackage(_request, Package);
        await CadExperimentJson.WriteAsync(_outputPath, Package, cancellationToken);
        return await DatasetAuditor.ComputeSha256Async(_outputPath, cancellationToken);
    }
}
