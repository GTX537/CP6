using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static ToolDispositionApprovalOwnerV1 ToolApproval(Harness h)=>new(h.Db,RegistrationAuthority(h),ScopeFence(h));
    private async Task<Guid> ToolApprovalSetup(Harness h)
    {
        var id=await MaintenanceSetup(h);await RegisterScope(h);
        foreach(var action in new[]{"dispose","dispose-policy","dispose-submit"})await EngineeringAction(h,"wms-plate-mold",action);
        await DispositionRequest(Disposition(h),id);await DispositionBlock(Disposition(h),id);
        return id;
    }
    private static ToolDispositionPolicyCommandV1 ToolPolicy(ToolDispositionApprovalContextV1 c)=>new(Guid.NewGuid(),"ENABLED",c.Disposition.Site,
        ["WEAR","DAMAGE","OBSOLETE"],2,true,DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()),
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()),"明确的本地测试政策，非真实业务批准");
    private static async Task<ToolDispositionApprovalContextV1> ToolApprovalReady(ToolDispositionApprovalOwnerV1 owner,Guid id)
    {var c=await owner.ContextAsync(id);await owner.PolicyAsync(id,c.Etag,ToolPolicy(c));return await owner.ContextAsync(id);}
    private static ToolDispositionSubmitV1 ToolSubmit(ToolDispositionApprovalContextV1 c)=>new(Guid.NewGuid(),c.Disposition.Request!.Reference,
        c.Policy!.Reference,c.Disposition.Obligations.Version,"报废申请原文 e\u0301");

    [CurrentPermissionPostgresFact]
    public async Task Tool_approval_freezes_actual_request_policy_and_exact_outbox_atomically_without_instance_or_discard()
    {
        await using var h=await Seed();var id=await ToolApprovalSetup(h);var owner=ToolApproval(h);var c=await ToolApprovalReady(owner,id);var cmd=ToolSubmit(c);
        var saved=await owner.SubmitAsync(id,c.Etag,cmd);Assert.Equal("DISPATCH_PENDING",saved.Submission!.State);Assert.False(saved.IsDiscarded);
        Assert.Equal(c.Disposition.Request!.Reference.Id.ToString(),saved.Submission.Subject.SubjectId);
        Assert.Equal(c.Disposition.Request.Reference.Digest,saved.Submission.Subject.ContentDigest.Value);
        Assert.Equal("1",saved.Submission.Subject.BusinessRevision);Assert.Equal(c.Policy!.Reference.Id.ToString(),saved.Submission.Policy.Ref.Id);
        Assert.Equal(8,Convert.FromBase64String(saved.Submission.BusinessSubmittedRowVersion).Length);
        var rows=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_v1.outbox WHERE tenant_id={h.Tenant}").ToArrayAsync();Assert.Single(rows);
        var payload=JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(rows[0],ApprovalBindingOwnerV2.Json)!;
        Assert.Equal(cmd.OperationId,payload.CommandId);Assert.Equal(saved.Submission.SubmissionId,payload.SubmissionId);
        Assert.Equal(h.User,payload.StarterId);Assert.Equal(cmd.Reason,payload.Reason);Assert.Null(payload.Engineering);
        Assert.Equal(ScopeEnvironment.ToString(),payload.Scope.EnvironmentId);Assert.Equal(h.Tenant,payload.Scope.TenantId);
        var snapshot=Convert.FromBase64String(payload.Snapshot.CanonicalBytesBase64);Assert.Equal(ApprovalCanonicalV2.Hash(snapshot),payload.Snapshot.Digest.Value);
        var body=JsonDocument.Parse(snapshot).RootElement;Assert.Equal(4,body.GetProperty("obligations").GetProperty("unknownOwners").GetArrayLength());
        Assert.False(body.GetProperty("obligations").GetProperty("canFinalize").GetBoolean());
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.NotEqual(3,(await ToolInstances(h).ReadAsync(id)).Source.GetProperty("status").GetInt32());
        Assert.Equal(saved,await owner.SubmitAsync(id,c.Etag,cmd));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_approval_v1.outbox"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_tool_approval_v1.submissions CASCADE"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_approval_lost_commit_response_recovers_original_with_read_only_after_policy_and_submit_revocation()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var id=await ToolApprovalSetup(h);var owner=ToolApproval(h);
        var c=await ToolApprovalReady(owner,id);var cmd=ToolSubmit(c);fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,c.Etag,cmd))).Message);
        var saved=(await owner.OperationAsync(cmd.OperationId,false)).Result!;
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&(x.ActionCode=="dispose-submit"||x.ActionCode=="dispose-policy")).ExecuteDeleteAsync();
        Assert.Equal(saved,await owner.SubmitAsync(id,c.Etag,cmd));
        Assert.Equal("UNAVAILABLE",(await owner.ContextAsync(id)).PolicyKnowledge);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,c.Etag,cmd with{Reason="changed"}))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(cmd.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_approval_new_http_keys_keep_original_intent_and_cannot_change_starter_snapshot_or_policy()
    {
        await using var h=await Seed();var id=await ToolApprovalSetup(h);var owner=ToolApproval(h);var c=await ToolApprovalReady(owner,id);var cmd=ToolSubmit(c);
        var first=await owner.SubmitAsync(id,c.Etag,cmd);var second=await owner.SubmitAsync(id,c.Etag,cmd with{OperationId=Guid.NewGuid(),Reason="另一次传输说明"});
        Assert.Equal(first.Submission,second.Submission);Assert.NotEqual(first.OperationId,second.OperationId);
        Assert.Single(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_tool_approval_v1.outbox WHERE tenant_id={h.Tenant}").ToArrayAsync());
        var peer=await ActualReviewer(h);await EngineeringAction(h,"wms-plate-mold","dispose-submit");
        // The peer's actual session cannot disclose or adopt the original actor's transport command.
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ToolApproval(peer).OperationAsync(cmd.OperationId,false))).Status);
        var current=await owner.ContextAsync(id);await owner.PolicyAsync(id,current.Etag,ToolPolicy(current));current=await owner.ContextAsync(id);
        Assert.Equal("SUBJECT_ALREADY_SUBMITTED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,current.Etag,ToolSubmit(current)))).Message);
        Assert.Equal(first,await owner.SubmitAsync(id,c.Etag,cmd));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_approval_current_policy_required_and_invalid_or_stale_inputs_leave_no_intent()
    {
        await using var h=await Seed();var id=await ToolApprovalSetup(h);var owner=ToolApproval(h);var c=await owner.ContextAsync(id);
        Assert.Equal("MISSING",c.PolicyKnowledge);var policy=ToolPolicy(c);
        Assert.Equal("TOOL_APPROVAL_POLICY_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.PolicyAsync(id,c.Etag,policy with{RequiredHumanApprovals=0}))).Message);
        Assert.Equal(428,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.PolicyAsync(id,null,policy))).Status);
        await owner.PolicyAsync(id,c.Etag,policy);c=await owner.ContextAsync(id);var cmd=ToolSubmit(c);
        Assert.Equal("TOOL_APPROVAL_INPUT_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,c.Etag,cmd with{Reason="invalid\uD800"}))).Message);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,"\"stale\"",cmd))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,c.Etag,cmd with{ExpectedObligationVersion="wrong"}))).Status);
        await owner.PolicyAsync(id,c.Etag,policy with{OperationId=Guid.NewGuid(),State="REVOKED"});var revoked=await owner.ContextAsync(id);
        Assert.Equal("POLICY_UNRESOLVED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,revoked.Etag,ToolSubmit(revoked)))).Message);
        Assert.Null(revoked.Submission);Assert.Equal("UNKNOWN",(await owner.OperationAsync(cmd.OperationId,false)).State);
        var key=Guid.NewGuid();Assert.Equal("CLOSED",(await owner.OperationAsync(key,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,c.Etag,cmd with{OperationId=key}))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_approval_native_change_at_final_gate_rolls_back_submission_outbox_command_together()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var id=await ToolApprovalSetup(h);var owner=ToolApproval(h);var c=await ToolApprovalReady(owner,id);var cmd=ToolSubmit(c);
        hook.Before=async command=>{
            if(!command.CommandText.Contains("SELECT body_json AS"))return;
            await using var probe=command.Connection!.CreateCommand();probe.Transaction=command.Transaction;
            probe.CommandText="SELECT EXISTS(SELECT FROM cp6_tool_approval_v1.outbox WHERE tenant_id=@tenant)";probe.Parameters.Add(new Npgsql.NpgsqlParameter("tenant",h.Tenant));
            if(!(bool)(await probe.ExecuteScalarAsync())!)return;hook.Before=null;
            await using var change=command.Connection.CreateCommand();change.Transaction=command.Transaction;
            change.CommandText="UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=19 WHERE \"Id\"=@id";change.Parameters.Add(new Npgsql.NpgsqlParameter("id",id));await change.ExecuteNonQueryAsync();
        };
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,c.Etag,cmd))).Status);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_tool_approval_v1.outbox WHERE tenant_id={h.Tenant}").ToArrayAsync());
        Assert.Null((await owner.ContextAsync(id)).Submission);Assert.Equal("UNKNOWN",(await owner.OperationAsync(cmd.OperationId,false)).State);
        Assert.Equal(0,(await ToolInstances(h).ReadAsync(id)).Source.GetProperty("usedShots").GetInt32());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_approval_two_connections_resolve_different_transport_keys_to_one_durable_intent()
    {
        await using var h=await Seed();var id=await ToolApprovalSetup(h);var owner=ToolApproval(h);var c=await ToolApprovalReady(owner,id);var one=ToolSubmit(c);
        await using var other=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peer=ToolApproval(h with{Db=other});
        var results=await Task.WhenAll(owner.SubmitAsync(id,c.Etag,one),peer.SubmitAsync(id,c.Etag,one with{OperationId=Guid.NewGuid()}));
        Assert.Equal(results[0].Submission,results[1].Submission);
        Assert.Single(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_tool_approval_v1.outbox WHERE tenant_id={h.Tenant}").ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_approval_authority_revocation_after_outbox_insert_rolls_back_all_local_effects()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var id=await ToolApprovalSetup(h);var owner=ToolApproval(h);var c=await ToolApprovalReady(owner,id);var cmd=ToolSubmit(c);
        hook.Before=async command=>{
            if(!command.CommandText.Contains("cp6_authentication_v1.acquire()"))return;
            await using var probe=command.Connection!.CreateCommand();probe.Transaction=command.Transaction;
            probe.CommandText="SELECT EXISTS(SELECT FROM cp6_tool_approval_v1.outbox WHERE tenant_id=@tenant)";probe.Parameters.Add(new Npgsql.NpgsqlParameter("tenant",h.Tenant));
            if(!(bool)(await probe.ExecuteScalarAsync())!)return;hook.Before=null;
            await using var revoke=command.Connection.CreateCommand();revoke.Transaction=command.Transaction;
            revoke.CommandText="DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"MenuId\"=443 AND \"ActionCode\"='dispose-submit'";revoke.Parameters.Add(new Npgsql.NpgsqlParameter("tenant",h.Tenant));await revoke.ExecuteNonQueryAsync();
        };
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,c.Etag,cmd));Assert.True(error.Status is 403 or 409,error.Status+":"+error.ToString());
        Assert.Null((await owner.ContextAsync(id)).Submission);Assert.Equal("UNKNOWN",(await owner.OperationAsync(cmd.OperationId,false)).State);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_tool_approval_v1.outbox WHERE tenant_id={h.Tenant}").ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_approval_actual_http_login_strict_fields_etag_and_durable_201_pending()
    {
        await using var h=await Seed();var id=await ToolApprovalSetup(h);var owner=ToolApproval(h);var c=await owner.ContextAsync(id);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString()});
        var cookies=await Login(host,h);var path=$"/api/wms/specials/v1/tools/{id}/approval-submissions";
        using var policyRequest=OperationsRequest(cookies,HttpMethod.Post,$"/api/wms/specials/v1/tools/{id}/disposition-policies");
        policyRequest.Headers.TryAddWithoutValidation("If-Match",c.Etag);policyRequest.Content=JsonContent.Create(ToolPolicy(c));
        using var policyResponse=await host.Send(policyRequest);Assert.True(policyResponse.StatusCode==HttpStatusCode.Created,await policyResponse.Content.ReadAsStringAsync());
        using var contextResponse=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/wms/specials/v1/tools/{id}/approval-context"));
        c=JsonDocument.Parse(await contextResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Deserialize<ToolDispositionApprovalContextV1>(ApprovalBindingOwnerV2.Json)!;
        Assert.Equal("CURRENT",c.PolicyKnowledge);var cmd=ToolSubmit(c);
        using var bad=OperationsRequest(cookies,HttpMethod.Post,path);bad.Headers.TryAddWithoutValidation("If-Match",c.Etag);
        bad.Content=JsonContent.Create(new{cmd.OperationId,cmd.Request,cmd.Policy,cmd.ExpectedObligationVersion,cmd.Reason,starterId=Guid.NewGuid()});
        using var rejected=await host.Send(bad);Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        using var get=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/wms/specials/v1/tools/{id}/approval-context"));Assert.Equal(HttpStatusCode.OK,get.StatusCode);Assert.True(get.Headers.CacheControl!.NoStore);
        using var post=OperationsRequest(cookies,HttpMethod.Post,path);post.Headers.TryAddWithoutValidation("If-Match",c.Etag);post.Content=JsonContent.Create(cmd);
        using var response=await host.Send(post);Assert.True(response.StatusCode==HttpStatusCode.Created,response.StatusCode+":"+await response.Content.ReadAsStringAsync());
        var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal("DISPATCH_PENDING",result.GetProperty("submission").GetProperty("state").GetString());Assert.False(result.GetProperty("isDiscarded").GetBoolean());
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }
}
