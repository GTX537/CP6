using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.GlAccountContentRulesV1;
using static CP6.Core.Services.Fin.GlAccountWorkflowStoreV1;

namespace CP6.Core.Services.Fin;

public sealed partial class GlAccountWorkflowOwnerV1
{
    private sealed record LegacyRow(Guid Id, string Code, string Name, int Type, int NormalSide, Guid? ParentId,
        int Level, bool IsLeaf, bool IsControl, string? SubLedgerType, bool RequirePartner, string? Role, string StandardScheme,
        bool IsActive, string? CurrencyCd);

    public Task<object> LegacyOptions(Guid context, Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireAction(frame.Actor, "view");
            RequireDisclosure(frame.Actor);
            return new { kind = "LEGACY_UNPROVEN", rows = await LegacyRows(frame, null, token), isExecutionPermit = false };
        }, ct);

    public Task<object> TemplatePreview(Guid context, GlTemplatePreviewInputV1 request,
        Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireAction(frame.Actor, "view");
            RequireAction(frame.Actor, "import");
            RequireDisclosure(frame.Actor);
            RequireEdit(frame, true);
            RequireScopeRevision(frame, request.ExpectedScopeRevision);
            var ids = request.LegacyIds;
            var modes = (request.StandardTemplate ? 1 : 0) + (request.Rows is not null ? 1 : 0) + (ids is not null ? 1 : 0);
            if (modes != 1 || ids is { Length: 0 or > 100 } || ids?.Distinct().Count() != ids?.Length)
                throw Error("TEMPLATE_BATCH_INVALID", 422);
            var legacy = ids is null ? null : await LegacyRows(frame, ids, token);
            var lines = legacy is not null ? MapLegacy(request, legacy)
                : request.StandardTemplate ? Standard(frame, request) : request.Rows!;
            if (lines.Length is 0 or > 100) throw Error("TEMPLATE_BATCH_INVALID", 422);

            var graph = await Graph(frame, token);
            var existing = (await store.Roots(frame.Id).ToArrayAsync(token)).ToDictionary(root => root.Code, StringComparer.Ordinal);
            var batch = new Dictionary<string, (Guid Id, GlTemplateLineV1 Line)>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                if (line?.Content is null || line.Content.ParentRootId.HasValue) throw Error("TEMPLATE_ROW_INVALID", 422);
                var code = Code(line.Code);
                if (existing.ContainsKey(code) || !batch.TryAdd(code, (Guid.NewGuid(), line)))
                    throw Error("CODE_CONFLICT");
            }

            var drafts = new List<GlPreparedDraftV1>(batch.Count);
            foreach (var (code, (id, line)) in batch)
            {
                Guid? parent = null;
                if (line.ParentCode is string parentCode)
                {
                    var normalized = Code(parentCode);
                    var nextParent = batch.TryGetValue(normalized, out var prepared) ? prepared.Id
                        : existing.TryGetValue(normalized, out var root) &&
                            graph.Current(root.Id, line.Content.From)?.Content.PostingClass == "GROUP" ? root.Id
                        : throw Error("TEMPLATE_PARENT_UNPROVEN", 422);
                    parent = nextParent;
                }
                var body = Normalize(line.Content with { ParentRootId = parent }, frame.Ledger.BusinessDate,
                    frame.Book, allowUnprovenDraft: request.StandardTemplate || legacy is not null);
                drafts.Add(new(id, code, body));
            }
            RequireTemplateTree(drafts);

            var plan = new GlTemplatePlanV1(frame.Book.ScopeRevision, frame.Book.PolicyRevision, frame.Ledger.BusinessDate,
                drafts.ToArray(), ids, legacy is null ? null : Digest(Encode(legacy)));
            var created = await store.AddPreview(frame.Id, frame.Actor.UserId, "TEMPLATE", plan, token);
            return new { previewId = created.Id, created.ExpiresAt, frame.Book.ScopeRevision, frame.Book.Scheme, plan.Drafts, legacy,
                willCreate = "DRAFT", controlState = "SUSPENDED" };
        }, ct);

    private async Task<LegacyRow[]> LegacyRows(Frame frame, Guid[]? ids, CancellationToken ct)
    {
        var query = db.GlAccounts.AsNoTracking().Where(row => row.TenantId == db.CurrentTenantId &&
            row.StandardScheme == frame.Book.Scheme);
        if (ids is not null) query = query.Where(row => ids.Contains(row.Id));
        var rows = await query.OrderBy(row => row.Id).Take(ids is null ? 100 : 101)
            .Select(row => new LegacyRow(row.Id, row.Code, row.Name, (int)row.Type, (int)row.NormalSide, row.ParentId,
                row.Level, row.IsLeaf, row.IsControl, row.SubLedgerType, row.RequirePartner, row.Role, row.StandardScheme,
                row.IsActive, row.CurrencyCd)).ToArrayAsync(ct);
        if (ids is not null && rows.Length != ids.Length) throw Error("NOT_FOUND_OR_NOT_VISIBLE", 404);
        return rows;
    }

    private static GlTemplateLineV1[] MapLegacy(GlTemplatePreviewInputV1 request, LegacyRow[] rows)
    {
        var byId = rows.ToDictionary(row => row.Id);
        return rows.Select(row =>
        {
            string? parentCode = null;
            if (row.ParentId is Guid parent)
                parentCode = byId.TryGetValue(parent, out var legacyParent) ? legacyParent.Code
                    : throw Error("TEMPLATE_PARENT_UNPROVEN", 422);
            var currency = string.IsNullOrWhiteSpace(row.CurrencyCd) ? null : new GlCurrencyV1("SINGLE", [row.CurrencyCd]);
            return new GlTemplateLineV1(row.Code, parentCode,
                new(row.Name, row.Type, row.NormalSide, null, row.IsLeaf ? "POSTABLE" : "GROUP", row.IsControl,
                    row.SubLedgerType, row.RequirePartner, row.Role, currency, request.From, request.ToExclusive));
        }).ToArray();
    }

    private static GlTemplateLineV1[] Standard(Frame frame, GlTemplatePreviewInputV1 request)
    {
        if (frame.Book.Scheme is not (FinCoaTemplate.CnGaap or FinCoaTemplate.Intl)) throw Error("TEMPLATE_NOT_CONFIGURED", 422);
        return FinCoaTemplate.Get(frame.Book.Scheme).Select(row => new GlTemplateLineV1(row.Code, row.ParentCode,
            new(row.Name, (int)row.Type, (int)row.NormalSide, null, row.IsLeaf ? "POSTABLE" : "GROUP", row.IsControl,
                row.SubLedgerType, row.RequirePartner, row.Role, null, request.From, request.ToExclusive))).ToArray();
    }

    private static void RequireTemplateTree(List<GlPreparedDraftV1> drafts)
    {
        var byId = drafts.ToDictionary(draft => draft.RootId);
        foreach (var draft in drafts)
        {
            var seen = new HashSet<Guid> { draft.RootId };
            var candidate = draft;
            while (candidate.Content.ParentRootId is Guid parent)
            {
                if (!seen.Add(parent)) throw Error("PARENT_CYCLE", 422);
                if (seen.Count > 32) throw Error("DEPENDENCY_LIMIT", 422);
                if (!byId.TryGetValue(parent, out var next)) break;
                if (next.Content.PostingClass != "GROUP") throw Error("TEMPLATE_PARENT_UNPROVEN", 422);
                candidate = next;
            }
        }
    }

    private async Task<GlReceiptV1> ImportTemplate(Frame frame, GlCommandV1 command, GlStoredPreviewV1 preview,
        CancellationToken ct)
    {
        RequireEdit(frame, true);
        var plan = Decode<GlTemplatePlanV1>(preview.Snapshot);
        if (plan.ScopeRevision != frame.Book.ScopeRevision || plan.PolicyRevision != frame.Book.PolicyRevision ||
            plan.BusinessDate != frame.Ledger.BusinessDate)
            throw Error("STALE", 412);
        if (plan.LegacyIds is not null && Digest(Encode(await LegacyRows(frame, plan.LegacyIds, ct))) != plan.LegacyDigest)
            throw Error("STALE", 412);

        var references = new List<GlDraftReferenceV1>(plan.Drafts.Length);
        foreach (var row in plan.Drafts)
        {
            var proposal = await AddDraft(frame, row, false, ct);
            references.Add(new(row.RootId, proposal.Id, proposal.VersionId));
        }
        return new(command.OperationRef, "DRAFTS_CREATED", frame.Book.ScopeRevision, null, null, null,
            null, null, "SUSPENDED", references.Select(draft => draft.RootId).ToArray(), references.ToArray());
    }
}
