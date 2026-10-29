using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private const string PhysicalOwner = "Main.Wms.RmaPhysicalWork.v1", PhysicalJournal = "Main.Wms.RmaPhysicalCommand.v1",
        ReceiptLocationOwner = "Main.Wms.RmaReceiptLocation.v1";
    private sealed class PhysicalHeadRow
    {
        public int Revision { get; init; }
        public byte[] Reference { get; init; } = null!;
    }
    private sealed record PhysicalWorkRow
    {
        public Guid Id { get; init; }
        public Guid Application { get; init; }
        public Guid Actor { get; init; }
        public int Revision { get; init; }
        public string State { get; init; } = null!;
        public DateTimeOffset UpdatedAt { get; init; }
        public byte[] Original { get; init; } = null!;
        public byte[] Binding { get; init; } = null!;
        public byte[]? Preparation { get; init; }
        public byte[]? Result { get; init; }
        public byte[]? Proof { get; init; }
    }
    private sealed class PhysicalRangeRow
    {
        public Guid Work { get; init; }
        public Guid Claim { get; init; }
        public decimal From { get; init; }
        public decimal To { get; init; }
    }
    private sealed record ReceiptLocationSelection(Guid ApplicationId, Guid WarehouseId, Guid LocationId);
    private static T PhysicalDecode<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes,RmaFormalStoreV1.Json)!;
    private static FulfilmentRefV1 RmaIdentity(Guid app) => new("Main.Wms.RmaFormal.v1",app.ToString(),"1",Digest(new { ApplicationId = app }));
    private static FulfilmentRefV1 PhysicalWorkRef(PhysicalWorkRow row) => new(PhysicalOwner,row.Id.ToString(),row.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Digest(new { row.Id,row.Application,row.Revision,row.State,Original = PhysicalDecode<RmaPhysicalWorkOriginalV1>(row.Original) }));
    private IQueryable<PhysicalWorkRow> PhysicalWorks() => db.Database.SqlQuery<PhysicalWorkRow>($"""
        SELECT work_id AS "Id",application_id AS "Application",actor_id AS "Actor",revision AS "Revision",state AS "State",updated_at AS "UpdatedAt",
          original AS "Original",binding_ref AS "Binding",preparation AS "Preparation",result AS "Result",no_effect_proof AS "Proof"
        FROM cp6_rma_formal_v1.physical_work WHERE tenant_id={db.CurrentTenantId}
        """);
    private IQueryable<PhysicalRangeRow> PendingPhysicalRanges() => db.Database.SqlQuery<PhysicalRangeRow>($"""
        SELECT r.work_id AS "Work",r.claim_id AS "Claim",r.range_from AS "From",r.range_to AS "To"
        FROM cp6_rma_formal_v1.physical_line_ranges r JOIN cp6_rma_formal_v1.physical_work w
          ON w.tenant_id=r.tenant_id AND w.work_id=r.work_id
        WHERE r.tenant_id={db.CurrentTenantId} AND w.state IN('REGISTERED','PREPARED')
        """);
    private async Task<FulfilmentRefV1> PhysicalHead(Guid application, bool advance, Guid? work, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<PhysicalHeadRow>($"SELECT revision AS \"Revision\",reference AS \"Reference\" FROM cp6_rma_formal_v1.physical_heads WHERE tenant_id={db.CurrentTenantId} AND application_id={application}").SingleOrDefaultAsync(ct);
        if (row != null && !advance) return PhysicalDecode<FulfilmentRefV1>(row.Reference);
        var revision = checked((row?.Revision ?? 0) + 1);
        var reference = new FulfilmentRefV1("Main.Wms.RmaHead.v1",application.ToString(),revision.ToString(System.Globalization.CultureInfo.InvariantCulture),Digest(new { application,revision,work }));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_formal_v1.physical_heads(tenant_id,application_id,revision,reference)
            VALUES({db.CurrentTenantId},{application},{revision},{Encode(reference)})
            ON CONFLICT(tenant_id,application_id) DO UPDATE SET revision=EXCLUDED.revision,reference=EXCLUDED.reference
            """,ct);
        return reference;
    }
    private static RmaPhysicalWorkViewV1 PhysicalView(PhysicalWorkRow row, FulfilmentRefV1 head)
    {
        var original = PhysicalDecode<RmaPhysicalWorkOriginalV1>(row.Original);
        var work = new RmaApplicationWorkV1(row.Id,row.Application,"RECEIVE",row.Revision,0,0,row.State,
            PhysicalDecode<FulfilmentRefV1>(row.Binding),[],row.State == "REGISTERED" ? ["PREPARE","SEAL"] : row.State == "PREPARED" ? ["COMMIT","SEAL"] : [],
            original.RecordedAt,row.UpdatedAt);
        return new RmaPhysicalWorkViewV1(PhysicalWorkRef(row),work,original.Lines,head,
            row.Preparation == null ? [] : PhysicalDecode<RmaInboundPreparationV1[]>(row.Preparation),
            row.Result == null ? [] : PhysicalDecode<RmaInboundReceiptResultV1[]>(row.Result),row.Proof == null ? null : PhysicalDecode<FulfilmentRefV1>(row.Proof));
    }
    private async Task<RmaPhysicalWorkViewV1> AppendPhysicalView(PhysicalWorkRow row, CancellationToken ct, FulfilmentRefV1? currentHead = null)
    {
        var head = currentHead ?? await PhysicalHead(row.Application,true,row.Id,ct); var view = PhysicalView(row,head);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.physical_work_history(tenant_id,work_id,revision,body) VALUES({db.CurrentTenantId},{row.Id},{row.Revision},{Encode(view)})",ct);
        return view;
    }
    private async Task<PhysicalWorkRow> PhysicalWork(Guid application, Guid work, CancellationToken ct)
        => await PhysicalWorks().Where(x => x.Application == application && x.Id == work).SingleOrDefaultAsync(ct) ?? throw Error("RMA_PHYSICAL_WORK_NOT_FOUND",404);
    private async Task<RmaAuthorizationDecisionV1> ReceivingAuthorization(RmaFormalOriginalV1 app, CancellationToken ct)
    {
        var current = await AuthorizationCurrent(app.Id,ct) ?? throw Error("AUTHORIZATION_NOT_FOUND",409);
        var at = DateTimeOffset.UtcNow;
        if (app.State != "SUBMITTED" || current.Outcome != "APPROVED" || current.ValidFrom > at || current.ValidTo <= at)
            throw Error("AUTHORIZATION_NOT_EFFECTIVE",409);
        return current;
    }
    private async Task<Location> PhysicalLocation(OperationsAccessV1 a, RmaTrustedScopeV1 scope, Guid id, CancellationToken ct)
    {
        var code = await RequireScope(a,scope,ct);
        var row = await db.Locations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.WarehouseCd == code && !x.IsDeleted,ct)
            ?? throw Error("RECEIPT_LOCATION_NOT_FOUND",404);
        await a.RequireLocation(code,row.LocationCd,ct);
        if (row.IsPickable || row.IsBlocked || row.IsInitializationQuarantined) throw Error("RETURN_LOCATION_NOT_QUARANTINED",409);
        return row;
    }
    private async Task<Ranges.Segment[]> AvailableReceiptRanges(RmaReturnShareV1 share, Guid? excludeWork, CancellationToken ct)
    {
        var claim = Guid.Parse(share.RangeClaimRef.Id);
        var active = await ClaimSegments().Where(x => x.ClaimId == claim && x.State == "ACTIVE_RESERVED").ToArrayAsync(ct);
        var pending = await PendingPhysicalRanges().Where(x => x.Claim == claim && (excludeWork == null || x.Work != excludeWork)).ToArrayAsync(ct);
        return Ranges.Except(active.Select(x => new Ranges.Segment(x.From,x.To)),pending.Select(x => new Ranges.Segment(x.From,x.To)));
    }
    public Task<RmaPhysicalReturnContextV1> PhysicalReturnContextAsync(Guid application, CancellationToken ct = default) => Run("query",true,async (store,a,t) =>
    {
        var app = await Current(store,a,application,t); var head = await PhysicalHead(application,false,null,t);
        var authorization = await AuthorizationCurrent(application,t); var shares = new List<RmaReceiptShareV1>();
        if (authorization?.Outcome == "APPROVED")
            foreach (var share in authorization.AuthorizedShares)
                shares.Add(new(ShareReference(share),share,Ranges.Wire(await AvailableReceiptRanges(share,null,t))));
        var code = await RequireScope(a,app.Scope,t); var locations = new List<RmaReceiptLocationV1>();
        foreach (var location in await db.Locations.AsNoTracking().Where(x => x.WarehouseCd == code && !x.IsDeleted && !x.IsBlocked && !x.IsPickable && !x.IsInitializationQuarantined).OrderBy(x => x.LocationCd).ToArrayAsync(t))
        {
            try { await a.RequireLocation(code,location.LocationCd,t); } catch (CP6.Core.Services.Erp.OrderPriceCorrectionException e) when(e.Status == 403) { continue; }
            var option = await store.Fact(ReceiptLocationOwner,a.ActorId,new ReceiptLocationSelection(application,app.Scope.WarehouseId,location.Id),true,t);
            locations.Add(new(option,app.Scope.WarehouseId,location.Id,code,location.LocationCd,location.LocationName ?? location.LocationCd));
        }
        var works = new List<RmaPhysicalWorkViewV1>();
        foreach (var row in await PhysicalWorks().Where(x => x.Application == application).OrderBy(x => x.Id).ToArrayAsync(t))
        { var view = PhysicalView(row,head); await ReadPhysicalResults(a,view,t); works.Add(view); }
        return new RmaPhysicalReturnContextV1(RmaIdentity(application),head,authorization,shares.ToArray(),locations.ToArray(),works.ToArray());
    },ct);
    public Task<RmaPhysicalWorkViewV1> ReadPhysicalWorkAsync(Guid application, Guid work, int? revision = null, CancellationToken ct = default) => Run("query",false,async (store,a,t) =>
    {
        await Current(store,a,application,t); var row = await PhysicalWork(application,work,t);
        if (revision == null) { var view = PhysicalView(row,await PhysicalHead(application,false,null,t)); await ReadPhysicalResults(a,view,t); return view; }
        var bytes = await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_rma_formal_v1.physical_work_history WHERE tenant_id={db.CurrentTenantId} AND work_id={work} AND revision={revision}").SingleOrDefaultAsync(t)
            ?? throw Error("RMA_PHYSICAL_WORK_VERSION_NOT_FOUND",404);
        var historical = PhysicalDecode<RmaPhysicalWorkViewV1>(bytes); await ReadPhysicalResults(a,historical,t); return historical;
    },ct);
    private Task ReadPhysicalResults(OperationsAccessV1 a, RmaPhysicalWorkViewV1 view, CancellationToken ct)
        => view.Results.Length == 0 ? Task.CompletedTask : Inbound.ReadResults(a,view.Results,ct);
    public Task<JsonElement[]> QueryPhysicalInboundAsync(Guid application, Guid work, CancellationToken ct = default) => Run("query",false,async (store,a,t) =>
    {
        await Current(store,a,application,t); var row = await PhysicalWork(application,work,t);
        var view = PhysicalView(row,await PhysicalHead(application,false,null,t)); await ReadPhysicalResults(a,view,t);
        return await Inbound.Query(view,t);
    },ct);
}
