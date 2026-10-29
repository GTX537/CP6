using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using static CP6.Core.Services.Mes.ReturnQualityPolicySourceV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public abstract partial class ReturnDispositionOwnerV2(CP6Context db,OperationsOwnerV1 owner,ReturnQualityOwnerV1 quality,
    ReturnDispositionPolicyV2 policies,StockMovementService stock,StockQcService stockQc)
{
    protected abstract string Kind { get; }
    public ReturnDispositionDefinitionV2 Definition => ReturnDispositionOwnersV2.Get(Kind);
    private const string Journal = "Main.ReturnDisposition.Command.v2";
    private readonly ReturnDispositionStoreV2 store =new(db);
    private readonly ReturnDispositionStockV2 posting =new(db,stock,stockQc);
    private FulfilmentRefV1 Contract => RmaInboundAdapterV1.Ref(Definition.Owner,"RMA-DISPOSITION-2",Definition);
    private Task<T> Run<T>(Guid app,string action,bool write,Func<OperationsAccessV1,ReturnQualityChoiceV1[],CancellationToken,Task<T>> body,
        CancellationToken ct,Func<CancellationToken,Task<string>>? choose =null)
    {
        if (app ==Guid.Empty) throw Error("VALIDATION_FAILED",400);
        return owner.ExecuteAsync(Definition.Resource,action,write,async(a,t) =>
            await body(a,await quality.DispositionSources(a,app,write,t),t),ct,actionAtFence:choose);
    }
    private async Task Scope(OperationsAccessV1 access,RmaQualitySourceV1 source,Guid creator,CancellationToken ct)
    {
        if (access.RmaApplicationSelfOnly && creator !=access.ActorId || !await access.Outbounds().AnyAsync(x => x.Id ==source.OutboundId,ct))
            throw Error("OBJECT_FORBIDDEN",403);
        var location =await db.Locations.AsNoTracking().SingleAsync(x => x.Id ==source.Receipt.LocationId,ct);
        await access.RequireLocation(location.WarehouseCd,location.LocationCd,ct);
    }
    private static ReturnQualityChoiceV1 Source(ReturnQualityChoiceV1[] sources,Guid line)
        => sources.SingleOrDefault(x => x.Source.LineId ==line) ?? throw Error("DISPOSITION_SOURCE_NOT_FOUND",404);
    private static bool Current(ReturnQualityChoiceV1 source,ReturnDispositionPreparationV2 p)
        => source.Inspection?.ObservationCurrent ==true && source.Inspection.Decision?.Result.DecisionRef ==p.Decision.DecisionRef;
    private async Task<ReturnDispositionViewV2> View(OperationsAccessV1 access,ReturnQualityChoiceV1[] sources,Guid id,CancellationToken ct)
    {
        var row =await store.Rows().SingleOrDefaultAsync(x => x.Id ==id && x.Kind ==Kind,ct) ?? throw Error("DISPOSITION_WORK_NOT_FOUND",404);
        var source =Source(sources,row.Line); await Scope(access,source.Source,row.Actor,ct);
        var p =ReturnDispositionStoreV2.Decode<ReturnDispositionPreparationV2>(row.Body);
        var stockSource =p.Stock.Source;
        await access.RequireStockKey(stockSource.Warehouse,stockSource.Location,stockSource.Product,stockSource.Lot,false,ct);
        if (p.Stock.Destination is {} destination)
            await access.RequireStockKey(destination.Warehouse,destination.Location,destination.Product,destination.Lot,true,ct);
        return ReturnDispositionStoreV2.View(row,Current(source,p));
    }
    private async Task<ReturnDispositionChoiceV2[]> Choices(OperationsAccessV1 access,ReturnQualityChoiceV1[] sources,CancellationToken ct)
    {
        var lines =sources.Select(x => x.Source.LineId).ToArray();
        var claims =(await store.Rows().Where(x => x.State !="SEALED_NO_EFFECT" && lines.Contains(x.Line)).ToArrayAsync(ct))
            .Select(x => ReturnDispositionStoreV2.Decode<ReturnDispositionPreparationV2>(x.Body)).ToArray();
        var choices =new List<ReturnDispositionChoiceV2>();
        var bucket =DateTimeOffset.UtcNow.ToUnixTimeSeconds()/900; var expires =DateTimeOffset.FromUnixTimeSeconds((bucket+1)*900);
        foreach (var source in sources)
        {
            await Scope(access,source.Source,source.Source.RegistrarId,ct);
            if (source.Inspection?.Decision ==null) continue;
            var decision =source.Inspection.Decision.Result;
            foreach (var part in decision.Partitions)
            {
                var available =Ranges.Except(Ranges.Read(part.Ranges),claims.Where(x => x.Source.LineId ==source.Source.LineId).SelectMany(x => Ranges.Read(x.Ranges)));
                if (available.Length ==0) continue;
                var options =source.Inspection.ObservationCurrent && part.Quality is "ACCEPTED" or "REJECTED"
                    ? policies.Options(db.CurrentTenantId,source.Source,Kind,part.Quality) : [];
                var reasons =options.Length >0 ? Array.Empty<string>() : new[] { source.Inspection.ObservationCurrent ? "DISPOSITION_NOT_ELIGIBLE" : "CURRENT_QUALITY_OBSERVATION_REQUIRED" };
                var ranges =Ranges.Wire(available); var amount =new FulfilmentQtyV1(Ranges.Text(Ranges.Measure(available)),part.Quantity.Uom);
                var proposal =RmaInboundAdapterV1.Ref(Definition.Owner,source.Source.LineId+":"+part.Quality,new { source.Source.Reference,decision.DecisionRef,part.Quality,ranges,Options =options.Select(x => x.Reference),expires });
                var selection =RmaInboundAdapterV1.Ref(Definition.Owner,source.Source.LineId+":"+part.Quality+":selection",new { proposal,Kind });
                choices.Add(new(source.Source.LineId,source.Inspection.Inspection.Id,new(proposal,selection,source.Source.RmaRef,source.Source.Reference,
                    decision.DecisionRef,part.Quality,ranges,amount,options.Length ==0 ? [] : [Kind],options.Select(x => x.Reference).ToArray(),
                    [source.Source.PhysicalHeadRef,decision.CurrentHeadProof],expires,"COMPLETE",reasons),options));
            }
        }
        return choices.ToArray();
    }
    public Task<ReturnDispositionContextV2> ContextAsync(Guid app,CancellationToken ct =default) => Run(app,"query",false,async(a,s,t) =>
    {
        var works =new List<ReturnDispositionViewV2>();
        foreach (var row in await store.Rows().Where(x => x.App ==app && x.Kind ==Kind).ToArrayAsync(t)) works.Add(await View(a,s,row.Id,t));
        var keys =a.Authorities[0].Actor.ActionKeys;
        return new ReturnDispositionContextV2(Definition,await Choices(a,s,t),works.ToArray(),new[] { "query","prepare","post","seal" }.Where(x => keys.Contains(Definition.Resource+":"+x)).ToArray());
    },ct);
    public Task<ReturnDispositionViewV2> ReadAsync(Guid app,Guid id,CancellationToken ct =default)
        => Run(app,"query",false,(a,s,t) => View(a,s,id,t),ct);
    private Task<ReturnDispositionCommandResultV2> Command<T>(Guid app,Guid command,string action,T input,
        Func<OperationsAccessV1,ReturnQualityChoiceV1[],CancellationToken,Task<ReturnDispositionViewV2>> apply,CancellationToken ct)
    {
        if (command ==Guid.Empty) throw Error("VALIDATION_FAILED",400);
        var bytes =Encode(new { Kind,app,action,input }); var journal =new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original =null;
        return Run(app,action,true,async(a,s,t) =>
        {
            if (original !=null) return await Disclose(a,s,original,app,bytes,t);
            var result =new ReturnDispositionCommandResultV2(action,await apply(a,s,t));
            await journal.Store(Journal,"RMA-DISPOSITION/2",command,a.ActorId,bytes,result,[app],new { result.Value.WorkRef,Kind },t); return result;
        },ct,async t => { original =await journal.Read(Journal,command,t); return original ==null ? action : "query"; });
    }
    private async Task<ReturnDispositionCommandResultV2> Disclose(OperationsAccessV1 a,ReturnQualityChoiceV1[] sources,
        MainOwnerOperationJournalV1.Original original,Guid app,byte[]? bytes,CancellationToken ct)
    {
        if (original.Actor !=a.ActorId || !original.Objects.Contains(app)) throw Error("OBJECT_FORBIDDEN",403);
        if (bytes !=null && !MainOwnerOperationJournalV1.Matches(original,bytes)) throw Error("COMMAND_BINDING_CONFLICT",409);
        var saved =JsonSerializer.Deserialize<ReturnDispositionCommandResultV2>(original.Result)!;
        _ =await View(a,sources,saved.Value.Preparation.WorkId,ct); return saved;
    }
    public Task<ReturnDispositionQueryResultV2> QueryAsync(ReturnDispositionQueryV2 input,CancellationToken ct =default)
    {
        if (input.CommandId ==Guid.Empty) throw Error("VALIDATION_FAILED",400);
        return Run(input.ApplicationId,"query",false,async(a,s,t) =>
        {
            var original =await new MainOwnerOperationJournalV1(db).Read(Journal,input.CommandId,t);
            return original ==null ? new ReturnDispositionQueryResultV2("NOT_OBSERVED",null) : new("FOUND",await Disclose(a,s,original,input.ApplicationId,null,t));
        },ct);
    }
}
