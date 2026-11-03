using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;

namespace CP6.Core.Services.Oa;

// Caller input boundary. Published schema parsing and current permission acquisition belong
// to their Owners; this component preserves JSON numbers and absent/null/empty distinctions.
internal static class PersonalFormValuesV2
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static ScopeAuthorizationExceptionV1 Error(string code) => new(code, 422);
    internal static string? Title(string? title)
    {
        title = title?.Trim();
        if (title?.Length > 200 || title?.Contains('\0') == true) throw Error("FORM_TITLE_INVALID");
        return string.IsNullOrEmpty(title) ? null : title;
    }
    internal static FormSchema Schema(string text) => JsonSerializer.Deserialize<FormSchema>(text, Json)!;
    internal static JsonNode? SchemaProperty(JsonObject value,string name)
        =>value.FirstOrDefault(x=>string.Equals(x.Key,name,StringComparison.OrdinalIgnoreCase)).Value;
    internal static string Initial(string schema)
    {
        var output=new JsonObject();
        foreach(var field in SchemaProperty(JsonNode.Parse(schema)!.AsObject(),"fields")!.AsArray())
        {
            var value=field!.AsObject();var property=value.FirstOrDefault(x=>string.Equals(x.Key,"default",StringComparison.OrdinalIgnoreCase));
            if(property.Key!=null)output[SchemaProperty(value,"name")!.GetValue<string>()]=property.Value?.DeepClone();
        }
        return output.ToJsonString();
    }
    internal static string Merge(FormSchema schema, JsonElement input, string? original, IReadOnlyDictionary<string, string> mask)
    {
        Shape(input);
        var fields = schema.Fields.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var result = original == null ? new JsonObject() : JsonNode.Parse(original)!.AsObject();
        foreach (var property in input.EnumerateObject())
        {
            if (!fields.ContainsKey(property.Name)) throw Error("FORM_FIELD_UNKNOWN");
            var next = JsonNode.Parse(property.Value.GetRawText());
            if (mask[property.Name] == "hidden") throw new ScopeAuthorizationExceptionV1("FIELD_WRITE_FORBIDDEN", 403);
            if (mask[property.Name] != "edit" && !JsonNode.DeepEquals(result[property.Name], next))
                throw new ScopeAuthorizationExceptionV1("FIELD_WRITE_FORBIDDEN", 403);
            if (mask[property.Name] == "edit") result[property.Name] = next;
        }
        // Payload is the visible editable snapshot. Omission removes only editable fields;
        // hidden/readonly persisted values cannot be erased by a projected client response.
        foreach (var name in fields.Keys.Where(n => mask[n] == "edit" && !input.TryGetProperty(n, out _))) result.Remove(name);
        using var merged = JsonDocument.Parse(result.ToJsonString());
        Validate(schema, merged.RootElement, partial: true);
        return result.ToJsonString();
    }
    internal static string Patch(FormSchema schema,JsonElement input,string original,IReadOnlyDictionary<string,string> mask)
    {
        Shape(input);var fields=schema.Fields.ToDictionary(f=>f.Name,StringComparer.Ordinal);var result=JsonNode.Parse(original)!.AsObject();
        foreach(var field in input.EnumerateObject())
        {
            if(!fields.ContainsKey(field.Name))throw Error("FORM_FIELD_UNKNOWN");
            if(mask[field.Name]!="edit")throw new ScopeAuthorizationExceptionV1("FIELD_WRITE_FORBIDDEN",403);
            result[field.Name]=JsonNode.Parse(field.Value.GetRawText());
        }
        // Omitted keys stay unchanged; an explicit JSON null stays null. No whole-object replacement.
        using var merged=JsonDocument.Parse(result.ToJsonString());Validate(schema,merged.RootElement,partial:true);return result.ToJsonString();
    }
    internal static void Shape(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Count() > 500
            || Encoding.UTF8.GetByteCount(data.GetRawText()) > 1024 * 1024 || Depth(data) > 8) throw Error("FORM_DATA_INVALID");
    }
    private static int Depth(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => 1 + value.EnumerateObject().Select(x => Depth(x.Value)).DefaultIfEmpty(0).Max(),
        JsonValueKind.Array => 1 + value.EnumerateArray().Select(Depth).DefaultIfEmpty(0).Max(),
        _ => 0
    };
    internal static void Validate(FormSchema schema, JsonElement data, bool partial)
    {
        Shape(data); var fields = schema.Fields.ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
        {
            if (!fields.TryGetValue(property.Name, out var field)) throw Error("FORM_FIELD_UNKNOWN");
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.Null) continue;
            var valid = field.Type.ToLowerInvariant() switch
            {
                "number" => value.ValueKind == JsonValueKind.Number,
                "checkbox" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    || value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(x => x.ValueKind is JsonValueKind.String or JsonValueKind.Number),
                "select" or "radio" => value.ValueKind is JsonValueKind.String or JsonValueKind.Number,
                "user" or "dept" => value.ValueKind == JsonValueKind.String
                    || value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String),
                "upload" => throw Error("FORM_FILE_SOURCE_REQUIRED"),
                "table" => FormDataValidator.IsValidDraftValue(field, value),
                _ => value.ValueKind == JsonValueKind.String
            };
            if (!valid) throw Error("FORM_FIELD_TYPE_INVALID");
            if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > (field.MaxLength ?? 10000)) throw Error("FORM_FIELD_LENGTH_INVALID");
        }
        if (!partial && FormDataValidator.ValidateFields(schema, data, null, null).Count != 0) throw Error("FORM_VALIDATION_FAILED");
    }
    internal static (string Data, string[] Errors, HashSet<string> Hidden) Recompute(FormSchema schema, string original, bool validateShape = true)
    {
        var data = JsonNode.Parse(original)!.AsObject();
        var names = schema.Fields.Select(f => f.Name).ToArray();
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        var required = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var rule in schema.Rules)
        {
            var snapshot = JsonSerializer.SerializeToElement(data);
            if (!ApprovalExpressionV2.FormCondition(rule.When, snapshot, names)) continue;
            foreach (var effect in rule.Then)
            {
                switch (effect.Action)
                {
                    case "hide": hidden.Add(effect.Target); break;
                    case "show": hidden.Remove(effect.Target); break;
                    case "require": required[effect.Target] = true; break;
                    case "optional": required[effect.Target] = false; break;
                    case "compute":
                        data[effect.Target] = JsonNode.Parse(ApprovalExpressionV2.FormValue(effect.Expr!, JsonSerializer.SerializeToElement(data), names).GetRawText());
                        break;
                }
            }
        }
        var output = data.ToJsonString(); using var doc = JsonDocument.Parse(output);
        if(validateShape) Validate(schema, doc.RootElement, partial: true);
        return (output, FormDataValidator.ValidateFields(schema, doc.RootElement, required, hidden).ToArray(), hidden);
    }
    internal static string Project(string data, IReadOnlyDictionary<string, string> mask)
    {
        var value = JsonNode.Parse(data)!.AsObject();
        foreach (var key in value.Select(x => x.Key).Where(x => mask.GetValueOrDefault(x, "hidden") == "hidden").ToArray()) value.Remove(key);
        return value.ToJsonString();
    }
}
