using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingCostMasterStoreV1;
using static CP6.Core.Services.Fin.ManufacturingCostMasterV1;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

public sealed class ManufacturingCostMasterOwnerV1(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 scope,
    CostPolicySourcesV1? policySources = null)
{
    private readonly ManufacturingCostMasterStoreV1 store = new(db);
    private readonly FinanceCostCommandJournalV1 journal = new(db);
    private readonly CostPolicyOriginalV1 policies = new(db, policySources);

    public Task<JsonElement> SubmitPolicyAsync(Guid context, ManufacturingCostPolicySubmitV1 command, CancellationToken ct)
        => WriteAsync(context, "policy-submit", "SUBMIT_COST_POLICY",
            FinanceCostCommandIdentityV1.Create(command.CommandId, $"/{Versions}/policies",
                command, new { command.PreviousVersionId }), async (ledger, access, _, token) =>
            {
                RequireIntervalAndReason(command.From, command.ToExclusive, command.Reason);
                if (command.CurrencyScale is < 0 or > 8 || command.Rounding is not ("HALF_UP" or "HALF_EVEN")
                    || command.RateDateBasis is not ("APPROVED_ACCOUNTING_DATE" or "ACTUAL_INTERVAL_SEGMENTS")
                    || command.Valuation is not ("ACTUAL_OUTPUT" or "STANDARD_OUTPUT_WITH_VARIANCE")
                    || command.Variance is not ("VAR_TO_EXPENSE" or "RETAIN_WIP" or "ALLOCATE_OUTPUT")
                    || !ValidText(command.ApprovalBasisReference, 256))
                    throw Error("COST_POLICY_INPUT_INVALID", 422);
                if (command.Definition != null)
                {
                    CostPolicySourcesV1.Validate(command.Definition);
                    await policies.SourcesAsync(ledger.Scope(db.CurrentTenantId), access, command, false, token);
                }
                return await SubmitAsync(ledger.Id, Policy, command.PreviousVersionId, command.From,
                    access.ActorId, null, command, token);
            }, ct);

    public Task<JsonElement> SubmitBasisAsync(Guid context, ManufacturingRateBasisSubmitV1 command, CancellationToken ct)
        => WriteAsync(context, "rate-basis-submit", "SUBMIT_RATE_BASIS",
            FinanceCostCommandIdentityV1.Create(command.CommandId, $"/{Versions}/rate-bases",
                command, new { command.PreviousVersionId, command.PolicyVersionId }), async (ledger, access, _, token) =>
            {
                RequireIntervalAndReason(command.From, command.ToExclusive, command.Reason);
                if (!ValidText(command.EvidenceReference, 256)
                    || command.ZeroJustification is not null && !ValidText(command.ZeroJustification, 1000))
                    throw Error("RATE_BASIS_INPUT_INVALID", 422);
                var product = await RequireBasisSourceAsync(access, command, access.ActorId, null, token);
                await RequireApprovedPolicyAsync(ledger, command, access, token);
                return await SubmitAsync(ledger.Id, RateBasis, command.PreviousVersionId, command.From,
                    access.ActorId, product, command, token);
            }, ct);

    public Task<JsonElement> ReviewAsync(Guid context, string resource, Guid id, ManufacturingCostMasterDecisionV1 command,
        CancellationToken ct)
    {
        var kind = RequireKind(resource);
        var action = kind == Policy ? "policy-review" : "rate-basis-review";
        return WriteAsync(context, action, kind == Policy ? "APPROVE_COST_POLICY" : "APPROVE_RATE_BASIS",
            FinanceCostCommandIdentityV1.Create(command.CommandId, $"/{Versions}/{resource}/{id:D}/review",
                command, new { versionId = id, command.ExpectedRevision }), async (ledger, access, receipt, token) =>
            {
                var row = await RequireDecisionAsync(ledger.Id, kind, id, "1", command, token);
                if (row.Maker == access.ActorId)
                    throw Error("FINANCE_REVIEW_INDEPENDENCE_REQUIRED", 403);
                var qualification = await scope.QualificationAsync(ledger.Id, access.ActorId, action, token);
                await RequireCurrentBasisAsync(ledger, row, access, token);
                access.AtCommit(async final => { await scope.QualificationAsync(ledger.Id, access.ActorId, action, final); });

                await store.RecordApprovalAsync(ledger.Id, id, receipt, access.ActorId, qualification, command.Reason, token);
                row.ApprovalId = receipt;
                return row;
            }, ct);
    }

    public Task<JsonElement> PublishAsync(Guid context, string resource, Guid id, ManufacturingCostMasterDecisionV1 command,
        CancellationToken ct)
    {
        var kind = RequireKind(resource);
        var review = kind == Policy ? "policy-review" : "rate-basis-review";
        var release = kind == Policy ? "policy-release" : "rate-basis-release";
        return WriteAsync(context, release, kind == Policy ? "PUBLISH_COST_POLICY" : "PUBLISH_RATE_BASIS",
            FinanceCostCommandIdentityV1.Create(command.CommandId, $"/{Versions}/{resource}/{id:D}/publish",
                command, new { versionId = id, command.ExpectedRevision }), async (ledger, access, receipt, token) =>
            {
                var row = await RequireDecisionAsync(ledger.Id, kind, id, "2", command, token);
                var qualification = await scope.QualificationAsync(ledger.Id, access.ActorId, release, token);
                await RequireReviewer(token);
                if (kind != Policy) await RequireCurrentBasisAsync(ledger, row, access, token);
                if (row.PredecessorId is Guid earlier && await store.HasSuccessorAsync(ledger.Id, earlier, token))
                    throw Error("COST_MASTER_SUCCESSOR_CONFLICT", 409);

                if (kind == Policy)
                {
                    await store.PublishPolicyAsync(ledger, row, row.Policy, token);
                    row.FullPolicy = await policies.PublishAsync(ledger, row, access, token);
                }
                else
                    await store.PublishBasisAsync(ledger.Id, row, row.Basis, token);
                await store.RecordReleaseAsync(ledger.Id, id, receipt, access.ActorId, qualification, command.Reason, token);
                row.ReleaseId = receipt;
                access.AtCommit(async final =>
                {
                    await scope.QualificationAsync(ledger.Id, access.ActorId, release, final);
                    await RequireReviewer(final);
                });
                return row;

                async Task RequireReviewer(CancellationToken authorityToken)
                {
                    await owner.RequireCurrentFinanceReviewerAsync(review, row.Reviewer!.Value, authorityToken);
                    if (await scope.QualificationAsync(ledger.Id, row.Reviewer.Value, review, authorityToken) != row.ReviewQualificationId)
                        throw Error("FINANCE_QUALIFICATION_REQUIRED", 403);
                }
            }, ct);
    }

    public Task<object> ReadAsync(Guid context, Guid id, CancellationToken ct)
        => scope.WithContextAsync(owner, context, "view", false, async (ledger, access, token) =>
        {
            await access.ActionAsync(OperationsOwnerV1.FinCost, View, token);
            var row = await store.ReadAsync(ledger.Id, id, token);
            await RequireVisibleSourcesAsync(ledger.Scope(db.CurrentTenantId), row, access, token);
            if (row.Kind == Policy && row.ReleaseId.HasValue) row.FullPolicy = await policies.ReadAsync(ledger.Id, id, token);
            return Snapshot(ledger, row);
        }, ct);

    internal async Task RequireCommandSourcesAsync(LedgerRow ledger, Guid version, OperationsAccessV1 access, CancellationToken ct)
        => await RequireVisibleSourcesAsync(ledger.Scope(db.CurrentTenantId), await store.ReadAsync(ledger.Id, version, ct), access, ct);

    private Task<JsonElement> WriteAsync(Guid context, string action, string operation, FinanceCostCommandIdentityV1 identity,
        Func<LedgerRow, OperationsAccessV1, Guid, CancellationToken, Task<VersionRow>> write, CancellationToken ct)
    {
        FinanceCostCommandJournalV1.Binding? prior = null;
        return scope.WithContextAsync(owner, context, action, true, async (ledger, access, token) =>
        {
            if (prior != null)
            {
                await access.ActionAsync(OperationsOwnerV1.FinCost, View, token);
                if (!prior.AcceptedAt.HasValue || !OwnsCommandRoute(prior.Route))
                    throw Error("COMMAND_IDENTITY_CONFLICT", 409);
                var terminal = await journal.ReadTerminalAsync(ledger.Id, identity.CommandId, token)
                    ?? throw Error("AUTHORITY_UNAVAILABLE", 503);
                await RequireCommandSourcesAsync(ledger, CommandVersion(terminal), access, token);
                if (!prior.Matches(access.ActorId, identity))
                    throw Error("COMMAND_IDENTITY_CONFLICT", 409);
                return FinanceCostCommandJournalV1.JsonBody(terminal.Result);
            }

            var receipt = Guid.NewGuid();
            await journal.BindAsync(ledger.Id, access.ActorId, identity, token);
            var result = await write(ledger, access, receipt, token);
            var snapshot = Snapshot(ledger, result);
            await journal.RecordAsync(ledger.Id, identity, receipt, operation, result.Id,
                "COST_MASTER_COMMAND_RESULT", snapshot, token);
            return JsonSerializer.SerializeToElement(snapshot, Wire);
        }, ct, async (ledger, token) =>
        {
            prior = await journal.FindAsync(ledger.Id, identity.CommandId, token);
            return prior == null ? action : "view";
        });
    }

    private async Task<VersionRow> SubmitAsync(Guid ledger, string kind, Guid? earlier, DateOnly from, Guid maker,
        Guid? product, object body, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var row = new VersionRow
        {
            Id = id, Kind = kind, RootId = id, Sequence = 1, PredecessorId = earlier, Maker = maker,
            ProductId = product, Body = JsonSerializer.Serialize(body, Wire),
        };
        if (earlier is Guid predecessor)
        {
            var previous = await store.ReadAsync(ledger, predecessor, ct);
            if (previous.Kind != kind || previous.ReleaseId == null)
                throw Error("COST_MASTER_PREDECESSOR_NOT_RELEASED", 409);
            DateOnly? end;
            if (body is ManufacturingRateBasisSubmitV1 basis)
            {
                var previousBasis = previous.Basis;
                if (previousBasis.WorkCenterId != basis.WorkCenterId || previousBasis.OperationProfileId != basis.OperationProfileId
                    || previous.ProductId != product)
                    throw Error("RATE_BASIS_PREDECESSOR_SCOPE_MISMATCH", 409);
                end = previousBasis.ToExclusive;
            }
            else
            {
                if (previous.Policy.Definition != null && ((ManufacturingCostPolicySubmitV1)body).Definition == null)
                    throw Error("COST_POLICY_CONTRACT_DOWNGRADE", 409);
                end = previous.Policy.ToExclusive;
            }
            if (end == null || from < end || await store.HasSuccessorAsync(ledger, predecessor, ct))
                throw Error("COST_MASTER_SUCCESSOR_CONFLICT", 409);
            row.RootId = previous.RootId;
            row.Sequence = checked(previous.Sequence + 1);
        }

        await store.InsertAsync(ledger, row, ct);
        return row;
    }

    private async Task<VersionRow> RequireDecisionAsync(Guid ledger, string kind, Guid id, string revision,
        ManufacturingCostMasterDecisionV1 command, CancellationToken ct)
    {
        if (!ValidText(command.Reason, 1000))
            throw Error("COST_MASTER_INPUT_INVALID", 422);
        var row = await store.ReadAsync(ledger, id, ct);
        if (kind != row.Kind)
            throw Error("COST_MASTER_VERSION_NOT_FOUND", 404);
        if (row.Revision != command.ExpectedRevision || row.Revision != revision)
            throw Error("COST_MASTER_VERSION_STALE", 412);
        return row;
    }

    private async Task RequireCurrentBasisAsync(LedgerRow ledger, VersionRow row, OperationsAccessV1 access, CancellationToken ct)
    {
        if (row.Kind == Policy)
        {
            await policies.SourcesAsync(ledger.Scope(db.CurrentTenantId), access, row.Policy, true, ct);
            return;
        }
        var basis = row.Basis;
        if (await RequireBasisSourceAsync(access, basis, row.Maker, null, ct) != row.ProductId)
            throw Error("RATE_BASIS_SOURCE_CHANGED", 409);
        await RequireApprovedPolicyAsync(ledger, basis, access, ct);
    }

    private async Task RequireApprovedPolicyAsync(LedgerRow ledger, ManufacturingRateBasisSubmitV1 basis,
        OperationsAccessV1 access, CancellationToken ct)
    {
        if (!await store.AllowsBasisAsync(ledger, basis, ct))
            throw Error("APPROVED_RATE_BASIS_POLICY_REQUIRED", 422);
        await policies.HoldAsync(ledger, basis.PolicyVersionId, access, null, ct);
    }

    private async Task RequireVisibleSourcesAsync(FinanceScopeV1 scope, VersionRow row, OperationsAccessV1 access, CancellationToken ct)
    {
        if (row.Kind == RateBasis)
            await RequireBasisSourceAsync(access, row.Basis, row.Maker, row.ProductId, ct);
        else
            await policies.SourcesAsync(scope, access, row.Policy, false, ct);
    }

    private async Task<Guid> RequireBasisSourceAsync(OperationsAccessV1 access, ManufacturingRateBasisSubmitV1 basis,
        Guid maker, Guid? recordedProduct, CancellationToken ct)
    {
        var rates = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, ct);
        await rates.RequirePublishedRateCenterAsync(basis.WorkCenterId, maker, ct);
        var products = (await access.SourceAsync(OperationsOwnerV1.Product, ct)).Products();
        var id = recordedProduct is Guid original
            ? await products.Where(product => product.Id == original).Select(product => (Guid?)product.Id).SingleOrDefaultAsync(ct)
            : await (from operation in db.ProductProcesses
                     join product in products on operation.ProductCd equals product.ProductCd
                     where operation.Id == basis.OperationProfileId && !operation.IsDeleted
                     select (Guid?)product.Id).SingleOrDefaultAsync(ct);
        return id ?? throw Error("OPERATION_PROFILE_NOT_FOUND", 404);
    }

    internal static Guid CommandVersion(FinanceCostCommandJournalV1.Terminal terminal)
        => FinanceCostCommandJournalV1.JsonBody(terminal.Receipt).GetProperty("businessKey").GetGuid();

    private static object Snapshot(LedgerRow ledger, VersionRow row)
    {
        using var body = JsonDocument.Parse(row.Body);
        return new
        {
            schema = "CP6-COST-MASTER-VERSION-1", versionId = row.Id, row.RootId, row.Kind,
            version = row.Sequence.ToString(CultureInfo.InvariantCulture), previousVersionId = row.PredecessorId,
            ledger.Currency, row.State, row.Revision, row.ApprovalId, row.ReleaseId, body = body.RootElement.Clone(),
            row.FullPolicy,
        };
    }

    private static string RequireKind(string resource) => resource switch
    {
        "policies" => Policy,
        "rate-bases" => RateBasis,
        _ => throw Error("COST_MASTER_VERSION_NOT_FOUND", 404),
    };

    private static void RequireIntervalAndReason(DateOnly from, DateOnly? until, string? reason)
    {
        if (from == default || until <= from || !ValidText(reason, 1000))
            throw Error("COST_MASTER_INPUT_INVALID", 422);
    }

    private static bool ValidText(string? value, int limit)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Contains('\0');
}
