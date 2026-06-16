using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 表单引擎服务（OA 章02 / 08）。SchemaJson/DataJson 直存 JSON 列；提交走服务端 schema 复核
/// （前端校验不可信，后端为准）。改版只升 FormDef.Version，旧 FormData 不动。
/// </summary>
public class FormService : IFormService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    // 复算回写序列化：保留非 ASCII（中文不转义为 \uXXXX），与原 dataJson 直存行为一致
    private static readonly JsonSerializerOptions WriteOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly CP6Context _db;
    public FormService(CP6Context db) => _db = db;

    public async Task<Guid> SaveDefAsync(string formKey, string formName, string schemaJson, string? user = null)
    {
        if (string.IsNullOrWhiteSpace(formKey)) throw new InvalidOperationException("FormKey 不能为空");

        var def = await _db.Wf_FormDefs.FirstOrDefaultAsync(x => x.FormKey == formKey);
        if (def == null)
        {
            def = new Wf_FormDef
            {
                Id = Guid.NewGuid(),
                FormKey = formKey,
                FormName = formName,
                SchemaJson = schemaJson,
                Version = 1,
                Creator = user,
            };
            _db.Wf_FormDefs.Add(def);
        }
        else
        {
            if (def.SchemaJson != schemaJson) def.Version++;   // 仅 schema 变更才升版
            def.FormName = formName;
            def.SchemaJson = schemaJson;
            def.Modifier = user;
            def.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        return def.Id;
    }

    public Task<Wf_FormDef?> GetDefAsync(string formKey) =>
        _db.Wf_FormDefs.FirstOrDefaultAsync(x => x.FormKey == formKey);

    public async Task<Guid> SubmitDataAsync(string formKey, string? bizId, string dataJson, string? user = null)
    {
        var def = await _db.Wf_FormDefs.FirstOrDefaultAsync(x => x.FormKey == formKey && x.Enable)
                  ?? throw new InvalidOperationException($"表单定义不存在或已停用：{formKey}");

        // 服务端复算（章06 §6 铁律：前端体验、后端为准）——按 rules 重算 compute、按生效 required/可见复核
        var (recomputed, errors) = RecomputeAndValidate(def.SchemaJson, dataJson);
        if (errors.Count > 0) throw new InvalidOperationException("表单校验失败：" + string.Join("；", errors));

        var data = new Wf_FormData
        {
            Id = Guid.NewGuid(),
            FormKey = formKey,
            FormVersion = def.Version,
            BizId = bizId,
            DataJson = recomputed,   // 存服务端复算后的数据（compute 以后端为准）
            Creator = user,
        };
        _db.Wf_FormDatas.Add(data);
        await _db.SaveChangesAsync();
        return data.Id;
    }

    public IReadOnlyList<string> ValidateData(string schemaJson, string dataJson)
    {
        FormSchema? schema;
        try { schema = JsonSerializer.Deserialize<FormSchema>(schemaJson, JsonOpts); }
        catch (JsonException) { return new[] { "表单 schema 解析失败" }; }
        if (schema?.Fields is not { Count: > 0 }) return Array.Empty<string>();   // 无字段定义 → 不拦

        JsonElement data;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson);
            data = doc.RootElement.Clone();
        }
        catch (JsonException) { return new[] { "表单数据解析失败" }; }

        return ValidateFields(schema, data, requiredOverride: null, hidden: null);
    }

    /// <summary>
    /// 服务端规则复算 + 复核（章06 §6，与前端 ruleEngine 同语义）。对命中的规则做<b>单轮前向</b>处理：
    /// compute 计算回写到数据、require/optional 改生效必填、show/hide 改可见（隐藏字段免校验）。
    /// 返回 (复算后的 dataJson, 错误清单)。compute 以后端为准，落库用复算结果。
    /// </summary>
    public (string dataJson, IReadOnlyList<string> errors) RecomputeAndValidate(string schemaJson, string dataJson)
    {
        FormSchema? schema;
        try { schema = JsonSerializer.Deserialize<FormSchema>(schemaJson, JsonOpts); }
        catch (JsonException) { return (dataJson, new[] { "表单 schema 解析失败" }); }
        if (schema?.Fields is not { Count: > 0 }) return (dataJson, Array.Empty<string>());

        JsonObject obj;
        try { obj = JsonNode.Parse(string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return (dataJson, new[] { "表单数据解析失败" }); }

        // 生效必填/可见初值取静态 schema；vars 为求值取值源（标量）
        var required = schema.Fields.ToDictionary(f => f.Name, f => f.Required, StringComparer.Ordinal);
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        var vars = ExpressionEvaluator.ParseVars(obj.ToJsonString());

        foreach (var rule in schema.Rules)
        {
            if (!ExpressionEvaluator.Evaluate(rule.When, vars)) continue;
            foreach (var eff in rule.Then)
            {
                switch (eff.Action)
                {
                    case "require":  required[eff.Target] = true; break;
                    case "optional": required[eff.Target] = false; break;
                    case "show":     hidden.Remove(eff.Target); break;
                    case "hide":     hidden.Add(eff.Target); break;
                    case "compute":
                        var v = ExpressionEvaluator.Compute(eff.Expr, vars);
                        if (v is not null) { obj[eff.Target] = ToNode(v); vars[eff.Target] = v; }   // 写回数据 + 供后续规则
                        break;
                    // disable/enable/setOptions 为前端表现，后端复核不关心
                }
            }
        }

        JsonElement data;
        using (var doc = JsonDocument.Parse(obj.ToJsonString())) data = doc.RootElement.Clone();
        var errors = ValidateFields(schema, data, required, hidden);
        return (obj.ToJsonString(WriteOpts), errors);   // 保留中文不转义
    }

    private static JsonNode? ToNode(object? v) => v switch
    {
        double d => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        _ => null,
    };

    /// <summary>字段静态复核（required/类型/长度/正则）。<paramref name="requiredOverride"/> 覆盖字段必填，
    /// <paramref name="hidden"/> 中字段全部跳过（隐藏字段免校验）。</summary>
    private static List<string> ValidateFields(FormSchema schema, JsonElement data,
        IReadOnlyDictionary<string, bool>? requiredOverride, IReadOnlySet<string>? hidden)
    {
        var errors = new List<string>();
        foreach (var f in schema.Fields)
        {
            if (hidden is not null && hidden.Contains(f.Name)) continue;   // 隐藏字段免校验

            JsonElement v = default;
            bool has = data.ValueKind == JsonValueKind.Object && data.TryGetProperty(f.Name, out v);
            bool empty = !has
                         || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                         || (v.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(v.GetString()));

            bool req = requiredOverride is not null && requiredOverride.TryGetValue(f.Name, out var r) ? r : f.Required;
            var label = string.IsNullOrEmpty(f.Label) ? f.Name : f.Label;
            if (req && empty) { errors.Add($"{label} 必填"); continue; }
            if (empty) continue;   // 非必填且空 → 跳过类型校验

            switch (f.Type)
            {
                case "number":
                    if (v.ValueKind != JsonValueKind.Number) errors.Add($"{label} 必须是数字");
                    break;
                case "checkbox":
                    break;   // 允许 bool / 数组，阶段1 不深校
                default:
                    if (v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString() ?? string.Empty;
                        if (f.MaxLength is int max && s.Length > max) errors.Add($"{label} 超出最大长度 {max}");
                        if (!string.IsNullOrEmpty(f.Pattern) && !Regex.IsMatch(s, f.Pattern)) errors.Add($"{label} 格式不符");
                    }
                    break;
            }
        }
        return errors;
    }
}
