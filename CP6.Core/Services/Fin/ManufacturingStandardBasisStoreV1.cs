using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

internal sealed class ManufacturingStandardBasisStoreV1(CP6Context db)
{
    private Guid Tenant => db.CurrentTenantId;

    private IQueryable<Row> Versions(Guid ledger) => db.Database.SqlQuery<Row>($"""
        SELECT v.id AS "Id",v.work_order_id AS "WorkOrderId",v.previous_id AS "PreviousId",v.policy_version_id AS "PolicyVersionId",
            v.sequence AS "Sequence",v.state AS "State",v.revision AS "Revision",v.body::text AS "Json",
            v.valid_from AS "From",v.to_exclusive AS "ToExclusive",v.maker AS "Maker",v.reviewer AS "Reviewer",
            v.review_qualification_id AS "ReviewQualificationId",v.review_key AS "ReviewKey",v.review_reason AS "ReviewReason",
            v.publisher AS "Publisher",v.release_key AS "ReleaseKey",v.revoker AS "Revoker",v.revoke_key AS "RevokeKey",
            v.revoke_reason AS "RevokeReason",(h.basis_id=v.id) IS TRUE AS "Selected"
        FROM cp6_fin_mfg_v1.standard_basis_versions v LEFT JOIN cp6_fin_mfg_v1.standard_basis_heads h
            ON (h.tenant_id,h.ledger_id,h.work_order_id)=(v.tenant_id,v.ledger_id,v.work_order_id)
        WHERE v.tenant_id={Tenant} AND v.ledger_id={ledger}
        """);

    internal async Task<Row> ReadAsync(Guid ledger, Guid id, CancellationToken ct)
        => await Versions(ledger).SingleOrDefaultAsync(row => row.Id == id, ct)
            ?? throw Error("STANDARD_BASIS_NOT_FOUND", 404);

    internal Task<Row?> SelectedAsync(Guid ledger, Guid order, CancellationToken ct)
        => Versions(ledger).SingleOrDefaultAsync(row => row.WorkOrderId == order && row.Selected, ct);

    internal Task<Guid?> HeadAsync(Guid ledger, Guid order, CancellationToken ct)
        => db.Database.SqlQuery<Guid?>($"""
            SELECT basis_id AS "Value" FROM cp6_fin_mfg_v1.standard_basis_heads
            WHERE (tenant_id,ledger_id,work_order_id)=({Tenant},{ledger},{order})
            """).SingleOrDefaultAsync(ct);

    internal Task<Replay?> ReplayAsync(Guid ledger, Guid maker, Guid command, string json, CancellationToken ct)
        => db.Database.SqlQuery<Replay>($"""
            SELECT id AS "Id", sequence AS "Sequence",
                submission=CAST({json} AS jsonb) AS "Matches"
            FROM cp6_fin_mfg_v1.standard_basis_versions WHERE (tenant_id,ledger_id,maker,submission_key)=({Tenant},{ledger},{maker},{command})
            """).SingleOrDefaultAsync(ct);

    internal async Task<long> InsertAsync(Guid ledger, Guid version, Guid maker, StandardBasisSubmissionV1 command,
        StandardBasisBodyV1 body, string request, CancellationToken ct)
    {
        var inserted = await db.Database.SqlQuery<long>($"""
            INSERT INTO cp6_fin_mfg_v1.standard_basis_versions
                (tenant_id,ledger_id,work_order_id,id,sequence,previous_id,policy_version_id,valid_from,to_exclusive,body,maker,submission_key,submission)
            SELECT {Tenant},{ledger},{command.WorkOrderId},{version},coalesce(max(sequence),0)+1,
                {command.PreviousVersionId},{command.PolicyVersionId},{command.From},{command.ToExclusive},
                CAST({JsonSerializer.Serialize(body)} AS jsonb),{maker},{command.CommandId},CAST({request} AS jsonb)
            FROM cp6_fin_mfg_v1.standard_basis_versions WHERE (tenant_id,ledger_id,work_order_id)=({Tenant},{ledger},{command.WorkOrderId})
            RETURNING sequence AS "Value"
            """).ToListAsync(ct);
        return inserted.Single();
    }

    internal Task<int> ApproveAsync(Guid ledger, Guid id, Guid reviewer, Guid qualification, StandardBasisReviewV1 command,
        CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.standard_basis_versions SET state='APPROVED',revision=2,
                reviewer={reviewer},review_qualification_id={qualification},review_key={command.CommandId},
                review_reason={command.Reason},reviewed_at=clock_timestamp()
            WHERE (tenant_id,ledger_id,id)=({Tenant},{ledger},{id})
            """, ct);

    internal async Task PublishAsync(Guid ledger, Row row, Guid publisher, Guid qualification, Guid command, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.standard_basis_versions SET state='PUBLISHED',revision=3,
                publisher={publisher},release_qualification_id={qualification},release_key={command},released_at=clock_timestamp()
            WHERE (tenant_id,ledger_id,id)=({Tenant},{ledger},{row.Id})
            """, ct);
        if (row.PreviousId == null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_mfg_v1.standard_basis_heads (tenant_id,ledger_id,work_order_id,basis_id)
                VALUES ({Tenant},{ledger},{row.WorkOrderId},{row.Id})
                """, ct);
        else
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_fin_mfg_v1.standard_basis_heads SET basis_id={row.Id}
                WHERE (tenant_id,ledger_id,work_order_id)=({Tenant},{ledger},{row.WorkOrderId})
                """, ct);
    }

    internal Task<int> RevokeAsync(Guid ledger, Guid id, Guid revoker, StandardBasisReviewV1 command, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.standard_basis_versions SET state='REVOKED',revision=4,revoker={revoker},
                revoke_key={command.CommandId},revoke_reason={command.Reason},revoked_at=clock_timestamp()
            WHERE (tenant_id,ledger_id,id)=({Tenant},{ledger},{id})
            """, ct);

    internal async Task<StandardBasisLineV1[]> PriceAsync(StandardBasisLineV1[] lines, CancellationToken ct)
    {
        try
        {
            var amounts = await db.Database.SqlQuery<AmountRow>($"""
                SELECT r.ordinality AS "Ordinal",round((r.item->>'Quantity')::numeric*(r.item->>'UnitPrice')::numeric,2)
                    ::numeric(28,8) AS "Amount"
                FROM jsonb_array_elements(CAST({JsonSerializer.Serialize(lines)} AS jsonb)) WITH ORDINALITY r(item,ordinality)
                """).OrderBy(row => row.Ordinal).ToArrayAsync(ct);
            return lines.Select((line, index) => line with { Amount = amounts[index].Amount }).ToArray();
        }
        catch (Npgsql.PostgresException error) when (error.SqlState == Npgsql.PostgresErrorCodes.NumericValueOutOfRange)
        {
            throw Error("STANDARD_BASIS_AMOUNT_OUT_OF_RANGE", 422);
        }
    }

    internal sealed class Row
    {
        public Guid Id { get; set; }
        public Guid WorkOrderId { get; set; }
        public Guid? PreviousId { get; set; }
        public Guid PolicyVersionId { get; set; }
        public long Sequence { get; set; }
        public string State { get; set; } = null!;
        public long Revision { get; set; }
        public string Json { get; set; } = null!;
        public DateOnly From { get; set; }
        public DateOnly? ToExclusive { get; set; }
        public Guid Maker { get; set; }
        public Guid? Reviewer { get; set; }
        public Guid? ReviewQualificationId { get; set; }
        public Guid? ReviewKey { get; set; }
        public string? ReviewReason { get; set; }
        public Guid? Publisher { get; set; }
        public Guid? ReleaseKey { get; set; }
        public Guid? Revoker { get; set; }
        public Guid? RevokeKey { get; set; }
        public string? RevokeReason { get; set; }
        public bool Selected { get; set; }
        internal StandardBasisBodyV1 ReadBody() => JsonSerializer.Deserialize<StandardBasisBodyV1>(Json)!;
        internal StandardBasisReferenceV1 Reference => new(Id, Sequence, PolicyVersionId);
    }

    internal sealed class Replay
    {
        public Guid Id { get; set; }
        public long Sequence { get; set; }
        public bool Matches { get; set; }
    }

    internal sealed class AmountRow
    {
        public long Ordinal { get; set; }
        public decimal Amount { get; set; }
    }
}
