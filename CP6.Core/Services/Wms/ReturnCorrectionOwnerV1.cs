using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using static CP6.Core.Services.Mes.ReturnQualityPolicySourceV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class ReturnCorrectionOwnerV1(CP6Context db,OperationsOwnerV1 owner,ReturnQualityOwnerV1 quality,
    RmaFormalOwnerV1 rma,ReturnCorrectionApprovalSourceV1 approvals,StockMovementService stock,StockQcService qc)
{
    internal const string Resource ="wms-return-correction";
    private const string Journal ="Main.Wms.ReturnCorrection.Command.v1";
    private readonly ReturnCorrectionStoreV1 store =new(db);
    private readonly ReturnCorrectionTargetsV1 targets =new(db);
    private readonly ReturnCorrectionStockV1 posting =new(db,stock,qc);
    private Task<T> Run<T>(Guid app,string action,bool write,Func<OperationsAccessV1,ReturnQualityChoiceV1[],CancellationToken,Task<T>> body,
        CancellationToken ct,Func<CancellationToken,Task<string>>? choose =null)
    {
        if (app ==Guid.Empty) throw Error("VALIDATION_FAILED",400);
        return owner.ExecuteAsync(Resource,action,write,async(a,t) =>await body(a,await quality.DispositionSources(a,app,write,t),t),ct,actionAtFence:choose);
    }
    private async Task Scope(OperationsAccessV1 a,ReturnCorrectionTargetV1 target,CancellationToken ct)
    {
        await ReturnCorrectionTargetsV1.Scope(a,target,ct);
        await ReturnCorrectionTargetsV1.Scope(await a.SourceAsync(target.Resource,ct),target,ct);
    }
    private async Task<ReturnCorrectionViewV1> View(OperationsAccessV1 a,ReturnQualityChoiceV1[] sources,Guid id,CancellationToken ct)
    {
        var row =await store.Rows().SingleOrDefaultAsync(x => x.Id ==id && sources.Select(s => s.Source.ApplicationId).Contains(x.App),ct) ?? throw Error("CORRECTION_NOT_FOUND",404);
        var candidate =ReturnCorrectionStoreV1.Decode<ReturnCorrectionCandidateV1>(row.Body);
        if (a.RmaApplicationSelfOnly && candidate.CreatorId !=a.ActorId) throw Error("OBJECT_FORBIDDEN",403);
        await Scope(a,candidate.Target,ct);
        var remaining =Ranges.Wire(Ranges.Except(Ranges.Read(candidate.Target.Ranges),Ranges.Read(await store.Reversed(row.Axis,row.Target,ct))));
        var allowed =row.State switch { "DRAFT" =>new[] { "draft","submit","withdraw" },"SUBMITTED" =>["bind-approval","withdraw"],"APPROVED" =>row.Preparation ==null ? ["prepare","withdraw"] : ["apply","withdraw"],_ =>[] };
        return new(candidate,ReturnCorrectionStoreV1.Head(row),row.State,
            row.Approval ==null ? null : ReturnCorrectionStoreV1.Decode<ReturnCorrectionApprovalV1>(row.Approval),
            row.Preparation ==null ? null : ReturnCorrectionStoreV1.Decode<ReturnCorrectionPreparationV1>(row.Preparation),
            row.Result ==null ? null : ReturnCorrectionStoreV1.Decode<ReturnCorrectionReceiptV1>(row.Result),
            row.Proof ==null ? null : ReturnCorrectionStoreV1.Decode<FulfilmentRefV1>(row.Proof),remaining,allowed.Where(x => a.Authorities[0].Actor.ActionKeys.Contains(Resource+":"+x)).ToArray());
    }
    public Task<ReturnCorrectionContextV1> ContextAsync(Guid app,CancellationToken ct =default) =>Run(app,"query",false,async(a,s,t) =>
    {
        var choices =new List<ReturnCorrectionChoiceV1>();
        foreach (var target in await targets.Read(s,t))
        {
            await Scope(a,target,t);
            var available =Ranges.Except(Ranges.Read(target.Ranges),Ranges.Read(await store.Reversed(target.Axis,target.Id,t)).Concat(Ranges.Read(await store.Fenced(target.Source.LineId,null,t))));
            choices.Add(new(target,Ranges.Wire(available),approvals.Find(db.CurrentTenantId,target)));
        }
        var values =new List<ReturnCorrectionViewV1>();
        foreach (var row in await store.Rows().Where(x => x.App ==app).ToArrayAsync(t)) values.Add(await View(a,s,row.Id,t));
        var keys =a.Authorities[0].Actor.ActionKeys;
        return new ReturnCorrectionContextV1(choices.ToArray(),values.ToArray(),new[] { "query","draft","submit","bind-approval","prepare","apply","withdraw" }.Where(x => keys.Contains(Resource+":"+x)).ToArray());
    },ct);
    public Task<ReturnCorrectionViewV1> ReadAsync(Guid app,Guid id,CancellationToken ct =default) =>Run(app,"query",false,(a,s,t) =>View(a,s,id,t),ct);
    private Task<ReturnCorrectionCommandResultV1> Command<T>(Guid app,Guid command,string action,T input,
        Func<OperationsAccessV1,ReturnQualityChoiceV1[],CancellationToken,Task<ReturnCorrectionViewV1>> apply,CancellationToken ct)
    {
        if (command ==Guid.Empty) throw Error("VALIDATION_FAILED",400);
        var bytes =Encode(new { app,action,input }); var journal =new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original =null;
        return Run(app,action,true,async(a,s,t) =>
        {
            if (original !=null) return await Disclose(a,s,original,app,bytes,t);
            var value =await apply(a,s,t); var result =new ReturnCorrectionCommandResultV1(action,value);
            await journal.Store(Journal,"RMA-CORRECTION/1",command,a.ActorId,bytes,result,[app],new { value.HeadRef,action },t); return result;
        },ct,async t => { original =await journal.Read(Journal,command,t); return original ==null ? action : "query"; });
    }
    private async Task<ReturnCorrectionCommandResultV1> Disclose(OperationsAccessV1 a,ReturnQualityChoiceV1[] s,MainOwnerOperationJournalV1.Original original,Guid app,byte[]? bytes,CancellationToken ct)
    {
        if (original.Actor !=a.ActorId || !original.Objects.Contains(app)) throw Error("OBJECT_FORBIDDEN",403);
        if (bytes !=null && !MainOwnerOperationJournalV1.Matches(original,bytes)) throw Error("COMMAND_BINDING_CONFLICT",409);
        var saved =JsonSerializer.Deserialize<ReturnCorrectionCommandResultV1>(original.Result)!; _ =await View(a,s,saved.Value.Candidate.Id,ct); return saved;
    }
    public Task<ReturnCorrectionQueryResultV1> QueryAsync(ReturnCorrectionQueryV1 input,CancellationToken ct =default)
    {
        if (input.CommandId ==Guid.Empty) throw Error("VALIDATION_FAILED",400);
        return Run(input.ApplicationId,"query",false,async(a,s,t) =>
        {
            var previous =await new MainOwnerOperationJournalV1(db).Read(Journal,input.CommandId,t);
            return previous ==null ? new ReturnCorrectionQueryResultV1("NOT_OBSERVED",null) : new("FOUND",await Disclose(a,s,previous,input.ApplicationId,null,t));
        },ct);
    }
    private static void Match(ReturnCorrectionViewV1 view,FulfilmentRefV1 head,string state)
    { if (view.HeadRef !=head) throw Error("CAS_CONFLICT",412); if (view.State !=state) throw Error("CORRECTION_STATE_INVALID",409); }
    private async Task Available(ReturnCorrectionCandidateV1 c,CancellationToken ct)
    {
        var selected =Ranges.Read(c.Ranges);
        if (Ranges.Intersects(selected,Ranges.Read(await store.Reversed(c.Target.Axis,c.Target.Id,ct)))) throw Error("REVERSE_BUDGET_EXCEEDED",409);
        if (Ranges.Intersects(selected,Ranges.Read(await store.Fenced(c.Target.Source.LineId,c.Id,ct)))) throw Error("CORRECTION_SCOPE_OCCUPIED",409);
    }
}
