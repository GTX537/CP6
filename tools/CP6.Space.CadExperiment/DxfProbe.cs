using System.Globalization;

namespace CP6.Space.CadExperiment;

public sealed record DxfProbeResult(
    bool HasPairedLines,
    bool HasEofMarker,
    string? CadVersion,
    int? InsertionUnitsCode,
    long EntityCount,
    long HandleCount,
    long DuplicateHandleCount,
    IReadOnlyDictionary<string, long> EntityTypeCounts,
    IReadOnlyDictionary<string, long> LayerCounts,
    IReadOnlyList<string> Errors);

public static class DxfProbe
{
    public static DxfProbeResult Inspect(string path)
    {
        var errors = new List<string>();
        string? section = null;
        string? pendingHeaderVariable = null;
        string? version = null;
        int? insertionUnits = null;
        var entityTypes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var layers = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long handleCount = 0;
        long duplicateHandleCount = 0;
        long entityCount = 0;
        var inEntity = false;
        var expectingSectionName = false;
        var paired = true;
        var lineNumber = 0;
        (int Code, string Value)? lastPair = null;

        using var reader = new StreamReader(path);
        while (reader.ReadLine() is { } codeLine)
        {
            lineNumber++;
            var valueLine = reader.ReadLine();
            if (valueLine is null)
            {
                paired = false;
                errors.Add("DXF does not contain paired group-code/value lines.");
                break;
            }

            lineNumber++;
            if (!int.TryParse(
                    codeLine.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var code))
            {
                errors.Add($"Invalid group code at line {lineNumber - 1}.");
                continue;
            }

            var pair = (Code: code, Value: valueLine.Trim());
            lastPair = pair;
            if (expectingSectionName)
            {
                section = pair.Code == 2 ? pair.Value.ToUpperInvariant() : null;
                expectingSectionName = false;
                continue;
            }

            if (pair.Code == 0 && pair.Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase))
            {
                expectingSectionName = true;
                inEntity = false;
                continue;
            }

            if (pair.Code == 0 && pair.Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                section = null;
                inEntity = false;
                continue;
            }

            if (section == "HEADER")
            {
                if (pair.Code == 9)
                {
                    pendingHeaderVariable = pair.Value;
                    continue;
                }

                if (pendingHeaderVariable == "$ACADVER" && pair.Code == 1)
                {
                    version = pair.Value;
                    pendingHeaderVariable = null;
                }
                else if (pendingHeaderVariable == "$INSUNITS"
                         && pair.Code == 70
                         && int.TryParse(
                             pair.Value,
                             NumberStyles.Integer,
                             CultureInfo.InvariantCulture,
                             out var units))
                {
                    insertionUnits = units;
                    pendingHeaderVariable = null;
                }
            }

            if (section != "ENTITIES")
            {
                continue;
            }

            if (pair.Code == 0)
            {
                inEntity = !pair.Value.Equals("EOF", StringComparison.OrdinalIgnoreCase)
                    && !pair.Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase);
                if (inEntity)
                {
                    entityCount++;
                    Increment(entityTypes, pair.Value);
                }

                continue;
            }

            if (!inEntity)
            {
                continue;
            }

            if (pair.Code == 5)
            {
                handleCount++;
                if (!handles.Add(pair.Value))
                {
                    duplicateHandleCount++;
                }
            }
            else if (pair.Code == 8)
            {
                Increment(layers, pair.Value);
            }
        }

        var hasEof = lastPair is { Code: 0 }
            && lastPair.Value.Value.Equals("EOF", StringComparison.OrdinalIgnoreCase);
        if (!hasEof)
        {
            errors.Add("DXF does not end with a 0/EOF pair.");
        }

        return new DxfProbeResult(
            paired,
            hasEof,
            version,
            insertionUnits,
            entityCount,
            handleCount,
            duplicateHandleCount,
            entityTypes,
            layers,
            errors);
    }

    private static void Increment(IDictionary<string, long> values, string key)
    {
        values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
    }
}
