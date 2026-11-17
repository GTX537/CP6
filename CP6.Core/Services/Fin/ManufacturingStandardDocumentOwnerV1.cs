using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Mes;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;
using static CP6.Core.Services.Fin.ManufacturingStandardDocumentStoreV1;
using static CP6.Core.Services.Fin.StandardDocumentBinding;

namespace CP6.Core.Services.Fin;

public sealed class ManufacturingStandardDocumentOwnerV1(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 scope,
    CostPolicySourcesV1? policySources = null)
{
    private readonly ManufacturingStandardDocumentStoreV1 store = new(db);
    private readonly ManufacturingStandardBasisStoreV1 standards = new(db);
    private readonly ManufacturingStandardBasisSourcesV1 source = new(db);
    private readonly FinanceCostCommandJournalV1 journal = new(db);

    public Task<StandardDocumentReplyV1> CreateAsync(Guid context, StandardDocumentCreateV1 command, CancellationToken ct)
        => WriteAsync(context, StandardDocumentBinding.Create(command.CommandId, "/" + Documents, command,
            new { command.WorkOrderId }, command.Reason), async (ledger, access, binding, token) =>
        {
            await source.RequireOrderAsync(ledger.Id, command.WorkOrderId, access, token);
            var id = Guid.NewGuid();
            await store.CreateAsync(ledger.Id, id, command.WorkOrderId, access.ActorId, command.Reason, token);
            return await store.RecordCommandAsync(ledger.Id, access.ActorId, binding, "CREATE", id, token);
        }, ct);

    public Task<StandardDocumentReplyV1> PrepareAsync(Guid context, Guid id, StandardDocumentPrepareV1 command, CancellationToken ct)
        => WriteAsync(context, StandardDocumentBinding.Create(command.CommandId, $"/{Documents}/{id:D}/collection-plans",
            command, new { command.ExpectedVersion, command.StandardBasisVersionId }, command.Reason),
            async (ledger, access, binding, token) =>
        {
            var (doc, order) = await LoadDocumentAsync(ledger.Id, id, access, token);
            RequireHead(doc, ParseVersion(command.ExpectedVersion));
            await RequireSelectableBasisAsync(ledger, order, command.StandardBasisVersionId, access, token);
            if (doc.Version is long current
                && (await store.RequireEditionAsync(ledger.Id, id, current, token)).StandardBasisId == command.StandardBasisVersionId)
                throw Error("STANDARD_BASIS_ALREADY_FROZEN", 409);

            var plan = Guid.NewGuid();
            var expires = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeMilliseconds());
            await store.PrepareAsync(ledger.Id, doc, plan, command.StandardBasisVersionId, ledger.BusinessDate,
                access.ActorId, expires, command.Reason, token);
            return await store.RecordCommandAsync(ledger.Id, access.ActorId, binding, "PREPARE", id, token,
                plan: plan, expiresAt: expires);
        }, ct);

    public Task<StandardDocumentReplyV1> FreezeAsync(Guid context, Guid id, StandardDocumentFreezeV1 command, CancellationToken ct)
        => WriteAsync(context, StandardDocumentBinding.Create(command.CommandId, $"/{Documents}/{id:D}/freeze",
            command, new { command.ExpectedVersion, command.PlanId }, command.Reason),
            async (ledger, access, binding, token) =>
        {
            var (doc, order) = await LoadDocumentAsync(ledger.Id, id, access, token);
            var expected = ParseVersion(command.ExpectedVersion);
            RequireHead(doc, expected);
            var plan = await store.RequirePlanAsync(ledger.Id, id, command.PlanId, access.ActorId, token);
            if (plan.ExpectedVersion != expected || plan.AccountingDate != ledger.BusinessDate)
                throw Error("STANDARD_DOCUMENT_PLAN_STALE", 412);
            RequireLivePlan(plan);
            await RequireSelectableBasisAsync(ledger, order, plan.StandardBasisId, access, token);
            access.AtCommit(_ => { RequireLivePlan(plan); return Task.CompletedTask; });

            var version = checked((expected ?? 0L) + 1);
            await store.FreezeAsync(ledger.Id, doc, version, command.PlanId, plan.StandardBasisId, plan.AccountingDate,
                access.ActorId, command.Reason, token);
            return await store.RecordCommandAsync(ledger.Id, access.ActorId, binding, "FREEZE", id, token, version, command.PlanId);
        }, ct);

    public Task<object> ReadAsync(Guid context, Guid id, long? version, CancellationToken ct)
        => scope.WithContextAsync<object>(owner, context, "view", false, async (ledger, access, token) =>
        {
            await access.ActionAsync(OperationsOwnerV1.FinCost, "basis-view", token);
            var (doc, _) = await LoadDocumentAsync(ledger.Id, id, access, token);
            var selected = version ?? doc.Version;
            if (selected == null)
                return new { documentId = id, doc.WorkOrderId, basisClass = "STANDARD", state = "DRAFT", version = (string?)null };

            var edition = await store.RequireEditionAsync(ledger.Id, id, selected.Value, token);
            var basis = await standards.ReadAsync(ledger.Id, edition.StandardBasisId, token);
            var (body, _, _) = await ManufacturingStandardBasisSourcesV1.ReadVisibleBasisAsync(basis, access, token);
            return new
            {
                documentId = id, doc.WorkOrderId, basisClass = "STANDARD", state = "READY", version = VersionText(selected),
                edition.AccountingDate, edition.FrozenAt,
                standardBasis = new { versionId = basis.Id, sequence = VersionText(basis.Sequence), basis.PolicyVersionId },
                body = body.ToView(),
            };
        }, ct);

    public Task<object> ReadCommandAsync(Guid context, Guid command, CancellationToken ct)
        => scope.WithContextAsync<object>(owner, context, "view", false, async (ledger, access, token) =>
        {
            await access.ActionAsync(OperationsOwnerV1.FinCost, "basis-view", token);
            var original = await store.ReadCommandAsync(ledger.Id, command, token);
            if (original == null)
                return new { kind = "NOT_OBSERVED", commandId = command, provesPermanentNoEffect = false };
            await RequireOriginalScopeAsync(ledger.Id, original, access, token);
            return new { kind = "FOUND", reply = original.ToReply(command, false) };
        }, ct);

    internal async Task RequireCommandVisibilityAsync(Guid ledger, Guid command, OperationsAccessV1 access, CancellationToken ct)
    {
        await access.ActionAsync(OperationsOwnerV1.FinCost, "basis-view", ct);
        var original = await store.ReadCommandAsync(ledger, command, ct)
            ?? throw Error("NOT_FOUND_OR_NOT_VISIBLE", 404);
        await RequireOriginalScopeAsync(ledger, original, access, ct);
    }

    private Task<StandardDocumentReplyV1> WriteAsync(Guid context, StandardDocumentBinding binding,
        Func<LedgerRow, OperationsAccessV1, StandardDocumentBinding, CancellationToken, Task<StandardDocumentReplyV1>> write, CancellationToken ct)
    {
        FinanceCostCommandJournalV1.Binding? prior = null;
        return scope.WithContextAsync(owner, context, "collect", true, async (ledger, access, token) =>
        {
            await access.ActionAsync(OperationsOwnerV1.FinCost,
                prior?.AcceptedAt == null ? "basis-use" : "basis-view", token);
            if (prior == null)
            {
                await journal.BindAsync(ledger.Id, access.ActorId, binding.Formal, token);
                var reply = await write(ledger, access, binding, token);
                await RecordAsync(ledger.Id, binding.Formal, reply, token);
                return reply;
            }

            var original = await store.ReadCommandAsync(ledger.Id, binding.CommandId, token)
                ?? throw Error("COMMAND_IDENTITY_CONFLICT", 409);
            await RequireOriginalScopeAsync(ledger.Id, original, access, token);
            if (!prior.Matches(access.ActorId, prior.AcceptedAt.HasValue ? binding.Formal : binding.Legacy))
                throw Error("COMMAND_IDENTITY_CONFLICT", 409);
            return original.ToReply(binding.CommandId, true);
        }, ct, async (ledger, token) =>
        {
            prior = await journal.FindAsync(ledger.Id, binding.CommandId, token);
            return prior?.AcceptedAt == null ? "collect" : "view";
        });
    }

    private Task RecordAsync(Guid ledger, FinanceCostCommandIdentityV1 binding, StandardDocumentReplyV1 reply, CancellationToken ct)
    {
        var operation = reply.Operation switch
        {
            "CREATE" => "CREATE_STANDARD_DOCUMENT",
            "PREPARE" => "PREPARE_STANDARD_COLLECTION",
            "FREEZE" => "FREEZE_STANDARD_DOCUMENT",
            _ => throw new InvalidOperationException("标准成本单据命令未知。"),
        };
        return journal.RecordAsync(ledger, binding, reply.ReceiptId, operation, reply.DocumentId,
            "STANDARD_DOCUMENT_COMMAND_RESULT", new
            {
                schema = "CP6-STANDARD-DOCUMENT-COMMAND-RESULT-1",
                reply.CommandId, reply.DocumentId, reply.Version, reply.PlanId,
                expiresAt = reply.ExpiresAt is { } expires ? FinanceCostCommandCanonicalV1.Time(expires) : null,
            }, ct);
    }

    private async Task<(Document Document, WorkOrder Order)> LoadDocumentAsync(Guid ledger, Guid id,
        OperationsAccessV1 access, CancellationToken ct)
    {
        var doc = await store.RequireDocumentAsync(ledger, id, ct);
        var order = await source.RequireOrderAsync(ledger, doc.WorkOrderId, access, ct);
        return (doc, order);
    }

    private async Task RequireOriginalScopeAsync(Guid ledger, Command original, OperationsAccessV1 access, CancellationToken ct)
    {
        await LoadDocumentAsync(ledger, original.DocumentId, access, ct);
        if (original.StandardBasisId is Guid standard)
        {
            var basis = await standards.ReadAsync(ledger, standard, ct);
            await ManufacturingStandardBasisSourcesV1.ReadVisibleBasisAsync(basis, access, ct);
        }
    }

    private async Task RequireSelectableBasisAsync(LedgerRow ledger, WorkOrder order, Guid version, OperationsAccessV1 access, CancellationToken ct)
    {
        var basis = await standards.ReadAsync(ledger.Id, version, ct);
        if (basis.WorkOrderId != order.Id)
            throw Error("STANDARD_BASIS_NOT_FOUND", 404);
        if (basis.State == "REVOKED")
            throw Error("STANDARD_BASIS_REVOKED", 409);
        if (!basis.Selected)
            throw Error("STANDARD_BASIS_NOT_SELECTED", 409);
        var date = ledger.BusinessDate;
        if (basis.From > date || basis.ToExclusive <= date)
            throw Error("STANDARD_BASIS_NOT_EFFECTIVE", 409);

        var (body, productCode, currentUnit) = await ManufacturingStandardBasisSourcesV1.ReadVisibleBasisAsync(basis, access, ct);
        if (order.ProductCd != productCode || body.Currency != ledger.Currency)
            throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 409);
        if (body.OutputUnit != currentUnit)
            throw Error("STANDARD_BASIS_UNIT_MISMATCH", 409);
        if (order.Status != WorkOrderStatus.Completed || order.CompletedQty != body.TargetQuantity)
            throw Error("STANDARD_BASIS_TARGET_MISMATCH", 409);

        await source.RequirePolicyAsync(ledger.Id, basis.PolicyVersionId, ledger.Currency, basis.From, basis.ToExclusive, ct);
        await new CostPolicyOriginalV1(db, policySources).AdoptAsync(ledger, basis.PolicyVersionId, access, "STANDARD", ct);
        await RequireApprovals(ct);
        access.AtCommit(RequireApprovals);

        async Task RequireApprovals(CancellationToken token)
        {
            await RequireQualification(basis.Reviewer!.Value, "basis-review", basis.ReviewQualificationId!.Value, token);
            await RequireQualification(basis.Publisher!.Value, "basis-release", basis.ReleaseQualificationId!.Value, token);
        }

        async Task RequireQualification(Guid subject, string action, Guid original, CancellationToken token)
        {
            await owner.RequireCurrentFinanceReviewerAsync(action, subject, token);
            if (await scope.QualificationAsync(ledger.Id, subject, action, token) != original)
                throw Error("FINANCE_QUALIFICATION_REQUIRED", 403);
        }
    }

    private static void RequireHead(Document doc, long? expected)
    {
        if (doc.Version != expected)
            throw Error("HEAD_CHANGED", 412);
    }

    private static void RequireLivePlan(CollectionPlan plan)
    {
        if (plan.ExpiresAt <= DateTimeOffset.UtcNow)
            throw Error("STANDARD_DOCUMENT_PLAN_EXPIRED", 412);
    }
}
