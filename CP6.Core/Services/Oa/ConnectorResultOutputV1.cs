using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Entity.Contracts.Wire;

namespace CP6.Core.Services.Oa;

internal static class ConnectorResultOutputV1
{
    private static readonly Regex Variable = new(@"\A[A-Za-z][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "scope", "scopeId", "tenant", "tenantId", "environment", "environmentId", "starter", "starterId",
        "definition", "definitionPins", "flowVersion", "flowVersionId", "flowDefVersionId", "pins", "pin",
        "jobId", "tokenId", "instanceId", "nodeId", "actor", "actorId", "authorization", "grants", "permission",
        "permissions", "businessOwner", "owner", "businessState", "businessStatus", "state", "status", "wf", "sys",
    };

    internal static Dictionary<string, object?>? Map(byte[] payload, IReadOnlyList<ConnectorOutputFieldV1>? fields)
    {
        if (fields is null || fields.Count > 64) return null;
        try
        {
            using var document = WorkflowCommandJsonV1.Parse(payload);
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != fields.Count) return null;
            var output = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (!Variable.IsMatch(field.Target) || Reserved.Contains(field.Target) ||
                    Reserved.Contains(field.Source) || !sources.Add(field.Source) ||
                    !value.TryGetProperty(field.Source, out var element) || !Accept(field, element) ||
                    !output.TryAdd(field.Target, element.Clone())) return null;
            }
            return output;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Accept(ConnectorOutputFieldV1 field, JsonElement value) => field.Kind switch
    {
        ConnectorOutputKindV1.Text => value.ValueKind == JsonValueKind.String && field.MaxLength is >= 1 and <= WorkflowCommandJsonV1.MaxDocumentBytes
            && value.GetString()!.Length <= field.MaxLength,
        ConnectorOutputKindV1.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        ConnectorOutputKindV1.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        ConnectorOutputKindV1.Decimal => value.ValueKind == JsonValueKind.Number
            && ExactDecimal(value, field.Precision, field.Scale),
        _ => false,
    };

    // PostgreSQL numeric 列按十进制位数校验。拒绝指数写法及静默四舍五入。
    private static bool ExactDecimal(JsonElement value, int precision, int scale)
    {
        if (precision is < 1 or > 28 || scale < 0 || scale > precision || !value.TryGetDecimal(out _)) return false;
        var raw = value.GetRawText().AsSpan().TrimStart('-');
        if (raw.IndexOfAny('e', 'E') >= 0) return false;
        var point = raw.IndexOf('.');
        var integer = point < 0 ? raw : raw[..point];
        var fraction = point < 0 ? 0 : raw.Length - point - 1;
        return integer.TrimStart('0').Length <= precision - scale && fraction <= scale;
    }
}
