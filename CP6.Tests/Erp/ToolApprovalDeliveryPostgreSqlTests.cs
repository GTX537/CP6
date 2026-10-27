using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class DeliveryTransport(ToolApprovalDeliveryRouteV1 route,Func<Guid,byte[],bool,CancellationToken,Task<ToolApprovalDeliveryResponseV1>> send):IToolApprovalDeliveryTransportV1
    {
        internal ToolApprovalDeliveryRouteV1 Current=route;
        internal int Posts,Queries;
        public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>tenant==Current.Tenant?Current:null;
        public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 r,Guid command,byte[] original,bool query,CancellationToken ct)
        {if(query)Queries++;else Posts++;return send(command,original,query,ct);}
    }
    private static ToolApprovalDeliveryRouteV1 DeliveryRoute(Harness h,IntakeFixture f)=>new(ScopeEnvironment,h.Tenant,f.Authority.Identity.Principal,"fixture-service","https://issuer.example","https://approval.example","fixture-1","DEMO");
    private static async Task<ToolApprovalDeliveryResponseV1> Deliver(IntakeFixture f,Guid command,byte[] bytes,bool query,CancellationToken ct)
    {
        try
        {
            if(query)return new(200,ApprovalCanonicalV2.Bytes(await f.Owner.OriginalAsync(command,ct)));
            var result=await f.Owner.SubmitAsync(bytes,ct);return new(result.Status,ApprovalCanonicalV2.Bytes(result.Ack));
        }
        catch(ScopeAuthorizationExceptionV1 e){return new(e.Status,[]);}
    }
    private static Task Due(Harness h)=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_v1.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");

    [CurrentPermissionPostgresFact]
    public async Task Tool_delivery_lost_receiver_response_recovers_same_original_after_submit_revoked_without_resend_or_business_effect()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var e=IntakeBody(f);var lose=true;
        var transport=new DeliveryTransport(DeliveryRoute(h,f),async(command,bytes,query,ct)=>{
            Assert.Null(h.Db.Database.CurrentTransaction);Assert.Equal(f.Payload,bytes);
            var response=await Deliver(f,command,bytes,query,ct);if(!query && lose){lose=false;throw new HttpRequestException("lost actual accepted response");}return response;
        });
        var worker=new ToolApprovalDeliveryV1(h.Db,ScopeFence(h),transport);
        var original=(await ToolApproval(h).OperationAsync(e.CommandId,false)).Result!;
        Assert.Equal("OUTCOME_UNKNOWN",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Posts);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="dispose-submit"||x.ActionCode=="dispose-policy").ExecuteDeleteAsync();
        await Due(h);
        Assert.Equal("RECEIVED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Posts);Assert.Equal(2,transport.Queries);
        Assert.Equal(original,(await ToolApproval(h).OperationAsync(e.CommandId,false)).Result);
        var current=await ToolApproval(h).ContextAsync(original.ObjectId);Assert.Equal("RECEIVED",current.Delivery!.State);Assert.Equal(2,current.Delivery.Attempts);
        Assert.Equal("DISPATCH_PENDING",current.Submission!.State);Assert.False(current.Submission.IsApproved);Assert.False(current.Disposition.IsDiscarded);
        Assert.Equal("IDLE_OR_DISABLED",(await worker.RunAsync(h.Tenant)).State);
        Assert.Single(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tool_approval_v1.delivery_results WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_delivery_competing_claims_expired_restart_and_stale_epoch_cannot_overwrite_current_receipt()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);
        var transport=new DeliveryTransport(DeliveryRoute(h,f),(command,bytes,query,ct)=>Deliver(f,command,bytes,query,ct));
        await using var db=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peer=h with{Db=db};
        var a=new ToolApprovalDeliveryV1(h.Db,ScopeFence(h),transport);var b=new ToolApprovalDeliveryV1(db,ScopeFence(peer),transport);
        var claims=await Task.WhenAll(a.ClaimAsync(h.Tenant),b.ClaimAsync(h.Tenant));Assert.Single(claims,x=>x!=null);var old=claims.Single(x=>x!=null)!;
        await Due(h);var fresh=(await b.ClaimAsync(h.Tenant))!;Assert.Equal(2,fresh.Epoch);Assert.NotEqual(old.Token,fresh.Token);
        var ack=(await f.Owner.SubmitAsync(f.Payload)).Ack;
        var json=JsonSerializer.Serialize(ack,ApprovalBindingOwnerV2.Json);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_v1.delivery SET state='RECEIVED',ack_json={json} WHERE tenant_id={h.Tenant}"));
        Assert.Equal("LEASE_LOST",await a.FinishAsync(old,"RECEIVED",null,ack));
        Assert.Equal("RECEIVED",await b.FinishAsync(fresh,"RECEIVED",null,ack));
        Assert.Equal("LEASE_LOST",await a.FinishAsync(old,"OUTCOME_UNKNOWN","late timeout",null));
        Assert.Equal(1,await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_v1.delivery_results WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(2,await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_v1.delivery_attempts WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_delivery_forbidden_pauses_until_explicit_registration_revision_then_reuses_original()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var allowed=false;
        var transport=new DeliveryTransport(DeliveryRoute(h,f),(command,bytes,query,ct)=>allowed?Deliver(f,command,bytes,query,ct):Task.FromResult(new ToolApprovalDeliveryResponseV1(403,[])));
        var worker=new ToolApprovalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("BLOCKED",(await worker.RunAsync(h.Tenant)).State);await Due(h);
        allowed=true;Assert.Equal("IDLE_OR_DISABLED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Queries);
        transport.Current=transport.Current with{Version="operator-restored-2"};
        Assert.Equal("RECEIVED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Posts);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_delivery_malformed_success_does_not_confirm_and_local_receipt_commit_unknown_does_not_resend()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await IntakeSetup(h);
        var transport=new DeliveryTransport(DeliveryRoute(h,f),async(command,bytes,query,ct)=>{
            var result=await Deliver(f,command,bytes,query,ct);if(!query)fault.Armed=true;return result;
        });
        var worker=new ToolApprovalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await worker.RunAsync(h.Tenant)).State);
        Assert.Equal("IDLE_OR_DISABLED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Posts);
        Assert.Equal("RECEIVED",(await ToolApprovalDeliveryV1.ReadAsync(h.Db,ScopeEnvironment,IntakeBody(f).SubmissionId,default))!.State);
        var ack=await f.Owner.OriginalAsync(IntakeBody(f).CommandId);var bytes=ApprovalCanonicalV2.Bytes(ack);
        var foreign=ack with{Subject=ack.Subject with{SubjectId=Guid.NewGuid().ToString()}};
        Assert.Throws<ScopeAuthorizationExceptionV1>(()=>ToolApprovalDeliveryV1.Receipt(ApprovalCanonicalV2.Bytes(foreign),IntakeBody(f)));
        foreach(var bad in new[]{ack with{InstanceId=Guid.NewGuid()},ack with{State="APPROVED"},ack with{BindingRevisionId=null},ack with{SubmissionId=Guid.NewGuid()},ack with{RowVersion="AA=="}})
            Assert.Throws<ScopeAuthorizationExceptionV1>(()=>ToolApprovalDeliveryV1.Receipt(ApprovalCanonicalV2.Bytes(bad),IntakeBody(f)));
        var unknown=System.Text.Json.Nodes.JsonNode.Parse(bytes)!.AsObject();unknown["approved"]=true;
        Assert.Throws<ScopeAuthorizationExceptionV1>(()=>ToolApprovalDeliveryV1.Receipt(JsonSerializer.SerializeToUtf8Bytes(unknown),IntakeBody(f)));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_delivery_invalid_receipt_persists_conflict_and_never_blindly_retries()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);
        var transport=new DeliveryTransport(DeliveryRoute(h,f),(_,_,_,_)=>Task.FromResult(new ToolApprovalDeliveryResponseV1(200,"{}"u8.ToArray())));
        var worker=new ToolApprovalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("CONFLICT",(await worker.RunAsync(h.Tenant)).State);Assert.Equal("IDLE_OR_DISABLED",(await worker.RunAsync(h.Tenant)).State);
        Assert.Equal(0,transport.Posts);Assert.Equal(1,transport.Queries);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_delivery_current_service_revoke_after_receiving_prevents_local_confirmation_and_recovers_later()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var revoke=true;
        var transport=new DeliveryTransport(DeliveryRoute(h,f),async(command,bytes,query,ct)=>{
            var result=await Deliver(f,command,bytes,query,ct);
            if(!query&&revoke){revoke=false;await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_binding_v2.registrations SET enabled=false WHERE tenant_id={h.Tenant} AND owner='Main.Wms.ToolDisposition.v1'",ct);}return result;
        });
        var worker=new ToolApprovalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await worker.RunAsync(h.Tenant)).State);
        Assert.Empty(await h.Db.Database.SqlQuery<long>($"SELECT epoch AS \"Value\" FROM cp6_tool_approval_v1.delivery_results WHERE tenant_id={h.Tenant}").ToArrayAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_binding_v2.registrations SET enabled=true WHERE tenant_id={h.Tenant} AND owner='Main.Wms.ToolDisposition.v1'");await Due(h);
        Assert.Equal("RECEIVED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Posts);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_delivery_pending_original_cannot_move_to_another_destination_or_service_identity()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var available=false;
        var transport=new DeliveryTransport(DeliveryRoute(h,f),(command,bytes,query,ct)=>available?Deliver(f,command,bytes,query,ct):Task.FromResult(new ToolApprovalDeliveryResponseV1(503,[])));
        var worker=new ToolApprovalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await worker.RunAsync(h.Tenant)).State);await Due(h);available=true;
        var original=transport.Current;transport.Current=original with{Destination="https://other.example",Version="changed"};
        Assert.Equal("OUTCOME_UNKNOWN",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Queries);Assert.Equal(0,transport.Posts);
        transport.Current=original with{Version="credential-restored"};Assert.Equal("RECEIVED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Posts);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_delivery_alias_without_actual_local_canonical_owner_is_conflict_not_a_second_business_intent()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var e=IntakeBody(f);var accepted=(await f.Owner.SubmitAsync(f.Payload)).Ack;
        var canonical=Guid.NewGuid();var alias=accepted with{SubmissionId=canonical,Alias=new(e.SubmissionId,canonical,Guid.NewGuid())};
        var transport=new DeliveryTransport(DeliveryRoute(h,f),(_,_,_,_)=>Task.FromResult(new ToolApprovalDeliveryResponseV1(200,ApprovalCanonicalV2.Bytes(alias))));
        var worker=new ToolApprovalDeliveryV1(h.Db,ScopeFence(h),transport);Assert.Equal("CONFLICT",(await worker.RunAsync(h.Tenant)).State);
        var view=await ToolApprovalDeliveryV1.ReadAsync(h.Db,ScopeEnvironment,e.SubmissionId,default);
        Assert.Equal("DELIVERY_CANONICAL_OWNER_MISSING",view!.ErrorCode);Assert.Null(view.Receipt);
        Assert.Single(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_tool_approval_v1.submissions WHERE tenant_id={h.Tenant}").ToArrayAsync());
    }
}
