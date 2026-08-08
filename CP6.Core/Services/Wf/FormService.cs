using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var draft = await SaveDraftAsync(formKey, formName, schemaJson, null, user);
        await PublishAsync(formKey, draft.RowVersion, Guid.Empty);
        return draft.DefinitionId;
    }

    public async Task<Wf_FormDef?> GetDefAsync(string formKey)
    {
        var head = await _db.Wf_FormDefs.AsNoTracking().FirstOrDefaultAsync(x => x.FormKey == formKey);
        if (head == null) return null;
        var published = await _db.Wf_FormDefVersions.AsNoTracking()
            .Where(x => x.FormDefId == head.Id && x.Status == WfDefinitionVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (published != null)
        {
            head.FormName = published.FormNameSnapshot;
            head.SchemaJson = published.SchemaJson;
            head.Version = published.Version;
        }
        return head;
    }

    public async Task<DefinitionDraftDto?> GetDraftAsync(string formKey, bool createIfMissing = true, string? user = null)
    {
        var head = await _db.Wf_FormDefs.FirstOrDefaultAsync(x => x.FormKey == formKey);
        if (head == null) return null;
        var draft = await _db.Wf_FormDefVersions
            .SingleOrDefaultAsync(x => x.FormDefId == head.Id && x.Status == WfDefinitionVersionStatus.Draft);
        if (draft == null && createIfMissing)
        {
            var latest = await _db.Wf_FormDefVersions.AsNoTracking()
                .Where(x => x.FormDefId == head.Id && x.Status == WfDefinitionVersionStatus.Published)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
            draft = new Wf_FormDefVersion
            {
                Id = Guid.NewGuid(), FormDefId = head.Id, Version = (latest?.Version ?? 0) + 1,
                Status = WfDefinitionVersionStatus.Draft,
                FormNameSnapshot = latest?.FormNameSnapshot ?? head.FormName,
                SchemaJson = latest?.SchemaJson ?? head.SchemaJson, Creator = user
            };
            _db.Wf_FormDefVersions.Add(draft);
            await _db.SaveChangesAsync();
        }
        return draft == null ? null : ToDraft(head.Id, draft);
    }

    public async Task<DefinitionDraftDto> SaveDraftAsync(
        string formKey, string formName, string schemaJson, byte[]? rowVersion, string? user = null)
    {
        if (string.IsNullOrWhiteSpace(formKey)) throw new InvalidOperationException("FormKey 不能为空");
        var head = await _db.Wf_FormDefs.FirstOrDefaultAsync(x => x.FormKey == formKey);
        if (head == null)
        {
            head = new Wf_FormDef
            {
                Id = Guid.NewGuid(), FormKey = formKey, FormName = formName,
                SchemaJson = schemaJson, Version = 1, Creator = user
            };
            _db.Wf_FormDefs.Add(head);
        }
        else
        {
            head.FormName = formName;
            head.Modifier = user;
            head.ModifyDate = DateTime.UtcNow;
        }

        var draft = await _db.Wf_FormDefVersions
            .SingleOrDefaultAsync(x => x.FormDefId == head.Id && x.Status == WfDefinitionVersionStatus.Draft);
        if (draft == null)
        {
            var maxVersion = await _db.Wf_FormDefVersions.Where(x => x.FormDefId == head.Id)
                .Select(x => (int?)x.Version).MaxAsync() ?? 0;
            draft = new Wf_FormDefVersion
            {
                Id = Guid.NewGuid(), FormDefId = head.Id, Version = maxVersion + 1,
                Status = WfDefinitionVersionStatus.Draft, FormNameSnapshot = formName,
                SchemaJson = schemaJson, Creator = user
            };
            _db.Wf_FormDefVersions.Add(draft);
        }
        else
        {
            EnsureRowVersion(draft.RowVersion, rowVersion);
            if (rowVersion != null) _db.Entry(draft).Property(x => x.RowVersion).OriginalValue = rowVersion;
            draft.FormNameSnapshot = formName;
            draft.SchemaJson = schemaJson;
            draft.Modifier = user;
            draft.ModifyDate = DateTime.UtcNow;
        }
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("E-WF-045"); }
        return ToDraft(head.Id, draft);
    }

    public async Task<DefinitionPublishResult> PublishAsync(
        string formKey, byte[]? rowVersion, Guid publishedBy, CancellationToken ct = default)
    {
        var head = await _db.Wf_FormDefs.SingleOrDefaultAsync(x => x.FormKey == formKey, ct)
                   ?? throw new InvalidOperationException("E-WF-036");
        var draft = await _db.Wf_FormDefVersions
            .SingleOrDefaultAsync(x => x.FormDefId == head.Id && x.Status == WfDefinitionVersionStatus.Draft, ct)
                    ?? throw new InvalidOperationException("E-WF-036");
        EnsureRowVersion(draft.RowVersion, rowVersion);
        if (rowVersion != null) _db.Entry(draft).Property(x => x.RowVersion).OriginalValue = rowVersion;

        FormSchema? schema;
        try { schema = JsonSerializer.Deserialize<FormSchema>(draft.SchemaJson, JsonOpts); }
        catch (JsonException) { throw new InvalidOperationException("E-WF-036"); }
        if (schema == null || FormDataValidator.ValidateSchema(schema).Count > 0)
            throw new InvalidOperationException("E-WF-036");
        await new FlowFormCompatibilityValidator(_db).ValidateFormPublishAsync(head.Id, draft.SchemaJson, ct);

        var at = DateTime.UtcNow;
        draft.Status = WfDefinitionVersionStatus.Published;
        draft.PublishedAtUtc = at;
        draft.PublishedBy = publishedBy;
        head.FormName = draft.FormNameSnapshot;
        head.SchemaJson = draft.SchemaJson;
        head.Version = draft.Version;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("E-WF-045"); }
        return new(head.Id, draft.Id, draft.Version, at);
    }

    public async Task<IReadOnlyList<DefinitionVersionItem>> ListVersionsAsync(string formKey, CancellationToken ct = default)
    {
        var headId = await _db.Wf_FormDefs.Where(x => x.FormKey == formKey)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (headId == null) return Array.Empty<DefinitionVersionItem>();
        return await _db.Wf_FormDefVersions.AsNoTracking().Where(x => x.FormDefId == headId)
            .OrderByDescending(x => x.Version)
            .Select(x => new DefinitionVersionItem(x.Id, x.Version, x.Status, x.FormNameSnapshot, x.PublishedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<DefinitionVersionDto?> GetVersionAsync(string formKey, int version, CancellationToken ct = default)
    {
        var row = await (from head in _db.Wf_FormDefs.AsNoTracking()
                         join item in _db.Wf_FormDefVersions.AsNoTracking() on head.Id equals item.FormDefId
                         where head.FormKey == formKey && item.Version == version
                         select new { head.Id, Item = item }).SingleOrDefaultAsync(ct);
        return row == null ? null : new(row.Id, row.Item.Id, row.Item.Version, row.Item.Status,
            row.Item.FormNameSnapshot, row.Item.SchemaJson, row.Item.PublishedAtUtc);
    }

    public async Task<Guid> SubmitDataAsync(string formKey, string? bizId, string dataJson, string? user = null)
    {
        var def = await _db.Wf_FormDefs.FirstOrDefaultAsync(x => x.FormKey == formKey && x.Enable)
                  ?? throw new InvalidOperationException($"表单定义不存在或已停用：{formKey}");
        var version = await _db.Wf_FormDefVersions
            .Where(x => x.FormDefId == def.Id && x.Status == WfDefinitionVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("E-WF-036");

        // 服务端复算（章06 §6 铁律：前端体验、后端为准）——按 rules 重算 compute、按生效 required/可见复核
        var (recomputed, errors) = RecomputeAndValidate(version.SchemaJson, dataJson);
        if (errors.Count > 0) throw new InvalidOperationException("表单校验失败：" + string.Join("；", errors));

        var data = new Wf_FormData
        {
            Id = Guid.NewGuid(),
            FormDefVersionId = version.Id,
            FormKey = formKey,
            FormVersion = version.Version,
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

        return FormDataValidator.ValidateFields(schema, data, requiredOverride: null, hidden: null);
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
        var errors = FormDataValidator.ValidateFields(schema, data, required, hidden);
        return (obj.ToJsonString(WriteOpts), errors);   // 保留中文不转义
    }

    private static JsonNode? ToNode(object? v) => v switch
    {
        double d => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        _ => null,
    };

    private static DefinitionDraftDto ToDraft(Guid defId, Wf_FormDefVersion draft) =>
        new(defId, draft.Id, draft.Version, draft.FormNameSnapshot, draft.SchemaJson, draft.RowVersion, draft.Status);

    private static void EnsureRowVersion(byte[]? current, byte[]? expected)
    {
        if (expected != null && current != null && !current.SequenceEqual(expected))
            throw new InvalidOperationException("E-WF-045");
    }
}
