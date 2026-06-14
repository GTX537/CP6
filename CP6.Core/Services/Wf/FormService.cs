using System.Text.Json;
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

        var errors = ValidateData(def.SchemaJson, dataJson);
        if (errors.Count > 0) throw new InvalidOperationException("表单校验失败：" + string.Join("；", errors));

        var data = new Wf_FormData
        {
            Id = Guid.NewGuid(),
            FormKey = formKey,
            FormVersion = def.Version,
            BizId = bizId,
            DataJson = dataJson,
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

        var errors = new List<string>();
        foreach (var f in schema.Fields)
        {
            JsonElement v = default;
            bool has = data.ValueKind == JsonValueKind.Object && data.TryGetProperty(f.Name, out v);
            bool empty = !has
                         || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                         || (v.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(v.GetString()));

            var label = string.IsNullOrEmpty(f.Label) ? f.Name : f.Label;
            if (f.Required && empty) { errors.Add($"{label} 必填"); continue; }
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
