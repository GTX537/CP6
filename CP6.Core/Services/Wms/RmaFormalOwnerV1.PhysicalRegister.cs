using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private static void PhysicalReferences(FulfilmentRefV1[] values)
    {
        if (values == null || values.Length > 200) throw Error("PHYSICAL_RETURN_INPUT_INVALID",400);
        foreach (var value in values) Reference(value);
        if (values.Distinct().Count() != values.Length) throw Error("PHYSICAL_RETURN_INPUT_INVALID",400);
    }
    public Task<RmaPhysicalWorkViewV1> RegisterPhysicalReturnAsync(Guid application, Guid command,
        RmaRegisterPhysicalReturnV1 input, CancellationToken ct = default)
    {
        Reference(input.ExpectedAuthorizationHeadRef); Reference(input.ExpectedRmaHeadRef);
        if (input.ReceiptLines == null || input.ReceiptLines.Length is < 1 or > 100 || input.Remarks is { Length: < 1 or > 1000 })
            throw Error("PHYSICAL_RETURN_INPUT_INVALID",400);
        foreach (var line in input.ReceiptLines)
        {
            if (line == null || line.LineId == Guid.Empty || line.OccurredAt == default || line.Ranges == null) throw Error("PHYSICAL_RETURN_INPUT_INVALID",400);
            Reference(line.ReturnShareRef); Reference(line.LocationOptionRef); PhysicalReferences(line.LotRefs); PhysicalReferences(line.SerialRefs); PhysicalReferences(line.LpnRefs);
        }
        if (input.ReceiptLines.Select(x => x.LineId).Distinct().Count() != input.ReceiptLines.Length) throw Error("PHYSICAL_RETURN_INPUT_INVALID",400);
        var c = input with { ReceiptLines = input.ReceiptLines.Select(x => x with { Ranges = [..x.Ranges],
            LotRefs = [..x.LotRefs],SerialRefs = [..x.SerialRefs],LpnRefs = [..x.LpnRefs] }).ToArray() };
        var parsed = c.ReceiptLines.ToDictionary(x => x.LineId,x => (Ranges: Ranges.Input(x.Ranges),Quantity: Quantity(x.Quantity)));
        return PhysicalCommand(application,command,"physical-register","REGISTER",c,async (store,a,t) =>
        {
            var app = await Current(store,a,application,t); WriteFields(a,c.Remarks != null);
            var authorization = await ReceivingAuthorization(app,t);
            if (authorization.DecisionRef != c.ExpectedAuthorizationHeadRef || await PhysicalHead(application,false,null,t) != c.ExpectedRmaHeadRef)
                throw Error("CAS_CONFLICT",412);
            var ids = c.ReceiptLines.Select(x => x.LineId).ToArray();
            if (await db.Database.SqlQuery<Guid>($"SELECT line_id AS \"Value\" FROM cp6_rma_formal_v1.physical_lines WHERE tenant_id={db.CurrentTenantId}").AnyAsync(x => ids.Contains(x),t))
                throw Error("PHYSICAL_LINE_ID_REUSED",409);
            var shares = authorization.AuthorizedShares.ToDictionary(x => ShareReference(x));
            var reserved = new Dictionary<Guid,List<Ranges.Segment>>(); var lines = new List<RmaPhysicalLineV1>();
            foreach (var item in c.ReceiptLines)
            {
                if (!shares.TryGetValue(item.ReturnShareRef,out var share)) throw Error("RETURN_SHARE_NOT_CURRENT",409);
                if (item.OccurredAt > DateTimeOffset.UtcNow || item.Quantity.Uom != share.Quantity.Uom || parsed[item.LineId].Quantity != Ranges.Measure(parsed[item.LineId].Ranges))
                    throw Error("PHYSICAL_RETURN_QUANTITY_INVALID",422);
                if (item.LotRefs.Except(share.Original.LotRefs).Any() || item.SerialRefs.Except(share.Original.SerialRefs).Any() || item.LpnRefs.Except(share.Original.LpnRefs).Any()
                    || share.Original.LotRefs.Count > 0 && item.LotRefs.Length == 0) throw Error("PHYSICAL_RETURN_TRACKING_MISMATCH",422);
                var ranges = parsed[item.LineId].Ranges; var claim = Guid.Parse(share.RangeClaimRef.Id);
                if (!Ranges.Subset(ranges,await AvailableReceiptRanges(share,null,t))
                    || reserved.TryGetValue(claim,out var used) && Ranges.Intersects(ranges,used)) throw Error("RETURN_RANGE_NOT_AVAILABLE",409);
                if (!reserved.TryGetValue(claim,out var added)) reserved[claim] = added = [];
                added.AddRange(ranges);
                var selected = await store.Selection<ReceiptLocationSelection>(item.LocationOptionRef,ReceiptLocationOwner,a.ActorId,t);
                if (selected.ApplicationId != application || selected.WarehouseId != app.Scope.WarehouseId) throw Error("RECEIPT_LOCATION_SCOPE_MISMATCH",403);
                var native = await PhysicalLocation(a,app.Scope,selected.LocationId,t);
                var location = new RmaReceiptLocationV1(item.LocationOptionRef,app.Scope.WarehouseId,native.Id,native.WarehouseCd,native.LocationCd,native.LocationName ?? native.LocationCd);
                var reference = new FulfilmentRefV1("Main.Wms.RmaPhysicalLine.v1",item.LineId.ToString(),"1",Digest(new { Input = item,Share = share,Location = location }));
                lines.Add(new(reference,item,share,location));
            }
            var at = DateTimeOffset.UtcNow; var work = Guid.NewGuid(); var actor = await Actor(store,a,t);
            var binding = new RmaCommandBindingV1(command,app.Scope,actor,"POST","/api/wms/rma/v1/applications/{id}/physical/registrations",
                "wms.rma.physical.register","REGISTER",RmaIdentity(application),[authorization.DecisionRef],Digest(c),["RMA-IN:"+work],[c.ExpectedRmaHeadRef],at);
            var bindingRef = await store.Fact("Main.Wms.RmaCommandBinding.v1",a.ActorId,binding,false,t);
            var original = new RmaPhysicalWorkOriginalV1(work,application,a.ActorId,authorization.DecisionRef,View(app).ApplicationRef,lines.ToArray(),c.Remarks,at);
            var row = new PhysicalWorkRow { Id = work,Application = application,Actor = a.ActorId,Revision = 1,State = "REGISTERED",UpdatedAt = at,Original = Encode(original),Binding = Encode(bindingRef) };
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_rma_formal_v1.physical_work(tenant_id,work_id,application_id,actor_id,revision,state,updated_at,original,binding_ref)
                VALUES({db.CurrentTenantId},{work},{application},{a.ActorId},1,{row.State},{at},{row.Original},{row.Binding})
                """,t);
            foreach (var line in lines)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.physical_lines(tenant_id,line_id,work_id,body) VALUES({db.CurrentTenantId},{line.Input.LineId},{work},{Encode(line)})",t);
                var ranges = parsed[line.Input.LineId].Ranges;
                for (var ordinal = 0; ordinal < ranges.Length; ordinal++)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cp6_rma_formal_v1.physical_line_ranges(tenant_id,work_id,line_id,claim_id,ordinal,range_from,range_to)
                        VALUES({db.CurrentTenantId},{work},{line.Input.LineId},{Guid.Parse(line.Share.RangeClaimRef.Id)},{ordinal},{ranges[ordinal].From},{ranges[ordinal].To})
                        """,t);
            }
            return await AppendPhysicalView(row,t);
        },ct);
    }
    private Task<RmaPhysicalWorkViewV1> PhysicalCommand<T>(Guid application, Guid command, string action, string operation, T input,
        Func<RmaFormalStoreV1,OperationsAccessV1,CancellationToken,Task<RmaPhysicalWorkViewV1>> apply, CancellationToken ct)
    {
        if (application == Guid.Empty || command == Guid.Empty) throw Error("VALIDATION_FAILED",400);
        var bytes = Encode(new { application,operation,input }); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Run(action,true,async (store,a,t) =>
        {
            if (original != null) return await DisclosePhysical(store,a,application,original,bytes,t);
            var result = await apply(store,a,t);
            await journal.Store(PhysicalJournal,"WMS-RMA.PHYSICAL/1",command,a.ActorId,bytes,result,[application],new { operation,result.WorkRef,result.RmaHeadRef },t);
            return result;
        },ct,async t => { original = await journal.Read(PhysicalJournal,command,t); return original == null ? action : "query"; });
    }
    private async Task<RmaPhysicalWorkViewV1> DisclosePhysical(RmaFormalStoreV1 store, OperationsAccessV1 a, Guid application,
        MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || !original.Objects.Contains(application)) throw Error("OBJECT_FORBIDDEN",403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original,input)) throw Error("COMMAND_BINDING_CONFLICT",409);
        await Current(store,a,application,ct); var view = JsonSerializer.Deserialize<RmaPhysicalWorkViewV1>(original.Result)!;
        await ReadPhysicalResults(a,view,ct); return view;
    }
    public Task<RmaPhysicalCommandResultV1> QueryPhysicalCommandAsync(RmaPhysicalCommandQueryV1 query, CancellationToken ct = default)
    {
        if (query.ApplicationId == Guid.Empty || query.CommandId == Guid.Empty) throw Error("VALIDATION_FAILED",400);
        return Run("query",false,async (store,a,t) =>
        {
            var original = await new MainOwnerOperationJournalV1(db).Read(PhysicalJournal,query.CommandId,t);
            if (original == null) { await Current(store,a,query.ApplicationId,t); return new RmaPhysicalCommandResultV1("NOT_OBSERVED",null); }
            return new RmaPhysicalCommandResultV1("FOUND",await DisclosePhysical(store,a,query.ApplicationId,original,null,t));
        },ct);
    }
}
