using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;
using static CP6.Core.Services.Fin.ManufacturingStandardBasisStoreV1;

namespace CP6.Core.Services.Fin;

public sealed class ManufacturingStandardBasisOwnerV1(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 scope,
    CostPolicySourcesV1? policySources = null)
{
    private const string Review = "basis-review";
    private const string Release = "basis-release";
    private readonly ManufacturingStandardBasisStoreV1 store = new(db);
    private readonly ManufacturingStandardBasisSourcesV1 sources = new(db, policySources);

    public Task<StandardBasisStatusV1> SubmitAsync(Guid context, StandardBasisSubmissionV1 command, CancellationToken ct)
        => scope.WithContextAsync<StandardBasisStatusV1>(owner, context, "basis-submit", true, async (ledger, access, token) =>
        {
            RequireReason(command.CommandId, command.Reason);
            if (command.From == default || command.ToExclusive <= command.From || command.Materials == null || command.Times == null
                || command.Materials.Length > 1000 || command.Times.Length > 500
                || command.ZeroBasisReference != null &&
                    (string.IsNullOrWhiteSpace(command.ZeroBasisReference) || command.ZeroBasisReference.Length > 256))
                throw Error("STANDARD_BASIS_SUBMISSION_INVALID", 422);
            var order = await sources.RequireOrderAsync(ledger.Id, command.WorkOrderId, access, token);
            var request = JsonSerializer.Serialize(command);
            var replay = await store.ReplayAsync(ledger.Id, access.ActorId, command.CommandId, request, token);
            if (replay != null)
            {
                if (!replay.Matches)
                    throw Error("FINANCE_COMMAND_CONFLICT", 409);
                return new(replay.Id, replay.Sequence, "SUBMITTED", 1);
            }
            if (await store.HeadAsync(ledger.Id, order.Id, token) != command.PreviousVersionId)
                throw Error("STANDARD_BASIS_HEAD_STALE", 412);
            await sources.RequirePolicyAsync(ledger.Id, command.PolicyVersionId, ledger.Currency, command.From, command.ToExclusive, token);
            var body = await sources.BuildAsync(order, ledger, access, command, token);
            var id = Guid.NewGuid();
            var sequence = await store.InsertAsync(ledger.Id, id, access.ActorId, command, body, request, token);
            return new(id, sequence, "SUBMITTED", 1);
        }, ct);

    public Task<StandardBasisStatusV1> ReviewAsync(Guid context, Guid id, StandardBasisReviewV1 command, CancellationToken ct)
        => scope.WithContextAsync<StandardBasisStatusV1>(owner, context, Review, true, async (ledger, access, token) =>
        {
            RequireReason(command.CommandId, command.Reason);
            var row = await store.ReadAsync(ledger.Id, id, token);
            if (row.Maker == access.ActorId)
                throw Error("FINANCE_REVIEW_INDEPENDENCE_REQUIRED", 403);
            var proven = await scope.QualificationAsync(ledger.Id, access.ActorId, Review, token);
            await RequireVisibleAsync(ledger.Id, row, access, token);
            if (row.Reviewer == access.ActorId && row.ReviewKey == command.CommandId)
                return command.ExpectedRevision == 1 && row.ReviewReason == command.Reason ? At(row, "APPROVED", 2)
                    : throw Error("FINANCE_COMMAND_CONFLICT", 409);
            RequireState(row, command.ExpectedRevision, "SUBMITTED");
            await sources.RequirePolicyAsync(ledger.Id, row.PolicyVersionId, ledger.Currency, row.From, row.ToExclusive, token);
            await new CostPolicyOriginalV1(db, policySources).AdoptAsync(ledger, row.PolicyVersionId, access, "STANDARD", token);
            access.AtCommit(async final => { await scope.QualificationAsync(ledger.Id, access.ActorId, Review, final); });
            await store.ApproveAsync(ledger.Id, id, access.ActorId, proven, command, token);
            return At(row, "APPROVED", 2);
        }, ct);

    public Task<StandardBasisStatusV1> PublishAsync(Guid context, Guid id, StandardBasisReleaseV1 command, CancellationToken ct)
        => scope.WithContextAsync<StandardBasisStatusV1>(owner, context, Release, true, async (ledger, access, token) =>
        {
            if (command.CommandId == Guid.Empty) throw Error("STANDARD_BASIS_COMMAND_INVALID", 422);
            var row = await store.ReadAsync(ledger.Id, id, token);
            var proven = await scope.QualificationAsync(ledger.Id, access.ActorId, Release, token);
            await RequireVisibleAsync(ledger.Id, row, access, token);
            if (row.Publisher == access.ActorId && row.ReleaseKey == command.CommandId)
                return command.ExpectedRevision == 2 ? At(row, "PUBLISHED", 3) : throw Error("FINANCE_COMMAND_CONFLICT", 409);
            RequireState(row, command.ExpectedRevision, "APPROVED");
            if (row.From > ledger.BusinessDate || row.ToExclusive <= ledger.BusinessDate)
                throw Error("STANDARD_BASIS_NOT_EFFECTIVE", 409);
            if (await store.HeadAsync(ledger.Id, row.WorkOrderId, token) != row.PreviousId)
                throw Error("STANDARD_BASIS_HEAD_STALE", 412);
            await sources.RequirePolicyAsync(ledger.Id, row.PolicyVersionId, ledger.Currency, row.From, row.ToExclusive, token);
            await RequireReviewerAsync(token);
            await new CostPolicyOriginalV1(db, policySources).AdoptAsync(ledger, row.PolicyVersionId, access, "STANDARD", token);
            access.AtCommit(async final =>
            {
                await scope.QualificationAsync(ledger.Id, access.ActorId, Release, final);
                await RequireReviewerAsync(final);
            });
            await store.PublishAsync(ledger.Id, row, access.ActorId, proven, command.CommandId, token);
            return At(row, "PUBLISHED", 3);

            async Task RequireReviewerAsync(CancellationToken current)
            {
                var reviewer = row.Reviewer!.Value;
                await owner.RequireCurrentFinanceReviewerAsync(Review, reviewer, current);
                if (await scope.QualificationAsync(ledger.Id, reviewer, Review, current) != row.ReviewQualificationId)
                    throw Error("FINANCE_QUALIFICATION_REQUIRED", 403);
            }
        }, ct);

    public Task<StandardBasisStatusV1> RevokeAsync(Guid context, Guid id, StandardBasisReviewV1 command, CancellationToken ct)
        => scope.WithContextAsync<StandardBasisStatusV1>(owner, context, "basis-revoke", true, async (ledger, access, token) =>
        {
            RequireReason(command.CommandId, command.Reason);
            var row = await store.ReadAsync(ledger.Id, id, token);
            await sources.RequireOrderAsync(ledger.Id, row.WorkOrderId, access, token);
            if (row.Revoker == access.ActorId && row.RevokeKey == command.CommandId)
                return command.ExpectedRevision == 3 && row.RevokeReason == command.Reason ? At(row, "REVOKED", 4)
                    : throw Error("FINANCE_COMMAND_CONFLICT", 409);
            RequireState(row, command.ExpectedRevision, "PUBLISHED");
            await store.RevokeAsync(ledger.Id, id, access.ActorId, command, token);
            return At(row, "REVOKED", 4);
        }, ct);

    public Task<object> ReadAsync(Guid context, Guid id, CancellationToken ct)
        => scope.WithContextAsync<object>(owner, context, "basis-view", false, async (ledger, access, token) =>
        {
            var row = await store.ReadAsync(ledger.Id, id, token);
            var body = await RequireVisibleAsync(ledger.Id, row, access, token);
            return new { version = At(row, row.State, row.Revision), row.Selected,
                row.WorkOrderId, row.PolicyVersionId, row.From, row.ToExclusive, body = body.ToView() };
        }, ct);

    public Task<object> ReadComparisonAsync(Guid context, Guid workOrder, long number, CancellationToken ct)
        => scope.WithContextAsync<object>(owner, context, "basis-view", false, async (ledger, access, token) =>
        {
            await access.ActionAsync(OperationsOwnerV1.FinCost, "view", token);
            var order = await sources.RequireOrderAsync(ledger.Id, workOrder, access, token);
            var sheet = await db.CostSheets.AsNoTracking().Where(row => row.WorkOrderNo == order.WorkOrderNo)
                .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token) ?? throw Error("STANDARD_COMPARISON_NOT_FOUND", 404);
            var version = await CostCollectionBasisStoreV1.ReadAsync(db, sheet, number, token);
            if (version == null || version.Basis.LedgerId != ledger.Id || version.Basis.StandardBasis == null)
                throw Error("STANDARD_COMPARISON_NOT_FOUND", 404);
            var row = await store.ReadAsync(ledger.Id, version.Basis.StandardBasis.VersionId, token);
            var (body, _, _) = await ManufacturingStandardBasisSourcesV1.ReadVisibleBasisAsync(row, access, token);
            var rates = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, token);
            foreach (var rate in version.Basis.Rates)
                await rates.RequirePublishedRateCenterAsync(rate.WorkCenterId, rate.RateMaker!.Value, token);
            var amounts = version.Basis.Amounts;
            return new
            {
                collectionVersion = version.Number, standardBasis = row.Reference, body = body.ToView(),
                actual = StandardBasisBodyV1.DecimalText(amounts.TotalActual), standard = StandardBasisBodyV1.DecimalText(amounts.StandardCost),
                variance = StandardBasisBodyV1.DecimalText(amounts.TotalActual - amounts.StandardCost),
            };
        }, ct);

    private async Task<StandardBasisBodyV1> RequireVisibleAsync(Guid ledger, Row row, OperationsAccessV1 access, CancellationToken ct)
    {
        await sources.RequireOrderAsync(ledger, row.WorkOrderId, access, ct);
        var (body, _, _) = await ManufacturingStandardBasisSourcesV1.ReadVisibleBasisAsync(row, access, ct);
        return body;
    }

    private static void RequireReason(Guid command, string? reason)
    {
        if (command == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw Error("STANDARD_BASIS_COMMAND_INVALID", 422);
    }

    private static void RequireState(Row row, long revision, string state)
    {
        if (row.Revision != revision || row.State != state)
            throw Error("STANDARD_BASIS_VERSION_STALE", 412);
    }

    private static StandardBasisStatusV1 At(Row row, string state, long revision) => new(row.Id, row.Sequence, state, revision);
}
