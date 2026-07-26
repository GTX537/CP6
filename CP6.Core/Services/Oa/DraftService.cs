using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed class DraftService : IDraftService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;
    private readonly IDefinitionVersionResolver _versions;
    private readonly IFormSubmissionService _submissions;

    public DraftService(CP6Context db, IDefinitionVersionResolver versions, IFormSubmissionService submissions)
    {
        _db = db;
        _versions = versions;
        _submissions = submissions;
    }

    public async Task<DraftDetail> CreateAsync(
        Guid ownerId, string formKey, JsonElement data, string? title, CancellationToken ct = default)
    {
        var form = await _versions.ResolveLatestFormAsync(formKey, ct);
        var normalized = ValidatePartial(data, form.Version!.SchemaJson);
        var draft = new Wf_FormDraft
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            FormDefId = form.Head.Id,
            FormDefVersionId = form.Version.Id,
            DataJson = normalized,
            Title = NormalizeTitle(title),
            Status = WfFormDraftStatus.Active,
            Creator = ownerId.ToString()
        };
        _db.Wf_FormDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);
        return ToDetail(draft, form.Head.FormKey, form.Version.FormNameSnapshot,
            form.Version.Version, form.Version.Version, form.Version.SchemaJson, stale: false);
    }

    public async Task<DraftDetail> UpdateAsync(
        Guid ownerId, Guid draftId, JsonElement data, string? title, byte[]? rowVersion,
        CancellationToken ct = default)
    {
        var loaded = await LoadOwnedActiveAsync(ownerId, draftId, ct);
        EnsureRowVersion(loaded.Draft.RowVersion, rowVersion);
        if (rowVersion != null)
            _db.Entry(loaded.Draft).Property(x => x.RowVersion).OriginalValue = rowVersion;
        loaded.Draft.DataJson = ValidatePartial(data, loaded.Version.SchemaJson);
        loaded.Draft.Title = NormalizeTitle(title);
        loaded.Draft.Modifier = ownerId.ToString();
        loaded.Draft.ModifyDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("E-WF-041"); }
        return ToDetail(loaded.Draft, loaded.Form.FormKey, loaded.Version.FormNameSnapshot,
            loaded.Version.Version, await LatestVersionAsync(loaded.Draft.FormDefId, ct),
            loaded.Version.SchemaJson, await IsStaleAsync(loaded.Draft, ct));
    }

    public async Task<DraftPage> ListAsync(
        Guid ownerId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query =
            from draft in _db.Wf_FormDrafts.AsNoTracking()
            join form in _db.Wf_FormDefs.AsNoTracking() on draft.FormDefId equals form.Id
            join version in _db.Wf_FormDefVersions.AsNoTracking() on draft.FormDefVersionId equals version.Id
            where draft.OwnerUserId == ownerId && draft.Status == WfFormDraftStatus.Active
            let latest = _db.Wf_FormDefVersions
                .Where(x => x.FormDefId == draft.FormDefId && x.Status == WfDefinitionVersionStatus.Published)
                .Max(x => (int?)x.Version)
            select new DraftListItem(
                draft.Id, form.FormKey, version.FormNameSnapshot, version.Version, latest ?? version.Version,
                draft.DataJson, draft.Title, draft.ModifyDate ?? draft.CreateDate,
                latest != version.Version, draft.RowVersion);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<DraftDetail> GetAsync(Guid ownerId, Guid draftId, CancellationToken ct = default)
    {
        var loaded = await LoadOwnedActiveAsync(ownerId, draftId, ct);
        return ToDetail(loaded.Draft, loaded.Form.FormKey, loaded.Version.FormNameSnapshot,
            loaded.Version.Version, await LatestVersionAsync(loaded.Draft.FormDefId, ct),
            loaded.Version.SchemaJson, await IsStaleAsync(loaded.Draft, ct));
    }

    public async Task<DraftRebaseResult> RebaseAsync(
        Guid ownerId, Guid draftId, int targetVersion, bool confirmRemovedValues,
        byte[]? rowVersion, CancellationToken ct = default)
    {
        var loaded = await LoadOwnedActiveAsync(ownerId, draftId, ct);
        EnsureRowVersion(loaded.Draft.RowVersion, rowVersion);
        if (rowVersion != null)
            _db.Entry(loaded.Draft).Property(x => x.RowVersion).OriginalValue = rowVersion;
        var target = await _db.Wf_FormDefVersions
            .Where(x => x.FormDefId == loaded.Draft.FormDefId &&
                        x.Status == WfDefinitionVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("E-WF-036");
        if (target.Version != targetVersion) throw new InvalidOperationException("E-WF-040");

        var sourceSchema = ParseSchema(loaded.Version.SchemaJson);
        var targetSchema = ParseSchema(target.SchemaJson);
        var sourceData = JsonNode.Parse(loaded.Draft.DataJson)?.AsObject() ?? new JsonObject();
        var result = new JsonObject();
        var removed = new List<string>();
        var sourceFields = sourceSchema.Fields.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var targetFields = targetSchema.Fields.ToDictionary(x => x.Name, StringComparer.Ordinal);

        foreach (var field in targetFields.Values)
        {
            if (sourceFields.TryGetValue(field.Name, out var oldField) &&
                Compatible(oldField.Type, field.Type) && sourceData.TryGetPropertyValue(field.Name, out var value))
                result[field.Name] = value?.DeepClone();
            else
                result[field.Name] = DefaultValue(target.SchemaJson, field.Name);
        }
        foreach (var old in sourceFields.Keys)
        {
            if (targetFields.TryGetValue(old, out var current) &&
                Compatible(sourceFields[old].Type, current.Type)) continue;
            if (sourceData.TryGetPropertyValue(old, out var value) && HasValue(value))
                removed.Add(old);
        }

        if (removed.Count > 0 && !confirmRemovedValues)
            throw new DraftRebaseConfirmationException(removed);

        var oldVersionId = loaded.Draft.FormDefVersionId;
        loaded.Draft.FormDefVersionId = target.Id;
        loaded.Draft.RebasedFromVersionId = oldVersionId;
        loaded.Draft.DataJson = result.ToJsonString();
        loaded.Draft.Modifier = ownerId.ToString();
        loaded.Draft.ModifyDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("E-WF-041"); }
        var validation = ValidationErrors(target.SchemaJson, result.ToJsonString());
        return new(loaded.Draft.Id, target.Version, loaded.Draft.DataJson, removed,
            validation, loaded.Draft.RowVersion);
    }

    public async Task<SubmitFormResult> SubmitAsync(
        Guid ownerId, Guid draftId, string submissionKey, byte[]? rowVersion,
        CancellationToken ct = default)
    {
        var existing = await _db.Wf_FormDrafts.FirstOrDefaultAsync(x => x.Id == draftId, ct)
            ?? throw new InvalidOperationException("E-WF-003");
        if (existing.OwnerUserId != ownerId) throw new UnauthorizedAccessException("E-WF-003");
        if (existing.Status == WfFormDraftStatus.Submitted && existing.SubmittedFormDataId != null)
            return await SubmittedResultAsync(existing.SubmittedFormDataId.Value, ct);
        if (existing.Status != WfFormDraftStatus.Active) throw new InvalidOperationException("E-WF-003");
        EnsureRowVersion(existing.RowVersion, rowVersion);
        if (rowVersion != null)
            _db.Entry(existing).Property(x => x.RowVersion).OriginalValue = rowVersion;
        if (await IsStaleAsync(existing, ct)) throw new InvalidOperationException("E-WF-040");

        var formKey = await _db.Wf_FormDefs.Where(x => x.Id == existing.FormDefId)
            .Select(x => x.FormKey).SingleAsync(ct);
        using var document = JsonDocument.Parse(existing.DataJson);
        var ownsTransaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null;
        await using var transaction = ownsTransaction ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var result = await _submissions.SubmitAsync(
                new SubmitFormCommand(formKey, ownerId, submissionKey, document.RootElement.Clone(), draftId), ct);
            existing.Status = WfFormDraftStatus.Submitted;
            existing.SubmittedFormDataId = result.FormDataId;
            existing.SubmittedAtUtc = DateTime.UtcNow;
            existing.Modifier = ownerId.ToString();
            existing.ModifyDate = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteAsync(Guid ownerId, Guid draftId, CancellationToken ct = default)
    {
        var loaded = await LoadOwnedActiveAsync(ownerId, draftId, ct);
        _db.Wf_FormDrafts.Remove(loaded.Draft);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<(Wf_FormDraft Draft, Wf_FormDef Form, Wf_FormDefVersion Version)>
        LoadOwnedActiveAsync(Guid ownerId, Guid draftId, CancellationToken ct)
    {
        var draft = await _db.Wf_FormDrafts.FirstOrDefaultAsync(x => x.Id == draftId, ct)
            ?? throw new InvalidOperationException("E-WF-003");
        if (draft.OwnerUserId != ownerId) throw new UnauthorizedAccessException("E-WF-003");
        if (draft.Status != WfFormDraftStatus.Active) throw new InvalidOperationException("E-WF-003");
        var form = await _db.Wf_FormDefs.SingleAsync(x => x.Id == draft.FormDefId, ct);
        var version = await _db.Wf_FormDefVersions.SingleAsync(x => x.Id == draft.FormDefVersionId, ct);
        return (draft, form, version);
    }

    private Task<bool> IsStaleAsync(Wf_FormDraft draft, CancellationToken ct) =>
        _db.Wf_FormDefVersions.AnyAsync(x => x.FormDefId == draft.FormDefId &&
            x.Status == WfDefinitionVersionStatus.Published &&
            x.Version > _db.Wf_FormDefVersions.Where(v => v.Id == draft.FormDefVersionId)
                .Select(v => v.Version).Single(), ct);

    private Task<int> LatestVersionAsync(Guid formDefId, CancellationToken ct) =>
        _db.Wf_FormDefVersions.Where(x => x.FormDefId == formDefId &&
                x.Status == WfDefinitionVersionStatus.Published)
            .MaxAsync(x => x.Version, ct);

    private async Task<SubmitFormResult> SubmittedResultAsync(Guid formDataId, CancellationToken ct)
    {
        var data = await _db.Wf_FormDatas.AsNoTracking().SingleAsync(x => x.Id == formDataId, ct);
        var instance = await _db.Wf_FlowInstances.AsNoTracking()
            .SingleOrDefaultAsync(x => x.FormDataId == data.Id, ct);
        var flowVersion = instance?.FlowDefVersionId is Guid versionId
            ? await _db.Wf_FlowDefVersions.Where(x => x.Id == versionId).Select(x => (int?)x.Version).SingleAsync(ct)
            : null;
        return new(data.Id, data.FormDefVersionId!.Value, data.FormVersion,
            instance?.Id, instance?.FlowDefVersionId, flowVersion);
    }

    private static DraftDetail ToDetail(Wf_FormDraft draft, string formKey, string formName,
        int version, int latestVersion, string schemaJson, bool stale) =>
        new(draft.Id, formKey, formName, draft.FormDefVersionId, version, latestVersion, schemaJson,
            draft.DataJson, draft.Title, stale, draft.RowVersion);

    private static string ValidatePartial(JsonElement data, string schemaJson)
    {
        if (data.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("E-WF-047");
        var schema = ParseSchema(schemaJson);
        var fields = schema.Fields.ToDictionary(x => x.Name, StringComparer.Ordinal);
        if (data.EnumerateObject().Count() > 500) throw new InvalidOperationException("E-WF-047");
        foreach (var property in data.EnumerateObject())
        {
            if (!fields.TryGetValue(property.Name, out var field)) throw new InvalidOperationException("E-WF-039");
            if (property.Value.ValueKind == JsonValueKind.Null) continue;
            if (!FormDataValidator.IsValidDraftValue(field, property.Value))
                throw new InvalidOperationException("E-WF-047");
        }
        var canonical = FormSubmissionService.Canonicalize(data);
        if (System.Text.Encoding.UTF8.GetByteCount(canonical) > 1024 * 1024)
            throw new InvalidOperationException("E-WF-047");
        return canonical;
    }

    private static FormSchema ParseSchema(string schemaJson) =>
        JsonSerializer.Deserialize<FormSchema>(schemaJson, JsonOptions)
        ?? throw new InvalidOperationException("E-WF-047");

    private static bool Compatible(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static JsonNode? DefaultValue(string schemaJson, string fieldName)
    {
        var root = JsonNode.Parse(schemaJson);
        var field = root?["fields"]?.AsArray().FirstOrDefault(x =>
            string.Equals(x?["name"]?.GetValue<string>(), fieldName, StringComparison.Ordinal));
        return field?["default"]?.DeepClone();
    }

    private static bool HasValue(JsonNode? value) =>
        value is not null && value.ToJsonString() is not "null" and not "\"\"";

    private static IReadOnlyList<string> ValidationErrors(string schemaJson, string dataJson)
    {
        var schema = ParseSchema(schemaJson);
        using var doc = JsonDocument.Parse(dataJson);
        return schema.Fields.Where(x => x.Required &&
                (!doc.RootElement.TryGetProperty(x.Name, out var value) ||
                 value.ValueKind == JsonValueKind.Null ||
                 value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString())))
            .Select(x => x.Name + ":required").ToList();
    }

    private static string? NormalizeTitle(string? title)
    {
        title = title?.Trim();
        if (title?.Length > 200) throw new InvalidOperationException("E-WF-047");
        return string.IsNullOrEmpty(title) ? null : title;
    }

    private static void EnsureRowVersion(byte[]? current, byte[]? expected)
    {
        if (current != null && (expected == null || !current.SequenceEqual(expected)))
            throw new InvalidOperationException("E-WF-041");
    }
}

public sealed class DraftRebaseConfirmationException : InvalidOperationException
{
    public DraftRebaseConfirmationException(IReadOnlyList<string> removedFields) : base("E-WF-048")
        => RemovedFields = removedFields;
    public IReadOnlyList<string> RemovedFields { get; }
}
