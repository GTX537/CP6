using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Erp.PlateMoldSourceStoreV1;
namespace CP6.Core.Services.Erp;

public sealed record PlateMoldWriterScopeV1(Guid TenantId,Guid ActorId,PlateMoldTechnicalControlV1 NewTechnicalControl);
public sealed record PlateMoldWriteResultV1(Guid OperationId,string Kind,Guid ToolId,string Number,int Revision,
    PlateMoldSourceRefV1 Source,string NativeRowVersion,PlateMoldTechnicalControlV1 TechnicalControl,
    ToolNativeOrderRefV1 Order,string CommercialState,ToolOrderIntentReceiptV1? Intent=null,
    string PeState="NOT_EXECUTED",string OrderSourceKind="NATIVE_ORDER_LINK_ONLY");
public sealed record PlateMoldWriteOperationV1(Guid OperationId,string State,PlateMoldWriteResultV1? Result=null);

/// <summary>Legacy native maintenance and explicit unknown technical-control knowledge.
/// Exact observed bytes are immutable; this Owner does not approve/freeze a professional tool specification.</summary>
public sealed class PlateMoldWriteOwnerV1(CP6Context db,OperationsOwnerV1 owner,IOrderService orders,
    OrderReadOwnerV1 reader,ToolOrderLinkOwnerV1 links)
{
    private const string Writer="Main.Erp.PlateMoldWrite.v1",Contract="Main.Erp.PlateMoldWrite.commands.v1";
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context);
    private OperationsOwnerV1 Authority=>owner.Owns(db) && reader.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static PlateMoldTechnicalControlV1 NewControl=>new("NEW_UNCONTROLLED_DRAFT",PlateMoldSourceStoreV1.Owner,Guid.Empty,"absent","");
    private static PlateMoldTechnicalControlV1 Observed(PlateMoldSourceRefV1 source)=>new("TECHNICAL_CONTROL_UNPROVEN",source.Owner,source.Id,source.Version,source.Digest);
    public Task<PlateMoldWriterScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.PlateMold,"query",false,(a,_)=>Task.FromResult(new PlateMoldWriterScopeV1(db.CurrentTenantId,a.ActorId,NewControl)),ct,readOnlyAuthorityFence:true);
    internal Task<T> ReadNative<T>(Func<IQueryable<PlateMold>,CancellationToken,Task<T>> read,string action="query",CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.PlateMold,action,false,(a,token)=>read(a.PlateMolds(true),token),ct);
    private async Task<(OperationsAccessV1 Query,PlateMoldSourceStoreV1 Store)> Source(OperationsAccessV1 a,CancellationToken ct)
    {var query=await a.SourceAsync(OperationsOwnerV1.PlateMold,ct);var store=new PlateMoldSourceStoreV1(db);await store.Fence(ct);return(query,store);}
    private async Task<ToolNativeOrderRefV1> OriginalOrder(OperationsAccessV1 a,PlateMold tool,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(tool.ArrangeNo))throw Error("NATIVE_ORDER_LINK_UNPROVEN",409);
        var candidates=await db.OrderDetails.AsNoTracking().Where(d=>d.HaibaiNo1==tool.ArrangeNo).Select(d=>d.Id).Take(2).ToArrayAsync(ct);
        if(candidates.Length!=1 || await db.PlateMolds.AnyAsync(p=>p.Id!=tool.Id && p.ArrangeNo==tool.ArrangeNo,ct))throw Error("NATIVE_ORDER_LINK_UNPROVEN",409);
        return await reader.EnlistToolRead(a,candidates[0],ct);
    }
    public Task<PlateMoldDto?> EditorAsync(string number,int? revision,CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.PlateMold,"query",false,async(a,token)=>
        {
            var rows=a.PlateMolds(true).Where(p=>p.WdPtnNo==number);
            rows=revision==null?rows.Where(p=>!p.IsDeleted):rows.Where(p=>p.WdRev==revision.Value);
            var native=await rows.OrderByDescending(p=>p.WdRev).FirstOrDefaultAsync(token);if(native==null)return null;
            var(query,store)=await Source(a,token);var source=await store.Read(query,native.Id,null,token);
            var dto=PlateMoldService.NativeDto(native);dto.ExpectedNativeRowVersion=source.CurrentNativeRowVersion;dto.ExpectedTechnicalControl=Observed(source.Reference);
            if(!string.IsNullOrWhiteSpace(native.ArrangeNo))
            {var original=await OriginalOrder(a,native,token);dto.ExpectedOrder=new(original.OrderId,original.LineId,original.OrderRowVersion,original.LineRowVersion);}
            return dto;
        },ct);
    public Task<PlateMoldWriteResultV1> ExecuteAsync(string kind,string? number,int revision,PlateMoldDto input,CancellationToken ct=default)
    {
        var action=kind switch {"CREATE"=>"add","REVISE" or "UPDATE"=>"edit","DELETE"=>"del",_=>throw Error("TOOL_COMMAND_INVALID",400)};
        if(input==null || input.OperationId==null || input.OperationId==Guid.Empty || input.ExpectedTechnicalControl==null
            || string.IsNullOrWhiteSpace(input.ExpectedNativeRowVersion))throw Error("VERSION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(input);var operation=frozen.OperationId.Value;
        if(string.IsNullOrWhiteSpace(frozen.OperationReason) || frozen.OperationReason.Length>500 || frozen.OperationReason.Any(char.IsControl)
            || frozen.OrderDepartment?.Length>10 || frozen.OrderDepartment?.Any(char.IsControl)==true)throw Error("TOOL_COMMAND_REASON_REQUIRED",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{kind,number,revision,input=frozen});
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.PlateMold,action,true,async(a,token)=>
        {
            var(query,store)=await Source(a,token);
            if(original!=null)return await Disclose(a,query,store,operation,original,bytes,token);
            if(orders is not OrderService orderService)throw Error("ORIGINAL_ORDER_OWNER_UNAVAILABLE",503);
            PlateMold native;ToolNativeOrderRefV1? expectedOrder=null;
            if(kind=="CREATE")
            {
                if(frozen.ExpectedNativeRowVersion!="absent" || frozen.ExpectedTechnicalControl!=NewControl || frozen.ExpectedOrder!=null)
                    throw Error("TOOL_CREATE_PRECONDITION_INVALID",400);
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_DocSequence\" IN SHARE ROW EXCLUSIVE MODE",token);
                var allocated=await DocNumber.NextAsync(db,"MLD");native=PlateMoldService.NativeCreate(frozen,allocated.No,1);
                native.Creator=a.Actor;native.CreateDate=DateTime.Now;native.Status=0;native.McTransferFlg=false;
                db.PlateMolds.Add(native);
            }
            else
            {
                if(string.IsNullOrWhiteSpace(number) || revision<1)throw Error("TOOL_IDENTITY_REQUIRED",400);
                native=await a.PlateMolds().SingleOrDefaultAsync(p=>p.WdPtnNo==number && p.WdRev==revision,token)??throw Error("OBJECT_FORBIDDEN",403);
                var current=await store.Read(query,native.Id,null,token);
                if(frozen.ExpectedTechnicalControl!=Observed(current.Reference))throw Error("TECHNICAL_CONTROL_CHANGED",412);
                if(current.CurrentNativeRowVersion!=frozen.ExpectedNativeRowVersion
                    || frozen.RowVersion is {Length:>0} && Convert.ToBase64String(frozen.RowVersion)!=frozen.ExpectedNativeRowVersion)
                    throw Error("TOOL_NATIVE_VERSION_CHANGED",412);
                if(kind is "UPDATE" or "REVISE")
                {
                    var highest=await db.PlateMolds.Where(p=>p.WdPtnNo==number).MaxAsync(p=>p.WdRev,token);
                    if(highest!=revision)throw Error("TOOL_PREDECESSOR_CHANGED",412);
                }
                if(kind is "UPDATE" or "DELETE")
                {
                    expectedOrder=await OriginalOrder(a,native,token);
                    if(frozen.ExpectedOrder!=new PlateMoldOrderPreconditionV1(expectedOrder.OrderId,expectedOrder.LineId,expectedOrder.OrderRowVersion,expectedOrder.LineRowVersion)
                        || expectedOrder.Deleted)throw Error("ORIGINAL_ORDER_CHANGED",412);
                }
                db.Attach(native);
                if(kind=="REVISE")
                {
                    if(frozen.StDate<=native.StDate || frozen.StDate.AddDays(-1)<native.StDate)throw Error("TOOL_EFFECTIVITY_INVALID",400);
                    if(await db.PlateMolds.AnyAsync(p=>p.WdPtnNo==number && !p.IsDeleted && p.StDate>DateTime.Today,token))throw Error("TOOL_FUTURE_REVISION_EXISTS",409);
                    var next=PlateMoldService.NativeCreate(frozen,native.WdPtnNo,checked(revision+1));
                    // Do not re-key history. Original observations of the former interval remain immutable.
                    native.EndDate=frozen.StDate.AddDays(-1);native.Modifier=a.Actor;native.ModifyDate=DateTime.Now;
                    next.Creator=a.Actor;next.CreateDate=DateTime.Now;next.Status=0;next.McTransferFlg=false;db.PlateMolds.Add(next);native=next;
                }
                else if(kind=="UPDATE")PlateMoldService.NativeApply(frozen,native);
                else native.IsDeleted=true;
            }
            native.Modifier=a.Actor;native.ModifyDate=DateTime.Now;await db.SaveChangesAsync(token);
            var id=native.Id;
            a.AtCommit(async last=>{if(!await a.PlateMolds(true).AnyAsync(p=>p.Id==id,last))throw Error("OBJECT_FORBIDDEN",403);});
            ToolNativeOrderRefV1 order;ToolOrderIntentReceiptV1? intent=null;string state;
            if(kind is "CREATE" or "REVISE")
            {var linked=await links.EnlistLegacyCreate(a,operation,id,frozen.OrderDepartment,token);order=linked.Order;state="OWNER_APPLIED";}
            else
            {
                var current=await store.Read(query,id,null,token);
                intent=await orderService.RequestToolOrderAsync(a,operation,kind=="UPDATE"?"CHANGE_REQUEST":"CANCEL_REQUEST",current.Reference,expectedOrder!,frozen,token);
                order=expectedOrder!;state=intent.State;
            }
            var saved=await store.Read(query,id,null,token);
            var result=new PlateMoldWriteResultV1(operation,kind,id,native.WdPtnNo,native.WdRev,saved.Reference,saved.CurrentNativeRowVersion,Observed(saved.Reference),order,state,intent);
            await journal.Store(Writer,Contract,operation,a.ActorId,bytes,result,[id],new{action="legacy-tool-command",actor=a.ActorId,result},token);
            return result;
        },ct,actionAtFence:async token=>{original=await journal.Read(Writer,operation,token);return original==null?action:"query";});
    }
    private async Task<PlateMoldWriteResultV1> Disclose(OperationsAccessV1 a,OperationsAccessV1 query,PlateMoldSourceStoreV1 store,
        Guid id,MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<PlateMoldWriteResultV1>(original.Result);
        if(result==null || result.OperationId!=id || original.Objects.Length!=1 || original.Objects[0]!=result.ToolId || result.PeState!="NOT_EXECUTED")
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var source=await store.Read(query,result.ToolId,PlateMoldSourceStoreV1.Revision(result.Source.Version),ct);
        if(source.Reference!=result.Source)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var order=await reader.EnlistToolRead(a,result.Order.LineId,ct);
        if(order.OrderId!=result.Order.OrderId || order.Number!=result.Order.Number || order.LineNumber!=result.Order.LineNumber)throw Error("ORIGINAL_ORDER_UNAVAILABLE",409);
        if(result.Intent!=null)
        {
            var receipt=await new MainOwnerOperationJournalV1(db).Read("Main.Order.ToolCommercialIntent.v1",id,ct);
            if(receipt==null || receipt.Actor!=a.ActorId || JsonSerializer.Deserialize<ToolOrderIntentReceiptV1>(receipt.Result)!=result.Intent)
                throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        }
        return result;
    }
    public Task<PlateMoldWriteOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.PlateMold,"query",close,async(a,token)=>
        {
            var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(Writer,id,token);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new PlateMoldWriteOperationV1(id,"CLOSED");
                var(query,store)=await Source(a,token);return new PlateMoldWriteOperationV1(id,"COMMITTED",await Disclose(a,query,store,id,original,null,token));
            }
            if(!close)return new PlateMoldWriteOperationV1(id,"UNKNOWN");var result=new PlateMoldWriteOperationV1(id,"CLOSED");
            await journal.Store(Writer,Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-legacy-tool",actor=a.ActorId},token);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
