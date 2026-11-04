using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task ToolWithdrawal_actual_HTTP_preserves_physical_block_and_original_request_and_late_approval_is_stale()
    {
        await using var h=await Seed();var f=await FactSetup(h);var tool=await ApplicationTool(h);
        await EngineeringAction(h,"wms-plate-mold","dispose-withdraw");
        var before=await Disposition(h).ContextAsync(tool);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString()});
        var cookies=await Login(host,h);var root=$"/api/wms/specials/v1/tools/{tool}/approval-submissions/{f.Decision.SubmissionId}";
        using var contextResponse=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/withdraw-context"));
        Assert.Equal(HttpStatusCode.OK,contextResponse.StatusCode);
        var context=await DocumentHttpData<JsonElement>(contextResponse);Assert.True(context.GetProperty("canWithdraw").GetBoolean());
        var version=context.GetProperty("controlRowVersion").GetString();var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw approval without releasing physical obligations");
        using(var forged=OperationsRequest(cookies,HttpMethod.Post,root+"/withdraw",new{commandId=Guid.NewGuid(),reason="Cannot select actual actor",actor=new{actualHandlerId=Guid.NewGuid()}}))
        {forged.Headers.TryAddWithoutValidation("If-Match","\""+version+"\"");using var denied=await host.Send(forged);Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
        using var request=OperationsRequest(cookies,HttpMethod.Post,root+"/withdraw",command);request.Headers.TryAddWithoutValidation("If-Match","\""+version+"\"");
        using var response=await host.Send(request);var result=await DocumentHttpData<JsonElement>(response);
        Assert.Equal("WITHDRAWN",result.GetProperty("state").GetString());
        var fact=result.GetProperty("fact").Deserialize<ApprovalWithdrawalFactV2>(ApprovalBindingOwnerV2.Json)!;
        Assert.Equal(f.Decision.Subject,fact.Subject);Assert.Equal(h.User,fact.Actor.ActualHandlerId);
        Assert.Equal(version,fact.BusinessBeforeRowVersion);Assert.NotEqual(version,fact.BusinessAfterRowVersion);
        FactAck(await f.Owner.ReceiveAsync(f.Payload));var applied=await ApplicationAttempt(h,tool);
        Assert.Equal("STALE",applied.Receipt.Application);Assert.Equal("STALE_RECEIPT",applied.Receipt.OutcomeCode);
        var after=await Disposition(h).ContextAsync(tool);
        Assert.Equal(before.Etag,after.Etag);Assert.Equal(ApprovalCanonicalV2.Bytes(before.Request!),ApprovalCanonicalV2.Bytes(after.Request!));
        Assert.Equal(ApprovalCanonicalV2.Bytes(before.Block!),ApprovalCanonicalV2.Bytes(after.Block!));
        Assert.False(after.IsDiscarded);Assert.Equal("BLOCKED_PENDING_DISPOSITION",after.State);Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="dispose-withdraw").ExecuteDeleteAsync();
        using var replayRequest=OperationsRequest(cookies,HttpMethod.Post,root+"/withdraw",command);replayRequest.Headers.TryAddWithoutValidation("If-Match","\""+version+"\"");
        using var replay=await host.Send(replayRequest);Assert.Equal(result.GetRawText(),(await DocumentHttpData<JsonElement>(replay)).GetRawText());
        Assert.Equal("COMMITTED",(await ToolApproval(h).WithdrawalOperationAsync(command.CommandId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task ToolWithdrawal_outbox_failure_rolls_back_control_and_two_connections_preserve_physical_obligations()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await FactSetup(h);var tool=await ApplicationTool(h);
        await EngineeringAction(h,"wms-plate-mold","dispose-withdraw");FactAck(await f.Owner.ReceiveAsync(f.Payload));
        var owner=ToolApproval(h);var c=await owner.WithdrawalContextAsync(tool,f.Decision.SubmissionId);
        var app=Applications(h);var ac=await app.ContextAsync(tool);var input=new ToolApplicationCommandV2(Guid.NewGuid(),ac.ReceiptStreamId!.Value,null,"Observe actual application obligations");
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw competing original approval");
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_tool_approval_control_v1.withdraw_outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>owner.WithdrawAsync(tool,f.Decision.SubmissionId,"\""+c.ControlRowVersion+"\"",command)));hook.Before=null;
        Assert.Equal("UNKNOWN",(await owner.WithdrawalOperationAsync(command.CommandId,false)).State);
        Assert.Equal(c.ControlRowVersion,(await owner.WithdrawalContextAsync(tool,f.Decision.SubmissionId)).ControlRowVersion);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=ToolApproval(h with{Db=other});
        ToolApplicationResultV2? applied=null;ToolWithdrawalResultV1? withdrawn=null;
        var errors=await Task.WhenAll(Record.ExceptionAsync(async()=>{applied=await app.ApplyAsync(tool,ac.Etag,input);}),
            Record.ExceptionAsync(async()=>{withdrawn=await peer.WithdrawAsync(tool,f.Decision.SubmissionId,"\""+c.ControlRowVersion+"\"",command);}));
        Assert.Null(errors[1]);Assert.Equal("WITHDRAWN",withdrawn!.State);
        if(applied!=null)Assert.Equal("BLOCKED",applied.Receipt.Application);
        else Assert.Equal("APPLICATION_CONTEXT_CHANGED",Assert.IsType<OrderPriceCorrectionException>(errors[0]).Message);
        var fresh=await app.ContextAsync(tool);
        Assert.Equal("STALE",(await app.ApplyAsync(tool,fresh.Etag,new(Guid.NewGuid(),fresh.ReceiptStreamId!.Value,fresh.Receipt?.ReceiptId,"Observe withdrawal at current control"))).Receipt.Application);
        Assert.False((await Disposition(h).ContextAsync(tool)).IsDiscarded);Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ToolWithdrawal_pending_intake_BLOCKED_successor_preserves_single_original_submission()
    {
        await using var h=await Seed();var f=await IntakeSetup(h,extraActions:["dispose-withdraw"]);
        var tool=await ApplicationTool(h);var submission=IntakeBody(f).SubmissionId;
        var owner=ToolApproval(h);var c=await owner.WithdrawalContextAsync(tool,submission);
        var result=await owner.WithdrawAsync(tool,submission,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"Withdraw before intake accepts original"));
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}) AS \"Value\"").SingleAsync());
        await f.Owner.SubmitAsync(f.Payload);var fixture=new ToolWithdrawalFixture(f,tool,submission);var service=await GrantToolWithdrawRelay(h,fixture);
        var workflow=ToolWithdrawals(h,fixture,service);var current=await workflow.ContextAsync(submission);
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var blocked=await workflow.WithdrawAsync(submission,"\""+current.SubmissionRowVersion+"\"",bytes);
        Assert.Equal("BLOCKED",blocked.Ack.State);Assert.Equal("WORKFLOW_NOT_STARTED",blocked.Ack.ErrorCode);
        c=await owner.WithdrawalContextAsync(tool,submission);
        var next=await owner.ReauthorizeWithdrawalAsync(tool,submission,result.TransportCommandId!.Value,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"New explicit authorization after pending intake"));
        await Starter(h).RunAsync(h.Tenant);current=await workflow.ContextAsync(submission);
        var obsolete=await workflow.WithdrawAsync(submission,"\""+current.SubmissionRowVersion+"\"",bytes);
        Assert.Equal("BLOCKED",obsolete.Ack.State);Assert.Equal("TOOL_WITHDRAWAL_SUPERSEDED",obsolete.Ack.ErrorCode);
        var nextBytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdraw_outbox WHERE command_id={next.TransportCommandId}").SingleAsync();
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(submission,"\""+current.SubmissionRowVersion+"\"",nextBytes)).Ack.State);
        Assert.Equal(result,(await owner.WithdrawalOperationAsync(result.OperationId,false)).Result);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_v1.submissions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ToolWithdrawal_current_read_scope_and_original_commit_recovery_do_not_require_content_write()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await ToolWithdrawalSetup(h);var owner=ToolApproval(h);
        var c=await owner.WithdrawalContextAsync(f.Tool,f.Submission);Assert.False(c.CanWithdraw);
        var input=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw with current independent action and source read");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawAsync(f.Tool,f.Submission,"\""+c.ControlRowVersion+"\"",input))).Status);
        await EngineeringAction(h,"wms-plate-mold","dispose-withdraw");
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-plate-mold",FieldName="Remarks",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawalContextAsync(f.Tool,f.Submission))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="wms-plate-mold"&&x.FieldName=="Remarks").ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Access,2));
        c=await owner.WithdrawalContextAsync(f.Tool,f.Submission);Assert.True(c.CanWithdraw);
        await using var foreign=await Seed();await MaintenanceSetup(foreign);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ToolApproval(foreign).WithdrawalContextAsync(f.Tool,f.Submission))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawalContextAsync(f.Tool,Guid.NewGuid()))).Status);
        fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawAsync(f.Tool,f.Submission,"\""+c.ControlRowVersion+"\"",input))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="dispose-withdraw").ExecuteDeleteAsync();
        var recovered=await owner.WithdrawalOperationAsync(input.CommandId,false);Assert.Equal("COMMITTED",recovered.State);
        Assert.Equal(recovered.Result,await owner.WithdrawAsync(f.Tool,f.Submission,"\""+c.ControlRowVersion+"\"",input));
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawAsync(f.Tool,f.Submission,"\""+c.ControlRowVersion+"\"",input with{Reason="Different original input"}))).Status);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="wms-plate-mold"&&x.FieldName=="Remarks").ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Access,3));
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawalOperationAsync(input.CommandId,false))).Message);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}
