using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wf;

public sealed class FormSubmissionService : IFormSubmissionService
{
    private static readonly JsonSerializerOptions SchemaOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;
    private readonly IDefinitionVersionResolver _versions;
    private readonly IFormService _forms;
    private readonly FlowEngine _engine;

    public FormSubmissionService(
        CP6Context db, IDefinitionVersionResolver versions, IFormService forms, FlowEngine engine)
    {
        _db = db;
        _versions = versions;
        _forms = forms;
        _engine = engine;
    }

    public async Task<SubmitFormResult> SubmitAsync(SubmitFormCommand command, CancellationToken ct = default)
    {
        ValidateSubmissionKey(command.SubmissionKey);
        if (command.Data.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("E-WF-047");
        var canonicalInput = Canonicalize(command.Data);
        if (Encoding.UTF8.GetByteCount(canonicalInput) > 1024 * 1024) throw new InvalidOperationException("E-WF-047");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{command.FormKey}\n{command.DraftId?.ToString("N") ?? "-"}\n{canonicalInput}")));

        var prior = await _db.Wf_FormDatas.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubmissionKey == command.SubmissionKey, ct);
        if (prior != null) return await ExistingResultAsync(prior, hash, ct);

        var form = await _versions.ResolveLatestFormAsync(command.FormKey, ct);
        var schema = JsonSerializer.Deserialize<FormSchema>(form.Version!.SchemaJson, SchemaOptions)
                     ?? throw new InvalidOperationException("E-WF-047");
        ValidateShape(command.Data, schema);
        var raw = command.Data.GetRawText();
        var (normalized, errors) = _forms.RecomputeAndValidate(form.Version.SchemaJson, raw);
        if (errors.Count > 0) throw new InvalidOperationException("E-WF-047:" + string.Join("|", errors));

        var ownsTransaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null;
        await using var tx = ownsTransaction ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var data = new Wf_FormData
            {
                Id = Guid.NewGuid(),
                FormDefVersionId = form.Version.Id,
                FormKey = form.Head.FormKey,
                FormVersion = form.Version.Version,
                SubmissionKey = command.SubmissionKey,
                RequestHash = hash,
                SubmittedBy = command.ActorId,
                SubmittedAtUtc = DateTime.UtcNow,
                DataJson = normalized,
                Creator = command.ActorId.ToString()
            };
            _db.Wf_FormDatas.Add(data);

            var binding = await _db.Wf_FormFlowBindings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.FormDefId == form.Head.Id && x.Enable, ct);
            Guid? instanceId = null;
            Guid? flowVersionId = null;
            int? flowVersion = null;
            if (binding != null)
            {
                var flowHead = await _db.Wf_FlowDefs.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == binding.FlowDefId, ct)
                    ?? throw new InvalidOperationException("E-WF-029");
                var flow = await _versions.ResolveLatestFlowAsync(flowHead.FlowKey, validateDependencies: true, ct);
                flowVersionId = flow.Version.Id;
                flowVersion = flow.Version.Version;
                instanceId = await _engine.StartPinnedAsync(
                    flow.Version.Id, command.ActorId, normalized,
                    new FlowBusinessRef("SFS", data.Id.ToString()),
                    new FlowFormRef(form.Version.Id, data.Id), ct);
            }
            else
            {
                await _db.SaveChangesAsync(ct);
            }

            if (tx != null) await tx.CommitAsync(ct);
            return new(data.Id, form.Version.Id, form.Version.Version,
                instanceId, flowVersionId, flowVersion);
        }
        catch (DbUpdateException) when (ownsTransaction)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var existing = await _db.Wf_FormDatas.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SubmissionKey == command.SubmissionKey, ct);
            if (existing != null) return await ExistingResultAsync(existing, hash, ct);
            throw;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<SubmitFormResult> ExistingResultAsync(Wf_FormData data, string hash, CancellationToken ct)
    {
        if (!string.Equals(data.RequestHash, hash, StringComparison.Ordinal))
            throw new InvalidOperationException("E-WF-044");
        var instance = await _db.Wf_FlowInstances.AsNoTracking()
            .SingleOrDefaultAsync(x => x.FormDataId == data.Id, ct);
        var flowVersion = instance?.FlowDefVersionId is Guid id
            ? await _db.Wf_FlowDefVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            : null;
        return new(data.Id, data.FormDefVersionId!.Value, data.FormVersion,
            instance?.Id, instance?.FlowDefVersionId, flowVersion?.Version);
    }

    private static void ValidateSubmissionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 ||
            key.Any(x => !(char.IsLetterOrDigit(x) || x is '-' or '_' or '.')))
            throw new InvalidOperationException("E-WF-044");
    }

    private static void ValidateShape(JsonElement data, FormSchema schema)
    {
        if (data.EnumerateObject().Count() > 500) throw new InvalidOperationException("E-WF-047");
        var known = schema.Fields.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
        {
            if (!known.Contains(property.Name)) throw new InvalidOperationException("E-WF-039");
            if (Depth(property.Value) > 8) throw new InvalidOperationException("E-WF-047");
            if (property.Value.ValueKind == JsonValueKind.String &&
                property.Value.GetString()!.Length > 10_000)
                throw new InvalidOperationException("E-WF-047");
        }
    }

    private static int Depth(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return 1 + value.EnumerateObject().Select(x => Depth(x.Value)).DefaultIfEmpty().Max();
        if (value.ValueKind == JsonValueKind.Array)
            return 1 + value.EnumerateArray().Select(Depth).DefaultIfEmpty().Max();
        return 1;
    }

    internal static string Canonicalize(JsonElement element)
    {
        JsonNode? Canonical(JsonElement item) => item.ValueKind switch
        {
            JsonValueKind.Object => new JsonObject(item.EnumerateObject()
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => KeyValuePair.Create<string, JsonNode?>(x.Name, Canonical(x.Value)))),
            JsonValueKind.Array => new JsonArray(item.EnumerateArray().Select(Canonical).ToArray()),
            JsonValueKind.String => JsonValue.Create(item.GetString()),
            JsonValueKind.Number => JsonNode.Parse(item.GetRawText()),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null => null,
            _ => null
        };
        return Canonical(element)?.ToJsonString() ?? "null";
    }
}
