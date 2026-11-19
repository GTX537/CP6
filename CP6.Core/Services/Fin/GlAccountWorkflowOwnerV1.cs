using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.GlAccountWorkflowStoreV1;
using Ledger = CP6.Core.Services.Fin.ManufacturingFinanceScopeV1.LedgerRow;

namespace CP6.Core.Services.Fin;

/// <summary>总账科目 Owner。账套、主体及字段权均从当前服务端上下文取得。</summary>
public sealed partial class GlAccountWorkflowOwnerV1(CP6Context db, IamAdministrationV1 iam, ManufacturingFinanceScopeV1 scope)
{
    public const string Resource = "fin-account";
    public static IReadOnlyList<FieldRegistry.Field> Fields { get; } = Array.AsReadOnly<FieldRegistry.Field>(
    [
        new("Code","科目编码"), new("Name","科目名称"), new("Type","会计大类"), new("NormalSide","正常方向"),
        new("ParentRootId","上级科目"), new("PostingClass","过账类别"), new("IsControl","控制科目标志"),
        new("SubLedgerType","子账类型"), new("RequirePartner","往来单位要求"), new("RoleKey","科目角色"),
        new("Currency","币种规则"), new("EffectiveInterval","有效区间"), new("ControlState","控制状态")
    ]);

    private static readonly IReadOnlyDictionary<string, string> CommandActions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["create"] = "add", ["revise"] = "edit", ["successor"] = "edit", ["submit"] = "submit",
        ["withdraw"] = "submit", ["abandon"] = "edit", ["approve"] = "review",
        ["reject"] = "review", ["publish"] = "release", ["suspend"] = "deactivate",
        ["approve-restore"] = "review", ["restore"] = "restore", ["retire"] = "retire", ["import-template"] = "import"
    };

    private const string Contract = "FIN-MASTER-01.GL.v1";
    private readonly GlAccountWorkflowStoreV1 store = new(db);
    private readonly MainOwnerOperationJournalV1 journal = new(db);

    private sealed record Frame(UserPermissionContext Actor, Ledger Ledger, GlBookV1 Book)
    {
        internal Guid Id => Ledger.Id;
        internal string JournalOwner => "Main.Fin.GlAccount.v1/" + Id.ToString("N");
        internal List<Func<CancellationToken, Task>> FinalChecks { get; } = [];
    }

    private sealed record Subject(GlRootV1 Root, GlProposalV1 Proposal, GlVersionV1 Version)
    {
        internal GlHeadsV1 Heads => Head(Root, Proposal);
    }

    public Task<object> Options(Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => Run(current, async (actor, token) =>
        {
            RequireAction(actor, "view");
            var choices = new List<object>();
            foreach (var ledger in await scope.ListAsync(actor.UserId, token))
            {
                if (await store.Book(ledger.Id, token) is not { } book) continue;
                var contextToken = await scope.IssueContextAsync(ledger.Id, actor.UserId, token);
                choices.Add(new { contextToken, scope = ledger.Scope(db.CurrentTenantId), book.Scheme,
                    baseCurrency = ledger.Currency, ledger.BusinessDate, book.ScopeRevision });
            }
            var actions = actor.ActionKeys.Where(key => key.StartsWith(Resource + ":", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
            var fields = Fields.ToDictionary(field => field.Name, field => FieldAccess(actor, field.Name), StringComparer.Ordinal);
            return new { choices, actions, fields };
        }, ct);

    public Task<object> Roots(Guid context, Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireAction(frame.Actor, "view");
            var disclose = CanReadContent(frame.Actor);
            var roots = await store.RootList(frame.Id, token);
            var at = (await Graph(frame, token)).AsOf(frame.Ledger.BusinessDate);
            var list = roots.Select(root =>
            {
                if (Digest(root.Payload) != root.Digest) throw Error("GL_VERSION_UNPROVEN", 503);
                var name = Decode<GlAccountContentV1>(root.Payload).Name;
                return new { root.Id, code = CanReadField(frame.Actor, "Code") ? root.Code : null,
                    controlState = CanReadField(frame.Actor, "ControlState") ? root.ControlState : null,
                    name = CanReadField(frame.Actor, "Name") ? name : null, proposalState = disclose ? root.ProposalState : null,
                    currentVersionId = disclose ? at.GetValueOrDefault(root.Id)?.Segment.VersionId : null };
            }).ToArray();
            var parentChoices = roots.Where(root => disclose && root.ControlState == "ENABLED" &&
                    at.GetValueOrDefault(root.Id)?.Content.PostingClass == "GROUP")
                .Select(root => new { root.Id, root.Code, at[root.Id].Content.Name }).ToArray();
            return new { frame.Book.ScopeRevision, frame.Book.Scheme, frame.Ledger.BusinessDate,
                baseCurrency = frame.Ledger.Currency, roles = CanReadField(frame.Actor, "RoleKey") ? frame.Book.Roles : [],
                currencies = CanReadField(frame.Actor, "Currency") ? frame.Book.Currencies : [], rows = list, parentChoices,
                canCreate = disclose && Can(frame.Actor, "add") && CanEditContent(frame.Actor, true),
                canImport = disclose && Can(frame.Actor, "import") && CanEditContent(frame.Actor, true),
                canReadDetails = disclose, canReadHistory = disclose && Can(frame.Actor, "history") };
        }, ct);

    public Task<object> ParentOptions(Guid context, DateOnly date, Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireAction(frame.Actor, "view");
            RequireDisclosure(frame.Actor);
            if (date < frame.Ledger.BusinessDate) throw Error("INVALID_EFFECTIVE_INTERVAL", 422);
            var roots = await store.Roots(frame.Id).OrderBy(root => root.Code).ToArrayAsync(token);
            var at = new GlAccountGraphV1(roots, await store.Graph(frame.Id, token)).AsOf(date);
            return roots.Where(root => root.ControlState == "ENABLED" &&
                    at.GetValueOrDefault(root.Id)?.Content.PostingClass == "GROUP")
                .Select(root => new { root.Id, root.Code, at[root.Id].Content.Name }).ToArray();
        }, ct);

    public Task<object> Revisions(Guid context, Guid root, Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireOriginalRead(frame.Actor, "WITHHELD");
            _ = await store.Root(frame.Id, root, token);
            return await store.Revisions(frame.Id, root, token);
        }, ct);

    public Task<object> Detail(Guid context, Guid root, Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireAction(frame.Actor, "view");
            RequireDisclosure(frame.Actor);
            var account = await ReadSubject(frame, root, token);
            var timeline = await store.Timeline(frame.Id, account.Root, token);
            object? currentEffective = null;
            if (timeline is not null)
            {
                var graph = await Graph(frame, token);
                var day = frame.Ledger.BusinessDate;
                if (graph.Current(root, day) is { } piece)
                    currentEffective = new { piece.Segment, version = await store.Version(frame.Id, root, piece.Segment.VersionId, token),
                        level = graph.Level(piece, day) };
            }
            var decision = account.Proposal.DecisionId is Guid decisionId
                ? await store.Decision(frame.Id, root, decisionId, token) : null;
            var restoreDecision = account.Root.ControlState == "SUSPENDED" && timeline is not null
                ? await store.CurrentRestoreDecision(frame.Id, root, account.Root.ControlRevision, timeline.Revision, token) : null;
            return new
            {
                rootId = root, account.Root.Code, account.Root.ControlState, frame.Book.Scheme,
                scope = frame.Ledger.Scope(db.CurrentTenantId), frame.Book.ScopeRevision, heads = account.Heads,
                account.Proposal, account.Version, decision, timeline, timelineReference = timeline?.Reference(root),
                currentEffective, restoreDecision,
                canReadHistory = Can(frame.Actor, "history"),
                allowedActions = await Allowed(frame, account, restoreDecision is not null, token), isExecutionPermit = false
            };
        }, ct);

    public Task<object> History(Guid context, Guid root, Guid proposalId, long revision,
        Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireOriginalRead(frame.Actor, "WITHHELD");
            var account = await store.Root(frame.Id, root, token);
            var proposal = await store.History(frame.Id, root, proposalId, revision, token);
            var version = await store.Version(frame.Id, root, proposal.VersionId, token);
            var decision = proposal.DecisionId is Guid reference ? await store.Decision(frame.Id, root, reference, token) : null;
            return new { kind = "HISTORY_ONLY", rootId = account.Id, account.Code, proposal, version, decision,
                allowedActions = Array.Empty<string>(), isExecutionPermit = false };
        }, ct);

    public Task<object> Original(Guid context, Guid operation, Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireOriginalRead(frame.Actor, "RECEIPT_WITHHELD");
            if (operation == Guid.Empty) throw Error("OPERATION_KEY_REQUIRED", 400);
            var entry = await journal.Read(frame.JournalOwner, operation, token);
            if (entry is null)
                return new { kind = "NOT_OBSERVED", operationRef = operation, asOf = DateTimeOffset.UtcNow,
                    mayRetryOriginal = true, provesPermanentNoEffect = false };
            return Receipt(frame, entry);
        }, ct);

    public Task<object> Role(Guid context, string role, DateOnly? date, Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireAction(frame.Actor, "view");
            RequireDisclosure(frame.Actor);
            if (!frame.Book.ProfessionalAvailable) return new GlAccountEligibilityV1("UNPROVEN", "PROFESSIONAL_SERVICE_UNAVAILABLE", null, null);
            if (!frame.Book.Roles.Contains(role, StringComparer.Ordinal))
                return new GlAccountEligibilityV1("NOT_CONFIGURED", null, null, null);
            return (await Graph(frame, token)).Role(role, date ?? frame.Ledger.BusinessDate);
        }, ct);

    public Task<object> Eligibility(Guid context, Guid root, string purpose, string currency, DateOnly? date,
        Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireAction(frame.Actor, "view");
            RequireDisclosure(frame.Actor);
            if (purpose is not ("GL_MANUAL" or "GL_AUTO") || string.IsNullOrEmpty(currency))
                throw Error("USE_INTENT_INVALID", 422);
            var account = await store.Root(frame.Id, root, token);
            var graph = await Graph(frame, token);
            var effective = date ?? frame.Ledger.BusinessDate;
            var status = graph.Screen(account, effective, purpose, currency, frame.Ledger.Currency);
            if (status.Status != "OBSERVED_AS_OF") return status;
            try { GlAccountContentRulesV1.RequireProfessional(frame.Book, frame.Ledger.Currency, effective, effective == DateOnly.MaxValue ? null : effective.AddDays(1)); }
            catch (OrderPriceCorrectionException error) when (error.Status is 422 or 503)
            { return new GlAccountEligibilityV1("UNPROVEN", error.Message, null, null); }
            return status;
        }, ct);

    public Task<object> Execute(Guid context, GlCommandV1 command, Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            ValidateCommand(command);
            var input = MainOwnerOperationJournalV1.Encode(command);
            if (await journal.Read(frame.JournalOwner, command.OperationRef, token) is { } original)
            {
                RequireOriginalRead(frame.Actor, "RECEIPT_WITHHELD");
                var receipt = Receipt(frame, original);
                if (!MainOwnerOperationJournalV1.Matches(original, input)) throw Error("OPERATION_INPUT_CONFLICT");
                return receipt;
            }

            RequireAction(frame.Actor, "view");
            RequireAction(frame.Actor, CommandActions[command.Kind]);
            RequireDisclosure(frame.Actor);
            var result = await Decide(frame, command, token);
            var objects = result.RootIds ?? (result.RootId.HasValue ? [result.RootId.Value] : []);
            var audit = new AuditEvidenceV1("FIN_GL_OWNER_UTC", "REQUIRED_AUDIT", "USER", frame.Actor.UserId,
                frame.Actor.UserId, PermissionVersion: AuditEvidenceV1.PermissionDecisionVersion(db.CurrentTenantId, frame.Actor),
                OperationId: command.OperationRef, Outcome: result.Kind, RecordedAtUtc: DateTimeOffset.UtcNow,
                EntityVersion: result.VersionId?.ToString("D"), Action: command.Kind,
                OriginalOperationDigest: MainOwnerOperationJournalV1.Digest(input));
            await journal.Store(frame.JournalOwner, Contract, command.OperationRef, frame.Actor.UserId, input, result,
                objects, audit, token);
            await store.Event(frame.Id, command.OperationRef, command.Kind, frame.Actor.UserId,
                new { result, command.Reason, scope = frame.Ledger.Scope(db.CurrentTenantId), audit }, token);
            if (result.ErrorCode is null && command.PreviewId is Guid consumed)
                await store.Consume(frame.Id, consumed, command.OperationRef, token);
            if (result.ErrorCode is null && result.Kind != "NO_CHANGE" && command.Kind is ("publish" or "suspend" or "restore" or "retire"))
                await store.EnqueueRefresh(frame.Ledger.Scope(db.CurrentTenantId), frame.Id, result, command.Kind, token);
            return result;
        }, ct, exclusive: true);

    private async Task<GlReceiptV1> Decide(Frame frame, GlCommandV1 command, CancellationToken ct)
    {
        const string point = "gl_business_decision";
        var transaction = db.Database.CurrentTransaction!;
        await transaction.CreateSavepointAsync(point, ct);
        try
        {
            GlStoredPreviewV1? preview = null;
            if (command.PreviewId is Guid previewId)
                preview = await store.Preview(frame.Id, frame.Actor.UserId, previewId, command.Kind == "publish" ? "PUBLISH" : "TEMPLATE", ct);
            RequireScopeRevision(frame, command.ExpectedScopeRevision);
            var result = command.Kind switch
            {
                "create" => await Create(frame, command, ct),
                "import-template" => await ImportTemplate(frame, command, preview!, ct),
                _ => await Change(frame, command, await ReadSubject(frame, command.RootId!.Value, ct), preview, ct)
            };
            if (result.Kind != "NO_CHANGE")
                result = result with { ScopeRevision = await store.Advance(frame.Id, frame.Book.ScopeRevision, ct) };
            if (result.RootId is Guid rootId)
            {
                var root = await store.Root(frame.Id, rootId, ct);
                var proposal = await store.Proposal(frame.Id, rootId, ct) ?? throw Error("PROPOSAL_UNPROVEN", 503);
                var timeline = await store.Timeline(frame.Id, root, ct);
                result = result with { Heads = Head(root, proposal), TimelineReference = timeline?.Reference(rootId) };
            }
            await transaction.ReleaseSavepointAsync(point, ct);
            return result;
        }
        catch (OrderPriceCorrectionException error) when (error.Status is 409 or 412 or 422)
        {
            // 以当前身份确定的业务拒绝可按原键回读，业务写入整体回滚。
            await transaction.RollbackToSavepointAsync(point, ct);
            frame.FinalChecks.Clear();
            return new(command.OperationRef, "REJECTED", frame.Book.ScopeRevision, null, null, null, null, null, null,
                ErrorCode: error.Message, ErrorStatus: error.Status);
        }
    }

    private async Task<object> Run(Func<CancellationToken, Task<Guid>> current,
        Func<UserPermissionContext, CancellationToken, Task<object>> body, CancellationToken ct)
    {
        try
        {
            return await iam.ExecuteIdentityAsync(current, async (actor, token) =>
            {
                // 账套及成员来源与当前 IAM 根锁采用固定顺序。受信配置变更不能越过决定点。
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_fin_mfg_v1.ledgers, cp6_fin_mfg_v1.members IN SHARE MODE", token);
                var result = await body(actor, token);
                if (await current(token) != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
                return result;
            }, ct);
        }
        catch (IamAdministrationExceptionV1 error) { throw Error(error.Message, error.Status); }
        catch (CurrentPermissionUnavailableException) { throw Error("GL_AUTHORITY_UNAVAILABLE", 503); }
        catch (DbException) { throw Error("GL_STORE_UNAVAILABLE", 503); }
    }

    private Task<object> With(Guid context, Func<CancellationToken, Task<Guid>> current,
        Func<Frame, CancellationToken, Task<object>> body, CancellationToken ct, bool exclusive = false)
        => Run(current, async (actor, token) =>
        {
            if (context == Guid.Empty) throw Error("LEDGER_CONTEXT_UNPROVEN", 404);
            var ledger = await RequireContext(context, actor.UserId, token);
            var book = await store.Book(ledger.Id, token) ?? throw Error("LEDGER_CONTEXT_UNPROVEN", 404);
            await GlAccountScopeGateV1.Enter(db, ledger.Id, exclusive, token);
            var frame = new Frame(actor, ledger, book);
            var result = await body(frame, token);
            var final = await RequireContext(context, actor.UserId, token);
            if (final.Id != ledger.Id || final.BusinessDate != ledger.BusinessDate || final.Currency != ledger.Currency)
                throw Error("LEDGER_CONTEXT_UNPROVEN", 404);
            foreach (var check in frame.FinalChecks) await check(token);
            return result;
        }, ct);

    private async Task<Ledger> RequireContext(Guid token, Guid actor, CancellationToken ct)
    {
        try { return await scope.RequireContextAsync(token, actor, ct); }
        catch (OrderPriceCorrectionException error) when (error.Message == "FINANCE_CONTEXT_NOT_FOUND")
        { throw Error("LEDGER_CONTEXT_UNPROVEN", 404); }
    }

    private async Task<GlAccountGraphV1> Graph(Frame frame, CancellationToken ct)
        => new(await store.Roots(frame.Id).ToArrayAsync(ct), await store.Graph(frame.Id, ct));

    private async Task<Subject> ReadSubject(Frame frame, Guid root, CancellationToken ct)
    {
        var account = await store.Root(frame.Id, root, ct);
        var proposal = await store.Proposal(frame.Id, root, ct) ?? throw Error("PROPOSAL_UNPROVEN", 503);
        return new(account, proposal, await store.Version(frame.Id, root, proposal.VersionId, ct));
    }

    private static GlHeadsV1 Head(GlRootV1 root, GlProposalV1 proposal)
        => new(root.ControlRevision, root.TimelineRevision, proposal.Id, proposal.Revision);

    private static bool Can(UserPermissionContext actor, string action) => actor.ActionKeys.Contains(Resource + ":" + action);
    private static void RequireAction(UserPermissionContext actor, string action)
    {
        if (!Can(actor, action)) throw Error("GL_FORBIDDEN", 403);
    }

    private static int FieldAccess(UserPermissionContext actor, string field)
        => actor.FieldPerms.TryGetValue(Resource, out var rules) && rules.TryGetValue(field, out var access) ? access : 1;

    private static bool CanReadField(UserPermissionContext actor, string name) => FieldAccess(actor, name) is 1 or 2;
    private static bool CanReadContent(UserPermissionContext actor) => Fields.All(field => CanReadField(actor, field.Name));
    private static bool CanEditContent(UserPermissionContext actor, bool includeCode)
        => Fields.Where(field => field.Name != "ControlState" && (includeCode || field.Name != "Code"))
            .All(field => FieldAccess(actor, field.Name) == 1);

    private static void RequireDisclosure(UserPermissionContext actor)
    {
        if (!CanReadContent(actor)) throw Error("GL_FIELDS_WITHHELD", 403);
    }

    private static void RequireOriginalRead(UserPermissionContext actor, string error)
    {
        if (!Can(actor, "view") || !Can(actor, "history") || !CanReadContent(actor)) throw Error(error, 403);
    }

    private static GlReceiptV1 Receipt(Frame frame, MainOwnerOperationJournalV1.Original original)
    {
        if (original.Actor != frame.Actor.UserId) throw Error("RECEIPT_WITHHELD", 403);
        if (original.Contract != Contract || original.State != "COMMITTED")
            throw Error("GL_ORIGINAL_UNPROVEN", 503);
        return JsonSerializer.Deserialize<GlReceiptV1>(original.Result) ?? throw Error("GL_ORIGINAL_UNPROVEN", 503);
    }

    private static void RequireScopeRevision(Frame frame, long expected)
    {
        if (frame.Book.ScopeRevision != expected) throw Error("STALE", 412);
    }

    private static void ValidateCommand(GlCommandV1 cmd)
    {
        if (cmd.OperationRef == Guid.Empty || cmd.Kind is null || !CommandActions.ContainsKey(cmd.Kind) ||
            cmd.ExpectedScopeRevision <= 0 || string.IsNullOrWhiteSpace(cmd.Reason) || cmd.Reason.Length > 256 ||
            cmd.Reason.Any(char.IsControl))
            throw Error("GL_COMMAND_INVALID", 400);
        var rootsRequired = cmd.Kind is not ("create" or "import-template");
        var editsContent = cmd.Kind is "create" or "successor" or "revise";
        if ((rootsRequired ? cmd.RootId is null || cmd.RootId == Guid.Empty || cmd.ExpectedHeads is null :
                cmd.RootId is not null || cmd.ExpectedHeads is not null)
            || (editsContent != (cmd.Content is not null)) || (editsContent != cmd.Activate.HasValue)
            || ((cmd.Kind == "create") != (cmd.Code is not null))
            || ((cmd.Kind is "publish" or "import-template") != cmd.PreviewId.HasValue) || cmd.PreviewId == Guid.Empty
            || ((cmd.Kind == "restore") != cmd.DecisionId.HasValue) || cmd.DecisionId == Guid.Empty
            || ((cmd.Kind is "restore" or "approve-restore") != (cmd.TimelineSubject is not null)))
            throw Error("GL_COMMAND_INVALID", 400);
    }

    private async Task<string[]> Allowed(Frame frame, Subject subject, bool hasRestoreDecision, CancellationToken ct)
    {
        var allowed = new List<string>();
        void Add(string kind) { if (Can(frame.Actor, CommandActions[kind])) allowed.Add(kind); }
        var mayReview = frame.Book.ProfessionalAvailable && Can(frame.Actor, "review") &&
            await store.Qualification(frame.Id, frame.Actor.UserId, "review", ct) is not null;
        var mayRelease = frame.Book.ProfessionalAvailable && Can(frame.Actor, "release") &&
            await store.Qualification(frame.Id, frame.Actor.UserId, "release", ct) is not null;
        if (subject.Root.ControlState == "RETIRED") return [];
        switch (subject.Proposal.State)
        {
            case "DRAFT": case "REJECTED":
                if (CanEditContent(frame.Actor, false)) Add("revise");
                Add("submit");
                if (subject.Proposal.State == "DRAFT") Add("withdraw");
                else Add("abandon");
                break;
            case "SUBMITTED":
                Add("withdraw");
                if (mayReview && !subject.Proposal.Makers.Contains(frame.Actor.UserId)) { Add("approve"); Add("reject"); }
                break;
            case "APPROVED":
                Add("withdraw");
                if (mayRelease && (!subject.Proposal.Activate || FieldAccess(frame.Actor, "ControlState") == 1)) Add("publish");
                break;
            case "RELEASED": case "WITHDRAWN": case "CLOSED":
                if (CanEditContent(frame.Actor, false)) Add("successor");
                break;
        }
        if (subject.Root.ControlState == "ENABLED" && FieldAccess(frame.Actor, "ControlState") == 1) Add("suspend");
        if (subject.Root.ControlState == "SUSPENDED" && subject.Root.TimelineRevision.HasValue)
        {
            if (mayReview) Add("approve-restore");
            if (hasRestoreDecision && frame.Book.ProfessionalAvailable && FieldAccess(frame.Actor, "ControlState") == 1) Add("restore");
        }
        if (FieldAccess(frame.Actor, "ControlState") == 1) Add("retire");
        return allowed.ToArray();
    }
}
