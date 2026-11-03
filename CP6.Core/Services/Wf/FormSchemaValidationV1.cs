using System.Text;
using System.Text.Json;
using CP6.Entity.Contracts.Wire;

namespace CP6.Core.Services.Wf;

/// <summary>Authoring boundary only. Validate the executable projection; persist the caller's complete original text.</summary>
public static class FormSchemaValidationV1
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
        { "input", "textarea", "number", "select", "radio", "checkbox", "date", "datetime", "user", "dept", "upload", "table" };
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal)
        { "show", "hide", "require", "optional", "disable", "enable", "setOptions", "compute" };

    public static FormSchema Parse(string text)
    {
        try
        {
            // WFC envelope limits do not apply to a form's passive layout/extension tree.
            using var doc = StrictJson.Parse(Utf8.GetBytes(text));
            var root = Object(doc.RootElement, "$", "fields", "rules");
            var fields = Property(root, "fields");
            if (fields.ValueKind != JsonValueKind.Array) throw Invalid("$.fields", "ARRAY_REQUIRED");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var externalOptions = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var value in fields.EnumerateArray())
            {
                var path = $"$.fields[{index++}]";
                var field = Object(value, path, "name", "type", "required", "label", "maxLength", "pattern", "columns", "minRows", "maxRows", "default", "options", "optionSourceV1");
                var name = Text(Property(field, "name"), path + ".name");
                if (string.IsNullOrWhiteSpace(name) || !names.Add(name)) throw Invalid(path + ".name", "EMPTY_OR_DUPLICATE");
                var typeValue = Property(field, "type");
                var type = typeValue.ValueKind == JsonValueKind.Undefined ? "input" : Text(typeValue, path + ".type");
                if (!Types.Contains(type)) throw Invalid(path + ".type", "UNSUPPORTED_CONTROL");
                ValidateDefault(field, type, path);
                ValidateOptions(Property(field, "options"), path + ".options");
                ValidateOptionSource(field,type,path);
                if(Property(field,"optionSourceV1").ValueKind!=JsonValueKind.Undefined)externalOptions.Add(name);
                if (type.Equals("table",StringComparison.OrdinalIgnoreCase))
                {
                    var columns = Property(field, "columns");
                    if (columns.ValueKind != JsonValueKind.Array) throw Invalid(path + ".columns", "ARRAY_REQUIRED");
                    var columnIndex = 0;
                    foreach (var item in columns.EnumerateArray())
                    {
                        var columnPath = path + $".columns[{columnIndex++}]";
                        var column = Object(item, columnPath, "name", "type", "required", "label", "maxLength", "pattern", "default", "options", "optionSourceV1");
                        var columnType = Property(column, "type");
                        ValidateDefault(column, columnType.ValueKind == JsonValueKind.Undefined ? "input" : Text(columnType, columnPath + ".type"), columnPath);
                        ValidateOptions(Property(column, "options"), columnPath + ".options");
                        ValidateOptionSource(column,columnType.ValueKind==JsonValueKind.Undefined?"input":columnType.GetString()!,columnPath);
                    }
                }
            }
            var rules = Property(root, "rules");
            if (rules.ValueKind != JsonValueKind.Undefined)
            {
                if (rules.ValueKind != JsonValueKind.Array) throw Invalid("$.rules", "ARRAY_REQUIRED");
                var ruleIndex = 0;
                foreach (var item in rules.EnumerateArray())
                {
                    var path = $"$.rules[{ruleIndex++}]";
                    var rule = Object(item, path, "when", "then");
                    var when = Property(rule, "when");
                    if (when.ValueKind != JsonValueKind.Undefined)
                    {
                        var expression = Text(when, path + ".when");
                        if (!string.IsNullOrWhiteSpace(expression) && !ExpressionEvaluator.ValidateSchemaExpression(expression, names, enforceLimits: false))
                            throw Invalid(path + ".when", "EXPRESSION_INVALID");
                    }
                    var effects = Property(rule, "then");
                    if (effects.ValueKind != JsonValueKind.Array) throw Invalid(path + ".then", "ARRAY_REQUIRED");
                    var effectIndex = 0;
                    foreach (var effectValue in effects.EnumerateArray())
                    {
                        var effectPath = path + $".then[{effectIndex++}]";
                        var effect = Object(effectValue, effectPath, "action", "target", "expr", "options");
                        var action = Text(Property(effect, "action"), effectPath + ".action");
                        if (!Actions.Contains(action)) throw Invalid(effectPath + ".action", "UNSUPPORTED_ACTION");
                        if (!names.Contains(Text(Property(effect, "target"), effectPath + ".target"))) throw Invalid(effectPath + ".target", "FIELD_NOT_FOUND");
                        if (action == "compute" && !ExpressionEvaluator.ValidateSchemaExpression(Text(Property(effect, "expr"), effectPath + ".expr"), names, enforceLimits: false))
                            throw Invalid(effectPath + ".expr", "EXPRESSION_INVALID");
                        if (action == "setOptions") ValidateOptions(Property(effect, "options"), effectPath + ".options", required: true);
                        if(action=="setOptions"&&externalOptions.Contains(Text(Property(effect,"target"),effectPath+".target")))throw Invalid(effectPath,"OPTION_SOURCE_CONFLICT");
                    }
                }
            }
            var schema = doc.Deserialize<FormSchema>(Json)!;
            var errors = FormDataValidator.SchemaDiagnostics(schema);
            if (errors.Count != 0) throw new DefinitionDiagnosticExceptionV2("FORM_SCHEMA_INVALID:$.fields:"+string.Join(";",errors.Select(e=>e.Message)),errors.ToArray());
            return schema;
        }
        catch (Exception e) when (e is JsonException or EncoderFallbackException or ArgumentNullException)
        { throw Invalid("$", "JSON_SHAPE"); }
    }

    private static InvalidOperationException Invalid(string path, string code) => new DefinitionDiagnosticExceptionV2($"FORM_SCHEMA_INVALID:{path}:{code}",[new(path,code)]);
    private static JsonElement Object(JsonElement value, string path, params string[] interpreted)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(path, "OBJECT_REQUIRED");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
            if (interpreted.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && !seen.Add(property.Name))
                throw Invalid(path + "." + property.Name, "AMBIGUOUS_PROPERTY");
        return value;
    }
    private static JsonElement Property(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }
    private static string Text(JsonElement value, string path) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw Invalid(path, "STRING_REQUIRED");
    private static void ValidateOptions(JsonElement options, string path, bool required = false)
    {
        if (!required && options.ValueKind == JsonValueKind.Undefined) return;
        if (options.ValueKind != JsonValueKind.Array) throw Invalid(path, "ARRAY_REQUIRED");
        var index = 0;
        foreach (var item in options.EnumerateArray())
        {
            var optionPath=path+$"[{index++}]";
            var option = Object(item, optionPath, "label", "value");
            _ = Text(Property(option, "label"), optionPath + ".label");
            if (Property(option, "value").ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                throw Invalid(optionPath + ".value", "OPTION_VALUE_INVALID");
        }
    }
    private static void ValidateOptionSource(JsonElement field,string type,string path)
    {
        var source=Property(field,"optionSourceV1");if(source.ValueKind==JsonValueKind.Undefined)return;
        var at=path+".optionSourceV1";Object(source,at,"owner","scope","typeId");
        if(type.ToLowerInvariant() is not("select" or "radio" or "checkbox"))throw Invalid(at,"OPTION_CONTROL_REQUIRED");
        if(source.EnumerateObject().Any(p=>!new[]{"owner","scope","typeId"}.Contains(p.Name,StringComparer.OrdinalIgnoreCase))
            ||Property(source,"owner").ValueKind!=JsonValueKind.String||Property(source,"owner").GetString()!=CP6.Core.Services.Sys.FormOptionSourceV1.OwnerName
            ||Property(source,"scope").ValueKind!=JsonValueKind.String||Property(source,"scope").GetString()!="SHARED"
            ||Property(source,"typeId").ValueKind!=JsonValueKind.Number||!Property(source,"typeId").TryGetInt32(out var id)||id<=0)throw Invalid(at,"OPTION_SOURCE_INVALID");
        var options=Property(field,"options");
        if(options.ValueKind!=JsonValueKind.Undefined&&options.GetArrayLength()!=0)throw Invalid(at,"OPTION_SOURCE_CONFLICT");
    }
    private static void ValidateDefault(JsonElement field, string type, string path)
    {
        var value = Property(field, "default");
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        var valid = type.ToLowerInvariant() switch
        {
            "number" => value.ValueKind == JsonValueKind.Number,
            "checkbox" => value.ValueKind is JsonValueKind.Array or JsonValueKind.True or JsonValueKind.False,
            "table" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Object),
            "select" or "radio" => value.ValueKind is JsonValueKind.String or JsonValueKind.Number,
            "user" or "dept" or "upload" => value.ValueKind is JsonValueKind.String or JsonValueKind.Array,
            _ => value.ValueKind == JsonValueKind.String
        };
        if (!valid) throw Invalid(path + ".default", "DEFAULT_TYPE_INVALID");
    }
}
