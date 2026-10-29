using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using static CP6.Core.Services.Mes.ReturnQualityPolicySourceV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Mes;

/// <summary>Independent Main Quality return inspection and decision Owner. RMA observation never signs a decision.</summary>
public sealed partial class ReturnQualityOwnerV1(CP6Context db, OperationsOwnerV1 owner, RmaFormalOwnerV1 rma, ReturnQualityPolicySourceV1 policies)
{
    private const string Journal = "Main.Quality.ReturnCommand.v1";
    internal static readonly string[] States = ["ACCEPTED","REJECTED","HOLD","PENDING"];
    private static readonly FulfilmentRefV1 Contract = RmaInboundAdapterV1.Ref("QA-NCR-01.ReturnAdapter","RMA-QUALITY-1",new { Version = 1,Owner = "QA-NCR-01" });
    private readonly ReturnQualityStoreV1 store = new(db);
    internal async Task<ReturnQualityChoiceV1[]> DispositionSources(OperationsAccessV1 parent,Guid app,bool write,CancellationToken ct)
    {
        await rma.QualityFence(write,ct);
        var ra =await parent.SourceAsync(OperationsOwnerV1.Rma,ct); var qa =await parent.SourceAsync(OperationsOwnerV1.ReturnQuality,ct);
        var sources =await rma.QualitySources(ra,app,ct); var rows =await store.Heads().Where(x => x.Application ==app).ToArrayAsync(ct);
        var result =new List<ReturnQualityChoiceV1>();
        foreach (var source in sources)
        {
            var row =rows.SingleOrDefault(x => x.Line ==source.LineId); var view =row ==null ? null : await View(qa,sources,row.Id,ct);
            await Scope(qa,source,view?.Inspection.CreatorId ?? source.RegistrarId,ct); result.Add(new(source,view,null));
        }
        return result.ToArray();
    }
    private Task<T> Run<T>(Guid app, string resource, string action, bool write,
        Func<OperationsAccessV1,OperationsAccessV1,RmaQualitySourceV1[],CancellationToken,Task<T>> body,
        CancellationToken ct, Func<CancellationToken,Task<string>>? choose = null)
    {
        if (app == Guid.Empty) throw Error("VALIDATION_FAILED",400);
        return owner.ExecuteAsync(resource,action,write,async (a,t) =>
        {
            await rma.QualityFence(write,t);
            var ra = resource == OperationsOwnerV1.Rma ? a : await a.SourceAsync(OperationsOwnerV1.Rma,t);
            var qa = resource == OperationsOwnerV1.ReturnQuality ? a : await a.SourceAsync(OperationsOwnerV1.ReturnQuality,t);
            return await body(a,qa,await rma.QualitySources(ra,app,t),t);
        },ct,actionAtFence:choose);
    }
    private static RmaQualitySourceV1 Source(RmaQualitySourceV1[] sources, Guid line)
        => sources.SingleOrDefault(x => x.LineId == line) ?? throw Error("QUALITY_RECEIPT_NOT_FOUND",404);
    private static async Task Scope(OperationsAccessV1 qa, RmaQualitySourceV1 source, Guid creator, CancellationToken ct)
    {
        if (qa.RmaApplicationSelfOnly && creator != qa.ActorId || !await qa.Outbounds().AnyAsync(x => x.Id == source.OutboundId,ct))
            throw Error("OBJECT_FORBIDDEN",403);
    }
    private ReturnQualityPolicyV1 Policy(RmaQualitySourceV1 source, FulfilmentRefV1 expected)
    {
        var value = policies.Find(db.CurrentTenantId,source) ?? throw Error("QUALITY_POLICY_NOT_CONFIGURED",503);
        if (value.Reference != expected) throw Error("QUALITY_POLICY_CHANGED",412); return value;
    }
    private async Task<ReturnQualityViewV1> View(OperationsAccessV1 qa,RmaQualitySourceV1[] sources,Guid id,CancellationToken ct,int? revision = null)
    {
        var row = await store.Heads().SingleOrDefaultAsync(x => x.Id == id,ct) ?? throw Error("QUALITY_INSPECTION_NOT_FOUND",404);
        var source = Source(sources,row.Line); var value = await store.View(row,revision,ct);
        if (value.Inspection.Source.Reference != source.Reference) throw Error("QUALITY_RECEIPT_CHANGED",409);
        await Scope(qa,source,value.Inspection.CreatorId,ct); return value;
    }
    public Task<ReturnQualityContextV1> ContextAsync(Guid app,CancellationToken ct = default) => Run(app,OperationsOwnerV1.ReturnQuality,"query",false,async (a,qa,sources,t) =>
    {
        var rows = await store.Heads().Where(x => x.Application == app).ToArrayAsync(t); var result = new List<ReturnQualityChoiceV1>();
        foreach (var source in sources)
        {
            var row = rows.SingleOrDefault(x => x.Line == source.LineId); var view = row == null ? null : await store.View(row,null,t);
            await Scope(qa,source,view?.Inspection.CreatorId ?? source.RegistrarId,t);
            result.Add(new(source,view,policies.Find(db.CurrentTenantId,source)));
        }
        var keys = a.Authorities[0].Actor.ActionKeys;
        return new ReturnQualityContextV1(app,result.ToArray(),new[] { "query","start","decide","withdraw" }.Where(x => keys.Contains(OperationsOwnerV1.ReturnQuality+":"+x)).Concat(keys.Contains("wms-rma:quality-observe") ? ["observe"] : []).ToArray());
    },ct);
    public Task<ReturnQualityViewV1> ReadAsync(Guid app,Guid id,int? revision = null,CancellationToken ct = default)
        => Run(app,OperationsOwnerV1.ReturnQuality,"query",false,(_,qa,s,t) => View(qa,s,id,t,revision),ct);
    public Task<ReturnQualityCommandResultV1> StartAsync(Guid app,Guid command,ReturnQualityStartV1 input,CancellationToken ct = default)
    {
        Reference(input.ExpectedSourceRef); Reference(input.ExpectedRmaHeadRef); Reference(input.ExpectedPolicyRef); Reason(input.Reason);
        if (input.LineId == Guid.Empty) throw Error("VALIDATION_FAILED",400);
        var frozen = OperationsOwnerV1.Freeze(input);
        return Command(app,command,OperationsOwnerV1.ReturnQuality,"start",frozen,async (a,qa,s,t) =>
        {
            await a.ActionAsync(OperationsOwnerV1.Rma,"inspection-start",t);
            var source = Source(s,frozen.LineId); await Scope(qa,source,source.RegistrarId,t);
            if (source.Reference != frozen.ExpectedSourceRef || source.PhysicalHeadRef != frozen.ExpectedRmaHeadRef) throw Error("CAS_CONFLICT",412);
            _ = Policy(source,frozen.ExpectedPolicyRef);
            var prior = await store.Heads().SingleOrDefaultAsync(x => x.Application == app && x.Line == source.LineId,t);
            if (prior != null) return await View(qa,s,prior.Id,t);
            var id = Guid.NewGuid(); var at = DateTimeOffset.UtcNow;
            var scope = RmaInboundAdapterV1.Ref("QA-NCR-01.ReturnScope",id.ToString(),new { source.Reference,source.Receipt.ReceivedRanges,source.Receipt.Quantity });
            var request = new ReturnInspectionRequestV1("RMA-QUALITY:"+source.Reference.Id,scope,Contract,source.RmaRef,[source.Reference],source.Receipt.ReceivedRanges,
                source.Receipt.Quantity,source.QualityPolicyRef,source.Receipt.RecordedAt,[source.Reference,source.Receipt.MovementRef],null);
            var reference = RmaInboundAdapterV1.Ref("QA-NCR-01.ReturnInspection",id.ToString(),new { id,request,Actor = a.ActorId,at });
            var value = new ReturnQualityInspectionV1(id,reference,a.ActorId,source,request,at);
            await store.Start(value,t);
            var voids =await new ReturnCorrectionStoreV1(db).Reversed("PHYSICAL_RECEIPT",source.LineId,t);
            if (voids.Length >0)
            {
                // Mechanical source projection: no professional acceptance is inferred.
                var remaining =Ranges.Except(Ranges.Read(source.Receipt.ReceivedRanges),Ranges.Read(voids));
                var parts =States.Select(state => new ReturnQualityPartitionV1(state,state =="PENDING" ? Ranges.Wire(remaining) : [],
                    new(state =="PENDING" ? Ranges.Text(Ranges.Measure(remaining)) : "0",source.Receipt.Quantity.Uom),[],[])).ToArray();
                await SaveDecision(await View(qa,s,id,t),a.ActorId,frozen.ExpectedPolicyRef,parts,"ACTIVE",frozen.Reason,t,voids);
            }
            return await View(qa,s,id,t);
        },ct);
    }
    private Task<ReturnQualityCommandResultV1> Command<T>(Guid app,Guid command,string resource,string action,T input,
        Func<OperationsAccessV1,OperationsAccessV1,RmaQualitySourceV1[],CancellationToken,Task<ReturnQualityViewV1>> apply,CancellationToken ct)
    {
        if (command == Guid.Empty) throw Error("VALIDATION_FAILED",400);
        var bytes = Encode(new { app,resource,action,input }); var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Run(app,resource,action,true,async (a,qa,s,t) =>
        {
            if (original != null) return await Disclose(a,qa,s,original,app,bytes,t);
            var result = new ReturnQualityCommandResultV1(action,await apply(a,qa,s,t));
            await journal.Store(Journal,"QA-NCR-01.RMA-QUALITY/1",command,a.ActorId,bytes,result,[app],new { action,result.Value.HeadRef },t); return result;
        },ct,async t => { original = await journal.Read(Journal,command,t); return original == null ? action : "query"; });
    }
    private async Task<ReturnQualityCommandResultV1> Disclose(OperationsAccessV1 a,OperationsAccessV1 qa,RmaQualitySourceV1[] sources,
        MainOwnerOperationJournalV1.Original original,Guid app,byte[]? input,CancellationToken ct)
    {
        if (original.Actor != a.ActorId || !original.Objects.Contains(app)) throw Error("OBJECT_FORBIDDEN",403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original,input)) throw Error("COMMAND_BINDING_CONFLICT",409);
        var saved = JsonSerializer.Deserialize<ReturnQualityCommandResultV1>(original.Result)!;
        _ = await View(qa,sources,saved.Value.Inspection.Id,ct); return saved;
    }
    public Task<ReturnQualityQueryResultV1> QueryAsync(ReturnQualityQueryV1 input,CancellationToken ct = default)
    {
        if (input.CommandId == Guid.Empty) throw Error("VALIDATION_FAILED",400);
        return Run(input.ApplicationId,OperationsOwnerV1.ReturnQuality,"query",false,async (a,qa,s,t) =>
        {
            var original = await new MainOwnerOperationJournalV1(db).Read(Journal,input.CommandId,t);
            return original == null ? new ReturnQualityQueryResultV1("NOT_OBSERVED",null)
                : new ReturnQualityQueryResultV1("FOUND",await Disclose(a,qa,s,original,input.ApplicationId,null,t));
        },ct);
    }
}
