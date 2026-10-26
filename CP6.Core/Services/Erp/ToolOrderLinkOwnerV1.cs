using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using static CP6.Core.Services.Erp.PlateMoldSourceStoreV1;
namespace CP6.Core.Services.Erp;

/// <summary>Real native Order creation/link in the original Main transaction. Not a formal Sales publication.</summary>
public sealed class ToolOrderLinkOwnerV1(CP6Context db, OperationsOwnerV1 owner, IOrderService orders, OrderReadOwnerV1 reader)
{
    private const string LinkOwner = "Main.Erp.ToolOrderLink.v1", Contract = "Main.Erp.ToolOrderLink.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) && reader.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<PlateMoldSourceScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.PlateMold,"query",false,(a,_) => Task.FromResult(new PlateMoldSourceScopeV1(db.CurrentTenantId,a.ActorId)),ct,readOnlyAuthorityFence:true);
    private sealed class Stored { public byte[] Body { get; set; } = []; public string Digest { get; set; } = ""; }
    private async Task<ToolOrderLinkReceiptV1?> StoredLink(Guid id,CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<Stored>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_tool_order_link_v1.links
            WHERE tenant_id={db.CurrentTenantId} AND tool_id={id}
            """).SingleOrDefaultAsync(ct);
        if(row==null)return null;
        var value = JsonSerializer.Deserialize<ToolOrderLinkReceiptV1>(row.Body);
        if(MainOwnerOperationJournalV1.Digest(row.Body)!=row.Digest || value==null || value.TenantId!=db.CurrentTenantId
            || value.Tool.Id!=id || value.SourceKind!="NATIVE_ORDER_LINK_ONLY" || value.PeState!="NOT_EXECUTED")
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return value;
    }
    private async Task<(OperationsAccessV1 Query,PlateMoldSourceStoreV1 Store)> Source(OperationsAccessV1 root,CancellationToken ct)
    {
        root.RequireToolOrderTransaction(db,false);
        var query=await root.SourceAsync(OperationsOwnerV1.PlateMold,ct);
        var store=new PlateMoldSourceStoreV1(db); await store.Fence(ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_tool_order_link_v1.links IN SHARE MODE",ct);
        return (query,store);
    }
    private async Task VerifyOriginal(OperationsAccessV1 root,OperationsAccessV1 query,PlateMoldSourceStoreV1 store,
        ToolOrderLinkReceiptV1 original,CancellationToken ct)
    {
        var source=await store.Read(query,original.Tool.Id,Revision(original.Tool.Version),ct);
        if(source.Reference!=original.Tool)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var current=await reader.EnlistToolRead(root,original.Order.LineId,ct);
        if(current.OrderId!=original.Order.OrderId || current.Number!=original.Order.Number || current.LineNumber!=original.Order.LineNumber)
            throw Error("ORIGINAL_ORDER_UNAVAILABLE",409);
    }
    public Task<ToolOrderLinkViewV1> ReadAsync(Guid toolId,CancellationToken ct=default) => Authority.ExecuteAsync(
        OperationsOwnerV1.PlateMold,"query",false,async (a,token)=>
        {
            var (query,store)=await Source(a,token); var current=await store.Read(query,toolId,null,token);
            var link=await StoredLink(toolId,token); if(link==null)return new ToolOrderLinkViewV1(null,false,"UNRECORDED");
            await VerifyOriginal(a,query,store,link,token);
            var order=await reader.EnlistToolRead(a,link.Order.LineId,token);
            var native=await db.PlateMolds.AsNoTracking().SingleAsync(p=>p.Id==toolId,token);
            var applicable=!native.IsDeleted && !order.Deleted && current.Reference==link.Tool
                && current.CurrentNativeRowVersion==link.NativeToolRowVersion && order==link.Order && native.ArrangeNo==link.Order.ArrangeNumber;
            return new ToolOrderLinkViewV1(link,applicable,applicable?"CURRENT":"ORIGINAL_CHANGED");
        },ct);
    public Task<ToolOrderLinkReceiptV1> CreateOrLinkAsync(ToolOrderLinkCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.Tool==null || string.IsNullOrWhiteSpace(input.ExpectedNativeRowVersion)
            || input.OrderDepartment?.Length>10 || input.OrderDepartment?.Any(char.IsControl)==true)
            throw Error("TOOL_ORDER_COMMAND_INVALID",400);
        var frozen=OperationsOwnerV1.Freeze(input);var bytes=MainOwnerOperationJournalV1.Encode(frozen);
        var journal=new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? previous=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.PlateMold,"edit",true,async(a,token)=>
        {
            var (query,store)=await Source(a,token);
            if(previous!=null)return await Disclose(a,query,store,frozen.OperationId,previous,bytes,token);
            if(!await a.PlateMolds().AnyAsync(p=>p.Id==frozen.Tool.Id,token))throw Error("OBJECT_FORBIDDEN",403);
            a.AtCommit(async last=>{if(!await a.PlateMolds().AnyAsync(p=>p.Id==frozen.Tool.Id,last))throw Error("OBJECT_FORBIDDEN",403);});
            var source=await store.CurrentExact(query,frozen.Tool,token);
            if(source.CurrentNativeRowVersion!=frozen.ExpectedNativeRowVersion)throw Error("TOOL_NATIVE_VERSION_CHANGED",412);
            var native=await db.PlateMolds.AsNoTracking().SingleAsync(p=>p.Id==frozen.Tool.Id,token);
            var existing=await StoredLink(native.Id,token); ToolOrderLinkReceiptV1 result;
            if(existing!=null)
            {
                await VerifyOriginal(a,query,store,existing,token);
                var current=await reader.EnlistToolRead(a,existing.Order.LineId,token);
                if(current.Deleted || current.ArrangeNumber!=existing.Order.ArrangeNumber || native.ArrangeNo!=existing.Order.ArrangeNumber)
                    throw Error("TOOL_ORDER_LINK_CHANGED",409);
                result=existing with { OperationId=frozen.OperationId,Effect="EXISTING_LINK" };
            }
            else
            {
                Guid lineId; var created=string.IsNullOrWhiteSpace(native.ArrangeNo);
                if(created)
                {
                    if(orders is not OrderService service)throw Error("ORIGINAL_ORDER_OWNER_UNAVAILABLE",503);
                    var number=await service.CreateToolLinkedAsync(a,Draft(native,frozen.OrderDepartment),token);
                    lineId=await db.OrderDetails.Where(d=>d.WebOrderNo==number && d.WebOrderDetailNo==1).Select(d=>d.Id).SingleAsync(token);
                }
                else
                {
                    var candidates=await db.OrderDetails.AsNoTracking().Where(d=>d.HaibaiNo1==native.ArrangeNo && !d.IsDeleted).Select(d=>d.Id).Take(2).ToArrayAsync(token);
                    if(candidates.Length!=1)throw Error("NATIVE_ORDER_LINK_UNPROVEN",409); lineId=candidates[0];
                }
                var order=await reader.EnlistToolRead(a,lineId,token);
                if(order.Deleted || string.IsNullOrWhiteSpace(order.ArrangeNumber)
                    || await db.PlateMolds.AnyAsync(p=>p.Id!=native.Id && p.ArrangeNo==order.ArrangeNumber,token))throw Error("NATIVE_ORDER_LINK_UNPROVEN",409);
                if(created)
                {
                    // The legacy column stores local wall-clock time (timestamp without time zone).
                    // Raw EF DateTime parameters otherwise default to timestamptz regardless of Kind.
                    var modified=new NpgsqlParameter("tool_modified",NpgsqlDbType.Timestamp)
                        {Value=DateTime.SpecifyKind(DateTime.Now,DateTimeKind.Unspecified)};
                    var changed=await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE public."T_PlateMold" SET "ArrangeNo"={order.ArrangeNumber},"Modifier"={a.Actor},"ModifyDate"={modified}
                        WHERE "TenantId"={db.CurrentTenantId} AND "Id"={native.Id} AND "RowVersion"={native.RowVersion}
                        """,token);
                    if(changed!=1)throw Error("TOOL_NATIVE_VERSION_CHANGED",412);
                }
                var updated=await store.Read(query,native.Id,null,token);
                result=new(frozen.OperationId,db.CurrentTenantId,frozen.Tool,updated.CurrentNativeRowVersion,order,created?"ORDER_CREATED":"NATIVE_LINK_RECORDED");
                var body=MainOwnerOperationJournalV1.Encode(result);var digest=MainOwnerOperationJournalV1.Digest(body);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_tool_order_link_v1.links(tenant_id,tool_id,order_id,line_id,body_bytes,body_digest)
                    VALUES({db.CurrentTenantId},{native.Id},{order.OrderId},{order.LineId},{body},{digest})
                    """,token);
            }
            await journal.Store(LinkOwner,Contract,frozen.OperationId,a.ActorId,bytes,result,[native.Id],
                new { action="create-or-link-original-order",actor=a.ActorId,result },token);
            return result;
        },ct,actionAtFence:async token=>{previous=await journal.Read(LinkOwner,frozen.OperationId,token);return previous==null?"edit":"query";});
    }
    private static OrderDto Draft(PlateMold p,string? department)
    {
        var code=p.WdPtnNo.Length>15?p.WdPtnNo[..15]:p.WdPtnNo;
        return new() {CustomerCd=p.CustomerCd,OrderType="30",OrderDepartment=department,OrderDate=DateTime.Today,
            CustomerDeliveryDate=p.DeliveryDate,Quantity=1m,SalesPriceDiv="1",Details=[new() {
                ProductCd=code,ItemCd=code,ProductCatBig="61",CustomerItemName1=p.VrsnName,CpItemName1=p.VrsnName,
                HaibaiNo2=p.CustomerCd,HaibaiNo3=p.DecisionEstimateNo,QtyUnit="個",Quantity=1m,IndividualUnitPrice=p.DecisionAmount,
                Amount=p.DecisionAmount,DeliveryCd=p.CustomerCd,CustomerDeliveryDate=p.DeliveryDate,SalesAvailable=p.EarningsCd,SalesPriceDiv="1",OrderType="30" }]};
    }
    private async Task<ToolOrderLinkReceiptV1> Disclose(OperationsAccessV1 a,OperationsAccessV1 query,PlateMoldSourceStoreV1 store,
        Guid id,MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<ToolOrderLinkReceiptV1>(original.Result);
        if(result==null || result.OperationId!=id || result.TenantId!=db.CurrentTenantId || original.Objects.Length!=1 || original.Objects[0]!=result.Tool.Id)
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var stored=await StoredLink(result.Tool.Id,ct);
        if(stored==null || stored with {OperationId=result.OperationId,Effect=result.Effect}!=result)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        await VerifyOriginal(a,query,store,result,ct);return result;
    }
    public Task<ToolOrderLinkOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.PlateMold,"query",close,async(a,token)=>
        {
            var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(LinkOwner,id,token);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new ToolOrderLinkOperationV1(id,"CLOSED");
                var(query,store)=await Source(a,token);
                return new ToolOrderLinkOperationV1(id,"COMMITTED",await Disclose(a,query,store,id,original,null,token));
            }
            if(!close)return new ToolOrderLinkOperationV1(id,"UNKNOWN");
            var result=new ToolOrderLinkOperationV1(id,"CLOSED");
            await journal.Store(LinkOwner,Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-tool-order",actor=a.ActorId},token);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
