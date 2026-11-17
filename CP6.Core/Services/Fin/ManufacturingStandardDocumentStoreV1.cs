using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;
using static CP6.Core.Services.Fin.StandardDocumentBinding;

namespace CP6.Core.Services.Fin;

internal sealed class ManufacturingStandardDocumentStoreV1(CP6Context db)
{
    private Guid Tenant => db.CurrentTenantId;

    private IQueryable<Document> DocumentsIn(Guid ledger) => db.Database.SqlQuery<Document>($"""
        SELECT id AS "Id",work_order_id AS "WorkOrderId",current_version AS "Version"
        FROM cp6_fin_mfg_v1.standard_cost_documents WHERE tenant_id={Tenant} AND ledger_id={ledger}
        """);

    internal async Task<Document> RequireDocumentAsync(Guid ledger, Guid id, CancellationToken ct)
        => await DocumentsIn(ledger).SingleOrDefaultAsync(row => row.Id == id, ct)
            ?? throw Error("STANDARD_DOCUMENT_NOT_FOUND", 404);

    internal Task<Document?> FindByWorkOrderAsync(Guid ledger, Guid workOrder, CancellationToken ct)
        => DocumentsIn(ledger).SingleOrDefaultAsync(row => row.WorkOrderId == workOrder, ct);

    internal async Task CreateAsync(Guid ledger, Guid document, Guid workOrder, Guid actor, string reason, CancellationToken ct)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.standard_cost_documents (tenant_id,ledger_id,id,work_order_id,created_by,reason)
            VALUES ({Tenant},{ledger},{document},{workOrder},{actor},{reason})
            ON CONFLICT (tenant_id,ledger_id,work_order_id) DO NOTHING
            """, ct) != 1)
            throw Error("STANDARD_DOCUMENT_EXISTS", 409);
    }

    internal async Task<CollectionPlan> RequirePlanAsync(Guid ledger, Guid document, Guid plan, Guid actor, CancellationToken ct)
        => await db.Database.SqlQuery<CollectionPlan>($"""
            SELECT standard_basis_id AS "StandardBasisId",expected_version AS "ExpectedVersion",
                accounting_date AS "AccountingDate",expires_at AS "ExpiresAt"
            FROM cp6_fin_mfg_v1.standard_collection_plans
            WHERE (tenant_id,ledger_id,document_id,id,actor_id)=({Tenant},{ledger},{document},{plan},{actor})
            """).SingleOrDefaultAsync(ct) ?? throw Error("STANDARD_DOCUMENT_PLAN_NOT_FOUND", 404);

    internal async Task PrepareAsync(Guid ledger, Document document, Guid plan, Guid basis, DateOnly date,
        Guid actor, DateTimeOffset expiresAt, string reason, CancellationToken ct)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.standard_collection_plans
                (tenant_id,ledger_id,document_id,id,work_order_id,expected_version,standard_basis_id,accounting_date,actor_id,expires_at,reason)
            VALUES ({Tenant},{ledger},{document.Id},{plan},{document.WorkOrderId},{document.Version},{basis},{date},{actor},{expiresAt},{reason})
            """, ct) != 1)
            throw Error("STANDARD_DOCUMENT_PLAN_NOT_WRITTEN", 409);
    }

    internal async Task FreezeAsync(Guid ledger, Document document, long version, Guid plan, Guid basis, DateOnly date,
        Guid actor, string reason, CancellationToken ct)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.standard_document_versions
                (tenant_id,ledger_id,document_id,work_order_id,version,standard_basis_id,plan_id,accounting_date,frozen_by,reason)
            VALUES ({Tenant},{ledger},{document.Id},{document.WorkOrderId},{version},{basis},{plan},{date},{actor},{reason})
            """, ct) != 1)
            throw Error("STANDARD_DOCUMENT_VERSION_NOT_WRITTEN", 409);
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.standard_cost_documents SET current_version={version}
            WHERE (tenant_id,ledger_id,id)=({Tenant},{ledger},{document.Id}) AND current_version IS NOT DISTINCT FROM {document.Version}
            """, ct) != 1)
            throw Error("HEAD_CHANGED", 412);
    }

    internal async Task<Edition> RequireEditionAsync(Guid ledger, Guid document, long version, CancellationToken ct)
        => await db.Database.SqlQuery<Edition>($"""
            SELECT standard_basis_id AS "StandardBasisId",accounting_date AS "AccountingDate",
                frozen_at AS "FrozenAt"
            FROM cp6_fin_mfg_v1.standard_document_versions
            WHERE (tenant_id,ledger_id,document_id,version)=({Tenant},{ledger},{document},{version})
            """).SingleOrDefaultAsync(ct) ?? throw Error("STANDARD_DOCUMENT_VERSION_NOT_FOUND", 404);

    internal Task<Command?> ReadCommandAsync(Guid ledger, Guid command, CancellationToken ct)
        => db.Database.SqlQuery<Command>($"""
            SELECT c.document_id AS "DocumentId",c.receipt_id AS "ReceiptId",c.operation AS "Operation",
                c.plan_id AS "PlanId",c.version AS "Version",p.expires_at AS "ExpiresAt",p.standard_basis_id AS "StandardBasisId"
            FROM cp6_fin_mfg_v1.standard_document_commands c LEFT JOIN cp6_fin_mfg_v1.standard_collection_plans p
                ON (p.tenant_id,p.ledger_id,p.document_id,p.id)=(c.tenant_id,c.ledger_id,c.document_id,c.plan_id)
            WHERE (c.tenant_id,c.ledger_id,c.command_id)=({Tenant},{ledger},{command})
            """).SingleOrDefaultAsync(ct);

    internal async Task<StandardDocumentReplyV1> RecordCommandAsync(Guid ledger, Guid actor, StandardDocumentBinding binding,
        string operation, Guid document, CancellationToken ct, long? version = null, Guid? plan = null, DateTimeOffset? expiresAt = null)
    {
        var receipt = Guid.NewGuid();
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.standard_document_commands
                (tenant_id,ledger_id,command_id,receipt_id,principal,method,route,body_digest,expected_digest,operation,document_id,plan_id,version,r2_bound)
            VALUES ({Tenant},{ledger},{binding.CommandId},{receipt},{actor},'POST',{binding.Route},{binding.BodyDigest},
                {binding.ExpectedDigest},{operation},{document},{plan},{version},true)
            """, ct) != 1)
            throw Error("STANDARD_DOCUMENT_COMMAND_NOT_WRITTEN", 409);
        return new(binding.CommandId, receipt, operation, document, VersionText(version), plan, expiresAt, false);
    }

    internal sealed class Document
    {
        public Guid Id { get; set; }
        public Guid WorkOrderId { get; set; }
        public long? Version { get; set; }
    }

    internal sealed class CollectionPlan
    {
        public Guid StandardBasisId { get; set; }
        public long? ExpectedVersion { get; set; }
        public DateOnly AccountingDate { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    internal sealed class Edition
    {
        public Guid StandardBasisId { get; set; }
        public DateOnly AccountingDate { get; set; }
        public DateTimeOffset FrozenAt { get; set; }
    }

    internal sealed class Command
    {
        public Guid DocumentId { get; set; }
        public Guid ReceiptId { get; set; }
        public string Operation { get; set; } = null!;
        public Guid? PlanId { get; set; }
        public long? Version { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public Guid? StandardBasisId { get; set; }

        internal StandardDocumentReplyV1 ToReply(Guid command, bool replayed) => new(command, ReceiptId, Operation,
            DocumentId, VersionText(Version), PlanId, Operation == "PREPARE" ? ExpiresAt : null, replayed);
    }
}
