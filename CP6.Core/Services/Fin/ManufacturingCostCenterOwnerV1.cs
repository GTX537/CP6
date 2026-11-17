using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingCenterSubmissionV1(Guid CommandId, Guid RootId, Guid OperationId,
    DateOnly From, DateOnly? ToExclusive, string Reason, Guid? SupersedesVersionId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingCenterReviewV1(Guid CommandId, long ExpectedRevision, string Decision, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingCenterPublishV1(Guid CommandId, long ExpectedRevision);

public sealed record ManufacturingCenterEditionV1(Guid VersionId, Guid RootId, string State, long Revision);

public sealed class ManufacturingCostCenterOwnerV1(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 scope)
{
    private const string ReviewAction = "center-review";
    private const string ReleaseAction = "center-release";
    private Guid Tenant => db.CurrentTenantId;

    public Task<ManufacturingCenterEditionV1> SubmitAsync(Guid context, ManufacturingCenterSubmissionV1 command, CancellationToken ct)
        => scope.WithContextAsync<ManufacturingCenterEditionV1>(owner, context, "center-submit", true, async (ledger, access, token) =>
        {
            if (command.CommandId == Guid.Empty || command.From == default || command.ToExclusive <= command.From
                || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000)
                throw Error("COST_CENTER_SUBMISSION_INVALID", 422);
            var request = JsonSerializer.Serialize(command);
            var existing = await db.Database.SqlQuery<SubmissionReplay>($"""
                SELECT id AS "Id",root_id AS "RootId", (submission=CAST({request} AS jsonb)) AS "Matches"
                FROM cp6_fin_mfg_v1.center_submissions
                WHERE tenant_id={Tenant} AND ledger_id={ledger.Id} AND maker={access.ActorId} AND submission_key={command.CommandId}
                """).SingleOrDefaultAsync(token);
            if (existing != null && !existing.Matches)
                throw Error("FINANCE_COMMAND_CONFLICT", 409);
            var sourceVersion = await RequireOperationAsync(access, command.OperationId, token);
            if (existing != null)
                return new(existing.Id, existing.RootId, "SUBMITTED", 1);
            var root = await ReadRootAsync(ledger.Id, command.RootId, token);
            RequireEffectiveRange(ledger, command.From);
            var version = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_mfg_v1.center_submissions
                    (tenant_id,ledger_id,id,root_id,operation_id,operation_version,code,name,valid_from,to_exclusive,
                     supersedes_version_id,maker,submission_key,submission)
                VALUES ({Tenant},{ledger.Id},{version},{root.Id},{command.OperationId},{sourceVersion},{root.Code},{root.Name},
                    {command.From},{command.ToExclusive},{command.SupersedesVersionId},{access.ActorId},
                    {command.CommandId},CAST({request} AS jsonb))
                """, token);
            return new(version, root.Id, "SUBMITTED", 1);
        }, ct);

    public Task<ManufacturingCenterEditionV1> ReviewAsync(Guid context, Guid version, ManufacturingCenterReviewV1 command,
        CancellationToken ct) => scope.WithContextAsync<ManufacturingCenterEditionV1>(owner, context, ReviewAction, true,
        async (ledger, access, token) =>
        {
            if (command.CommandId == Guid.Empty || command.Decision is not ("APPROVE" or "REJECT")
                || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000)
                throw Error("COST_CENTER_REVIEW_INVALID", 422);
            var row = await ReadAsync(ledger.Id, version, token);
            if (row.Maker == access.ActorId)
                throw Error("FINANCE_REVIEW_INDEPENDENCE_REQUIRED", 403);
            var proof = await scope.QualificationAsync(ledger.Id, access.ActorId, ReviewAction, token);
            var sourceVersion = await RequireOperationAsync(access, row.OperationId, token);
            var approved = command.Decision == "APPROVE";
            var state = approved ? "APPROVED" : "REJECTED";
            if (row.Reviewer == access.ActorId && row.ReviewKey == command.CommandId)
                return JsonSerializer.Deserialize<ManufacturingCenterReviewV1>(row.Review!) == command
                    ? new(version, row.RootId, state, 2) : throw Error("FINANCE_COMMAND_CONFLICT", 409);
            if (row.State != "SUBMITTED" || row.Revision != command.ExpectedRevision)
                throw Error("COST_CENTER_VERSION_STALE", 412);
            if (approved)
            {
                RequireProcessVersion(row, sourceVersion);
                RequireEffectiveRange(ledger, row.From);
                await RequireRootAsync(ledger.Id, row, token);
            }
            access.AtCommit(async final => { await scope.QualificationAsync(ledger.Id, access.ActorId, ReviewAction, final); });
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_fin_mfg_v1.center_submissions
                SET state={state},revision=2,reviewer={access.ActorId},review_qualification_id={proof},
                    review_key={command.CommandId},review=CAST({JsonSerializer.Serialize(command)} AS jsonb),reviewed_at=clock_timestamp()
                WHERE tenant_id={Tenant} AND ledger_id={ledger.Id} AND id={version}
                """, token);
            return new(version, row.RootId, state, 2);
        }, ct);

    public Task<ManufacturingCenterEditionV1> PublishAsync(Guid context, Guid version, ManufacturingCenterPublishV1 command,
        CancellationToken ct) => scope.WithContextAsync<ManufacturingCenterEditionV1>(owner, context, ReleaseAction, true,
        async (ledger, access, token) =>
        {
            if (command.CommandId == Guid.Empty)
                throw Error("COST_CENTER_RELEASE_INVALID", 422);
            var row = await ReadAsync(ledger.Id, version, token);
            var proof = await scope.QualificationAsync(ledger.Id, access.ActorId, ReleaseAction, token);
            var sourceVersion = await RequireOperationAsync(access, row.OperationId, token);
            if (row.Publisher == access.ActorId && row.ReleaseKey == command.CommandId)
                return command.ExpectedRevision == 2 ? new(version, row.RootId, "RELEASED", 3)
                    : throw Error("FINANCE_COMMAND_CONFLICT", 409);
            if (row.State != "APPROVED" || row.Revision != command.ExpectedRevision)
                throw Error("COST_CENTER_VERSION_STALE", 412);
            RequireProcessVersion(row, sourceVersion);
            RequireEffectiveRange(ledger, row.From);
            await RequireRootAsync(ledger.Id, row, token);
            await RequireReviewerAsync(ledger.Id, row, token);
            access.AtCommit(async final =>
            {
                await scope.QualificationAsync(ledger.Id, access.ActorId, ReleaseAction, final);
                await RequireReviewerAsync(ledger.Id, row, final);
            });
            var overlaps = await db.Database.SqlQuery<Guid>($"""
                SELECT v.version_id AS "Value" FROM cp6_fin_mfg_v1.center_calendar v
                WHERE v.tenant_id={Tenant} AND v.ledger_id={ledger.Id} AND v.root_id={row.RootId}
                    AND v.effective_dates && datemultirange(daterange({row.From},{row.ToExclusive},'[)'))
                """).ToArrayAsync(token);
            if (row.SupersedesVersionId is Guid previous ? overlaps.Length != 1 || overlaps[0] != previous : overlaps.Length != 0)
                throw Error("COST_CENTER_RELEASE_INTERVAL_CONFLICT", 409);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_fin_mfg_v1.center_submissions
                SET state='RELEASED',revision=3,publisher={access.ActorId},release_qualification_id={proof},
                    release_key={command.CommandId},released_at=clock_timestamp()
                WHERE tenant_id={Tenant} AND ledger_id={ledger.Id} AND id={version};
                INSERT INTO cp6_fin_mfg_v1.center_versions
                    (tenant_id,ledger_id,root_id,version_id,version,code,name,valid_from,to_exclusive,approval_id,submission_id)
                SELECT {Tenant},{ledger.Id},{row.RootId},{version},COALESCE(max(v.version),0)+1,
                    {row.Code},{row.Name},{row.From},{row.ToExclusive},{version},{version}
                FROM cp6_fin_mfg_v1.center_versions v WHERE v.tenant_id={Tenant} AND v.ledger_id={ledger.Id} AND v.root_id={row.RootId}
                """, token);
            return new(version, row.RootId, "RELEASED", 3);
        }, ct);

    private async Task RequireReviewerAsync(Guid ledger, EditionRow row, CancellationToken ct)
    {
        await owner.RequireCurrentFinanceReviewerAsync(ReviewAction, row.Reviewer!.Value, ct);
        if (await scope.QualificationAsync(ledger, row.Reviewer.Value, ReviewAction, ct) != row.ReviewQualificationId)
            throw Error("FINANCE_QUALIFICATION_REQUIRED", 403);
    }

    private async Task<CostCenter> ReadRootAsync(Guid ledger, Guid rootId, CancellationToken ct)
    {
        var root = await db.CostCenters.AsNoTracking().SingleOrDefaultAsync(center => center.Id == rootId && center.IsActive, ct)
            ?? throw Error("COST_CENTER_NOT_FOUND", 404);
        if (root.Type != CostCenterType.Process || root.ParentId.HasValue || root.LinkMachineId != null
            || !Regex.IsMatch(root.Code, @"\A[A-Z0-9][A-Z0-9._-]{0,29}\z", RegexOptions.CultureInvariant)
            || string.IsNullOrWhiteSpace(root.Name) || root.Name != root.Name.Trim() || root.Name.Any(char.IsControl))
            throw Error("PROCESS_CENTER_ADOPTION_UNAVAILABLE", 409);
        if (await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_versions
                WHERE tenant_id={Tenant} AND root_id={rootId} AND ledger_id<>{ledger}) AS "Value"
            """).SingleAsync(ct))
            throw Error("COST_CENTER_NOT_FOUND", 404);
        return root;
    }

    private async Task RequireRootAsync(Guid ledger, EditionRow row, CancellationToken ct)
    {
        var root = await ReadRootAsync(ledger, row.RootId, ct);
        if (root.Code != row.Code || root.Name != row.Name)
            throw Error("COST_CENTER_SOURCE_CHANGED", 409);
    }

    private async Task<byte[]> RequireOperationAsync(OperationsAccessV1 access, Guid id, CancellationToken ct)
    {
        var product = await access.SourceAsync(OperationsOwnerV1.Product, ct);
        var visible = product.Products();
        var operation = await db.ProductProcesses.AsNoTracking().Where(process => process.Id == id && !process.IsDeleted
                && visible.Any(candidate => candidate.ProductCd == process.ProductCd))
            .Select(process => new { process.RowVersion }).SingleOrDefaultAsync(ct)
            ?? throw Error("OPERATION_PROFILE_NOT_FOUND", 404);
        return operation.RowVersion ?? throw Error("OPERATION_PROFILE_VERSION_UNAVAILABLE", 409);
    }

    private static void RequireProcessVersion(EditionRow row, byte[] current)
    {
        if (!row.OperationVersion.AsSpan().SequenceEqual(current))
            throw Error("OPERATION_PROFILE_VERSION_CHANGED", 409);
    }

    private static void RequireEffectiveRange(LedgerRow ledger, DateOnly from)
    {
        if (from < ledger.BusinessDate)
            throw Error("COST_CENTER_RELEASE_BACKDATED", 422);
    }

    private async Task<EditionRow> ReadAsync(Guid ledger, Guid version, CancellationToken ct)
        => await db.Database.SqlQuery<EditionRow>($"""
            SELECT root_id AS "RootId",operation_id AS "OperationId",operation_version AS "OperationVersion",
                code AS "Code",name AS "Name",valid_from AS "From",to_exclusive AS "ToExclusive",
                maker AS "Maker",state AS "State",revision AS "Revision",reviewer AS "Reviewer",
                review_qualification_id AS "ReviewQualificationId",review_key AS "ReviewKey",review::text AS "Review",
                publisher AS "Publisher",release_key AS "ReleaseKey",supersedes_version_id AS "SupersedesVersionId"
            FROM cp6_fin_mfg_v1.center_submissions WHERE tenant_id={Tenant} AND ledger_id={ledger} AND id={version}
            """).SingleOrDefaultAsync(ct) ?? throw Error("COST_CENTER_VERSION_NOT_FOUND", 404);

    private sealed class SubmissionReplay
    {
        public Guid Id { get; set; }
        public Guid RootId { get; set; }
        public bool Matches { get; set; }
    }

    private sealed class EditionRow
    {
        public Guid RootId { get; set; }
        public Guid OperationId { get; set; }
        public byte[] OperationVersion { get; set; } = null!;
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public DateOnly From { get; set; }
        public DateOnly? ToExclusive { get; set; }
        public Guid Maker { get; set; }
        public string State { get; set; } = null!;
        public long Revision { get; set; }
        public Guid? Reviewer { get; set; }
        public Guid? ReviewQualificationId { get; set; }
        public Guid? ReviewKey { get; set; }
        public string? Review { get; set; }
        public Guid? Publisher { get; set; }
        public Guid? ReleaseKey { get; set; }
        public Guid? SupersedesVersionId { get; set; }
    }
}
