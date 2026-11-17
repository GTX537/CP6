using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;
using static CP6.Core.Services.Fin.ManufacturingRateStoreV1;

namespace CP6.Core.Services.Fin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingRateDraftV1(Guid WorkCenterId, Guid OperationProfileId, CostCenterVersionLocatorV1 CostCenter,
    Guid PolicyVersionId, Guid BasisId, string LaborRate, string OverheadRate, DateOnly From, DateOnly? ToExclusive);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingRateTransitionV1(long ExpectedRevision);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingRatePublishV1(Guid PreviewToken);

public sealed record ManufacturingRateSegmentV1(Guid RateId, DateOnly From, DateOnly? ToExclusive);
public sealed record ManufacturingRateStatusV1(Guid RateId, string State, long Revision);
public sealed record ManufacturingRatePreviewV1(Guid Token, Guid RateId, long TimelineRevision,
    ManufacturingRateSegmentV1[] Before, ManufacturingRateSegmentV1[] After);

public sealed class ManufacturingRateOwnerV1(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 scope)
{
    private readonly ManufacturingRateStoreV1 store = new(db);
    private const string Draft = "rate-draft";
    private const string Review = "rate-review";
    private const string Release = "rate-release";

    public Task<object> OptionsAsync(string? legalEntityKey, string? ledgerKey, CancellationToken ct)
        => owner.ExecuteAsync<object>(OperationsOwnerV1.FinCost, "view", true, async (access, token) =>
        {
            if ((legalEntityKey == null) != (ledgerKey == null))
                throw Error("FINANCE_CONTEXT_INCOMPLETE", 422);
            await scope.GateAsync(false, token);
            var ledgers = await scope.ListAsync(access.ActorId, token);
            var choices = new List<FinanceContextChoiceV1>(ledgers.Length);
            FinanceScopeV1? selected = null;
            Guid? contextToken = null;
            string[] currencies = [];
            DateOnly? businessDate = null;
            foreach (var ledger in ledgers)
            {
                var issued = await scope.IssueContextAsync(ledger.Id, access.ActorId, token);
                choices.Add(new(issued, ledger.LegalEntityKey, ledger.LedgerKey));
                if (ledger.LegalEntityKey == legalEntityKey && ledger.LedgerKey == ledgerKey)
                {
                    contextToken = issued;
                    selected = ledger.Scope(db.CurrentTenantId);
                    currencies = [ledger.Currency];
                    businessDate = ledger.BusinessDate;
                }
            }
            if (legalEntityKey != null && selected == null)
                throw Error("FINANCE_CONTEXT_NOT_FOUND", 404);
            return new
            {
                contextToken, scope = selected, contexts = choices, financeContextRef = (object?)null,
                availability = selected == null ? "UNPROVEN" : "AVAILABLE",
                actions = Array.Empty<object>(), readFields = Array.Empty<string>(), writeFields = Array.Empty<string>(),
                businessDate, currencies, asOf = DateTimeOffset.UtcNow, isExecutionPermit = false,
            };
        }, ct);

    public Task<object> CentersAsync(Guid context, DateOnly? businessDate, CancellationToken ct)
        => scope.WithContextAsync<object>(owner, context, "view", false, async (ledger, access, token) => new
        {
            items = Array.Empty<object>(), masters = await scope.ListCentersAsync(ledger.Id, access.ActorId,
                businessDate ?? ledger.BusinessDate, token), nextCursor = (string?)null,
            snapshotId = Guid.NewGuid(), coverage = "COMPLETE_AUTHORIZED_VIEW", asOf = DateTimeOffset.UtcNow,
        }, ct);

    public Task<ManufacturingRateStatusV1> DraftAsync(Guid context, ManufacturingRateDraftV1 command, CancellationToken ct)
        => scope.WithContextAsync(owner, context, Draft, true, async (ledger, access, token) =>
        {
            if (command.CostCenter == null || command.From == default || command.ToExclusive <= command.From)
                throw Error("RATE_DRAFT_INVALID", 422);
            await scope.RequireLocatorAsync(ledger.Id, access.ActorId, command.CostCenter, token);
            var rate = new RateRow
            {
                Id = Guid.NewGuid(), Maker = access.ActorId, WorkCenterId = command.WorkCenterId,
                OperationId = command.OperationProfileId, CenterId = command.CostCenter.RootId,
                CenterVersionId = command.CostCenter.VersionId, PolicyVersionId = command.PolicyVersionId, BasisId = command.BasisId,
                From = command.From, ToExclusive = command.ToExclusive,
                LaborRate = ParseRate(command.LaborRate), OverheadRate = ParseRate(command.OverheadRate),
            };
            await RequireSourcesAsync(ledger, access, rate, true, token);
            await store.InsertAsync(ledger.Id, rate, token);
            return new ManufacturingRateStatusV1(rate.Id, "DRAFT", 1);
        }, ct);

    public Task<ManufacturingRateStatusV1> SubmitAsync(Guid context, Guid id, ManufacturingRateTransitionV1 command, CancellationToken ct)
        => scope.WithContextAsync(owner, context, Draft, true, async (ledger, access, token) =>
        {
            var rate = await store.RateAsync(ledger.Id, id, token);
            Expect(rate, command.ExpectedRevision, "DRAFT");
            if (rate.Maker != access.ActorId)
                throw Error("RATE_VERSION_NOT_FOUND", 404);
            await RequireSourcesAsync(ledger, access, rate, true, token);
            await store.SubmitAsync(ledger.Id, rate, token);
            return new ManufacturingRateStatusV1(id, "SUBMITTED", rate.Revision + 1);
        }, ct);

    public Task<ManufacturingRateStatusV1> ReviewAsync(Guid context, Guid id, ManufacturingRateTransitionV1 command, CancellationToken ct)
        => scope.WithContextAsync(owner, context, Review, true, async (ledger, access, token) =>
        {
            var rate = await store.RateAsync(ledger.Id, id, token);
            Expect(rate, command.ExpectedRevision, "SUBMITTED");
            if (rate.Maker == access.ActorId)
                throw Error("RATE_REVIEW_INDEPENDENCE_REQUIRED", 403);
            var qualification = await scope.QualificationAsync(ledger.Id, access.ActorId, Review, token);
            await RequireSourcesAsync(ledger, access, rate, false, token);
            access.AtCommit(async final => { await scope.QualificationAsync(ledger.Id, access.ActorId, Review, final); });
            await store.ApproveAsync(ledger.Id, rate, access.ActorId, qualification, token);
            return new ManufacturingRateStatusV1(id, "APPROVED", rate.Revision + 1);
        }, ct);

    public Task<ManufacturingRatePreviewV1> PreviewAsync(Guid context, Guid id, ManufacturingRateTransitionV1 command, CancellationToken ct)
        => scope.WithContextAsync(owner, context, Release, true, async (ledger, access, token) =>
        {
            var rate = await store.RateAsync(ledger.Id, id, token);
            Expect(rate, command.ExpectedRevision, "APPROVED");
            await RequireReleaseAsync(ledger, access, rate, token);
            var head = await store.HeadAsync(ledger.Id, rate, token);
            var before = await store.SegmentsAsync(ledger.Id, rate, token);
            var key = Guid.NewGuid();
            await store.InsertPreviewAsync(ledger.Id, rate, access.ActorId, key, head, token);
            return new ManufacturingRatePreviewV1(key, rate.Id, head, before, Replace(before, rate));
        }, ct);

    public Task<ManufacturingRateStatusV1> PublishAsync(Guid context, Guid id, ManufacturingRatePublishV1 command, CancellationToken ct)
        => scope.WithContextAsync(owner, context, Release, true, async (ledger, access, token) =>
        {
            var replay = await store.RequirePreviewAsync(ledger.Id, id, access.ActorId, command.PreviewToken, token);
            var rate = await store.RateAsync(ledger.Id, id, token);
            if (replay)
            {
                await scope.QualificationAsync(ledger.Id, access.ActorId, Release, token);
                await RequireSourcesAsync(ledger, access, rate, false, token);
                return new ManufacturingRateStatusV1(id, rate.State, rate.Revision);
            }
            var qualification = await RequireReleaseAsync(ledger, access, rate, token);
            var head = await store.HeadAsync(ledger.Id, rate, token);
            var before = await store.SegmentsAsync(ledger.Id, rate, token);
            await store.ReleaseAsync(ledger.Id, rate, access.ActorId, qualification, command.PreviewToken,
                head, before, Replace(before, rate), token);
            return new ManufacturingRateStatusV1(id, "RELEASED", rate.Revision + 1);
        }, ct);

    private async Task<Guid> RequireReleaseAsync(LedgerRow ledger, OperationsAccessV1 access, RateRow rate, CancellationToken ct)
    {
        if (rate.State != "APPROVED")
            throw Error("RATE_APPROVAL_REQUIRED", 409);
        if (!rate.ReviewOriginalActor)
            throw Error("FINANCE_RATE_REVIEW_ORIGIN_UNPROVEN", 409);
        var qualification = await scope.QualificationAsync(ledger.Id, access.ActorId, Release, ct);
        await RequireReviewerAsync(ct);
        await RequireSourcesAsync(ledger, access, rate, false, ct);
        access.AtCommit(async final =>
        {
            await scope.QualificationAsync(ledger.Id, access.ActorId, Release, final);
            await RequireReviewerAsync(final);
        });
        return qualification;

        async Task RequireReviewerAsync(CancellationToken token)
        {
            await owner.RequireCurrentFinanceReviewerAsync(Review, rate.Reviewer!.Value, token);
            if (await scope.QualificationAsync(ledger.Id, rate.Reviewer.Value, Review, token) != rate.ReviewQualificationId)
                throw Error("FINANCE_QUALIFICATION_REQUIRED", 403);
        }
    }

    private async Task RequireSourcesAsync(LedgerRow ledger, OperationsAccessV1 access, RateRow rate, bool write, CancellationToken ct)
    {
        var grants = await access.ActionAsync(OperationsOwnerV1.ProcessCostRate, write ? "edit" : "query", ct);
        if (write)
        {
            grants.RequireRateWrite("LaborRate");
            grants.RequireRateWrite("OverheadRate");
        }
        else
            grants.RequireUnmaskedRates();
        await grants.RequirePublishedRateCenterAsync(rate.WorkCenterId, rate.Maker, ct);

        var products = await access.SourceAsync(OperationsOwnerV1.Product, ct);
        var visibleProducts = products.Products();
        if (!await db.ProductProcesses.AnyAsync(step => step.Id == rate.OperationId && !step.IsDeleted
            && visibleProducts.Any(product => product.ProductCd == step.ProductCd), ct))
            throw Error("OPERATION_PROFILE_NOT_FOUND", 404);

        await store.RequireInputsAsync(ledger, rate, ct);
    }

    private static void Expect(RateRow rate, long revision, string state)
    {
        if (rate.Revision != revision)
            throw Error("RATE_VERSION_STALE", 412);
        if (rate.State != state)
            throw Error("RATE_STATE_INVALID", 409);
    }

    private static decimal ParseRate(string? value)
    {
        if (value == null || !Regex.IsMatch(value, @"\A(?:0|[1-9][0-9]{0,19})(?:\.[0-9]{1,8})?\z", RegexOptions.CultureInvariant))
            throw Error("RATE_PRECISION_INVALID", 422);
        return decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    private static ManufacturingRateSegmentV1[] Replace(ManufacturingRateSegmentV1[] before, RateRow rate)
    {
        var after = new List<ManufacturingRateSegmentV1>();
        foreach (var segment in before)
        {
            if (segment.ToExclusive <= rate.From || segment.From >= rate.ToExclusive)
            {
                after.Add(segment);
                continue;
            }
            if (segment.From < rate.From)
                after.Add(segment with { ToExclusive = rate.From });
            if (rate.ToExclusive is { } end && (segment.ToExclusive == null || segment.ToExclusive > end))
                after.Add(segment with { From = end });
        }
        after.Add(new(rate.Id, rate.From, rate.ToExclusive));
        return after.OrderBy(segment => segment.From).ToArray();
    }
}
